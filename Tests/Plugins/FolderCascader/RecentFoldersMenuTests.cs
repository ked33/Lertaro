using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FolderCascader.Navigation;
using static Lertaro.Plugins.FolderCascader.Tests.MenuBuilderTestHelpers;

namespace Lertaro.Plugins.FolderCascader.Tests;

[TestClass]
public sealed class RecentFoldersMenuTests
{
    [TestMethod]
    public void LatestFifteenAppearImmediatelyWithoutCheckingTheirPathsOrPreparingSubmenuHandles()
    {
        var provider = new Provider();
        var entries = Entries(40);
        entries.Add(new(" ", 100));
        entries.Add(new(entries[39].Path.ToLowerInvariant(), 39));
        var items = new List<DynamicMenuItem>();
        RecentFoldersMenu.AppendRoot(items, provider, false, new(entries, 20));
        Assert.HasCount(16, items);
        Assert.IsTrue(items.All(item => item.LoadDeferredItem == null));
        Assert.IsFalse(provider.TryGetPath(new IntPtr(18), out _), "Only the category and fifteen root handles are allocated.");
        CollectionAssert.AreEqual(Enumerable.Range(26, 15).Reverse().Select(i => $@"C:\Folder{i}").ToArray(),
            items.Skip(1).Select(item => GetPath(provider, item.SubMenuHandle)).ToArray());
        var checkedPaths = new List<string>();
        var submenu = RecentFoldersMenu.BuildSubmenu(provider, path => { checkedPaths.Add(path); return path != @"C:\Folder25"; });
        Assert.HasCount(20, submenu);
        Assert.HasCount(21, checkedPaths);
        Assert.AreEqual(@"C:\Folder24", GetPath(provider, submenu[0].SubMenuHandle));
        Assert.AreEqual(@"C:\Folder5", GetPath(provider, submenu[^1].SubMenuHandle));
        Assert.IsFalse(checkedPaths.Intersect(items.Skip(1).Select(item => GetPath(provider, item.SubMenuHandle))).Any());
    }

    [TestMethod]
    [DataRow(0, 0, 0)]
    [DataRow(1, 1, 0)]
    [DataRow(14, 14, 0)]
    [DataRow(15, 15, 0)]
    [DataRow(16, 15, 1)]
    [DataRow(34, 15, 19)]
    [DataRow(35, 15, 20)]
    public void ShortHistoryDoesNotRepeatRootFoldersToFillTheSubmenu(int count, int rootCount, int submenuCount)
    {
        var provider = new Provider();
        var items = new List<DynamicMenuItem>();
        RecentFoldersMenu.AppendRoot(items, provider, false, new(Entries(count), 20));
        Assert.HasCount(1 + rootCount, items);
        var submenu = RecentFoldersMenu.BuildSubmenu(provider, _ => true);
        Assert.HasCount(Math.Max(1, submenuCount), submenu);
        if (submenuCount == 0) Assert.IsTrue(submenu[0].IsDisabled && !submenu[0].HasSubMenu);
        CollectionAssert.AreEqual(Entries(count).OrderByDescending(entry => entry.OpenedUtcTicks).Select(entry => entry.Path).ToArray(),
            items.Skip(1).Concat(submenu.Where(item => !item.IsDisabled)).Select(item => GetPath(provider, item.SubMenuHandle)).ToArray());
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 1)]
    [DataRow(20, 20)]
    [DataRow(100, 100)]
    [DataRow(int.MaxValue, 100)]
    public void MenuLimitAppliesOnlyToTheSubmenu(int limit, int expectedSubmenuCount)
    {
        var provider = new Provider();
        var items = new List<DynamicMenuItem>();
        RecentFoldersMenu.AppendRoot(items, provider, false, new(Entries(150), limit));
        Assert.HasCount(16, items);
        Assert.HasCount(expectedSubmenuCount, RecentFoldersMenu.BuildSubmenu(provider, _ => true));
    }

    [TestMethod]
    [DoNotParallelize]
    public void SubmenuUsesTheOpeningSnapshotAndTheNextSessionRefreshesIt()
    {
        var previous = RecentFoldersService.GetSnapshotFunc;
        try
        {
            RecentFoldersService.GetSnapshotFunc = () => new(Entries(40), 20);
            var provider = new Provider();
            var items = new List<DynamicMenuItem>();
            RecentFoldersMenu.AppendRoot(items, provider, false);
            RecentFoldersService.GetSnapshotFunc = () => new(Entries(16), 1);
            var submenu = RecentFoldersMenu.BuildSubmenu(provider, _ => true);
            Assert.HasCount(20, submenu);
            Assert.AreEqual(@"C:\Folder25", GetPath(provider, submenu[0].SubMenuHandle));
            provider.ClearSession();
            Assert.HasCount(0, provider.RecentFolderSnapshot.Entries);
            items.Clear();
            RecentFoldersMenu.AppendRoot(items, provider, false);
            submenu = RecentFoldersMenu.BuildSubmenu(provider, _ => true);
            Assert.HasCount(1, submenu);
            Assert.AreEqual(@"C:\Folder1", GetPath(provider, submenu[0].SubMenuHandle));
        }
        finally { RecentFoldersService.GetSnapshotFunc = previous; }
    }

    [TestMethod]
    public void SeparatorIsInsertedOnlyAfterVisibleHistory()
    {
        var items = new List<DynamicMenuItem> { new() { Text = "History" } };
        RecentFoldersMenu.AppendRoot(items, new Provider(), true, new([], 20));
        Assert.HasCount(3, items);
        Assert.IsTrue(items[1].IsSeparator);
        var empty = new List<DynamicMenuItem>();
        RecentFoldersMenu.AppendRoot(empty, new Provider(), false, new([], 20));
        Assert.HasCount(1, empty);
        Assert.IsFalse(empty[0].IsSeparator);
    }

    private static List<RecentFolderEntry> Entries(int count) =>
        Enumerable.Range(1, count).Select(i => new RecentFolderEntry($@"C:\Folder{i}", i)).ToList();
}
