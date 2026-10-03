using System.Windows.Automation;
using Lertaro.PluginSdk.Helpers;
using Native = Lertaro.Core.Hook.ExplorerNativeHooks;
using PointNative = Lertaro.App.Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods;

namespace Lertaro.App.Services.ShellMenu.QuickNav;

internal static class ExplorerHoveredFolder
{
    internal static async Task<string?> CaptureAsync(IntPtr hwnd, int x, int y)
    {
        // Capture only native window identity before the popup can cover the click point. UIA later
        // queries this file list directly, rather than FromPoint (which would hit our own menu).
        var under = PointNative.WindowFromPoint(new PointNative.POINT { x = x, y = y });
        if (hwnd == IntPtr.Zero || Native.GetAncestor(under, 2 /* GA_ROOT */) != hwnd) return null;
        var tab = ExplorerFolderPathReader.GetActiveTab(hwnd);
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ShellThread.Run("QuickNavigationHover", () => completion.TrySetResult(Read(hwnd, under, tab, x, y)));
        // Accessibility providers and shell extensions can stall. The optional item must not hold up the menu.
        try { return await completion.Task.WaitAsync(TimeSpan.FromMilliseconds(750)).ConfigureAwait(false); }
        catch (TimeoutException) { return null; }
    }

    private static string? Read(IntPtr hwnd, IntPtr fileList, IntPtr tab, int x, int y)
    {
        try
        {
            var point = new System.Windows.Point(x, y);
            if (Native.GetAncestor(fileList, 2 /* GA_ROOT */) != hwnd || tab != ExplorerFolderPathReader.GetActiveTab(hwnd)) return null;
            var list = AutomationElement.FromHandle(fileList);
            var candidates = list.FindAll(TreeScope.Descendants, new AndCondition(
                new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem)),
                new PropertyCondition(AutomationElement.IsOffscreenProperty, false)));
            foreach (AutomationElement element in candidates)
            {
                var current = element.Current;
                if (current.IsOffscreen || !current.BoundingRectangle.Contains(point) || string.IsNullOrWhiteSpace(current.Name)) continue;

                var path = ExplorerFolderPathReader.Read(hwnd, tab, current.Name);
                // Switching tabs, scrolling, or navigating while COM is reading invalidates the captured hit.
                var after = element.Current;
                return tab == ExplorerFolderPathReader.GetActiveTab(hwnd)
                    && after.Name == current.Name && !after.IsOffscreen && after.BoundingRectangle.Contains(point)
                    ? path : null;
            }
        }
        // Explorer may close or recycle a UIA element between any two calls; omit only the optional entry.
        catch { }
        return null;
    }
}
