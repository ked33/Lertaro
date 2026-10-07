using System.Reflection;
using Lertaro.Core.Services.Search;
using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

// BeginServiceReconnectGracePeriod/ShouldWaitForServiceReconnect/ClearServiceReconnectState/
// ResetAutoInstallFlag all read/write PROCESS-WIDE static fields shared by every ServiceConnectionHandler
// instance (by design -- one reconnect timer serves every open window) -- so every test here must run
// un-parallelized and explicitly reset that state before AND after, or leftover state from one test (or
// a prior failed run) silently changes another test's outcome.
[TestClass]
[DoNotParallelize]
public sealed class ServiceConnectionHandlerTests
{
    private static ServiceConnectionHandler MakeHandler() => new(
        new SearchService(),
        onStatusUpdated: _ => { },
        onServiceInstallStarted: () => { },
        onServiceInstallCompleted: () => { },
        onServiceInstallError: _ => { },
        onServiceFailedToStart: () => { },
        onServiceReachable: () => { });

    private static void SetGlobalField(string name, object value) =>
        typeof(ServiceConnectionMonitor).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, value);

    [TestInitialize]
    [TestCleanup]
    public void ResetGlobalReconnectState()
    {
        MakeHandler().ClearServiceReconnectState();
        SetGlobalField("_globalAutoInstallingService", false);
        // Each STA test has its own Dispatcher; never reuse a timer owned by a previous test thread.
        ServiceConnectionMonitor._sharedStatusTimer = null;
        ServiceConnectionMonitor._sharedSearchService = null;
    }

    [TestMethod]
    public void ShouldWaitForServiceReconnect_DefaultState_ReturnsFalse() =>
        Assert.IsFalse(MakeHandler().ShouldWaitForServiceReconnect());

    [TestMethod]
    public void BeginServiceReconnectGracePeriod_ThenShouldWait_ReturnsTrue()
    {
        var handler = MakeHandler();

        handler.BeginServiceReconnectGracePeriod();

        Assert.IsTrue(handler.ShouldWaitForServiceReconnect());
    }

    [TestMethod]
    public void ClearServiceReconnectState_AfterGracePeriodStarted_ShouldWaitReturnsFalseAgain()
    {
        var handler = MakeHandler();
        handler.BeginServiceReconnectGracePeriod();

        handler.ClearServiceReconnectState();

        Assert.IsFalse(handler.ShouldWaitForServiceReconnect());
    }

    [TestMethod]
    public void ShouldWaitForServiceReconnect_AutoInstallingRegardlessOfGracePeriod_ReturnsTrue()
    {
        SetGlobalField("_globalAutoInstallingService", true);

        Assert.IsTrue(MakeHandler().ShouldWaitForServiceReconnect());
    }

    [TestMethod]
    public void IsAutoInstallingService_ReflectsGlobalState()
    {
        SetGlobalField("_globalAutoInstallingService", true);

        Assert.IsTrue(MakeHandler().IsAutoInstallingService);
    }

    [TestMethod]
    public void ClearServiceReconnectState_ClearsAutoInstallingFlagToo()
    {
        SetGlobalField("_globalAutoInstallingService", true);
        var handler = MakeHandler();

        handler.ClearServiceReconnectState();

        Assert.IsFalse(handler.IsAutoInstallingService);
    }

    [TestMethod]
    public void HasAttemptedAutoInstall_ReflectsGlobalState()
    {
        SetGlobalField("_globalAutoInstallAttempted", true);

        Assert.IsTrue(MakeHandler().HasAttemptedAutoInstall);

        SetGlobalField("_globalAutoInstallAttempted", false);
    }

    [TestMethod]
    public void ResetAutoInstallFlag_ClearsHasAttemptedAutoInstall()
    {
        SetGlobalField("_globalAutoInstallAttempted", true);
        var handler = MakeHandler();

        handler.ResetAutoInstallFlag();

        Assert.IsFalse(handler.HasAttemptedAutoInstall);
    }

    [TestMethod]
    public void ResetAutoInstallFlag_DoesNotClearReconnectGracePeriod()
    {
        var handler = MakeHandler();
        handler.BeginServiceReconnectGracePeriod();

        handler.ResetAutoInstallFlag();

        Assert.IsTrue(handler.ShouldWaitForServiceReconnect());
    }

    [TestMethod]
    public void Constructor_NullSearchService_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new ServiceConnectionHandler(null!, _ => { }, () => { }, () => { }, _ => { }, () => { }, () => { }));

    [TestMethod]
    public void Constructor_NullOnStatusUpdated_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new ServiceConnectionHandler(new SearchService(), null!, () => { }, () => { }, _ => { }, () => { }, () => { }));

    [TestMethod]
    public void Constructor_NullOnServiceReachable_Throws() =>
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new ServiceConnectionHandler(new SearchService(), _ => { }, () => { }, () => { }, _ => { }, () => { }, null!));

    [StaTestMethod]
    public void FailedPing_KeepsMonitoringAndNotifiesAgainAfterRecovery()
    {
        var failures = 0;
        var recoveries = 0;
        using var search = new SearchService();
        using var handler = new ServiceConnectionHandler(search, _ => { }, () => { }, () => { }, _ => { },
            () => failures++, () => recoveries++);
        handler.Start();
        SetGlobalField("_globalAutoInstallAttempted", true); // Do not invoke an installer in a test.

        handler.ProcessPingResult(true);
        handler.ProcessPingResult(false);
        handler.ProcessPingResult(false);
        Assert.AreEqual(1, failures, "report degradation once, without repeated UI callbacks");
        Assert.IsTrue(ServiceConnectionMonitor.ActiveSubscribers.Contains(handler));
        Assert.IsTrue(handler.HasReportedFailure);
        ServiceConnectionMonitor.ApplyPollInterval(false);
        Assert.AreEqual(ServiceConnectionMonitor.SteadyPollIntervalMs,
            (int)ServiceConnectionMonitor._sharedStatusTimer!.Interval.TotalMilliseconds);

        handler.ProcessPingResult(true);
        Assert.AreEqual(2, recoveries, "a successful ping after failure must restart bootstrap");
        Assert.IsFalse(handler.HasReportedFailure);
    }

    [StaTestMethod]
    public void FailedStatus_KeepsMonitoringUntilServiceBecomesReady()
    {
        var failures = 0;
        var statuses = new List<string>();
        using var search = new SearchService();
        using var handler = new ServiceConnectionHandler(search, status => statuses.Add(status.State),
            () => { }, () => { }, _ => { }, () => failures++, () => { });
        handler.Start(requireDetailedStatus: true);
        SetGlobalField("_globalAutoInstallAttempted", true);

        handler.ProcessStatus(new() { State = "error" });
        handler.ProcessStatus(new() { State = "error" });
        Assert.AreEqual(1, failures);
        Assert.IsTrue(ServiceConnectionMonitor.ActiveSubscribers.Contains(handler));
        ServiceConnectionMonitor.ApplyPollInterval(false);
        Assert.AreEqual(ServiceConnectionMonitor.SteadyPollIntervalMs,
            (int)ServiceConnectionMonitor._sharedStatusTimer!.Interval.TotalMilliseconds);

        handler.ProcessStatus(new() { State = "loading-cache" });
        ServiceConnectionMonitor.ApplyPollInterval(true);
        Assert.AreEqual(400, (int)ServiceConnectionMonitor._sharedStatusTimer!.Interval.TotalMilliseconds);
        handler.ProcessStatus(new() { State = "ready" });
        CollectionAssert.AreEqual(new[] { "loading-cache", "ready" }, statuses);
        Assert.IsFalse(handler.HasReportedFailure);
    }

    [TestMethod]
    public void BootstrapInstallerInProgress_PreventsCompetingRecovery()
    {
        var field = typeof(ServiceInstallManager).GetField("_silentInstallInFlight", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            field.SetValue(null, 1);
            Assert.IsTrue(MakeHandler().ShouldWaitForServiceReconnect());
        }
        finally
        {
            field.SetValue(null, 0);
        }
    }
}
