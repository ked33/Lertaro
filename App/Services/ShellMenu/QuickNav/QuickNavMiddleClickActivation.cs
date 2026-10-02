namespace Lertaro.App.Services.ShellMenu.QuickNav;

// A middle click activates the window under the cursor, but ExplorerTracker only learns about that
// on the later foreground event. Reading it at click time therefore still describes the previous
// window (a console, say) and the gate refuses. The click itself is what brings Explorer forward, so
// the gate just has to wait for that — off the hook callback, which must stay non-blocking.
internal static class QuickNavMiddleClickActivation
{
    // Long enough for the foreground event to be mirrored across the hook pipe. A same-folder switch
    // spends the whole budget, because the new window's path is then indistinguishable from the old
    // one's until the budget ends; the menu still opens for that folder.
    internal const int WaitMs = 400;
    internal const int PollMs = 15;

    internal readonly record struct ClickTarget(IntPtr Root, string ProcessName, string ClassName);

    internal readonly record struct Host(IntPtr Hwnd, string Process, string ClassName, bool IsDesktop, string? Path);

    // Only an Explorer file list is worth waiting on. Anything else keeps today's answer immediately,
    // so another program's middle click is never delayed.
    internal static bool ShouldWait(ClickTarget click) =>
        click.Root != IntPtr.Zero
        && click.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)
        && IsFileListClass(click.ClassName);

    internal static bool IsFileListClass(string className) =>
        className.Equals("DirectUIHWND", StringComparison.OrdinalIgnoreCase)
        || className.Equals("SysListView32", StringComparison.OrdinalIgnoreCase);

    // Null when the clicked Explorer never became the tracked window: the menu stays closed.
    internal static Host? Resolve(Host before, ClickTarget click, Func<Host> read, Action<int> wait, int waitMs = WaitMs, int pollMs = PollMs)
    {
        if (!ShouldWait(click) || before.Hwnd == click.Root)
            return before;

        var waited = 0;
        while (true)
        {
            var now = read();
            if (now.Hwnd == click.Root && PathSettled(before, now))
                return now;
            if (waited >= waitMs)
                return now.Hwnd == click.Root ? now : null;
            wait(pollMs);
            waited += pollMs;
        }
    }

    // The activation event publishes the hwnd before the folder. A path still empty, or still the
    // previous window's, is that in-between read.
    private static bool PathSettled(Host before, Host now) =>
        !string.IsNullOrEmpty(now.Path) && (before.Hwnd == now.Hwnd || now.Path != before.Path);
}
