using System.Windows.Input;
using System.Text.Json;
using Lertaro.App.Services.Plugin;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.Helpers;

internal static class SearchFilterShortcutValidation
{
    private static string Text(string key) => TranslationService.Get("CoreExtensions_FilterShortcut_" + key);

    internal static string? FindReservedOwner(string shortcut, HotkeyPageSettings settings, bool includeCustomActions = true)
    {
        if (!WpfUiHelper.TryParseHotkey(shortcut, out var key, out var mods)
            || mods == ModifierKeys.None || key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return Text("Invalid");
        if (HotkeyStringFormat.IsReservedWindowsShortcut(shortcut)
            || mods == ModifierKeys.Alt && key is Key.Tab or Key.F4 or Key.Space or Key.Escape
            || mods == (ModifierKeys.Alt | ModifierKeys.Shift) && key == Key.Tab
            || mods == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.Escape
            || mods == ModifierKeys.Control && key == Key.Escape) return "Windows";
        if (mods == ModifierKeys.Control && key is Key.A or Key.C or Key.X or Key.V or Key.Z or Key.Y
            or Key.Left or Key.Right or Key.Back or Key.Delete
            || mods == (ModifierKeys.Control | ModifierKeys.Shift) && key is Key.Left or Key.Right or Key.Z
            || mods == ModifierKeys.Shift && key is Key.Insert or Key.Delete or Key.Left or Key.Right or Key.Home or Key.End)
            return Text("TextEditing");
        if (!string.IsNullOrEmpty(settings.SelectJumpModifier)
            && mods == WpfUiHelper.GetWpfModifier(settings.SelectJumpModifier)
            && (key is >= Key.D1 and <= Key.D9 || key is >= Key.NumPad1 and <= Key.NumPad9))
            return Text("ResultShortcut");
        foreach (var property in typeof(HotkeyPageSettings).GetProperties())
            if (property.PropertyType == typeof(string) && property.Name.EndsWith("Hotkey")
                && WpfUiHelper.MatchesHotkey(property.GetValue(settings) as string, mods, key))
                return BuiltinName(property.Name);
        foreach (var registration in PluginManager.Instance.Actions)
            if (WpfUiHelper.MatchesHotkey(HotkeyActionTrigger.ResolveEffectiveHotkey(
                    registration.Action, registration.Plugin, settings.PluginActionHotkeys), mods, key))
                return registration.Action.Name;
        if (includeCustomActions)
            foreach (var (hotkey, title) in ReadCustomActions(PluginSettingsService.GetSetting<object?>("Lertaro.Plugins.CustomActions", "Actions", null)))
                if (WpfUiHelper.MatchesHotkey(hotkey, mods, key)) return title;
        return null;
    }

    private static string BuiltinName(string property) => TranslationService.Get(property switch
    {
        "ToggleWindowHotkey" => "Hotkeys_ToggleLabel", "QuickSwitchHotkey" => "Hotkeys_QuickSwitchLabel",
        "NextItemHotkey" => "Hotkeys_SelectNextItem", "PreviousItemHotkey" => "Hotkeys_SelectPreviousItem",
        "ActionsMenuHotkey" => "Hotkeys_SelectActions", "CompleteFromSelectionHotkey" => "Hotkeys_SelectComplete",
        "QuickLookHotkey" => "Hotkeys_SelectQuickLook", "KeywordHistoryPreviousHotkey" => "Hotkeys_KeywordHistoryPrevious",
        "KeywordHistoryNextHotkey" => "Hotkeys_KeywordHistoryNext", "KeywordHistoryDeleteHotkey" => "Hotkeys_KeywordHistoryDelete",
        "OpenFullWindowHotkey" => "Hotkeys_OpenFullWindow", "StayOpenHotkey" => "Hotkeys_StayOpen",
        "QuickPanelHotkey" => "Hotkeys_QuickPanel", "QuickNavigationHotkey" => "Hotkeys_QuickNavHotkey",
        _ => "Hotkeys_GroupGlobal"
    });

    private static List<(string Hotkey, string Title)> ReadCustomActions(object? value)
    {
        var result = new List<(string, string)>();
        var array = JsonSerializer.SerializeToElement(value);
        if (array.ValueKind != JsonValueKind.Array) return result;
        foreach (var row in array.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || row.TryGetProperty("Enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
                continue;
            if (row.TryGetProperty("Hotkey", out var hotkey) && hotkey.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(hotkey.GetString()))
                result.Add((hotkey.GetString()!, row.TryGetProperty("Title", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString()! : "CustomActions"));
        }
        return result;
    }

    internal static string? Validate(IEnumerable<SearchFilterShortcut> filters, Func<string, string?> reservedOwner)
    {
        var keys = new HashSet<(Key, ModifierKeys)>();
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter.Hotkey) && string.IsNullOrWhiteSpace(filter.Keyword)) continue;
            if (string.IsNullOrWhiteSpace(filter.Keyword) || filter.Keyword.Any(c => char.IsWhiteSpace(c) || c is ':' or ',' or '|' or ';')
                || (!string.IsNullOrWhiteSpace(filter.Hotkey) && string.IsNullOrWhiteSpace(filter.Rule)))
                return Text("InvalidFilter");
            if (!keywords.Add(filter.Keyword)) return $"{filter.Keyword}: {Text("DuplicateKeyword")}";
            if (string.IsNullOrWhiteSpace(filter.Hotkey)) continue;
            if (!WpfUiHelper.TryParseHotkey(filter.Hotkey, out var key, out var mods) || mods == ModifierKeys.None)
                return $"{filter.Keyword}: {Text("Invalid")}";
            if (!keys.Add((key, mods))) return $"{filter.Keyword}: {Text("DuplicateHotkey")}";
            if (reservedOwner(filter.Hotkey) is { } owner)
                return $"{filter.Keyword} ({HotkeyStringFormat.ToDisplayText(filter.Hotkey)}): {Text("Conflict")} {owner}";
        }
        return null;
    }

    internal static string? ValidatePending(IEnumerable<PluginInfoViewModel>? plugins, HotkeyPageSettings settings)
    {
        var filters = SearchFilterSession.GetShortcuts();
        var customActions = ReadCustomActions(PluginSettingsService.GetSetting<object?>("Lertaro.Plugins.CustomActions", "Actions", null));
        foreach (var plugin in plugins ?? [])
        {
            if (!plugin.HasPendingConfigEdits) continue;
            foreach (var field in Walk(plugin.ConfigFields))
            {
                if (field.PluginId == "Lertaro.Plugins.CustomActions" && field.SchemaField.Key == "Actions")
                    customActions = ReadCustomActions(field.ArrayItems.Select(row => row.GetValue()).ToList());
                if (field.PluginId != "Lertaro.Plugins.CoreExtensions" || field.SchemaField.Key != "CustomFilters") continue;
                filters.Clear();
                foreach (var row in field.ArrayItems)
                {
                    object? Value(string key) => row.Children.FirstOrDefault(c => c.SchemaField.Key == key)?.Value;
                    if (Value("Enabled") is false) continue;
                    filters.Add(new SearchFilterShortcut((Value("Keyword") as string ?? "").Trim(),
                        Value("Hotkey") as string ?? "", "", Value("Rule") as string ?? ""));
                }
            }
        }
        return Validate(filters, hotkey =>
        {
            var reserved = FindReservedOwner(hotkey, settings, includeCustomActions: false);
            if (reserved != null) return reserved;
            WpfUiHelper.TryParseHotkey(hotkey, out var key, out var mods);
            return customActions.FirstOrDefault(action => WpfUiHelper.MatchesHotkey(action.Hotkey, mods, key)).Title;
        });
    }

    private static IEnumerable<PluginConfigFieldViewModel> Walk(IEnumerable<PluginConfigFieldViewModel> fields)
    {
        foreach (var field in fields)
        {
            yield return field;
            if (field.IsGroup || field.IsObject)
                foreach (var child in Walk(field.Children)) yield return child;
        }
    }
}
