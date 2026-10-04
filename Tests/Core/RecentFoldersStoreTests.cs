using System.Text.Json;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Core.Tests;

[TestClass]
public sealed class RecentFoldersStoreTests
{
    private string _directory = null!;
    private string StorePath => Path.Combine(_directory, "recent-folders.json");
    [TestInitialize] public void Initialize() => _directory = Path.Combine(Path.GetTempPath(), "RecentFoldersTests_" + Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [TestMethod]
    public void VisitsAreUniqueNewestFirstAndBounded()
    {
        using var store = new RecentFoldersStore(StorePath, new() { Capacity = 2 });
        store.Record(@"C:\A", 10);
        store.Record(@"C:\B", 20);
        store.Record(@"c:/a/", 30);
        store.Record(@"C:\C", 40);
        var entries = store.GetSnapshot().Entries;
        CollectionAssert.AreEqual(new[] { @"C:\C", @"c:\a" }, entries.Select(e => e.Path).ToArray());
        Assert.AreEqual(30L, entries[1].OpenedUtcTicks);
        store.Record(@"C:\C", 35);
        Assert.AreEqual(40L, store.GetSnapshot().Entries[0].OpenedUtcTicks);
    }

    [TestMethod]
    public void DisabledStoreDoesNotUpdateExistingEntries()
    {
        using var store = new RecentFoldersStore(StorePath, new(), () => 15);
        store.Record(@"C:\A", 10);
        store.ApplySettings(new() { Enabled = false });
        store.Record(@"C:\A", 20);
        store.Record(@"C:\B", 30);
        Assert.HasCount(1, store.GetSnapshot().Entries);
        Assert.AreEqual(10L, store.GetSnapshot().Entries[0].OpenedUtcTicks);
    }

    [TestMethod]
    public void ClearRejectsEarlierInFlightSamplesButAllowsANewVisit()
    {
        using var store = new RecentFoldersStore(StorePath, new(), () => 50);
        store.Record(@"C:\A", 10);
        store.Remove();
        store.Record(@"C:\B", 40);
        Assert.HasCount(0, store.GetSnapshot().Entries);
        store.Record(@"C:\A", 60);
        Assert.HasCount(1, store.GetSnapshot().Entries);
        store.Flush();
        using var reopened = new RecentFoldersStore(StorePath, new());
        Assert.AreEqual(60L, reopened.GetSnapshot().Entries[0].OpenedUtcTicks);
    }

    [TestMethod]
    public void RemoveAndApplyNeverWriteBackAStaleSettingsSnapshot()
    {
        using var store = new RecentFoldersStore(StorePath, new(), () => 50);
        store.Record(@"C:\A", 10);
        var oldSnapshot = store.GetSnapshot();
        store.Record(@"C:\B", 20);
        store.Remove(@"c:\a");
        store.ApplySettings(new() { Capacity = 1, MenuLimit = 1 });
        Assert.HasCount(1, oldSnapshot.Entries);
        Assert.AreEqual(@"C:\B", store.GetSnapshot().Entries.Single().Path);
    }

    [TestMethod]
    public void ApplyingExclusionsPrunesOnlyExactDirectory()
    {
        using var store = new RecentFoldersStore(StorePath, new(), () => 50);
        store.Record(@"C:\Private", 10);
        store.Record(@"C:\Private\Child", 20);
        store.Record(@"C:\PrivateOther", 30);
        store.ApplySettings(new() { ExcludedDirectories = [@"c:\private\"] });
        store.Record(@"C:\Private", 55);
        store.Record(@"C:\Private\Later", 60);
        CollectionAssert.AreEqual(new[] { @"C:\Private\Later", @"C:\PrivateOther", @"C:\Private\Child" },
            store.GetSnapshot().Entries.Select(e => e.Path).ToArray());
    }

    [TestMethod]
    public void RootAndUncPathsSurvivePersistenceWithoutFilesystemProbes()
    {
        using (var store = new RecentFoldersStore(StorePath, new()))
        {
            store.Record(@"C:\", 10);
            store.Record(@"\\offline-server\share\folder", 20);
            store.Record("shell:Downloads", 30);
            store.Record("relative", 40);
            store.Record(@"C:\wild*", 50);
        }
        using var reopened = new RecentFoldersStore(StorePath, new());
        CollectionAssert.AreEqual(new[] { @"\\offline-server\share\folder", @"C:\" }, reopened.GetSnapshot().Entries.Select(e => e.Path).ToArray());
    }

    [TestMethod]
    public void CorruptMainFallsBackToBackupButMissingMainDoesNot()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath, "broken JSON");
        File.WriteAllText(StorePath + ".bak", JsonSerializer.Serialize(new[] { new RecentFolderEntry(@"C:\A", 10) }));
        using (var store = new RecentFoldersStore(StorePath, new())) Assert.HasCount(1, store.GetSnapshot().Entries);
        File.Delete(StorePath);
        using var empty = new RecentFoldersStore(StorePath, new());
        Assert.HasCount(0, empty.GetSnapshot().Entries);
    }

    [TestMethod]
    public void MalformedEntriesAreFilteredAndLatestDuplicateWins()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(new RecentFolderEntry?[]
        {
            null, new("relative", 100), new(@"C:\A", 10), new(@"c:\a\", 20), new(@"C:\B", long.MaxValue)
        }));
        using var store = new RecentFoldersStore(StorePath, new());
        Assert.AreEqual(20L, store.GetSnapshot().Entries.Single().OpenedUtcTicks);
    }

    [TestMethod]
    public void NewSettingsHaveDefaultsAndUntrustedLimitsAreClamped()
    {
        var settings = JsonSerializer.Deserialize<UserSettings>("{}")!.RecentFolders;
        Assert.IsTrue(settings.Enabled);
        Assert.AreEqual(200, settings.Capacity);
        Assert.AreEqual(20, settings.MenuLimit);
        var corrected = new RecentFoldersSettings { Capacity = 0, MenuLimit = 1000 }.CopyValidated();
        Assert.AreEqual(1, corrected.Capacity);
        Assert.AreEqual(1, corrected.MenuLimit);
    }

    [TestMethod]
    public void ExclusionNormalizationUsesExactDirectoriesAndEnvironmentVariables()
    {
        var root = RecentFolderPaths.Normalize("%TEMP%")!;
        Assert.IsTrue(RecentFolderPaths.IsExcluded(root, [root]));
        Assert.IsFalse(RecentFolderPaths.IsExcluded(root + @"\child", [root]));
        Assert.IsFalse(RecentFolderPaths.IsExcluded(root + "-other", [root]));
        Assert.IsTrue(RecentFolderPaths.IsExcluded(@"C:\", [@"C:\"]));
        Assert.IsFalse(RecentFolderPaths.IsExcluded(@"C:\child", [@"C:\"]));
        Assert.IsNull(RecentFolderPaths.Normalize(@"\\?\C:\device"));
    }
}
