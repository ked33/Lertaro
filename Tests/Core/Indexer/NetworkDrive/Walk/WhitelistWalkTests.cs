using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

[TestClass]
public sealed class WhitelistWalkTests
{
    [TestMethod]
    public void ScanAndResume_ReachWhitelistUnderExcludedParentWithoutIncludingSiblings()
    {
        using var dir = new TempDirectory();
        var excluded = Path.Combine(dir.Path, ".excluded");
        var allowed = Directory.CreateDirectory(Path.Combine(excluded, "allowed")).FullName;
        var cache = Directory.CreateDirectory(Path.Combine(allowed, ".cache")).FullName;
        File.WriteAllText(Path.Combine(cache, "keep.tmp"), "x");
        File.WriteAllText(Path.Combine(excluded, "sibling.txt"), "x");
        File.WriteAllText(Path.Combine(dir.Path, ".gitignore"), ".excluded\n*.tmp\n");
        File.WriteAllText(Path.Combine(allowed, ".ignore"), "*\n");

        var without = Scan(dir.Path, []);
        Assert.IsFalse(without.Records.Any(r => r.Name == "keep.tmp"));

        var withWhitelist = Scan(dir.Path, [allowed], without);
        Assert.IsTrue(withWhitelist.Records.Any(r => r.Name == ".excluded"), "The tree needs the ancestor row.");
        Assert.IsTrue(withWhitelist.Records.Any(r => r.Name == "keep.tmp"));
        Assert.IsFalse(withWhitelist.Records.Any(r => r.Name == "sibling.txt"));

        var rules = ExclusionRuleSet.From(new UserSettings { WhitelistedPaths = [allowed] });
        Assert.IsTrue(rules.IsExcludedPath(excluded, true), "Traversal must not reveal an excluded ancestor in search.");
        Assert.IsFalse(rules.IsExcludedPath(Path.Combine(cache, "keep.tmp"), false));

        var removed = Scan(dir.Path, [], withWhitelist);
        Assert.IsFalse(removed.Records.Any(r => r.Name == "keep.tmp"));
    }

    [TestMethod]
    public void Whitelist_DoesNotDisableTraversalDepthLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), "lertaro-whitelist-depth");
        var filter = WalkFilter.Create(root, new WalkOptions([], [".*"], [], 1, 1, false, [root]));
        Assert.IsFalse(filter.ShouldDescend(Path.Combine(root, "a", "b"), FileAttributes.Directory, 2, NetworkIgnoreRuleSet.Empty));
    }

    private static FileRecordStore Scan(string root, string[] whitelist, FileRecordStore? previous = null)
    {
        var store = new FileRecordStore { RootId = 1 };
        store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        var builder = new TreeBuilder(store, root, root,
            new WalkOptions([], [".*"], [], 0, 1, true, whitelist),
            CancellationToken.None, (_, _) => { }, diffBaseline: TreeDiffBaseline.From(previous), recheckExclusions: previous != null);
        builder.RegisterDirectoryIndices(0, store.Records);
        builder.Run();
        return store;
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
