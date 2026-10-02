using System.IO;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FolderCascader.Navigation;

internal static class RecentFoldersMenu
{
    internal const string HandlePath = "foldercascader://recent-folders";

    internal static void AppendRoot(List<DynamicMenuItem> items, Provider provider, bool historyShown)
    {
        if (historyShown) items.Add(new DynamicMenuItem { IsSeparator = true });
        items.Add(new DynamicMenuItem
        {
            Text = TranslationService.Get("FolderCascader_RecentFolders"),
            HasSubMenu = true,
            SubMenuHandle = provider.AllocateHandle(HandlePath),
            HBitmapItem = IconBitmapCache.HistoryHBitmap
        });
    }

    internal static List<DynamicMenuItem> Build(Provider provider, RecentFoldersSnapshot snapshot,
        Func<string, bool>? exists = null)
    {
        exists ??= Directory.Exists;
        var items = new List<DynamicMenuItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot.Entries)
        {
            if (items.Count >= Math.Clamp(snapshot.MenuLimit, 1, 100)) break;
            if (string.IsNullOrWhiteSpace(entry.Path) || !seen.Add(entry.Path) || !exists(entry.Path)) continue;
            items.Add(new DynamicMenuItem
            {
                Text = MenuBuilder.GetDisplayName(entry.Path, "") + $" ({entry.Path})",
                HasSubMenu = true,
                SubMenuHandle = provider.AllocateHandle(entry.Path)
            });
        }
        if (items.Count == 0)
            items.Add(new DynamicMenuItem { Text = TranslationService.Get("FolderCascader_NoRecentFolders"), IsDisabled = true });
        return items;
    }
}
