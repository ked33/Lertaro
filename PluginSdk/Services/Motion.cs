using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Lertaro.PluginSdk.Services;

/// <summary>State-driven opacity and visible-only loops used by host and theme templates.</summary>
public static class Motion
{
    public static readonly DependencyProperty OpacityProperty = DependencyProperty.RegisterAttached(
        "Opacity", typeof(double), typeof(Motion), new PropertyMetadata(double.NaN, Changed));
    public static double GetOpacity(DependencyObject obj) => (double)obj.GetValue(OpacityProperty);
    public static void SetOpacity(DependencyObject obj, double value) => obj.SetValue(OpacityProperty, value);

    public static readonly DependencyProperty LoopProperty = DependencyProperty.RegisterAttached(
        "Loop", typeof(Storyboard), typeof(Motion), new PropertyMetadata(null, Changed));
    public static Storyboard? GetLoop(DependencyObject obj) => (Storyboard?)obj.GetValue(LoopProperty);
    public static void SetLoop(DependencyObject obj, Storyboard? value) => obj.SetValue(LoopProperty, value);

    public static readonly DependencyProperty IsBackgroundProperty = DependencyProperty.RegisterAttached(
        "IsBackground", typeof(bool), typeof(Motion), new PropertyMetadata(false, Changed));
    public static bool GetIsBackground(DependencyObject obj) => (bool)obj.GetValue(IsBackgroundProperty);
    public static void SetIsBackground(DependencyObject obj, bool value) => obj.SetValue(IsBackgroundProperty, value);

    public static readonly DependencyProperty IsBackgroundActiveProperty = DependencyProperty.RegisterAttached(
        "IsBackgroundActive", typeof(bool), typeof(Motion),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, Changed));
    public static bool GetIsBackgroundActive(DependencyObject obj) => (bool)obj.GetValue(IsBackgroundActiveProperty);
    public static void SetIsBackgroundActive(DependencyObject obj, bool value) => obj.SetValue(IsBackgroundActiveProperty, value);

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(Motion));

    private static void Changed(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        if (obj is not FrameworkElement element) return;
        if (args.Property == IsBackgroundActiveProperty && element.GetValue(StateProperty) == null) return;
        if (element.GetValue(StateProperty) is not State state)
        {
            state = new State(element);
            element.SetValue(StateProperty, state);
        }
        state.Apply(args.Property == OpacityProperty);
    }

    private sealed class State
    {
        private readonly FrameworkElement _element;
        private Storyboard? _running;
        private bool _subscribed;
        private int _opacityVersion;

        public State(FrameworkElement element)
        {
            _element = element;
            element.Loaded += (_, _) => { Subscribe(); Apply(false); };
            element.Unloaded += (_, _) => { Unsubscribe(); Stop(); };
            element.IsVisibleChanged += (_, _) => Apply(false);
            if (element.IsLoaded) Subscribe();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            PropertyChangedEventManager.AddHandler(AnimationSettings.Instance, PolicyChanged, string.Empty);
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            PropertyChangedEventManager.RemoveHandler(AnimationSettings.Instance, PolicyChanged, string.Empty);
        }

        private void PolicyChanged(object? sender, PropertyChangedEventArgs e) => Apply(false);

        private void Stop()
        {
            if (_running != null)
            {
                _running.Remove(_element);
                _running = null;
                // Storyboard.Remove is deferred until a timing tick. A detached VisualBrush may
                // have no next frame, so clear the target clocks of our transform loops as well.
                ClearTransformClocks(_element.RenderTransform);
            }
            _opacityVersion++;
            RestoreOpacity();
        }

        private void RestoreOpacity()
        {
            _element.BeginAnimation(UIElement.OpacityProperty, null);
            var opacity = GetOpacity(_element);
            if (!double.IsNaN(opacity)) _element.SetCurrentValue(UIElement.OpacityProperty, opacity);
        }

        private static void ClearTransformClocks(Transform transform)
        {
            if (transform.IsFrozen) return;
            switch (transform)
            {
                case TranslateTransform translation:
                    translation.BeginAnimation(TranslateTransform.XProperty, null);
                    translation.BeginAnimation(TranslateTransform.YProperty, null);
                    break;
                case RotateTransform rotation:
                    rotation.BeginAnimation(RotateTransform.AngleProperty, null);
                    break;
                case TransformGroup group:
                    foreach (var child in group.Children) ClearTransformClocks(child);
                    break;
            }
        }

        public void Apply(bool animateOpacity)
        {
            var background = GetIsBackground(_element);
            // ponytail: theme VisualBrush visuals are detached and shared across windows. Run while
            // any app window is visible; per-window theme instances would allow finer visibility.
            if (background) Subscribe();
            else if (!_element.IsLoaded) Unsubscribe();
            var from = _element.Opacity;
            Stop();
            var opacity = GetOpacity(_element);
            if (!double.IsNaN(opacity))
            {
                _element.SetCurrentValue(UIElement.OpacityProperty, opacity);
                if (animateOpacity && _element.IsLoaded && _element.IsVisible && AnimationSettings.Instance.Transitions)
                {
                    var fade = new DoubleAnimation(from, opacity, TimeSpan.FromMilliseconds(150)) { FillBehavior = FillBehavior.Stop };
                    var version = _opacityVersion;
                    fade.Completed += (_, _) =>
                    {
                        if (version == _opacityVersion) RestoreOpacity();
                    };
                    _element.BeginAnimation(UIElement.OpacityProperty, fade);
                }
            }
            if ((background
                    ? GetIsBackgroundActive(_element) && AnimationSettings.Instance.Backgrounds && AnimationSettings.Instance.HasVisibleWindows
                    : _element.IsLoaded && _element.IsVisible && AnimationSettings.Instance.Enabled)
                && GetLoop(_element) is { } loop)
            {
                _running = loop;
                loop.Begin(_element, true);
            }
        }
    }
}
