namespace Lertaro.Core.Hook;

// Shared bounded STA dispatcher for plugin adapter/collector reads (IFileDialogAdapter,
// IInlineSearchAdapter, IActivePathCollector). Third-party plugin code can hang inside
// cross-process COM calls, so every such read from tracker machinery runs on a throwaway
// STA thread with a timeout instead of directly on the calling thread.
// Split out of ExplorerActivePathPoller so ExplorerWindowClassifier can share the exact
// same semantics; the live-thread budget lives here for both callers.
internal static class ExplorerStaInvoker
{
    // A timeout cannot abort a COM call. Count all live reads, including late ones, and release
    // the slot only when the worker actually exits. A hung host cannot create unbounded threads.
    private const int MaxConcurrentReads = 8;
    private static int _activeReads;

    public static T RunOnStaWithTimeout<T>(Func<T> func, T fallback, TimeSpan timeout)
        => RunOnStaWithTimeout(func, fallback, timeout, out _);

    public static T RunOnStaWithTimeout<T>(Func<T> func, T fallback, TimeSpan timeout, out bool timedOut)
    {
        timedOut = false;
        if (Interlocked.Increment(ref _activeReads) > MaxConcurrentReads)
        {
            Interlocked.Decrement(ref _activeReads);
            Logger.Log("[ExplorerStaInvoker] Concurrent-read budget exhausted; failing the read fast.", LogLevel.Warn);
            timedOut = true;
            return fallback;
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var result = fallback;
            try { result = func(); }
            catch (Exception ex)
            {
                Logger.Log($"[ExplorerStaInvoker] Plugin read failed: {ex.Message}", LogLevel.Warn);
            }
            finally { Interlocked.Decrement(ref _activeReads); }
            completion.TrySetResult(result);
        })
        {
            IsBackground = true,
            Name = "ExplorerStaInvoke"
        };
        try
        {
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        catch
        {
            Interlocked.Decrement(ref _activeReads);
            throw;
        }
        if (completion.Task.Wait(timeout)) return completion.Task.Result;
        timedOut = true;
        Logger.Log("[ExplorerStaInvoker] Plugin read timed out; continuing with fallback.", LogLevel.Warn);
        return fallback;
    }
}
