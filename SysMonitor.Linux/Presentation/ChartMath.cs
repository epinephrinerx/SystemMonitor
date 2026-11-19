namespace SysMonitor.Linux.Presentation;

/// <summary>A rectangle in control coordinates.</summary>
public readonly record struct ChartRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>One plotted reading.</summary>
public readonly record struct ChartPoint(double X, double Y);

/// <summary>
/// The geometry of the 72-point chart, pulled out of the drawing so it can be
/// checked without a display (spec section 5b).
/// </summary>
public static class ChartMath
{
    public const int Points = 72;
    public const int Columns = 6;
    public const int Rows = 4;
    public const double StrokeThickness = 1.2;

    /// <summary>
    /// The plot rectangle: (0.5, 0.5, floor(w)-1, floor(h)-1), so a 1 px line
    /// lands on a pixel instead of straddling two.
    /// </summary>
    public static ChartRect PlotRect(double width, double height) =>
        new(0.5, 0.5, Math.Max(0, Math.Floor(width) - 1), Math.Max(0, Math.Floor(height) - 1));

    /// <summary>
    /// 100 for percentages, otherwise max(max × 1.25, 0.001): the spec's
    /// scale rule.
    /// </summary>
    public static double Scale(double seriesMax, double maximum) =>
        maximum > 0 ? maximum : Math.Max(seriesMax * 1.25, 0.001);

    /// <summary>
    /// A reading's point. The newest lands on the right edge and a short
    /// series starts part-way across; it is not stretched.
    /// </summary>
    public static ChartPoint Point(int index, int count, double value, double scale, ChartRect plot)
    {
        double step = plot.Width / (Points - 1);
        double x = plot.Right - (count - 1 - index) * step;
        double ratio = scale <= 0 ? 0 : Math.Clamp(value / scale, 0, 1);
        return new ChartPoint(x, plot.Bottom - ratio * plot.Height);
    }

    /// <summary>The five inner vertical lines, already snapped for a 1 px pen.</summary>
    public static IReadOnlyList<double> GridX(ChartRect plot)
    {
        var lines = new double[Columns - 1];
        for (int i = 1; i < Columns; i++)
        {
            lines[i - 1] = Math.Round(plot.X + plot.Width * i / Columns) + 0.5;
        }
        return lines;
    }

    /// <summary>The three inner horizontal lines, already snapped.</summary>
    public static IReadOnlyList<double> GridY(ChartRect plot)
    {
        var lines = new double[Rows - 1];
        for (int i = 1; i < Rows; i++)
        {
            lines[i - 1] = Math.Round(plot.Y + plot.Height * i / Rows) + 0.5;
        }
        return lines;
    }

    /// <summary>
    /// The solid fill under the line: the accent mixed toward the panel, by
    /// 0.62 on a dark plot and 0.72 on a light one.
    /// </summary>
    public static Colour FillColour(Colour accent, Colour plot) =>
        plot.IsDarkPlot
            ? accent.Mix(Colour.FromHex("#0f172a"), 0.62)
            : accent.Mix(Colour.FromHex("#ffffff"), 0.72);

    /// <summary>The ceiling text: "100%" for a fixed maximum, else "x.x MB/s".</summary>
    public static string Ceiling(double maximum, double seriesMax, string unit) =>
        maximum > 0
            ? "100%"
            : Format.Speed(Math.Max(seriesMax * 1.25, 0.001)) + " " + unit;
}
