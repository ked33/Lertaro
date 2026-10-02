using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lertaro.App.Helpers;
using Lertaro.App.Services;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

/// <summary>
/// Manages panel-height layout math for QuickSearchWindow -- actions list height and results list
/// height. Ctrl+N shortcut-hint labeling is a separate concern handled by QuickSearchShortcutHelper
/// (still triggered from here after a resize, since that's when rows scroll into/out of view).
/// </summary>
internal sealed class QuickSearchWindowLayoutManager
{
    private readonly Lertaro.App.QuickSearchWindow _window;
    private int _layoutUpdateQueued;

    internal QuickSearchWindowLayoutManager(Lertaro.App.QuickSearchWindow window) => _window = window;

    public void UpdateActionsLayout()
    {
        _window.LstActions.Height = double.NaN;
        QueueResultsLayoutUpdate();
    }

    // Coalesce collection notifications and let ItemContainerGenerator finish before updating heights.
    // Normal runs before Render, so WPF's next layout sees the new dimensions. Do not force UpdateLayout
    // or toggle SizeToContent here: both synchronously enter layout/UIA while results are being applied.
    // In particular, DisableProcessing around that work starved COM cleanup in the captured input stall
    // (466 short waits totaling 7.44s). WPF owns layout re-entrancy and must be free to pump COM messages.
    public void QueueResultsLayoutUpdate()
    {
        if (Interlocked.Exchange(ref _layoutUpdateQueued, 1) == 1)
            return;

        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref _layoutUpdateQueued, 0);
            ApplyResultsLayout();
        }), DispatcherPriority.Normal);
    }

    // Only updates dimensions; SizeToContent handles the next layout. The initial show explicitly calls
    // UpdateLayout AFTER this computation so its position is based on the new panel height.
    public void ApplyResultsLayout()
    {
        if (_window.ResultsPanelControl.ActionsGrid.Visibility == Visibility.Visible)
        {
            ApplyActionsResultHeight();
            return;
        }

        // Sum each visible row's own height rather than assuming a uniform row size -- a section
        // header, the "show more" row, or a row whose icon forces it to grow past the base height
        // (see MinHeight in ListBox.xaml) would otherwise throw off a single-height-times-count guess,
        // leaving stray blank space (or clipping) at the bottom of the list.
        var results = _window.ViewModel.Results;
        var visibleCount = Math.Min(results.Count, UiMetrics.QuickSearchMaxVisibleResultRows);

        double resultsHeight = 0;
        for (var i = 0; i < visibleCount; i++)
        {
            resultsHeight += results[i].ScaledItemHeight;
        }

        // Item-based virtualization throughout, which realizes only the ~9 visible rows rather than the
        // full ~50-row result set. It was switched to pixel-based scrolling for one case: the startup
        // panel's tab strip sat stacked above this list, and the height left over after it was rarely a
        // whole multiple of the row height, so the boundary row had to be clipped rather than dropped.
        // Nothing sits above the list any more.
        ScrollViewer.SetCanContentScroll(_window.LstResults, true);

        // Hints need refreshing even when the height is unchanged. ScrollChanged refreshes them again
        // after layout if resizing changes the visible rows.
        UpdateShortcutHints();

        if (_window.LstResults.Height == resultsHeight && _window.ResultsPanelControl.Height == resultsHeight)
            return;

        _window.LstResults.Height = resultsHeight;
        _window.ResultsPanelControl.Height = resultsHeight;
    }

    private void ApplyActionsResultHeight()
    {
        // Keep the floating actions menu's normal nine-row budget even for a short result set.
        var maxResultHeight = UiMetrics.ScaledQuickSearchMaxResultHeight;
        if (_window.LstResults.Height == maxResultHeight && _window.ResultsPanelControl.Height == maxResultHeight)
            return;

        _window.LstResults.Height = maxResultHeight;
        _window.ResultsPanelControl.Height = maxResultHeight;
    }

    public void UpdateShortcutHints() =>
        QuickSearchShortcutHelper.UpdateShortcutHints(_window, WpfUiHelper.GetScrollViewer(_window.LstResults));
}
