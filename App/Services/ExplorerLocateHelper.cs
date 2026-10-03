using System.IO;
using Lertaro.Core;
using Lertaro.PluginSdk.Helpers;
using MessageBox = Lertaro.App.Views.Controls.Dialogs.CustomMessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace Lertaro.App.Services;

// Locating always selects the item in its parent. Custom folder-open commands cannot promise selection.
internal static class ExplorerLocateHelper
{
    // ponytail: QTTabBar reuses the active Shell view across tabs. Serialize locate requests until
    // selection finishes; an API that identifies each extension-owned tab would remove this limit.
    private static readonly SemaphoreSlim LocateGate = new(1, 1);

    /// <summary>
    /// Opens the folder holding <paramref name="path"/> with that item selected. Returns immediately;
    /// the shell work runs on a ShellThread, which is where the reasoning for that lives.
    /// </summary>
    public static void LocateInExplorer(string path) =>
        ShellThread.Run("ExplorerLocate", () => LocateInExplorerCore(path));

    private static void LocateInExplorerCore(string path)
    {
        LocateGate.Wait();
        try
        {
            path = Path.TrimEndingDirectorySeparator(UserPathResolver.Expand(path));
            var fileManager = UserSettings.Load().DefaultFileManager;

            if (fileManager.OpenFoldersInNewExplorerTabs && FileExecutor.TryLocateInNewExplorerTab(path, () => TryRevealAndActivate(path)))
                return;

            RevealWithShell(path);
        }
        finally
        {
            LocateGate.Release();
        }
    }

    // Trim a folder's trailing separator so its parent is returned, just as for a file.
    internal static string? ResolveContainingFolder(string path)
        => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));

    /// <summary>
    /// Selects <paramref name="path"/> inside an Explorer window that is already open.
    /// </summary>
    /// <returns><see langword="false"/> when no open window could be driven, so the caller falls back.</returns>
    public static bool TryLocateInExistingExplorer(string path, IntPtr explorerHwnd)
    {
        if (explorerHwnd == IntPtr.Zero) return false;
        path = Path.TrimEndingDirectorySeparator(UserPathResolver.Expand(path));
        var fileManager = UserSettings.Load().DefaultFileManager;
        if (fileManager.OpenFoldersInNewExplorerTabs)
            return FileExecutor.TryLocateInNewExplorerTab(path, preferredExplorerWindow: explorerHwnd);

        // The parent, whatever the item is. Navigating to the item itself when it happened to be a
        // folder made "open containing folder" step INTO that folder and select nothing -- which is
        // just what "open" does, and not what the action says. A drive root has no parent to show,
        // so it falls through to LocateInExplorer rather than pretending it worked.
        var targetFolder = ResolveContainingFolder(path);
        if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
        {
            return false;
        }

        object? window = null;
        try
        {
            // A zero tab handle -- an Explorer window without tabs -- means "this window's only view".
            window = ExplorerShellWindowsHelper.FindShellWindowForTab(ExplorerShellWindowsHelper.GetActiveTabHandle(explorerHwnd), explorerHwnd);
            if (window == null) return false;

            // Folders get selected too, for the same reason: the gate used to be File.Exists, so a
            // located folder was never highlighted once the window arrived.
            return ExplorerShellWindowsHelper.NavigateAndSelect(window, targetFolder, Path.GetFileName(path));
        }

        catch (Exception ex)
        {
            Logger.Log($"[FileExecutor] Locate in existing explorer failed for '{path}': {ex.Message}", LogLevel.Error);
            return false;
        }
        finally
        {
            ExplorerShellWindowsHelper.ReleaseComObject(window);
        }
    }

    private static void RevealWithShell(string path)
    {
        // Open the parent normally so shell extensions such as QTTabBar can put it in an existing
        // window's new tab. Then select in that view without navigating it again. Direct /select
        // bypasses that folder-open interception and can force a separate Explorer window.
        var parent = ResolveContainingFolder(path);
        if (!string.IsNullOrWhiteSpace(parent) && (File.Exists(path) || Directory.Exists(path))
            && ShellOpenHelper.TryOpenFolder(parent)
            && ExplorerShellWindowsHelper.TrySelectInOpenedFolder(parent, Path.GetFileName(path)))
            return;

        if (TryRevealAndActivate(path)) return;

        Logger.Log($"[FileExecutor] Locate failed for '{path}': the shell could not select the item in its parent folder.", LogLevel.Error);
        MessageBox.Show(string.Format(TranslationManager.Instance["Executor_LocateFailed"], path), TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static bool TryRevealAndActivate(string path)
    {
        if (!ShellOpenHelper.TryRevealInFolder(path)) return false;
        var parent = ResolveContainingFolder(path);
        // /select is also asynchronous. Once its view appears, select and activate the owning window.
        if (!string.IsNullOrWhiteSpace(parent))
            ExplorerShellWindowsHelper.TrySelectInOpenedFolder(parent, Path.GetFileName(path));
        return true;
    }
}
