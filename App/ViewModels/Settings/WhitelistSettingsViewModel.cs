using System.ComponentModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings;

public sealed class WhitelistSettingsViewModel : ViewModelBase, IDataErrorInfo
{
    private readonly UserSettings _settings;
    private string _paths;

    public WhitelistSettingsViewModel(UserSettings settings)
    {
        _settings = settings;
        _paths = string.Join(Environment.NewLine, settings.WhitelistedPaths);
        BrowseCommand = new RelayCommand(Browse);
    }

    public string Paths
    {
        get => _paths;
        set
        {
            if (SetProperty(ref _paths, value ?? string.Empty))
                OnPropertyChanged(nameof(ValidationMessage));
        }
    }

    public ICommand BrowseCommand { get; }
    public bool IsValid => Lines().All(path => PathWhitelist.TryNormalizeDirectory(path, out _));
    public string Error => string.Empty;
    public string this[string name] => name == nameof(Paths) && !IsValid
        ? TranslationManager.Instance["Whitelist_InvalidPath"] : string.Empty;
    public string ValidationMessage => this[nameof(Paths)];

    public void Save()
    {
        if (!IsValid)
            throw new ArgumentException("Whitelist entries must be absolute directory paths.");
        _settings.WhitelistedPaths = Lines().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IEnumerable<string> Lines() => Paths
        .Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(path => path.Trim('"'));

    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = true };
        if (dialog.ShowDialog() != true)
            return;
        Paths = string.Join(Environment.NewLine, Lines().Concat(dialog.FolderNames).Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
