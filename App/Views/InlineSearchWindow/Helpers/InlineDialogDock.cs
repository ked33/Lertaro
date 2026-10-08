using DockRect = Lertaro.Core.Hook.ExplorerTracker.RECT;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// Physical pixels throughout. Both sizing and placement use the same outside region; clamping a
// window to the monitor afterwards would silently pull it back over the dialog.
internal readonly record struct InlineDialogDock(
    double Left, double Top, double Right, double Bottom, double WindowWidth, InlineDialogDock.Side Edge)
{
    internal enum Side { Left, Right, Below, Above }

    internal static InlineDialogDock? Find(DockRect host, System.Drawing.Rectangle work,
        double windowWidth, double fullWindowHeight, double minWindowWidth,
        double marginX, double marginY, double gap)
    {
        if (work.Width <= 0 || work.Height <= 0 || host.Right <= host.Left || host.Bottom <= host.Top
            || !double.IsFinite(windowWidth) || !double.IsFinite(fullWindowHeight)) return null;
        var width = Math.Min(work.Width, Math.Max(1, windowWidth - 2 * marginX));
        var height = Math.Max(1, fullWindowHeight - 2 * marginY);
        var minimumWidth = Math.Min(width, Math.Max(1, minWindowWidth - 2 * marginX));
        var left = Math.Clamp(host.Left - gap, work.Left, work.Right);
        var right = Math.Clamp(host.Right + gap, work.Left, work.Right);
        var leftRoom = left - work.Left;
        var rightRoom = work.Right - right;

        // Prefer the roomier side, shrinking only as far as the readable minimum. Ties keep the right.
        // A stable full-card height keeps a different result count from changing the docking side.
        if (height <= work.Height)
        {
            if (leftRoom > rightRoom && leftRoom >= minimumWidth)
                return new(work.Left, work.Top, left, work.Bottom, Math.Min(width, leftRoom) + 2 * marginX, Side.Left);
            if (rightRoom >= minimumWidth)
                return new(right, work.Top, work.Right, work.Bottom, Math.Min(width, rightRoom) + 2 * marginX, Side.Right);
        }

        var below = Math.Clamp(host.Bottom + gap, work.Top, work.Bottom);
        if (work.Bottom - below >= height)
            return new(work.Left, below, work.Right, work.Bottom, width + 2 * marginX, Side.Below);
        var above = Math.Clamp(host.Top - gap, work.Top, work.Bottom);
        if (above - work.Top >= height)
            return new(work.Left, work.Top, work.Right, above, width + 2 * marginX, Side.Above);

        // Maximized/oversized dialogs may leave no usable exterior. The caller retains its inside fallback.
        return null;
    }

    internal (double Left, double Top) Position(DockRect host, double windowHeight, double marginX, double marginY)
    {
        var width = WindowWidth - 2 * marginX;
        var height = Math.Max(0, windowHeight - 2 * marginY);
        var left = Edge switch
        {
            Side.Left => Right - width,
            Side.Right => Left,
            _ => Math.Clamp((host.Left + (double)host.Right - width) / 2, Left, Math.Max(Left, Right - width))
        };
        var top = Edge switch
        {
            Side.Below => Top,
            Side.Above => Bottom - height,
            _ => Math.Clamp(host.Top, Top, Math.Max(Top, Bottom - height))
        };
        return (left - marginX, top - marginY);
    }

    internal bool Fits(double windowHeight, double marginY) => windowHeight - 2 * marginY <= Bottom - Top;

    internal (double Left, double Top) Clamp(double left, double top, double windowHeight, double marginX, double marginY) =>
        (Math.Clamp(left, Left - marginX, Math.Max(Left - marginX, Right - WindowWidth + marginX)),
         Math.Clamp(top, Top - marginY, Math.Max(Top - marginY, Bottom - windowHeight + marginY)));
}
