namespace Lertaro.Core.Hook.InlineSearch;

public sealed class GlobalHotkeyDetector
{
    private readonly UserSettings _settings;
    private readonly ExplorerTracker _explorerTracker;

    private readonly ModifierDoubleTapDetector _toggleWindowTapDetector = new();
    private readonly ModifierDoubleTapDetector _quickSwitchTapDetector = new();
    private readonly ModifierKeyState _modifierKeyState = new();

    /// <summary>
    /// Where a recognized Quick Switch is handed off. The hook cannot do the work itself: deciding needs the
    /// tracker lock, which the WinEvent thread holds across cross-process reads, and the navigation calls into
    /// another process. Production posts a thread message to the tracker thread (see HookProcess).
    /// </summary>
    private readonly Action<IntPtr> _requestQuickSwitch;

    public GlobalHotkeyDetector(UserSettings settings, ExplorerTracker explorerTracker, Action<IntPtr>? requestQuickSwitch = null)
    {
        _settings = settings;
        _explorerTracker = explorerTracker;
        // Without a tracker thread to post to, the same policy runs here: that is what tests get, and what any
        // caller that only needs the gesture answered gets.
        _requestQuickSwitch = requestQuickSwitch
            ?? (hwnd => QuickSwitchNavigator.Execute(_explorerTracker, hwnd));
    }

    public void OnKeyDown(int vkCode) => _modifierKeyState.OnKeyDown(vkCode);

    // Secure-desktop transitions can hide a modifier key-up from the low-level hook; refresh the
    // hook-owned snapshot before processing the next key event.
    internal void SynchronizeModifierState() => _modifierKeyState.Synchronize(vkCode =>
        (KeyboardNativeMethods.GetAsyncKeyState(vkCode) & 0x8000) != 0);

    internal bool HasControlAltOrWindowsDown => _modifierKeyState.HasControlAltOrWindowsDown;

    internal bool CheckModifiersMatch(string expectedModifier) =>
        KeyboardUtils.CheckModifiersMatch(expectedModifier, _modifierKeyState, "NONE");

    internal bool CheckModifiersMatchOnly(string expectedModifier) =>
        KeyboardUtils.CheckModifiersMatch(expectedModifier, _modifierKeyState, "CONTROL");

    public void OnKeyUp(int vkCode) => _modifierKeyState.OnKeyUp(vkCode);

    internal bool IsTapModifierKey(int vkCode) =>
        IsTapModifier(_settings.Hotkeys.ToggleWindowHotkey, vkCode)
        || IsTapModifier(_settings.Hotkeys.QuickSwitchHotkey, vkCode);

    private static bool IsTapModifier(string hotkey, int vkCode) =>
        HotkeyStringFormat.IsBareModifier(hotkey, out var modifier) && KeyboardUtils.IsModifierKey(vkCode, modifier);

    internal void CancelModifierTaps()
    {
        _toggleWindowTapDetector.ResetOnOtherInput();
        _quickSwitchTapDetector.ResetOnOtherInput();
    }

    // Feed every event before any early-return/consumption in the keyboard hook. Mouse cancellation
    // runs on the same hook thread. Suppressed and injected events cancel but never complete a tap.
    internal (bool ToggleWindow, bool QuickSwitch) ProcessModifierTaps(int vkCode, uint time,
        bool isDown, bool isInjected, bool allowed, bool allowQuickSwitch, IntPtr foreground, Func<int, bool> isKeyDown)
    {
        var toggleModifier = IsTapModifier(_settings.Hotkeys.ToggleWindowHotkey, vkCode);
        var switchModifier = IsTapModifier(_settings.Hotkeys.QuickSwitchHotkey, vkCode);
        var clean = allowed && !isInjected && (toggleModifier || switchModifier)
            && !HasOtherInputDown(vkCode, isKeyDown);
        var toggle = _toggleWindowTapDetector.ProcessKey(vkCode, time, isDown, toggleModifier, clean, isInjected, foreground);
        var quickSwitch = _quickSwitchTapDetector.ProcessKey(vkCode, time, isDown, switchModifier,
            clean && allowQuickSwitch, isInjected, foreground);
        return (toggle, quickSwitch);
    }

    internal static bool HasOtherInputDown(int modifierVk, Func<int, bool> isKeyDown)
    {
        // The current edge has not updated GetAsyncKeyState yet. Ignore its generic alias,
        // but include the opposite side, other keys and mouse buttons. Sample only at tap edges.
        var generic = modifierVk switch { 0xA0 or 0xA1 => 0x10, 0xA2 or 0xA3 => 0x11, 0xA4 or 0xA5 => 0x12, _ => modifierVk };
        for (var vk = 1; vk < 0xFF; vk++)
            if (vk != modifierVk && vk != generic && isKeyDown(vk)) return true;
        return false;
    }

    public bool CheckToggleWindowHotkey(int vkCode, uint time, out bool consumeKey, Action? onDoubleCtrl)
    {
        consumeKey = false;
        var triggered = false;
        if (!HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.ToggleWindowHotkey, out _))
        {
            HotkeyStringFormat.ParseCombo(_settings.Hotkeys.ToggleWindowHotkey, out var modifier, out var key);
            var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
            if (targetVk != 0 && vkCode == targetVk)
            {
                if (CheckModifiersMatch(modifier))
                {
                    triggered = true;
                    consumeKey = true;
                }
            }
        }

        if (triggered)
        {
            onDoubleCtrl?.Invoke();
        }
        return triggered;
    }

    /// <summary>
    /// Whether this keystroke is the toggle hotkey's chord form -- the same test the combo branch of
    /// <see cref="CheckToggleWindowHotkey"/> applies, answered without touching the tap detectors'
    /// state. False for the bare-modifier form, which no chord with a key of its own can be.
    /// </summary>
    internal bool IsToggleWindowComboDown(int vkCode)
    {
        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.ToggleWindowHotkey, out _))
            return false;

        HotkeyStringFormat.ParseCombo(_settings.Hotkeys.ToggleWindowHotkey, out var modifier, out var key);
        var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
        return targetVk != 0 && vkCode == targetVk && CheckModifiersMatch(modifier);
    }

    /// <summary>Whether this keystroke is the quick panel's own combination, answered without consuming it.</summary>
    internal bool IsQuickPanelComboDown(int vkCode)
    {
        HotkeyStringFormat.ParseCombo(_settings.Hotkeys.QuickPanelHotkey, out var modifier, out var key);
        var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
        return targetVk != 0 && vkCode == targetVk && CheckModifiersMatch(modifier);
    }

    /// <summary>The quick panel's own global combo. A plain combination, with no bare-modifier form.</summary>
    /// <remarks>
    /// The tap detectors the other two hotkeys carry exist because those can be configured as a bare
    /// modifier, which needs double-tap timing to tell apart from the same modifier being held down for
    /// something else. This one is always a real key, so there is nothing to disambiguate.
    /// </remarks>
    public bool CheckQuickPanelHotkey(int vkCode, out bool consumeKey)
    {
        consumeKey = false;

        HotkeyStringFormat.ParseCombo(_settings.Hotkeys.QuickPanelHotkey, out var modifier, out var key);
        var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
        if (targetVk == 0 || vkCode != targetVk) return false;
        if (!CheckModifiersMatch(modifier)) return false;

        consumeKey = true;
        return true;
    }

    /// <summary>The global shortcut for opening Quick Navigation in desktop mode.</summary>
    public bool CheckQuickNavigationHotkey(int vkCode, out bool consumeKey)
    {
        consumeKey = false;
        HotkeyStringFormat.ParseCombo(_settings.Hotkeys.QuickNavigationHotkey, out var modifier, out var key);
        var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
        if (targetVk == 0 || vkCode != targetVk || !CheckModifiersMatch(modifier)) return false;

        consumeKey = true;
        return true;
    }

    public bool CheckAndHandleQuickSwitch(int vkCode, uint time, out bool consumeKey)
    {
        consumeKey = false;
        var triggered = false;
        if (!HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.QuickSwitchHotkey, out _))
        {
            HotkeyStringFormat.ParseCombo(_settings.Hotkeys.QuickSwitchHotkey, out var modifier, out var key);
            var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
            if (targetVk != 0 && vkCode == targetVk)
            {
                if (CheckModifiersMatch(modifier))
                {
                    triggered = true;
                }
            }
        }

        return TryHandleQuickSwitchNavigation(triggered, out consumeKey);
    }

    // Quick Switch's trigger doesn't just toggle a window like the other hotkey does -- it re-navigates
    // the active (dialog) Explorer-like window back to the last folder that was active outside it. Kept as
    // its own method so the gesture-detection above (shared via ModifierDoubleTapDetector) and this
    // navigation policy read as two separate steps, even though they still live in the same class.
    internal bool TryHandleQuickSwitchNavigation(bool triggered, out bool consumeKey)
    {
        consumeKey = false;
        if (!triggered) return false;

        // Read without the lock, deliberately. These fields are written under StateLock, but waiting for it
        // here used to drop the keystroke -- the WinEvent thread holds it across cross-process reads, and a
        // low-level hook that overruns LowLevelHooksTimeout is removed by Windows. A stale answer costs one
        // skipped or one futile hand-off, never a wrong window: the tracker thread re-reads the state before
        // it navigates (see QuickSwitchNavigator.Execute).
        if (!_explorerTracker.IsActiveWindowDialog || _explorerTracker.ActiveAdapter == null)
        {
            Logger.Log("[GlobalHotkeyDetector] Quick Switch ignored: no file dialog is tracked.", LogLevel.Info);
            return false;
        }

        // Nothing to switch to, so the keystroke is left to the host window: a dialog that swallows Ctrl+G
        // without navigating is a keystroke the user cannot get back.
        if (!_explorerTracker.HasNavigationSource && string.IsNullOrEmpty(_explorerTracker.LastActiveExplorerPath) && _explorerTracker.LastPathProvider == null)
        {
            Logger.Log("[GlobalHotkeyDetector] Quick Switch ignored: no browsed folder is known yet.", LogLevel.Info);
            return false;
        }

        _requestQuickSwitch(_explorerTracker.ActiveHwnd);
        consumeKey = true;
        return true;
    }
}
