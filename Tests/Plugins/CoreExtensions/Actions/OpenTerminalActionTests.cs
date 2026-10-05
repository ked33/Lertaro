using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.CoreExtensions.Actions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

[TestClass]
public sealed class OpenTerminalActionTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize() => _directory = Directory.CreateTempSubdirectory("lertaro-terminal-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CreateStartInfo_PreservesDirectoryAndElevation(bool admin)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_directory, "中文 space & ' $value [1]")).FullName;
        var start = TerminalLauncher.CreateStartInfo("", directory, admin);

        Assert.AreEqual("pwsh.exe", start.FileName);
        Assert.AreEqual(directory, start.WorkingDirectory);
        Assert.IsTrue(start.UseShellExecute);
        Assert.AreEqual(admin ? "runas" : "", start.Verb);
        Assert.AreEqual("", start.Arguments);
        CollectionAssert.AreEqual(
            new[] { "-WorkingDirectory", directory },
            start.ArgumentList.ToArray());
    }

    [TestMethod]
    public void CreateStartInfo_RelativeDirectory_UsesInlineContext()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_directory, "child folder")).FullName;
        var start = TerminalLauncher.CreateStartInfo("\"child folder\"", _directory, false);

        Assert.AreEqual(directory, start.WorkingDirectory);
        Assert.AreEqual(directory, start.ArgumentList.Last());
    }

    [TestMethod]
    public void CreateStartInfo_File_UsesParentDirectory()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_directory, "child")).FullName;
        var file = Path.Combine(directory, "test.txt");
        File.WriteAllText(file, "");

        var start = TerminalLauncher.CreateStartInfo(file, _directory, true);

        Assert.AreEqual(directory, start.WorkingDirectory);
        Assert.AreEqual(directory, start.ArgumentList.Last());
    }

    [TestMethod]
    public void CreateStartInfo_MissingDirectory_DoesNotSilentlyLaunchElsewhere()
    {
        Assert.ThrowsExactly<DirectoryNotFoundException>(() =>
            TerminalLauncher.CreateStartInfo("missing", _directory, false));
    }

    [TestMethod]
    [DataRow("pwsh", "pwsh", "pwsha")]
    [DataRow("PWSH", "pwsh", "pwsha")]
    public void RegisteredActions_BarePrefixOffersNormalThenAdmin(string query, string normal, string admin)
    {
        // These are the same exact-token and completion rules used by KeywordMatcher.
        var matches = new CoreExtensionsPlugin().GetActions()
            .Where(a => a.Keywords.Any(k => TriggerWord.TryMatch(query, k, out _) || TriggerWord.IsTypedPrefixOf(query, k)))
            .ToArray();

        CollectionAssert.AreEqual(new[] { normal, admin }, matches.Select(a => a.Keywords.Single()).ToArray());
        var results = new ISearchResult[] { new FakeResult { ContextDirectory = _directory, IsDir = true } };
        foreach (var action in matches)
        {
            Assert.IsTrue(action.IsVisibleInSearch(results, SearchWindowType.Inline));
            Assert.IsFalse(action.IsVisibleInSearch(results, SearchWindowType.Main));
            Assert.IsFalse(action.IsVisibleInSearch(results, SearchWindowType.Quick));
            Assert.IsTrue(action.IsVisibleInMenu(results, SearchWindowType.Main));
            Assert.IsTrue(action.CanExecute(results));
            Assert.IsFalse(action.CanExecute(Array.Empty<ISearchResult>()));
            Assert.IsFalse(action.CanExecute(new ISearchResult[] { new FakeResult() }));
        }
    }

    [TestMethod]
    [DataRow("pwsha")]
    public void RegisteredActions_AdminKeywordDoesNotMatchNormalAction(string query)
    {
        var matches = new CoreExtensionsPlugin().GetActions()
            .Where(a => a.Keywords.Any(k => TriggerWord.TryMatch(query, k, out _) || TriggerWord.IsTypedPrefixOf(query, k)))
            .ToArray();

        Assert.HasCount(1, matches);
        Assert.AreEqual(query, matches[0].Keywords.Single());
    }

    private sealed class FakeResult : ISearchResult
    {
        public string Name => "";
        public string FullPath => "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication => false;
    }
}
