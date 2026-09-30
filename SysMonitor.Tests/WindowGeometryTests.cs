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

/// <summary>
/// The widget's shape is part of how it reads, so its drags scale the whole
/// panel instead of moving one edge. Every rule here is computed from the
/// panel's own ratio, and clamping scales both axes by the same factor, so
/// the shape survives the limits too.
/// </summary>
[TestClass]
public class WidgetRatioResizeTests
{
    private const double Pad = WindowGeometry.ShadowPad * 2;
    private static readonly (double W, double H) Min = AppConfig.MinWidget;
    private static readonly (double W, double H) Max = AppConfig.MaxWidget;
    private static readonly Rect Origin = new(100, 200, 500, 250);
    private const double WorkBottom = 1200;

    private static Rect Drag(Edge edge, double dx, double dy,
                             Rect? origin = null, double? workBottom = null)
    {
        return WindowGeometry.ResizeWidget(origin ?? Origin, edge, new Vector(dx, dy),
                                           Min, Max, workBottom ?? WorkBottom);
    }

    private static double PanelRatio(Rect window)
    {
        Size panel = WindowGeometry.Panel(window.Width, window.Height);
        return panel.Width / panel.Height;
    }

    [TestMethod]
    public void Dragging_any_side_keeps_the_panel_shape()
    {
        double ratio = PanelRatio(Origin);
        foreach (var (edge, dx, dy) in new[]
                 {
                     (Edge.Right, 80.0, 0.0), (Edge.Left, 60.0, 0.0),
                     (Edge.Bottom, 0.0, 40.0), (Edge.Top, 0.0, 30.0),
                 })
        {
            Rect result = Drag(edge, dx, dy);
            Assert.AreEqual(ratio, PanelRatio(result), 0.5 / 100,
                $"{edge} drifted off the panel's shape");
        }
    }

    [TestMethod]
    public void Dragging_a_side_drives_that_axis_and_the_other_follows()
    {
        // The shape that is locked is the panel's -- what the eye sees -- so
        // the follower's expectation comes from the panel's own ratio.
        double panelW = Origin.Width - Pad, panelH = Origin.Height - Pad;

        Rect right = Drag(Edge.Right, 50, 0);
        Assert.AreEqual(Origin.Width + 50, right.Width, 0.5,
            "the dragged edge follows the pointer");
        Assert.AreEqual(Origin.Height + 50 * panelH / panelW, right.Height, 0.5,
            "the other dimension follows the shape");

        Rect bottom = Drag(Edge.Bottom, 0, 25);
        Assert.AreEqual(Origin.Height + 25, bottom.Height, 0.5);
        Assert.AreEqual(Origin.Width + 25 * panelW / panelH, bottom.Width, 0.5);
    }

    [TestMethod]
    public void A_corner_scales_along_the_axis_the_pointer_moved_further()
    {
        double panelW = Origin.Width - Pad, panelH = Origin.Height - Pad;

        Rect xLed = Drag(Edge.Right | Edge.Bottom, 50, 10);
        Assert.AreEqual(Origin.Width + 50, xLed.Width, 0.5);
        Assert.AreEqual(Origin.Height + 50 * panelH / panelW, xLed.Height, 0.5);

        Rect yLed = Drag(Edge.Right | Edge.Bottom, 10, 50);
        Assert.AreEqual(Origin.Height + 50, yLed.Height, 0.5);
        Assert.AreEqual(Origin.Width + 50 * panelW / panelH, yLed.Width, 0.5);
    }

    [TestMethod]
    public void The_sides_opposite_the_drag_stay_exactly_where_they_were()
    {
        Rect right = Drag(Edge.Right, 70, 0);
        Assert.AreEqual(Origin.Left, right.Left);
        Assert.AreEqual(Origin.Top, right.Top);

        Rect left = Drag(Edge.Left, 70, 0);
        Assert.AreEqual(Origin.Right, left.Right);

        Rect top = Drag(Edge.Top, 0, 40);
        Assert.AreEqual(Origin.Bottom, top.Bottom);

        Rect corner = Drag(Edge.Right | Edge.Bottom, 70, 40);
        Assert.AreEqual(Origin.Left, corner.Left);
        Assert.AreEqual(Origin.Top, corner.Top);
    }

    [TestMethod]
    public void A_vertical_drag_widens_around_the_middle()
    {
        Rect bottom = Drag(Edge.Bottom, 0, 50);
        double centreBefore = Origin.Left + Origin.Width / 2;
        double centreAfter = bottom.Left + bottom.Width / 2;
        Assert.AreEqual(centreBefore, centreAfter, 0.5);
    }

    [TestMethod]
    public void Hitting_a_limit_keeps_the_shape_inside_both_axes()
    {
        double ratio = PanelRatio(Origin);
        Rect huge = Drag(Edge.Right, 5000, 0);
        Size panel = WindowGeometry.Panel(huge.Width, huge.Height);
        Assert.IsTrue(panel.Width <= Max.W + 0.5 && panel.Height <= Max.H + 0.5,
            "the clamp let a dimension past the widget's maximum");
        Assert.AreEqual(ratio, PanelRatio(huge), 0.5 / 100,
            "the clamp broke the shape");

        Rect tiny = Drag(Edge.Left, 5000, 0);
        panel = WindowGeometry.Panel(tiny.Width, tiny.Height);
        Assert.IsTrue(panel.Width >= Min.W - 0.5 && panel.Height >= Min.H - 0.5,
            "the clamp let a dimension past the widget's minimum");
        Assert.AreEqual(ratio, PanelRatio(tiny), 0.5 / 100);
    }

    [TestMethod]
    public void A_panel_outside_the_limits_never_goes_negative_or_runaway()
    {
        // A config written by hand could hold a size the widget no longer
        // allows. The drag still ends with a sane window.
        Rect invalid = new(100, 200, 300, 300);      // 276x276 panel, ratio 1
        Rect result = Drag(Edge.Right, 40, 0, invalid);
        Size panel = WindowGeometry.Panel(result.Width, result.Height);

        Assert.IsTrue(panel.Width > 0 && panel.Height > 0);
        Assert.IsTrue(panel.Width <= Max.W + 0.5 && panel.Height <= Max.H + 0.5);
        Assert.AreEqual(1.0, panel.Width / panel.Height, 0.5 / 100,
            "even an invalid start keeps its own shape");
    }

    [TestMethod]
    public void A_horizontal_drag_grows_downwards_while_there_is_room()
    {
        double panelW = Origin.Width - Pad, panelH = Origin.Height - Pad;
        Rect result = Drag(Edge.Right, 60, 0);
        Assert.AreEqual(Origin.Top, result.Top, "there is room below, the top stays");
        Assert.AreEqual(Origin.Height + 60 * panelH / panelW, result.Height, 0.5);
    }

    [TestMethod]
    public void A_horizontal_drag_grows_upwards_at_the_bottom_of_the_screen()
    {
        // The widget sits just above the taskbar: growing down would push it
        // off the work area, so it grows up off the same bottom edge instead.
        double panelW = Origin.Width - Pad, panelH = Origin.Height - Pad;
        double workBottom = Origin.Bottom - WindowGeometry.ShadowPad + 20;
        Rect result = Drag(Edge.Right, 60, 0, workBottom: workBottom);

        Assert.AreEqual(Origin.Bottom, result.Bottom, 0.5,
            "the bottom edge stays put at the screen's bottom");
        Assert.AreEqual(Origin.Height + 60 * panelH / panelW,
                        result.Height, 0.5);
    }
}
