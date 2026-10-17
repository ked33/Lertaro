using System.Runtime.InteropServices;
using Lertaro.Core.Hook.InlineSearch;
using Key = Lertaro.Core.Hook.InlineSearch.SearchActivationInputBuffer.Key;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
public sealed class SearchActivationInputBufferTests
{
    private static readonly Key A = new(0x41, 0x1E);
    private static readonly Key B = new(0x42, 0x30);
    private static readonly Key Shift = new(0xA0, 0x2A);
    private static readonly IntPtr Origin = (IntPtr)1, Search = (IntPtr)2, Other = (IntPtr)3;

    [TestMethod]
    public void TypingBeforeFocus_PreservesPressReleaseRepeatAndShiftOrder()
    {
        var f = new Replay();
        Key[] typed = [A, A, A with { IsUp = true }, Shift, B, B with { IsUp = true }, Shift with { IsUp = true }];
        foreach (var key in typed) Assert.IsTrue(f.Physical(key));
        Assert.AreEqual(0, f.Batches);
        f.Complete();
        f.Drain();
        CollectionAssert.AreEqual(typed, f.Delivered.ToArray());
        Assert.IsFalse(f.Buffer.NeedsInput);
        Assert.IsFalse(f.Physical(B));
    }

    [TestMethod]
    public void TypingDuringReplay_CannotOvertakeBufferedInputOrDuplicateAcknowledgement()
    {
        var f = new Replay();
        Assert.IsTrue(f.Physical(A));
        Assert.IsTrue(f.Physical(A with { IsUp = true }));
        f.Complete();
        f.Complete(); // Must not disarm the buffer before its queued batch is delivered.
        Assert.IsTrue(f.Physical(B));
        Assert.IsTrue(f.Physical(B with { IsUp = true }));
        f.Drain();
        Assert.AreEqual(2, f.Batches);
        CollectionAssert.AreEqual(new[] { A, A with { IsUp = true }, B, B with { IsUp = true } }, f.Delivered.ToArray());
    }

    [TestMethod]
    public void NewSummon_IgnoresOldAcknowledgementCancellationAndReplay()
    {
        var f = new Replay();
        Assert.IsTrue(f.Physical(A));
        Assert.IsTrue(f.Physical(A with { IsUp = true }));
        var oldRequest = f.Request;
        f.Request = f.Buffer.Begin(Origin, f.Now);
        Assert.IsTrue(f.Physical(B));
        Assert.IsTrue(f.Physical(B with { IsUp = true }));
        f.Buffer.Complete(oldRequest, Search, Search, f.Now);
        f.Buffer.Complete(oldRequest, IntPtr.Zero, Search, f.Now);
        Assert.IsFalse(f.Buffer.AllowReplay(oldRequest, A, Search, f.Now));
        Assert.AreEqual(0, f.Batches);
        f.Complete();
        f.Drain();
        CollectionAssert.AreEqual(new[] { B, B with { IsUp = true } }, f.Delivered.ToArray());
    }

    [TestMethod]
    public void FocusSwitch_CancelsBufferAndSwallowsOrphanRepeatsUntilRelease()
    {
        var f = new Replay();
        Assert.IsTrue(f.Physical(A));
        f.Foreground = Other;
        Assert.IsTrue(f.Physical(A));
        Assert.IsTrue(f.Physical(A with { IsUp = true }));
        Assert.IsFalse(f.Physical(B));
        f.Complete();
        Assert.AreEqual(0, f.Batches);
        Assert.IsFalse(f.Buffer.NeedsInput);
    }

    [TestMethod]
    [DataRow(749, true)]
    [DataRow(750, false)]
    public void FocusAcknowledgement_ExpiresAtTheDeadline(int elapsed, bool delivered)
    {
        var f = new Replay();
        Assert.IsTrue(f.Physical(A));
        Assert.IsTrue(f.Physical(A with { IsUp = true }));
        f.Now = 100 + elapsed;
        f.Complete();
        f.Drain();
        Assert.AreEqual(delivered ? 2 : 0, f.Delivered.Count);
        Assert.IsFalse(f.Buffer.NeedsInput);
    }

    [TestMethod]
    public void MissingAcknowledgement_DoesNotKeepSwallowingNewKeys()
    {
        var f = new Replay();
        Assert.IsTrue(f.Physical(A));
        f.Now = 850;
        Assert.IsFalse(f.Physical(B));
        Assert.IsTrue(f.Physical(A with { IsUp = true }));
        Assert.IsFalse(f.Buffer.NeedsInput);
        Assert.AreEqual(0, f.Batches);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    public void AcknowledgementForNonForegroundWindow_DiscardsInput(int target)
    {
        var f = new Replay();
        Assert.IsTrue(f.Physical(A));
        f.Buffer.Complete(f.Request, (IntPtr)target, Search, f.Now);
        Assert.IsTrue(f.Physical(A with { IsUp = true }));
        Assert.AreEqual(0, f.Batches);
        Assert.IsFalse(f.Buffer.NeedsInput);
    }

    [TestMethod]
    [DataRow(0xA2)] // Ctrl
    [DataRow(0xA4)] // Alt
    [DataRow(0x5B)] // Windows
    [DataRow(0x70)] // F1
    public void ShortcutDuringHandoff_IsNotReplayedAsSearchInput(int vk)
    {
        var f = new Replay();
        Assert.IsTrue(f.Physical(A));
        Assert.IsFalse(f.Physical(new Key(vk, 0)));
        Assert.IsTrue(f.Physical(A with { IsUp = true }));
        f.Complete();
        Assert.AreEqual(0, f.Batches);
    }

    [TestMethod]
    public void ClickDuringReplay_DropsRemainingTextButBalancesDeliveredShift()
    {
        var f = new Replay();
        Assert.IsTrue(f.Physical(Shift));
        Assert.IsTrue(f.Physical(A));
        f.Complete();
        f.DispatchOne(); // Shift-down reached the search box.
        f.Foreground = Other;
        f.Buffer.Cancel(); // Mouse hook cancellation.
        f.Drain();
        CollectionAssert.AreEqual(new[] { Shift, Shift with { IsUp = true } }, f.Delivered.ToArray());
        Assert.IsTrue(f.Physical(Shift with { IsUp = true }));
        Assert.IsTrue(f.Physical(A with { IsUp = true }));
        Assert.IsFalse(f.Buffer.NeedsInput);
    }

    [TestMethod]
    public void ForegroundChangeDuringReplay_RejectsTextEvenWithoutMouseNotification()
    {
        var f = new Replay();
        Assert.IsTrue(f.Physical(Shift));
        Assert.IsTrue(f.Physical(A));
        f.Complete();
        f.DispatchOne();
        f.Foreground = Other;
        f.Drain();
        CollectionAssert.AreEqual(new[] { Shift, Shift with { IsUp = true } }, f.Delivered.ToArray());
    }

    [TestMethod]
    public void SuccessfulHandoff_LeavesHeldShiftAvailableForNormalShiftClick()
    {
        var f = new Replay();
        Assert.IsTrue(f.Physical(Shift));
        f.Complete();
        f.Drain();
        Assert.IsFalse(f.Buffer.NeedsInput);
        f.Buffer.Cancel(); // A later click must not synthesize Shift-up.
        f.Drain();
        CollectionAssert.AreEqual(new[] { Shift }, f.Delivered.ToArray());
        Assert.IsFalse(f.Physical(Shift with { IsUp = true }));
    }

    [TestMethod]
    public void NewEmptyHandoff_DoesNotDropCleanupFromAnOlderReplay()
    {
        var f = new Replay();
        Assert.IsTrue(f.Physical(Shift));
        f.Complete();
        f.DispatchOne();
        f.Request = f.Buffer.Begin(Origin, f.Now);
        f.Complete();
        f.Drain();
        CollectionAssert.AreEqual(new[] { Shift, Shift with { IsUp = true } }, f.Delivered.ToArray());
        Assert.IsTrue(f.Physical(Shift with { IsUp = true }));
        Assert.IsFalse(f.Buffer.NeedsInput);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void SynchronousReplayCallbacks_BalanceKeysEvenWhenSendPartlyFails(bool successfulSend)
    {
        var delivered = new List<Key>();
        SearchActivationInputBuffer? buffer = null;
        var sends = 0;
        buffer = new SearchActivationInputBuffer((request, keys) =>
        {
            sends++;
            foreach (var key in keys)
                if (buffer!.AllowReplay(request, key, Search, 101)) delivered.Add(key);
            if (sends == 1 && !successfulSend) return false;
            buffer!.AllowReplay(request, new Key(SearchActivationInputBuffer.EndOfBatch, 0), Search, 101);
            return true;
        });
        var request = buffer.Begin(Origin, 100);
        Assert.IsTrue(buffer.HandlePhysical(Shift, Origin, false, 101));
        buffer.Complete(request, Search, Search, 101);
        CollectionAssert.AreEqual(successfulSend ? new[] { Shift } : new[] { Shift, Shift with { IsUp = true } }, delivered);
        Assert.AreEqual(!successfulSend, buffer.HandlePhysical(Shift with { IsUp = true }, Search, true, 102));
        Assert.IsFalse(buffer.NeedsInput);
    }

    [TestMethod]
    public void CapacityLimit_CancelsWithoutLeakingHeldKeyRepeats()
    {
        var f = new Replay();
        for (var i = 0; i <= SearchActivationInputBuffer.Capacity; i++) Assert.IsTrue(f.Physical(A));
        Assert.IsTrue(f.Physical(A with { IsUp = true }));
        Assert.IsFalse(f.Physical(B));
        f.Complete();
        Assert.AreEqual(0, f.Batches);
    }

    [TestMethod]
    public void SendFailure_DoesNotRetryTextOrLeakItsPhysicalRelease()
    {
        var f = new Replay { FailSend = true };
        Assert.IsTrue(f.Physical(A));
        f.Complete();
        Assert.IsTrue(f.Physical(A with { IsUp = true }));
        Assert.IsFalse(f.Physical(B));
        Assert.AreEqual(1, f.Batches);
        Assert.IsFalse(f.Buffer.NeedsInput);
    }

    [TestMethod]
    public void KeyboardLayoutChange_MatchesReleaseByScanCode()
    {
        var f = new Replay();
        Assert.IsFalse(f.Physical(B with { IsUp = true })); // Its press was never suppressed.
        Assert.IsTrue(f.Physical(A));
        f.Foreground = Search;
        var release = A with { VirtualKey = 0x51, IsUp = true };
        Assert.IsTrue(f.Physical(release));
        f.Complete();
        f.Drain();
        CollectionAssert.AreEqual(new[] { A, release }, f.Delivered.ToArray());
        Assert.IsFalse(f.Buffer.NeedsInput);
        Assert.AreNotEqual(new Key(0x25, 0x4B).Identity, new Key(0x25, 0x4B, Extended: true).Identity);
    }

    [TestMethod]
    public void NoForegroundOrNoBufferedKeys_NeedsNoReplay()
    {
        var f = new Replay();
        Assert.AreEqual(0, f.Buffer.Begin(IntPtr.Zero, f.Now));
        Assert.IsFalse(f.Buffer.NeedsInput);
        f.Request = f.Buffer.Begin(Origin, f.Now);
        f.Complete();
        Assert.AreEqual(0, f.Batches);
        Assert.IsFalse(f.Buffer.NeedsInput);
    }

    [TestMethod]
    public void NativeInput_UsesPlatformLayoutScanCodesAndTaggedGenerations()
    {
        Assert.AreEqual(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<SearchActivationInputNative.Input>());
        Assert.AreEqual((IntPtr)(IntPtr.Size == 8 ? 8 : 4),
            Marshal.OffsetOf<SearchActivationInputNative.Input>(nameof(SearchActivationInputNative.Input.Data)));
        if (IntPtr.Size != 8) return; // Buffering is enabled only in the shipped 64-bit architectures.
        var input = SearchActivationInputNative.CreateInput(int.MaxValue, new Key(0x25, 0x4B, IsUp: true, Extended: true));
        Assert.AreEqual(1u, input.Type);
        Assert.AreEqual((ushort)0, input.Data.Keyboard.VirtualKey);
        Assert.AreEqual((ushort)0x4B, input.Data.Keyboard.ScanCode);
        Assert.AreEqual(11u, input.Data.Keyboard.Flags);
        Assert.IsTrue(SearchActivationInputNative.TryGetRequest(input.Data.Keyboard.ExtraInfo, out var request));
        Assert.AreEqual(int.MaxValue, request);
        Assert.IsFalse(SearchActivationInputNative.TryGetRequest(IntPtr.Zero, out _));
        Assert.IsFalse(SearchActivationInputNative.TryGetRequest((IntPtr)123, out _));
        var marker = SearchActivationInputNative.CreateInput(1, new Key(SearchActivationInputBuffer.EndOfBatch, 0));
        Assert.AreEqual((ushort)0xFF, marker.Data.Keyboard.VirtualKey);
        Assert.AreEqual(0u, marker.Data.Keyboard.Flags);
    }

    // Model SendInput's queued hook callbacks. Tests can place physical input or a foreground switch
    // between individual replay events without sending keystrokes to the CI machine's desktop.
    private sealed class Replay
    {
        private readonly Queue<(int Request, Key Key)> _queued = new();
        internal readonly SearchActivationInputBuffer Buffer;
        internal readonly List<Key> Delivered = [];
        internal int Request, Batches;
        internal bool FailSend;
        internal long Now = 101;
        internal IntPtr Foreground = Origin;

        internal Replay()
        {
            Buffer = new SearchActivationInputBuffer((request, keys) =>
            {
                Batches++;
                if (FailSend) return false;
                foreach (var key in keys) _queued.Enqueue((request, key));
                _queued.Enqueue((request, new Key(SearchActivationInputBuffer.EndOfBatch, 0)));
                return true;
            });
            Request = Buffer.Begin(Origin, 100);
        }

        internal bool Physical(Key key) => Buffer.HandlePhysical(key, Foreground, Foreground == Search, Now);
        internal void Complete()
        {
            Foreground = Search;
            Buffer.Complete(Request, Search, Foreground, Now);
        }
        internal void DispatchOne()
        {
            var (request, key) = _queued.Dequeue();
            if (Buffer.AllowReplay(request, key, Foreground, Now)) Delivered.Add(key);
        }
        internal void Drain()
        {
            var remaining = 1000;
            while (_queued.Count != 0)
            {
                Assert.IsTrue(remaining-- > 0, "replay must terminate");
                DispatchOne();
            }
        }
    }
}
