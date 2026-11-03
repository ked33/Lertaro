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

    [TestMethod]
    public void AFileHitStopsScanningAndDisposesTheShellEnumerator()
    {
        var disposed = false;
        IEnumerable<(string Path, bool IsFolder)> Matches()
        {
            try
            {
                yield return (@"C:\Work\Report.txt", false);
                throw new AssertFailedException("A file hit must not request another Shell item.");
            }
            finally { disposed = true; }
        }
        Assert.IsNull(ExplorerFolderPathReader.ResolveUniqueFolder(Matches(),
            _ => throw new AssertFailedException("A file hit must not probe the filesystem.")));
        Assert.IsTrue(disposed);
    }

    [TestMethod]
    public void AnAmbiguousFolderHitStopsAtTheSecondMatchAndDisposesTheEnumerator()
    {
        var disposed = false;
        IEnumerable<(string Path, bool IsFolder)> Matches()
        {
            try
            {
                yield return (@"C:\Work\Report", true);
                yield return (@"C:\Work\Report.txt", false);
                throw new AssertFailedException("Ambiguity must stop further Shell enumeration.");
            }
            finally { disposed = true; }
        }
        Assert.IsNull(ExplorerFolderPathReader.ResolveUniqueFolder(Matches(),
            _ => throw new AssertFailedException("An ambiguous hit must not probe the filesystem.")));
        Assert.IsTrue(disposed);
    }

    [TestMethod]
    public void ACancelledHoverProbeReturnsNoFolder()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.IsNull(ExplorerFolderPathReader.Read(IntPtr.Zero, IntPtr.Zero, "Report", cancelled.Token));
    }

    [TestMethod]
    [DataRow(@"C:\Work", @"C:\Work")]
    [DataRow(@"Address: C:\Work\New folder", @"C:\Work\New folder")]
    [DataRow(@"地址: D:\中文目录", @"D:\中文目录")]
    [DataRow(@"地址：D:\中文目录", @"D:\中文目录")]
    [DataRow(@"Adresse : \\server\share\folder", @"\\server\share\folder")]
    [DataRow(@"\\server\share\folder", @"\\server\share\folder")]
    [DataRow(@"Address: \\?\C:\Long folder", @"\\?\C:\Long folder")]
    public void AddressBarsKeepExplicitLocalAndNetworkPaths(string text, string expected) =>
        Assert.AreEqual(expected, ExplorerFolderPathReader.ParseAddressBarPath(text));

    [TestMethod]
    [DataRow("")]
    [DataRow("Home")]
    [DataRow("地址: 下载")]
    [DataRow("Address: This PC")]
    [DataRow(@"Address: C:relative")]
    [DataRow(@"Address: \rooted-but-not-qualified")]
    [DataRow("Address: shell:Downloads")]
    [DataRow(@"shell:C:\Work")]
    [DataRow(@"C:\Work\::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    public void AddressBarsNeverGuessVirtualOrRelativeLocations(string text) =>
        Assert.IsNull(ExplorerFolderPathReader.ParseAddressBarPath(text));
}
