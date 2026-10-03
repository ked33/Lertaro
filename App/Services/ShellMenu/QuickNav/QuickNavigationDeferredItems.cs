using System.Windows;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using System.Windows.Input;
using System.Windows.Threading;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Services.ShellMenu.QuickNav;

internal static class QuickNavigationDeferredItems
{
    internal static void Attach(ContextMenu menu, MenuItem row, DynamicMenuItem item,
        Func<DynamicMenuItem, MenuItem> createRow, Func<bool> isCurrent, FrameworkElement? separator = null)
    {
        if (separator != null) separator.Visibility = row.Visibility;
        var cancellation = new CancellationTokenSource();
        DynamicMenuItem? pending = null;
        var ready = false;
        var detached = false;

        void Detach()
        {
            if (detached) return;
            detached = true;
            menu.Opened -= Opened;
            menu.Closed -= Closed;
            menu.MouseMove -= Retry;
            menu.MouseLeave -= Retry;
            menu.PreviewMouseUp -= Retry;
            menu.PreviewKeyUp -= Retry;
            menu.RemoveHandler(MenuItem.SubmenuClosedEvent, new RoutedEventHandler(Retry));
            cancellation.Cancel();
            cancellation.Dispose();
        }

        void Apply()
        {
            if (detached || !ready || !menu.IsOpen || !isCurrent()) return;
            var index = menu.Items.IndexOf(row);
            if (index < 0) { Detach(); return; }
            if (pending == null && row.Visibility == Visibility.Collapsed)
            {
                menu.Items.RemoveAt(index);
                if (separator != null) menu.Items.Remove(separator);
                UpdateSeparators(menu);
                Detach();
                return;
            }
            if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.MiddleButton == MouseButtonState.Pressed
                || Mouse.RightButton == MouseButtonState.Pressed) return;
            var reservedSlot = pending != null && row.Visibility == Visibility.Visible && !row.IsEnabled;
            if (!reservedSlot && !CanChange(menu)) return;
            // Build before removing the marker, so a rendering failure cannot corrupt the menu.
            var replacement = pending == null ? null : createRow(pending);
            if (reservedSlot && replacement != null)
            {
                // Populate a disabled loading slot in place, even while another row is being used.
                // Its measured size prevents screen-edge popup repositioning and accidental clicks.
                if (row.ActualHeight > 0) replacement.Height = row.ActualHeight;
                if (row.ActualWidth > 0) replacement.Width = row.ActualWidth;
            }
            menu.Items.RemoveAt(index);
            if (replacement != null)
            {
                menu.Items.Insert(index, replacement);
                if (separator != null) separator.Visibility = Visibility.Visible;
            }
            else if (separator != null) menu.Items.Remove(separator);
            UpdateSeparators(menu);
            Detach();
        }

        void Retry(object? sender, RoutedEventArgs e) => menu.Dispatcher.BeginInvoke(new Action(Apply), DispatcherPriority.Background);
        void Closed(object? sender, RoutedEventArgs e) => Detach();
        void Opened(object? sender, RoutedEventArgs e)
        {
            menu.Opened -= Opened;
            // Render and input run first, even when a capture completed synchronously.
            menu.Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (detached || !menu.IsOpen || !isCurrent()) return;
                try
                {
                    pending = await item.LoadDeferredItem!(cancellation.Token);
                    if (detached || !menu.IsOpen || !isCurrent()) return;
                    if (ReferenceEquals(pending, item)) { Detach(); return; }
                    ready = true;
                    Apply();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Core.Logger.Log($"[QuickNavigation] Deferred item failed: {ex.Message}", Core.LogLevel.Warn);
                    pending = null;
                    ready = true;
                    Apply();
                }
            }), DispatcherPriority.Background);
        }

        menu.Opened += Opened;
        menu.Closed += Closed;
        menu.MouseMove += Retry;
        menu.MouseLeave += Retry;
        menu.PreviewMouseUp += Retry;
        menu.PreviewKeyUp += Retry;
        menu.AddHandler(MenuItem.SubmenuClosedEvent, new RoutedEventHandler(Retry));
    }

    // Optional rows do not leave leading, trailing or duplicate separators while they are hidden.
    internal static void UpdateSeparators(ContextMenu menu)
    {
        var remaining = menu.Items.OfType<MenuItem>().Count(row => row.Visibility == Visibility.Visible);
        var hasRow = false;
        foreach (var entry in menu.Items)
        {
            if (entry is MenuItem { Visibility: Visibility.Visible })
            {
                remaining--;
                hasRow = true;
            }
            else if (entry is System.Windows.Controls.Separator divider)
            {
                divider.Visibility = hasRow && remaining > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (divider.Visibility == Visibility.Visible) hasRow = false;
            }
        }
    }

    // Growing a popup near a screen edge can reposition the whole popup, not just its tail.
    // Wait while an actionable row is being pointed at or navigated, or a submenu is open.
    // Event-driven retries avoid a polling timer while the user browses an existing submenu.
    internal static bool CanChange(ContextMenu menu) =>
        Mouse.LeftButton != MouseButtonState.Pressed && Mouse.MiddleButton != MouseButtonState.Pressed
        && Mouse.RightButton != MouseButtonState.Pressed
        && !menu.Items.OfType<MenuItem>()
            .Any(row => row.IsSubmenuOpen || row.IsKeyboardFocusWithin || (row.Focusable && row.IsMouseOver));
}
