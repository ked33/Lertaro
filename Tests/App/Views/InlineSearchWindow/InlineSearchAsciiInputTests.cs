using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lertaro.App.Views.Controls;
using Lertaro.App.Views.InlineSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.InlineSearchWindow;

[TestClass]
public sealed class InlineSearchAsciiInputTests
{
    [TestMethod]
    public void IsAllowed_AcceptsEntirePrintableAsciiRangeAndEmptyText()
    {
        Assert.IsTrue(InlineSearchAsciiInput.IsAllowed(""));
        Assert.IsTrue(InlineSearchAsciiInput.IsAllowed(
            new string(Enumerable.Range(32, 95).Select(c => (char)c).ToArray())));
    }

    [TestMethod]
    [DataRow("中文")]
    [DataRow("abc中文123")]
    [DataRow("日本語")]
    [DataRow("한글")]
    [DataRow("русский")]
    [DataRow("é")]
    [DataRow("e\u0301")]
    [DataRow("Ａ１，")]
    [DataRow("😀")]
    [DataRow("\u200b")]
    [DataRow("\u007f")]
    [DataRow("\t")]
    [DataRow("\0")]
    public void IsAllowed_RejectsNonAsciiAndControlCharacters(string text)
        => Assert.IsFalse(InlineSearchAsciiInput.IsAllowed(text));

    [StaTestMethod]
    public void Attach_DisablesImeOnlyForRestrictedBox()
    {
        var restricted = new TextBox();
        var unrestricted = new TextBox();
        InlineSearchAsciiInput.Attach(restricted);
        Assert.IsFalse(InputMethod.GetIsInputMethodEnabled(restricted));
        Assert.IsTrue(InputMethod.GetIsInputMethodEnabled(unrestricted));
        unrestricted.Text = "中文";
        Assert.AreEqual("中文", unrestricted.Text);
    }

    [StaTestMethod]
    public void ProgrammaticReplacement_RejectsWholeEditBeforeQueryObserver()
    {
        var box = new TextBox { Text = "before AFTER" };
        box.Select(7, 5);
        InlineSearchAsciiInput.Attach(box);
        var observed = new List<string>();
        box.TextChanged += (_, _) => observed.Add(box.Text);

        box.SelectedText = "混合abc";

        Assert.AreEqual("before AFTER", box.Text);
        Assert.IsTrue(observed.All(InlineSearchAsciiInput.IsAllowed));
        box.Text = "new query*.txt";
        Assert.AreEqual("new query*.txt", box.Text);
        box.Clear();
        Assert.AreEqual("", box.Text);
    }

    [StaTestMethod]
    public void Paste_MixedLanguageMultiline_PreservesTextAndSelection()
    {
        var box = new TextBox { Text = "before AFTER" };
        // Same initial handler/order as the actual shared SearchBoxControl.
        DataObject.AddPastingHandler(box, SearchBoxPasteHandler.Handle);
        InlineSearchAsciiInput.Attach(box);
        box.Select(7, 5);
        var args = Paste(box, "abc\r\n中文");

        Assert.IsTrue(args.CommandCancelled);
        Assert.AreEqual("before AFTER", box.Text);
        Assert.AreEqual(7, box.SelectionStart);
        Assert.AreEqual(5, box.SelectionLength);
    }

    [StaTestMethod]
    public void Paste_AsciiMultiline_KeepsSharedQuerySyntaxAndCaret()
    {
        var box = new TextBox { Text = "before AFTER" };
        DataObject.AddPastingHandler(box, SearchBoxPasteHandler.Handle);
        InlineSearchAsciiInput.Attach(box);
        box.Select(7, 5);
        var args = Paste(box, "abc\r\n123");

        Assert.IsTrue(args.CommandCancelled);
        Assert.AreEqual("before abc | 123", box.Text);
        Assert.AreEqual(box.Text.Length, box.CaretIndex);
        Assert.AreEqual(0, box.SelectionLength);
    }

    [StaTestMethod]
    public void Paste_AsciiSingleLine_ReplacesSelection()
    {
        var box = new TextBox { Text = "abc" };
        InlineSearchAsciiInput.Attach(box);
        box.SelectAll();
        Paste(box, "*.txt 123");
        Assert.AreEqual("*.txt 123", box.Text);
    }

    private static DataObjectPastingEventArgs Paste(TextBox box, string text)
    {
        var args = new DataObjectPastingEventArgs(
            new DataObject(DataFormats.UnicodeText, text), false, DataFormats.UnicodeText);
        box.RaiseEvent(args);
        return args;
    }
}
