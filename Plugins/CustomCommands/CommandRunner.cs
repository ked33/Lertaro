using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Lertaro.Plugins.CustomCommands;

// Placeholder resolution shared by both ways a command can run: the keyword-triggered instant answer
// (CustomCommandsInstantProvider, which has typed argument text to substitute in) and the quick
// navigation menu entry (CustomCommandsQuickNavProvider, a plain menu click with no typed args --
// argSuffix is empty there; directory-dependent commands are excluded from that menu).
internal static class CommandRunner
{
    internal const string CurrentDirectoryPlaceholder = "{currentDirectory}";

    internal static bool RequiresCurrentDirectory(CustomCommandsInstantProvider.CommandItem cmd) =>
        cmd.UseCurrentDirectory || (cmd.Parameter?.Contains(CurrentDirectoryPlaceholder, StringComparison.Ordinal) ?? false);

    public static string ResolveParameter(CustomCommandsInstantProvider.CommandItem cmd, string argSuffix, string? currentDirectory = null)
    {
        if (RequiresCurrentDirectory(cmd) && !Directory.Exists(currentDirectory))
            throw new DirectoryNotFoundException("This command requires an existing inline search directory.");
        var resolvedParam = cmd.Parameter ?? "";

        // Parse arguments supporting quotes (e.g., "a b" or 'a b')
        var parsedArgs = new List<string>();
        if (!string.IsNullOrEmpty(argSuffix))
        {
            var inQuotes = false;
            var quoteChar = '\0';
            var currentArg = new System.Text.StringBuilder();

            for (var i = 0; i < argSuffix.Length; i++)
            {
                var c = argSuffix[i];
                if ((c == '"' || c == '\'') && (i == 0 || argSuffix[i - 1] != '\\'))
                {
                    if (inQuotes && c == quoteChar)
                    {
                        inQuotes = false;
                    }
                    else if (!inQuotes)
                    {
                        inQuotes = true;
                        quoteChar = c;
                    }
                    else
                    {
                        currentArg.Append(c);
                    }
                }
                else if (c == ' ' && !inQuotes)
                {
                    if (currentArg.Length > 0)
                    {
                        parsedArgs.Add(currentArg.ToString());
                        currentArg.Clear();
                    }
                }
                else
                {
                    currentArg.Append(c);
                }
            }
            if (currentArg.Length > 0)
            {
                parsedArgs.Add(currentArg.ToString());
            }
        }

        // One pass over the template: substituted paths/input must never become more placeholders.
        // Quote native arguments here; users must not add quotes around placeholders.
        return Regex.Replace(resolvedParam, @"\{currentDirectory\}|%s(\d+)|\{(\d+)\}|%s|\{\}", m =>
        {
            if (m.Value == CurrentDirectoryPlaceholder)
                return ArgQuoting.Quote(Path.GetFullPath(currentDirectory!));
            if (m.Value is "%s" or "{}")
                return string.IsNullOrEmpty(argSuffix) ? string.Empty : ArgQuoting.Quote(argSuffix);

            var digits = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            var value = int.TryParse(digits, out var n) && n >= 1 && n <= parsedArgs.Count
                ? parsedArgs[n - 1]
                : string.Empty;
            return value.Length == 0 ? string.Empty : ArgQuoting.Quote(value);
        });
    }

    // Launches cmd directly via Process.Start -- used by the quick navigation menu entry, which
    // executes through a plain OnExecute delegate rather than the app's generic instant-answer
    // dispatch (that path needs a serialized "cc_exec:{json}"/"runas:path args" string instead, see
    // CustomCommandsInstantProvider.GetInstantResults, since by the time the user activates a result
    // the original CommandItem object is long gone). Mirrors CustomActions.DynamicActionProvider's own
    // RunMulti -- same direct ProcessStartInfo approach, no string round-trip needed here either.
    public static void Run(CustomCommandsInstantProvider.CommandItem cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.Path) || RequiresCurrentDirectory(cmd)) return;

        var resolvedParam = ResolveParameter(cmd, "");
        var psi = new ProcessStartInfo
        {
            FileName = cmd.Path,
            Arguments = resolvedParam,
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(cmd.WorkingDir) && Directory.Exists(cmd.WorkingDir))
            psi.WorkingDirectory = cmd.WorkingDir;
        if (cmd.RunSilently) psi.WindowStyle = ProcessWindowStyle.Hidden;
        if (cmd.RunAsAdmin) psi.Verb = "runas";

        try { Process.Start(psi); }
        catch (Exception ex)
        {
            // A vanished/moved/renamed command target fails silently otherwise -- the user clicks
            // and nothing happens, with no trace. CreateNoWindow is not set here: it has no effect
            // under UseShellExecute (WindowStyle.Hidden is the effective suppression).
            PluginSdk.Logger.Log($"[CustomCommands] Failed to launch '{cmd.Path}': {ex.Message}", PluginSdk.LogLevel.Error);
        }
    }
}
