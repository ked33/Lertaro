using Lertaro.Core.IndexV2.Search;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Core.Tests.IndexV2.Search;

[TestClass]
public sealed class FileTypeFilterSearchTests
{
    [TestCleanup]
    public void Reset() => SearchContext.FileTypeFilter = null;

    [TestMethod]
    [DataRow("")]
    [DataRow("report")]
    [DataRow("Z:\\")]
    [DataRow("Z:\\report")]
    public void FilterRunsBeforeResultLimit_ForEmptyNameAndPathQueries(string query)
    {
        var records = new List<FileRecord> { LiveIndexFixture.Root() };
        for (var i = 2; i < 1502; i++)
            records.Add(new FileRecord((ulong)i, 1, $"report{i:D4}.txt", FileRecordFlags.None));
        records.Add(new FileRecord(1502, 1, "report-folder.lnk", FileRecordFlags.Directory));
        records.Add(new FileRecord(1503, 1, "report-shortcut.LNK", FileRecordFlags.None));
        using var fixture = LiveIndexFixture.Build("Z", records);
        SearchContext.FileTypeFilter = new FileTypeFilter("*.lnk");
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, query, 1, results.Add, CancellationToken.None);

        Assert.HasCount(1, results);
        Assert.AreEqual("report-shortcut.LNK", results[0].Name);
        Assert.IsFalse(results[0].IsDir);
    }

    [TestMethod]
    public void FilterUsesRenamedAndNewEntriesFromTheDelta()
    {
        using var fixture = LiveIndexFixture.Build("Z", [LiveIndexFixture.Root(),
            new FileRecord(2, 1, "old.txt", FileRecordFlags.None), new FileRecord(3, 1, "gone.lnk", FileRecordFlags.None)]);
        fixture.Index.Mutate((_, delta) =>
        {
            delta.Upsert(2, 1, "renamed.lnk", FileRecordFlags.None, 0, 0, 0, 0);
            delta.Upsert(3, 1, "gone.txt", FileRecordFlags.None, 0, 0, 0, 0);
            delta.Upsert(100, 1, "new.lnk", FileRecordFlags.None, 0, 0, 0, 0);
        });
        SearchContext.FileTypeFilter = new FileTypeFilter("*.lnk");
        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, "", 10, results.Add, CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "renamed.lnk", "new.lnk" }, results.Select(r => r.Name).ToArray());
    }

    [TestMethod]
    public void UnfilteredEmptyQueryStillReturnsNothing()
    {
        using var fixture = LiveIndexFixture.Build("Z", [LiveIndexFixture.Root(), new FileRecord(2, 1, "a.lnk", FileRecordFlags.None)]);
        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, "", 10, results.Add, CancellationToken.None);
        Assert.IsEmpty(results);
    }

    [TestMethod]
    public async Task ConcurrentFiltersDoNotLeakBetweenSearches()
    {
        using var fixture = LiveIndexFixture.Build("Z", [LiveIndexFixture.Root(),
            new FileRecord(2, 1, "a.lnk", FileRecordFlags.None), new FileRecord(3, 1, "b.pdf", FileRecordFlags.None)]);
        async Task<string> Search(string rule)
        {
            SearchContext.FileTypeFilter = new FileTypeFilter(rule);
            await Task.Yield();
            var results = new List<SearchResult>();
            IndexV2Searcher.SearchStreaming(fixture.Index, "", 10, results.Add, CancellationToken.None);
            return results.Single().Name;
        }
        var found = await Task.WhenAll(Search("*.lnk"), Search("*.pdf"));
        CollectionAssert.AreEqual(new[] { "a.lnk", "b.pdf" }, found);
        Assert.IsNull(SearchContext.FileTypeFilter);
    }
}
