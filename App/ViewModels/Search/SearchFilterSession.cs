using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services.Plugin;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.ViewModels.Search;

public sealed class SearchFilterSession : ViewModelBase
{
    private readonly Action _refresh;
    public SearchFilterShortcut? Active { get; private set; }
    public string Keyword => Active?.Keyword ?? string.Empty;
    public bool IsActive => Active != null;
    public string Hint => Active == null ? string.Empty : string.Format(
        Lertaro.PluginSdk.Services.TranslationService.Get("CoreExtensions_FilterShortcut_Hint"),
        Active.Keyword, Active.Rule, HotkeyStringFormat.ToDisplayText(Active.Hotkey));
    public ICommand ClearCommand { get; }

    public SearchFilterSession(Action refresh)
    {
        _refresh = refresh;
        ClearCommand = new RelayCommand(() => Set(null));
    }

    internal static List<SearchFilterShortcut> GetShortcuts() => PluginManager.Instance.QueryTokenProviders
        .SelectMany(provider => PluginPerformanceMonitor.Measure(provider, () => provider.GetFilterShortcuts().ToList())).ToList();

    public void Set(SearchFilterShortcut? filter, bool refresh = true)
    {
        if (Active == filter) return;
        Active = filter;
        OnPropertyChanged(nameof(Active));
        OnPropertyChanged(nameof(Keyword));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(Hint));
        if (refresh) _refresh();
    }

    internal bool TryToggle(Key key, ModifierKeys modifiers, bool repeat, IEnumerable<SearchFilterShortcut> shortcuts)
    {
        var matches = shortcuts.Where(f => WpfUiHelper.MatchesHotkey(f.Hotkey, modifiers, key)).Take(2).ToList();
        if (matches.Count != 1) return false;
        if (!repeat) Set(string.Equals(Active?.Token, matches[0].Token, StringComparison.OrdinalIgnoreCase) ? null : matches[0]);
        return true;
    }

    internal IReadOnlyList<string> WithTokens(IReadOnlyList<string> tokens) => Active == null
        ? tokens
        : tokens.Append(Active.Token).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal void Reconcile()
    {
        if (Active != null)
            Set(GetShortcuts().FirstOrDefault(f => string.Equals(f.Token, Active.Token, StringComparison.OrdinalIgnoreCase)), refresh: false);
    }
}
