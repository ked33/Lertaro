using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.CoreExtensions.Actions;

public class OpenPowerShellAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_GroupName_Cmd");

    public string DisplayName => TranslationService.Get("Action_OpenPowerShell");

    public string Description => TranslationService.Get("Action_OpenPowerShell_Desc");

    public IReadOnlyList<string> Keywords => new[] { "pwsh" };

    public bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => windowType == SearchWindowType.Inline;

    public bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => results.Count == 1 && results[0].IsDir;

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M3 5h18v14H3V5zm2 2v10h14V7H5zm2 2 3 3-3 3V9zm5 6h5v-2h-5v2z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count == 1 && !string.IsNullOrWhiteSpace(results[0].ContextDirectory) && Directory.Exists(results[0].ContextDirectory);

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view) => Process.Start(TerminalLauncher.CreateStartInfo(results[0].FullPath, results[0].ContextDirectory, windowsTerminal: false, runAsAdmin: false));
}

public class OpenAdminPowerShellAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_GroupName_Cmd");

    public string DisplayName => TranslationService.Get("Action_OpenAdminPowerShell");

    public string Description => TranslationService.Get("Action_OpenAdminPowerShell_Desc");

    public IReadOnlyList<string> Keywords => new[] { "pwsha" };

    public bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => windowType == SearchWindowType.Inline;

    public bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => results.Count == 1 && results[0].IsDir;

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M3 5h18v14H3V5zm2 2v10h14V7H5zm2 2 3 3-3 3V9zm5 6h5v-2h-5v2z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count == 1 && !string.IsNullOrWhiteSpace(results[0].ContextDirectory) && Directory.Exists(results[0].ContextDirectory);

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view) => Process.Start(TerminalLauncher.CreateStartInfo(results[0].FullPath, results[0].ContextDirectory, windowsTerminal: false, runAsAdmin: true));
}

public class OpenWindowsTerminalAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_GroupName_Cmd");

    public string DisplayName => TranslationService.Get("Action_OpenWindowsTerminal");

    public string Description => TranslationService.Get("Action_OpenWindowsTerminal_Desc");

    public IReadOnlyList<string> Keywords => new[] { "wt" };

    public bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => windowType == SearchWindowType.Inline;

    public bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => results.Count == 1 && results[0].IsDir;

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M3 5h18v14H3V5zm2 2v10h14V7H5zm2 2 3 3-3 3V9zm5 6h5v-2h-5v2z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count == 1 && !string.IsNullOrWhiteSpace(results[0].ContextDirectory) && Directory.Exists(results[0].ContextDirectory);

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view) => Process.Start(TerminalLauncher.CreateStartInfo(results[0].FullPath, results[0].ContextDirectory, windowsTerminal: true, runAsAdmin: false));
}

public class OpenAdminWindowsTerminalAction : ISearchResultAction
{
    public string GroupName => TranslationService.Get("Action_GroupName_Cmd");

    public string DisplayName => TranslationService.Get("Action_OpenAdminWindowsTerminal");

    public string Description => TranslationService.Get("Action_OpenAdminWindowsTerminal_Desc");

    public IReadOnlyList<string> Keywords => new[] { "wta" };

    public bool IsVisibleInSearch(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => windowType == SearchWindowType.Inline;

    public bool IsVisibleInMenu(IReadOnlyList<ISearchResult> results, SearchWindowType windowType) => results.Count == 1 && results[0].IsDir;

    public ImageSource? Icon => VectorIconHelper.CreateVectorIcon(
        "M3 5h18v14H3V5zm2 2v10h14V7H5zm2 2 3 3-3 3V9zm5 6h5v-2h-5v2z",
        "TextPrimary");

    public bool CanExecute(IReadOnlyList<ISearchResult> results) => results.Count == 1 && !string.IsNullOrWhiteSpace(results[0].ContextDirectory) && Directory.Exists(results[0].ContextDirectory);

    public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view) => Process.Start(TerminalLauncher.CreateStartInfo(results[0].FullPath, results[0].ContextDirectory, windowsTerminal: true, runAsAdmin: true));
}

internal static class TerminalLauncher
{
    internal static ProcessStartInfo CreateStartInfo(string pathText, string contextDirectory, bool windowsTerminal, bool runAsAdmin)
    {
        // Resolve relative paths against the inline directory, never the application's working directory.
        var path = pathText.Trim().Trim('"');
        var directory = string.IsNullOrWhiteSpace(path)
            ? Path.GetFullPath(contextDirectory)
            : Path.GetFullPath(path, Path.GetFullPath(contextDirectory));
        if (File.Exists(directory))
            directory = Path.GetDirectoryName(directory)!;
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Terminal directory does not exist: {directory}");

        var startInfo = new ProcessStartInfo
        {
            FileName = windowsTerminal ? "wt.exe" : "pwsh.exe",
            WorkingDirectory = directory,
            UseShellExecute = true,
            Verb = runAsAdmin ? "runas" : string.Empty
        };

        // Native arguments preserve the directory through elevation without evaluating it as shell code.
        if (windowsTerminal)
        {
            startInfo.ArgumentList.Add("-w");
            startInfo.ArgumentList.Add("new");
            startInfo.ArgumentList.Add("-d");
        }
        else
        {
            startInfo.ArgumentList.Add("-WorkingDirectory");
        }
        // Windows Terminal parses semicolons even inside a single argv entry.
        startInfo.ArgumentList.Add(windowsTerminal ? directory.Replace(";", @"\;") : directory);
        return startInfo;
    }
}
