using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Lertaro.App.Services.ShellMenu.QuickNav;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Services.ShellMenu.QuickNav;

[TestClass]
public sealed class QuickNavigationMenuShortcutTests
{
    private static MenuItem Item(string key, Action execute, bool enabled = true)
    {
        var item = new MenuItem { Header = "Projects", IsEnabled = enabled };
        QuickNavigationMenuKeyHandler.AttachShortcut(item,
            new DynamicMenuItem { Text = "Projects", ShortcutHint = key }, execute);
        return item;
    }

    [StaTestMethod]
    public void Shortcut_AppendsUppercaseSuffixWithoutChangingNameOrArrowColumn()
    {
        var data = new DynamicMenuItem { Text = "D-Software", ShortcutHint = "e", HasSubMenu = true };
        var item = new MenuItem();
        QuickNavigationMenuKeyHandler.AttachShortcut(item, data, () => { });

        var header = (ScrollViewer)item.Header;
        Assert.AreEqual("D-Software (E)", ((TextBlock)header.Content).Text);
        Assert.AreEqual("D-Software", data.Text);
        Assert.AreEqual("E", AutomationProperties.GetAcceleratorKey(item));
        Assert.IsTrue(string.IsNullOrEmpty(item.InputGestureText));
    }

    [StaTestMethod]
    public void Shortcut_ExecutesUniqueSiblingOnceAndLeavesOtherLayersAlone()
    {
        var rootCount = 0;
        var childCount = 0;
        var root = new ContextMenu();
        var parent = Item("e", () => rootCount++);
        parent.Items.Add(Item("E", () => childCount++));
        root.Items.Add(parent);

        Assert.IsTrue(QuickNavigationMenuKeyHandler.TryInvokeShortcut(root, Key.E, ModifierKeys.None, false));
        Assert.AreEqual(1, rootCount);
        Assert.AreEqual(0, childCount);
        Assert.IsTrue(QuickNavigationMenuKeyHandler.TryInvokeShortcut(parent, Key.E, ModifierKeys.None, false));
        Assert.AreEqual(1, rootCount);
        Assert.AreEqual(1, childCount);
    }

    [StaTestMethod]
    public void Shortcut_DuplicateIncludingDisabledEntryIsConsumedWithoutExecuting()
    {
        var count = 0;
        var root = new ContextMenu();
        root.Items.Add(Item("E", () => count++));
        root.Items.Add(Item("e", () => count++, enabled: false));

        Assert.IsTrue(QuickNavigationMenuKeyHandler.TryInvokeShortcut(root, Key.E, ModifierKeys.None, false));
        Assert.AreEqual(0, count);
    }

    [StaTestMethod]
    public void Shortcut_RejectsModifiersRepeatDisabledAndNonletterKeys()
    {
        var count = 0;
        var root = new ContextMenu();
        var item = Item("E", () => count++);
        root.Items.Add(item);
        foreach (var modifiers in new[] { ModifierKeys.Control, ModifierKeys.Alt, ModifierKeys.Shift, ModifierKeys.Windows })
            Assert.IsFalse(QuickNavigationMenuKeyHandler.TryInvokeShortcut(root, Key.E, modifiers, false));
        foreach (var key in new[] { Key.Enter, Key.Escape, Key.Right, Key.D1, Key.F1 })
            Assert.IsFalse(QuickNavigationMenuKeyHandler.TryInvokeShortcut(root, key, ModifierKeys.None, false));
        Assert.IsTrue(QuickNavigationMenuKeyHandler.TryInvokeShortcut(root, Key.E, ModifierKeys.None, true));
        item.IsEnabled = false;
        Assert.IsTrue(QuickNavigationMenuKeyHandler.TryInvokeShortcut(root, Key.E, ModifierKeys.None, false));
        item.IsEnabled = true;
        item.Visibility = Visibility.Collapsed;
        Assert.IsFalse(QuickNavigationMenuKeyHandler.TryInvokeShortcut(root, Key.E, ModifierKeys.None, false));
        Assert.AreEqual(0, count);
    }

    [StaTestMethod]
    public void Shortcut_InvalidHintsAndNonactionableRowsKeepTheirOriginalHeader()
    {
        foreach (var data in new[]
        {
            new DynamicMenuItem { ShortcutHint = "" },
            new DynamicMenuItem { ShortcutHint = "Ctrl+E" },
            new DynamicMenuItem { ShortcutHint = "中" },
            new DynamicMenuItem { ShortcutHint = "E", IsActionable = false },
            new DynamicMenuItem { ShortcutHint = "E", IsHeader = true },
            new DynamicMenuItem { ShortcutHint = "E", IsSeparator = true }
        })
        {
            var item = new MenuItem { Header = "Original" };
            QuickNavigationMenuKeyHandler.AttachShortcut(item, data, () => Assert.Fail("Must not run"));
            Assert.AreEqual("Original", item.Header);
            Assert.AreEqual("", AutomationProperties.GetAcceleratorKey(item));
        }
    }

    [StaTestMethod]
    public void Scope_RootFocusWorksBeforeAnItemIsSelectedAndRejectsForeignFocus()
    {
        var root = new ContextMenu();
        var child = new MenuItem();
        root.Items.Add(child);
        Assert.AreSame(root, QuickNavigationMenuKeyHandler.FindShortcutScope(root, root));
        Assert.AreSame(root, QuickNavigationMenuKeyHandler.FindShortcutScope(root, child));
        Assert.IsNull(QuickNavigationMenuKeyHandler.FindShortcutScope(root, new TextBox()));
        Assert.IsNull(QuickNavigationMenuKeyHandler.FindShortcutScope(root, new MenuItem()));
        Assert.IsNull(QuickNavigationMenuKeyHandler.FindShortcutScope(root, new ContextMenu()));
        Assert.IsNull(QuickNavigationMenuKeyHandler.FindShortcutScope(root, null));
    }

    [StaTestMethod]
    public void Scope_DoesNotMatchAChildOfAClosedSubmenu()
    {
        var root = new ContextMenu();
        var parent = new MenuItem();
        var child = new MenuItem();
        parent.Items.Add(child);
        root.Items.Add(parent);
        Assert.IsNull(QuickNavigationMenuKeyHandler.FindShortcutScope(root, child));
    }
}
