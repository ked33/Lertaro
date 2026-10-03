using Lertaro.Plugins.FolderCascader.Navigation;

namespace Lertaro.Plugins.FolderCascader.Tests.Navigation;

[TestClass]
public sealed class QuickNavigationSnapshotCacheTests
{
    private sealed record Value(int Number);

    [TestMethod]
    [Timeout(10000)]
    public async Task RepeatedRequestsShareWorkAndObsoleteQueuedInputsAreSkipped()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<int>();
        var cache = new NavigationSnapshotCache<int, Value>((a, b) => a == b, input =>
        {
            lock (calls) calls.Add(input);
            Assert.AreEqual(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
            if (input == 1)
            {
                started.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            }
            return new(input);
        });
        var first = cache.Get(1);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreSame(first, cache.Get(1));
            var obsolete = cache.Get(2);
            var latest = cache.Get(3);
            Assert.IsTrue(first.IsCanceled);
            Assert.IsTrue(obsolete.IsCanceled);
            release.Set();
            var value = await latest.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(3, value.Number);
            Assert.AreSame(value, await cache.Get(3));
            lock (calls) CollectionAssert.AreEqual(new[] { 1, 3 }, calls);
        }
        finally { release.Set(); }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task ExpiredSnapshotRemainsImmediatelyAvailableDuringRefresh()
    {
        long now = 0;
        var calls = 0;
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new NavigationSnapshotCache<int, Value>((a, b) => a == b, _ =>
        {
            var count = Interlocked.Increment(ref calls);
            if (count == 2)
            {
                started.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            }
            return new(count);
        }, () => Interlocked.Read(ref now));
        var first = await cache.Get(1).WaitAsync(TimeSpan.FromSeconds(5));
        Interlocked.Exchange(ref now, 10001);
        try
        {
            var stale = cache.Get(1);
            Assert.IsTrue(stale.IsCompletedSuccessfully);
            Assert.AreSame(first, await stale);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreSame(first, await cache.Get(1));
            release.Set();
            Assert.IsTrue(SpinWait.SpinUntil(() => cache.Get(1).Result.Number == 2, 3000));
            Assert.AreEqual(2, calls);
        }
        finally { release.Set(); }
    }

    [TestMethod]
    [Timeout(10000)]
    public async Task FailedPreparationCanBeRetried()
    {
        var calls = 0;
        var cache = new NavigationSnapshotCache<int, Value>((a, b) => a == b,
            input => input == 2 && Interlocked.Increment(ref calls) == 1 ? throw new IOException("unavailable") : new(input));
        Assert.AreEqual(1, (await cache.Get(1).WaitAsync(TimeSpan.FromSeconds(5))).Number);
        try { await cache.Get(2).WaitAsync(TimeSpan.FromSeconds(5)); Assert.Fail(); }
        catch (IOException) { }
        Assert.AreEqual(2, (await cache.Get(2).WaitAsync(TimeSpan.FromSeconds(5))).Number);
    }
}
