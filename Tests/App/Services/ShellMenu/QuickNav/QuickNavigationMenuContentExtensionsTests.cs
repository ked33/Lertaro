using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.App.Converters;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services.ShellMenu.QuickNav;

namespace Lertaro.App.Tests.Services.ShellMenu.QuickNav;

// Regression coverage for GitHub issue #184: a long file/folder name used to make the whole cascading
// ContextMenu/Popup auto-size to fit it, dragging every other row's column out just as wide.
// CreateItemHeader is what caps a single row's own text width and wires in the same MarqueeBehavior
// DataTemplates.xaml's search-result rows already use, so hovering an overflowing row still reveals its
// full name instead of leaving it permanently cut off.
[TestClass]
public sealed class QuickNavigationMenuContentExtensionsTests
{
    [StaTestMethod]
    public void CreateItemHeader_CapsWidthToMaxItemTextWidth()
    {
        var header = QuickNavigationMenuContentExtensions.CreateItemHeader("a-reasonably-long-folder-name");

        Assert.AreEqual(220, header.MaxWidth);
    }

    [StaTestMethod]
    public void CreateItemHeader_HidesHorizontalScrollbarButStillClips()
    {
        var header = QuickNavigationMenuContentExtensions.CreateItemHeader("some text");

        Assert.AreEqual(ScrollBarVisibility.Hidden, header.HorizontalScrollBarVisibility);
        Assert.AreEqual(ScrollBarVisibility.Disabled, header.VerticalScrollBarVisibility);
    }

    [StaTestMethod]
    public void CreateItemHeader_NotFocusable_SoTabOrderSkipsIt()
    {
        var header = QuickNavigationMenuContentExtensions.CreateItemHeader("some text");

        Assert.IsFalse(header.Focusable);
    }

    [StaTestMethod]
    public void CreateItemHeader_ContentIsTextBlockWithTheGivenText()
    {
        var header = QuickNavigationMenuContentExtensions.CreateItemHeader("Projects (very long client name here)");

        var textBlock = header.Content as TextBlock;
        Assert.IsNotNull(textBlock);
        Assert.AreEqual("Projects (very long client name here)", textBlock.Text);
    }

    [StaTestMethod]
    public void CreateItemHeader_TextTrimmingIsNone_SoMarqueeCanRevealTheFullName()
    {
        var header = QuickNavigationMenuContentExtensions.CreateItemHeader("some text");

        var textBlock = (TextBlock)header.Content;
        Assert.AreEqual(TextTrimming.None, textBlock.TextTrimming);
    }

    [StaTestMethod]
    public void CreateItemHeader_EnablesMarqueeOnTheTextBlock()
    {
        var header = QuickNavigationMenuContentExtensions.CreateItemHeader("some text");

        var textBlock = (TextBlock)header.Content;
        Assert.IsTrue(MarqueeBehavior.GetEnableMarquee(textBlock));
    }

    [StaTestMethod]
    public void CreateItemHeader_BubblesMouseWheelToTheMenuScroller()
    {
        var header = QuickNavigationMenuContentExtensions.CreateItemHeader("some text");

        Assert.IsTrue(ScrollViewerHelper.GetBubbleMouseWheel(header));
    }

    [StaTestMethod]
    public void MiddleClick_CompleteClick_OpensOnceWithoutOrdinaryClick()
    {
        var row = new MenuItem();
        var opens = 0;
        var ordinaryClicks = 0;
        row.Click += (s, e) => ordinaryClicks++;
        QuickNavigationMenuContentExtensions.AttachMiddleClick(row, Folder(), true, () => opens++);

        Assert.IsTrue(RaiseButton(row, UIElement.PreviewMouseDownEvent).Handled);
        Assert.AreEqual(0, opens);
        Assert.IsTrue(RaiseButton(row, UIElement.PreviewMouseUpEvent).Handled);
        RaiseButton(row, UIElement.PreviewMouseUpEvent);

        Assert.AreEqual(1, opens);
        Assert.AreEqual(0, ordinaryClicks);
    }

    [StaTestMethod]
    public void MiddleClick_AndShortcut_UseTheSameOpenActionWithoutOrdinaryClick()
    {
        var menu = new ContextMenu();
        var row = new MenuItem();
        menu.Items.Add(row);
        var opens = 0;
        var ordinaryClicks = 0;
        row.Click += (s, e) => ordinaryClicks++;
        var folder = Folder();
        folder.ShortcutHint = "E";
        QuickNavigationMenuContentExtensions.AttachMiddleClick(row, folder, true, () => opens++);

        Assert.IsTrue(QuickNavigationMenuKeyHandler.TryInvokeShortcut(menu, Key.E, ModifierKeys.None, false));
        Assert.AreEqual(1, opens);
        RaiseButton(row, UIElement.PreviewMouseDownEvent);
        RaiseButton(row, UIElement.PreviewMouseUpEvent);
        Assert.AreEqual(2, opens);
        Assert.AreEqual(0, ordinaryClicks);
    }

    [StaTestMethod]
    public void MiddleClick_ReleaseThatOpenedMenu_DoesNotOpenFolder()
    {
        var row = new MenuItem();
        var opens = 0;
        QuickNavigationMenuContentExtensions.AttachMiddleClick(row, Folder(), true, () => opens++);

        Assert.IsTrue(RaiseButton(row, UIElement.PreviewMouseUpEvent).Handled);

        Assert.AreEqual(0, opens);
    }

    [StaTestMethod]
    [DataRow(false, true, true, false)] // File or command, even if it has an available path.
    [DataRow(true, false, true, false)] // Category or a continuation page of a real folder.
    [DataRow(true, true, false, false)] // Unavailable folder or missing provider path.
    [DataRow(true, true, true, true)] // Disabled provider item.
    public void MiddleClick_AndShortcut_NonFolderOrUnavailableItem_DoNotOpen(
        bool hasSubMenu, bool actionable, bool available, bool disabled)
    {
        var row = new MenuItem();
        var menu = new ContextMenu();
        menu.Items.Add(row);
        var opens = 0;
        var item = new DynamicMenuItem { HasSubMenu = hasSubMenu, IsActionable = actionable, IsDisabled = disabled, ShortcutHint = "E" };
        QuickNavigationMenuContentExtensions.AttachMiddleClick(row, item, available, () => opens++);

        Assert.IsTrue(RaiseButton(row, UIElement.PreviewMouseDownEvent).Handled);
        Assert.IsTrue(RaiseButton(row, UIElement.PreviewMouseUpEvent).Handled);
        QuickNavigationMenuKeyHandler.TryInvokeShortcut(menu, Key.E, ModifierKeys.None, false);

        Assert.AreEqual(0, opens);
    }

    [StaTestMethod]
    public void MiddleClick_CascadingFolder_OnlyOpensClickedChild()
    {
        var parent = new MenuItem();
        var child = new MenuItem();
        parent.Items.Add(child);
        var parentOpens = 0;
        var childOpens = 0;
        QuickNavigationMenuContentExtensions.AttachMiddleClick(parent, Folder(), true, () => parentOpens++);
        QuickNavigationMenuContentExtensions.AttachMiddleClick(child, Folder(), true, () => childOpens++);

        RaiseButton(child, UIElement.PreviewMouseDownEvent);
        RaiseButton(child, UIElement.PreviewMouseUpEvent);
        // If the child's down had armed its ancestor, this release would incorrectly open the parent.
        RaiseButton(parent, UIElement.PreviewMouseUpEvent);

        Assert.AreEqual(1, childOpens);
        Assert.AreEqual(0, parentOpens);
    }

    [StaTestMethod]
    [DataRow(MouseButton.Left)]
    [DataRow(MouseButton.Right)]
    [DataRow(MouseButton.XButton1)]
    public void MiddleClick_OtherButtons_KeepTheirExistingHandlers(MouseButton button)
    {
        var row = new MenuItem();
        var opens = 0;
        QuickNavigationMenuContentExtensions.AttachMiddleClick(row, Folder(), true, () => opens++);

        Assert.IsFalse(RaiseButton(row, UIElement.PreviewMouseDownEvent, button).Handled);
        Assert.IsFalse(RaiseButton(row, UIElement.PreviewMouseUpEvent, button).Handled);

        Assert.AreEqual(0, opens);
    }

    [StaTestMethod]
    public void MiddleClick_LeavingRowBeforeRelease_CancelsOpen()
    {
        var row = new MenuItem();
        var opens = 0;
        QuickNavigationMenuContentExtensions.AttachMiddleClick(row, Folder(), true, () => opens++);

        RaiseButton(row, UIElement.PreviewMouseDownEvent);
        row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseLeaveEvent });
        RaiseButton(row, UIElement.PreviewMouseUpEvent);

        Assert.AreEqual(0, opens);
    }

    [StaTestMethod]
    public void MiddleClick_UnloadingMenuBeforeRelease_CancelsOpen()
    {
        var row = new MenuItem();
        var opens = 0;
        QuickNavigationMenuContentExtensions.AttachMiddleClick(row, Folder(), true, () => opens++);

        RaiseButton(row, UIElement.PreviewMouseDownEvent);
        row.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        RaiseButton(row, UIElement.PreviewMouseUpEvent);

        Assert.AreEqual(0, opens);
    }

    [StaTestMethod]
    public void MiddleClick_DisabledBeforeRelease_DoesNotOpen()
    {
        var row = new MenuItem();
        var opens = 0;
        QuickNavigationMenuContentExtensions.AttachMiddleClick(row, Folder(), true, () => opens++);

        RaiseButton(row, UIElement.PreviewMouseDownEvent);
        row.IsEnabled = false;
        RaiseButton(row, UIElement.PreviewMouseUpEvent);

        Assert.AreEqual(0, opens);
    }

    private static DynamicMenuItem Folder() => new() { HasSubMenu = true };

    private static MouseButtonEventArgs RaiseButton(MenuItem row, RoutedEvent routedEvent, MouseButton button = MouseButton.Middle)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button) { RoutedEvent = routedEvent };
        row.RaiseEvent(args);
        return args;
    }
}
