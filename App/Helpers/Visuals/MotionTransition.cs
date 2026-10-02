using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Animation;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.Helpers.Visuals;

internal static class MotionTransition
{
    private static readonly ConditionalWeakTable<UIElement, Dictionary<DependencyProperty, (Action Cancel, bool HasCompletion)>> Active = new();

    public static bool HasCompletion(UIElement target, DependencyProperty property) =>
        Active.TryGetValue(target, out var entries) && entries.TryGetValue(property, out var entry) && entry.HasCompletion;

    public static void Cancel(UIElement target, DependencyProperty property)
    {
        if (Active.TryGetValue(target, out var entries) && entries.TryGetValue(property, out var entry)) entry.Cancel();
        target.BeginAnimation(property, null);
    }

    public static void Start(UIElement target, DependencyProperty property, double to, Duration duration,
        IEasingFunction? easing = null, Action? completed = null)
    {
        var from = (double)target.GetValue(property);
        Cancel(target, property);
        target.SetCurrentValue(property, to);
        if (!AnimationSettings.Instance.Transitions || !target.IsVisible || double.IsNaN(from))
        {
            completed?.Invoke();
            return;
        }

        var entries = Active.GetOrCreateValue(target);
        var finished = false;
        void Finish(bool invoke)
        {
            if (finished) return;
            finished = true;
            entries.Remove(property);
            AnimationSettings.Instance.PropertyChanged -= PolicyChanged;
            target.IsVisibleChanged -= VisibilityChanged;
            if (target is FrameworkElement element) element.Unloaded -= Unloaded;
            target.BeginAnimation(property, null);
            // Removing an animation invalidates SetCurrentValue's effective value. Restore the
            // requested state after removal, before a completion callback can inspect/use it.
            if (invoke) target.SetCurrentValue(property, to);
            if (invoke) completed?.Invoke();
        }
        void PolicyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!AnimationSettings.Instance.Transitions) Finish(true);
        }
        void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!target.IsVisible) Finish(true);
        }
        void Unloaded(object sender, RoutedEventArgs e) => Finish(true);

        entries[property] = (() => Finish(false), completed != null);
        AnimationSettings.Instance.PropertyChanged += PolicyChanged;
        target.IsVisibleChanged += VisibilityChanged;
        if (target is FrameworkElement owner) owner.Unloaded += Unloaded;
        var animation = new DoubleAnimation(from, to, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        animation.Completed += (_, _) => Finish(true);
        target.BeginAnimation(property, animation);
    }
}
