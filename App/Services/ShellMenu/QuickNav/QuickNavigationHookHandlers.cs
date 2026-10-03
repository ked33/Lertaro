using System.Windows.Threading;
using Lertaro.App.Helpers.App;
using Lertaro.App.Views.InlineSearchWindow.Helpers;
using Lertaro.Core;
using Lertaro.Core.Hook;
using Lertaro.Core.Hook.Ipc;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.App.Services.ShellMenu.QuickNav;

// Split out from App.xaml.cs to keep that file under the repo's per-file line limit. These handlers
// move the Quick Navigation gate evaluation off the hook-event thread onto ShellThread (STA), then
// marshal only the resulting "show menu" call back to the WPF Dispatcher.
internal static class QuickNavigationHookHandlers
{
    // A newer middle click supersedes one still waiting on Explorer's activation, so the older one
    // cannot open the menu at coordinates the user has already left.
    private static int _middleClickGeneration;

    public static void AttachTo(HookIpcClient hookClient, Dispatcher dispatcher)
    {
        hookClient.OnMouseDoubleClick += (x, y) => HandleDoubleClick(dispatcher, x, y);
        hookClient.OnMouseMiddleClick += (x, y) => HandleMiddleClick(dispatcher, x, y);
    }

    private static void HandleDoubleClick(Dispatcher dispatcher, int x, int y)
    {
        if (!UserSettings.Load().Hotkeys.QuickNavTriggerOnDoubleClick) return;
        if (InlineSearchWindowNativeMethods.IsPointInsideWindow(x, y)) return;
        var trk = InlineSearchManager.Instance.ExplorerTracker;
        var proc = AppNativeHelper.GetProcessNameOfWindow(trk.ActiveHwnd);
        var cls = AppNativeHelper.GetClassNameOfWindow(trk.ActiveHwnd);
        var hwnd = trk.ActiveHwnd;
        var isDesktop = trk.IsDesktop;
        ShellThread.Run("QuickNavigationGate", () =>
        {
            if (QuickNavigationTriggerGate.CanShow(hwnd, proc, cls, isDesktop, x, y, MouseTriggerType.DoubleClick))
                dispatcher.BeginInvoke(() => QuickNavigationMenu.Show(x, y));
        });
    }

    private static void HandleMiddleClick(Dispatcher dispatcher, int x, int y)
    {
        if (!UserSettings.Load().Hotkeys.QuickNavTriggerOnMiddleClick) return;
        if (InlineSearchWindowNativeMethods.IsPointInsideWindow(x, y)) return;
        var generation = Interlocked.Increment(ref _middleClickGeneration);
        // The wait lives here, on a shell worker, never in the hook callback: a low-level hook that
        // pauses delays the middle click for every program on the desktop.
        ShellThread.Run("QuickNavigationGate", () =>
        {
            if (generation != Volatile.Read(ref _middleClickGeneration)) return;
            var host = ResolveMiddleClickHost(x, y);
            if (host == null || generation != Volatile.Read(ref _middleClickGeneration)) return;
            if (QuickNavigationTriggerGate.CanShow(host.Value.Hwnd, host.Value.Process, host.Value.ClassName, host.Value.IsDesktop, x, y, MouseTriggerType.MiddleClick)
                || FileDialogQuickNavGate.CanShow(host.Value.Hwnd, host.Value.Process, host.Value.ClassName, x, y))
            {
                var hoveredFolder = !host.Value.IsDesktop && host.Value.Process.Equals("explorer", StringComparison.OrdinalIgnoreCase)
                    ? ExplorerHoveredFolder.CaptureAsync(host.Value.Hwnd, x, y) : null;
                dispatcher.BeginInvoke(() =>
                {
                    if (generation != Volatile.Read(ref _middleClickGeneration)) return;
                    QuickNavigationMenu.Show(x, y, hoveredFolder);
                });
            }
        });
    }

    private static QuickNavMiddleClickActivation.Host? ResolveMiddleClickHost(int x, int y)
    {
        var before = ReadHost();
        var under = InlineSearchWindowNativeMethods.WindowFromPoint(new InlineSearchWindowNativeMethods.POINT { x = x, y = y });
        var root = under == IntPtr.Zero ? IntPtr.Zero : ExplorerNativeHooks.GetAncestor(under, ExplorerNativeHooks.GA_ROOTOWNER);
        if (root == IntPtr.Zero) root = under;
        var click = new QuickNavMiddleClickActivation.ClickTarget(
            root,
            AppNativeHelper.GetProcessNameOfWindow(root),
            AppNativeHelper.GetClassNameOfWindow(under));
        return QuickNavMiddleClickActivation.Resolve(before, click, ReadHost, Thread.Sleep);
    }

    private static QuickNavMiddleClickActivation.Host ReadHost()
    {
        var tracker = InlineSearchManager.Instance.ExplorerTracker;
        var hwnd = tracker.ActiveHwnd;
        return new QuickNavMiddleClickActivation.Host(
            hwnd,
            AppNativeHelper.GetProcessNameOfWindow(hwnd),
            AppNativeHelper.GetClassNameOfWindow(hwnd),
            tracker.IsDesktop,
            tracker.ActivePath);
    }
}
