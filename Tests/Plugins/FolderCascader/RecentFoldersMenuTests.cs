using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FolderCascader.Navigation;

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
    public void EmptyHistoryShowsADisabledPlaceholder()
    {
        var items = RecentFoldersMenu.Build(new Provider(), new(Array.Empty<RecentFolderEntry>(), 20));
        Assert.HasCount(1, items);
        Assert.IsTrue(items[0].IsDisabled);
        Assert.IsFalse(items[0].HasSubMenu);
    }

    [TestMethod]
    public void SeparatorIsInsertedOnlyAfterVisibleHistory()
    {
        var items = new List<DynamicMenuItem> { new() { Text = "History" } };
        RecentFoldersMenu.AppendRoot(items, new Provider(), historyShown: true);
        Assert.HasCount(3, items);
        Assert.IsTrue(items[1].IsSeparator);
        Assert.IsTrue(items[2].HasSubMenu);
        var empty = new List<DynamicMenuItem>();
        RecentFoldersMenu.AppendRoot(empty, new Provider(), historyShown: false);
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
}
