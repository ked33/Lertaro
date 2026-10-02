namespace Lertaro.Core.Hook;

// Pure transition state, kept apart from native events so stale and repeated samples can be tested.
internal sealed class RecentFolderVisitState
{
    public IntPtr Target { get; private set; }
    public long Generation { get; private set; }
    private string? _lastPath;
    private IntPtr _lastTab;

    public void ChangeTarget(IntPtr target, bool reset = false)
    {
        if (Target == target && !reset) return;
        Target = target;
        Generation++;
        _lastPath = null;
        _lastTab = IntPtr.Zero;
    }

    public bool Accept(IntPtr target, long generation, IntPtr tab, string? path)
    {
        if (target == IntPtr.Zero || target != Target || generation != Generation) return false;
        var normalized = RecentFolderPaths.Normalize(path);
        var changed = normalized != null && (tab != _lastTab
            || !string.Equals(_lastPath, normalized, StringComparison.OrdinalIgnoreCase));
        _lastPath = normalized;
        _lastTab = tab;
        return changed;
    }

    internal static bool IsExplorer(string className, string processName) =>
        className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase)
        && processName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
}
