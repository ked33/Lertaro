using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Plugins.FolderCascader.Tests;

[TestClass]
public sealed class FolderCascaderPluginTests
{
    [TestMethod]
    [DataRow(null, "")]
    [DataRow("", "")]
    [DataRow(" e ", "E")]
    [DataRow("Z", "Z")]
    [DataRow("Ctrl+E", "")]
    [DataRow("EE", "")]
    [DataRow("1", "")]
    [DataRow("中", "")]
    [DataRow("é", "")]
    public void NormalizeShortcut_OnlyAcceptsOneAsciiLetter(string? value, string expected)
        => Assert.AreEqual(expected, FolderCascaderPlugin.NormalizeShortcut(value));

    [TestMethod]
    public void FindShortcutProblems_DetectsCaseInsensitiveKeysWithinNormalizedLevel()
    {
        var problems = FolderCascaderPlugin.FindShortcutProblems([
            new() { Name = "First", Path = "C:/First", SubMenu = " Tools//Network/ ", ShortcutKey = "e" },
            new() { Name = "Second", Path = "C:/Second", SubMenu = "Tools/Network", ShortcutKey = "E" },
            new() { Name = "Other", Path = "C:/Other", SubMenu = "Tools", ShortcutKey = "E" },
            new() { Name = "Invalid", Path = "C:/Invalid", ShortcutKey = "Ctrl+E" },
            new() { Name = "-", Path = "-", ShortcutKey = "Ctrl+E" }
        ]);

        Assert.HasCount(2, problems);
        Assert.IsTrue(problems.Any(p => p.Contains("First") && p.Contains("Second")));
        Assert.IsTrue(problems.Any(p => p.Contains("Invalid")));
        Assert.IsFalse(problems.Any(p => p.Contains("Other")));
    }

    [TestMethod]
    public void GetConfigSchema_ShortcutIsOptionalAndOldEntriesHaveNoShortcut()
    {
        var schema = new FolderCascaderPlugin().GetConfigSchema();
        var folderFields = schema.Fields.Single().SubFields!.Single(f => f.Key == "Folders").SubFields!;
        var shortcut = folderFields.Single(f => f.Key == "ShortcutKey");
        Assert.AreEqual(ConfigFieldType.Text, shortcut.FieldType);
        Assert.AreEqual(1, shortcut.MaxLength);
        Assert.AreEqual("", shortcut.DefaultValue);
        Assert.AreEqual("", new FolderCascaderPlugin.FolderConfigItem().ShortcutKey);
        Assert.IsNotNull(schema.OnSave);
    }

    [TestMethod]
    public void GetConfigSchema_OpenedFoldersAreShownByDefault()
    {
        var fields = new FolderCascaderPlugin().GetConfigSchema().Fields.Single().SubFields!;

        var setting = fields.Single(field => field.Key == "ShowOpenedFolders");

        Assert.AreEqual(ConfigFieldType.Boolean, setting.FieldType);
        Assert.IsTrue((bool)setting.DefaultValue!);
    }
}
