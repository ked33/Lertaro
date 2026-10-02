using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Lertaro.PluginSdk;
using Lertaro.Plugins.FolderCascader.Navigation;

namespace Lertaro.Plugins.FolderCascader.Tests.Navigation;

[TestClass]
[DoNotParallelize]
public sealed class IconBitmapCacheTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);
    private readonly ConcurrentQueue<string> _warnings = new();
    private Action<string, LogLevel>? _previousLogger;

    [TestInitialize]
    public void CaptureWarnings()
    {
        _previousLogger = Logger.LogAction;
        Logger.LogAction = (message, level) =>
        {
            if (level == LogLevel.Warn) _warnings.Enqueue(message);
        };
    }

    [TestCleanup]
    public void RestoreLogger() => Logger.LogAction = _previousLogger;

    [TestMethod]
    [DataRow(ApartmentState.MTA)]
    [DataRow(ApartmentState.STA)]
    public async Task EnsureIcons_WithoutApplication_RendersFallbackIcons(ApartmentState apartment)
    {
        Assert.IsNull(Application.Current);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                IconBitmapCache.EnsureIcons();
                AssertIcons(Color.FromRgb(33, 150, 243));
                finished.SetResult();
            }
            catch (Exception ex) { finished.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(apartment);
        thread.Start();

        await finished.Task.WaitAsync(TestTimeout);
        Assert.IsEmpty(_warnings, string.Join(Environment.NewLine, _warnings));
    }

    [TestMethod]
    public async Task EnsureIcons_WithApplication_RefreshesThemeFromUiAndWorkerWithoutLockInversion()
    {
        // WPF permits only one Application per process, so all themed cases share this lifetime.
        var started = new TaskCompletionSource<Application>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiThread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                started.SetResult(app);
                app.Run();
            }
            catch (Exception ex) { started.TrySetException(ex); }
        }) { IsBackground = true };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        var application = await started.Task.WaitAsync(TestTimeout);
        var dispatcher = application.Dispatcher;
        try
        {
            foreach (var (color, frozen) in new (Color? Color, bool Frozen)[]
                     { (Colors.Crimson, false), (Colors.SeaGreen, true), (null, false) })
            {
                SolidColorBrush? brush = null;
                var expected = color ?? Color.FromRgb(33, 150, 243);
                await dispatcher.InvokeAsync(() =>
                {
                    application.Resources.Remove("AccentBlue");
                    if (color.HasValue)
                    {
                        brush = new SolidColorBrush(color.Value);
                        if (frozen) brush.Freeze();
                        // Application resources automatically freeze plain brushes. A binding keeps
                        // this one mutable, as with a dynamic theme's resource in the real UI.
                        else BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding { Source = color.Value });
                        application.Resources["AccentBlue"] = brush;
                        Assert.AreEqual(frozen, brush.IsFrozen);
                    }
                    IconBitmapCache.EnsureIcons();
                    AssertIcons(expected);
                }).Task.WaitAsync(TestTimeout);

                await Task.Run(IconBitmapCache.EnsureIcons).WaitAsync(TestTimeout);
                AssertIcons(expected);
                await dispatcher.InvokeAsync(() =>
                {
                    if (brush != null) Assert.AreEqual(frozen, brush.IsFrozen, "Shared theme brushes must not be frozen by icon rendering.");
                }).Task.WaitAsync(TestTimeout);
            }

            // Keep the UI busy until the worker queues its dispatcher call, then request icons on
            // the UI too. Taking the icon lock before dispatching would deadlock this exact order.
            var pendingWorker = await dispatcher.InvokeAsync(() =>
            {
                using var queued = new ManualResetEventSlim();
                DispatcherHookEventHandler onPosted = (_, _) => queued.Set();
                dispatcher.Hooks.OperationPosted += onPosted;
                Task worker;
                try
                {
                    worker = Task.Run(IconBitmapCache.EnsureIcons);
                    Assert.IsTrue(queued.Wait(TestTimeout), "Worker did not queue its rendering operation.");
                }
                finally { dispatcher.Hooks.OperationPosted -= onPosted; }
                IconBitmapCache.EnsureIcons();
                return worker;
            }).Task.WaitAsync(TestTimeout);
            await pendingWorker.WaitAsync(TestTimeout);
            AssertIcons(Color.FromRgb(33, 150, 243));
            Assert.IsEmpty(_warnings, string.Join(Environment.NewLine, _warnings));
        }
        finally
        {
            _ = dispatcher.BeginInvoke(new Action(application.Shutdown));
            Assert.IsTrue(uiThread.Join(TestTimeout), "Icon rendering blocked the UI dispatcher.");
        }
    }

    private static void AssertIcons(Color expected)
    {
        IntPtr[] handles = [IconBitmapCache.FavoritesHBitmap, IconBitmapCache.HistoryHBitmap,
            IconBitmapCache.OpenedFoldersHBitmap, IconBitmapCache.CategoryHBitmap, IconBitmapCache.AddHBitmap];
        foreach (var handle in handles)
        {
            Assert.AreNotEqual(IntPtr.Zero, handle);
            using var bitmap = System.Drawing.Image.FromHbitmap(handle);
            Assert.AreEqual(64, bitmap.Width);
            Assert.AreEqual(64, bitmap.Height);
        }
        using var addIcon = System.Drawing.Image.FromHbitmap(IconBitmapCache.AddHBitmap);
        var pixel = addIcon.GetPixel(32, 32);
        Assert.AreEqual(System.Drawing.Color.FromArgb(expected.R, expected.G, expected.B).ToArgb(), pixel.ToArgb());
    }
}
