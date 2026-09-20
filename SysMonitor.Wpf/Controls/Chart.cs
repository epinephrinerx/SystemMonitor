using System.Windows;
using System.Windows.Media;
using SysMonitor.Model;

namespace SysMonitor.Controls;

/// <summary>
/// A filled line graph of one series.
///
/// A bare <see cref="FrameworkElement"/> that draws in OnRender, not a chart
/// built from elements: a hundred and eighty points as a hundred and eighty
/// visuals would cost more than everything else in the window put together.
/// One <see cref="StreamGeometry"/> per repaint, frozen, and a scratch buffer
/// reused between repaints so drawing allocates nothing per frame.
/// </summary>
public sealed class Chart : FrameworkElement
{
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

    public static readonly DependencyProperty GridBrushProperty =
        DependencyProperty.Register(nameof(GridBrush), typeof(Brush), typeof(Chart),
            new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

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
        Brush safe = brush is SolidColorBrush solid ? Palette.Brush(solid.Color) : brush;
        var pen = new Pen(safe, thickness);
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }
        return pen;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        Pen grid = PenFor(GridBrush, 0.6);
        for (int line = 1; line < 4; line++)
        {
            double y = Math.Round(height * line / 4.0) + 0.5;
            dc.DrawLine(grid, new Point(0, y), new Point(width, y));
        }

        History? series = Series;
        if (series is null || series.Count < 2)
        {
            return;
        }

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
        double step = width / (series.Capacity - 1);
        double left = width - (count - 1) * step;

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(left, height), isFilled: true, isClosed: true);
            for (int i = 0; i < count; i++)
            {
                double y = height - Math.Clamp(_scratch[i] / max, 0, 1) * height;
                ctx.LineTo(new Point(left + i * step, y), isStroked: true, isSmoothJoin: true);
            }
            ctx.LineTo(new Point(width, height), isStroked: false, isSmoothJoin: false);
        }
        geometry.Freeze();

        Brush fill = Accent.Clone();
        fill.Opacity = 0.22;
        fill.Freeze();
        dc.DrawGeometry(fill, PenFor(Accent, 1.4), geometry);
    }
}
