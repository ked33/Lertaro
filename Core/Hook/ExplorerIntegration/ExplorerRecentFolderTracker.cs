using System.Diagnostics;
using System.Text;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Core.Hook;

/// <summary>Shares ExplorerTracker's WinEvents; never polls idle windows or blocks a hook callback.</summary>
internal sealed class ExplorerRecentFolderTracker : IDisposable
{
    private readonly object _gate = new();
    private readonly RecentFolderVisitState _state = new();
    private readonly Timer _timer;
    private readonly Func<IntPtr, string> _processName;
    private readonly Action<IntPtr, string, long> _visited;
    private readonly Func<IntPtr, (IntPtr Tab, string? Path)> _read;
    private readonly Func<IntPtr, IntPtr, bool> _stillActive;
    private readonly int _quietMs;
    private bool _enabled, _disposed, _reading, _pending;

    public ExplorerRecentFolderTracker(Func<IntPtr, string> processName, Action<IntPtr, string, long> visited)
        : this(visited, ReadNative,
            (target, tab) => ExplorerNativeHooks.GetForegroundWindow() == target && ExplorerFolderPathReader.GetActiveTab(target) == tab)
    {
        _processName = processName;
    }

    internal ExplorerRecentFolderTracker(Action<IntPtr, string, long> visited,
        Func<IntPtr, (IntPtr Tab, string? Path)> read, Func<IntPtr, IntPtr, bool> stillActive, int quietMs = 200)
    {
        _processName = _ => string.Empty;
        _visited = visited;
        _read = read;
        _stillActive = stillActive;
        _quietMs = quietMs;
        _timer = new Timer(_ => StartRead(), null, Timeout.Infinite, Timeout.Infinite);
    }

    private static (IntPtr Tab, string? Path) ReadNative(IntPtr target)
    {
        if (ExplorerNativeHooks.GetForegroundWindow() != target) return (IntPtr.Zero, null);
        var tab = ExplorerFolderPathReader.GetActiveTab(target);
        return (tab, ExplorerFolderPathReader.Read(target, tab));
    }

    public void Configure(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed || _enabled == enabled) return;
            _enabled = enabled;
            _state.ChangeTarget(IntPtr.Zero, reset: true);
            _pending = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        if (enabled) Observe(ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND, ExplorerNativeHooks.GetForegroundWindow());
    }

    public void Observe(uint eventType, IntPtr eventHwnd)
    {
        lock (_gate)
        {
            if (_disposed || !_enabled) return;
            var foreground = ExplorerNativeHooks.GetForegroundWindow();
            // Foreground changes also clear deduplication when the user leaves Explorer.
            if (eventType != ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND && eventHwnd != foreground
                && ExplorerNativeHooks.GetAncestor(eventHwnd, 2 /* GA_ROOT */) != foreground) return;
            if (foreground != _state.Target || eventType == ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND)
            {
                var name = new StringBuilder(128);
                ExplorerNativeHooks.GetClassName(foreground, name, name.Capacity);
                var native = RecentFolderVisitState.IsExplorer(name.ToString(), _processName(foreground));
                _state.ChangeTarget(native ? foreground : IntPtr.Zero);
            }
            Request(_state.Target);
        }
    }

    internal void Request(IntPtr target)
    {
        lock (_gate)
        {
            if (_disposed || !_enabled) return;
            _state.ChangeTarget(target);
            if (target == IntPtr.Zero) return;
            _pending = true;
            _timer.Change(_quietMs, Timeout.Infinite);
        }
    }

    private void StartRead()
    {
        lock (_gate)
        {
            if (_disposed || !_enabled || !_pending || _reading || _state.Target == IntPtr.Zero) return;
            _pending = false;
            _reading = true;
            var target = _state.Target;
            var generation = _state.Generation;
            var observed = DateTime.UtcNow.Ticks;
            var worker = new Thread(() => Read(target, generation, observed))
                { IsBackground = true, Name = "RecentFolderSta" };
            try
            {
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
            }
            catch (Exception ex)
            {
                _reading = false;
                Logger.Log($"[RecentFolders] Could not start Explorer reader: {ex.Message}", LogLevel.Warn);
            }
        }
    }

    private void Read(IntPtr target, long generation, long observed)
    {
        try
        {
            var started = Stopwatch.StartNew();
            var (tab, path) = _read(target);
            if (started.ElapsedMilliseconds > 2000 || !_stillActive(target, tab)) return;
            lock (_gate)
            {
                if (!_disposed && _enabled && _state.Accept(target, generation, tab, path))
                    _visited(target, path!, observed);
            }
        }
        catch (Exception ex) { Logger.Log($"[RecentFolders] Explorer read failed: {ex.Message}", LogLevel.Warn); }
        finally
        {
            lock (_gate)
            {
                _reading = false;
                if (!_disposed && _enabled && _pending) _timer.Change(_quietMs, Timeout.Infinite);
            }
        }
        // ponytail: one live STA read. A permanently hung Shell call suspends this recorder until it
        // returns or the Hook restarts; never abandon/recreate workers on every event. If recovery from
        // a permanently wedged Shell becomes necessary, isolate reads in a restartable helper process.
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _timer.Dispose(); }
    }
}
