using System.Windows;
using SysMonitor.Model;
using SysMonitor.ViewModels;

namespace SysMonitor.Tests;

/// <summary>
/// Dragging an edge or a corner. Resizing is the part the user reported
/// broken twice, so the arithmetic is pinned rather than trusted.
/// </summary>
[TestClass]
public class WindowGeometryTests
{
    private const double Pad = WindowGeometry.ShadowPad * 2;
    private static readonly Rect Origin = new(100, 200, 500, 400);

    private static Rect Drag(Edge edge, double dx, double dy,
                             WindowGeometry.View view = WindowGeometry.View.Overall)
    {
        (var min, var max) = WindowGeometry.Limits(view);
        return WindowGeometry.Resize(Origin, edge, new Vector(dx, dy), min, max);
    }

    // ------------------------------------------------------------- hit test
    [TestMethod]
    public void Every_edge_is_grabbable()
    {
        Assert.AreEqual(Edge.Left, WindowGeometry.HitTest(new Point(2, 200), 500, 400));
        Assert.AreEqual(Edge.Right, WindowGeometry.HitTest(new Point(498, 200), 500, 400));
        Assert.AreEqual(Edge.Top, WindowGeometry.HitTest(new Point(250, 2), 500, 400));
        Assert.AreEqual(Edge.Bottom, WindowGeometry.HitTest(new Point(250, 398), 500, 400));
    }

    [TestMethod]
    public void Every_corner_claims_both_its_edges()
    {
        Assert.AreEqual(Edge.Left | Edge.Top,
            WindowGeometry.HitTest(new Point(4, 4), 500, 400));
        Assert.AreEqual(Edge.Right | Edge.Top,
            WindowGeometry.HitTest(new Point(496, 4), 500, 400));
        Assert.AreEqual(Edge.Left | Edge.Bottom,
            WindowGeometry.HitTest(new Point(4, 396), 500, 400));
        Assert.AreEqual(Edge.Right | Edge.Bottom,
            WindowGeometry.HitTest(new Point(496, 396), 500, 400));
    }

    [TestMethod]
    public void The_middle_of_the_window_is_for_dragging_not_resizing()
    {
        Assert.AreEqual(Edge.None, WindowGeometry.HitTest(new Point(250, 200), 500, 400));
    }

    // --------------------------------------------------------------- resize
    [TestMethod]
    public void Dragging_the_right_edge_leaves_the_left_where_it_was()
    {
        Rect result = Drag(Edge.Right, 60, 0);
        Assert.AreEqual(Origin.Left, result.Left);
        Assert.AreEqual(Origin.Top, result.Top);
        Assert.AreEqual(560, result.Width);
        Assert.AreEqual(Origin.Height, result.Height);
    }

    [TestMethod]
    public void Dragging_the_left_edge_moves_the_window_and_pins_the_right()
    {
        // This is what "remember where it was" is for: the far side must not
        // creep while the near one is dragged.
        Rect result = Drag(Edge.Left, -60, 0);
        Assert.AreEqual(40, result.Left);
        Assert.AreEqual(560, result.Width);
        Assert.AreEqual(Origin.Right, result.Right);
    }

    [TestMethod]
    public void Dragging_the_top_edge_moves_the_window_and_pins_the_bottom()
    {
        Rect result = Drag(Edge.Top, 0, -50);
        Assert.AreEqual(150, result.Top);
        Assert.AreEqual(450, result.Height);
        Assert.AreEqual(Origin.Bottom, result.Bottom);
    }

    [TestMethod]
    public void A_corner_moves_both_axes_at_once()
    {
        Rect result = Drag(Edge.Left | Edge.Top, -40, -30);
        Assert.AreEqual(60, result.Left);
        Assert.AreEqual(170, result.Top);
        Assert.AreEqual(Origin.Right, result.Right);
        Assert.AreEqual(Origin.Bottom, result.Bottom);
    }

    [TestMethod]
    public void A_left_edge_pushed_past_the_minimum_still_pins_the_right()
    {
        // Clamping is where an incremental implementation drifts: the size
        // stops but the window keeps sliding.
        Rect result = Drag(Edge.Left, 5000, 0);
        Assert.AreEqual(AppConfig.MinOverall.W + Pad, result.Width);
        Assert.AreEqual(Origin.Right, result.Right,
            "the right edge must not move once the width has stopped shrinking");
    }

    [TestMethod]
    public void A_top_edge_pushed_past_the_minimum_still_pins_the_bottom()
    {
        Rect result = Drag(Edge.Top, 0, 5000);
        Assert.AreEqual(AppConfig.MinOverall.H + Pad, result.Height);
        Assert.AreEqual(Origin.Bottom, result.Bottom);
    }

    [TestMethod]
    public void The_full_view_stops_at_its_minimum_rather_than_changing_view()
    {
        // It used to step back to the overall view when dragged below the size
        // its tabs need, which meant a resize could replace what you were
        // looking at. The drag stops instead, and only a button changes view.
        (var min, _) = WindowGeometry.Limits(WindowGeometry.View.Full);
        Assert.AreEqual(AppConfig.MinFull, min);

        Rect result = Drag(Edge.Right | Edge.Bottom, -5000, -5000, WindowGeometry.View.Full);
        Assert.AreEqual(AppConfig.MinFull.W + Pad, result.Width);
        Assert.AreEqual(AppConfig.MinFull.H + Pad, result.Height);
    }

    [TestMethod]
    public void The_full_view_is_measured_by_its_panel_not_its_window()
    {
        // The shadow margin is not part of what the user is sizing.
        Assert.AreEqual(AppConfig.MinFull, WindowGeometry.Limits(WindowGeometry.View.Full).Min);
    }

    [TestMethod]
    public void The_mini_strip_stays_capped_while_the_panel_does_not()
    {
        (var miniMin, var miniMax) = WindowGeometry.Limits(WindowGeometry.View.Widget);
        Assert.AreEqual(AppConfig.MinWidget, miniMin);
        Assert.AreEqual(AppConfig.MaxWidget, miniMax);

        (_, var expandedMax) = WindowGeometry.Limits(WindowGeometry.View.Overall);
        Assert.IsTrue(expandedMax.W >= 3840, $"width cap {expandedMax.W} is below 4K");
        Assert.IsTrue(expandedMax.H >= 2160, $"height cap {expandedMax.H} is below 4K");
    }

    [TestMethod]
    public void The_panel_size_excludes_the_shadow_margin()
    {
        // Named for what it checks. Whether MainWindow then writes this to the
        // config is not something this can reach; it takes a window.
        Size panel = WindowGeometry.Panel(654, 504);
        Assert.AreEqual(654 - Pad, panel.Width);
        Assert.AreEqual(504 - Pad, panel.Height);
    }

    [TestMethod]
    public void A_resize_round_trips_through_the_panel_size()
    {
        // What the config stores has to be what restores the same window.
        (var min, var max) = WindowGeometry.Limits(WindowGeometry.View.Overall);
        Rect window = WindowGeometry.Resize(Origin, Edge.Right | Edge.Bottom,
                                            new Vector(120, 80), min, max);
        Size panel = WindowGeometry.Panel(window.Width, window.Height);

        Assert.AreEqual(window.Width, panel.Width + Pad);
        Assert.AreEqual(window.Height, panel.Height + Pad);
    }
}
