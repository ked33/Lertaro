using System.Windows.Automation;
using Lertaro.PluginSdk.Helpers;
using Native = Lertaro.Core.Hook.ExplorerNativeHooks;
using PointNative = Lertaro.App.Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods;

namespace Lertaro.App.Services.ShellMenu.QuickNav;

internal static class ExplorerHoveredFolder
{
    internal static async Task<string?> CaptureAsync(IntPtr hwnd, int x, int y)
    {
        // Capture the native file-list identity now; the menu stays closed until this probe finishes.
        var under = PointNative.WindowFromPoint(new PointNative.POINT { x = x, y = y });
        if (hwnd == IntPtr.Zero || Native.GetAncestor(under, 2 /* GA_ROOT */) != hwnd) return null;
        var tab = ExplorerFolderPathReader.GetActiveTab(hwnd);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var token = timeout.Token;
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ShellThread.Run("QuickNavigationHover", () => completion.TrySetResult(Read(hwnd, under, tab, x, y, token)));
        // Bound the hit-test wait before the root menu is shown; a stalled provider must not freeze the UI.
        try { return await completion.Task.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; }
    }

    private static string? Read(IntPtr hwnd, IntPtr fileList, IntPtr tab, int x, int y, CancellationToken token)
    {
        try
        {
            var point = new System.Windows.Point(x, y);
            if (Native.GetAncestor(fileList, 2 /* GA_ROOT */) != hwnd || tab != ExplorerFolderPathReader.GetActiveTab(hwnd)) return null;
            token.ThrowIfCancellationRequested();
            var list = AutomationElement.FromHandle(fileList);
            var hit = AutomationElement.FromPoint(point);
            AutomationElement? item = null;
            // A hit can be a text/icon child. Walk only its ancestors, and require the captured
            // file list as ancestor so another window or an Explorer sidebar cannot supply a hit.
            // ponytail: cap malformed/deep provider trees at 32; omit the optional entry beyond that.
            for (var depth = 0; hit != null && depth < 32; depth++)
            {
                token.ThrowIfCancellationRequested();
                if (Automation.Compare(hit, list))
                {
                    if (item == null) return null;
                    var current = item.Current;
                    if (current.IsOffscreen || !current.BoundingRectangle.Contains(point)
                        || string.IsNullOrWhiteSpace(current.Name)) return null;
                    var path = ExplorerFolderPathReader.Read(hwnd, tab, current.Name, token);
                    token.ThrowIfCancellationRequested();
                    // Switching tabs, scrolling, or navigating while COM reads invalidates the hit.
                    var after = item.Current;
                    return tab == ExplorerFolderPathReader.GetActiveTab(hwnd)
                        && after.Name == current.Name && !after.IsOffscreen && after.BoundingRectangle.Contains(point)
                        ? path : null;
                }
                var type = hit.Current.ControlType;
                if (item == null && (type == ControlType.ListItem || type == ControlType.DataItem)) item = hit;
                hit = TreeWalker.RawViewWalker.GetParent(hit);
            }
        }
        // Explorer may close or recycle a UIA element between any two calls; omit only the optional entry.
        catch { }
        return null;
    }
}
