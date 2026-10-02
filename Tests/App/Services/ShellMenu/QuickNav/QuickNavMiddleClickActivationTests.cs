using Lertaro.App.Services.ShellMenu.QuickNav;

namespace Lertaro.App.Tests.Services.ShellMenu.QuickNav;

// The click activates Explorer, but the tracker still names the previous window until that event
// arrives. The wait is the whole fix, so these pin when it happens and when it gives up.
[TestClass]
public sealed class QuickNavigationMiddleClickActivationTests
{
    private static readonly IntPtr Explorer = new(0x11);
    private static readonly IntPtr Console = new(0x22);

    private static QuickNavMiddleClickActivation.Host Host(IntPtr hwnd, string? path) =>
        new(hwnd, "host", "CabinetWClass", IsDesktop: false, path);

    [TestMethod]
    public void OnlyAnExplorerFileListIsWorthWaitingFor()
    {
        Assert.IsTrue(QuickNavMiddleClickActivation.ShouldWait(
            new(Explorer, "explorer", "DirectUIHWND")));
        Assert.IsTrue(QuickNavMiddleClickActivation.ShouldWait(
            new(Explorer, "EXPLORER", "SysListView32")));
        Assert.IsFalse(QuickNavMiddleClickActivation.ShouldWait(
            new(Console, "cmd", "ConsoleWindowClass")));
        Assert.IsFalse(QuickNavMiddleClickActivation.ShouldWait(
            new(Explorer, "explorer", "CabinetWClass")));
    }

    [TestMethod]
    public void AnAlreadyTrackedWindowIsUsedImmediately()
    {
        var reads = 0;
        var result = QuickNavMiddleClickActivation.Resolve(
            Host(Explorer, @"C:\here"),
            new(Explorer, "explorer", "DirectUIHWND"),
            () => { reads++; return Host(Explorer, @"C:\here"); },
            _ => Assert.Fail("nothing to wait for"));

        Assert.AreEqual(0, reads);
        Assert.AreEqual(Explorer, result?.Hwnd);
    }

    [TestMethod]
    public void AClickElsewhereDoesNotWait()
    {
        var result = QuickNavMiddleClickActivation.Resolve(
            Host(Console, @"C:\old"),
            new(Explorer, "notepad", "Edit"),
            () => Host(Explorer, @"C:\new"),
            _ => Assert.Fail("another program's click must not be delayed"));

        Assert.AreEqual(Console, result?.Hwnd);
    }

    [TestMethod]
    public void ItWaitsUntilTheClickedExplorerPublishesItsOwnPath()
    {
        var reads = 0;
        var waited = 0;
        var result = QuickNavMiddleClickActivation.Resolve(
            Host(Console, @"C:\old"),
            new(Explorer, "explorer", "DirectUIHWND"),
            () =>
            {
                reads++;
                return reads < 3 ? Host(Console, @"C:\old") : Host(Explorer, @"C:\clicked");
            },
            ms => waited += ms,
            waitMs: 400,
            pollMs: 15);

        Assert.AreEqual(@"C:\clicked", result?.Path);
        Assert.AreEqual(30, waited);
    }

    [TestMethod]
    public void AnActivationThatHasNotPublishedAPathYetKeepsWaiting()
    {
        var reads = 0;
        var result = QuickNavMiddleClickActivation.Resolve(
            Host(Console, @"C:\old"),
            new(Explorer, "explorer", "SysListView32"),
            () =>
            {
                reads++;
                return reads == 1 ? Host(Explorer, null) : Host(Explorer, @"C:\clicked");
            },
            _ => { },
            waitMs: 400,
            pollMs: 15);

        Assert.AreEqual(@"C:\clicked", result?.Path);
    }

    [TestMethod]
    public void ASameFolderSwitchOpensOnceTheWaitIsSpent()
    {
        var result = QuickNavMiddleClickActivation.Resolve(
            Host(Console, @"C:\shared"),
            new(Explorer, "explorer", "DirectUIHWND"),
            () => Host(Explorer, @"C:\shared"),
            _ => { },
            waitMs: 30,
            pollMs: 15);

        Assert.AreEqual(Explorer, result?.Hwnd);
        Assert.AreEqual(@"C:\shared", result?.Path);
    }

    [TestMethod]
    public void AClickThatNeverActivatesExplorerOpensNothing()
    {
        var result = QuickNavMiddleClickActivation.Resolve(
            Host(Console, @"C:\old"),
            new(Explorer, "explorer", "DirectUIHWND"),
            () => Host(Console, @"C:\old"),
            _ => { },
            waitMs: 30,
            pollMs: 15);

        Assert.IsNull(result);
    }
}
