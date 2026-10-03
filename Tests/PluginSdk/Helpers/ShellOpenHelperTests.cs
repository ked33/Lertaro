using Lertaro.PluginSdk.Helpers;

namespace Lertaro.PluginSdk.Tests.Helpers;

// Cover guards and Explorer request construction without launching processes or opening desktop windows.
[TestClass]
public sealed class ShellOpenHelperTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void TryOpenFolder_NoFolder_IsRefusedWithoutCallingTheShell(string? folderPath) =>
        Assert.IsFalse(ShellOpenHelper.TryOpenFolder(folderPath));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void TryRevealInFolder_NoItem_IsRefusedWithoutCallingTheShell(string? itemPath) =>
        Assert.IsFalse(ShellOpenHelper.TryRevealInFolder(itemPath));

    [TestMethod]
    [DataRow(@"D:\D-Software\Weixin\Weixin.exe", @"D:\D-Software\Weixin\Weixin.exe")]
    [DataRow(@"D:\D-Software\Photoshop CS6\Photoshop.exe", @"D:\D-Software\Photoshop CS6\Photoshop.exe")]
    [DataRow(@"D:\文档\键盘 NJ68.md", @"D:\文档\键盘 NJ68.md")]
    [DataRow(@"D:\Client、Server\脚本\测试.js", @"D:\Client、Server\脚本\测试.js")]
    [DataRow(@"D:\a,b & c\item.txt", @"D:\a,b & c\item.txt")]
    [DataRow(@"D:\a,b\item.txt", @"D:\a,b\item.txt")]
    [DataRow(@"D:\a=b\item.txt", @"D:\a=b\item.txt")]
    [DataRow(@"D:\脚本\", @"D:\脚本")]
    [DataRow(@"D:\脚本/", @"D:\脚本")]
    [DataRow(@"D:\", @"D:\")]
    [DataRow(@"\\server\shared folder\脚本\", @"\\server\shared folder\脚本")]
    [DataRow(@"\\server\shared folder\", @"\\server\shared folder")]
    [DataRow(@"D:\target.lnk", @"D:\target.lnk")]
    public void BuildRevealStartInfo_QuotesTheItemItselfUsingExplorerSyntax(string path, string expected)
    {
        var info = ShellOpenHelper.BuildRevealStartInfo(path);

        Assert.AreEqual(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), info.FileName);
        Assert.IsTrue(Path.IsPathFullyQualified(info.FileName));
        Assert.IsFalse(info.UseShellExecute);
        Assert.AreEqual($"/select,\"{expected}\"", info.Arguments);
        Assert.IsEmpty(info.ArgumentList);
    }

    [TestMethod]
    public void BuildRevealStartInfo_ExpandsEnvironmentVariablesBeforeSelecting()
    {
        var info = ShellOpenHelper.BuildRevealStartInfo(@"  %SystemRoot%\explorer.exe  ");
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        Assert.AreEqual($"/select,\"{expected}\"", info.Arguments);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("shell:Downloads")]
    [DataRow("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    [DataRow("D:\\bad\"name.txt")]
    [DataRow("D:\\bad\0name.txt")]
    public void BuildRevealStartInfo_RefusesInvalidOrNonFileSystemTargets(string? path) =>
        Assert.Throws<ArgumentException>(() => ShellOpenHelper.BuildRevealStartInfo(path!));
}
