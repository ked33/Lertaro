using Lertaro.Core.Hook;

namespace Lertaro.Core.Tests.Hook;

[TestClass]
public sealed class RecentFolderVisitStateTests
{
    [TestMethod]
    public void RepeatedEventsDoNotBecomeVisitsButNavigatingBackDoes()
    {
        var state = new RecentFolderVisitState();
        state.ChangeTarget(1);
        Assert.IsTrue(state.Accept(1, state.Generation, 10, @"C:\A"));
        Assert.IsFalse(state.Accept(1, state.Generation, 10, @"c:/a/"));
        Assert.IsTrue(state.Accept(1, state.Generation, 10, @"C:\B"));
        Assert.IsTrue(state.Accept(1, state.Generation, 10, @"C:\A"));
    }

    [TestMethod]
    public void TabSwitchAndReturningToExplorerCountAsVisits()
    {
        var state = new RecentFolderVisitState();
        state.ChangeTarget(1);
        Assert.IsTrue(state.Accept(1, state.Generation, 10, @"C:\A"));
        Assert.IsTrue(state.Accept(1, state.Generation, 11, @"C:\A"));
        state.ChangeTarget(0);
        state.ChangeTarget(1);
        Assert.IsTrue(state.Accept(1, state.Generation, 11, @"C:\A"));
    }

    [TestMethod]
    public void LateResultFromPreviousForegroundSessionIsDiscarded()
    {
        var state = new RecentFolderVisitState();
        state.ChangeTarget(1);
        var generation = state.Generation;
        state.ChangeTarget(2);
        state.ChangeTarget(1);
        Assert.IsFalse(state.Accept(1, generation, 10, @"C:\Old"));
        Assert.IsFalse(state.Accept(2, state.Generation, 10, @"C:\Other"));
        Assert.IsTrue(state.Accept(1, state.Generation, 10, @"C:\New"));
    }

    [TestMethod]
    public void VirtualFolderClearsLastVisitWithoutBecomingAnEntry()
    {
        var state = new RecentFolderVisitState();
        state.ChangeTarget(1);
        state.Accept(1, state.Generation, 10, @"C:\A");
        Assert.IsFalse(state.Accept(1, state.Generation, 10, "shell:MyComputerFolder"));
        Assert.IsTrue(state.Accept(1, state.Generation, 10, @"C:\A"));
    }

    [TestMethod]
    public void OnlyNativeExplorerWindowsAreAccepted()
    {
        Assert.IsTrue(RecentFolderVisitState.IsExplorer("CabinetWClass", "explorer"));
        Assert.IsFalse(RecentFolderVisitState.IsExplorer("CabinetWClass", "other"));
        Assert.IsFalse(RecentFolderVisitState.IsExplorer("WorkerW", "explorer"));
        Assert.IsFalse(RecentFolderVisitState.IsExplorer("#32770", "explorer"));
        Assert.IsFalse(RecentFolderVisitState.IsExplorer("XYplorer", "XYplorer"));
    }
}
