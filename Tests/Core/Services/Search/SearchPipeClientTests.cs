using Lertaro.Core.Services.Search;
using Lertaro.Core.Wire;

namespace Lertaro.Core.Tests.Services.Search;

[TestClass]
public sealed class SearchPipeClientTests
{
    private static PipeResponse Disconnected => new()
    {
        Kind = PipeResponseKind.Error, IsTransportError = true,
        Message = "End of stream reached. Read 0 of 4 bytes."
    };

    [TestMethod]
    [DataRow(SearchRequestId.Ping)]
    [DataRow(SearchRequestId.Status)]
    public async Task Probe_TransientDisconnectThenSuccess_Retries(SearchRequestId command)
    {
        var attempts = 0;
        var result = await SearchPipeClient.SendWithProbeRetryAsync(new() { Id = command },
            (_, _) => Task.FromResult(++attempts < 3 ? Disconnected : new PipeResponse { Kind = PipeResponseKind.Ok }),
            CancellationToken.None, retryDelayMs: 0);
        Assert.AreEqual(3, attempts);
        Assert.AreEqual(PipeResponseKind.Ok, result.Kind);
    }

    [TestMethod]
    public async Task Probe_PermanentDisconnect_StopsAfterBoundedRetries()
    {
        var attempts = 0;
        var result = await SearchPipeClient.SendWithProbeRetryAsync(new() { Id = SearchRequestId.Status },
            (_, _) => { attempts++; return Task.FromResult(Disconnected); }, CancellationToken.None, retryDelayMs: 0);
        Assert.AreEqual(3, attempts);
        Assert.IsTrue(result.IsTransportError);
    }

    [TestMethod]
    public async Task Probe_ConnectedButNeverAnswers_CancelsReadAndRetries()
    {
        var attempts = 0;
        var result = await SearchPipeClient.SendWithProbeRetryAsync(new() { Id = SearchRequestId.Status },
            async (_, token) =>
            {
                attempts++;
                await Task.Delay(Timeout.Infinite, token);
                return new PipeResponse { Kind = PipeResponseKind.Ok };
            }, CancellationToken.None, probeTimeoutMs: 20, retryDelayMs: 0);
        Assert.AreEqual(3, attempts);
        Assert.IsTrue(result.IsTransportError);
        StringAssert.Contains(result.Message, "timed out");
    }

    [TestMethod]
    public async Task Probe_ServiceError_IsNotRetried()
    {
        var attempts = 0;
        var result = await SearchPipeClient.SendWithProbeRetryAsync(new() { Id = SearchRequestId.Status },
            (_, _) => { attempts++; return Task.FromResult(new PipeResponse { Kind = PipeResponseKind.Error, Message = "Index failed" }); },
            CancellationToken.None, retryDelayMs: 0);
        Assert.AreEqual(1, attempts);
        Assert.IsFalse(result.IsTransportError);
        Assert.AreEqual("Index failed", result.Message);
    }

    [TestMethod]
    [DataRow(SearchRequestId.Initialize)]
    [DataRow(SearchRequestId.Rebuild)]
    [DataRow(SearchRequestId.LaunchHook)]
    [DataRow(SearchRequestId.ApplyUpdate)]
    public async Task MutatingCommands_AreNeverReplayedAfterDisconnect(SearchRequestId command)
    {
        var attempts = 0;
        var result = await SearchPipeClient.SendWithProbeRetryAsync(new() { Id = command },
            (_, _) => { attempts++; return Task.FromResult(Disconnected); }, CancellationToken.None, retryDelayMs: 0);
        Assert.AreEqual(1, attempts);
        Assert.IsTrue(result.IsTransportError);
    }

    [TestMethod]
    public async Task CallerCancellation_IsNotTreatedAsTransientFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await SearchPipeClient.SendWithProbeRetryAsync(new() { Id = SearchRequestId.Ping },
                (_, token) =>
                {
                    attempts++;
                    cancellation.Cancel();
                    return Task.FromCanceled<PipeResponse>(token);
                }, cancellation.Token, retryDelayMs: 0));
        Assert.AreEqual(1, attempts);
    }

    [TestMethod]
    public async Task AlreadyCancelled_DoesNotConnect()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await SearchPipeClient.SendWithProbeRetryAsync(new() { Id = SearchRequestId.Ping },
                (_, _) => throw new InvalidOperationException("Must not connect."), cancellation.Token));
    }
}
