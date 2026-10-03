using System.Runtime.InteropServices;
using System.Windows;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.CoreExtensions.Actions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

[TestClass]
[DoNotParallelize] // Broken-target tests intercept the host's process-wide message-box delegate.
public sealed class LocateShortcutTargetActionTests
{
    [TestMethod]
    public void RegisteredImmediatelyAfterFileLocate_WithoutADefaultHotkey()
    {
        var actions = new CoreExtensionsPlugin().GetActions().ToList();
        var index = actions.FindIndex(a => a is LocateInExplorerAction);

        Assert.IsGreaterThanOrEqualTo(0, index);
        Assert.IsInstanceOfType<LocateShortcutTargetAction>(actions[index + 1]);
        Assert.IsEmpty(actions[index + 1].Hotkey);
    }

    [TestMethod]
    public void CanExecute_RequiresOnlyExistingShortcutFiles()
    {
        using var dir = new TempDirectory();
        var shortcut = Path.Combine(dir.Path, "item.LNK");
        var file = Path.Combine(dir.Path, "item.txt");
        File.WriteAllText(shortcut, "");
        File.WriteAllText(file, "");
        var action = new LocateShortcutTargetAction();

        Assert.IsTrue(action.CanExecute([new FakeResult { FullPath = shortcut }]));
        Assert.IsFalse(action.CanExecute([]));
        Assert.IsFalse(action.CanExecute([new FakeResult { FullPath = file }]));
        Assert.IsFalse(action.CanExecute([new FakeResult { FullPath = shortcut + ".missing.lnk" }]));
        Assert.IsFalse(action.CanExecute([new FakeResult { FullPath = dir.Path, IsDir = true }]));
        Assert.IsFalse(action.CanExecute([new FakeResult { FullPath = shortcut }, new FakeResult { FullPath = file }]));
    }

    [TestMethod]
    public void Execute_RealShortcutsLocateTargetFileAndFolder_InSelectionOrder()
    {
        using var dir = new TempDirectory();
        var file = Path.Combine(dir.Path, "实际文件.txt");
        File.WriteAllText(file, "keep");
        var folder = Directory.CreateDirectory(Path.Combine(dir.Path, "实际文件夹")).FullName;
        var fileShortcut = CreateShortcut(dir.Path, "file.lnk", file);
        var folderShortcut = CreateShortcut(dir.Path, "folder.LNK", folder);
        var view = new FakeWindow();

        new LocateShortcutTargetAction().Execute(
            [new FakeResult { FullPath = fileShortcut }, new FakeResult { FullPath = folderShortcut }], view);

        CollectionAssert.AreEqual(new[] { file, folder }, view.LocatedPaths);
        Assert.AreEqual("keep", File.ReadAllText(file));
    }

    [TestMethod]
    public void Execute_MissingAndInvalidTargets_ReportFailureWithoutLocatingTheShortcut()
    {
        using var dir = new TempDirectory();
        var missing = CreateShortcut(dir.Path, "missing.lnk", Path.Combine(dir.Path, "gone.txt"));
        var invalid = Path.Combine(dir.Path, "invalid.lnk");
        File.WriteAllText(invalid, "not a shortcut");
        var view = new FakeWindow();
        var messages = 0;
        var previous = PluginMessageBoxService.ShowFunc;
        PluginMessageBoxService.ShowFunc = (_, _, _, _, _) => { messages++; return MessageBoxResult.OK; };
        try
        {
            new LocateShortcutTargetAction().Execute(
                [new FakeResult { FullPath = missing }, new FakeResult { FullPath = invalid }], view);
        }
        finally
        {
            PluginMessageBoxService.ShowFunc = previous;
        }

        Assert.AreEqual(2, messages);
        Assert.IsEmpty(view.LocatedPaths);
    }

    private static string CreateShortcut(string directory, string name, string target)
    {
        var path = Path.Combine(directory, name);
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? shortcut = null;
        try
        {
            shortcut = ((dynamic)shell).CreateShortcut(path);
            ((dynamic)shortcut).TargetPath = target;
            ((dynamic)shortcut).Save();
        }
        finally
        {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
        return path;
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-locate-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    private sealed class FakeWindow : IPluginSearchWindow
    {
        public List<string> LocatedPaths { get; } = [];
        public void LocateInExplorerExternal(string path) => LocatedPaths.Add(path);
        public void OpenFileOrFolderExternal(string path) => Assert.Fail("Locating must not open the target.");
        public void OpenFileOrFolderAsAdminExternal(string path) => Assert.Fail("Locating must not launch the target as admin.");
        public void HideWindow() { }
    }
}
