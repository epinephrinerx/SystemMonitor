using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class ChartMathTests
{
    [TestMethod]
    public void Constants_MatchSpecSection5b()
    {
        Assert.AreEqual(72, ChartMath.Points);
        Assert.AreEqual(6, ChartMath.Columns);
        Assert.AreEqual(4, ChartMath.Rows);
        Assert.AreEqual(1.2, ChartMath.StrokeThickness, 0.001);
    }

    [TestMethod]
    public void PlotRect_SnapsToHalfPixelBorder_AndFloorsSizeMinusOne()
    {
        ChartRect rect = ChartMath.PlotRect(width: 300.7, height: 200.2);

        Assert.AreEqual(0.5, rect.X);
        Assert.AreEqual(0.5, rect.Y);
        Assert.AreEqual(299.0, rect.Width); // floor(300.7) - 1 = 300 - 1 = 299
        Assert.AreEqual(199.0, rect.Height); // floor(200.2) - 1 = 200 - 1 = 199
        Assert.AreEqual(299.5, rect.Right);
        Assert.AreEqual(199.5, rect.Bottom);

        // Zero or tiny dimensions never become negative
        ChartRect zero = ChartMath.PlotRect(0, 0);
        Assert.AreEqual(0.0, zero.Width);
        Assert.AreEqual(0.0, zero.Height);
    }

    [TestMethod]
    public void Scale_FixedMaximum_ReturnsMaximum()
    {
        // For percentages (maximum = 100), fixed maximum always wins
        Assert.AreEqual(100.0, ChartMath.Scale(seriesMax: 45.0, maximum: 100.0));
        Assert.AreEqual(100.0, ChartMath.Scale(seriesMax: 99.0, maximum: 100.0));
        Assert.AreEqual(100.0, ChartMath.Scale(seriesMax: 0.0, maximum: 100.0));
    }

    [TestMethod]
    public void Scale_AutoMaximum_MultipliesSeriesMaxByOneAndQuarter_WithFloor()
    {
        // seriesMax * 1.25
        Assert.AreEqual(100.0, ChartMath.Scale(seriesMax: 80.0, maximum: 0));
        Assert.AreEqual(12.5, ChartMath.Scale(seriesMax: 10.0, maximum: 0));

        // When seriesMax is 0, scale floors to 0.001 so division never divides by 0
        Assert.AreEqual(0.001, ChartMath.Scale(seriesMax: 0.0, maximum: 0));
    }

    [TestMethod]
    public void Point_XStep_PlacesNewestReadingOnRightEdge_AndOldestOnLeftEdgeWhenFull()
    {
        var plot = new ChartRect(0.5, 0.5, 710.0, 200.0);
        // plot.Width = 710, step = 710 / 71 = 10.0 px per point
        // plot.Right = 710.5

        // Full 72-point series:
        // Newest point: index = 71 (count = 72)
        ChartPoint newest = ChartMath.Point(index: 71, count: 72, value: 50, scale: 100, plot);
        Assert.AreEqual(plot.Right, newest.X, 0.001, "Newest point must land exactly on plot.Right");

        // Oldest point of full series: index = 0 (count = 72)
        ChartPoint oldest = ChartMath.Point(index: 0, count: 72, value: 50, scale: 100, plot);
        Assert.AreEqual(plot.X, oldest.X, 0.001, "Oldest point of 72 must land on plot.X");

        // Middle point: index = 35
        ChartPoint mid = ChartMath.Point(index: 35, count: 72, value: 50, scale: 100, plot);
        Assert.AreEqual(plot.Right - (72 - 1 - 35) * 10.0, mid.X, 0.001);
    }

    [TestMethod]
    public void Point_PartialSeries_IsNotStretched_StartsPartWayAcross()
    {
        var plot = new ChartRect(0.5, 0.5, 710.0, 200.0);
        double step = 710.0 / (ChartMath.Points - 1); // 10.0

        // Partial series with only 10 points:
        // Newest point (index 9) must still land on plot.Right
        ChartPoint newest = ChartMath.Point(index: 9, count: 10, value: 50, scale: 100, plot);
        Assert.AreEqual(plot.Right, newest.X, 0.001, "Newest point must still be at plot.Right");

        // Oldest point of 10 points (index 0) must land 9 steps to the left of plot.Right, NOT at plot.X!
        ChartPoint oldest = ChartMath.Point(index: 0, count: 10, value: 50, scale: 100, plot);
        double expectedOldestX = plot.Right - 9 * step;
        Assert.AreEqual(expectedOldestX, oldest.X, 0.001);
        Assert.IsTrue(oldest.X > plot.X, "Partial series must start part-way across, not stretched to plot.X");
    }

    [TestMethod]
    public void Point_YCoordinate_InvertedCartesian_MapsZeroToBottomAndScaleToTop()
    {
        var plot = new ChartRect(0.5, 0.5, 710.0, 200.0);

        // value = 0 -> bottom
        ChartPoint zero = ChartMath.Point(index: 0, count: 1, value: 0, scale: 100, plot);
        Assert.AreEqual(plot.Bottom, zero.Y, 0.001);

        // value = scale -> top (plot.Bottom - plot.Height = plot.Y)
        ChartPoint full = ChartMath.Point(index: 0, count: 1, value: 100, scale: 100, plot);
        Assert.AreEqual(plot.Y, full.Y, 0.001);

        // value = half scale -> middle
        ChartPoint half = ChartMath.Point(index: 0, count: 1, value: 50, scale: 100, plot);
        Assert.AreEqual(plot.Bottom - 100.0, half.Y, 0.001);

        // Clamping: values above scale or below 0 clamp properly
        ChartPoint over = ChartMath.Point(index: 0, count: 1, value: 150, scale: 100, plot);
        Assert.AreEqual(plot.Y, over.Y, 0.001);

        ChartPoint under = ChartMath.Point(index: 0, count: 1, value: -20, scale: 100, plot);
        Assert.AreEqual(plot.Bottom, under.Y, 0.001);
    }

    [TestMethod]
    public void GridLines_Produce5VerticalAnd3HorizontalLines_SnappedToPen()
    {
        var plot = new ChartRect(0.5, 0.5, 600.0, 400.0);

        IReadOnlyList<double> gridX = ChartMath.GridX(plot);
        Assert.AreEqual(5, gridX.Count, "6 columns have 5 inner vertical lines");
        for (int i = 1; i < 6; i++)
        {
            double expected = Math.Round(plot.X + plot.Width * i / 6.0) + 0.5;
            Assert.AreEqual(expected, gridX[i - 1]);
        }

        IReadOnlyList<double> gridY = ChartMath.GridY(plot);
        Assert.AreEqual(3, gridY.Count, "4 rows have 3 inner horizontal lines");
        for (int i = 1; i < 4; i++)
        {
            double expected = Math.Round(plot.Y + plot.Height * i / 4.0) + 0.5;
            Assert.AreEqual(expected, gridY[i - 1]);
        }
    }

    [TestMethod]
    public void FillColour_MixesTowardsBackground_062OnDark_072OnLight()
    {
        Colour accent = Accents.Cpu; // #3b82f6
        Colour darkPlot = Colour.FromHex("#0f172a"); // IsDarkPlot = true
        Colour lightPlot = Colour.FromHex("#ffffff"); // IsDarkPlot = false

        Colour darkFill = ChartMath.FillColour(accent, darkPlot);
        Colour expectedDark = accent.Mix(Colour.FromHex("#0f172a"), 0.62);
        Assert.AreEqual(expectedDark, darkFill);

        Colour lightFill = ChartMath.FillColour(accent, lightPlot);
        Colour expectedLight = accent.Mix(Colour.FromHex("#ffffff"), 0.72);
        Assert.AreEqual(expectedLight, lightFill);
    }

    [TestMethod]
    public void Ceiling_FixedMaximum_Returns100Percent()
    {
        Assert.AreEqual("100%", ChartMath.Ceiling(maximum: 100, seriesMax: 45.0, unit: "%"));
        Assert.AreEqual("100%", ChartMath.Ceiling(maximum: 1, seriesMax: 0.5, unit: "GB"));
    }

    [TestMethod]
    public void Ceiling_AutoMaximum_FormatsSeriesMaxTimesOneAndQuarter()
    {
        // 80 * 1.25 = 100 -> >= 100 formatted with F0 -> "100 MB/s"
        Assert.AreEqual("100 MB/s", ChartMath.Ceiling(maximum: 0, seriesMax: 80.0, unit: "MB/s"));

        // 10 * 1.25 = 12.5 -> < 100 formatted with F1 -> "12.5 MB/s"
        Assert.AreEqual("12.5 MB/s", ChartMath.Ceiling(maximum: 0, seriesMax: 10.0, unit: "MB/s"));

        // Zero seriesMax -> floor 0.001 -> < 100 formatted with F1 -> "0.0 MB/s"
        Assert.AreEqual("0.0 MB/s", ChartMath.Ceiling(maximum: 0, seriesMax: 0.0, unit: "MB/s"));
    }

    [TestMethod]
    public void HistoryRing_72Capacity_PreservesOldestFirstOrder_AndCalculatesMax()
    {
        var history = new History(72);
        Assert.AreEqual(72, history.Capacity);
        Assert.AreEqual(0, history.Count);

        // Add 10 points: 1..10
        for (int i = 1; i <= 10; i++)
        {
            history.Add(i * 10);
        }
        Assert.AreEqual(10, history.Count);
        Assert.AreEqual(10, history.Revision);
        Assert.AreEqual(100.0, history.Latest);
        Assert.AreEqual(100.0, history.Max);

        double[] buffer10 = new double[10];
        int copied10 = history.CopyTo(buffer10);
        Assert.AreEqual(10, copied10);
        Assert.AreEqual(10.0, buffer10[0], "First copied must be oldest");
        Assert.AreEqual(100.0, buffer10[9], "Last copied must be newest");

        // Now push beyond 72 points to test circular overwriting
        for (int i = 11; i <= 100; i++)
        {
            history.Add(i);
        }
        Assert.AreEqual(72, history.Count);
        Assert.AreEqual(100, history.Revision);
        Assert.AreEqual(100.0, history.Latest);
        Assert.AreEqual(100.0, history.Max);

        double[] buffer72 = new double[72];
        int copied72 = history.CopyTo(buffer72);
        Assert.AreEqual(72, copied72);
        // The last 72 readings pushed were 29..100
        Assert.AreEqual(29.0, buffer72[0], "Oldest in ring should be 29");
        Assert.AreEqual(100.0, buffer72[71], "Newest in ring should be 100");
    }
}