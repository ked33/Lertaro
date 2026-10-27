using System.Reflection;
using Lertaro.Core.Hook;

namespace Lertaro.Core.Tests.Hook;

[TestClass]
[DoNotParallelize]
public sealed class ExplorerStaInvokerTests
{
    // More than ExplorerStaInvoker.WorkerCount, so round-robin reaches every worker at least twice.
    private const int Reads = 12;

    [TestMethod]
    public void RunOnStaWithTimeout_RunsTheReadOnAStaWorkerThatIsNotTheCaller()
    {
        var ran = ExplorerStaInvoker.RunOnStaWithTimeout(
            () => (Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.Name, Thread.CurrentThread.GetApartmentState()),
            (0, (string?)null, ApartmentState.Unknown), TimeSpan.FromSeconds(5));

        Assert.AreNotEqual(Environment.CurrentManagedThreadId, ran.Item1, "the read ran inline on the caller");
        Assert.AreEqual(ApartmentState.STA, ran.Item3, "a cross-process shell read needs a single-threaded apartment");
        StringAssert.StartsWith(ran.Item2, "ExplorerStaWorker", $"unexpected worker name: {ran.Item2}");
    }

    [TestMethod]
    public void RunOnStaWithTimeout_RepeatedReadsReuseThePoolInsteadOfAThreadPerRead()
    {
        // The old shape started one throwaway STA thread per read and abandoned it on timeout. An apartment
        // that dies while the target process still holds registrations in it is what wedged explorer.exe's
        // shell thread, so the pool being fixed-size is the regression test, not a tidiness preference.
        var workers = new HashSet<int>();
        for (var i = 0; i < Reads; i++)
            workers.Add(ExplorerStaInvoker.RunOnStaWithTimeout(() => Environment.CurrentManagedThreadId, 0, TimeSpan.FromSeconds(5)));

        Assert.IsLessThanOrEqualTo(4, workers.Count,
            "more distinct workers than ExplorerStaInvoker.WorkerCount means a thread is being started per read");
    }

    [TestMethod]
    public void RunOnStaWithTimeout_ReadThatThrowsLeavesEveryWorkerUsable()
    {
        // A worker now outlives the read it ran, so an exception escaping into its dispatcher would stop the
        // pool taking work at all -- and the reads that follow would fall back rather than answer.
        var fallback = ExplorerStaInvoker.RunOnStaWithTimeout(() => throw new InvalidOperationException("boom"), "kept", TimeSpan.FromSeconds(5));

        Assert.AreEqual("kept", fallback);
        for (var i = 0; i < Reads; i++)
            Assert.AreEqual("ok", ExplorerStaInvoker.RunOnStaWithTimeout(() => "ok", "fallback", TimeSpan.FromSeconds(5)),
                "a throwing read stopped a worker from taking further reads");
    }

    [TestMethod]
    public void RunOnStaWithTimeout_Completed_ReturnsResultAndNotTimedOut()
    {
        var result = ExplorerStaInvoker.RunOnStaWithTimeout(() => "ok", "fallback", TimeSpan.FromSeconds(1), out var timedOut);

        Assert.AreEqual("ok", result);
        Assert.IsFalse(timedOut);
    }

    [TestMethod]
    public async Task RunOnStaWithTimeout_TimedOut_ReturnsFallbackAndSetsTimedOut()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var budget = typeof(ExplorerStaInvoker).GetField("_activeReads", BindingFlags.Static | BindingFlags.NonPublic)!;
        var read = Task.Run(() =>
        {
            var result = ExplorerStaInvoker.RunOnStaWithTimeout(() =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                return "late";
            }, "fallback", TimeSpan.FromSeconds(1), out var timedOut);
            return (result, timedOut);
        });
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)), "the read must start before checking its timeout");
            var (result, timedOut) = await read;
            Assert.AreEqual("fallback", result);
            Assert.IsTrue(timedOut);
            Assert.AreEqual(1, (int)budget.GetValue(null)!, "a running timed-out read still owns its slot");
        }
        finally
        {
            release.Set();
            await read;
            Assert.IsTrue(SpinWait.SpinUntil(() => (int)budget.GetValue(null)! == 0, TimeSpan.FromSeconds(5)));
        }
    }

    [TestMethod]
    public async Task ResumeWhileReadIsRunning_DoesNotLoseItsBudgetOrUnderflowOnCompletion()
    {
        var budget = typeof(ExplorerStaInvoker).GetField("_activeReads", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var read = Task.Run(() => ExplorerStaInvoker.RunOnStaWithTimeout(() =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return "done";
        }, "timeout", TimeSpan.FromSeconds(10)));
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.AreEqual(1, (int)budget.GetValue(null)!);
            ExplorerStaInvoker.ResetAbandonedReads();
            Assert.AreEqual(1, (int)budget.GetValue(null)!);
        }
        finally { release.Set(); }
        Assert.AreEqual("done", await read);
        Assert.IsTrue(SpinWait.SpinUntil(() => (int)budget.GetValue(null)! == 0, TimeSpan.FromSeconds(5)));
        Assert.AreEqual("ok", ExplorerStaInvoker.RunOnStaWithTimeout(() => "ok", "refused", TimeSpan.FromSeconds(5)));
    }
}
