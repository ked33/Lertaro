using Lertaro.Core.IndexV2.Delta;

namespace Lertaro.Core.Tests.IndexV2.Delta;

[TestClass]
public sealed class DeltaAddedIndexTests
{
    private static LiveIndexFixture Build() => LiveIndexFixture.Build("C", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "Projects", FileRecordFlags.Directory),
        new FileRecord(3, 1, "Other", FileRecordFlags.Directory),
        new FileRecord(4, 2, "base.txt", FileRecordFlags.None),
    });

    [TestMethod]
    public void LargeChurn_LookupVisitsOnlyTheRequestedFullWidthId()
    {
        using var fixture = Build();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            for (var id = 100; id < 20_100; id++)
                DeltaLinkOps.AddLink(delta, (uint)id, 2, $"file{id}.txt", FileRecordFlags.None);
            var highId = new UInt128(1, 100);
            DeltaLinkOps.AddLink(delta, highId, 2, "high.txt", FileRecordFlags.None);
            DeltaLinkOps.AddLink(delta, highId, 3, "hard-link.txt", FileRecordFlags.None);

            Assert.AreEqual(2, delta.AddedRowsForId(highId).Length);
            Assert.AreEqual(1, delta.AddedRowsForId(100).Length);
            Assert.AreEqual(0, delta.AddedRowsForId(new UInt128(2, 100)).Length);
            DeltaLinkOps.UpdateMetadata(delta, highId, 321, 1, 2, 3);
            DeltaLinkOps.UpdateFlags(delta, highId, FileRecordFlags.Hidden);
            foreach (var row in delta.AddedRowsForId(highId))
            {
                Assert.AreEqual(321L, delta.Added[row].Size);
                Assert.AreEqual((ushort)FileRecordFlags.Hidden, delta.Added[row].Flags);
            }
            Assert.AreEqual(0L, delta.Added[delta.AddedRowsForId(100)[0]].Size);
            Assert.IsTrue(delta.TryGetPathForFrn(highId, out var path));
            Assert.AreEqual(@"C:\Projects\high.txt", path);
        });
    }

    [TestMethod]
    public void HardLinks_ReplayRenameAndDeletePreserveTheOtherNames()
    {
        using var fixture = Build();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            DeltaLinkOps.AddLink(delta, 100, 2, "one.txt", FileRecordFlags.None);
            DeltaLinkOps.AddLink(delta, 100, 2, "ONE.TXT", FileRecordFlags.None);
            DeltaLinkOps.AddLink(delta, 100, 3, "one.txt", FileRecordFlags.None);
            DeltaLinkOps.AddLink(delta, 100, 2, "two.txt", FileRecordFlags.None);
            Assert.AreEqual(3, delta.VisibleAddedCount);

            Assert.IsTrue(DeltaLinkOps.RemoveLinkForRename(delta, 100, 2, "one.txt"));
            DeltaLinkOps.AddLink(delta, 100, 2, "renamed.txt", FileRecordFlags.None);
            Assert.IsTrue(DeltaLinkOps.RemoveLink(delta, 100, 2, "two.txt"));
            Assert.IsFalse(DeltaLinkOps.RemoveLink(delta, 100, 2, "two.txt"));
            DeltaLinkOps.UpdateMetadata(delta, 100, 777, 10, 20, 30);
            DeltaLinkOps.UpdateFlags(delta, 100, FileRecordFlags.ReadOnly);

            var live = delta.Added.Where(r => !r.Removed).ToArray();
            Assert.HasCount(2, live);
            Assert.IsTrue(live.Any(r => r.ParentFrn == 3 && r.Name == "one.txt"));
            Assert.IsTrue(live.Any(r => r.ParentFrn == 2 && r.Name == "renamed.txt"));
            Assert.IsTrue(live.All(r => r.Size == 777 && r.LastWrite == 20
                && r.Flags == (ushort)FileRecordFlags.ReadOnly));
            Assert.IsTrue(delta.Added.Where(r => r.Removed).All(r => r.Size == 0));
        });

        Assert.IsTrue(fixture.Index.Compact(fixture.Path));
        var records = fixture.Index.ToStore().Records.Where(r => r.Id == 100).ToArray();
        Assert.HasCount(2, records);
        Assert.IsTrue(records.All(r => r.Size == 777));
    }

    [TestMethod]
    public void UpsertReplacementAndResurrection_AreVisibleThroughTheSameLookup()
    {
        using var fixture = Build();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            delta.Upsert(100, 2, "old.txt", FileRecordFlags.None, 1, 0, 0, 0);
            delta.Upsert(100, 3, "new.txt", FileRecordFlags.None, 2, 0, 0, 0);
            DeltaLinkOps.UpdateMetadata(delta, 100, 999, 1, 2, 3);
            Assert.IsTrue(delta.TryGetPathForFrn(100, out var path));
            Assert.AreEqual(@"C:\Other\new.txt", path);
            Assert.AreEqual(999L, delta.Added.Single().Size);

            delta.Remove(100);
            Assert.IsFalse(delta.TryGetPathForFrn(100, out _));
            Assert.IsTrue(delta.TryGetHistoricalPathForFrn(100, out var historical));
            Assert.AreEqual(path, historical);
            delta.Upsert(100, 2, "revived.txt", FileRecordFlags.None, 3, 0, 0, 0);
            Assert.AreEqual(1, delta.AddedRowsForId(100).Length);
            DeltaLinkOps.AddLink(delta, 100, 2, "REVIVED.TXT", FileRecordFlags.None);
            Assert.HasCount(1, delta.Added);
        });
    }

    [TestMethod]
    public void DirectoryRenameAndCascade_SkipRemovedRowsInTheIdBucket()
    {
        using var fixture = Build();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            DeltaLinkOps.AddLink(delta, 100, 2, "old", FileRecordFlags.Directory);
            DeltaLinkOps.AddLink(delta, 101, 100, "child.txt", FileRecordFlags.None);
            Assert.IsTrue(DeltaLinkOps.RemoveLinkForRename(delta, 100, 2, "old"));
            DeltaLinkOps.AddLink(delta, 100, 3, "new", FileRecordFlags.Directory);

            Assert.IsTrue(delta.TryGetPathForFrn(101, out var path));
            Assert.AreEqual(@"C:\Other\new\child.txt", path);
            Assert.AreEqual("new", delta.FindAddedDirectory(100)?.Name);
            Assert.IsTrue(DeltaLinkOps.RemoveLink(delta, 100, 3, "new"));
            Assert.IsNull(delta.FindAddedDirectory(100));
            Assert.IsFalse(delta.TryGetPathForFrn(101, out _));
        });
    }

    [TestMethod]
    public void OutOfOrderParent_HealsBeforeDuplicateAndRemovalMatching()
    {
        using var fixture = Build();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            DeltaLinkOps.AddLink(delta, 101, 100, "child.txt", FileRecordFlags.None);
            Assert.IsFalse(DeltaLinkOps.RemoveLink(delta, 101, 100, "child.txt"));
            DeltaLinkOps.AddLink(delta, 100, 2, "parent", FileRecordFlags.Directory);
            DeltaLinkOps.AddLink(delta, 101, 100, "CHILD.TXT", FileRecordFlags.None);
            Assert.AreEqual(1, delta.AddedRowsForId(101).Length);
            Assert.IsTrue(DeltaLinkOps.RemoveLink(delta, 101, 100, "child.txt"));
        });
    }

    [TestMethod]
    public void AttributeOverrides_RemainResolvableForMetadataAndParentLookup()
    {
        using var fixture = Build();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            DeltaLinkOps.UpdateFlags(delta, 4, FileRecordFlags.Hidden);
            DeltaLinkOps.UpdateFlags(delta, 2, FileRecordFlags.Directory | FileRecordFlags.Hidden);
            Assert.IsTrue(delta.TryGetPathForFrn(4, out var path));
            Assert.AreEqual(@"C:\Projects\base.txt", path);
            Assert.IsTrue(delta.TryFindLiveBaseDirectory(2, out var parent));
            Assert.AreEqual(snapshot.FirstRowForId(2), parent);
        });
    }

    [TestMethod]
    public void UnchangedAttributes_PreserveMetadataWithoutCreatingAFullOverride()
    {
        using var fixture = Build();
        fixture.Index.Mutate((snapshot, delta) =>
        {
            DeltaLinkOps.UpdateMetadata(delta, 4, 123, 1, 2, 3);
            DeltaLinkOps.UpdateFlags(delta, 4, FileRecordFlags.None);
            DeltaLinkOps.UpdateFlags(delta, 1, FileRecordFlags.Directory);

            Assert.IsEmpty(delta.BaseOverrides);
            Assert.AreEqual(123L, delta.MetadataOf(snapshot.FirstRowForId(4)).Size);
            Assert.IsTrue(delta.TryGetPathForFrn(4, out var path));
            Assert.AreEqual(@"C:\Projects\base.txt", path);
        });
    }
}
