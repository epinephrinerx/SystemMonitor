using System.Windows;

namespace SysMonitor;

/// <summary>Which sides of the window a drag is moving.</summary>
[Flags]
internal enum Edge
{
    None = 0,
    Left = 1,
    Right = 2,
    Top = 4,
    Bottom = 8,
}

/// <summary>
/// The arithmetic behind dragging an edge or a corner, kept apart from the
/// event plumbing so it can be checked without a window.
/// </summary>
internal static class WindowGeometry
{
    /// <summary>Room the drop shadow needs on every side.</summary>
    public const double ShadowPad = 12;

    /// <summary>How far in from the window edge a drag counts as that edge.</summary>
    public const double EdgeBand = ShadowPad + 6;

    /// <summary>A corner claims a longer run of both its edges.</summary>
    public const double CornerBand = ShadowPad + 16;

    /// <summary>
    /// Which edges the pointer is on, if any. Measured from the window's own
    /// rectangle, shadow margin included: that margin is invisible, but it is
    /// where the pointer goes when someone aims at the edge of what they see.
    /// </summary>
    public static Edge HitTest(Point point, double width, double height)
    {
        // Corners first, so the run where two bands overlap resizes both ways
        // rather than whichever side happens to be tested first.
        bool left = point.X <= CornerBand;
        bool right = point.X >= width - CornerBand;
        bool top = point.Y <= CornerBand;
        bool bottom = point.Y >= height - CornerBand;

        if (left && top)
        {
            return Edge.Left | Edge.Top;
        }
        if (right && top)
        {
            return Edge.Right | Edge.Top;
        }
        if (left && bottom)
        {
            return Edge.Left | Edge.Bottom;
        }
        if (right && bottom)
        {
            return Edge.Right | Edge.Bottom;
        }

        if (point.X <= EdgeBand)
        {
            return Edge.Left;
        }
        if (point.X >= width - EdgeBand)
        {
            return Edge.Right;
        }
        if (point.Y <= EdgeBand)
        {
            return Edge.Top;
        }
        if (point.Y >= height - EdgeBand)
        {
            return Edge.Bottom;
        }
        return Edge.None;
    }

    /// <summary>
    /// Where the window ends up when an edge is dragged.
    ///
    /// Everything is computed from the rectangle the drag started on, never
    /// from the last frame: an incremental version drifts, and clamping at the
    /// minimum size makes it drift further every frame the pointer keeps
    /// moving. The side opposite the one being dragged stays exactly where it
    /// was, including when the size runs into its limit.
    /// </summary>
    public static Rect Resize(Rect origin, Edge edge, Vector delta,
                              (double W, double H) min, (double W, double H) max)
    {
        double minW = min.W + ShadowPad * 2;
        double maxW = max.W + ShadowPad * 2;
        double minH = min.H + ShadowPad * 2;
        double maxH = max.H + ShadowPad * 2;

        double left = origin.Left;
        double top = origin.Top;
        double width = origin.Width;
        double height = origin.Height;

        if (edge.HasFlag(Edge.Right))
        {
            width = Math.Clamp(origin.Width + delta.X, minW, maxW);
        }
        else if (edge.HasFlag(Edge.Left))
        {
            width = Math.Clamp(origin.Width - delta.X, minW, maxW);
            // Pin the right edge: whatever the width ended up being, the far
            // side has not moved.
            left = origin.Right - width;
        }

        if (edge.HasFlag(Edge.Bottom))
        {
            height = Math.Clamp(origin.Height + delta.Y, minH, maxH);
        }
        else if (edge.HasFlag(Edge.Top))
        {
            height = Math.Clamp(origin.Height - delta.Y, minH, maxH);
            top = origin.Bottom - height;
        }

        return new Rect(left, top, width, height);
    }

    /// <summary>The panel inside a window of this size.</summary>
    public static Size Panel(double width, double height) =>
        new(width - ShadowPad * 2, height - ShadowPad * 2);

/// <summary>Which of the three views a size is being judged against.</summary>
    public enum View
    {
        Mini,
        Expanded,
        Full,
    }

    /// <summary>
    /// Limits for a view. The mini strip is capped; the other two are windows
    /// that should be allowed to fill whatever monitor they are on.
    /// </summary>
    public static ((double W, double H) Min, (double W, double H) Max) Limits(View view) => view switch
    {
        View.Full => (AppConfig.MinFull, (4000.0, 3000.0)),
        View.Expanded => (AppConfig.MinExp, (4000.0, 3000.0)),
        _ => (AppConfig.MinMini, AppConfig.MaxMini),
    };

    /// <summary>
    /// Has the full view been dragged below the size its tabs need?
    ///
    /// Asked of the panel size, not the window: the shadow margin is not part
    /// of what the user is sizing, and the threshold in the requirement is
    /// about what is visible.
    /// </summary>
    public static bool TooSmallForFull(double panelWidth, double panelHeight) =>
        panelWidth < AppConfig.MinFull.W || panelHeight < AppConfig.MinFull.H;
}
