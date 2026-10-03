using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

// Which folder a "locate in Explorer" should end up showing. Only the decision is covered: the rest of
// the call is ShellWindows COM against a live Explorer window.
[TestClass]
public sealed class ExplorerLocateHelperTests
{
    [TestMethod]
    [DataRow(@"C:\folder\file.txt", @"C:\folder")]
    [DataRow(@"C:\folder\sub", @"C:\folder")]
    [DataRow(@"C:\folder\sub\", @"C:\folder")]
    [DataRow(@"C:\folder\sub/", @"C:\folder")]
    [DataRow(@"C:\脚本", @"C:\")]
    [DataRow(@"\\server\share\sub\", @"\\server\share")]
    public void ResolveContainingFolder_FilesAndFoldersResolveToTheirParent(string path, string expected) =>
        Assert.AreEqual(expected, ExplorerLocateHelper.ResolveContainingFolder(path));

    [TestMethod]
    [DataRow(@"C:\")]
    [DataRow("shell:AppsFolder")]
    [DataRow("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    public void ResolveContainingFolder_NothingToShow_ReportsNoFolder(string path) =>
        // A drive root and a virtual shell token have no containing folder, which is what routes them to
        // the shell-locate fallback instead of pretending there was one to open. Null or empty, exactly:
        // only "no folder" is promised, not which spelling of it comes back.
        Assert.IsTrue(
            string.IsNullOrEmpty(ExplorerLocateHelper.ResolveContainingFolder(path)),
            $"'{path}' resolved to a containing folder");
}
