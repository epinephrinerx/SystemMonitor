using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class WindowGeometryTests
{
    private static readonly RectD WorkArea = new(0, 0, 1920, 1080);

    [TestMethod]
    public void LimitsAndReferenceSizes_MatchSpec()
    {
        // Reference sizes (panel)
        Assert.AreEqual(new SizeD(270, 104), WindowGeometry.ReferenceSize(ViewMode.Widget));
        Assert.AreEqual(new SizeD(630, 480), WindowGeometry.ReferenceSize(ViewMode.Overall));
        Assert.AreEqual(new SizeD(640, 480), WindowGeometry.ReferenceSize(ViewMode.Full));

        // Min panel sizes
        Assert.AreEqual(new SizeD(200, 64), WindowGeometry.MinPanelSize(ViewMode.Widget));
        Assert.AreEqual(new SizeD(470, 300), WindowGeometry.MinPanelSize(ViewMode.Overall));
        Assert.AreEqual(new SizeD(640, 480), WindowGeometry.MinPanelSize(ViewMode.Full));

        // Max panel sizes
        Assert.AreEqual(new SizeD(900, 400), WindowGeometry.MaxPanelSize(ViewMode.Widget));
        Assert.AreEqual(new SizeD(4000, 3000), WindowGeometry.MaxPanelSize(ViewMode.Overall));
        Assert.AreEqual(new SizeD(4000, 3000), WindowGeometry.MaxPanelSize(ViewMode.Full));

        // Shadow pad is 12 DIPs
        Assert.AreEqual(12.0, WindowGeometry.ShadowPad);
        SizeD windowSize = WindowGeometry.WindowSize(new SizeD(270, 104));
        Assert.AreEqual(new SizeD(294, 128), windowSize);
        Assert.AreEqual(new SizeD(270, 104), WindowGeometry.PanelFromWindow(windowSize));
    }

    [TestMethod]
    public void Snap_Within25Pixels_SnapsEdgesToWorkArea()
    {
        // Window is 300x150
        RectD rectNearLeft = new(15, 100, 300, 150);
        RectD snappedLeft = WindowGeometry.Snap(rectNearLeft, WorkArea);
        Assert.AreEqual(0.0, snappedLeft.X);
        Assert.AreEqual(100.0, snappedLeft.Y);

        // Exactly at margin (25 px) -> snaps
        RectD rectAtMargin = new(25, 100, 300, 150);
        Assert.AreEqual(0.0, WindowGeometry.Snap(rectAtMargin, WorkArea).X);

        // Beyond margin (26 px) -> does NOT snap
        RectD rectBeyondMargin = new(26, 100, 300, 150);
        Assert.AreEqual(26.0, WindowGeometry.Snap(rectBeyondMargin, WorkArea).X);

        // Near right edge: Right = 1910 (10 px from 1920) -> snaps right edge to 1920 (X = 1620)
        RectD rectNearRight = new(1610, 100, 300, 150);
        RectD snappedRight = WindowGeometry.Snap(rectNearRight, WorkArea);
        Assert.AreEqual(1620.0, snappedRight.X);
        Assert.AreEqual(1920.0, snappedRight.Right);

        // Near top edge (Y = 12 <= 25) -> snaps to Y = 0
        RectD rectNearTop = new(100, 12, 300, 150);
        Assert.AreEqual(0.0, WindowGeometry.Snap(rectNearTop, WorkArea).Y);

        // Near bottom edge: Bottom = 1065 (15 px from 1080) -> snaps bottom edge to 1080 (Y = 930)
        RectD rectNearBottom = new(100, 915, 300, 150);
        Assert.AreEqual(930.0, WindowGeometry.Snap(rectNearBottom, WorkArea).Y);
    }

    [TestMethod]
    public void KeepOnScreen_PullsWindowInsideWorkArea()
    {
        // Pushed off-screen left (X = -40)
        RectD offLeft = new(-40, 200, 300, 150);
        RectD clampedLeft = WindowGeometry.KeepOnScreen(offLeft, WorkArea);
        Assert.AreEqual(0.0, clampedLeft.X);
        Assert.AreEqual(200.0, clampedLeft.Y);

        // Pushed off-screen top (Y = -30)
        RectD offTop = new(200, -30, 300, 150);
        RectD clampedTop = WindowGeometry.KeepOnScreen(offTop, WorkArea);
        Assert.AreEqual(200.0, clampedTop.X);
        Assert.AreEqual(0.0, clampedTop.Y);

        // Pushed off-screen right (X = 1800, Right = 2100 > 1920)
        RectD offRight = new(1800, 200, 300, 150);
        RectD clampedRight = WindowGeometry.KeepOnScreen(offRight, WorkArea);
        Assert.AreEqual(1620.0, clampedRight.X);
        Assert.AreEqual(1920.0, clampedRight.Right);

        // Pushed off-screen bottom (Y = 1000, Bottom = 1150 > 1080)
        RectD offBottom = new(200, 1000, 300, 150);
        RectD clampedBottom = WindowGeometry.KeepOnScreen(offBottom, WorkArea);
        Assert.AreEqual(930.0, clampedBottom.Y);
        Assert.AreEqual(1080.0, clampedBottom.Bottom);

        // Window larger than work area pins to work area top-left
        RectD oversized = new(50, 50, 2500, 1500);
        RectD clampedOversized = WindowGeometry.KeepOnScreen(oversized, WorkArea);
        Assert.AreEqual(0.0, clampedOversized.X);
        Assert.AreEqual(0.0, clampedOversized.Y);
    }

    [TestMethod]
    public void FirstRunPosition_Insets24PixelsFromBottomRight()
    {
        SizeD windowSize = new(294, 128); // Widget panel 270x104 + pad
        PointD pos = WindowGeometry.FirstRunPosition(WorkArea, windowSize);

        // X = 1920 - 294 - 24 = 1602
        Assert.AreEqual(1602.0, pos.X);
        // Y = 1080 - 128 - 24 = 928
        Assert.AreEqual(928.0, pos.Y);
    }

    [TestMethod]
    public void HitTest_CornersAndEdges_PrioritisesCorners()
    {
        SizeD window = new(300, 200);

        // Corner band is 28 px
        Assert.AreEqual(ResizeEdge.TopLeft, WindowGeometry.HitTest(window, new PointD(10, 10)));
        Assert.AreEqual(ResizeEdge.TopRight, WindowGeometry.HitTest(window, new PointD(290, 10)));
        Assert.AreEqual(ResizeEdge.BottomLeft, WindowGeometry.HitTest(window, new PointD(10, 190)));
        Assert.AreEqual(ResizeEdge.BottomRight, WindowGeometry.HitTest(window, new PointD(290, 190)));

        // Edges (outside 28px corner band, inside 18px edge band)
        Assert.AreEqual(ResizeEdge.Left, WindowGeometry.HitTest(window, new PointD(10, 100)));
        Assert.AreEqual(ResizeEdge.Right, WindowGeometry.HitTest(window, new PointD(290, 100)));
        Assert.AreEqual(ResizeEdge.Top, WindowGeometry.HitTest(window, new PointD(150, 10)));
        Assert.AreEqual(ResizeEdge.Bottom, WindowGeometry.HitTest(window, new PointD(150, 190)));

        // Center / non-edge
        Assert.AreEqual(ResizeEdge.None, WindowGeometry.HitTest(window, new PointD(150, 100)));
    }

    [TestMethod]
    public void ResizeWidget_PreservesAspectRatio()
    {
        // Widget panel 270x104 + 24 pad = 294x128
        RectD start = new(100, 100, 270, 104);
        double initialRatio = start.Width / start.Height;
        SizeD min = new(200, 64);
        SizeD max = new(900, 400);

        // Drag right edge by +54 px
        RectD resizedRight = WindowGeometry.ResizeWidget(
            start, ResizeEdge.Right, dx: 54, dy: 0, WorkArea, min, max);

        Assert.AreEqual(100.0, resizedRight.X, "Left edge should remain pinned");
        Assert.AreEqual(start.Width + 54, resizedRight.Width, 0.01);
        Assert.AreEqual(initialRatio, resizedRight.Width / resizedRight.Height, 0.001);

        // Drag bottom edge by +26 px
        RectD resizedBottom = WindowGeometry.ResizeWidget(
            start, ResizeEdge.Bottom, dx: 0, dy: 26, WorkArea, min, max);

        Assert.AreEqual(start.Height + 26, resizedBottom.Height, 0.01);
        Assert.AreEqual(initialRatio, resizedBottom.Width / resizedBottom.Height, 0.001);

        // Drag corner (BottomRight)
        RectD resizedCorner = WindowGeometry.ResizeWidget(
            start, ResizeEdge.BottomRight, dx: 54, dy: 20, WorkArea, min, max);

        Assert.AreEqual(initialRatio, resizedCorner.Width / resizedCorner.Height, 0.001);
    }

    [TestMethod]
    public void Placement_RemembersPositionBeforeFull_UnlessMovedInFull()
    {
        var placement = new Placement();
        RectD widgetRect = new(500, 300, 294, 128);

        // Switch to Full: remembers widgetRect, returns current top-left
        PointD fullOrigin = placement.ApplySwitch(ViewMode.Widget, ViewMode.Full, widgetRect);
        Assert.AreEqual(widgetRect.TopLeft, fullOrigin);
        Assert.IsTrue(placement.InFull);
        Assert.IsNotNull(placement.BeforeFull);

        // Switch back without moving: restores pre-Full position!
        RectD fullRect = new(100, 50, 664, 504);
        PointD returnPos = placement.ApplySwitch(ViewMode.Full, ViewMode.Widget, fullRect);
        Assert.AreEqual(widgetRect.TopLeft, returnPos);
        Assert.IsFalse(placement.InFull);

        // Now enter Full again and move window while in Full
        placement.ApplySwitch(ViewMode.Widget, ViewMode.Full, widgetRect);
        placement.NoteMoved();
        Assert.IsTrue(placement.MovedWhileFull);

        // Switch back after moving: keeps current position instead of jumping back!
        RectD movedFullRect = new(200, 150, 664, 504);
        PointD movedReturnPos = placement.ApplySwitch(ViewMode.Full, ViewMode.Widget, movedFullRect);
        Assert.AreEqual(movedFullRect.TopLeft, movedReturnPos);
    }

    [TestMethod]
    public void PanelFromWindow_And_WindowFromPanel_RectConversions()
    {
        // Panel at (100, 100) with size 270x104
        RectD panel = new(100, 100, 270, 104);
        RectD window = WindowGeometry.WindowFromPanel(panel);

        // Window adds ShadowPad (12) on all sides: X - 12, Y - 12, W + 24, H + 24
        Assert.AreEqual(88.0, window.X);
        Assert.AreEqual(88.0, window.Y);
        Assert.AreEqual(294.0, window.Width);
        Assert.AreEqual(128.0, window.Height);

        // Converting window back to panel strips ShadowPad (12) on all sides
        RectD roundTripPanel = WindowGeometry.PanelFromWindow(window);
        Assert.AreEqual(panel, roundTripPanel);
    }

    [TestMethod]
    public void ResizeWidget_ThroughWindowPanelConversion_MaintainsPanelAspectRatio()
    {
        // Widget starting with 270x104 panel at (100, 100)
        RectD panelStart = new(100, 100, 270, 104);
        RectD windowStart = WindowGeometry.WindowFromPanel(panelStart);

        Assert.AreEqual(new RectD(88, 88, 294, 128), windowStart);

        // Resize flow matching ShellWindow: convert window to panel, resize widget panel, convert back
        RectD panel = WindowGeometry.PanelFromWindow(windowStart);
        Assert.AreEqual(panelStart, panel);

        // Drag right edge by +54 DIPs on 270x104 panel -> scale = (270 + 54) / 270 = 1.2
        // Panel becomes 324 x 124.8
        SizeD min = WindowGeometry.MinPanelSize(ViewMode.Widget);
        SizeD max = WindowGeometry.MaxPanelSize(ViewMode.Widget);
        RectD resizedPanel = WindowGeometry.ResizeWidget(
            panel, ResizeEdge.Right, dx: 54, dy: 0, WorkArea, min, max);

        Assert.AreEqual(100.0, resizedPanel.X, "Left edge remains pinned");
        Assert.AreEqual(100.0, resizedPanel.Y, "Top edge remains pinned");
        Assert.AreEqual(324.0, resizedPanel.Width, 0.001);
        Assert.AreEqual(124.8, resizedPanel.Height, 0.001);

        // Convert back to window rectangle
        RectD resizedWindow = WindowGeometry.WindowFromPanel(resizedPanel);
        Assert.AreEqual(88.0, resizedWindow.X);
        Assert.AreEqual(88.0, resizedWindow.Y);
        Assert.AreEqual(348.0, resizedWindow.Width, 0.001); // 324 + 24
        Assert.AreEqual(148.8, resizedWindow.Height, 0.001); // 124.8 + 24

        // Round-trip panel matches within floating-point tolerance
        RectD roundTripPanel = WindowGeometry.PanelFromWindow(resizedWindow);
        Assert.AreEqual(resizedPanel.X, roundTripPanel.X, 0.001);
        Assert.AreEqual(resizedPanel.Y, roundTripPanel.Y, 0.001);
        Assert.AreEqual(resizedPanel.Width, roundTripPanel.Width, 0.001);
        Assert.AreEqual(resizedPanel.Height, roundTripPanel.Height, 0.001);

        // Production code path: WindowGeometry.ResizeWidgetWindow takes window rect directly
        RectD directResizedWindow = WindowGeometry.ResizeWidgetWindow(
            new RectD(88, 88, 294, 128), ResizeEdge.Right, dx: 54, dy: 0, WorkArea, min, max);
        Assert.AreEqual(88.0, directResizedWindow.X, 0.001);
        Assert.AreEqual(88.0, directResizedWindow.Y, 0.001);
        Assert.AreEqual(348.0, directResizedWindow.Width, 0.001);
        Assert.AreEqual(148.8, directResizedWindow.Height, 0.001);
    }
}
