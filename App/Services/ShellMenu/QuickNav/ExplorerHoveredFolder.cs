using System.Windows.Automation;
using Lertaro.PluginSdk.Helpers;
using Native = Lertaro.Core.Hook.ExplorerNativeHooks;
using PointNative = Lertaro.App.Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods;

namespace Lertaro.App.Services.ShellMenu.QuickNav;

internal static class ExplorerHoveredFolder
{
    internal static async Task<string?> CaptureAsync(IntPtr hwnd, int x, int y)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ShellThread.Run("QuickNavigationHover", () => completion.TrySetResult(Read(hwnd, x, y)));
        // Accessibility providers and shell extensions can stall. The optional item must not hold up the menu.
        try { return await completion.Task.WaitAsync(TimeSpan.FromMilliseconds(750)).ConfigureAwait(false); }
        catch (TimeoutException) { return null; }
    }

    private static string? Read(IntPtr hwnd, int x, int y)
    {
        try
        {
            var point = new System.Windows.Point(x, y);
            var under = PointNative.WindowFromPoint(new PointNative.POINT { x = x, y = y });
            if (hwnd == IntPtr.Zero || Native.GetAncestor(under, 2 /* GA_ROOT */) != hwnd) return null;
            var tab = ExplorerFolderPathReader.GetActiveTab(hwnd);
            var element = AutomationElement.FromPoint(point);
            for (var depth = 0; element != null && depth < 16; depth++, element = TreeWalker.RawViewWalker.GetParent(element))
            {
                var current = element.Current;
                if (current.ControlType == ControlType.List) return null;
                if (current.ControlType != ControlType.ListItem && current.ControlType != ControlType.DataItem) continue;
                if (current.IsOffscreen || !current.BoundingRectangle.Contains(point) || string.IsNullOrWhiteSpace(current.Name)) return null;

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
