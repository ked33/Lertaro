using Lertaro.App.Views.QuickSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.QuickSearchWindow.Helpers;

[TestClass]
public sealed class QuickSearchWindowFocusHelperTests
{
    [TestMethod]
    public void ResolveSearchBoxFocus_ClipboardHandoff_SelectsEverything() =>
        Assert.AreEqual(QuickSearchWindowFocusHelper.SearchBoxFocus.SelectAll,
            QuickSearchWindowFocusHelper.ResolveSearchBoxFocus(selectSearchText: true, caretAtEnd: false));

    [TestMethod]
    public void ResolveSearchBoxFocus_ClipboardHandoffBeatsCarriedOverQuery() =>
        // Select-all exists so the next keystroke replaces the imported text; parking the caret after it
        // instead would leave the two behaviours fighting over the same keystroke.
        Assert.AreEqual(QuickSearchWindowFocusHelper.SearchBoxFocus.SelectAll,
            QuickSearchWindowFocusHelper.ResolveSearchBoxFocus(selectSearchText: true, caretAtEnd: true));

    [TestMethod]
    public void ResolveSearchBoxFocus_CarriedOverQuery_ParksTheCaretAtTheEnd() =>
        Assert.AreEqual(QuickSearchWindowFocusHelper.SearchBoxFocus.CaretAtEnd,
            QuickSearchWindowFocusHelper.ResolveSearchBoxFocus(selectSearchText: false, caretAtEnd: true));

    [TestMethod]
    public void ResolveSearchBoxFocus_PlainSummon_LeavesTheCaretAlone() =>
        Assert.AreEqual(QuickSearchWindowFocusHelper.SearchBoxFocus.LeaveCaret,
            QuickSearchWindowFocusHelper.ResolveSearchBoxFocus(selectSearchText: false, caretAtEnd: false));

    [TestMethod]
    public void FocusAttempt_ImmediateSuccessAcknowledgesOnceWithoutWaitingForTimer()
    {
        var results = new List<bool>();
        var attempt = new QuickSearchWindowFocusHelper.FocusAttempt(500, results.Add);
        Assert.IsTrue(attempt.Tick(0, true, () => true));
        Assert.IsTrue(attempt.Succeeded);
        Assert.IsTrue(attempt.Tick(20, true, () => throw new AssertFailedException("already focused")));
        attempt.Cancel();
        CollectionAssert.AreEqual(new[] { true }, results);
    }

    [TestMethod]
    public void FocusAttempt_FailedNativeOrTextBoxFocusRetriesUntilBothAreReady()
    {
        var results = new List<bool>();
        var attempt = new QuickSearchWindowFocusHelper.FocusAttempt(500, results.Add);
        Assert.IsFalse(attempt.Tick(0, true, () => false));
        Assert.IsFalse(attempt.Tick(20, true, () => false));
        Assert.IsEmpty(results);
        Assert.IsTrue(attempt.Tick(40, true, () => true));
        CollectionAssert.AreEqual(new[] { true }, results);
    }

    [TestMethod]
    [DataRow(500L, true)]
    [DataRow(20L, false)]
    public void FocusAttempt_TimeoutOrForegroundChangeNeverAttemptsAnotherActivation(long now, bool canContinue)
    {
        var results = new List<bool>();
        var attempt = new QuickSearchWindowFocusHelper.FocusAttempt(500, results.Add);
        Assert.IsFalse(attempt.Tick(0, true, () => false));
        Assert.IsTrue(attempt.Tick(now, canContinue, () => throw new AssertFailedException("must not steal focus")));
        Assert.IsFalse(attempt.Succeeded);
        CollectionAssert.AreEqual(new[] { false }, results);
    }

    [TestMethod]
    public void FocusAttempt_HideOrResummonInvalidatesAlreadyQueuedTimerCallbacks()
    {
        var oldResults = new List<bool>();
        var newResults = new List<bool>();
        var oldAttempt = new QuickSearchWindowFocusHelper.FocusAttempt(500, oldResults.Add);
        Assert.IsFalse(oldAttempt.Tick(0, true, () => false));
        oldAttempt.Cancel();
        oldAttempt.Cancel();
        var newAttempt = new QuickSearchWindowFocusHelper.FocusAttempt(600, newResults.Add);
        Assert.IsTrue(oldAttempt.Tick(40, true, () => throw new AssertFailedException("stale callback")));
        Assert.IsTrue(newAttempt.Tick(100, true, () => true));
        CollectionAssert.AreEqual(new[] { false }, oldResults);
        CollectionAssert.AreEqual(new[] { true }, newResults);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void FocusAttempt_HideDuringNativeActivationCannotAcknowledgeSuccess(bool focusResult)
    {
        var results = new List<bool>();
        var attempt = new QuickSearchWindowFocusHelper.FocusAttempt(500, results.Add);
        Assert.IsTrue(attempt.Tick(0, true, () =>
        {
            attempt.Cancel(); // SetForegroundWindow/Activate can pump messages.
            return focusResult;
        }));
        Assert.IsFalse(attempt.Succeeded);
        CollectionAssert.AreEqual(new[] { false }, results);
    }
}
