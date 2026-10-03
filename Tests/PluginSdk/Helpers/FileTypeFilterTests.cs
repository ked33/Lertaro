using Lertaro.PluginSdk.Helpers;

namespace Lertaro.PluginSdk.Tests.Helpers;

[TestClass]
public sealed class FileTypeFilterTests
{
    [TestMethod]
    public void ExtensionFilter_ExcludesDirectoriesAndMatchesCaseInsensitively()
    {
        var filter = new FileTypeFilter("lnk; *.pdf");
        Assert.IsTrue(filter.Matches("Example.LNK", false));
        Assert.IsTrue(filter.Matches("Example.pdf", false));
        Assert.IsFalse(filter.Matches("Example.lnk", true));
        Assert.IsFalse(filter.Matches("Example.lnk.exe", false));
    }

    [TestMethod]
    public void ExplicitFolderAndFileRulesRetainTheirMeaning()
    {
        Assert.IsTrue(new FileTypeFilter("folder; *.lnk").Matches("Docs", true));
        Assert.IsTrue(new FileTypeFilter("file").Matches("LICENSE", false));
        Assert.IsFalse(new FileTypeFilter("file").Matches("Docs", true));
        Assert.IsFalse(new FileTypeFilter("").Matches("Example.lnk", false));
        Assert.IsFalse(new FileTypeFilter("*.*").Matches("LICENSE", false));
    }
}
