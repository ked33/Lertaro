using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Helpers;

[TestClass]
public sealed class SearchFilterShortcutTests
{
    private static readonly SearchFilterShortcut Link = new("lnk", "Ctrl+D1", "@lnk", "*.lnk");
    private static readonly SearchFilterShortcut Pdf = new("pdf", "Ctrl+D2", "@pdf", "*.pdf");

    [TestMethod]
    public void Toggle_RepeatIsConsumedWithoutSwitching_NextPressClears()
    {
        var refreshes = 0;
        var session = new SearchFilterSession(() => refreshes++);
        Assert.IsTrue(session.TryToggle(Key.D1, ModifierKeys.Control, false, [Link]));
        Assert.AreEqual("lnk", session.Keyword);
        Assert.IsTrue(session.TryToggle(Key.D1, ModifierKeys.Control, true, [Link]));
        Assert.AreEqual(1, refreshes);
        Assert.IsTrue(session.IsActive);
        Assert.IsTrue(session.TryToggle(Key.D1, ModifierKeys.Control, false, [Link]));
        Assert.IsFalse(session.IsActive);
        Assert.AreEqual(2, refreshes);
    }

    [TestMethod]
    public void Toggle_OtherFilterReplaces_PreservesExplicitTokens()
    {
        var session = new SearchFilterSession(() => { });
        session.Set(Link);
        Assert.IsTrue(session.TryToggle(Key.D2, ModifierKeys.Control, false, [Link, Pdf]));
        CollectionAssert.AreEqual(new[] { "-f", "@pdf" }, session.WithTokens(["-f", "@pdf"]).ToArray());
        session.ClearCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { "-f", "@pdf" }, session.WithTokens(["-f", "@pdf"]).ToArray());
    }

    [TestMethod]
    public void Toggle_DoesNotAcceptNumpad_ExtraModifier_OrAmbiguousBindings()
    {
        var session = new SearchFilterSession(() => Assert.Fail("Should not toggle"));
        Assert.IsFalse(session.TryToggle(Key.NumPad1, ModifierKeys.Control, false, [Link]));
        Assert.IsFalse(session.TryToggle(Key.D1, ModifierKeys.Control | ModifierKeys.Shift, false, [Link]));
        Assert.IsFalse(session.TryToggle(Key.D1, ModifierKeys.Control, false, [Link, Pdf with { Hotkey = "Ctrl+1" }]));
    }

    [TestMethod]
    [DataRow("Ctrl+1", Key.D1)]
    [DataRow("Ctrl+D1", Key.D1)]
    [DataRow("Ctrl+NumPad1", Key.NumPad1)]
    public void Parse_DigitsKeepTheirKeyboardIdentity(string text, Key expected)
    {
        Assert.IsTrue(WpfUiHelper.TryParseHotkey(text, out var key, out var mods));
        Assert.AreEqual(expected, key);
        Assert.AreEqual(ModifierKeys.Control, mods);
    }

    [TestMethod]
    [DataRow("Ctrl+42")]
    [DataRow("Ctrl+1+2")]
    [DataRow("Ctrl+unknown")]
    public void Parse_RejectsInvalidPrimaryKeys(string text) => Assert.IsFalse(WpfUiHelper.TryParseHotkey(text, out _, out _));

    [TestMethod]
    public void Display_UsesFriendlyDigitsWithoutChangingNumpad()
    {
        Assert.AreEqual("Ctrl+1", HotkeyStringFormat.ToDisplayText("Ctrl+D1"));
        Assert.AreEqual("Ctrl+NumPad1", HotkeyStringFormat.ToDisplayText("Ctrl+NumPad1"));
    }

    [TestMethod]
    public void Validation_RejectsCanonicalDuplicatesAndReservedBindings()
    {
        Assert.IsNotNull(SearchFilterShortcutValidation.Validate([Link, Pdf with { Hotkey = "Control+1" }], _ => null));
        Assert.IsNotNull(SearchFilterShortcutValidation.Validate([Link, Pdf with { Keyword = "LNK" }], _ => null));
        Assert.IsNotNull(SearchFilterShortcutValidation.Validate([Link], _ => "Result shortcut"));
        Assert.IsNull(SearchFilterShortcutValidation.Validate([Link, Pdf], _ => null));
        Assert.IsNotNull(SearchFilterShortcutValidation.FindReservedOwner("Ctrl+1", new HotkeyPageSettings()));
        Assert.IsNotNull(SearchFilterShortcutValidation.FindReservedOwner("Ctrl+C", new HotkeyPageSettings()));
        Assert.IsNotNull(SearchFilterShortcutValidation.FindReservedOwner("Win+1", new HotkeyPageSettings()));
    }
}
