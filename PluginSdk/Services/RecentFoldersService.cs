namespace Lertaro.PluginSdk.Services;

public sealed record RecentFolderEntry(string Path, long OpenedUtcTicks);

public sealed record RecentFoldersSnapshot(IReadOnlyList<RecentFolderEntry> Entries, int MenuLimit);

/// <summary>Explorer browsing history, independent of search history. The host owns persistence.</summary>
public static class RecentFoldersService
{
    public static Func<RecentFoldersSnapshot>? GetSnapshotFunc { get; set; }
    public static RecentFoldersSnapshot GetSnapshot() =>
        GetSnapshotFunc?.Invoke() ?? new(Array.Empty<RecentFolderEntry>(), 20);
}
