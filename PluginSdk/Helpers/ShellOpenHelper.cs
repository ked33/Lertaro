using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Lertaro.PluginSdk.Helpers;

/// <summary>
/// The one place this app hands "open this folder" and "show me that item" to the Windows shell.
/// </summary>
/// <remarks>
/// Folder opens use the shell association. File-system selection uses Windows Explorer's /select
/// command for compatibility with Explorer's own selection flow. Virtual shell items retain the PIDL route.
/// </remarks>
public static class ShellOpenHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    // Shared by ordinary folder opens and "open containing folder". Call on the shell worker just
    // before the request, since later user input can revoke the foreground permission.
    public static void AllowExplorerForeground()
    {
        // ponytail: target the desktop shell process; separately hosted Explorer windows
        // would need a handoff to their specific process instead.
        var shellWindow = GetShellWindow();
        if (shellWindow != IntPtr.Zero &&
            GetWindowThreadProcessId(shellWindow, out var shellProcessId) != 0 && shellProcessId != 0)
        {
            var allowed = AllowSetForegroundWindow(shellProcessId);
            var error = allowed ? 0 : Marshal.GetLastWin32Error();
            Logger.Log($"[ShellOpenHelper] Explorer foreground handoff: allowed={allowed}, error={error}.", LogLevel.Debug);
        }
    }

    private const int SwShowNormal = 1;

    // ShellExecuteW's documented success test: the returned HINSTANCE is a value greater than 32, and
    // anything at or below it is one of its own error codes.
    private const int ShellExecuteSuccessThreshold = 32;

    // SHParseDisplayName needs the shell's own item id for the target, and it is the reason a virtual
    // token ("shell:AppsFolder", "::{CLSID}") works here at all -- it parses shell names, not file
    // system ones.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr ShellExecuteW(
        IntPtr hwnd,
        string? lpOperation,
        string lpFile,
        string? lpParameters,
        string? lpDirectory,
        int nShowCmd);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[]? apidl, uint dwFlags);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILClone(IntPtr pidl);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ILRemoveLastID(IntPtr pidl);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILFindLastID(IntPtr pidl);

    /// <summary>
    /// Opens <paramref name="folderPath"/> the way a double-click would: whatever the user has registered
    /// for folders (Explorer, or a replacement file manager) decides what happens.
    /// </summary>
    /// <returns><see langword="false"/> when the shell refused the request, or nothing was asked for.</returns>
    public static bool TryOpenFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return false;
        try
        {
            AllowExplorerForeground();
            return (long)ShellExecuteW(IntPtr.Zero, "open", folderPath, null, null, SwShowNormal) > ShellExecuteSuccessThreshold;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Shows the folder holding <paramref name="itemPath"/> with that item selected. A folder passed in
    /// is revealed in its parent the same way a file is, so callers do not need to know which they hold.
    /// </summary>
    /// <returns>Whether Explorer was launched or the shell API accepted the request; selection completes asynchronously.</returns>
    public static bool TryRevealInFolder(string? itemPath)
    {
        if (string.IsNullOrWhiteSpace(itemPath)) return false;
        try
        {
            var path = UserPathResolver.Expand(itemPath);
            if (UserPathResolver.IsVirtualPath(path)) return TryRevealShellItem(path);

            var startInfo = BuildRevealStartInfo(path);
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                Logger.Log($"[ShellOpenHelper] Cannot locate missing or inaccessible item '{path}'.", LogLevel.Error);
                return false;
            }

            AllowExplorerForeground();
            using var process = Process.Start(startInfo);
            if (process != null) return true;
            Logger.Log($"[ShellOpenHelper] Explorer could not be launched to select '{path}'.", LogLevel.Error);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ShellOpenHelper] Locate failed for '{itemPath}': {ex.GetType().Name} (0x{ex.HResult:X8}): {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    // Build only: no process launch, so path handling can be checked without opening Explorer windows.
    internal static ProcessStartInfo BuildRevealStartInfo(string itemPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemPath);
        var path = UserPathResolver.Expand(itemPath);
        if (UserPathResolver.IsVirtualPath(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new ArgumentException("Explorer selection requires a valid file-system path.", nameof(itemPath));

        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            // Explorer parses commas and quotes itself, not with the C runtime argument rules.
            // Always quote the validated path: ArgumentList leaves comma-only paths unquoted.
            // Root backslashes stay literal; ordinary folder trailing separators were trimmed above.
            Arguments = $"/select,\"{path}\"",
            // CreateProcess avoids file associations and guarantees a process handle for the hand-off.
            UseShellExecute = false
        };
    }

    private static bool TryRevealShellItem(string itemPath)
    {
        var pidl = IntPtr.Zero;
        var parentPidl = IntPtr.Zero;
        try
        {
            var parseResult = SHParseDisplayName(itemPath, IntPtr.Zero, out pidl, 0, out _);
            if (parseResult < 0 || pidl == IntPtr.Zero)
            {
                Logger.Log($"[ShellOpenHelper] SHParseDisplayName failed for '{itemPath}': HRESULT=0x{parseResult:X8}.", LogLevel.Error);
                return false;
            }

            // Explicit parent + child works for directories too; do not ask the shell to open the item.
            parentPidl = ILClone(pidl);
            if (parentPidl == IntPtr.Zero || !ILRemoveLastID(parentPidl))
            {
                Logger.Log($"[ShellOpenHelper] Cannot resolve a parent shell item for '{itemPath}'.", LogLevel.Error);
                return false;
            }
            var childPidl = ILFindLastID(pidl);
            if (childPidl == IntPtr.Zero) return false;

            AllowExplorerForeground();
            var selectResult = SHOpenFolderAndSelectItems(parentPidl, 1, new[] { childPidl }, 0);
            if (selectResult < 0)
                Logger.Log($"[ShellOpenHelper] SHOpenFolderAndSelectItems failed for '{itemPath}': HRESULT=0x{selectResult:X8}.", LogLevel.Error);
            return selectResult >= 0;
        }
        finally
        {
            if (parentPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(parentPidl);
            if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
        }
    }
}
