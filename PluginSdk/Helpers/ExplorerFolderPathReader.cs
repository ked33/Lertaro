using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.PluginSdk.Helpers;

/// <summary>Reads one Explorer tab through Shell COM. Call only on an isolated STA worker.</summary>
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

    public static string? Read(IntPtr target, IntPtr activeTab)
    {
        object? windows = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
            if (type == null || (windows = Activator.CreateInstance(type)) == null) return null;
            var count = (int)((dynamic)windows).Count;
            for (var i = 0; i < count; i++)
            {
                object? window = null, document = null, folder = null, item = null;
                try
                {
                    window = ((dynamic)windows).Item(i);
                    if (window == null || (IntPtr)((dynamic)window).HWND != target) continue;
                    if (activeTab != IntPtr.Zero && !MatchesTab(window, activeTab)) continue;
                    document = ((dynamic)window).Document;
                    folder = ((dynamic)document!).Folder;
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
}
