using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Lertaro.Core;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.Helpers.Visuals;

internal static class AnimationPolicy
{
    private static bool _initialized;

    public static void Initialize(UserSettings settings)
    {
        Apply(settings);
        if (_initialized) return;
        _initialized = true;
        SystemParameters.StaticPropertyChanged += SystemChanged;
        AnimationSettings.Instance.PropertyChanged += (_, _) =>
        {
            foreach (Window window in System.Windows.Application.Current.Windows) ApplyWindow(window);
        };
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, args) =>
            {
                if (!ReferenceEquals(sender, args.OriginalSource)) return;
                var window = (Window)sender;
                window.IsVisibleChanged -= WindowVisibilityChanged;
                window.IsVisibleChanged += WindowVisibilityChanged;
                ApplyWindow(window);
                UpdateWindowVisibility();
            }));
        System.Windows.Application.Current.Exit += (_, _) => SystemParameters.StaticPropertyChanged -= SystemChanged;
    }

    private static void WindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => UpdateWindowVisibility();

    private static void UpdateWindowVisibility() => AnimationSettings.Instance.SetWindowVisibility(
        System.Windows.Application.Current.Windows.Cast<Window>().Any(window => window.IsVisible));

    public static void Apply(UserSettings settings)
    {
        AnimationSettings.Instance.Update(settings.EnableAnimations, settings.AnimateTransitions, settings.AnimateScrolling,
            settings.AnimateBackgrounds, settings.AnimateMarquee, SystemParameters.ClientAreaAnimation);
        var resources = System.Windows.Application.Current?.Resources;
        if (resources == null) return;
        var enabled = AnimationSettings.Instance.Transitions;
        var none = System.Windows.Controls.Primitives.PopupAnimation.None;
        resources[SystemParameters.ToolTipPopupAnimationKey] = enabled ? SystemParameters.ToolTipPopupAnimation : none;
        resources[SystemParameters.MenuPopupAnimationKey] = enabled ? SystemParameters.MenuPopupAnimation : none;
        resources[SystemParameters.ComboBoxPopupAnimationKey] = enabled ? SystemParameters.ComboBoxPopupAnimation : none;
    }

    private static void SystemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(UserSettings.Load())));
    }

    private static void ApplyWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var disabled = AnimationSettings.Instance.Transitions ? 0 : 1;
        // DWMWA_TRANSITIONS_FORCEDISABLED affects only this application's window.
        _ = DwmSetWindowAttribute(hwnd, 3, ref disabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
