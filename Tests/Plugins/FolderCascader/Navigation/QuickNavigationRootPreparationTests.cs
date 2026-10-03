using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FolderCascader.Navigation;
using static Lertaro.Plugins.FolderCascader.Tests.MenuBuilderTestHelpers;

namespace Lertaro.Plugins.FolderCascader.Tests.Navigation;

[TestClass]
[DoNotParallelize]
public sealed class QuickNavigationRootPreparationTests
{
    [TestMethod]
    public void RootTextsAndRecentFifteenAreImmediateAndOpenedFoldersLoadOnlyOnExpansion()
    {
        var settings = PluginSettingsService.GetSettingFunc;
        var favorites = FavoritesService.GetFavoritesFunc;
        var recent = RecentFoldersService.GetSnapshotFunc;
        try
        {
            PluginSettingsService.GetSettingFunc = (_, key, fallback) => key switch
            {
                "Folders" => new List<FolderCascaderPlugin.FolderConfigItem> { Folder("Configured", @"C:\Missing-quicknav-test") },
                "ShowHistory" => false,
                _ => fallback
            };
            FavoritesService.GetFavoritesFunc = () => [new() { Path = @"C:\Missing-favorite-test" }];
            RecentFoldersService.GetSnapshotFunc = () => new(Enumerable.Range(1, 35)
                .Select(i => new RecentFolderEntry($@"C:\Recent{i}", i)).ToArray(), 20);
            var captures = 0;
            var result = new FakeResult
            {
                FullPath = @"C:\",
                OpenedFolderPathsLoader = () => { captures++; return Task.FromResult<IReadOnlyList<string>>([@"C:\Fresh"]); }
            };
            var provider = new Provider();
            var items = provider.GetMenuItems(result, IntPtr.Zero).ToArray();
            Assert.AreEqual(0, captures);
            Assert.IsTrue(items.All(item => item.LoadDeferredItem == null));
            Assert.IsTrue(items.Any(item => item.Text == "Configured" && !item.IsDisabled));
            Assert.IsTrue(items.Any(item => item.HasSubMenu && GetPath(provider, item.SubMenuHandle) == "foldercascader://favorites"));
            var recentIndex = Array.FindIndex(items, item => item.HasSubMenu && GetPath(provider, item.SubMenuHandle) == RecentFoldersMenu.HandlePath);
            Assert.AreEqual(15, items.Length - recentIndex - 1);
            var current = items.Single(item => item.HasSubMenu && GetPath(provider, item.SubMenuHandle) == "foldercascader://opened-folders");
            var paths = provider.GetMenuItems(result, current.SubMenuHandle).ToArray();
            Assert.AreEqual(1, captures);
            Assert.AreEqual(@"C:\Fresh", GetPath(provider, paths[0].SubMenuHandle));
            provider.GetMenuItems(result, current.SubMenuHandle).ToArray();
            Assert.AreEqual(1, captures, "Reuse the capture within one menu session.");
        }
        finally
        {
            PluginSettingsService.GetSettingFunc = settings;
            FavoritesService.GetFavoritesFunc = favorites;
            RecentFoldersService.GetSnapshotFunc = recent;
        }
    }
}
