namespace Lertaro.Core.Hook.InlineSearch;

// Two complete physical taps, confirmed on the second release so a chord can still cancel.
// Shared by all bare-modifier shortcuts; ordinary combinations remain key-down gestures.
internal sealed class ModifierDoubleTapDetector
{
    private readonly HashSet<int> _pressedKeys = new();
    private int _sequenceKey;
    private bool _firstTapComplete;
    private uint _firstDownTime;
    private uint _firstUpTime;
    private uint _currentDownTime;
    private IntPtr _foreground;

    internal bool ProcessKey(int vkCode, uint time, bool isDown, bool isModifier,
        bool allowTap, bool isInjected, IntPtr foreground)
    {
        if (isInjected || !isModifier)
        {
            ResetOnOtherInput();
            return false; // Synthetic releases must not release a physically held key.
        }

        if (_foreground != foreground) ResetOnOtherInput();
        if (isDown)
        {
            var firstDown = _pressedKeys.Add(vkCode);
            if (!firstDown || _pressedKeys.Count != 1 || !allowTap || foreground == IntPtr.Zero)
            {
                ResetOnOtherInput();
                return false;
            }

            var interval = unchecked(time - _firstDownTime);
            if (_sequenceKey == vkCode && _firstTapComplete && interval < 350)
            {
                if (interval <= 100 || unchecked(time - _firstUpTime) < 50)
                {
                    ResetOnOtherInput();
                    return false;
                }
                _currentDownTime = time;
                return false;
            }

            ResetOnOtherInput();
            _sequenceKey = vkCode;
            _foreground = foreground;
            _firstDownTime = _currentDownTime = time;
            return false;
        }

        var wasDown = _pressedKeys.Remove(vkCode);
        if (!wasDown || _pressedKeys.Count != 0 || !allowTap || _sequenceKey != vkCode)
        {
            ResetOnOtherInput();
            return false;
        }

        var held = unchecked(time - _currentDownTime);
        if (held == 0 || held > 250)
        {
            ResetOnOtherInput();
            return false;
        }
        if (!_firstTapComplete)
        {
            _firstTapComplete = true;
            _firstUpTime = time;
            return false;
        }

        ResetOnOtherInput(); // A third tap cannot reuse the second one.
        return true;
    }

    internal void ResetOnOtherInput()
    {
        _sequenceKey = 0;
        _firstTapComplete = false;
        _firstDownTime = _firstUpTime = _currentDownTime = 0;
        _foreground = IntPtr.Zero;
        // Preserve held keys: repeats and missing releases cannot start a new gesture.
        // After a missed release, the next real release clears the key; require fresh taps.
    }
}
