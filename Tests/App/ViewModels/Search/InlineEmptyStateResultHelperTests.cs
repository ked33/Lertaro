using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class InlineEmptyStateResultHelperTests
{
    [TestMethod]
    public void Build_ShowsCurrentFoldersBeforeHistory()
    {
        var result = InlineEmptyStateResultHelper.Build(
            new[] { @"C:\recent" },
            new[] { @"C:\opened" },
            "Folder history",
            "Current folders");

        Assert.AreEqual("SectionHeader", result[0].ResultKind);
        Assert.AreEqual("Current folders", result[0].Name);
        Assert.AreEqual(@"C:\opened", result[1].FullPath);
        Assert.AreEqual("SectionHeader", result[2].ResultKind);
        Assert.AreEqual("Folder history", result[2].Name);
        Assert.AreEqual(@"C:\recent", result[3].FullPath);
        Assert.AreEqual("OpenedFolder", result[3].ResultKind);
        Assert.IsFalse(result[3].IsJumpToExplorerPath, "Each history row needs its own numeric shortcut.");
    }

    [TestMethod]
    public void Build_KeepsEveryReportedFolderInTheReportedOrder()
    {
        var result = InlineEmptyStateResultHelper.Build(
            new[] { @"C:\recent\" },
            new[] { @"C:\focused", @"C:\recent", @"C:\CURRENT\", @"C:\other", @"C:\other\", "" },
            "Last directory",
            "Opened folders");

        CollectionAssert.AreEqual(
            new[] { @"C:\focused", @"C:\recent", @"C:\CURRENT", @"C:\other" },
            result.Where(r => r.ResultKind == "OpenedFolder").Select(r => r.FullPath).ToArray());
        Assert.AreEqual(1, result.Count(r => r.IsSearchSectionHeader), "Current folders must not reappear in history.");
    }

    [TestMethod]
    public void Build_IndexesHeadersAndRowsSequentially()
    {
        var result = InlineEmptyStateResultHelper.Build(
            Array.Empty<string>(),
            new[] { @"C:\one", @"C:\two" },
            "Last directory",
            "Opened folders");

        for (var index = 0; index < result.Count; index++)
            Assert.AreEqual(index, result[index].Index);
    }

    [TestMethod]
    public void Build_FiltersCurrentAndDuplicatesBeforeTakingFiveInMruOrder()
    {
        var result = InlineEmptyStateResultHelper.Build(
            new[] { @"C:\OPEN\", @"C:\dialog", @"C:\five", @"c:\five\", @"C:\four",
                @"C:\three", @"C:\two", @"C:\one", @"C:\older" },
            new[] { @"C:\open" }, "History", "Current", @"C:\dialog\");

        CollectionAssert.AreEqual(new[] { @"C:\open", @"C:\five", @"C:\four", @"C:\three", @"C:\two", @"C:\one" },
            result.Where(r => !r.IsSearchSectionHeader).Select(r => r.FullPath).ToArray());
    }

    [TestMethod]
    public void Build_HiddenCurrentGroupStillExcludesThoseFoldersFromHistory()
    {
        var result = InlineEmptyStateResultHelper.Build(new[] { @"C:\open", @"D:\old" },
            new[] { @"C:\open" }, "History", "Current", showOpenedFolders: false);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("History", result[0].Name);
        Assert.AreEqual(@"D:\old", result[1].FullPath);
    }

    [TestMethod]
    public void Build_NormalizesRootsAndUncWithoutProbingTheFilesystem()
    {
        var result = InlineEmptyStateResultHelper.Build(
            new[] { @"c:\", @"\\server\share\folder\", @"\\SERVER\share\folder", "relative", "", "shell:Downloads" },
            new[] { @"C:\" }, "History", "Current");
        CollectionAssert.AreEqual(new[] { @"C:\", @"\\server\share\folder" },
            result.Where(r => !r.IsSearchSectionHeader).Select(r => r.FullPath).ToArray());
    }
}
