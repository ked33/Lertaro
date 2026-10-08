using System.Diagnostics;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Core.Hook;

// One pending navigation, no idle timer. No plugin I/O is performed under this state lock.
internal sealed class FileDialogNavigationTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<(IntPtr Hwnd, uint Pid), long> _dialogs = new();
    private readonly Func<IntPtr, uint> _processId;
    private readonly Func<IntPtr, uint, bool> _isCurrent;
    private readonly Func<IntPtr, IFileDialogAdapter?> _resolve;
    private readonly Func<IntPtr, string?> _readSource;
    private readonly int _retryMs;
    private IntPtr _source;
    private uint _sourcePid;
    private long _version;
    private string? _lastActiveExplorerPath;
    private (IntPtr Hwnd, uint Pid, long Version)? _autoAttempt;
    private Request? _pending, _active;
    private Task _worker = Task.CompletedTask;

    private sealed record Request(IntPtr Hwnd, uint Pid, string? Path, long Version, bool Automatic)
    {
        public CancellationTokenSource Cancellation { get; } = new();
    }

    public FileDialogNavigationTracker() : this(FileDialogNavigationNative.ProcessId,
        FileDialogNavigationNative.IsCurrent, FileDialogNavigationNative.ResolveAdapter,
        FileDialogNavigationNative.ReadSource, 100) { }

    internal FileDialogNavigationTracker(Func<IntPtr, uint> processId, Func<IntPtr, uint, bool> isCurrent,
        Func<IntPtr, IFileDialogAdapter?> resolve, Func<IntPtr, string?> readSource, int retryMs)
    {
        _processId = processId;
        _isCurrent = isCurrent;
        _resolve = resolve;
        _readSource = readSource;
        _retryMs = retryMs;
    }

    public string? LastActiveExplorerPath { get { lock (_gate) return _lastActiveExplorerPath; } }
    internal Task Completion { get { lock (_gate) return _worker; } }

    public void ObserveForeground(IntPtr hwnd)
    {
        hwnd = FileDialogNavigationNative.Root(hwnd);
        bool knownSource;
        lock (_gate) knownSource = hwnd != IntPtr.Zero && hwnd == _source;
        if (knownSource || FileDialogNavigationNative.IsExplorer(hwnd)) SetSource(hwnd, newVisit: true);
        // Register a new common dialog even if its child controls aren't ready for classification yet.
        if (ExplorerNativeHooks.IsCommonDialogClass(hwnd))
        {
            var pid = _processId(hwnd);
            lock (_gate) _dialogs.TryAdd((hwnd, pid), _version);
        }
        lock (_gate)
        {
            _autoAttempt = null;
            if (_active != null && _active.Hwnd != hwnd) _active.Cancellation.Cancel();
            if (_pending != null && _pending.Hwnd != hwnd)
            {
                _pending.Cancellation.Dispose();
                _pending = null;
            }
        }
    }

    public void ForgetWindow(IntPtr hwnd)
    {
        lock (_gate)
        {
            foreach (var key in _dialogs.Keys.Where(k => k.Hwnd == hwnd).ToArray()) _dialogs.Remove(key);
            if (_source == hwnd) { _source = IntPtr.Zero; _sourcePid = 0; _version++; }
            if (_autoAttempt?.Hwnd == hwnd) _autoAttempt = null;
            if (_active?.Hwnd == hwnd) _active.Cancellation.Cancel();
            if (_pending?.Hwnd == hwnd) { _pending.Cancellation.Dispose(); _pending = null; }
        }
    }

    public void SetSource(IntPtr hwnd, bool newVisit = false)
    {
        var pid = _processId(hwnd);
        if (hwnd == IntPtr.Zero || pid == 0) return;
        lock (_gate)
        {
            if (!newVisit && _source == hwnd && _sourcePid == pid) return;
            _source = hwnd;
            _sourcePid = pid;
            _version++;
        }
    }

    public void SetLastActiveExplorerPath(string? path)
    {
        // Empty/timeout samples don't erase the last known folder used by search suggestions.
        if (!IsPath(path)) return;
        lock (_gate)
        {
            if (PathsEqual(path, _lastActiveExplorerPath)) return;
            _lastActiveExplorerPath = path;
            _version++;
        }
    }

    public void HandleDialogSeen(IntPtr hwnd)
    {
        var pid = _processId(hwnd);
        if (!_isCurrent(hwnd, pid)) return;
        lock (_gate)
        {
            var key = (hwnd, pid);
            if (!_dialogs.TryGetValue(key, out var applied))
            {
                _dialogs[key] = _version; // First opening keeps the application's initial directory.
                if (_dialogs.Count > 100)
                    foreach (var old in _dialogs.Keys.Where(k => _processId(k.Hwnd) != k.Pid).ToArray())
                        _dialogs.Remove(old);
                return;
            }
            if (applied < _version) Enqueue(hwnd, pid, null, automatic: true);
        }
    }

    public void RequestNavigation(IntPtr hwnd, string? path = null)
    {
        var pid = _processId(hwnd);
        if (hwnd == IntPtr.Zero || pid == 0 || (path != null && !IsPath(path))) return;
        lock (_gate) Enqueue(hwnd, pid, path, automatic: false);
    }

    private void Enqueue(IntPtr hwnd, uint pid, string? path, bool automatic)
    {
        // Only one automatic attempt per source/activation. Input-generated focus/name events must
        // not restart an exhausted attempt forever; a real activation or explicit hotkey can retry.
        if (automatic && _autoAttempt == (hwnd, pid, _version)) return;
        var existing = _pending ?? _active;
        if (existing is not null && !existing.Cancellation.IsCancellationRequested)
        {
            if (automatic && !existing.Automatic) return;
            if (existing.Hwnd == hwnd && existing.Pid == pid && existing.Version == _version
                && string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase)) return;
        }
        _active?.Cancellation.Cancel();
        _pending?.Cancellation.Dispose();
        _pending = new Request(hwnd, pid, path, _version, automatic);
        if (automatic) _autoAttempt = (hwnd, pid, _version);
        if (_worker.IsCompleted) _worker = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (true)
        {
            Request request;
            lock (_gate)
            {
                if (_pending == null)
                {
                    // Publish idle under the same lock as Enqueue, avoiding a stranded last request.
                    _worker = Task.CompletedTask;
                    return;
                }
                request = _active = _pending;
                _pending = null;
            }
            try { await NavigateAsync(request).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Logger.Log($"[QuickSwitch] Navigation failed: {ex.Message}", LogLevel.Warn); }
            finally
            {
                lock (_gate)
                {
                    _active = null;
                    request.Cancellation.Dispose();
                }
            }
        }
    }

    private async Task NavigateAsync(Request request)
    {
        var token = request.Cancellation.Token;
        var elapsed = Stopwatch.StartNew();
        for (var attempt = 0; attempt < 8 && elapsed.ElapsedMilliseconds < 3000; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (attempt != 0) await Task.Delay(_retryMs, token).ConfigureAwait(false);
            if (!_isCurrent(request.Hwnd, request.Pid))
            {
                // IPC may arrive just before the App finishes hiding its menu.
                if (attempt < 3 && request.Path != null) continue;
                return;
            }
            IntPtr source;
            uint sourcePid;
            long version;
            lock (_gate) { source = _source; sourcePid = _sourcePid; version = _version; }
            var path = request.Path;
            if (path == null)
            {
                // Never substitute an old folder if the newly visited Explorer hasn't registered yet.
                if (source == IntPtr.Zero || _processId(source) != sourcePid) continue;
                path = _readSource(source);
                if (_processId(source) != sourcePid) continue;
                lock (_gate)
                    if (source != _source || sourcePid != _sourcePid || version != _version) continue;
                if (!IsPath(path)) continue;
            }
            var adapter = _resolve(request.Hwnd);
            token.ThrowIfCancellationRequested();
            if (!_isCurrent(request.Hwnd, request.Pid)) return;
            if (adapter == null) continue;
            lock (_gate)
                if (request.Path == null && version != _version) continue;
            var target = request.Path == null && !Path.EndsInDirectorySeparator(path!) ? path + "\\" : path!;
            // The adapter owns preparation retries and completion checks. Never blindly repeat a
            // submission: a slow dialog may still be processing the first Enter/Open operation.
            if (!adapter.NavigateTo(request.Hwnd, target, token))
            {
                Logger.Log($"[QuickSwitch] Adapter {adapter.GetType().Name} did not complete navigation for 0x{request.Hwnd:X}.", LogLevel.Warn);
                return;
            }
            token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (version == _version) _dialogs[(request.Hwnd, request.Pid)] = version;
                if (request.Path == null && version == _version) _lastActiveExplorerPath = path;
            }
            Logger.Log($"[QuickSwitch] Navigation completed for 0x{request.Hwnd:X} on attempt {attempt + 1}.", LogLevel.Debug);
            return;
        }
        Logger.Log($"[QuickSwitch] Navigation expired for 0x{request.Hwnd:X}: source or adapter unavailable.", LogLevel.Warn);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _active?.Cancellation.Cancel();
            _pending?.Cancellation.Dispose();
            _pending = null;
            _dialogs.Clear();
            _autoAttempt = null;
            _source = IntPtr.Zero;
            _sourcePid = 0;
            _lastActiveExplorerPath = null;
            _version++;
        }
    }

    private static bool IsPath(string? path) => !string.IsNullOrWhiteSpace(path) && !path.Contains('\0') && Path.IsPathFullyQualified(path);
    private static bool PathsEqual(string? left, string? right) => string.Equals(left?.TrimEnd('\\', '/'), right?.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
