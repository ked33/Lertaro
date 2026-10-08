using Lertaro.App.Helpers;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Search;

// Builds the synthetic rows shown by an empty inline search box. Kept separate so the dispatch
// controller only coordinates the search flow and this path-deduplication logic remains testable.
internal static class InlineEmptyStateResultHelper
{
    public static List<AppSearchResult> Build(
        IEnumerable<string> recentFolderPaths,
        IEnumerable<string> openedFolderPaths,
        string recentFoldersHeader,
        string openedFoldersHeader,
        string? currentPath = null,
        bool showOpenedFolders = true)
    {
        var results = new List<AppSearchResult>();
        var openedRows = new List<AppSearchResult>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPath in openedFolderPaths)
        {
            var path = RecentFolderPaths.Normalize(rawPath);
            if (path == null) continue;
            if (seenPaths.Add(path) && showOpenedFolders) openedRows.Add(FolderRow(path));
        }

        if (openedRows.Count > 0)
        {
            SearchResultHelper.AddSectionHeader(results, openedFoldersHeader, string.Empty);
            results.AddRange(openedRows);
        }

        // The store already supplies MRU order. Filter before taking five, so current folders and
        // duplicate spellings never spend a history slot. No filesystem probes on the UI thread.
        if (RecentFolderPaths.Normalize(currentPath) is { } current)
            seenPaths.Add(current);
        var historyRows = new List<AppSearchResult>();
        foreach (var rawPath in recentFolderPaths)
        {
            var path = RecentFolderPaths.Normalize(rawPath);
            if (path == null || !seenPaths.Add(path)) continue;
            historyRows.Add(FolderRow(path));
            if (historyRows.Count == 5) break;
        }
        if (historyRows.Count > 0)
        {
            SearchResultHelper.AddSectionHeader(results, recentFoldersHeader, string.Empty);
            results.AddRange(historyRows);
        }

        for (var index = 0; index < results.Count; index++)
            results[index].Index = index;

        return results;
    }

    // Ordinary folder rows retain per-row numeric shortcuts; five history items cannot all mean Ctrl+G.
    private static AppSearchResult FolderRow(string path) => new()
    {
        Name = path, FullPath = path, ParentDir = string.Empty, IsDir = true,
        Drive = string.Empty, ResultKind = "OpenedFolder", SearchQuery = string.Empty
    };
}
