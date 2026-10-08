using Lertaro.App.Views.InlineSearchWindow.Helpers;
using DockRect = Lertaro.Core.Hook.ExplorerTracker.RECT;

namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

[TestClass]
public sealed class InlineDialogDockTests
{
    private static DockRect Box(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };

    [TestMethod]
    [DataRow(1000, 1900, "Left")]
    [DataRow(20, 920, "Right")]
    public void EdgeDialogsUseTheOppositeOutsideEdge(int left, int right, string expected)
    {
        var host = Box(left, 100, right, 1000);
        var dock = InlineDialogDock.Find(host, new(0, 0, 1920, 1040), 600, 320, 200, 12, 12, 4);
        Assert.IsNotNull(dock);
        Assert.AreEqual(expected, dock.Value.Edge.ToString());
        AssertOutsideAndOnScreen(dock.Value, host, new(0, 0, 1920, 1040), 320, 12);
    }

    [TestMethod]
    public void NarrowExteriorShrinksTheCardWithoutPullingItInside()
    {
        var host = Box(300, 50, 1250, 970);
        var dock = InlineDialogDock.Find(host, new(0, 0, 1280, 1000), 640, 320, 200, 12, 12, 4);
        Assert.IsNotNull(dock);
        Assert.AreEqual(InlineDialogDock.Side.Left, dock.Value.Edge);
        Assert.AreEqual(320, dock.Value.WindowWidth);
        AssertOutsideAndOnScreen(dock.Value, host, new(0, 0, 1280, 1000), 320, 12);
    }

    [TestMethod]
    [DataRow(10, 650, "Below")]
    [DataRow(400, 1030, "Above")]
    public void WideDialogsUseVerticalExteriorWhenNeitherSideFits(int top, int bottom, string expected)
    {
        var host = Box(50, top, 1870, bottom);
        var dock = InlineDialogDock.Find(host, new(0, 0, 1920, 1040), 600, 320, 200, 12, 12, 4);
        Assert.IsNotNull(dock);
        Assert.AreEqual(expected, dock.Value.Edge.ToString());
        AssertOutsideAndOnScreen(dock.Value, host, new(0, 0, 1920, 1040), 320, 12);
    }

    [TestMethod]
    public void MaximizedDialogFallsBackInsideOnlyWhenAllExteriorRegionsAreTooSmall()
    {
        Assert.IsNull(InlineDialogDock.Find(Box(0, 0, 1920, 1040), new(0, 0, 1920, 1040),
            600, 320, 200, 12, 12, 4));
        Assert.IsNull(InlineDialogDock.Find(Box(100, 100, 1820, 940), new(0, 0, 1920, 1040),
            600, 320, 200, 12, 12, 4));
    }

    [TestMethod]
    public void NegativeMonitorCoordinatesAndHighDpiKeepTheSameOutsideGeometry()
    {
        var work = new System.Drawing.Rectangle(-2560, -200, 2560, 1400);
        var host = Box(-1400, 0, -50, 1100);
        var dock = InlineDialogDock.Find(host, work, 900, 480, 300, 18, 18, 6);
        Assert.IsNotNull(dock);
        Assert.AreEqual(InlineDialogDock.Side.Left, dock.Value.Edge);
        AssertOutsideAndOnScreen(dock.Value, host, work, 480, 18);
        AssertOutsideAndOnScreen(dock.Value, host, work, 240, 18);
    }

    [TestMethod]
    public void DragOffsetsStayWithinTheChosenExteriorRegion()
    {
        var host = Box(1000, 100, 1900, 1000);
        var dock = InlineDialogDock.Find(host, new(0, 0, 1920, 1040), 600, 320, 200, 12, 12, 4);
        Assert.IsNotNull(dock);
        var (left, top) = dock.Value.Clamp(1500, 1200, 320, 12, 12);
        Assert.IsTrue(left + dock.Value.WindowWidth - 12 <= host.Left);
        Assert.IsTrue(top + 320 - 12 <= 1040);
    }

    [TestMethod]
    public void AnOversizedPathBannerCannotBePlacedAboveTheDialogOnAStaleHeightEstimate()
    {
        var dock = InlineDialogDock.Find(Box(50, 400, 1870, 1030), new(0, 0, 1920, 1040),
            600, 320, 200, 12, 12, 4);
        Assert.IsNotNull(dock);
        Assert.IsTrue(dock.Value.Fits(320, 12));
        Assert.IsFalse(dock.Value.Fits(600, 12));
    }

    private static void AssertOutsideAndOnScreen(InlineDialogDock dock, DockRect host,
        System.Drawing.Rectangle work, double height, double margin)
    {
        var (x, y) = dock.Position(host, height, margin, margin);
        var left = x + margin;
        var top = y + margin;
        var right = x + dock.WindowWidth - margin;
        var bottom = y + height - margin;
        Assert.IsTrue(right <= host.Left || left >= host.Right || bottom <= host.Top || top >= host.Bottom,
            "The visible card must not overlap the dialog.");
        Assert.IsTrue(left >= work.Left && right <= work.Right && top >= work.Top && bottom <= work.Bottom,
            "Keeping the card on-screen must not undo exterior placement.");
    }
}
