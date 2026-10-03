using System.Windows;
using System.Windows.Input;
using Lertaro.App.Services.AppWindow;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;

namespace Lertaro.App.Helpers;

internal static class SearchFilterHotkeySupport
{
    internal static SearchFilterSession? GetSession(object? context) => context switch
    {
        QuickSearchViewModel vm when !vm.IsInlineSearchContext => vm.FilterSession,
        SearchViewModel vm => vm.FilterSession,
        _ => null
    };

    internal static bool TryHandle(System.Windows.Input.KeyEventArgs e, ISearchWindow window)
    {
        if (window.IsInActionsMode || window is not Window { IsActive: true } host
            || GetSession(host.DataContext) is not { } session || Keyboard.Modifiers == ModifierKeys.None)
            return false;
        var key = WpfUiHelper.GetActualKey(e);
        var shortcuts = SearchFilterSession.GetShortcuts();
        var match = shortcuts.FirstOrDefault(f => WpfUiHelper.MatchesHotkey(f.Hotkey, Keyboard.Modifiers, key));
        if (match == null || SearchFilterShortcutValidation.FindReservedOwner(match.Hotkey, UserSettings.Load().Hotkeys) != null)
            return false;
        if (!session.TryToggle(key, Keyboard.Modifiers, e.IsRepeat, shortcuts)) return false;
        e.Handled = true;
        return true;
    }
}
