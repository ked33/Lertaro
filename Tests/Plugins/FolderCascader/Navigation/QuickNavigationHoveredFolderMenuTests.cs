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
    public void HoveredFolderIsPinnedAboveCurrentDirectoriesAndCascadesIntoItsOwnContents()
    {
        var hovered = Directory.CreateDirectory(Path.Combine(_directory.Path, "Hovered")).FullName;
        var child = Directory.CreateDirectory(Path.Combine(hovered, "Child")).FullName;
        var result = new FakeResult { FullPath = _directory.Path, HoveredFolderPath = hovered, IsDir = true };
        var provider = new Provider();
        Assert.IsFalse(provider.ShowGroupHeader);

        var items = provider.GetMenuItems(result, IntPtr.Zero).ToArray();

        Assert.HasCount(2, items);
        Assert.IsTrue(items[0].IsPinnedToTop);
        Assert.AreEqual("foldercascader://opened-folders", GetPath(provider, items[1].SubMenuHandle));
        Assert.AreEqual(hovered, GetPath(provider, items[0].SubMenuHandle));
        Assert.IsTrue(items[0].HasSubMenu && items[0].IsActionable);
        var children = provider.GetMenuItems(result, items[0].SubMenuHandle).ToArray();
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

    [TestMethod]
    [Timeout(5000)]
    public async Task RootDoesNotWaitForEitherCaptureAndSubmenuUsesTheFreshSnapshot()
    {
        var hover = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider();
        var result = new FakeResult
        {
            FullPath = _directory.Path, HoveredFolderPathTask = hover.Task, OpenedFolderPathsTask = opened.Task
        };
        var items = provider.GetMenuItems(result, IntPtr.Zero).ToArray();
        Assert.HasCount(2, items);
        Assert.IsTrue(items[0].IsPinnedToTop);
        Assert.IsFalse(hover.Task.IsCompleted);
        Assert.IsFalse(opened.Task.IsCompleted);
        Assert.IsTrue(items[0].IsDisabled);
        Assert.AreEqual(string.Empty, items[0].Text, "Unconfirmed hover must use a collapsed insertion marker.");

        var pendingHover = items[0].LoadDeferredItem!(CancellationToken.None);
        Assert.IsFalse(pendingHover.IsCompleted);
        hover.SetResult(_directory.Path);
        var resolved = await pendingHover;
        Assert.IsNotNull(resolved);
        Assert.AreEqual(_directory.Path, GetPath(provider, resolved.SubMenuHandle));

        opened.SetResult([@"C:\Fresh"]);
        Assert.AreSame(items[1], await items[1].LoadDeferredItem!(CancellationToken.None));
        var folders = provider.GetMenuItems(result, items[1].SubMenuHandle).ToArray();
        Assert.HasCount(1, folders);
        Assert.AreEqual(@"C:\Fresh", GetPath(provider, folders[0].SubMenuHandle));
    }

    [TestMethod]
    public async Task DeferredHoverCanBeCancelledBeforeTheCaptureCompletes()
    {
        var hover = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new FakeResult { FullPath = _directory.Path, HoveredFolderPathTask = hover.Task };
        var items = new Provider().GetMenuItems(result, IntPtr.Zero).ToArray();
        using var cancellation = new CancellationTokenSource();
        var pending = items[0].LoadDeferredItem!(cancellation.Token);
        cancellation.Cancel();
        try { await pending; Assert.Fail("A closed menu must not allocate a deferred folder handle."); }
        catch (OperationCanceledException) { }
        hover.SetResult(_directory.Path);
    }

    [TestMethod]
    public async Task EmptyDeferredCapturesRemoveTheirRows()
    {
        var result = new FakeResult
        {
            FullPath = _directory.Path,
            HoveredFolderPathTask = Task.FromResult<string?>(null),
            OpenedFolderPathsTask = Task.FromResult<IReadOnlyList<string>>([])
        };
        var items = new Provider().GetMenuItems(result, IntPtr.Zero).ToArray();
        Assert.HasCount(2, items);
        Assert.IsTrue(items[0].IsPinnedToTop);
        Assert.IsNull(await items[1].LoadDeferredItem!(CancellationToken.None));
        Assert.IsNull(await items[0].LoadDeferredItem!(CancellationToken.None));
    }
}
