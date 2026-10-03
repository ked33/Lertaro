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
    public void PendingItemDoesNotBlockOpeningAndIsInsertedAtItsReservedPosition()
    {
        var completion = new TaskCompletionSource<DynamicMenuItem?>();
        var menu = Menu();
        var marker = new MenuItem { Header = "Loading", IsEnabled = false, Focusable = false };
        var separator = new Separator();
        menu.Items.Add(marker);
        menu.Items.Add(separator);
        menu.Items.Add(new MenuItem { Header = "Recent", Focusable = false });
        QuickNavigationDeferredItems.Attach(menu, marker, new() { Text = "Loading", IsDisabled = true, LoadDeferredItem = _ => completion.Task },
            item => new MenuItem { Header = item.Text }, () => true, separator);
        try
        {
            menu.IsOpen = true;
            Assert.IsTrue(menu.IsOpen);
            Assert.IsFalse(completion.Task.IsCompleted);
            PumpUntil(() => marker.ActualHeight > 0);
            var reservedHeight = marker.ActualHeight;
            completion.SetResult(new() { Text = "Hovered" });
            PumpUntil(() => menu.Items[0] is MenuItem row && Equals(row.Header, "Hovered"));
            Assert.AreEqual(reservedHeight, ((MenuItem)menu.Items[0]).Height);
            Assert.AreSame(separator, menu.Items[1]);
            Assert.AreEqual("Recent", ((MenuItem)menu.Items[2]).Header);
        }
        finally { menu.IsOpen = false; }
    }

    [StaTestMethod]
    public void EmptyHoveredFolderRemovesItsSeparator()
    {
        var completion = new TaskCompletionSource<DynamicMenuItem?>();
        var menu = Menu();
        var marker = new MenuItem { Header = "Loading", IsEnabled = false, Focusable = false };
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
            completion.SetResult(null);
            PumpUntil(() => menu.Items.Count == 1);
            Assert.AreSame(current, menu.Items[0]);
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
