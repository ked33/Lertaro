using Lertaro.Core.Hook;

namespace Lertaro.Core.Tests.Hook;

[TestClass]
public sealed class RecentFolderTrackerTests
{
    [TestMethod]
    public void BurstCoalescesIntoOneReadAndDoesNotPollAfterward()
    {
        using var visited = new ManualResetEventSlim();
        var reads = 0;
        using var tracker = new ExplorerRecentFolderTracker((_, _, _) => visited.Set(),
            _ => { Interlocked.Increment(ref reads); return (10, @"C:\A"); }, (_, _) => true, quietMs: 750);
        tracker.Configure(true);
        for (var i = 0; i < 500; i++) tracker.Request(1);
        Assert.IsTrue(visited.Wait(10000));
        Assert.AreEqual(1, Volatile.Read(ref reads));
        Thread.Sleep(250);
        Assert.AreEqual(1, Volatile.Read(ref reads));
    }

    [TestMethod]
    public void BusyReadDoesNotSpawnMoreWorkersAndPendingReadUsesNewestWindow()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        var reads = 0;
        string? recorded = null;
        using var tracker = new ExplorerRecentFolderTracker((_, path, _) => { recorded = path; finished.Set(); },
            target =>
            {
                Interlocked.Increment(ref reads);
                if (target == 1) { started.Set(); release.Wait(10000); }
                return (10, target == 1 ? @"C:\Old" : @"C:\New");
            }, (_, _) => true, quietMs: 20);
        try
        {
            tracker.Configure(true);
            tracker.Request(1);
            Assert.IsTrue(started.Wait(10000));
            for (var i = 0; i < 500; i++) tracker.Request(2);
            Thread.Sleep(100);
            Assert.AreEqual(1, Volatile.Read(ref reads));
            release.Set();
            Assert.IsTrue(finished.Wait(10000));
            Assert.AreEqual(@"C:\New", recorded);
            Assert.AreEqual(2, Volatile.Read(ref reads));
        }
        finally { release.Set(); }
    }

    [TestMethod]
    public void DisabledRecorderIgnoresPendingAndNewRequests()
    {
        var reads = 0;
        using var tracker = new ExplorerRecentFolderTracker((_, _, _) => { },
            _ => { Interlocked.Increment(ref reads); return (10, @"C:\A"); }, (_, _) => true, quietMs: 100);
        tracker.Configure(true);
        tracker.Request(1);
        tracker.Configure(false);
        tracker.Request(2);
        Thread.Sleep(250);
        Assert.AreEqual(0, Volatile.Read(ref reads));
    }

    [TestMethod]
    public void ChangedActiveTabDiscardsSample()
    {
        using var checkedTarget = new ManualResetEventSlim();
        var visits = 0;
        using var tracker = new ExplorerRecentFolderTracker((_, _, _) => Interlocked.Increment(ref visits),
            _ => (10, @"C:\OldTab"), (_, _) => { checkedTarget.Set(); return false; }, quietMs: 20);
        tracker.Configure(true);
        tracker.Request(1);
        Assert.IsTrue(checkedTarget.Wait(10000));
        Assert.AreEqual(0, Volatile.Read(ref visits));
    }
}
