using System.Windows;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Views.Controls;
using Lertaro.PluginSdk.Services;
using DataObject = System.Windows.DataObject;
using DataFormats = System.Windows.DataFormats;
using TextBox = System.Windows.Controls.TextBox;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

internal static class InlineSearchAsciiInput
{
    internal static bool IsEnabled => PluginSettingsService.GetSetting(
        "Lertaro.Plugins.CoreExtensions", "InlineSearchDisableChineseInput", false);

    internal static bool IsAllowed(string text) => text.All(c => c is >= ' ' and <= '~');

    // Attach before the window's query handler, so rejected edits never become searches.
    internal static void Attach(TextBox textBox)
    {
        InputMethod.SetIsInputMethodEnabled(textBox, false);
        textBox.PreviewTextInput += (_, e) =>
        {
            if (!IsAllowed(e.Text)) e.Handled = true;
        };
        textBox.PreviewDragOver += HandleDrop;
        textBox.PreviewDrop += HandleDrop;
        DataObject.RemovePastingHandler(textBox, SearchBoxPasteHandler.Handle);
        DataObject.AddPastingHandler(textBox, HandlePaste);

        var acceptedText = textBox.Text;
        var selectionStart = textBox.SelectionStart;
        var selectionLength = textBox.SelectionLength;
        textBox.SelectionChanged += (_, _) =>
        {
            if (textBox.Text != acceptedText) return;
            selectionStart = textBox.SelectionStart;
            selectionLength = textBox.SelectionLength;
        };
        textBox.TextChanged += (_, _) =>
        {
            if (IsAllowed(textBox.Text))
            {
                acceptedText = textBox.Text;
                return;
            }

            // Covers drop, automation, completion and programmatic edits, including undo/redo.
            var start = selectionStart;
            var length = selectionLength;
            textBox.SetCurrentValue(TextBox.TextProperty, acceptedText);
            textBox.Select(start, length);
        };
    }

    private static void HandleDrop(object sender, System.Windows.DragEventArgs e)
    {
        var text = e.Data.GetData(DataFormats.UnicodeText) as string
            ?? e.Data.GetData(DataFormats.Text) as string;
        if (text != null && IsAllowed(text)) return;
        e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    internal static void HandlePaste(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        var text = e.DataObject.GetData(DataFormats.UnicodeText) as string
            ?? e.DataObject.GetData(DataFormats.Text) as string;
        e.CancelCommand();
        if (text == null || text.Any(c => !(c is >= ' ' and <= '~' or '\r' or '\n')))
            return;

        // Keep the shared search syntax for multi-line pastes; reject a mixed-language paste whole.
        text = SearchTextPasteFormatter.FormatForSearch(text)!;
        if (!IsAllowed(text)) return;
        var insertAt = textBox.SelectionStart;
        textBox.SelectedText = text;
        textBox.Select(insertAt + text.Length, 0);
    }
}
