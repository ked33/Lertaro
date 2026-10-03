using System.Runtime.InteropServices;

namespace Lertaro.Plugins.FolderCascader.Navigation;

// One snapshot per section, with one worker and at most one queued replacement. No polling timer.
// Shell preparation runs on a short-lived STA; snapshots contain data, never session handles or COM objects.
internal sealed class NavigationSnapshotCache<TInput, TValue> where TValue : class
{
    private readonly object _gate = new();
    private readonly Func<TInput, TInput, bool> _same;
    private readonly Func<TInput, TValue> _prepare;
    private readonly Func<long> _now;
    private Request? _request;
    private bool _running;
    private TValue? _cached;
    private long _expires;

    private sealed class Request(TInput input)
    {
        internal TInput Input { get; } = input;
        internal TaskCompletionSource<TValue> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal NavigationSnapshotCache(Func<TInput, TInput, bool> same, Func<TInput, TValue> prepare,
        Func<long>? now = null)
    {
        _same = same;
        _prepare = prepare;
        _now = now ?? (() => Environment.TickCount64);
    }

    internal Task<TValue> Get(TInput input)
    {
        lock (_gate)
        {
            var same = _request != null && _same(_request.Input, input);
            if (same && (!_request!.Completion.Task.IsCompleted || _now() < _expires))
                return _cached != null ? Task.FromResult(_cached) : _request!.Completion.Task;
            if (!same) { _cached = null; _expires = 0; }
            // Cancel an obsolete queued request. A native call already running is allowed to finish.
            _request?.Completion.TrySetCanceled();
            _request = new Request(input);
            if (!_running)
            {
                _running = true;
                // Thread.Start itself can wait for the Windows loader. Never do it on the UI thread.
                _ = Task.Run(StartWorker);
            }
            return _cached != null ? Task.FromResult(_cached) : _request.Completion.Task;
        }
    }

    private void StartWorker()
    {
        try
        {
            var thread = new Thread(() =>
            {
                var initialized = NavigationPreparationNative.OleInitialize(IntPtr.Zero) >= 0;
                try { Work(); }
                finally { if (initialized) NavigationPreparationNative.OleUninitialize(); }
            }) { IsBackground = true, Name = "QuickNav preparation" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _request!.Completion.TrySetException(ex);
                _ = _request.Completion.Task.Exception;
                _running = false;
            }
        }
    }

    private void Work()
    {
        while (true)
        {
            Request request;
            lock (_gate) request = _request!;
            try
            {
                var value = _prepare(request.Input);
                lock (_gate)
                {
                    if (ReferenceEquals(request, _request))
                    {
                        _cached = value;
                        _expires = _now() + 10000;
                        request.Completion.TrySetResult(value);
                    }
                }
            }
            catch (Exception ex)
            {
                request.Completion.TrySetException(ex);
                // Observe failures even if every waiting menu was closed; next opening can retry.
                _ = request.Completion.Task.Exception;
            }
            lock (_gate)
            {
                if (!ReferenceEquals(request, _request)) continue;
                _running = false;
                return;
            }
        }
    }
}

internal static class NavigationPreparationNative
{
    [DllImport("ole32.dll")] internal static extern int OleInitialize(IntPtr reserved);
    [DllImport("ole32.dll")] internal static extern void OleUninitialize();
}
