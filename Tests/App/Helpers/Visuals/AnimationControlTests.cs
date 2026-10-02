using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lertaro.App.Helpers.Visuals;
using Lertaro.Core;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.Tests.Helpers.Visuals;

[TestClass]
[DoNotParallelize]
public sealed class AnimationControlTests
{
    [TestMethod]
    public void AnimationDefaultsAndJsonRoundTrip()
    {
        var settings = JsonSerializer.Deserialize<UserSettings>("{}");
        Assert.IsNotNull(settings);
        Assert.IsFalse(settings.EnableAnimations);
        Assert.IsTrue(settings.AnimateTransitions);
        Assert.IsFalse(settings.AnimateScrolling || settings.AnimateBackgrounds || settings.AnimateMarquee);
        settings.EnableAnimations = true;
        settings.AnimateTransitions = false;
        settings.AnimateScrolling = true;
        settings.AnimateBackgrounds = true;
        settings.AnimateMarquee = true;
        var restored = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(settings));
        Assert.IsNotNull(restored);
        Assert.IsTrue(restored.EnableAnimations && restored.AnimateScrolling && restored.AnimateBackgrounds && restored.AnimateMarquee);
        Assert.IsFalse(restored.AnimateTransitions);
    }

    [TestMethod]
    public void AnimationPolicyHonorsEveryCombination()
    {
        var policy = new AnimationSettings();
        for (var bits = 0; bits < 64; bits++)
        {
            bool Bit(int bit) => (bits & (1 << bit)) != 0;
            policy.Update(Bit(0), Bit(1), Bit(2), Bit(3), Bit(4), Bit(5));
            var enabled = Bit(0) && Bit(5);
            Assert.AreEqual(enabled, policy.Enabled);
            Assert.AreEqual(enabled && Bit(1), policy.Transitions);
            Assert.AreEqual(enabled && Bit(2), policy.SmoothScrolling);
            Assert.AreEqual(enabled && Bit(3), policy.Backgrounds);
            Assert.AreEqual(enabled && Bit(4), policy.Marquee);
            Assert.AreEqual(Bit(5), policy.SystemAllowsAnimations);
        }
    }

    [TestMethod]
    public void AnimationDisableFinishesOnceAndSupersededCallbackNeverRuns() => Sta(() =>
    {
        var border = new Border { Width = 20, Height = 20 };
        WithWindow(border, () =>
        {
            Enable();
            var stale = 0;
            var current = 0;
            MotionTransition.Start(border, UIElement.OpacityProperty, 0, TimeSpan.FromSeconds(30), completed: () => stale++);
            MotionTransition.Start(border, UIElement.OpacityProperty, 0.7, TimeSpan.FromSeconds(30), completed: () => current++);
            Disable();
            Assert.AreEqual(0, stale);
            Assert.AreEqual(1, current);
            Assert.AreEqual(0.7, border.Opacity);
            Assert.IsFalse(border.HasAnimatedProperties);
            Disable();
            Assert.AreEqual(1, current);
        });
    });

    [TestMethod]
    public void AnimationDisabledTransitionIsImmediate() => Sta(() =>
    {
        Disable();
        var border = new Border();
        var calls = 0;
        MotionTransition.Start(border, FrameworkElement.WidthProperty, 160, TimeSpan.FromSeconds(30), completed: () => calls++);
        Assert.AreEqual(160.0, border.Width);
        Assert.AreEqual(1, calls);
        Assert.IsFalse(border.HasAnimatedProperties);
    });

    [TestMethod]
    public void AnimationFiniteTransitionsKeepFinalValuesOnCompletion() => Sta(() =>
    {
        var border = new Border { Width = 20, Height = 20 };
        WithWindow(border, () =>
        {
            Enable();
            MotionTransition.Start(border, UIElement.OpacityProperty, 0.6, TimeSpan.FromMilliseconds(10));
            for (var i = 0; i < 40 && border.HasAnimatedProperties; i++) Pump();
            Assert.IsFalse(border.HasAnimatedProperties);
            Assert.AreEqual(0.6, border.Opacity);

            Motion.SetOpacity(border, 0.2);
            for (var i = 0; i < 40 && border.HasAnimatedProperties; i++) Pump();
            Assert.IsFalse(border.HasAnimatedProperties);
            Assert.AreEqual(0.2, border.Opacity);
        });
    });

    [TestMethod]
    public void AnimationLoopStopsOnDisableAndHideAndResumes() => Sta(() =>
    {
        var border = new Border { Width = 20, Height = 20, RenderTransform = new RotateTransform() };
        var loop = new Storyboard();
        var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTargetProperty(spin, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        loop.Children.Add(spin);
        Motion.SetLoop(border, loop);
        WithWindow(border, () =>
        {
            Enable();
            Pump();
            Assert.IsTrue(((RotateTransform)border.RenderTransform).HasAnimatedProperties);
            Disable();
            Pump();
            Assert.IsFalse(((RotateTransform)border.RenderTransform).HasAnimatedProperties);
            Enable();
            Pump();
            Assert.IsTrue(((RotateTransform)border.RenderTransform).HasAnimatedProperties);
            border.Visibility = Visibility.Collapsed;
            Pump();
            Assert.IsFalse(((RotateTransform)border.RenderTransform).HasAnimatedProperties);
        });
    });

    [TestMethod]
    public void AnimationDetachedThemeVisualFollowsHostVisibility() => Sta(() =>
    {
        var visual = new Border { RenderTransform = new RotateTransform() };
        var root = new Grid();
        root.Children.Add(visual);
        var brush = new VisualBrush(root);
        Motion.SetIsBackground(visual, true);
        Motion.SetIsBackgroundActive(root, true);
        var loop = new Storyboard();
        var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTargetProperty(spin, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        loop.Children.Add(spin);
        Motion.SetLoop(visual, loop);
        try
        {
            Enable();
            AnimationSettings.Instance.SetWindowVisibility(true);
            Pump();
            Assert.IsTrue(((RotateTransform)visual.RenderTransform).HasAnimatedProperties);
            Motion.SetIsBackgroundActive(root, false);
            Assert.IsFalse(((RotateTransform)visual.RenderTransform).HasAnimatedProperties);
            Pump();
            Assert.IsFalse(((RotateTransform)visual.RenderTransform).HasAnimatedProperties);
            Motion.SetIsBackgroundActive(root, true);
            Pump();
            Assert.IsTrue(((RotateTransform)visual.RenderTransform).HasAnimatedProperties);
            AnimationSettings.Instance.SetWindowVisibility(false);
            Pump();
            Assert.IsFalse(((RotateTransform)visual.RenderTransform).HasAnimatedProperties);
        }
        finally
        {
            Disable();
            AnimationSettings.Instance.SetWindowVisibility(false);
            Motion.SetLoop(visual, null);
            Motion.SetIsBackground(visual, false);
            GC.KeepAlive(brush);
        }
    });

    [TestMethod]
    public void AnimationResultSelectionTemplateKeepsStaticFeedback() => Sta(() =>
    {
        Disable();
        var resources = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Lertaro.App;component/Resources/Styles.xaml")
        };
        var item = new ListBoxItem { Content = "Selection", Style = (Style)resources["ResultItemStyle"] };
        var grid = new Grid { Resources = resources };
        grid.Children.Add(item);
        WithWindow(grid, () =>
        {
            item.ApplyTemplate();
            var overlay = (Border)item.Template.FindName("SelectionOverlay", item);
            Assert.AreEqual(0.0, overlay.Opacity);
            item.IsSelected = true;
            Assert.AreEqual(1.0, overlay.Opacity);
            Assert.IsFalse(overlay.HasAnimatedProperties);
            item.IsSelected = false;
            Assert.AreEqual(0.0, overlay.Opacity);
        });
    });

    private static void Enable() => AnimationSettings.Instance.Update(true, true, true, true, true, true);
    private static void Disable() => AnimationSettings.Instance.Update(false, true, true, true, true, true);

    private static void WithWindow(UIElement content, Action action)
    {
        var window = new Window { Content = content, Width = 100, Height = 100, Left = -10000, ShowActivated = false, ShowInTaskbar = false };
        try { window.Show(); Pump(); action(); }
        finally { window.Close(); Pump(); Disable(); }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { error = e; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
