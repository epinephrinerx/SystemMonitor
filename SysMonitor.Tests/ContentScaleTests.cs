using System.Windows;
using SysMonitor.Model;
using SysMonitor.ViewModels;

namespace SysMonitor.Tests;

/// <summary>
/// The widget's content grows and shrinks with the widget itself, measured
/// against the default widget size, where the content draws exactly as it
/// always has. The scale comes from the panel alone -- the font-size slider
/// multiplies on top of it in the UI, and nothing here reads it.
/// </summary>
[TestClass]
public class ContentScaleTests
{
    private static readonly Size Reference = WindowGeometry.WidgetReference;
    private const double Pad = WindowGeometry.ShadowPad * 2;

    [TestMethod]
    public void The_default_widget_draws_at_scale_one()
    {
        Assert.AreEqual(1.0, WindowGeometry.ContentScale(Reference, Reference), 1e-9);
    }

    [TestMethod]
    public void A_widget_twice_the_reference_draws_twice_as_large()
    {
        Size panel = new(Reference.Width * 2, Reference.Height * 2);
        Assert.AreEqual(2.0, WindowGeometry.ContentScale(panel, Reference), 1e-9);
    }

    [TestMethod]
    public void An_off_ratio_panel_uses_its_scarcer_dimension()
    {
        Size panel = new(Reference.Width * 3, Reference.Height * 2);
        Assert.AreEqual(2.0, WindowGeometry.ContentScale(panel, Reference), 1e-9,
            "the taller dimension would overflow the panel if it led");

        panel = new Size(Reference.Width * 2, Reference.Height * 3);
        Assert.AreEqual(2.0, WindowGeometry.ContentScale(panel, Reference), 1e-9);
    }

    [TestMethod]
    public void The_limits_give_the_smallest_and_largest_scales()
    {
        Size minPanel = new(AppConfig.MinWidget.W, AppConfig.MinWidget.H);
        Assert.AreEqual(Math.Min(minPanel.Width / Reference.Width,
                                 minPanel.Height / Reference.Height),
                        WindowGeometry.ContentScale(minPanel, Reference), 1e-9);

        Size maxPanel = new(AppConfig.MaxWidget.W, AppConfig.MaxWidget.H);
        Assert.AreEqual(Math.Min(maxPanel.Width / Reference.Width,
                                 maxPanel.Height / Reference.Height),
                        WindowGeometry.ContentScale(maxPanel, Reference), 1e-9);
    }

    [TestMethod]
    public void A_degenerate_panel_falls_back_to_unity_rather_than_throwing()
    {
        Assert.AreEqual(1.0, WindowGeometry.ContentScale(new Size(0, 100), Reference));
        Assert.AreEqual(1.0, WindowGeometry.ContentScale(new Size(100, 0), Reference));
    }

    [TestMethod]
    public void The_reference_is_the_configured_default_widget()
    {
        // ResetSize and a fresh install both produce this size; if the
        // default moves, the scale's "1" must move with it.
        var fresh = new AppConfig();
        Assert.AreEqual(fresh.WidgetW, Reference.Width);
        Assert.AreEqual(fresh.WidgetH, Reference.Height);
    }

    [TestMethod]
    public void The_overall_reference_is_the_configured_default_overall()
    {
        var fresh = new AppConfig();
        Assert.AreEqual(fresh.OverallW, WindowGeometry.OverallReference.Width);
        Assert.AreEqual(fresh.OverallH, WindowGeometry.OverallReference.Height);
    }

    [TestMethod]
    public void The_scale_round_trips_through_a_resized_window()
    {
        // A ratio-locked drag to twice the reference window yields exactly
        // twice the scale: config in, scale out.
        Rect origin = new(0, 0, Reference.Width + Pad, Reference.Height + Pad);
        (var min, var max) = WindowGeometry.Limits(WindowGeometry.View.Widget);
        Rect dragged = WindowGeometry.ResizeWidget(
            origin, Edge.Right | Edge.Bottom,
            new Vector(Reference.Width, Reference.Height),
            min, max, new Rect(0, 0, 4000, 3000));
        double scale = WindowGeometry.ContentScale(
            WindowGeometry.Panel(dragged.Width, dragged.Height), Reference);

        Assert.AreEqual(2.0, scale, 0.01);
    }
}
