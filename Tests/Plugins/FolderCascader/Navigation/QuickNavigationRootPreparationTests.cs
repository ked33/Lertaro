using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FolderCascader.Navigation;
using static Lertaro.Plugins.FolderCascader.Tests.MenuBuilderTestHelpers;

namespace Lertaro.Plugins.FolderCascader.Tests.Navigation;

[TestClass]
[DoNotParallelize]
public sealed class QuickNavigationRootPreparationTests
{
    [TestMethod]
    public async Task RecentRootAndSubmenuUseOnePreparedSnapshotWithoutRecheckingPaths()
    {
        var provider = new Provider();
        var completion = new TaskCompletionSource<RecentFoldersSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var items = new List<DynamicMenuItem>();
        RecentFoldersMenu.AppendDeferred(items, provider, false, completion.Task);
        Assert.HasCount(16, items);
        Assert.IsTrue(items.Skip(1).All(item => item.Text == "" && item.LoadDeferredItem != null));
        var prepared = new RecentFoldersSnapshot(Enumerable.Range(1, 35).Reverse()
            .Select(i => new RecentFolderEntry($@"C:\PreparedOnly\{i}", i)).ToArray(), 20);
        completion.SetResult(prepared);
        var root = await Task.WhenAll(items.Skip(1).Select(item => item.LoadDeferredItem!(CancellationToken.None)));
        var submenu = RecentFoldersMenu.BuildPreparedSubmenu(provider, await provider.RecentFolderSnapshotTask!);
        Assert.HasCount(15, root);
        Assert.HasCount(20, submenu);
        var paths = root.Concat(submenu).Select(item => GetPath(provider, item!.SubMenuHandle)).ToArray();
        CollectionAssert.AreEqual(prepared.Entries.Select(entry => entry.Path).ToArray(), paths);
        Assert.IsTrue(root.Concat(submenu).All(item => item!.IsPathAvailable == true));
    }

    [TestMethod]
    public async Task ClosedMenuDoesNotAllocatePreparedRecentHandles()
    {
        var provider = new Provider();
        var completion = new TaskCompletionSource<RecentFoldersSnapshot>();
        var items = new List<DynamicMenuItem>();
        RecentFoldersMenu.AppendDeferred(items, provider, false, completion.Task);
        using var cancellation = new CancellationTokenSource();
        var pending = items[1].LoadDeferredItem!(cancellation.Token);
        cancellation.Cancel();
        try { await pending; Assert.Fail(); }
        catch (OperationCanceledException) { }
        completion.SetResult(new([new(@"C:\Late", 1)], 20));
        Assert.AreEqual(new IntPtr(3), provider.AllocateHandle("next"), "Only the recent-category handle was allocated.");
    }

    [TestMethod]
    public async Task FolderConfigurationChangesInvalidatePreparedNamesAndPaths()
    {
        using var directory = new TempDirectory();
        var preparation = new RootMenuPreparation();
        var config = new List<FolderCascaderPlugin.FolderConfigItem> { Folder("First", directory.Path) };
        var first = await preparation.Folders(config).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(first[0].Available);
        Assert.AreSame(first, await preparation.Folders(config));
        config[0].Name = "Changed";
        config[0].Path = Path.Combine(directory.Path, "Missing");
        var changed = await preparation.Folders(config).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("Changed", changed[0].Name);
        Assert.IsFalse(changed[0].Available);
        Assert.AreEqual("First", first[0].Name);
    }

    [TestMethod]
    public async Task FavoriteChangesInvalidateAvailabilityWithoutRetainingRemovedEntries()
    {
        var previous = FavoritesService.GetFavoritesFunc;
        try
        {
            var preparation = new RootMenuPreparation();
            FavoritesService.GetFavoritesFunc = () => [new() { Path = "https://example.com" }];
            Assert.IsTrue((await preparation.FavoriteAvailability().WaitAsync(TimeSpan.FromSeconds(5))).Available);
            FavoritesService.GetFavoritesFunc = () => [];
            Assert.IsFalse((await preparation.FavoriteAvailability().WaitAsync(TimeSpan.FromSeconds(5))).Available);
        }
        finally { FavoritesService.GetFavoritesFunc = previous; }
    }
}
