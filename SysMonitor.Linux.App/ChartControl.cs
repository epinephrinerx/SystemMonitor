using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.App;

/// <summary>
/// The 72-point chart, drawn exactly as spec section 5b: plot fill, 6x4 inner
/// grid, the series with a 1.2 px angular stroke and a solid mixed fill, then
/// the frame drawn last. A short series sits on the right edge; it is never
/// stretched.
///
/// The series can be re-pointed between frames, so a module page updates its
/// card in place instead of rebuilding the control.
/// </summary>
internal sealed class ChartControl : Control
{
    private readonly double[] _values = new double[ChartMath.Points];
    private readonly IBrush _plotBrush;
    private readonly IPen _gridPen;
    private readonly IPen _framePen;
    private readonly IBrush _fillBrush;
    private readonly IPen _linePen;
    private readonly Colour _plotColour;

    private History _history;
    private double _maximum;

    public ChartControl(History history, Colour accent, double maximum, Palette palette)
    {
        _history = history;
        _maximum = maximum;
        _plotColour = palette.Plot;

        _plotBrush = Ui.Brush(palette.Plot);
        _gridPen = new Pen(Ui.Brush(palette.Grid), 1);
        _framePen = new Pen(Ui.Brush(palette.Border), 1);
        _linePen = new Pen(Ui.Brush(accent), ChartMath.StrokeThickness)
        {
            LineJoin = PenLineJoin.Miter,
        };
        _fillBrush = Ui.Brush(ChartMath.FillColour(accent, palette.Plot));
    }

    public History History => _history;

    public double Maximum => _maximum;

    /// <summary>
    /// Point the chart at a series and its ceiling. The data tick calls this so
    /// the plot is redrawn even though the <see cref="History"/> fills in place.
    /// </summary>
    public void SetSeries(History history, double maximum)
    {
        _history = history;
        _maximum = maximum;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width < 2 || height < 2)
        {
            return;
        }

        ChartRect plot = ChartMath.PlotRect(width, height);
        var rect = new Rect(plot.X, plot.Y, plot.Width, plot.Height);
        context.FillRectangle(_plotBrush, rect);

        foreach (double x in ChartMath.GridX(plot))
        {
            context.DrawLine(_gridPen, new Point(x, plot.Y), new Point(x, plot.Bottom));
        }
        foreach (double y in ChartMath.GridY(plot))
        {
            context.DrawLine(_gridPen, new Point(plot.X, y), new Point(plot.Right, y));
        }

        int count = _history.CopyTo(_values);
        if (count >= 2)
        {
            double scale = ChartMath.Scale(_history.Max, _maximum);
            var geometry = new StreamGeometry();
            using (StreamGeometryContext path = geometry.Open())
            {
                path.BeginFigure(new Point(plot.X, plot.Bottom), isFilled: true);
                for (int i = 0; i < count; i++)
                {
                    ChartPoint point = ChartMath.Point(i, count, _values[i], scale, plot);
                    path.LineTo(new Point(point.X, point.Y));
                }
                path.LineTo(new Point(plot.Right, plot.Bottom));
                path.EndFigure(isClosed: true);
            }
            context.DrawGeometry(_fillBrush, _linePen, geometry);
        }

        // Frame last, so the series and grid cannot draw over it.
        context.DrawRectangle(null, _framePen, rect);
    }

    /// <summary>Keep the plot colour testable without a display.</summary>
    public bool IsDarkPlot => _plotColour.IsDarkPlot;
}
