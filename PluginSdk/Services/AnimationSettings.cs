using System.ComponentModel;

namespace Lertaro.PluginSdk.Services;

/// <summary>Shared motion policy for host and plugin UI. Media playback is not UI motion.</summary>
public sealed class AnimationSettings : INotifyPropertyChanged
{
    public static AnimationSettings Instance { get; } = new();
    public bool Enabled { get; private set; }
    public bool Transitions { get; private set; }
    public bool SmoothScrolling { get; private set; }
    public bool Backgrounds { get; private set; }
    public bool Marquee { get; private set; }
    public bool SystemAllowsAnimations { get; private set; } = true;
    public bool HasVisibleWindows { get; private set; }
    public System.Windows.Controls.Primitives.PopupAnimation PopupAnimation => Transitions
        ? System.Windows.Controls.Primitives.PopupAnimation.Fade
        : System.Windows.Controls.Primitives.PopupAnimation.None;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>VisualBrush visuals are detached from the window tree; the host supplies visibility.</summary>
    public void SetWindowVisibility(bool visible)
    {
        if (HasVisibleWindows == visible) return;
        HasVisibleWindows = visible;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasVisibleWindows)));
    }

    /// <summary>The host updates this on its UI thread, before creating windows and after edits.</summary>
    public void Update(bool enabled, bool transitions, bool scrolling, bool backgrounds, bool marquee, bool systemAllows)
    {
        var effective = enabled && systemAllows;
        if ((Enabled, Transitions, SmoothScrolling, Backgrounds, Marquee, SystemAllowsAnimations)
            == (effective, effective && transitions, effective && scrolling, effective && backgrounds, effective && marquee, systemAllows))
            return;
        Enabled = effective;
        Transitions = effective && transitions;
        SmoothScrolling = effective && scrolling;
        Backgrounds = effective && backgrounds;
        Marquee = effective && marquee;
        SystemAllowsAnimations = systemAllows;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
