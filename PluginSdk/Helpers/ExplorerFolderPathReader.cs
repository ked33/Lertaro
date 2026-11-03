using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.PluginSdk.Helpers;

/// <summary>Reads one Explorer tab through Shell COM, with an address-bar fallback for its directory.
/// Call only on an isolated STA worker.</summary>
public static class ExplorerFolderPathReader
{
    public static IntPtr GetActiveTab(IntPtr window)
    {
        var tab = IntPtr.Zero;
        EnumChildWindows(window, (child, _) =>
        {
            var name = new StringBuilder(128);
            GetClassName(child, name, name.Capacity);
            if (name.ToString() != "ShellTabWindowClass" || !IsWindowVisible(child)) return true;
            tab = child;
            return false;
        }, IntPtr.Zero);
        return tab;
    }

    public static string? Read(IntPtr target, IntPtr activeTab) => Read(target, activeTab, null);

    /// <summary>Resolves an exact displayed item name in the specified tab; ambiguous names are rejected.</summary>
    public static string? Read(IntPtr target, IntPtr activeTab, string? itemName) => Read(target, activeTab, itemName, default);

    public static string? Read(IntPtr target, IntPtr activeTab, string? itemName, CancellationToken cancellationToken)
    {
        if (target == IntPtr.Zero || !IsWindow(target) || cancellationToken.IsCancellationRequested || GetActiveTab(target) != activeTab)
            return null;
        var path = ReadShell(target, activeTab, itemName, cancellationToken);
        // An address bar identifies the current directory, never a hovered item. Do not turn a failed
        // item lookup into its parent, or reuse an old directory when Explorer has no COM registration.
        if (path == null && itemName == null && !cancellationToken.IsCancellationRequested)
            path = ReadAddressBar(target);
        return !cancellationToken.IsCancellationRequested && IsWindow(target) && GetActiveTab(target) == activeTab ? path : null;
    }

    private static string? ReadShell(IntPtr target, IntPtr activeTab, string? itemName, CancellationToken cancellationToken)
    {
        object? windows = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
            if (type == null || (windows = Activator.CreateInstance(type)) == null) return null;
            var count = (int)((dynamic)windows).Count;
            for (var i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? window = null, document = null, folder = null, item = null;
                try
                {
                    window = ((dynamic)windows).Item(i);
                    if (window == null || (IntPtr)((dynamic)window).HWND != target) continue;
                    if (activeTab != IntPtr.Zero && !MatchesTab(window, activeTab)) continue;
                    document = ((dynamic)window).Document;
                    folder = ((dynamic)document!).Folder;
                    if (itemName != null)
                        return ResolveUniqueFolder(ReadMatchingItems(folder!, itemName, cancellationToken));
                    item = ((dynamic)folder!).Self;
                    string? path = ((dynamic)item!).Path;
                    if (!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
                        && !UserPathResolver.IsVirtualPath(path) && !path.Contains("::{", StringComparison.Ordinal))
                        return path;
                }
                // Explorer can close a tab between any two automation calls. Treat it as no sample.
                catch { }
                finally { Release(item); Release(folder); Release(document); Release(window); }
            }
        }
        catch { }
        finally { Release(windows); }
        return null;
    }

    private static string? ReadAddressBar(IntPtr target)
    {
        if (!IsWindowVisible(target) || ClassName(target) != "CabinetWClass") return null;
        var toolbar = IntPtr.Zero;
        var ambiguous = false;
        // Explorer can keep its address bar outside ShellTabWindowClass, in the shared window frame.
        // Read only one visible breadcrumb toolbar, then let Read recheck the active tab after WM_GETTEXT.
        EnumChildWindows(target, (child, _) =>
        {
            if (!IsWindowVisible(child) || ClassName(child) != "ToolbarWindow32"
                || ClassName(GetParent(child)) != "Breadcrumb Parent") return true;
            if (toolbar != IntPtr.Zero) { ambiguous = true; return false; }
            toolbar = child;
            return true;
        }, IntPtr.Zero);
        if (toolbar == IntPtr.Zero || ambiguous) return null;
        var text = new StringBuilder(32768);
        // SMTO_BLOCK | SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT: do not pump unrelated callbacks or wait
        // indefinitely for Explorer. A truncated path must never become a different navigation target.
        if (SendMessageTimeout(toolbar, 0x000D /* WM_GETTEXT */, (IntPtr)text.Capacity, text,
                0x0023, 200, out var length) == IntPtr.Zero || length.ToInt64() >= text.Capacity - 1)
            return null;
        return ParseAddressBarPath(text.ToString());
    }

    internal static string? ParseAddressBarPath(string text)
    {
        var path = text.Trim();
        if (UserPathResolver.IsVirtualPath(path)) return null;
        if (!Path.IsPathFullyQualified(path))
        {
            var colon = path.IndexOfAny([':', '：']);
            if (colon < 0) return null;
            path = path[(colon + 1)..].Trim();
        }
        // ponytail: accept only explicit filesystem paths. Localized names like Downloads/Home are
        // ambiguous; resolving those would need Shell item identity, not a guessed special folder.
        return Path.IsPathFullyQualified(path) && !UserPathResolver.IsVirtualPath(path)
            && !path.Contains("::{", StringComparison.Ordinal) ? path : null;
    }

    private static string ClassName(IntPtr hwnd)
    {
        var name = new StringBuilder(128);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString();
    }

    private static IEnumerable<(string Path, bool IsFolder)> ReadMatchingItems(object folder, string name, CancellationToken cancellationToken)
    {
        object? items = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            items = ((dynamic)folder).Items();
            var count = (int)((dynamic)items!).Count;
            var timer = Stopwatch.StartNew();
            // ponytail: a bounded display-name scan avoids relying on undocumented UIA item indexes.
            // For very large views, omit the optional hover entry; use native item identity if needed later.
            for (var i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (timer.ElapsedMilliseconds > 250) throw new TimeoutException();
                object? item = null;
                try
                {
                    item = ((dynamic)items!).Item(i);
                    // Column zero matches Explorer's displayed name, including localized folder names
                    // and hidden file extensions. Inspect files too, to reject file/folder name collisions.
                    var displayName = (string)((dynamic)folder).GetDetailsOf(item, 0);
                    if (string.Equals(displayName, name, StringComparison.Ordinal))
                        yield return ((string)((dynamic)item!).Path, (bool)((dynamic)item!).IsFolder);
                }
                finally { Release(item); }
            }
        }
        finally { Release(items); }
    }

    internal static string? ResolveUniqueFolder(IEnumerable<(string Path, bool IsFolder)> matches,
        Func<string, bool>? directoryExists = null)
    {
        using var candidates = matches.GetEnumerator();
        // A matching file can never resolve to a unique folder, even if more names follow.
        // Stop immediately instead of scanning the rest of a large Explorer view for that case.
        if (!candidates.MoveNext() || !candidates.Current.IsFolder) return null;
        var path = candidates.Current.Path;
        if (candidates.MoveNext()) return null;
        return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
            && !UserPathResolver.IsVirtualPath(path) && !path.Contains("::{", StringComparison.Ordinal)
            && (directoryExists ?? Directory.Exists)(path) ? path : null;
    }

    private static bool MatchesTab(object window, IntPtr activeTab)
    {
        if (window is not IComServiceProvider provider) return false;
        var service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        var iid = typeof(IShellBrowser).GUID;
        var result = provider.QueryService(ref service, ref iid, out var pointer);
        if (pointer == IntPtr.Zero) return false;
        object? browser = null;
        try
        {
            if (result != 0) return false;
            browser = Marshal.GetObjectForIUnknown(pointer);
            return ((IShellBrowser)browser).GetWindow(out var tab) == 0 && tab == activeTab;
        }
        finally { Release(browser); Marshal.Release(pointer); }
    }

    private static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value))
        {
            try { Marshal.ReleaseComObject(value); }
            catch (InvalidComObjectException) { }
        }
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    private interface IComServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out IntPtr result);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E2-0000-0000-C000-000000000046")]
    private interface IShellBrowser
    {
        [PreserveSig] int GetWindow(out IntPtr hwnd);
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int length);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, StringBuilder text,
        uint flags, uint timeout, out IntPtr result);
}
