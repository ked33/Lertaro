using Lertaro.Core.IndexV2.Delta;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.IndexV2.Search;

internal static class IndexFileTypeFilter
{
    // Captured once per search/worker. Reject before TopN so unrelated entries never consume its budget.
    public static Predicate<int>? Create(Snapshot snapshot, DeltaOverlay delta)
    {
        var filter = SearchContext.FileTypeFilter;
        if (filter == null) return null;
        return row =>
        {
            if (row >= snapshot.Count)
            {
                var record = delta.Added[row - snapshot.Count];
                return filter.Matches(record.Name, (record.Flags & (ushort)FileRecordFlags.Directory) != 0);
            }
            if (delta.BaseOverrides.TryGetValue(row, out var changed))
                return filter.Matches(changed.Name, (changed.Flags & (ushort)FileRecordFlags.Directory) != 0);
            return filter.Matches(snapshot.GetName(row), snapshot.IsDirectory(row));
        };
    }
}
