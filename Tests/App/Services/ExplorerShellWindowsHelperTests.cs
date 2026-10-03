using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

[TestClass]
public sealed class ExplorerShellWindowsHelperTests
{
    [TestMethod]
    public void TrySelectInFolder_SelectsInTheOpenedTabWithoutNavigatingAgain()
    {
        var folder = new FakeFolder { Self = new FakeItem { Path = @"C:\target" } };
        var window = new FakeWindow(new FakeDocument(folder, folder));

        Assert.IsTrue(ExplorerShellWindowsHelper.TrySelectInFolder(window, @"C:\target", "item"));

        Assert.IsNull(window.NavigatedTo);
        Assert.AreSame(folder.Item, window.Document.SelectedItem);
        Assert.AreEqual(0x1 | 0x4 | 0x8 | 0x10, window.Document.SelectionFlags);
    }

    [TestMethod]
    public void TrySelectInFolder_TabStillShowsOldFolder_DoesNotSelectSameNamedItem()
    {
        var oldFolder = new FakeFolder { Self = new FakeItem { Path = @"C:\old" } };
        var targetFolder = new FakeFolder { Self = new FakeItem { Path = @"C:\target" } };
        var window = new FakeWindow(new FakeDocument(oldFolder, targetFolder));

        Assert.IsFalse(ExplorerShellWindowsHelper.TrySelectInFolder(window, @"C:\target", "same-name"));
        Assert.IsNull(window.Document.SelectedItem);
        Assert.IsTrue(ExplorerShellWindowsHelper.TrySelectInFolder(window, @"C:\target", "same-name"));
        Assert.AreSame(targetFolder.Item, window.Document.SelectedItem);
        Assert.IsNull(window.NavigatedTo);
    }

    [TestMethod]
    public void TrySelectInFolder_ItemNotReady_ReportsFailureWithoutNavigating()
    {
        var folder = new FakeFolder { Self = new FakeItem { Path = @"C:\target" }, Item = null };
        var window = new FakeWindow(new FakeDocument(folder, folder));

        Assert.IsFalse(ExplorerShellWindowsHelper.TrySelectInFolder(window, @"C:\target", "missing"));
        Assert.IsNull(window.Document.SelectedItem);
        Assert.IsNull(window.NavigatedTo);
    }

    [TestMethod]
    public void NavigateAndSelect_WaitsForTheRequestedFolderBeforeSelecting()
    {
        var oldFolder = new FakeFolder { Self = new FakeItem { Path = @"C:\old" } };
        var targetFolder = new FakeFolder { Self = new FakeItem { Path = @"C:\target" } };
        var window = new FakeWindow(new FakeDocument(oldFolder, targetFolder));

        Assert.IsTrue(ExplorerShellWindowsHelper.NavigateAndSelect(window, @"C:\target", "same-name"));

        Assert.AreEqual(@"C:\target", window.NavigatedTo);
        Assert.AreSame(targetFolder.Item, window.Document.SelectedItem);
        Assert.AreEqual(0x1 | 0x4 | 0x8 | 0x10, window.Document.SelectionFlags);
    }

    [TestMethod]
    public void NavigateAndSelect_ItemNeverAppears_ReturnsFalseForShellFallback()
    {
        var folder = new FakeFolder { Self = new FakeItem { Path = @"C:\target" }, Item = null };
        var window = new FakeWindow(new FakeDocument(folder, folder));

        Assert.IsFalse(ExplorerShellWindowsHelper.NavigateAndSelect(window, @"C:\target", "missing"));
        Assert.IsNull(window.Document.SelectedItem);
    }

    // Public because production accesses the same members through the COM dynamic binder.
    public sealed class FakeWindow(FakeDocument document)
    {
        public string? NavigatedTo { get; private set; }
        public FakeDocument Document { get; } = document;
        public void Navigate2(string path) => NavigatedTo = path;
    }

    public sealed class FakeDocument(FakeFolder first, FakeFolder current)
    {
        private bool _firstRead = true;
        public FakeFolder Folder
        {
            get
            {
                var folder = _firstRead ? first : current;
                _firstRead = false;
                return folder;
            }
        }

        public object? SelectedItem { get; private set; }
        public int SelectionFlags { get; private set; }
        public void SelectItem(object item, int flags) => (SelectedItem, SelectionFlags) = (item, flags);
    }

    public sealed class FakeFolder
    {
        public FakeItem Self { get; init; } = new();
        public object? Item { get; init; } = new();
        public object? ParseName(string name) => Item;
    }

    public sealed class FakeItem
    {
        public string Path { get; init; } = "";
    }
}
