using Lertaro.PluginSdk.Services;
using System.Windows.Threading;
using System.Windows;
using Lertaro.Core;

using Lertaro.App.Services.Plugin;
using Lertaro.App.Helpers.Visuals;
namespace Lertaro.App.Services.Theme;

public class ThemeManager
{
    private static readonly Lazy<ThemeManager> _instance = new(() => new ThemeManager());
    public static ThemeManager Instance => _instance.Value;

    private ResourceDictionary? _activeThemeDictionary;
    private string _currentThemeId = "Light";
    private PluginSdk.Abstractions.ITheme? _activeTheme;

    public string CurrentThemeId => _currentThemeId;
    public ResourceDictionary? ActiveThemeDictionary => _activeThemeDictionary;
    public PluginSdk.Abstractions.ITheme? ActiveTheme => _activeTheme;

    public event Action? ThemeChanged;

    private DispatcherTimer? _transitionTimer;
    private Action? _finishTransition;
    private int _transitionGeneration;

    private ThemeManager()
    {
        PluginSdk.Services.ThemeService.IsDarkThemeFunc = () => _activeTheme?.IsDark ?? false;
        AnimationSettings.Instance.PropertyChanged += (_, _) =>
        {
            if (!AnimationSettings.Instance.Transitions) _finishTransition?.Invoke();
        };
    }

    public IEnumerable<PluginSdk.Abstractions.ITheme> GetAvailableThemes() => PluginManager.Instance.ThemeProviders
            .SelectMany(p => PluginPerformanceMonitor.Measure(p, () => p.GetThemes()?.ToList() ?? new List<PluginSdk.Abstractions.ITheme>()))
            .GroupBy(t => t.Id)
            .Select(g => g.First()); // Avoid duplicates

    public void Initialize(string preferredThemeId) => ApplyTheme(preferredThemeId, saveSettings: false);

    /// <summary>Starts watching the OS light/dark setting and re-applies the user's configured
    /// light/dark theme pair whenever it flips, but only while ThemeFollowSystem is actually on --
    /// checked fresh off UserSettings each time rather than tracked locally, so toggling the setting
    /// off doesn't need a matching unsubscribe.</summary>
    public void InitializeSystemFollow()
    {
        SystemThemeWatcher.EnsureWatching();
        SystemThemeWatcher.SystemThemeChanged += () =>
        {
            // SystemEvents raises this on a non-UI thread. ApplyTheme touches WPF windows/dictionaries,
            // so marshal the whole handler body onto the Dispatcher before doing any theme work.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                var settings = UserSettings.Load();
                if (!settings.ThemeFollowSystem) return;
                ApplyTheme(ResolveLightDarkThemeId(SystemThemeWatcher.IsSystemLight, settings), saveSettings: false);
            }));
        };
    }

    /// <summary>Resolves the "follow system" light/dark theme pair's configured Id to one that
    /// actually exists (and is still the right light/dark flavor) among currently loaded theme
    /// providers. Themes come entirely from plugins (including the built-ins), so a hardcoded
    /// "Light"/"Dark" fallback isn't safe -- if the configured Id is unset, its provider got
    /// uninstalled/disabled, or it no longer matches the requested flavor, fall back to whatever
    /// theme of that flavor happens to be first in the available list instead.</summary>
    public string ResolveLightDarkThemeId(bool wantLight, UserSettings? settings = null)
    {
        settings ??= UserSettings.Load();
        var configured = wantLight ? settings.LightThemeId : settings.DarkThemeId;
        var themes = GetAvailableThemes().Where(t => t.IsDark != wantLight).ToList();
        if (!string.IsNullOrEmpty(configured) && themes.Any(t => string.Equals(t.Id, configured, StringComparison.OrdinalIgnoreCase)))
        {
            return configured;
        }
        return themes.FirstOrDefault()?.Id ?? "Light";
    }

    public bool ApplyTheme(string themeId, bool saveSettings = true)
    {
        var themes = GetAvailableThemes().ToList();
        // Fallback to Light if not found
        var theme = themes.FirstOrDefault(t => string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase)) ?? themes.FirstOrDefault(t => string.Equals(t.Id, "Light", StringComparison.OrdinalIgnoreCase));
        if (theme == null)
        {
            Logger.Log($"[ThemeManager] No themes found, failed to apply theme '{themeId}'", LogLevel.Error);
            return false;
        }

        _currentThemeId = theme.Id;
        _activeTheme = theme;

        try
        {
            var newDict = theme.GetResources();

            _transitionTimer?.Stop();
            _transitionTimer = null;
            _finishTransition = null;
            var generation = ++_transitionGeneration;
            var animate = _activeThemeDictionary != null && AnimationSettings.Instance.Transitions;

            void ApplyResources()
            {
                if (generation != _transitionGeneration) return;
                _transitionTimer?.Stop();
                _transitionTimer = null;
                _finishTransition = null;
                var resources = System.Windows.Application.Current.Resources;
                if (_activeThemeDictionary != null)
                {
                    SetBackgroundActive(_activeThemeDictionary, false);
                    resources.MergedDictionaries.Remove(_activeThemeDictionary);
                }
                resources.MergedDictionaries.Add(newDict);
                _activeThemeDictionary = newDict;
                SetBackgroundActive(newDict, true);
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    WindowEffectHelper.ApplyThemeEffects(window, theme);
                    if (window.Content is UIElement content && !MotionTransition.HasCompletion(content, UIElement.OpacityProperty))
                    {
                        if (animate)
                            MotionTransition.Start(content, UIElement.OpacityProperty, theme.WindowOpacity, TimeSpan.FromMilliseconds(180));
                        else
                        {
                            MotionTransition.Cancel(content, UIElement.OpacityProperty);
                            content.SetCurrentValue(UIElement.OpacityProperty, theme.WindowOpacity);
                        }
                    }
                }
                ThemeChanged?.Invoke();
            }

            if (!animate) ApplyResources();
            else
            {
                foreach (Window window in System.Windows.Application.Current.Windows)
                    if (window.Content is UIElement content && !MotionTransition.HasCompletion(content, UIElement.OpacityProperty))
                        MotionTransition.Start(content, UIElement.OpacityProperty, 0.1, TimeSpan.FromMilliseconds(120));
                _finishTransition = ApplyResources;
                _transitionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                _transitionTimer.Tick += (_, _) => ApplyResources();
                _transitionTimer.Start();
            }

            Logger.Log($"[ThemeManager] Theme applied successfully: '{theme.DisplayName}' (Dark: {theme.IsDark})");

            if (saveSettings)
            {
                var settings = UserSettings.Load();
                settings.Theme = _currentThemeId;
                settings.Save();
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ThemeManager] Error applying theme '{themeId}': {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    private static void SetBackgroundActive(ResourceDictionary resources, bool active)
    {
        if (resources["ContentBg"] is System.Windows.Media.VisualBrush { Visual: { } visual })
            Motion.SetIsBackgroundActive(visual, active);
    }
}
