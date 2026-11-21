namespace SysMonitor.Linux.Presentation;

public readonly record struct PointD(double X, double Y);

public readonly record struct SizeD(double Width, double Height);

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public PointD TopLeft => new(X, Y);
    public SizeD Size => new(Width, Height);
    public bool Contains(PointD point) =>
        point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;
}

/// <summary>Which window edge (or corner) a pointer press landed on.</summary>
public enum ResizeEdge
{
    None,
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// All the arithmetic of moving and sizing the main window. It is deliberately
/// separate from Avalonia: every rule in spec section 1 can be checked here.
///
/// Rect coordinates are window coordinates, shadow pad included, in DIPs --
/// except <see cref="ResizeWidget(RectD, ResizeEdge, double, double, RectD, SizeD, SizeD)"/>,
/// which works on the panel so the pad cannot skew the widget's ratio.
/// <see cref="ResizeWidgetWindow"/> converts around it. The size limits are
/// always panel sizes, so the pad is added on the way in and removed on the way
/// out.
/// </summary>
public static class WindowGeometry
{
    public const double ShadowPad = 12;

    /// <summary>Corners are tested within 12 + 16 px of both edges.</summary>
    public const double CornerBand = 28;

    /// <summary>Edges are tested within 12 + 6 px.</summary>
    public const double EdgeBand = 18;

    public const double SnapMargin = 25;

    /// <summary>First run sits 24 px inside the work area.</summary>
    public const double FirstRunInset = 24;

    /// <summary>The layout reference sizes for content scaling.</summary>
    public static SizeD ReferenceSize(ViewMode mode) => mode switch
    {
        ViewMode.Widget => new SizeD(270, 104),
        _ => new SizeD(640, 480),
    };

    public static SizeD MinPanelSize(ViewMode mode) => mode switch
    {
        ViewMode.Widget => new SizeD(200, 64),
        _ => new SizeD(640, 480),
    };

    public static SizeD MaxPanelSize(ViewMode mode) => mode switch
    {
        ViewMode.Widget => new SizeD(900, 400),
        _ => new SizeD(4000, 3000),
    };

    /// <summary>The stored panel size for a view; Main's zeros mean its minimum.</summary>
    public static SizeD PanelSize(ViewMode mode, AppConfig config) => mode switch
    {
        ViewMode.Widget => new SizeD(config.WidgetW, config.WidgetH),
        _ => new SizeD(
            config.MainW > 0 ? config.MainW : 640,
            config.MainH > 0 ? config.MainH : 480),
    };

    public static SizeD WindowSize(SizeD panel) =>
        new(panel.Width + ShadowPad * 2, panel.Height + ShadowPad * 2);

    public static SizeD PanelFromWindow(SizeD window) =>
        new(Math.Max(1, window.Width - ShadowPad * 2), Math.Max(1, window.Height - ShadowPad * 2));

    /// <summary>The panel rectangle inside a window rectangle: the pad on each side comes off.</summary>
    public static RectD PanelFromWindow(RectD window) =>
        new(
            window.X + ShadowPad,
            window.Y + ShadowPad,
            Math.Max(1, window.Width - ShadowPad * 2),
            Math.Max(1, window.Height - ShadowPad * 2));

    /// <summary>The window rectangle around a panel rectangle: the pad on each side goes back on.</summary>
    public static RectD WindowFromPanel(RectD panel) =>
        new(
            panel.X - ShadowPad,
            panel.Y - ShadowPad,
            panel.Width + ShadowPad * 2,
            panel.Height + ShadowPad * 2);

    public static SizeD WindowLimitsMin(ViewMode mode)
    {
        SizeD panel = MinPanelSize(mode);
        return WindowSize(panel);
    }

    public static SizeD WindowLimitsMax(ViewMode mode)
    {
        SizeD panel = MaxPanelSize(mode);
        return WindowSize(panel);
    }

    /// <summary>Hit test the whole window rect; corners win over edges.</summary>
    public static ResizeEdge HitTest(SizeD window, PointD point)
    {
        bool nearLeft = point.X <= EdgeBand;
        bool nearRight = point.X >= window.Width - EdgeBand;
        bool nearTop = point.Y <= EdgeBand;
        bool nearBottom = point.Y >= window.Height - EdgeBand;

        bool cornerLeft = point.X <= CornerBand;
        bool cornerRight = point.X >= window.Width - CornerBand;
        bool cornerTop = point.Y <= CornerBand;
        bool cornerBottom = point.Y >= window.Height - CornerBand;

        if (cornerLeft && cornerTop) return ResizeEdge.TopLeft;
        if (cornerRight && cornerTop) return ResizeEdge.TopRight;
        if (cornerLeft && cornerBottom) return ResizeEdge.BottomLeft;
        if (cornerRight && cornerBottom) return ResizeEdge.BottomRight;

        if (nearLeft) return ResizeEdge.Left;
        if (nearRight) return ResizeEdge.Right;
        if (nearTop) return ResizeEdge.Top;
        if (nearBottom) return ResizeEdge.Bottom;
        return ResizeEdge.None;
    }

    public static bool IsCorner(ResizeEdge edge) => edge is
        ResizeEdge.TopLeft or ResizeEdge.TopRight or ResizeEdge.BottomLeft or ResizeEdge.BottomRight;

    /// <summary>
    /// Move the rect the minimum amount that puts it inside the work area. A
    /// rect larger than the area is pinned to the area's top-left.
    /// </summary>
    public static RectD KeepOnScreen(RectD rect, RectD workArea)
    {
        double x = rect.X;
        double y = rect.Y;

        if (rect.Width >= workArea.Width)
        {
            x = workArea.X;
        }
        else
        {
            x = Math.Clamp(x, workArea.X, workArea.Right - rect.Width);
        }

        if (rect.Height >= workArea.Height)
        {
            y = workArea.Y;
        }
        else
        {
            y = Math.Clamp(y, workArea.Y, workArea.Bottom - rect.Height);
        }

        return rect with { X = x, Y = y };
    }

    /// <summary>Bottom-right of the work area, inset 24 px on both axes.</summary>
    public static PointD FirstRunPosition(RectD workArea, SizeD window)
    {
        double x = workArea.Right - window.Width - FirstRunInset;
        double y = workArea.Bottom - window.Height - FirstRunInset;
        return new PointD(Math.Max(workArea.X, x), Math.Max(workArea.Y, y));
    }

    /// <summary>
    /// Pull the rect to a work-area edge it is already close to. Each axis is
    /// decided independently, and both edges are candidates: the nearer one wins.
    /// </summary>
    public static RectD Snap(RectD rect, RectD workArea, double margin = SnapMargin)
    {
        double x = rect.X;
        double y = rect.Y;

        double toLeft = Math.Abs(rect.X - workArea.X);
        double toRight = Math.Abs(rect.Right - workArea.Right);
        if (toLeft <= margin || toRight <= margin)
        {
            x = toLeft <= toRight ? workArea.X : workArea.Right - rect.Width;
        }

        double toTop = Math.Abs(rect.Y - workArea.Y);
        double toBottom = Math.Abs(rect.Bottom - workArea.Bottom);
        if (toTop <= margin || toBottom <= margin)
        {
            y = toTop <= toBottom ? workArea.Y : workArea.Bottom - rect.Height;
        }

        return rect with { X = x, Y = y };
    }

    /// <summary>
    /// Free resize: the opposite edge stays pinned even when the size clamps.
    /// Deltas are from the rect captured at drag start, never from the last frame.
    /// </summary>
    public static RectD Resize(RectD start, ResizeEdge edge, double dx, double dy, SizeD min, SizeD max)
    {
        if (edge == ResizeEdge.None)
        {
            return start;
        }

        bool left = edge is ResizeEdge.Left or ResizeEdge.TopLeft or ResizeEdge.BottomLeft;
        bool right = edge is ResizeEdge.Right or ResizeEdge.TopRight or ResizeEdge.BottomRight;
        bool top = edge is ResizeEdge.Top or ResizeEdge.TopLeft or ResizeEdge.TopRight;
        bool bottom = edge is ResizeEdge.Bottom or ResizeEdge.BottomLeft or ResizeEdge.BottomRight;

        double width = start.Width;
        double height = start.Height;

        if (left)
        {
            width = Math.Clamp(start.Width - dx, min.Width, max.Width);
        }
        else if (right)
        {
            width = Math.Clamp(start.Width + dx, min.Width, max.Width);
        }

        if (top)
        {
            height = Math.Clamp(start.Height - dy, min.Height, max.Height);
        }
        else if (bottom)
        {
            height = Math.Clamp(start.Height + dy, min.Height, max.Height);
        }

        double x = left ? start.Right - width : start.X;
        double y = top ? start.Bottom - height : start.Y;
        return new RectD(x, y, width, height);
    }

    /// <summary>
    /// Widget resize in window coordinates: the panel rectangle is the one that
    /// keeps the 270x104 ratio, so the 12 DIP pad is removed, the panel is
    /// resized, and the pad is added back. This is the path the window uses.
    /// </summary>
    public static RectD ResizeWidgetWindow(
        RectD startWindow, ResizeEdge edge, double dx, double dy, RectD workArea, SizeD minPanel, SizeD maxPanel)
    {
        RectD panel = PanelFromWindow(startWindow);
        RectD panelArea = PanelFromWindow(workArea);
        return WindowFromPanel(
            ResizeWidget(panel, edge, dx, dy, panelArea, minPanel, maxPanel));
    }

    /// <summary>
    /// Widget resize in panel coordinates, which keeps the 270x104 aspect ratio
    /// (spec section 1). Use <see cref="ResizeWidgetWindow"/> when the rect is a
    /// window rect.
    ///
    /// A side drag drives the scale; a top/bottom drag widens around the centre;
    /// a corner uses whichever axis moved relatively further. A horizontal drag
    /// grows down until the work-area bottom and then grows up, and the centre
    /// widening is clamped so both side edges stay on screen.
    /// </summary>
    public static RectD ResizeWidget(
        RectD start, ResizeEdge edge, double dx, double dy, RectD workArea, SizeD min, SizeD max)
    {
        if (edge == ResizeEdge.None || start.Width <= 0 || start.Height <= 0)
        {
            return start;
        }

        double scale;
        bool horizontal = edge is ResizeEdge.Left or ResizeEdge.Right;
        bool vertical = edge is ResizeEdge.Top or ResizeEdge.Bottom;

        if (horizontal)
        {
            double wanted = edge == ResizeEdge.Right ? start.Width + dx : start.Width - dx;
            scale = wanted / start.Width;
        }
        else if (vertical)
        {
            double wanted = edge == ResizeEdge.Bottom ? start.Height + dy : start.Height - dy;
            scale = wanted / start.Height;
        }
        else
        {
            double scaleX = edge is ResizeEdge.TopRight or ResizeEdge.BottomRight
                ? (start.Width + dx) / start.Width
                : (start.Width - dx) / start.Width;
            double scaleY = edge is ResizeEdge.BottomLeft or ResizeEdge.BottomRight
                ? (start.Height + dy) / start.Height
                : (start.Height - dy) / start.Height;
            scale = Math.Abs(scaleX - 1) >= Math.Abs(scaleY - 1) ? scaleX : scaleY;
        }

        scale = ClampScale(scale, start, min, max);
        double width = start.Width * scale;
        double height = start.Height * scale;

        double x;
        double y;

        if (horizontal)
        {
            // Side drag: right or left edge drives, the other stays.
            x = edge == ResizeEdge.Right ? start.X : start.Right - width;
            y = start.Y;
            if (y + height > workArea.Bottom)
            {
                y = workArea.Bottom - height;
            }
            if (y < workArea.Y)
            {
                y = workArea.Y;
            }
        }
        else if (vertical)
        {
            // Centre widening: both side edges remain on screen.
            x = start.X + start.Width / 2 - width / 2;
            x = Math.Clamp(x, workArea.X, Math.Max(workArea.X, workArea.Right - width));
            y = edge == ResizeEdge.Bottom ? start.Y : start.Bottom - height;
        }
        else
        {
            // Corner: the opposite corner is pinned.
            bool left = edge is ResizeEdge.TopLeft or ResizeEdge.BottomLeft;
            bool top = edge is ResizeEdge.TopLeft or ResizeEdge.TopRight;
            x = left ? start.Right - width : start.X;
            y = top ? start.Bottom - height : start.Y;
            // Keep the moved edges inside the work area; a window wider than
            // the area has no valid range, so it is pinned to the left/top.
            x = width >= workArea.Width
                ? workArea.X
                : Math.Clamp(x, workArea.X, workArea.Right - width);
            y = height >= workArea.Height
                ? workArea.Y
                : Math.Clamp(y, workArea.Y, workArea.Bottom - height);
        }

        return new RectD(x, y, width, height);
    }

    /// <summary>
    /// Clamp a widget scale so both dimensions stay within the limits. A start
    /// already outside the limits has no valid range, so the allowed scale
    /// nearest to 1 is used.
    /// </summary>
    private static double ClampScale(double scale, RectD start, SizeD min, SizeD max)
    {
        double low = Math.Max(min.Width / start.Width, min.Height / start.Height);
        double high = Math.Min(max.Width / start.Width, max.Height / start.Height);
        if (low > high)
        {
            return Math.Abs(low - 1) <= Math.Abs(high - 1) ? low : high;
        }
        return Math.Clamp(scale, low, high);
    }

    /// <summary>
    /// Overall's auto-fit: measured size, the layout scale divided out, ceil,
    /// then clamped to [min, work area - 24].
    /// </summary>
    public static SizeD AutoFit(SizeD measured, double layoutScale, SizeD min, RectD workArea)
    {
        double scale = layoutScale <= 0 ? 1 : layoutScale;
        double width = Math.Ceiling(measured.Width / scale);
        double height = Math.Ceiling(measured.Height / scale);
        double maxWidth = Math.Max(min.Width, workArea.Width - ShadowPad * 2);
        double maxHeight = Math.Max(min.Height, workArea.Height - ShadowPad * 2);
        return new SizeD(
            Math.Clamp(width, min.Width, maxWidth),
            Math.Clamp(height, min.Height, maxHeight));
    }

    /// <summary>The scale applied to view content: min(panel/ref) on both axes.</summary>
    public static double ContentScale(SizeD panel, SizeD reference)
    {
        if (reference.Width <= 0 || reference.Height <= 0)
        {
            return 1;
        }
        return Math.Max(0.01, Math.Min(panel.Width / reference.Width, panel.Height / reference.Height));
    }

    public static ResizeEdge? EdgeForCursor(ResizeEdge edge) => edge == ResizeEdge.None ? null : edge;
}

/// <summary>
/// Position and size memory. One position is shared by both windows (R-024);
/// leaving Main returns to the pre-Main position unless the window was moved
/// while Main was showing.
/// </summary>
public sealed class Placement
{
    public PointD? Position { get; set; }
    public RectD? BeforeFull { get; private set; }
    public bool MovedWhileFull { get; private set; }
    public bool InFull { get; private set; }

    /// <summary>
    /// The top-left to use after a mode switch. Entering Main remembers the
    /// current rect; leaving it may restore the remembered one.
    /// </summary>
    public PointD ApplySwitch(ViewMode from, ViewMode to, RectD current)
    {
        if (IsMain(to))
        {
            BeforeFull = current;
            MovedWhileFull = false;
            InFull = true;
            return current.TopLeft;
        }

        if (IsMain(from) && !MovedWhileFull && BeforeFull is { } before)
        {
            InFull = false;
            return before.TopLeft;
        }

        InFull = false;
        return current.TopLeft;
    }

    /// <summary>Main is the only window the placement logic knows besides the Widget.</summary>
    private static bool IsMain(ViewMode mode) => mode == ViewMode.Main;

    /// <summary>A drag or resize while Full is showing makes the return position stale.</summary>
    public void NoteMoved()
    {
        if (InFull)
        {
            MovedWhileFull = true;
        }
    }

    public void Reset()
    {
        BeforeFull = null;
        MovedWhileFull = false;
        InFull = false;
    }
}
