using System.Text.Json;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Core;

/// <summary>Single App-process owner; bounded in-memory MRU with serialized atomic writes.</summary>
public sealed class RecentFoldersStore : IDisposable
{
    private static readonly Lazy<RecentFoldersStore> Shared = new(() => new(
        Path.Combine(Logger.UserDataDir, "recent-folders.json"), UserSettings.Load().RecentFolders ?? new()));
    public static RecentFoldersStore Instance => Shared.Value;
    public static void Shutdown() { if (Shared.IsValueCreated) Shared.Value.Dispose(); }

    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private readonly string _path;
    private readonly Timer _saveTimer;
    private readonly Func<long> _now;
    private List<RecentFolderEntry> _entries;
    private RecentFoldersSettings _settings;
    private long _ignoreThroughTicks;
    private bool _dirty;
    private bool _disposed;
    public event Action? Changed;

    public RecentFoldersStore(string path, RecentFoldersSettings settings, Func<long>? utcTicks = null)
    {
        _path = path;
        _now = utcTicks ?? (() => DateTime.UtcNow.Ticks);
        _settings = settings.CopyValidated();
        _entries = Load(path);
        TrimNoLock();
        _saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public RecentFoldersSnapshot GetSnapshot()
    {
        lock (_gate) return new(_entries.ToArray(), _settings.MenuLimit);
    }

    public void Record(string path, long observedUtcTicks)
    {
        var normalized = RecentFolderPaths.Normalize(path);
        if (normalized == null) return;
        lock (_gate)
        {
            if (_disposed || !_settings.Enabled || observedUtcTicks <= _ignoreThroughTicks
                || observedUtcTicks <= 0 || observedUtcTicks > DateTime.MaxValue.Ticks
                || RecentFolderPaths.IsExcluded(normalized, _settings.ExcludedDirectories)) return;
            var old = _entries.Find(e => e.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (old != null && old.OpenedUtcTicks >= observedUtcTicks) return;
            _entries.RemoveAll(e => e.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            _entries.Add(new(normalized, observedUtcTicks));
            TrimNoLock();
            MarkDirtyNoLock();
        }
        Changed?.Invoke();
    }

    public void ApplySettings(RecentFoldersSettings settings)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _settings = settings.CopyValidated();
            // Reject samples started under the previous recording/exclusion policy.
            _ignoreThroughTicks = _now();
            TrimNoLock();
            MarkDirtyNoLock();
        }
        Changed?.Invoke();
        _ = Task.Run(Flush);
    }

    public void Remove(string? path = null)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _ignoreThroughTicks = _now();
            if (path == null) _entries.Clear();
            else _entries.RemoveAll(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            MarkDirtyNoLock();
        }
        Changed?.Invoke();
        _ = Task.Run(Flush);
    }

    private void TrimNoLock()
    {
        // ponytail: at most 5000 entries; a list keeps the small MRU simple. Use a keyed linked list
        // only if this bounded store ever becomes large enough for its O(n) updates to matter.
        _entries = _entries.Where(e => !RecentFolderPaths.IsExcluded(e.Path, _settings.ExcludedDirectories))
            .OrderByDescending(e => e.OpenedUtcTicks).DistinctBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .Take(_settings.Capacity).ToList();
    }

    private void MarkDirtyNoLock()
    {
        if (!_dirty) _saveTimer.Change(1000, Timeout.Infinite);
        _dirty = true;
    }

    public void Flush()
    {
        lock (_writeGate)
        {
            RecentFolderEntry[] snapshot;
            lock (_gate)
            {
                if (!_dirty) return;
                snapshot = _entries.ToArray();
                _dirty = false;
            }
            try { AtomicFileStore.Write(_path, JsonSerializer.Serialize(snapshot), _path + ".bak"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Log($"[RecentFolders] Could not save history: {ex.Message}", LogLevel.Error);
                lock (_gate)
                {
                    _dirty = true;
                    if (!_disposed) _saveTimer.Change(5000, Timeout.Infinite);
                }
            }
        }
    }

    private static List<RecentFolderEntry> Load(string path)
    {
        // A deliberately deleted main file must not resurrect the backup.
        if (!File.Exists(path)) return new();
        return Read(path) ?? Read(path + ".bak") ?? new();
    }

    private static List<RecentFolderEntry>? Read(string path)
    {
        try
        {
            var entries = JsonSerializer.Deserialize<List<RecentFolderEntry?>>(File.ReadAllText(path));
            if (entries == null) return null;
            return entries.Where(e => e != null && e.OpenedUtcTicks > 0 && e.OpenedUtcTicks <= DateTime.MaxValue.Ticks)
                .Select(e => (Entry: e!, Path: RecentFolderPaths.Normalize(e!.Path)))
                .Where(e => e.Path != null).Select(e => e.Entry with { Path = e.Path! }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.Log($"[RecentFolders] Could not load history: {ex.Message}", LogLevel.Warn);
            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _saveTimer.Dispose(); }
        Flush();
    }
}
