using Lertaro.Core.Wire;

namespace Lertaro.Core.Tests.Wire;

[TestClass]
public sealed class RecentFolderMessageTests
{
    [TestMethod]
    public async Task VisitRoundTripPreservesSourcePathAndCaptureTimeWithoutMisaligningNextFrame()
    {
        using var stream = new MemoryStream();
        var time = DateTime.UtcNow.Ticks;
        await PipeRequestBinarySerializer.WriteMessageAsync(stream, new IpcMessage
        {
            Id = IpcMessageId.RecentFolderVisited, Hwnd = 0x1234ABCD,
            StringVal1 = @"\\server\share\资料", ObservedUtcTicks = time
        });
        await PipeRequestBinarySerializer.WriteMessageAsync(stream, new IpcMessage { Id = IpcMessageId.Stop });
        stream.Position = 0;
        var read = await PipeRequestBinarySerializer.ReadMessageAsync(stream);
        Assert.AreEqual(IpcMessageId.RecentFolderVisited, read.Id);
        Assert.AreEqual(0x1234ABCDL, read.Hwnd);
        Assert.AreEqual(@"\\server\share\资料", read.StringVal1);
        Assert.AreEqual(time, read.ObservedUtcTicks);
        Assert.AreEqual(IpcMessageId.Stop, (await PipeRequestBinarySerializer.ReadMessageAsync(stream)).Id);
    }
}
