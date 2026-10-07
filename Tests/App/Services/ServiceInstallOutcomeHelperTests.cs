using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

[TestClass]
public sealed class ServiceInstallOutcomeHelperTests
{
    [TestMethod]
    public void DetermineResult_RequiresSuccessfulInstallRegistrationAndStart()
    {
        Assert.AreEqual(
            ServiceInstallManager.SilentInstallResult.Started,
            ServiceInstallOutcomeHelper.DetermineResult(true, true, true));
        Assert.AreEqual(
            ServiceInstallManager.SilentInstallResult.Failed,
            ServiceInstallOutcomeHelper.DetermineResult(true, true, false));
        Assert.AreEqual(
            ServiceInstallManager.SilentInstallResult.Failed,
            ServiceInstallOutcomeHelper.DetermineResult(true, false, false));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1056)]
    public void Start_StoppedOrAlreadyRunning_SucceedsWithoutRetry(int exitCode)
    {
        Assert.IsTrue(ServiceInstallManager.TryStartWithRetry(() => exitCode,
            _ => Assert.Fail("Successful starts must not wait.")));
    }

    [TestMethod]
    public void Start_StopPendingThenStopped_RetriesWithoutReinstall()
    {
        var codes = new Queue<int?>(new int?[] { 1061, 1061, 0 });
        var waits = new List<int>();
        Assert.IsTrue(ServiceInstallManager.TryStartWithRetry(() => codes.Dequeue(), waits.Add));
        CollectionAssert.AreEqual(new[] { 500, 500 }, waits);
        Assert.AreEqual(0, codes.Count);
    }

    [TestMethod]
    [DataRow(5)]
    [DataRow(1060)]
    public void Start_PermanentError_DoesNotRetry(int exitCode)
    {
        Assert.IsFalse(ServiceInstallManager.TryStartWithRetry(() => exitCode,
            _ => Assert.Fail("Permanent failures need repair, not a retry loop.")));
    }

    [TestMethod]
    public void Start_TransitionNeverFinishes_HasBoundedRetries()
    {
        var attempts = 0;
        var waits = 0;
        Assert.IsFalse(ServiceInstallManager.TryStartWithRetry(() => { attempts++; return 1061; }, _ => waits++));
        Assert.AreEqual(21, attempts);
        Assert.AreEqual(20, waits);
    }

    [TestMethod]
    public void Start_ProcessTimeout_DoesNotRepeatIndefinitely()
    {
        Assert.IsFalse(ServiceInstallManager.TryStartWithRetry(() => null,
            _ => Assert.Fail("A timed-out sc process must not start another long wait.")));
    }
}
