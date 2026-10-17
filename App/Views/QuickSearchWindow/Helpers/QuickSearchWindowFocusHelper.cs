using System.Windows.Threading;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

// Split out to keep QuickSearchWindowController below the repository's per-file line limit; this
// helper owns the delayed keyboard-focus handoff used by the one window it receives.
internal sealed class QuickSearchWindowFocusHelper
{
    private readonly Lertaro.App.QuickSearchWindow _window;

    internal QuickSearchWindowFocusHelper(Lertaro.App.QuickSearchWindow window) => _window = window;

    // What the search box's text/caret should look like once it actually has keyboard focus. Kept as a
    // decision separate from the focus call so the precedence is explicit: a clipboard handoff selects
    // everything (so the next keystroke replaces the imported text) and MUST win over a carried-over
    // query, which wants the caret parked after the text so the next keystroke appends to it.
    internal enum SearchBoxFocus { LeaveCaret, SelectAll, CaretAtEnd }

    internal static SearchBoxFocus ResolveSearchBoxFocus(bool selectSearchText, bool caretAtEnd) =>
        selectSearchText ? SearchBoxFocus.SelectAll
        : caretAtEnd ? SearchBoxFocus.CaretAtEnd
        : SearchBoxFocus.LeaveCaret;

    private DispatcherTimer? _timer;
    private FocusAttempt? _attempt;
    private int _activationId;

    internal static void CompleteActivation(int activationId, IntPtr hwnd)
    {
        if (activationId != 0)
            App.HookClient?.SendMessage(new Lertaro.Core.Wire.IpcMessage
            {
                Id = Lertaro.Core.Wire.IpcMessageId.CompleteSearchActivation,
                IntVal = activationId,
                Hwnd = hwnd.ToInt64()
            });
    }

    internal void Cancel()
    {
        _timer?.Stop();
        _timer = null;
        _attempt?.Cancel();
        _attempt = null;
        // A successful acknowledgement may still have replay queued in the hook. Hiding or a new
        // summon invalidates that generation too, without cancelling a newer request.
        CompleteActivation(_activationId, IntPtr.Zero);
        _activationId = 0;
    }

    internal void FocusWhenForeground(IntPtr hwnd, bool selectSearchText, bool caretAtEnd = false, int activationId = 0)
    {
        Cancel();
        _activationId = activationId;
        var origin = QuickSearchWindowNative.GetForegroundWindow();
        var attempt = new FocusAttempt(Environment.TickCount64 + 500, success =>
        {
            if (success)
            {
                switch (ResolveSearchBoxFocus(selectSearchText, caretAtEnd))
                {
                    case SearchBoxFocus.SelectAll: _window.TxtSearch.SelectAll(); break;
                    case SearchBoxFocus.CaretAtEnd: _window.TxtSearch.CaretIndex = _window.TxtSearch.Text.Length; break;
                }
            }
            CompleteActivation(activationId, success ? hwnd : IntPtr.Zero);
        });
        _attempt = attempt;
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(20) };
        _timer = timer;

        void Tick()
        {
            if (_attempt != attempt) { timer.Stop(); return; }
            var foreground = QuickSearchWindowNative.GetForegroundWindow();
            var canContinue = hwnd != IntPtr.Zero && _window.IsVisible && _window.IsEnabled
                && (foreground == origin || foreground == hwnd);
            if (!attempt.Tick(Environment.TickCount64, canContinue, () =>
            {
                if (QuickSearchWindowNative.GetForegroundWindow() != hwnd)
                {
                    QuickSearchWindowNative.SetForegroundWindow(hwnd);
                    _window.Activate();
                }
                if (QuickSearchWindowNative.GetForegroundWindow() != hwnd) return false;
                _window.TxtSearch.Focus();
                System.Windows.Input.Keyboard.Focus(_window.TxtSearch);
                return _window.IsActive && _window.TxtSearch.IsKeyboardFocused
                    && QuickSearchWindowNative.GetForegroundWindow() == hwnd;
            })) return;

            timer.Stop();
            if (_attempt != attempt) return;
            if (!attempt.Succeeded && _window.IsVisible)
            {
                Lertaro.Core.Logger.Log("[SearchActivation] Search box did not acquire keyboard focus; cancelling summon.", Lertaro.Core.LogLevel.Warn);
                _window.HideWindowNoRestore();
            }
        }

        timer.Tick += (_, _) => Tick();
        Tick(); // Already-foreground windows must not wait for the first timer tick.
        if (!attempt.IsCompleted) timer.Start();
    }

    // The retry's terminal state is shared by success, timeout, hide and resummon. Test it without
    // taking desktop focus in CI: a cancelled timer callback must never run activation again.
    internal sealed class FocusAttempt(long deadline, Action<bool> complete)
    {
        internal bool IsCompleted { get; private set; }
        internal bool Succeeded { get; private set; }
        internal bool Tick(long now, bool canContinue, Func<bool> focus)
        {
            if (IsCompleted) return true;
            if (!canContinue || now >= deadline) { Cancel(); return true; }
            if (!focus()) return IsCompleted;
            // Activation can pump messages and reenter hide/resummon before Focus returns.
            if (IsCompleted) return true;
            IsCompleted = Succeeded = true;
            complete(true);
            return true;
        }
        internal void Cancel()
        {
            if (IsCompleted) return;
            IsCompleted = true;
            complete(false);
        }
    }
}
