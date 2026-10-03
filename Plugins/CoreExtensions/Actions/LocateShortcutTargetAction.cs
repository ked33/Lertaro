using System.IO;
using System.Windows;
using System.Windows.Media;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Actions;

public sealed class LocateShortcutTargetAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_BuiltinGroup");
    public string DisplayName => TranslationService.Get("Action_LocateShortcutTarget");
    public string Description => TranslationService.Get("Action_LocateShortcutTarget_Desc");
    public string Hotkey => string.Empty;

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M3.9 12c0-1.7 1.4-3.1 3.1-3.1h4V7H7a5 5 0 0 0 0 10h4v-1.9H7c-1.7 0-3.1-1.4-3.1-3.1z M8 13h8v-2H8v2z M17 7h-4v1.9h4a3.1 3.1 0 0 1 0 6.2h-4V17h4a5 5 0 0 0 0-10z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count > 0 && results.All(r =>
        r != null && !r.IsDir && !string.IsNullOrWhiteSpace(r.FullPath)
        && Path.GetExtension(r.FullPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
        && PathExistenceCache.ExistsResult(r));

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view)
    {
        if (!CanExecute(results)) return;

        foreach (var result in results)
        {
            var target = StartMenuShortcutResolver.ResolveShortcutTarget(result.FullPath);
            if (string.IsNullOrWhiteSpace(target) || (!File.Exists(target) && !Directory.Exists(target)))
            {
                PluginMessageBoxService.Show(
                    string.Format(TranslationService.Get("Action_LocateShortcutTarget_Failed"), result.FullPath),
                    DisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }

            view.LocateInExplorerExternal(target);
        }
    }
}
