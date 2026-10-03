using System.IO;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FolderCascader.Navigation;

internal static class RecentFoldersMenu
{
    internal const string HandlePath = "foldercascader://recent-folders";
    internal const int RootLimit = 15;

    internal static void AppendRoot(List<DynamicMenuItem> items, Provider provider, bool historyShown,
        RecentFoldersSnapshot? snapshot = null, Func<string, bool>? exists = null, bool validated = false)
    {
        // Partition once per popup so visits recorded before submenu expansion cannot shift its boundary.
        snapshot ??= RecentFoldersService.GetSnapshot();
        var folders = validated
            ? snapshot.Entries.Select(entry => CreateFolder(provider, entry.Path)).ToList()
            : Build(provider, snapshot, exists);
        provider.RecentFolderSubmenu = folders.Skip(RootLimit).ToArray();
        if (provider.RecentFolderSubmenu.Count == 0)
            provider.RecentFolderSubmenu = [new DynamicMenuItem { Text = TranslationService.Get("FolderCascader_NoRecentFolders"), IsDisabled = true }];

        if (historyShown) items.Add(new DynamicMenuItem { IsSeparator = true });
        items.Add(new DynamicMenuItem
        {
            Text = TranslationService.Get("FolderCascader_RecentFolders"),
            HasSubMenu = true,
            SubMenuHandle = provider.AllocateHandle(HandlePath), IsPathAvailable = false,
            HBitmapItem = IconBitmapCache.HistoryHBitmap
        });
        items.AddRange(folders.Take(RootLimit));
    }

    internal static void AppendDeferred(List<DynamicMenuItem> items, Provider provider, bool historyShown,
        Task<RecentFoldersSnapshot>? snapshot = null)
    {
        snapshot ??= provider.Preparation.Recent();
        provider.RecentFolderSnapshotTask = snapshot;
        if (snapshot.IsCompletedSuccessfully)
        {
            AppendRoot(items, provider, historyShown, snapshot.Result, validated: true);
            provider.RecentFolderSnapshotTask = null;
            return;
        }
        if (historyShown) items.Add(new DynamicMenuItem { IsSeparator = true });
        items.Add(new DynamicMenuItem
        {
            Text = TranslationService.Get("FolderCascader_RecentFolders"), HasSubMenu = true,
            SubMenuHandle = provider.AllocateHandle(HandlePath), IsPathAvailable = false,
            HBitmapItem = IconBitmapCache.HistoryHBitmap
        });
        for (var i = 0; i < RootLimit; i++)
        {
            var index = i;
            items.Add(new DynamicMenuItem
            {
                IsDisabled = true,
                LoadDeferredItem = async cancellation =>
                {
                    var data = await snapshot.WaitAsync(cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    return index < data.Entries.Count ? CreateFolder(provider, data.Entries[index].Path) : null;
                }
            });
        }
    }

    internal static RecentFoldersSnapshot Validate(RecentFoldersSnapshot snapshot, Func<string, bool>? exists = null)
    {
        exists ??= Directory.Exists;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var limit = RootLimit + Math.Clamp(snapshot.MenuLimit, 1, 100);
        var entries = snapshot.Entries.OrderByDescending(entry => entry.OpenedUtcTicks)
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path) && seen.Add(entry.Path) && exists(entry.Path))
            .Take(limit).ToArray();
        return new(entries, snapshot.MenuLimit);
    }

    internal static List<DynamicMenuItem> BuildPreparedSubmenu(Provider provider, RecentFoldersSnapshot snapshot)
    {
        var items = snapshot.Entries.Skip(RootLimit).Select(entry => CreateFolder(provider, entry.Path)).ToList();
        if (items.Count == 0)
            items.Add(new DynamicMenuItem { Text = TranslationService.Get("FolderCascader_NoRecentFolders"), IsDisabled = true });
        return items;
    }

    private static DynamicMenuItem CreateFolder(Provider provider, string path) => new()
    {
        Text = MenuBuilder.GetDisplayName(path, "") + $" ({path})", HasSubMenu = true,
        SubMenuHandle = provider.AllocateHandle(path), IsPathAvailable = true
    };

    internal static List<DynamicMenuItem> Build(Provider provider, RecentFoldersSnapshot snapshot,
        Func<string, bool>? exists = null) =>
        Validate(snapshot, exists).Entries.Select(entry => CreateFolder(provider, entry.Path)).ToList();
}
