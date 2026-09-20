using System.Windows;

namespace SysMonitor;

/// <summary>
/// The arithmetic behind dragging the corner, kept apart from the event
/// plumbing so it can be checked without a window.
/// </summary>
internal static class WindowGeometry
{
    /// <summary>Room the drop shadow needs on every side.</summary>
    public const double ShadowPad = 12;

    /// <summary>The bottom-right corner that starts a resize.</summary>
    public const double GripSize = 16;

    /// <summary>
    /// Is the pointer on the resize corner?
    ///
    /// The zone runs to the window's own edge rather than stopping at the
    /// panel, because the shadow margin is part of the corner as far as anyone
    /// aiming at it is concerned.
    /// </summary>
    public static bool InGrip(Point point, double width, double height) =>
        point.X >= width - ShadowPad - GripSize && point.X <= width
        && point.Y >= height - ShadowPad - GripSize && point.Y <= height;

    /// <summary>
    /// Window size for a dragged corner, held between the limits for the view
    /// being resized. Returns outer window size, shadow included.
    /// </summary>
    public static Size Clamp(double width, double height,
                             (double W, double H) min, (double W, double H) max) =>
        new(Math.Clamp(width, min.W + ShadowPad * 2, max.W + ShadowPad * 2),
            Math.Clamp(height, min.H + ShadowPad * 2, max.H + ShadowPad * 2));

    /// <summary>The panel inside a window of this size.</summary>
    public static Size Panel(double width, double height) =>
        new(width - ShadowPad * 2, height - ShadowPad * 2);

    /// <summary>Limits for a view: the mini strip is capped, the panel is not.</summary>
    public static ((double W, double H) Min, (double W, double H) Max) Limits(bool expanded) =>
        expanded
            ? (AppConfig.MinExp, (4000.0, 3000.0))
            : (AppConfig.MinMini, AppConfig.MaxMini);
}
