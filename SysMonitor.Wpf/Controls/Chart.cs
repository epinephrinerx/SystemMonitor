using System.Windows;
using System.Windows.Media;
using SysMonitor.Model;

namespace SysMonitor.Controls;

/// <summary>
/// A filled line graph of one series, drawn the way Task Manager draws one:
/// a framed plot box, a square grid behind the data, a solid tint under the
/// line, and an angular stroke on top.
///
/// A bare <see cref="FrameworkElement"/> that draws in OnRender, not a chart
/// built from elements: seventy-two points as seventy-two visuals would cost
/// more than everything else in the window put together. One
/// <see cref="StreamGeometry"/> per repaint and a scratch buffer reused
/// between repaints, so drawing allocates almost nothing per frame.
/// </summary>
public sealed class Chart : FrameworkElement
{
    /// <summary>Grid cells across and down, matching Task Manager's spacing.</summary>
    private const int Columns = 6;
    private const int Rows = 4;

    private double[] _scratch = Array.Empty<double>();

    public static readonly DependencyProperty SeriesProperty =
        DependencyProperty.Register(nameof(Series), typeof(History), typeof(Chart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Bumped by the view model after each push. The history object itself
    /// does not change identity, so without this WPF would have no reason to
    /// believe the picture is out of date.
    /// </summary>
    public static readonly DependencyProperty RevisionProperty =
        DependencyProperty.Register(nameof(Revision), typeof(int), typeof(Chart),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(Chart),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The square grid behind the data.</summary>
    public static readonly DependencyProperty GridBrushProperty =
        DependencyProperty.Register(nameof(GridBrush), typeof(Brush), typeof(Chart),
            new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The rectangle around the plot. Darker than the grid.</summary>
    public static readonly DependencyProperty FrameBrushProperty =
        DependencyProperty.Register(nameof(FrameBrush), typeof(Brush), typeof(Chart),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Behind the grid; the plot reads as a surface of its own.</summary>
    public static readonly DependencyProperty PlotBrushProperty =
        DependencyProperty.Register(nameof(PlotBrush), typeof(Brush), typeof(Chart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// The top of the scale. Percentages fix it at 100; rates leave it at 0
    /// and the graph scales to whatever the busiest moment was, so an idle
    /// adapter does not draw a flat line along the floor.
    /// </summary>
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(Chart),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public History? Series
    {
        get => (History?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public int Revision
    {
        get => (int)GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public Brush FrameBrush
    {
        get => (Brush)GetValue(FrameBrushProperty);
        set => SetValue(FrameBrushProperty, value);
    }

    public Brush? PlotBrush
    {
        get => (Brush?)GetValue(PlotBrushProperty);
        set => SetValue(PlotBrushProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>
    /// A frozen pen that does not drag the caller's brush down with it.
    ///
    /// Freezing a <see cref="Pen"/> freezes the brush it holds, and these
    /// brushes come from the view model, which recolours them when the theme
    /// changes. Freezing one there turns the next theme switch into
    /// "Cannot set a property ... because it is in a read-only state" -- which
    /// is exactly what it did. Take the colour, not the instance.
    /// </summary>
    internal static Pen PenFor(Brush brush, double thickness)
    {
        Brush safe = Safe(brush);
        if (Pens.TryGetValue((safe, thickness), out Pen? cached))
        {
            return cached;
        }
        var pen = new Pen(safe, thickness);
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }
        Pens[(safe, thickness)] = pen;
        return pen;
    }

    /// <summary>
    /// A brush that is safe to hand to something that will freeze it.
    ///
    /// Solid brushes come from the shared cache, keyed by colour. Anything
    /// else -- a gradient, an image brush -- is cloned, because the first
    /// version of this only handled the solid case and a gradient would have
    /// walked straight back into the bug it was written to prevent.
    /// </summary>
    private static Brush Safe(Brush brush)
    {
        if (brush is SolidColorBrush solid)
        {
            return Palette.Brush(solid.Color);
        }
        if (brush.IsFrozen)
        {
            return brush;
        }
        Brush copy = brush.Clone();
        copy.Freeze();
        return copy;
    }

    /// <summary>
    /// Pens by the brush and width they draw with. A repaint used to build
    /// three of them every time, which is not the "allocates almost nothing
    /// per frame" this class claims; the geometry alone is rebuilt, because
    /// that is the part that actually changes.
    /// </summary>
    private static readonly Dictionary<(Brush, double), Pen> Pens = new();

    /// <summary>
    /// A solid tint of the line colour for the area under it. Task Manager
    /// fills far more strongly than a faint wash -- at a glance the filled
    /// area is what tells you how busy something is.
    /// </summary>
    private static Brush FillFor(Brush accent, bool dark)
    {
        if (accent is not SolidColorBrush solid)
        {
            return Safe(accent);
        }
        Color line = solid.Color;
        // Toward the panel rather than to transparency, so overlapping grid
        // lines stay hidden under the fill the way they do in Task Manager.
        Color toward = dark ? Color.FromRgb(15, 23, 42) : Colors.White;
        return Palette.Brush(Mix(line, toward, dark ? 0.62 : 0.72));
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    private static bool IsDark(Brush? plot) =>
        plot is SolidColorBrush s && (s.Color.R + s.Color.G + s.Color.B) < 384;

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 2 || height <= 2)
        {
            return;
        }

        // Half-pixel offsets so a one-pixel line lands on a pixel instead of
        // straddling two and rendering as two grey ones.
        var plot = new Rect(0.5, 0.5, Math.Floor(width) - 1, Math.Floor(height) - 1);
        Pen frame = PenFor(FrameBrush, 1);
        Pen grid = PenFor(GridBrush, 1);

        dc.DrawRectangle(PlotBrush, null, plot);

        for (int column = 1; column < Columns; column++)
        {
            double x = Math.Round(plot.Left + plot.Width * column / Columns) + 0.5;
            dc.DrawLine(grid, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
        for (int row = 1; row < Rows; row++)
        {
            double y = Math.Round(plot.Top + plot.Height * row / Rows) + 0.5;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        History? series = Series;
        if (series is not null && series.Count >= 2)
        {
            DrawSeries(dc, plot, series);
        }

        // The frame goes on last so the fill cannot paint over it.
        dc.DrawRectangle(null, frame, plot);
    }

    private void DrawSeries(DrawingContext dc, Rect plot, History series)
    {
        if (_scratch.Length < series.Capacity)
        {
            _scratch = new double[series.Capacity];
        }
        int count = series.CopyTo(_scratch);
        if (count < 2)
        {
            return;
        }

        // A fixed scale for percentages; otherwise headroom above the peak so
        // the line is never pinned to the top edge.
        double max = Maximum > 0 ? Maximum : Math.Max(series.Max * 1.25, 0.001);

        // The newest reading sits at the right edge, and a series that has not
        // filled its buffer yet starts partway across rather than stretching.
        double step = plot.Width / (series.Capacity - 1);
        double left = plot.Right - (count - 1) * step;

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(left, plot.Bottom), isFilled: true, isClosed: true);
            for (int i = 0; i < count; i++)
            {
                double y = plot.Bottom - Math.Clamp(_scratch[i] / max, 0, 1) * plot.Height;
                // Angular, not smoothed: a spike should look like a spike.
                ctx.LineTo(new Point(left + i * step, y), isStroked: true, isSmoothJoin: false);
            }
            ctx.LineTo(new Point(plot.Right, plot.Bottom), isStroked: false, isSmoothJoin: false);
        }
        geometry.Freeze();

        dc.DrawGeometry(FillFor(Accent, IsDark(PlotBrush)), PenFor(Accent, 1.2), geometry);
    }
}
