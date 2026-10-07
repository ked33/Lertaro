using Lertaro.Core.Hook;
using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
public sealed class GlobalHotkeyDetectorTests
{
    [TestMethod]
    public void QuickPanelHotkey_RemainsAvailableAfterAnotherCtrlChord()
    {
        var settings = new UserSettings();
        settings.Hotkeys.QuickPanelHotkey = "Ctrl+F2";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0xA2);
        Assert.IsFalse(detector.CheckQuickPanelHotkey(0x4E, out _));
        detector.OnKeyUp(0x4E);
        detector.OnKeyUp(0xA2);

        detector.OnKeyDown(0xA2);
        var triggered = detector.CheckQuickPanelHotkey(0x71, out var consumeKey);

        Assert.IsTrue(triggered);
        Assert.IsTrue(consumeKey);
    }

    [TestMethod]
    public void ToggleHotkey_RecognizesTrackedAltState()
    {
        var settings = new UserSettings();
        settings.Hotkeys.ToggleWindowHotkey = "Alt+Space";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0xA4);
        var triggered = detector.CheckToggleWindowHotkey(0x20, 1000, out var consumeKey, null);

        Assert.IsTrue(triggered);
        Assert.IsTrue(consumeKey);
    }

    [TestMethod]
    public void QuickPanelHotkey_RecognizesMultipleTrackedModifiers()
    {
        var settings = new UserSettings();
        settings.Hotkeys.QuickPanelHotkey = "Ctrl+Alt+Shift+Win+F2";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0xA2);
        detector.OnKeyDown(0xA4);
        detector.OnKeyDown(0xA0);
        detector.OnKeyDown(0x5B);

        var triggered = detector.CheckQuickPanelHotkey(0x71, out var consumeKey);

        Assert.IsTrue(triggered);
        Assert.IsTrue(consumeKey);
    }

    private static GlobalHotkeyDetector CreateTapDetector(string toggle = "Ctrl", string quickSwitch = "Ctrl+G")
    {
        var settings = new UserSettings();
        settings.Hotkeys.ToggleWindowHotkey = toggle;
        settings.Hotkeys.QuickSwitchHotkey = quickSwitch;
        return new GlobalHotkeyDetector(settings, new ExplorerTracker());
    }

    private static (bool ToggleWindow, bool QuickSwitch) Feed(GlobalHotkeyDetector detector, int vk, uint time, bool down,
        bool injected = false, bool allowed = true, bool allowSwitch = true, Func<int, bool>? held = null) =>
        detector.ProcessModifierTaps(vk, time, down, injected, allowed, allowSwitch, (IntPtr)1, held ?? (_ => false));

    [TestMethod]
    [DataRow("Ctrl", 0xA2)]
    [DataRow("Ctrl", 0xA3)]
    [DataRow("Shift", 0xA0)]
    [DataRow("Alt", 0xA4)]
    [DataRow("Win", 0x5B)]
    public void BareModifier_TriggersOnlyOnSecondRelease(string modifier, int vk)
    {
        var detector = CreateTapDetector(modifier);
        Assert.AreEqual((false, false), Feed(detector, vk, 1000, true));
        Assert.AreEqual((false, false), Feed(detector, vk, 1040, false));
        Assert.AreEqual((false, false), Feed(detector, vk, 1200, true));
        Assert.IsFalse(detector.CheckToggleWindowHotkey(vk, 1200, out var consumed, () => Assert.Fail("Down must not toggle.")));
        Assert.IsFalse(consumed);
        Assert.AreEqual((true, false), Feed(detector, vk, 1240, false));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void TapThenCopy_CancelsBeforeSecondRelease(bool letterDown)
    {
        var detector = CreateTapDetector();
        Feed(detector, 0xA2, 1000, true);
        Feed(detector, 0xA2, 1040, false);
        Assert.AreEqual((false, false), Feed(detector, 0xA2, 1200, true));
        Feed(detector, 0x43, 1220, letterDown);
        Assert.AreEqual((false, false), Feed(detector, 0xA2, 1240, false));
    }

    [TestMethod]
    [DataRow(0x41)] // Letter already held before Ctrl.
    [DataRow(0x01)] // Mouse button already held.
    [DataRow(0xA0)] // Shift chord.
    [DataRow(0xA3)] // Opposite Ctrl side, even if its down was missed.
    public void HeldInput_RejectsCandidateEvenWithoutItsKeyDown(int heldKey)
    {
        var detector = CreateTapDetector();
        Feed(detector, 0xA2, 1000, true, held: vk => vk == heldKey);
        Feed(detector, 0xA2, 1040, false);
        Feed(detector, 0xA2, 1200, true);
        Assert.AreEqual((false, false), Feed(detector, 0xA2, 1240, false));
    }

    [TestMethod]
    public void HeldInputAtFinalRelease_CancelsCandidate()
    {
        var detector = CreateTapDetector();
        Feed(detector, 0xA2, 1000, true);
        Feed(detector, 0xA2, 1040, false);
        Feed(detector, 0xA2, 1200, true);
        Assert.AreEqual((false, false), Feed(detector, 0xA2, 1240, false, held: vk => vk == 0x01));
    }

    [TestMethod]
    [DataRow(0xA2, 0x11)]
    [DataRow(0xA0, 0x10)]
    [DataRow(0xA4, 0x12)]
    [DataRow(0x5B, 0x5B)]
    public void AsyncState_IgnoresCurrentModifierAndItsGenericAlias(int side, int generic)
    {
        Assert.IsFalse(GlobalHotkeyDetector.HasOtherInputDown(side, vk => vk == side || vk == generic));
    }

    [TestMethod]
    [DataRow(0xA2)]
    [DataRow(0x41)]
    public void InjectedInput_CancelsPhysicalGesture(int injectedKey)
    {
        var detector = CreateTapDetector();
        Feed(detector, 0xA2, 1000, true);
        Feed(detector, 0xA2, 1040, false);
        Feed(detector, injectedKey, 1100, true, injected: true);
        Feed(detector, injectedKey, 1120, false, injected: true);
        Feed(detector, 0xA2, 1200, true);
        Assert.AreEqual((false, false), Feed(detector, 0xA2, 1240, false));
    }

    [TestMethod]
    public void DisabledHotkeyEvent_DiscardsCandidate()
    {
        var detector = CreateTapDetector();
        Feed(detector, 0xA2, 1000, true);
        Feed(detector, 0xA2, 1040, false);
        Feed(detector, 0x41, 1100, true, allowed: false);
        Feed(detector, 0xA2, 1200, true);
        Assert.AreEqual((false, false), Feed(detector, 0xA2, 1240, false));
    }

    [TestMethod]
    public void MouseCancellation_ClearsBothIndependentHotkeys()
    {
        var detector = CreateTapDetector("Ctrl", "Ctrl");
        Feed(detector, 0xA2, 1000, true);
        Feed(detector, 0xA2, 1040, false);
        detector.CancelModifierTaps();
        Feed(detector, 0xA2, 1200, true);
        Assert.AreEqual((false, false), Feed(detector, 0xA2, 1240, false));
    }

    [TestMethod]
    [DataRow(0x0200, false)] // Motion alone is not an interruption.
    [DataRow(0x0201, true)]  // Left down.
    [DataRow(0x0202, true)]  // Left up.
    [DataRow(0x0204, true)]  // Right down.
    [DataRow(0x0207, true)]  // Middle down.
    [DataRow(0x020A, true)]  // Wheel.
    [DataRow(0x020B, true)]  // X button.
    [DataRow(0x020E, true)]  // Horizontal wheel.
    public void MouseMessages_CancelButtonsAndWheelsButNotMovement(int message, bool expected)
    {
        Assert.AreEqual(expected, MouseHookService.IsTapCancellingMessage(message));
    }

    [TestMethod]
    [DataRow(true, true)]
    [DataRow(false, false)]
    public void QuickSwitchBareModifier_RespectsItsOwnVisibilityGate(bool allowSwitch, bool expected)
    {
        var detector = CreateTapDetector("Alt+Space", "Ctrl");
        Feed(detector, 0xA2, 1000, true, allowSwitch: allowSwitch);
        Feed(detector, 0xA2, 1040, false, allowSwitch: allowSwitch);
        Feed(detector, 0xA2, 1200, true, allowSwitch: allowSwitch);
        Assert.AreEqual((false, expected), Feed(detector, 0xA2, 1240, false, allowSwitch: allowSwitch));
    }

    [TestMethod]
    public void OrdinaryKeys_DoNotPollAllAsyncKeyStates()
    {
        var detector = CreateTapDetector();
        Assert.AreEqual((false, false), Feed(detector, 0x41, 1000, true,
            held: _ => throw new InvalidOperationException("Only modifier edges should be sampled.")));
    }
}
