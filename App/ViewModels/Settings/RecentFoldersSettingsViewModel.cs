using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.ViewModels.Settings;

public sealed class RecentFoldersSettingsViewModel : ViewModelBase, IDataErrorInfo
{
    private readonly UserSettings _userSettings;
    private readonly RecentFoldersStore _store;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly RecentFoldersSettings _options;
    private string _excludedDirectories, _searchText = string.Empty;
    private int _refreshPending;
    private bool _disposed;
    private bool _isExclusionsExpanded;

    public RecentFoldersSettingsViewModel(UserSettings settings, RecentFoldersStore? store = null)
    {
        _userSettings = settings;
        _store = store ?? RecentFoldersStore.Instance;
        _options = (settings.RecentFolders ?? new()).CopyValidated();
        _excludedDirectories = string.Join(Environment.NewLine, _options.ExcludedDirectories);
        RemoveCommand = new RelayCommand<RecentFolderRow>(item => { if (item != null) _store.Remove(item.Path); });
        ClearCommand = new RelayCommand(() => _store.Remove());
        BrowseExclusionCommand = new RelayCommand(BrowseExclusion);
        _store.Changed += OnStoreChanged;
        Refresh();
    }

    public bool Enabled { get => _options.Enabled; set { _options.Enabled = value; OnPropertyChanged(); } }
    public int Capacity
    {
        get => _options.Capacity;
        set { _options.Capacity = value; OnPropertyChanged(); OnPropertyChanged(nameof(MenuLimit)); }
    }
    public int MenuLimit { get => _options.MenuLimit; set { _options.MenuLimit = value; OnPropertyChanged(); } }
    public string ExcludedDirectories
    {
        get => _excludedDirectories;
        set => SetProperty(ref _excludedDirectories, value);
    }
    public bool IsExclusionsExpanded { get => _isExclusionsExpanded; set => SetProperty(ref _isExclusionsExpanded, value); }
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) Refresh(); }
    }
    public ObservableCollection<RecentFolderRow> Items { get; } = new();
    public ICommand RemoveCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand BrowseExclusionCommand { get; }

    public string Error => string.Empty;
    public string this[string name] => name switch
    {
        nameof(Capacity) when Capacity is < 1 or > 5000 => TranslationManager.Instance["RecentFolders_CapacityError"],
        nameof(MenuLimit) when MenuLimit < 1 || MenuLimit > Math.Min(Capacity, 100) => TranslationManager.Instance["RecentFolders_MenuLimitError"],
        nameof(ExcludedDirectories) when ExclusionLines().Any(p => RecentFolderPaths.Normalize(p) == null) => TranslationManager.Instance["RecentFolders_ExclusionError"],
        _ => string.Empty
    };

    private IEnumerable<string> ExclusionLines() => ExcludedDirectories.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private void BrowseExclusion()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        ExcludedDirectories = string.Join(Environment.NewLine, ExclusionLines().Concat(dialog.FolderNames).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void OnStoreChanged()
    {
        if (_disposed) return;
        if (_dispatcher.CheckAccess()) { Refresh(); return; }
        if (Interlocked.Exchange(ref _refreshPending, 1) != 0) return;
        _dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref _refreshPending, 0);
            if (!_disposed) Refresh();
        }), DispatcherPriority.Background);
    }

    private void Refresh()
    {
        Items.Clear();
        foreach (var entry in _store.GetSnapshot().Entries)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(entry.Path));
            if (string.IsNullOrEmpty(name)) name = entry.Path;
            if (!string.IsNullOrWhiteSpace(SearchText)
                && !FuzzyMatcher.ComputeBestMatch(SearchText, name, new[] { entry.Path }).IsMatch) continue;
            var opened = new DateTime(entry.OpenedUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("g");
            Items.Add(new(entry.Path, name, opened));
        }
    }

    public void Save()
    {
        if (new[] { nameof(Capacity), nameof(MenuLimit), nameof(ExcludedDirectories) }.Any(p => this[p].Length != 0)) return;
        _options.ExcludedDirectories = ExclusionLines().ToList();
        _userSettings.RecentFolders = _options.CopyValidated();
        _store.ApplySettings(_userSettings.RecentFolders);
    }

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(Capacity));
        OnPropertyChanged(nameof(MenuLimit));
        OnPropertyChanged(nameof(ExcludedDirectories));
        Refresh();
    }

    public void Cleanup() { _disposed = true; _store.Changed -= OnStoreChanged; }
}

public sealed record RecentFolderRow(string Path, string Name, string LastOpened);
