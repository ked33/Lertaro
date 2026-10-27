using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Lertaro.Core.Hook;

// Shared bounded STA dispatcher for plugin adapter/collector reads (IFileDialogAdapter,
// IInlineSearchAdapter, IActivePathCollector). Third-party plugin code can hang inside
// cross-process COM calls, so every such read from tracker machinery runs on a worker apartment with a
// timeout instead of directly on the calling thread.
// Split out of ExplorerActivePathPoller so ExplorerWindowClassifier can share the exact same semantics;
// the abandoned-read budget lives here for both callers.
//
// The workers are long-lived, OLE-initialized and pump messages -- the same three things App's ShellThread
// insists on, for the same reason. A throwaway thread per read is not a bounded read: the apartment dies
// while the target process still holds registrations in it, and an abandoned thread sits in a wait that
// pumps nothing, so the target's own callback into us has nobody to answer it. When the target is
// explorer.exe that is how the shell stops responding for the whole desktop -- reported on Windows 10 22H2
// as "open folder fails, Win+E does nothing, and every queued window appears at once when Lertaro exits",
// which is exactly what tearing this process down would release.
internal static class ExplorerStaInvoker
{
    // Four, so one read parked inside a wedged shell call cannot hold the next one behind it.
    // ponytail: fixed at startup and never grown, because growing it means asking the loader for a thread
    // from wherever work arrives -- the thing that wedged a shell menu's extension elsewhere in this app.
    private const int WorkerCount = 4;
    private const int WorkerStartupTimeoutMs = 5000;

    // Count queued and running reads until completion. Timeout alone cannot release a COM call.
    // Queued timeouts are aborted, so resume can use healthy workers without discarding live slots.
    private const int MaxActiveReads = 8;
    private static int _activeReads;

    private static readonly List<Dispatcher> Workers = new();
    private static readonly object Gate = new();
    private static bool _started;
    private static int _next;

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    // In-flight COM calls still own their slots after resume; resetting them would undercount.
    internal static void ResetAbandonedReads() { }

    public static void Start() => EnsureWorkers();

    public static T RunOnStaWithTimeout<T>(Func<T> func, T fallback, TimeSpan timeout)
        => RunOnStaWithTimeout(func, fallback, timeout, out _);

    public static T RunOnStaWithTimeout<T>(Func<T> func, T fallback, TimeSpan timeout, out bool timedOut)
    {
        timedOut = false;
        if (Interlocked.Increment(ref _activeReads) > MaxActiveReads)
        {
            Interlocked.Decrement(ref _activeReads);
            Logger.Log("[ExplorerStaInvoker] Active-read budget exhausted; failing the read fast.", LogLevel.Warn);
            timedOut = true;
            return fallback;
        }

        var result = fallback;
        Exception? error = null;
        DispatcherOperation<bool> operation;
        try
        {
            var worker = NextWorker();
            if (worker == null) throw new InvalidOperationException("No STA worker available.");
            // Swallow plugin faults on the long-lived dispatcher so one read cannot stop its worker.
            operation = worker.InvokeAsync(() =>
            {
                try { result = func(); }
                catch (Exception ex) { error = ex; }
                return true;
            });
        }
        catch (Exception ex)
        {
            Interlocked.Decrement(ref _activeReads);
            Logger.Log($"[ExplorerStaInvoker] Could not queue plugin read: {ex.Message}", LogLevel.Warn);
            timedOut = true;
            return fallback;
        }

        _ = operation.Task.ContinueWith(_ => Interlocked.Decrement(ref _activeReads), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        if (operation.Task.Wait(timeout))
        {
            if (error != null)
                Logger.Log($"[ExplorerStaInvoker] Plugin read failed: {error.Message}", LogLevel.Warn);
            return result;
        }

        timedOut = true;
        // Queued work that timed out should never execute later against a stale host. A running COM
        // call cannot be aborted; its slot remains occupied until its task actually finishes.
        operation.Abort();
        Logger.Log("[ExplorerStaInvoker] Plugin read timed out; continuing with fallback.", LogLevel.Warn);
        return fallback;
    }

    private static void EnsureWorkers()
    {
        lock (Gate)
        {
            if (_started) return;
            _started = true;
            for (var index = 0; index < WorkerCount; index++)
                if (CreateWorker(index) is { } worker)
                    Workers.Add(worker);
        }
    }

    private static Dispatcher? NextWorker()
    {
        EnsureWorkers();
        lock (Gate)
        {
            if (Workers.Count == 0) return null;
            _next = (_next + 1) % Workers.Count;
            return Workers[_next];
        }
    }

    private static Dispatcher? CreateWorker(int index)
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            // Folder handlers, data objects and the shell objects a read hands back expect an
            // OLE-initialized apartment; without it they load incompletely.
            OleInitialize(IntPtr.Zero);
            ready.TrySetResult(Dispatcher.CurrentDispatcher);
            // A real message pump, so COM calls coming back into an object this apartment created are
            // serviced -- while the worker idles and, inside a blocking cross-process call, by COM itself.
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = $"ExplorerStaWorker{index}",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!ready.Task.Wait(TimeSpan.FromMilliseconds(WorkerStartupTimeoutMs)))
        {
            // Falling back to the calling thread's dispatcher would put the read on an event thread that
            // exists to be dispatched TO, not to do the reading: that is the wedge this class is for.
            Logger.Log($"[ExplorerStaInvoker] Worker {index} failed to start within {WorkerStartupTimeoutMs}ms", LogLevel.Error);
            return null;
        }

        return ready.Task.Result;
    }
}
