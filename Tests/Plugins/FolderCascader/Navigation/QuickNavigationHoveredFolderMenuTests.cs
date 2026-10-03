using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FolderCascader.Navigation;
using static Lertaro.Plugins.FolderCascader.Tests.MenuBuilderTestHelpers;

namespace Lertaro.Plugins.FolderCascader.Tests.Navigation;

[TestClass]
[DoNotParallelize]
public sealed class QuickNavigationHoveredFolderMenuTests
{
    private Func<string, string, object?, object?>? _settings;
    private Func<IReadOnlyList<string>>? _openedFolders;
    private TempDirectory _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _settings = PluginSettingsService.GetSettingFunc;
        _openedFolders = ExplorerPathService.GetOpenedFolderPathsFunc;
        _directory = new TempDirectory();
        PluginSettingsService.GetSettingFunc = (_, key, fallback) => key switch
        {
            "ShowFavorites" or "ShowHistory" or "ShowRecentFolders" => false,
            _ => fallback
        };
        ExplorerPathService.GetOpenedFolderPathsFunc = () => [_directory.Path];
    }

    [TestCleanup]
    public void Cleanup()
    {
        PluginSettingsService.GetSettingFunc = _settings;
        ExplorerPathService.GetOpenedFolderPathsFunc = _openedFolders;
        _directory.Dispose();
    }

    [TestMethod]
    public void HoveredFolderFollowsCurrentDirectoriesAndCascadesIntoItsOwnContents()
    {
        var hovered = Directory.CreateDirectory(Path.Combine(_directory.Path, "Hovered")).FullName;
        var child = Directory.CreateDirectory(Path.Combine(hovered, "Child")).FullName;
        var result = new FakeResult { FullPath = _directory.Path, HoveredFolderPath = hovered, IsDir = true };
        var provider = new Provider();

        var items = provider.GetMenuItems(result, IntPtr.Zero).ToArray();

        Assert.HasCount(2, items);
        Assert.AreEqual("foldercascader://opened-folders", GetPath(provider, items[0].SubMenuHandle));
        Assert.AreEqual(hovered, GetPath(provider, items[1].SubMenuHandle));
        Assert.IsTrue(items[1].HasSubMenu && items[1].IsActionable);
        var children = provider.GetMenuItems(result, items[1].SubMenuHandle).ToArray();
        Assert.IsTrue(children.Any(item => item.HasSubMenu && GetPath(provider, item.SubMenuHandle) == child));
        Assert.AreEqual(_directory.Path, result.FullPath, "Hovering must not replace the current-directory context.");

        var next = provider.GetMenuItems(new FakeResult { FullPath = _directory.Path }, IntPtr.Zero).ToArray();
        Assert.HasCount(1, next, "A keyboard/blank-space invocation must not retain the last hover.");
    }

    [TestMethod]
    public void FilesMissingFoldersAndAbsentHoverDoNotAddAnEntry()
    {
        var file = Path.Combine(_directory.Path, "file.txt");
        File.WriteAllText(file, "test");
        foreach (var path in new[] { null, "", ".", file, Path.Combine(_directory.Path, "Missing") })
        {
            var items = new Provider().GetMenuItems(new FakeResult { FullPath = _directory.Path, HoveredFolderPath = path }, IntPtr.Zero).ToArray();
            Assert.HasCount(1, items);
        }
    }

    [TestMethod]
    public void HoveredFolderRemainsAvailableWhenTheOpenedFolderListIsEmpty()
    {
        ExplorerPathService.GetOpenedFolderPathsFunc = () => [];
        var provider = new Provider();
        var items = provider.GetMenuItems(new FakeResult { FullPath = _directory.Path, HoveredFolderPath = _directory.Path }, IntPtr.Zero).ToArray();
        Assert.HasCount(1, items);
        Assert.AreEqual(_directory.Path, GetPath(provider, items[0].SubMenuHandle));
        Assert.IsFalse(items[0].IsSeparator);
    }
}
