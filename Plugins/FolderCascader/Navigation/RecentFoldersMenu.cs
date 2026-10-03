using System.IO;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FolderCascader.Navigation;

internal static class RecentFoldersMenu
{
    internal const string HandlePath = "foldercascader://recent-folders";
    internal const int RootLimit = 15;

    internal static void AppendRoot(List<DynamicMenuItem> items, Provider provider, bool historyShown,
        RecentFoldersSnapshot? snapshot = null)
    {
        snapshot ??= RecentFoldersService.GetSnapshot();
        // Capture metadata only. Preserve the root/submenu boundary for this popup even if new visits arrive.
        var entries = snapshot.Entries.OrderByDescending(entry => entry.OpenedUtcTicks)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        provider.RecentFolderSnapshot = new(entries, Math.Clamp(snapshot.MenuLimit, 1, 100));
        if (historyShown) items.Add(new DynamicMenuItem { IsSeparator = true });
        items.Add(new DynamicMenuItem
        {
            Text = TranslationService.Get("FolderCascader_RecentFolders"), HasSubMenu = true,
            SubMenuHandle = provider.AllocateHandle(HandlePath), IsPathAvailable = false,
            HBitmapItem = IconBitmapCache.HistoryHBitmap
        });
        items.AddRange(entries.Take(RootLimit).Select(entry => CreateFolder(provider, entry.Path)));
    }

    internal static List<DynamicMenuItem> BuildSubmenu(Provider provider, Func<string, bool>? exists = null)
    {
        exists ??= Directory.Exists;
        var snapshot = provider.RecentFolderSnapshot;
        // The host calls this on its submenu worker only after mouse/keyboard expansion.
        var items = snapshot.Entries.Skip(RootLimit).Where(entry => exists(entry.Path))
            .Take(snapshot.MenuLimit).Select(entry => CreateFolder(provider, entry.Path)).ToList();
        if (items.Count == 0)
            items.Add(new DynamicMenuItem { Text = TranslationService.Get("FolderCascader_NoRecentFolders"), IsDisabled = true });
        return items;
    }

    private static DynamicMenuItem CreateFolder(Provider provider, string path) => new()
    {
        Text = MenuBuilder.GetDisplayName(path, "") + $" ({path})", HasSubMenu = true,
        SubMenuHandle = provider.AllocateHandle(path), IsPathAvailable = true
    };
}
