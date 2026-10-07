using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
public sealed class ModifierDoubleTapDetectorTests
{
    private const int Left = 0xA2;
    private const int Right = 0xA3;

    private static bool Key(ModifierDoubleTapDetector detector, uint time, bool down, int vk = Left,
        bool modifier = true, bool allowed = true, bool injected = false, int foreground = 1) =>
        detector.ProcessKey(vk, time, down, modifier, allowed, injected, (IntPtr)foreground);

    private static bool Tap(ModifierDoubleTapDetector detector, uint start, uint hold = 40, int vk = Left, int foreground = 1)
    {
        Assert.IsFalse(Key(detector, start, true, vk, foreground: foreground));
        return Key(detector, unchecked(start + hold), false, vk, foreground: foreground);
    }

    [TestMethod]
    public void DoubleTap_TriggersOnlyOnSecondRelease()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        Assert.IsFalse(Key(detector, 1200, true));
        Assert.IsTrue(Key(detector, 1240, false));
        Assert.IsFalse(Key(detector, 1241, false)); // Duplicate release cannot trigger again.
        Assert.IsFalse(Tap(detector, 1400)); // The second tap is consumed, not reused.
        Assert.IsTrue(Tap(detector, 1600));
    }

    [TestMethod]
    [DataRow(100, false)]
    [DataRow(101, true)]
    [DataRow(349, true)]
    [DataRow(350, false)]
    [DataRow(500, false)]
    public void DownInterval_UsesExclusiveBounds(int interval, bool expected)
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        Assert.AreEqual(expected, Tap(detector, (uint)(1000 + interval)));
    }

    [TestMethod]
    [DataRow(49, false)]
    [DataRow(50, true)]
    public void ReleaseGap_RejectsBriefReleaseChatter(int gap, bool expected)
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000, 150));
        Assert.AreEqual(expected, Tap(detector, (uint)(1150 + gap)));
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, true)]
    [DataRow(250, true)]
    [DataRow(251, false)]
    public void FirstHold_MustBeShortAndNonzero(int hold, bool expected)
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000, (uint)hold));
        Assert.AreEqual(expected, Tap(detector, 1320));
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, true)]
    [DataRow(250, true)]
    [DataRow(251, false)]
    public void SecondHold_MustBeShortAndNonzero(int hold, bool expected)
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        Assert.AreEqual(expected, Tap(detector, 1200, (uint)hold));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void OtherKeyDuringSecondPress_CancelsEvenOnKeyUp(bool otherDown)
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        Assert.IsFalse(Key(detector, 1200, true));
        Assert.IsFalse(Key(detector, 1220, otherDown, 0x43, modifier: false));
        Assert.IsFalse(Key(detector, 1240, false));
    }

    [TestMethod]
    public void MouseCancellation_PreservesHeldStateUntilRealRelease()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        Assert.IsFalse(Key(detector, 1200, true));
        detector.ResetOnOtherInput();
        Assert.IsFalse(Key(detector, 1210, true));
        Assert.IsFalse(Key(detector, 1240, false));
        Assert.IsFalse(Tap(detector, 1400));
        Assert.IsTrue(Tap(detector, 1600));
    }

    [TestMethod]
    public void RepeatOrMissingRelease_CancelsWithoutInventingATap()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Key(detector, 1000, true));
        Assert.IsFalse(Key(detector, 1200, true));
        Assert.IsFalse(Key(detector, 1240, false));
        Assert.IsFalse(Tap(detector, 1400));
        Assert.IsTrue(Tap(detector, 1600));
    }

    [TestMethod]
    public void RepeatDuringSecondPress_CancelsBeforeRelease()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        Assert.IsFalse(Key(detector, 1200, true));
        Assert.IsFalse(Key(detector, 1220, true));
        Assert.IsFalse(Key(detector, 1240, false));
    }

    [TestMethod]
    public void OppositeSideRelease_DoesNotReleaseTheHeldKey()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Key(detector, 1000, true));
        Assert.IsFalse(Key(detector, 1040, false, Right));
        Assert.IsFalse(Key(detector, 1200, true));
        Assert.IsFalse(Key(detector, 1240, false));
    }

    [TestMethod]
    public void OverlappingSides_CancelBothTaps()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Key(detector, 1000, true));
        Assert.IsFalse(Key(detector, 1020, true, Right));
        Assert.IsFalse(Key(detector, 1040, false));
        Assert.IsFalse(Key(detector, 1060, false, Right));
        Assert.IsFalse(Tap(detector, 1200, vk: Right));
        Assert.IsTrue(Tap(detector, 1400, vk: Right));
    }

    [TestMethod]
    public void DifferentSides_DoNotFormDoubleTap()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        Assert.IsFalse(Tap(detector, 1200, vk: Right));
        Assert.IsTrue(Tap(detector, 1400, vk: Right));
    }

    [TestMethod]
    public void InjectedRelease_CannotCompleteOrReleasePhysicalTap()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        Assert.IsFalse(Key(detector, 1200, true));
        Assert.IsFalse(Key(detector, 1210, false, injected: true));
        Assert.IsFalse(Key(detector, 1220, true));
        Assert.IsFalse(Key(detector, 1240, false));
        Assert.IsFalse(Tap(detector, 1400));
        Assert.IsTrue(Tap(detector, 1600));
    }

    [TestMethod]
    public void InjectedDown_CannotStartATap()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Key(detector, 1000, true, injected: true));
        Assert.IsFalse(Key(detector, 1040, false));
        Assert.IsFalse(Tap(detector, 1200));
    }

    [TestMethod]
    public void DisabledRelease_DiscardsPendingGesture()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        Assert.IsFalse(Key(detector, 1200, true));
        Assert.IsFalse(Key(detector, 1240, false, allowed: false));
        Assert.IsFalse(Tap(detector, 1400));
    }

    [TestMethod]
    public void ForegroundChange_RequiresFreshPairInNewWindow()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        Assert.IsFalse(Tap(detector, 1200, foreground: 2));
        Assert.IsTrue(Tap(detector, 1400, foreground: 2));
    }

    [TestMethod]
    public void ForegroundChangeDuringSecondPress_Cancels()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000));
        Assert.IsFalse(Key(detector, 1200, true));
        Assert.IsFalse(Key(detector, 1240, false, foreground: 2));
    }

    [TestMethod]
    public void NoForeground_CannotTrigger()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, 1000, foreground: 0));
        Assert.IsFalse(Tap(detector, 1200, foreground: 0));
    }

    [TestMethod]
    public void NativeTimestampRollover_PreservesIntervals()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.IsFalse(Tap(detector, uint.MaxValue - 100));
        Assert.IsTrue(Tap(detector, 99));
    }
}
