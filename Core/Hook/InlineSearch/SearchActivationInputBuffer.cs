namespace Lertaro.Core.Hook.InlineSearch;

// Owned exclusively by the low-level hook thread, including completion (posted from IPC).
// Keep physical key events, not characters: replay must use the search box's normal IME pipeline.
internal sealed class SearchActivationInputBuffer(Func<int, IReadOnlyList<SearchActivationInputBuffer.Key>, bool> send)
{
    internal readonly record struct Key(int VirtualKey, int ScanCode, bool IsUp = false, bool Extended = false)
    {
        // The virtual key can change when the foreground switches to a different keyboard layout.
        internal int Identity => ScanCode == 0 ? VirtualKey : 0x10000 | ScanCode | (Extended ? 0x1000000 : 0);
    }
    internal const int TimeoutMs = 750;
    internal const int Capacity = 256;
    internal const int EndOfBatch = 0xFF;
    private readonly List<Key> _pending = new();
    private readonly HashSet<int> _held = new();
    private readonly HashSet<int> _discardUntilUp = new();
    private readonly Dictionary<int, (int Request, Key Key)> _injectedDown = new();
    private int _sequence;
    private int _request;
    private long _deadline;
    private IntPtr _origin;
    private IntPtr _target;

    internal bool NeedsInput => _request != 0 || _held.Count != 0 || _discardUntilUp.Count != 0 || _injectedDown.Count != 0;

    internal int Begin(IntPtr origin, long now)
    {
        Cancel();
        if (origin == IntPtr.Zero) return 0;
        _request = _sequence = _sequence == int.MaxValue ? 1 : _sequence + 1;
        _origin = origin;
        _deadline = now + TimeoutMs;
        return _request;
    }

    internal bool HandlePhysical(Key key, IntPtr foreground, bool appIsForeground, long now)
    {
        if (_request != 0 && (now >= _deadline
            || (_target != IntPtr.Zero ? foreground != _target : foreground != _origin && !appIsForeground)
            || !IsSearchKey(key.VirtualKey)))
            Cancel();

        // A cancelled, suppressed press must not leave an orphan release or auto-repeat in another app.
        if (_discardUntilUp.Contains(key.Identity))
        {
            if (key.IsUp) _discardUntilUp.Remove(key.Identity);
            return true;
        }

        if (_request == 0)
        {
            if (key.IsUp)
            {
                _held.Remove(key.Identity);
                _injectedDown.Remove(key.Identity);
            }
            return false;
        }

        if (_pending.Count == Capacity)
        {
            Cancel();
            return HandlePhysical(key, foreground, appIsForeground, now);
        }

        if (key.IsUp)
        {
            if (!_held.Remove(key.Identity)) return false;
        }
        else
        {
            _held.Add(key.Identity);
        }
        _pending.Add(key);
        return true;
    }

    // Ctrl/Alt/Win, function keys and mouse input cancel the handoff, so a deliberate application
    // switch or global shortcut is never replayed into the search window. Shift belongs to typing.
    private static bool IsSearchKey(int vk) => vk is 0x08 or 0x09 or 0x0D or 0x10 or 0x1B
        or >= 0x20 and <= 0x28 or 0x2E or >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A
        or >= 0x60 and <= 0x6F or 0xA0 or 0xA1 or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2;

    internal void Complete(int request, IntPtr target, IntPtr foreground, long now)
    {
        if (_request == 0 || request != _request) return;
        if (target == IntPtr.Zero || target != foreground || now >= _deadline)
        {
            Cancel();
            return;
        }
        if (_target != IntPtr.Zero) return; // A duplicate acknowledgement cannot finish an in-flight batch.
        _target = target;
        Flush();
    }

    // Every injected event is checked again in the hook. Even a focus switch *during* SendInput
    // cannot deliver the remaining buffered letters/Enter to another application.
    internal bool AllowReplay(int request, Key key, IntPtr foreground, long now)
    {
        if (key.IsUp && _injectedDown.TryGetValue(key.Identity, out var pressed) && pressed.Request == request)
        {
            _injectedDown.Remove(key.Identity);
            return true; // Also balance already-delivered downs when cancellation sends cleanup releases.
        }

        if (_request == 0 || request != _request) return false;
        if (foreground != _target || now >= _deadline)
        {
            Cancel();
            return false;
        }
        if (key.VirtualKey == EndOfBatch)
        {
            Flush();
            return false;
        }
        if (key.IsUp) return false;
        _injectedDown[key.Identity] = (request, key);
        return true;
    }

    private void Flush()
    {
        if (_pending.Count == 0)
        {
            // Ownership has reached the search box. Held keys now behave like ordinary input,
            // including Shift-click and a later release in another window.
            foreach (var pair in _injectedDown.Where(pair => pair.Value.Request == _request).ToArray())
                _injectedDown.Remove(pair.Key);
            _held.Clear();
            _request = 0;
            _target = _origin = IntPtr.Zero;
            return;
        }
        var batch = _pending.ToArray();
        _pending.Clear();
        // Stay armed until the batch's sentinel traverses the hook: newly typed physical keys must
        // follow, rather than overtake, the replay. SendInput inserts each batch in order.
        if (!send(_request, batch)) Cancel();
    }

    internal void Cancel()
    {
        _request = 0;
        _target = _origin = IntPtr.Zero;
        _pending.Clear();
        _discardUntilUp.UnionWith(_held);
        _held.Clear();
        // In particular, never strand an injected Shift-down when the user clicks away mid-replay.
        foreach (var group in _injectedDown.Values.ToArray().GroupBy(value => value.Request))
            send(group.Key, group.Select(value => value.Key with { IsUp = true }).ToArray());
    }
}
