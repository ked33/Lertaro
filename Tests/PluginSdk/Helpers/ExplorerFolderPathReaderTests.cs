using Lertaro.PluginSdk.Helpers;

namespace Lertaro.PluginSdk.Tests.Helpers;

[TestClass]
public sealed class ExplorerFolderPathReaderTests
{
    [TestMethod]
    public void AUniquePhysicalFolderKeepsItsActualPath()
    {
        var path = ExplorerFolderPathReader.ResolveUniqueFolder([(@"C:\Users\Test\Downloads", true)], _ => true);
        Assert.AreEqual(@"C:\Users\Test\Downloads", path);
    }

    [TestMethod]
    public void FilesAndAmbiguousDisplayedNamesNeverBecomeHoveredFolders()
    {
        Assert.IsNull(ExplorerFolderPathReader.ResolveUniqueFolder([], _ => true));
        Assert.IsNull(ExplorerFolderPathReader.ResolveUniqueFolder([(@"C:\Work\file.txt", false)], _ => true));
        // Hiding extensions can give a folder and a regular file the same displayed name.
        Assert.IsNull(ExplorerFolderPathReader.ResolveUniqueFolder(
            [(@"C:\Work\Report", true), (@"C:\Work\Report.txt", false)], _ => true));
        // Search/Home views can display identically named directories from different parents.
        Assert.IsNull(ExplorerFolderPathReader.ResolveUniqueFolder(
            [(@"C:\A\Report", true), (@"C:\B\Report", true)], _ => true));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("relative")]
    [DataRow("shell:Downloads")]
    [DataRow("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    public void NonFilesystemTargetsAreRejected(string path) =>
        Assert.IsNull(ExplorerFolderPathReader.ResolveUniqueFolder([(path, true)], _ => true));

    [TestMethod]
    public void MissingFoldersAndShellArchiveFoldersAreRejected() =>
        Assert.IsNull(ExplorerFolderPathReader.ResolveUniqueFolder([(@"C:\Work\archive.zip", true)], _ => false));

    [TestMethod]
    public void AnIncompleteScanCannotConfirmAUniquelyNamedFolder()
    {
        static IEnumerable<(string Path, bool IsFolder)> Incomplete()
        {
            yield return (@"C:\Work\Report", true);
            throw new TimeoutException();
        }
        Assert.ThrowsExactly<TimeoutException>(() => ExplorerFolderPathReader.ResolveUniqueFolder(Incomplete(), _ => true));
    }
}
