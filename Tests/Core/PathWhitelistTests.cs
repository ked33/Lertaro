using Lertaro.Core.Indexer.NetworkDrive;

namespace Lertaro.Core.Tests;

[TestClass]
public sealed class PathWhitelistTests
{
    [TestMethod]
    public void DefaultSettings_KeepDotDirectoriesExcluded()
    {
        var settings = new UserSettings();
        Assert.IsEmpty(settings.WhitelistedPaths);
        Assert.IsTrue(ExclusionRuleSet.From(settings).IsExcludedPath(@"C:\work\.claude\settings.json", false));
    }

    [TestMethod]
    public void NullFromConfiguration_IsTreatedAsAnEmptyWhitelist()
    {
        var settings = new UserSettings { WhitelistedPaths = null! };
        Assert.IsEmpty(settings.WhitelistedPaths);
        Assert.IsTrue(ExclusionRuleSet.From(settings).IsExcludedPath(@"C:\work\.claude\settings.json", false));
    }

    [TestMethod]
    public void EquivalentAndNestedPaths_CompileToOneRoot()
    {
        var whitelist = new PathWhitelist([@"C:\work\.claude\cache", @"C:\work\.claude\", @"c:/work/tmp/../.claude"]);
        Assert.HasCount(1, whitelist.Roots);
        Assert.IsTrue(whitelist.Contains(@"C:\work\.claude\config.json"));
    }

    [TestMethod]
    [DataRow(@"C:\work\.claude", true, false)]
    [DataRow(@"C:\work\.claude\", true, false)]
    [DataRow(@"c:/WORK/.CLAUDE/.cache/node_modules/file.tmp", false, false)]
    [DataRow(@"C:\work\.claude-backup\file.txt", false, true)]
    [DataRow(@"C:\other\.claude\file.txt", false, true)]
    [DataRow(@"C:\work\.grok\file.txt", false, true)]
    public void Whitelist_OverridesAllExclusionsOnlyInsideExactDirectory(string path, bool isDirectory, bool excluded)
    {
        var settings = new UserSettings
        {
            ExcludedPaths = [@"C:\work"],
            WhitelistedPaths = [@"C:\work\.claude"],
            IgnoredPathGlobs = [".*", "node_modules"],
            IgnoredPathRegexes = [@"\.tmp$"]
        };
        var rules = ExclusionRuleSet.From(settings);
        // Populate the ancestor cache before checking the exception.
        Assert.IsTrue(rules.IsExcludedPath(@"C:\other\.claude\other.txt", false));
        Assert.AreEqual(excluded, rules.IsExcludedPath(path, isDirectory));
    }

    [TestMethod]
    public void Whitelist_UsesAbsolutePathEvenForScopedSearch()
    {
        var settings = new UserSettings { WhitelistedPaths = [@"C:\work\.claude"] };
        var rules = ExclusionRuleSet.From(settings, @"C:\work");
        Assert.IsFalse(rules.IsExcludedPath(@"C:\work\.claude\.cache\a.txt", false));
        Assert.IsTrue(rules.IsExcludedPath(@"C:\work\project\.claude\a.txt", false));
    }

    [TestMethod]
    public void EnvironmentVariablesAndQuotes_AreExpandedOnce()
    {
        var whitelist = new PathWhitelist([" \"%USERPROFILE%/.claude/\" "]);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        Assert.IsTrue(whitelist.Contains(root));
        Assert.IsTrue(whitelist.Contains(Path.Combine(root, "file.txt")));
        Assert.IsFalse(whitelist.Contains(root + "-backup"));
    }

    [TestMethod]
    [DataRow(@"relative\folder")]
    [DataRow(@"C:relative")]
    [DataRow(@"C:\work\*")]
    [DataRow(@"C:\work\file:stream")]
    [DataRow(@"\\server")]
    [DataRow(@"\\.\C:\work")]
    [DataRow(@"\\?\C:\work")]
    public void InvalidPath_CannotBroadenWhitelist(string path)
    {
        Assert.IsFalse(PathWhitelist.TryNormalizeDirectory(path, out _));
        Assert.IsTrue(new PathWhitelist([path]).IsEmpty);
    }

    [TestMethod]
    public void UncAndDriveRoots_RespectBoundaries()
    {
        var whitelist = new PathWhitelist([@"\\server\share\.claude", @"D:\"]);
        Assert.IsTrue(whitelist.Contains(@"\\SERVER\share\.claude\file.txt"));
        Assert.IsFalse(whitelist.Contains(@"\\server\share\.claude2\file.txt"));
        Assert.IsTrue(whitelist.HasDescendant(@"\\server\share\"));
        Assert.IsFalse(whitelist.HasDescendant(@"\\server\share2"));
        Assert.IsTrue(whitelist.Contains(@"D:\a.txt"));
        Assert.IsFalse(whitelist.Contains(@"E:\a.txt"));
    }

    [TestMethod]
    public void Fingerprint_ChangesOnWhitelistChangesButPreservesEmptyCompatibility()
    {
        var original = IndexerHelper.ComputeExclusionFingerprint([], [".*"], []);
        Assert.AreEqual(original, IndexerHelper.ComputeExclusionFingerprint([], [".*"], [], []));
        var changed = IndexerHelper.ComputeExclusionFingerprint([], [".*"], [], [@"C:\work\.claude"]);
        Assert.AreNotEqual(original, changed);
        Assert.AreEqual(changed, IndexerHelper.ComputeExclusionFingerprint([], [".*"], [], [@"c:\WORK\.claude"]));
    }
}
