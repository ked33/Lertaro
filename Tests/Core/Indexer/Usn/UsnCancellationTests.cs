using System.Collections;
using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Tests.IndexV2;

namespace Lertaro.Core.Tests.Indexer.Usn;

[TestClass]
public sealed class UsnCancellationTests
{
    [TestMethod]
    public void CancelledBeforeApply_DoesNotMutateTheIndex()
    {
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = fixture.Index;
        var token = new CancellationToken(canceled: true);

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            indexer.ApplyUsnRecords("C", new[] { Record(2) }, token));
        Assert.AreEqual(0, fixture.Index.PendingChangeCount);
    }

    [TestMethod]
    public void CancelledMidBatch_ReleasesTheWriteLockAndDoesNotAdvanceTheWatermark()
    {
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        using var cancellation = new CancellationTokenSource();
        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = fixture.Index;
        indexer._driveMetadata["C"] = new UsnIndexer.DriveRuntimeMetadata { JournalId = 7, NextUsn = 100 };
        var revision = fixture.Index.Revision;

        Assert.ThrowsExactly<OperationCanceledException>(() =>
        {
            if (indexer.ApplyUsnRecords("C", new CancellingBatch(cancellation), cancellation.Token))
                indexer.AdvanceJournalWatermark("C", 7, 200);
        });

        // Re-entering the writer would throw if cancellation leaked the previous lock. The partial
        // batch remains replayable because no success/watermark was published past its first record.
        Assert.IsTrue(fixture.Index.Revision > revision, "a partial mutation must invalidate cached readers");
        fixture.Index.Mutate((snapshot, delta) =>
        {
            Assert.AreEqual(1, delta.AddedRowsForId(2).Length);
            Assert.AreEqual(0, delta.AddedRowsForId(3).Length);
        });
        Assert.AreEqual(100L, indexer._driveMetadata["C"].NextUsn);
    }

    [TestMethod]
    public void MetadataRefresh_ObservesCancellationBeforeResolvingPathsOrReadingFiles()
    {
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        Assert.ThrowsExactly<OperationCanceledException>(() => UsnMetadataReader.Refresh(
            fixture.Index, new HashSet<UInt128> { 1 }, new CancellationToken(canceled: true)));
        Assert.AreEqual(0, fixture.Index.PendingChangeCount);
    }

    private static ParsedUsnRecord Record(ulong id) => new()
    {
        FileReferenceNumber = id,
        ParentFileReferenceNumber = 1,
        FileName = $"file{id}.txt",
        Reason = Win32Api.USN_REASON_FILE_CREATE,
    };

    // Cancels exactly between two records while ApplyUsnRecords owns the write lock; no timing race,
    // filesystem activity, or long-running batch is needed to reproduce the stop responsiveness bug.
    private sealed class CancellingBatch(CancellationTokenSource cancellation) : IReadOnlyList<ParsedUsnRecord>
    {
        public int Count => 2;
        public ParsedUsnRecord this[int index] => Record((ulong)(index + 2));
        public IEnumerator<ParsedUsnRecord> GetEnumerator()
        {
            yield return Record(2);
            cancellation.Cancel();
            yield return Record(3);
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
