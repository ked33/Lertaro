using System.IO;
using System.Runtime.InteropServices;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.CoreExtensions.Providers.Indexing;

public class ExplorerPathCollector : IActivePathCollector
{
    public string Name => TranslationService.Get("Plugins_ExplorerTargetName");

    public string TargetName => TranslationService.Get("Plugins_ExplorerTargetName");

    public bool CanHandle(string className)
    {
        if (string.IsNullOrEmpty(className)) return false;

        return className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase) ||
               className.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
               className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase);
    }

    public string? TryGetPath(IntPtr activeHwnd, string activeClassName, IntPtr windowHwnd, string windowClassName, string processName)
    {
        if (windowHwnd == IntPtr.Zero) return null;

        // Check if it is the Desktop
        if (IsDesktopWindow(windowHwnd, windowClassName))
        {
            try
            {
                var path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                return IsReportedFilesystemPath(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }

        // Check if it is CabinetWClass (Windows Explorer)
        if (windowClassName.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase))
        {
            return GetActiveExplorerPath(windowHwnd);
        }

        return null;
    }

    /// <summary>
    /// Enumerates every filesystem location currently represented by ShellWindows. On current Windows
    /// versions ShellWindows exposes one entry per Explorer tab; classic Explorer simply contributes one
    /// entry per window.
    /// </summary>
    public IReadOnlyList<OpenedFolder> GetOpenedFolders()
    {
        IReadOnlyList<OpenedFolder> folders = Array.Empty<OpenedFolder>();
        using var done = new ManualResetEventSlim(false);
        var worker = new Thread(() =>
        {
            try { folders = GetOpenedFoldersCore(); }
            finally { done.Set(); }
        })
        {
            IsBackground = true,
            Name = "ExplorerOpenedFoldersSta"
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();

        return done.Wait(2000) ? folders : Array.Empty<OpenedFolder>();
    }

    private static IReadOnlyList<OpenedFolder> GetOpenedFoldersCore()
    {
        var folders = new List<OpenedFolder>();
        try
        {
            var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
            if (shellWindowsType == null || Activator.CreateInstance(shellWindowsType) is not object shellWindows)
                return folders;

            try
            {
                dynamic windows = shellWindows;
                var count = (int)windows.Count;
                for (var i = 0; i < count; i++)
                {
                    object? window = null;
                    try
                    {
                        window = windows.Item(i);
                        if (window == null) continue;

                        dynamic explorer = window;
                        var path = explorer.Document.Folder.Self.Path as string;
                        if (IsReportedFilesystemPath(path))
                            folders.Add(new OpenedFolder(path!, (IntPtr)explorer.HWND));
                    }
                    catch { }
                    finally
                    {
                        if (window != null && Marshal.IsComObject(window))
                            Marshal.ReleaseComObject(window);
                    }
                }
            }
            finally
            {
                if (Marshal.IsComObject(shellWindows))
                    Marshal.ReleaseComObject(shellWindows);
            }
        }
        catch { }

        return folders;
    }

    #region Win32 API and COM Helper
    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    private static bool IsDesktopWindow(IntPtr hwnd, string className)
    {
        if (hwnd == GetShellWindow()) return true;

        if (className.Equals("Progman", StringComparison.OrdinalIgnoreCase))
            return true;

        if (className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase))
        {
            var defView = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
                return true;
        }

        return false;
    }

    private static bool IsReportedFilesystemPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        !UserPathResolver.IsVirtualPath(path) &&
        !path.Contains("::{", StringComparison.Ordinal) &&
        Path.IsPathRooted(path);

    // GetActiveExplorerPathCore talks to Explorer entirely via dynamic COM (Shell.Application,
    // IShellBrowser, Document.Folder.Self.Path) with no built-in timeout -- if Explorer's own thread is
    // stalled (a huge/slow/network folder still enumerating, a misbehaving shell extension, an
    // unhydrated OneDrive placeholder file), those calls can block indefinitely. This method is called
    // synchronously from both the App's WPF UI thread (InlineSearchManager.EnsureWindowCreated) and the
    // Hook process's dedicated WinEvent tracker thread (ExplorerTracker.WinEventProc); either one hanging
    // freezes something load-bearing for the whole process, matching a reported "inline search hangs and
    // never recovers without restarting" bug. Run the actual COM work on its own throwaway STA thread and
    // bound the wait -- if it doesn't finish in time, give up and return null; the worker either finishes
    // harmlessly in the background afterward or leaks, which is far preferable to freezing the caller.
    private static string? GetActiveExplorerPath(IntPtr targetHwnd)
    {
        string? result = null;
        var done = new ManualResetEventSlim(false);
        var worker = new Thread(() =>
        {
            try { result = GetActiveExplorerPathCore(targetHwnd); }
            finally { done.Set(); }
        })
        {
            IsBackground = true,
            Name = "ExplorerPathCollectorSta"
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();

        if (!done.Wait(2000))
        {
            PluginSdk.Logger.Log("[ExplorerPathCollector] Timed out waiting for Explorer's COM response; Explorer may be busy or unresponsive.", PluginSdk.LogLevel.Warn);
            return null;
        }
        return result;
    }

    private static string? GetActiveExplorerPathCore(IntPtr targetHwnd) =>
        ExplorerFolderPathReader.Read(targetHwnd, ExplorerFolderPathReader.GetActiveTab(targetHwnd));
    #endregion
}
