using System.Text;
using System.Runtime.InteropServices;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Registries;
using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Hook;

internal static class FileDialogNavigationNative
{
    internal static uint ProcessId(IntPtr hwnd)
    {
        ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    private static string ClassName(IntPtr hwnd)
    {
        var name = new StringBuilder(128);
        ExplorerNativeHooks.GetClassName(hwnd, name, name.Capacity);
        return name.ToString();
    }

    internal static bool IsExplorer(IntPtr hwnd) => ClassName(hwnd) == "CabinetWClass";
    internal static IntPtr Root(IntPtr hwnd) => ExplorerNativeHooks.GetAncestor(hwnd, 2 /* GA_ROOT */);
    internal static bool IsCurrent(IntPtr hwnd, uint pid) => pid != 0 && ProcessId(hwnd) == pid
        && Root(ExplorerNativeHooks.GetForegroundWindow()) == hwnd;

    // The keyboard hook can use this without touching StateLock or executing plugin code.
    internal static bool MayBeDialog(IntPtr hwnd, IntPtr tracked, bool trackedIsDialog)
    {
        if (hwnd == IntPtr.Zero) return false;
        if (hwnd == tracked && trackedIsDialog) return true;
        if (ClassName(hwnd) != "#32770") return false;
        var breadcrumb = false;
        var nameField = false;
        var combo = false;
        var visited = 0;
        EnumChildWindows(hwnd, (child, _) =>
        {
            var name = ClassName(child);
            breadcrumb |= name == "Breadcrumb Parent";
            nameField |= (name is "Edit" or "ComboBox" or "ComboBoxEx32") && GetDlgCtrlID(child) is 1148 or 1152;
            combo |= name is "ComboBox" or "ComboBoxEx32";
            // ponytail: cap the hotkey's native scan; unusually large custom dialogs wait for classification.
            return !breadcrumb && !(nameField && combo) && ++visited < 128;
        }, IntPtr.Zero);
        return breadcrumb || (nameField && combo);
    }

    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hwnd, EnumChildProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr hwnd);

    internal static IFileDialogAdapter? ResolveAdapter(IntPtr hwnd) => ExplorerStaInvoker.RunOnStaWithTimeout(
        () => FileDialogAdapterRegistry.GetMatchingAdapter(hwnd, ClassName(hwnd), ProcessNameResolver.GetNameWithoutExtension(ProcessId(hwnd))),
        (IFileDialogAdapter?)null, TimeSpan.FromMilliseconds(200));

    internal static string? ReadSource(IntPtr hwnd) => ExplorerStaInvoker.RunOnStaWithTimeout(() =>
    {
        var className = ClassName(hwnd);
        var processName = ProcessNameResolver.GetNameWithoutExtension(ProcessId(hwnd));
        var collector = ActivePathCollectorRegistry.GetCollectors().FirstOrDefault(c => c.CanHandle(hwnd, className, processName));
        if (collector == null) return null;
        if (className == "CabinetWClass")
        {
            // Already on the bounded STA. Avoid ExplorerPathCollector's nested throwaway STA thread.
            var tab = ExplorerFolderPathReader.GetActiveTab(hwnd);
            var path = ExplorerFolderPathReader.Read(hwnd, tab);
            return tab == ExplorerFolderPathReader.GetActiveTab(hwnd) ? path : null;
        }
        var info = new KeyboardNativeMethods.GUITHREADINFO
            { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<KeyboardNativeMethods.GUITHREADINFO>() };
        var focused = KeyboardNativeMethods.GetGUIThreadInfo(
            ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out _), ref info) ? info.hwndFocus : IntPtr.Zero;
        if (focused == IntPtr.Zero) focused = hwnd;
        return collector.TryGetPath(focused, ClassName(focused), hwnd, className, processName);
    }, (string?)null, TimeSpan.FromMilliseconds(400));
}
