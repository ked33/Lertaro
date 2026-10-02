using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FolderCascader.Navigation;
using static Lertaro.Plugins.FolderCascader.Tests.MenuBuilderTestHelpers;

namespace Lertaro.Plugins.FolderCascader.Tests;

[TestClass]
public sealed class RecentFoldersMenuTests
{
    [TestMethod]
    public void MissingFoldersAreSkippedBeforeLimitAndEntriesRemainCascadable()
    {
        var provider = new Provider();
        var snapshot = new RecentFoldersSnapshot(new RecentFolderEntry[]
        {
            new(@"C:\Missing", 30), new(@"C:\A", 20), new(@"c:\a", 15), new(@"C:\B", 10)
        }, 2);
        var items = RecentFoldersMenu.Build(provider, snapshot, path => path != @"C:\Missing");
        Assert.HasCount(2, items);
        Assert.IsTrue(items.All(i => i.HasSubMenu && i.SubMenuHandle != IntPtr.Zero));
        Assert.IsTrue(provider.TryGetPath(items[0].SubMenuHandle, out var path));
        Assert.AreEqual(@"C:\A", path);
    }

    [TestMethod]
    public void LatestFifteenFoldersFollowTheCategoryAndSubmenuKeepsItsFullLimit()
    {
        var provider = new Provider();
        var entries = Entries(40);
        entries.Add(new(@"C:\Missing", 100));
        entries.Add(new(" ", 99));
        entries.Add(new(@"c:\folder40", 39));
        var items = new List<DynamicMenuItem> { new() { Text = "Existing" } };

        RecentFoldersMenu.AppendRoot(items, provider, false, new(entries, 20), path => path != @"C:\Missing");

        Assert.HasCount(17, items);
        Assert.AreEqual("Existing", items[0].Text);
        Assert.AreEqual(RecentFoldersMenu.HandlePath, GetPath(provider, items[1].SubMenuHandle));
        var rootFolders = items.Skip(2).ToArray();
        Assert.HasCount(15, rootFolders);
        Assert.HasCount(20, provider.RecentFolderSubmenu);
        CollectionAssert.AreEqual(Enumerable.Range(26, 15).Reverse().Select(i => $@"C:\Folder{i}").ToArray(),
            rootFolders.Select(item => GetPath(provider, item.SubMenuHandle)).ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(6, 20).Reverse().Select(i => $@"C:\Folder{i}").ToArray(),
            provider.RecentFolderSubmenu.Select(item => GetPath(provider, item.SubMenuHandle)).ToArray());
        Assert.IsTrue(rootFolders.Concat(provider.RecentFolderSubmenu)
            .All(item => item.HasSubMenu && item.IsActionable && !item.IsDisabled));
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
        RecentFoldersMenu.AppendRoot(items, provider, false, new(Entries(count), 20), _ => true);

        Assert.HasCount(1 + rootCount, items);
        var submenu = provider.RecentFolderSubmenu;
        Assert.HasCount(Math.Max(1, submenuCount), submenu);
        if (submenuCount == 0)
        {
            Assert.IsTrue(submenu[0].IsDisabled);
            Assert.IsFalse(submenu[0].HasSubMenu);
        }
        var paths = items.Skip(1).Concat(submenu.Where(item => !item.IsDisabled))
            .Select(item => GetPath(provider, item.SubMenuHandle)).ToArray();
        CollectionAssert.AreEqual(Entries(count).OrderByDescending(entry => entry.OpenedUtcTicks)
            .Select(entry => entry.Path).ToArray(), paths);
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
        RecentFoldersMenu.AppendRoot(items, provider, false, new(Entries(150), limit), _ => true);

        Assert.HasCount(16, items);
        Assert.HasCount(expectedSubmenuCount, provider.RecentFolderSubmenu);
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
            RecentFoldersMenu.AppendRoot(items, provider, false, exists: _ => true);

            RecentFoldersService.GetSnapshotFunc = () => new(Entries(16), 1);
            var submenu = provider.GetMenuItems(new FakeResult(), items[0].SubMenuHandle).ToArray();
            Assert.HasCount(20, submenu);
            Assert.AreEqual(@"C:\Folder25", GetPath(provider, submenu[0].SubMenuHandle));
            Assert.AreEqual(@"C:\Folder6", GetPath(provider, submenu[^1].SubMenuHandle));

            provider.ClearSession();
            Assert.HasCount(0, provider.RecentFolderSubmenu);
            items.Clear();
            RecentFoldersMenu.AppendRoot(items, provider, false, exists: _ => true);
            Assert.HasCount(1, provider.RecentFolderSubmenu);
            Assert.AreEqual(@"C:\Folder1", GetPath(provider, provider.RecentFolderSubmenu[0].SubMenuHandle));
        }
        finally
        {
            RecentFoldersService.GetSnapshotFunc = previous;
        }
    }

    [TestMethod]
    public void SeparatorIsInsertedOnlyAfterVisibleHistory()
    {
        var items = new List<DynamicMenuItem> { new() { Text = "History" } };
        RecentFoldersMenu.AppendRoot(items, new Provider(), historyShown: true, snapshot: new([], 20));
        Assert.HasCount(3, items);
        Assert.IsTrue(items[1].IsSeparator);
        Assert.IsTrue(items[2].HasSubMenu);
        var empty = new List<DynamicMenuItem>();
        RecentFoldersMenu.AppendRoot(empty, new Provider(), historyShown: false, snapshot: new([], 20));
        Assert.HasCount(1, empty);
        Assert.IsFalse(empty[0].IsSeparator);
    }

    [TestMethod]
    public void RecentFoldersAreShownByDefault()
    {
        var setting = new FolderCascaderPlugin().GetConfigSchema().Fields.Single().SubFields!
            .Single(field => field.Key == "ShowRecentFolders");
        Assert.IsTrue((bool)setting.DefaultValue!);
    }

    private static List<RecentFolderEntry> Entries(int count) =>
        Enumerable.Range(1, count).Select(i => new RecentFolderEntry($@"C:\Folder{i}", i)).ToList();
}
