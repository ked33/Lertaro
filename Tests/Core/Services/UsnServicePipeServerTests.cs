using Lertaro.Core.Services;

namespace Lertaro.Core.Tests.Services;

[TestClass]
public sealed class UsnServicePipeServerTests
{
    [TestMethod]
    public void StatusSignals_ConcurrentBurstLeavesOnlyOnePendingSend()
    {
        var channel = UsnServicePipeServer.CreateStatusSignalChannel();
        Parallel.For(0, 10_000, _ => channel.Writer.TryWrite(true));

        Assert.IsTrue(channel.Reader.TryRead(out _));
        Assert.IsFalse(channel.Reader.TryRead(out _), "a slow client must not accumulate stale sends");
        Assert.IsTrue(channel.Writer.TryWrite(true));
        Assert.IsTrue(channel.Reader.TryRead(out _), "a change during the next send must wake the reader again");
    }

    [TestMethod]
    public async Task StatusSignals_StopAllowsLateCallbacksAndCancelsAnIdleReader()
    {
        var channel = UsnServicePipeServer.CreateStatusSignalChannel();
        using var cancellation = new CancellationTokenSource();
        var read = channel.Reader.ReadAsync(cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => { await read; });

        channel.Writer.TryComplete();
        Assert.IsFalse(channel.Writer.TryWrite(true), "an in-flight handler after unsubscribe must be harmless");
        await channel.Reader.Completion;
    }
}
