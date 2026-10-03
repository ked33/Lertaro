using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Lertaro.App.Services.ShellMenu.QuickNav;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Services.ShellMenu.QuickNav;

[TestClass]
[DoNotParallelize]
public sealed class QuickNavigationDeferredItemsTests
{
    [StaTestMethod]
    public void HiddenOptionalRowsDoNotLeaveVisibleSeparators()
    {
        var menu = Menu();
        var leading = new Separator();
        var between = new Separator();
        var trailing = new Separator();
        var optional = new MenuItem { Visibility = Visibility.Collapsed };
        menu.Items.Add(leading);
        menu.Items.Add(new MenuItem { Header = "Configured" });
        menu.Items.Add(between);
        menu.Items.Add(optional);
        menu.Items.Add(trailing);
        QuickNavigationDeferredItems.UpdateSeparators(menu);
        Assert.IsTrue(new[] { leading, between, trailing }.All(item => item.Visibility == Visibility.Collapsed));
        optional.Visibility = Visibility.Visible;
        QuickNavigationDeferredItems.UpdateSeparators(menu);
        Assert.AreEqual(Visibility.Visible, between.Visibility);
        Assert.AreEqual(Visibility.Collapsed, leading.Visibility);
        Assert.AreEqual(Visibility.Collapsed, trailing.Visibility);
    }

    [StaTestMethod]
    public void ConfirmedHoverRevealsItsRowAndSeparatorAfterOpening()
    {
        var completion = new TaskCompletionSource<DynamicMenuItem?>();
        var menu = Menu();
        var marker = new MenuItem { Visibility = Visibility.Collapsed, IsEnabled = false, Focusable = false };
        var separator = new Separator();
        menu.Items.Add(marker);
        menu.Items.Add(separator);
        menu.Items.Add(new MenuItem { Header = "Recent", Focusable = false });
        QuickNavigationDeferredItems.Attach(menu, marker, new() { IsDisabled = true, LoadDeferredItem = _ => completion.Task },
            item => new MenuItem { Header = item.Text }, () => true, separator);
        try
        {
            menu.IsOpen = true;
            Assert.IsTrue(menu.IsOpen);
            Assert.IsFalse(completion.Task.IsCompleted);
            PumpUntil(() => menu.ActualHeight > 0);
            Assert.AreEqual(Visibility.Collapsed, separator.Visibility);
            completion.SetResult(new() { Text = "Hovered" });
            PumpUntil(() => menu.Items[0] is MenuItem row && Equals(row.Header, "Hovered"));
            Assert.AreEqual(Visibility.Visible, ((MenuItem)menu.Items[0]).Visibility);
            Assert.AreEqual(Visibility.Visible, separator.Visibility);
            Assert.AreSame(separator, menu.Items[1]);
            Assert.AreEqual("Recent", ((MenuItem)menu.Items[2]).Header);
        }
        finally { menu.IsOpen = false; }
    }

    [StaTestMethod]
    public void EmptyHoveredFolderDoesNotChangeMenuHeight()
    {
        var completion = new TaskCompletionSource<DynamicMenuItem?>();
        var menu = Menu();
        var marker = new MenuItem { Visibility = Visibility.Collapsed, IsEnabled = false, Focusable = false };
        var separator = new Separator();
        var current = new MenuItem { Header = "Current", Focusable = false };
        menu.Items.Add(marker);
        menu.Items.Add(separator);
        menu.Items.Add(current);
        QuickNavigationDeferredItems.Attach(menu, marker, new() { LoadDeferredItem = _ => completion.Task },
            item => new MenuItem { Header = item.Text }, () => true, separator);
        try
        {
            menu.IsOpen = true;
            PumpUntil(() => menu.ActualHeight > 0);
            menu.UpdateLayout();
            var heightBefore = menu.ActualHeight;
            var rowPositionBefore = current.TranslatePoint(new Point(), menu);
            Assert.AreEqual(Visibility.Collapsed, separator.Visibility);
            completion.SetResult(null);
            PumpUntil(() => menu.Items.Count == 1);
            Assert.AreSame(current, menu.Items[0]);
            menu.UpdateLayout();
            Assert.AreEqual(heightBefore, menu.ActualHeight);
            Assert.AreEqual(rowPositionBefore, current.TranslatePoint(new Point(), menu));
        }
        finally { menu.IsOpen = false; }
    }

    [StaTestMethod]
    public void ClosedOrSupersededMenusIgnoreLateResults()
    {
        foreach (var close in new[] { true, false })
        {
            var completion = new TaskCompletionSource<DynamicMenuItem?>();
            var menu = Menu();
            var marker = new MenuItem { Visibility = Visibility.Collapsed, Focusable = false };
            menu.Items.Add(new MenuItem { Header = "Current", Focusable = false });
            menu.Items.Add(marker);
            var current = true;
            var started = false;
            var rendered = false;
            QuickNavigationDeferredItems.Attach(menu, marker, new()
            {
                LoadDeferredItem = _ => { started = true; return completion.Task; }
            }, item => { rendered = true; return new MenuItem { Header = item.Text }; }, () => current);
            try
            {
                menu.IsOpen = true;
                PumpUntil(() => started);
                if (close) menu.IsOpen = false;
                else current = false;
                completion.SetResult(new() { Text = "Stale" });
                var drained = false;
                menu.Dispatcher.BeginInvoke(new Action(() => drained = true), DispatcherPriority.ApplicationIdle);
                PumpUntil(() => drained);
                Assert.IsFalse(rendered);
                Assert.AreSame(marker, menu.Items[1]);
            }
            finally { menu.IsOpen = false; }
        }
    }

    private static ContextMenu Menu() => new()
    {
        Placement = PlacementMode.AbsolutePoint, HorizontalOffset = 80, VerticalOffset = 80,
        StaysOpen = true
    };

    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || watch.ElapsedMilliseconds > 3000) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.IsTrue(condition(), "The deferred menu update did not finish.");
    }
}
