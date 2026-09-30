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

    /// <summary>
    /// Where the widget ends up when an edge is dragged with its shape locked.
    ///
    /// The widget is the one view whose proportions are part of how it reads,
    /// so a drag scales the whole panel rather than moving one edge: pulling
    /// a side drives the scale and the other dimension follows, pulling a
    /// corner scales along whichever axis the pointer moved relatively
    /// further, and pulling the top or bottom edge widens the panel around
    /// its middle.
    ///
    /// The scale is clamped so both dimensions stay inside the widget's own
    /// limits, which is what keeps the ratio through a clamp: both axes move
    /// by the same factor. A panel that starts outside those limits (a config
    /// edited by hand, an older build's extreme shape) has an empty feasible
    /// range; the fallback then keeps the scale nearest to where the panel
    /// already was, rather than jumping to an arbitrary end.
    ///
    /// The work area bounds the result: a horizontal drag grows downwards
    /// until the panel's bottom edge reaches the bottom of the work area and
    /// then grows upwards off that same edge, and widening around the middle
    /// is pulled back so both side edges stay on the screen.
    /// </summary>
    public static Rect ResizeWidget(Rect origin, Edge edge, Vector delta,
                                    (double W, double H) min, (double W, double H) max,
                                    Rect workArea)
    {
        double panelW = origin.Width - ShadowPad * 2;
        double panelH = origin.Height - ShadowPad * 2;
        if (panelW <= 0 || panelH <= 0 || edge == Edge.None)
        {
            return origin;
        }

        // The feasible scale keeps the panel inside both limits at once.
        double sMin = Math.Max(min.W / panelW, min.H / panelH);
        double sMax = Math.Min(max.W / panelW, max.H / panelH);
        if (sMin > sMax)
        {
            // No scale satisfies both limits -- the start is already outside
            // them (a hand-edited config, an older build's extreme shape).
            // Stay on whichever end of the broken range sits nearest the
            // starting size; keeping the shape matters more than either
            // single limit, so one dimension may stay outside the limits.
            if (Math.Abs(1 - sMin) >= Math.Abs(1 - sMax))
            {
                sMin = sMax;
            }
            else
            {
                sMax = sMin;
            }
        }

        double sX = edge.HasFlag(Edge.Right) ? (panelW + delta.X) / panelW
                  : edge.HasFlag(Edge.Left) ? (panelW - delta.X) / panelW
                  : 1.0;
        double sY = edge.HasFlag(Edge.Bottom) ? (panelH + delta.Y) / panelH
                  : edge.HasFlag(Edge.Top) ? (panelH - delta.Y) / panelH
                  : 1.0;

        bool horizontal = edge.HasFlag(Edge.Left) || edge.HasFlag(Edge.Right);
        bool vertical = edge.HasFlag(Edge.Top) || edge.HasFlag(Edge.Bottom);
        double s = horizontal && vertical
                   ? (Math.Abs(sX - 1) >= Math.Abs(sY - 1) ? sX : sY)
                   : horizontal ? sX
                   : vertical ? sY
                   : 1.0;

        if (horizontal && vertical)
        {
            // A corner drag has one edge following the pointer and one axis
            // following the scale -- and the followed axis has no pointer to
            // stop it at the screen's edge. Bound the scale by the room
            // between the anchored visible edge and the work area's far
            // side, so the panel grows only as far as the screen allows.
            double roomH = edge.HasFlag(Edge.Bottom)
                ? workArea.Bottom - origin.Top - ShadowPad          // top pinned, grows down
                : origin.Bottom - workArea.Top - ShadowPad;         // bottom pinned, grows up
            double roomW = edge.HasFlag(Edge.Left)
                ? origin.Right - workArea.Left - ShadowPad          // right pinned, grows left
                : workArea.Right - origin.Left - ShadowPad;         // left pinned, grows right
            s = Math.Min(s, Math.Min(roomH / panelH, roomW / panelW));
        }

        s = Math.Clamp(s, sMin, sMax);

        double width = panelW * s + ShadowPad * 2;
        double height = panelH * s + ShadowPad * 2;

        double left, top;
        if (edge.HasFlag(Edge.Left))
        {
            left = origin.Right - width;
        }
        else if (edge.HasFlag(Edge.Right))
        {
            left = origin.Left;
        }
        else
        {
            // A vertical drag widens around the middle, so the panel keeps
            // looking at the same spot -- but both edges stay on the screen.
            left = origin.Left + (origin.Width - width) / 2;
            double minLeft = workArea.Left - ShadowPad;
            double maxLeft = workArea.Right + ShadowPad - width;
            left = minLeft <= maxLeft ? Math.Clamp(left, minLeft, maxLeft)
                                      : workArea.Left + (workArea.Width - width) / 2;
        }

        if (edge.HasFlag(Edge.Top))
        {
            top = origin.Bottom - height;
        }
        else if (edge.HasFlag(Edge.Bottom))
        {
            top = origin.Top;
        }
        else
        {
            // A horizontal drag grows downwards until the panel's bottom
            // reaches the work area and then grows upwards off that same
            // edge -- one continuous rule, so the window never jumps
            // mid-drag. The shadow pad is invisible; the work area is judged
            // on the panel's bottom edge.
            top = Math.Min(origin.Top, workArea.Bottom + ShadowPad - height);
        }

        return new Rect(left, top, width, height);
    }

    /// <summary>
    /// Which of the three views a size is being judged against.
    ///
    /// The names are the ones used in conversation about this app: the widget
    /// strip, the overall summary, and the full per-device view.
    /// </summary>
    public enum View
    {
        Widget,
        Overall,
        Full,
    }

    /// <summary>
    /// Limits for a view. The widget strip is capped; the other two are
    /// windows that should be allowed to fill whatever monitor they are on.
    /// </summary>
    public static ((double W, double H) Min, (double W, double H) Max) Limits(View view) => view switch
    {
        View.Full => (AppConfig.MinFull, (4000.0, 3000.0)),
        View.Overall => (AppConfig.MinOverall, (4000.0, 3000.0)),
        _ => (AppConfig.MinWidget, AppConfig.MaxWidget),
    };
}
