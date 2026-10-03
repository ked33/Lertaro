using System.IO;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Models;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FolderCascader.Navigation;

internal sealed class RootMenuPreparation
{
    internal sealed record FolderInput(string Path, string Name, string SubMenu, string Shortcut);
    internal sealed record Folder(string Path, string Name, string Shortcut, bool Available);
    internal sealed record Favorites(bool Available);

    private readonly NavigationSnapshotCache<FolderInput[], Folder[]> _folders = new(
        (a, b) => a.SequenceEqual(b), input => input.Select(PrepareFolder).ToArray());
    private readonly NavigationSnapshotCache<string[], Favorites> _favorites = new(
        (a, b) => a.SequenceEqual(b), paths => new(MenuBuilderContentExtensions.HasAvailableFavorites(
            paths.Select(path => new FavoriteItem { Path = path }), File.Exists, Directory.Exists)));
    private readonly NavigationSnapshotCache<RecentFoldersSnapshot, RecentFoldersSnapshot> _recent = new(
        (a, b) => a.MenuLimit == b.MenuLimit && a.Entries.SequenceEqual(b.Entries),
        source => RecentFoldersMenu.Validate(source));

    internal Task<Folder[]> Folders(IReadOnlyList<FolderCascaderPlugin.FolderConfigItem> folders) =>
        _folders.Get(folders.Select(f => new FolderInput(f.Path, f.Name, f.SubMenu, f.ShortcutKey)).ToArray());
    internal Task<Favorites> FavoriteAvailability() =>
        _favorites.Get(FavoritesService.GetFavorites().Select(f => f.Path).ToArray());
    internal Task<RecentFoldersSnapshot> Recent() => _recent.Get(RecentFoldersService.GetSnapshot());

    internal static Folder PrepareFolder(FolderInput input)
    {
        // Nested categories are prepared only when expanded, not as part of the root snapshot.
        if (MenuBuilder.SplitSubMenuPath(input.SubMenu).Length > 0 || string.IsNullOrWhiteSpace(input.Path)
            || input.Path == "-" || input.Name == "-") return new(input.Path, input.Name, input.Shortcut, false);
        var expanded = UserPathResolver.Expand(input.Path);
        var resolved = UserPathResolver.Resolve(expanded);
        return new(UserPathResolver.IsVirtualPath(resolved) ? expanded : resolved,
            MenuBuilder.GetDisplayName(input.Path, input.Name), FolderCascaderPlugin.NormalizeShortcut(input.Shortcut),
            PathAvailability.IsFolderAvailable(resolved));
    }
}
