using System.IO;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FolderCascader.Navigation;

internal static class RecentFoldersMenu
{
    internal const string HandlePath = "foldercascader://recent-folders";
    internal const int RootLimit = 15;

    internal static void AppendRoot(List<DynamicMenuItem> items, Provider provider, bool historyShown,
        RecentFoldersSnapshot? snapshot = null, Func<string, bool>? exists = null)
    {
        // Partition once per popup so visits recorded before submenu expansion cannot shift its boundary.
        var folders = Build(provider, snapshot ?? RecentFoldersService.GetSnapshot(), exists);
        provider.RecentFolderSubmenu = folders.Skip(RootLimit).ToArray();
        if (provider.RecentFolderSubmenu.Count == 0)
            provider.RecentFolderSubmenu = [new DynamicMenuItem { Text = TranslationService.Get("FolderCascader_NoRecentFolders"), IsDisabled = true }];

        if (historyShown) items.Add(new DynamicMenuItem { IsSeparator = true });
        items.Add(new DynamicMenuItem
        {
            Text = TranslationService.Get("FolderCascader_RecentFolders"),
            HasSubMenu = true,
            SubMenuHandle = provider.AllocateHandle(HandlePath),
            HBitmapItem = IconBitmapCache.HistoryHBitmap
        });
        items.AddRange(folders.Take(RootLimit));
    }

    internal static List<DynamicMenuItem> Build(Provider provider, RecentFoldersSnapshot snapshot,
        Func<string, bool>? exists = null)
    {
        exists ??= Directory.Exists;
        var items = new List<DynamicMenuItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var limit = RootLimit + Math.Clamp(snapshot.MenuLimit, 1, 100);
        foreach (var entry in snapshot.Entries.OrderByDescending(entry => entry.OpenedUtcTicks))
        {
            if (items.Count >= limit) break;
            if (string.IsNullOrWhiteSpace(entry.Path) || !seen.Add(entry.Path) || !exists(entry.Path)) continue;
            items.Add(new DynamicMenuItem
            {
                Text = MenuBuilder.GetDisplayName(entry.Path, "") + $" ({entry.Path})",
                HasSubMenu = true,
                SubMenuHandle = provider.AllocateHandle(entry.Path)
            });
        }
        return items;
    }
}
