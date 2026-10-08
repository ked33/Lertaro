using Lertaro.Core.Hook;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using System.Runtime.InteropServices;

namespace Lertaro.Core.Tests.Hook;

[TestClass]
public sealed class FileDialogNavigationTrackerTests
{
    private static readonly IntPtr Dialog = (IntPtr)20, Explorer = (IntPtr)10;

    private static FileDialogNavigationTracker Create(Adapter adapter, Func<IntPtr, string?> read,
        Func<IntPtr, uint>? pid = null) => new(pid ?? (_ => 1), (hwnd, process) => hwnd == Dialog && process == 1,
            _ => adapter, read, retryMs: 1);

    [TestMethod]
    public void UnclassifiedFindDialogDoesNotConsumeTheQuickSwitchHotkey()
    {
        var hwnd = CreateWindowEx(0, "#32770", null, unchecked((int)0x80000000),
            0, 0, 100, 100, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.AreNotEqual(IntPtr.Zero, hwnd);
        try
        {
            var edit = CreateWindowEx(0, "Edit", null, 0x40000000,
                0, 0, 10, 10, hwnd, (IntPtr)1152, IntPtr.Zero, IntPtr.Zero);
            Assert.AreNotEqual(IntPtr.Zero, edit);
            Assert.IsFalse(FileDialogNavigationNative.MayBeDialog(hwnd, IntPtr.Zero, false),
                "Find dialogs can reuse the filename control ID without being file dialogs.");
            var combo = CreateWindowEx(0, "ComboBox", null, 0x40000000,
                0, 0, 10, 10, hwnd, (IntPtr)1136, IntPtr.Zero, IntPtr.Zero);
            Assert.AreNotEqual(IntPtr.Zero, combo);
            Assert.IsTrue(FileDialogNavigationNative.MayBeDialog(hwnd, IntPtr.Zero, false));
        }
        finally { DestroyWindow(hwnd); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int extendedStyle, string className, string? title, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);

    [TestMethod]
    public async Task NewExplorerRetriesEmptySamplesWithoutUsingPreviousFolder()
    {
        var targets = new List<string>();
        var adapter = new Adapter { Navigate = (path, _) => { targets.Add(path); return true; } };
        var reads = 0;
        var tracker = Create(adapter, _ => ++reads < 3 ? null : @"D:\New folder");
        tracker.SetLastActiveExplorerPath(@"C:\Old folder");
        tracker.SetSource(Explorer);

        tracker.RequestNavigation(Dialog);
        await tracker.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        CollectionAssert.AreEqual(new[] { @"D:\New folder\" }, targets);
        Assert.AreEqual(3, reads);
        Assert.AreEqual(@"D:\New folder", tracker.LastActiveExplorerPath);
    }

    [TestMethod]
    public async Task FirstOpeningDoesNotNavigateAndFailedAutoJumpRemainsRetryable()
    {
        var succeeds = false;
        var calls = 0;
        var adapter = new Adapter { Navigate = (_, _) => { calls++; return succeeds; } };
        var tracker = Create(adapter, _ => @"C:\Target");
        tracker.HandleDialogSeen(Dialog);
        await tracker.Completion;
        Assert.AreEqual(0, calls);

        tracker.SetSource(Explorer);
        tracker.HandleDialogSeen(Dialog);
        await tracker.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(1, calls);

        succeeds = true;
        tracker.SetSource(Explorer, newVisit: true);
        tracker.HandleDialogSeen(Dialog);
        await tracker.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(2, calls);

        tracker.HandleDialogSeen(Dialog);
        await tracker.Completion;
        Assert.AreEqual(2, calls, "Only a completed navigation consumes this source version.");
    }

    [TestMethod]
    public async Task RequestsCoalesceAndAnExplicitSelectionSupersedesPendingAutoNavigation()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var targets = new List<string>();
        var adapter = new Adapter { Navigate = (path, _) => { targets.Add(path); return true; } };
        var tracker = Create(adapter, _ =>
        {
            started.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return @"C:\Old";
        });
        tracker.HandleDialogSeen(Dialog);
        tracker.SetSource(Explorer);
        try
        {
            tracker.HandleDialogSeen(Dialog);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            for (var i = 0; i < 20; i++) tracker.HandleDialogSeen(Dialog);
            tracker.RequestNavigation(Dialog, @"C:\Intermediate\");
            tracker.RequestNavigation(Dialog, @"C:\Selected\");
            tracker.HandleDialogSeen(Dialog); // A focus event cannot overwrite an explicit selection.
        }
        finally { release.Set(); }
        await tracker.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        CollectionAssert.AreEqual(new[] { @"C:\Selected\" }, targets);
    }

    [TestMethod]
    public async Task LateSourceReadCannotOverwriteTheNewlyVisitedWindow()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var targets = new List<string>();
        var adapter = new Adapter { Navigate = (path, _) => { targets.Add(path); return true; } };
        var tracker = Create(adapter, hwnd =>
        {
            if (hwnd != Explorer) return @"D:\Latest";
            started.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return @"C:\Stale";
        });
        tracker.SetSource(Explorer);
        try
        {
            tracker.RequestNavigation(Dialog);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            tracker.SetSource((IntPtr)11);
        }
        finally { release.Set(); }
        await tracker.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        CollectionAssert.AreEqual(new[] { @"D:\Latest\" }, targets);
    }

    [TestMethod]
    public async Task DestroyedSourceIsNotReusedEvenIfAReadReturnedAPath()
    {
        var calls = 0;
        var sourcePid = 1u;
        var adapter = new Adapter { Navigate = (_, _) => { calls++; return true; } };
        var tracker = Create(adapter, _ => { sourcePid = 2; return @"C:\Stale"; },
            hwnd => hwnd == Explorer ? sourcePid : 1);
        tracker.SetSource(Explorer);
        tracker.RequestNavigation(Dialog);
        await tracker.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task DestroyingDialogCancelsAnInFlightNavigationAndResetsFirstOpening()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        var adapter = new Adapter
        {
            Navigate = (_, token) =>
            {
                started.TrySetResult();
                cancelled = token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
                return !cancelled;
            }
        };
        var tracker = Create(adapter, _ => @"C:\Folder");
        tracker.SetSource(Explorer);
        tracker.RequestNavigation(Dialog);
        try { await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { tracker.ForgetWindow(Dialog); }
        await tracker.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(cancelled);
        var restarted = false;
        adapter.Navigate = (_, _) => { restarted = true; return true; };
        tracker.HandleDialogSeen(Dialog);
        await tracker.Completion;
        Assert.IsFalse(restarted, "A recycled HWND must be treated as a new dialog.");
    }

    [TestMethod]
    public async Task MissingSourceStopsAfterFiniteRetriesAndKeepsLastKnownSuggestion()
    {
        var reads = 0;
        var navigated = false;
        var adapter = new Adapter { Navigate = (_, _) => { navigated = true; return true; } };
        var tracker = Create(adapter, _ => { reads++; return null; });
        tracker.SetLastActiveExplorerPath(@"C:\Previous");
        tracker.SetLastActiveExplorerPath(string.Empty);
        tracker.SetSource(Explorer);
        tracker.RequestNavigation(Dialog);
        await tracker.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(8, reads);
        Assert.IsFalse(navigated);
        Assert.AreEqual(@"C:\Previous", tracker.LastActiveExplorerPath);
    }

    private sealed class Adapter : IFileDialogAdapter
    {
        public Func<string, CancellationToken, bool> Navigate { get; set; } = (_, _) => true;
        public string Name => "Navigation test adapter";
        public bool CanHandle(IntPtr hwnd, string className, string processName) => true;
        public string? GetCurrentPath(IntPtr hwnd) => null;
        public bool NavigateTo(IntPtr hwnd, string path) => Navigate(path, CancellationToken.None);
        public bool NavigateTo(IntPtr hwnd, string path, CancellationToken token) => Navigate(path, token);
        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect) { rect = default; return false; }
        public bool RestoreFocus(IntPtr hwnd) => false;
    }
}
