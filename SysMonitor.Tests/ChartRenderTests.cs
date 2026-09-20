using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SysMonitor.Controls;
using SysMonitor.Model;

namespace SysMonitor.Tests;

/// <summary>
/// The chart drawn offscreen and read back pixel by pixel.
///
/// WPF renders perfectly well without a window, so checking that the graph
/// actually draws needs no full-screen window over somebody's desktop.
/// </summary>
[TestClass]
public class ChartRenderTests
{
    /// <summary>Render a chart and return its pixels, BGRA, row by row.</summary>
    private static byte[] Render(Action<Chart> setup, int width = 200, int height = 80)
    {
        byte[] pixels = Array.Empty<byte>();
        var thread = new Thread(() =>
        {
            var chart = new Chart
            {
                Width = width,
                Height = height,
                Accent = new SolidColorBrush(Colors.DodgerBlue),
                GridBrush = new SolidColorBrush(Colors.DimGray),
            };
            setup(chart);

            chart.Measure(new Size(width, height));
            chart.Arrange(new Rect(0, 0, width, height));
            chart.UpdateLayout();

            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(chart);

            pixels = new byte[width * height * 4];
            target.CopyPixels(pixels, width * 4, 0);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "render thread hung");
        return pixels;
    }

    /// <summary>How many pixels carry the accent's blue, roughly.</summary>
    private static int BluePixels(byte[] pixels)
    {
        int count = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            byte blue = pixels[i];
            byte green = pixels[i + 1];
            byte red = pixels[i + 2];
            byte alpha = pixels[i + 3];
            if (alpha > 0 && blue > red + 20 && blue > green + 10)
            {
                count++;
            }
        }
        return count;
    }

    [TestMethod]
    public void A_series_draws_something()
    {
        byte[] pixels = Render(chart =>
        {
            for (int i = 0; i < 40; i++)
            {
                chart.Series ??= new History(ChartCardPoints);
                chart.Series.Add(20 + i);
            }
            chart.Revision = chart.Series!.Revision;
        });

        Assert.IsTrue(BluePixels(pixels) > 100,
            "a rising series should paint a visible area in the accent colour");
    }

    [TestMethod]
    public void A_busier_series_paints_more_than_a_quiet_one()
    {
        byte[] quiet = Render(chart =>
        {
            chart.Series = new History(ChartCardPoints);
            for (int i = 0; i < 40; i++)
            {
                chart.Series.Add(5);
            }
            chart.Revision = chart.Series.Revision;
        });

        byte[] busy = Render(chart =>
        {
            chart.Series = new History(ChartCardPoints);
            for (int i = 0; i < 40; i++)
            {
                chart.Series.Add(95);
            }
            chart.Revision = chart.Series.Revision;
        });

        Assert.IsTrue(BluePixels(busy) > BluePixels(quiet) * 3,
            "a series near 100% should fill far more of the box than one near zero");
    }

    [TestMethod]
    public void One_reading_is_not_enough_to_draw_a_line()
    {
        // A single point has no line to it; the grid is all that shows.
        byte[] pixels = Render(chart =>
        {
            chart.Series = new History(ChartCardPoints);
            chart.Series.Add(50);
            chart.Revision = chart.Series.Revision;
        });

        Assert.AreEqual(0, BluePixels(pixels));
    }

    [TestMethod]
    public void An_empty_chart_still_draws_its_grid_without_failing()
    {
        byte[] pixels = Render(chart => chart.Series = null);

        Assert.AreEqual(200 * 80 * 4, pixels.Length);
        Assert.IsTrue(pixels.Any(b => b != 0), "the grid lines should be visible");
    }

    [TestMethod]
    public void A_rate_series_scales_to_its_own_peak()
    {
        // Maximum 0 means "no ceiling": a series topping out at 3 MB/s should
        // still fill the box rather than hugging the floor of a 0-100 scale.
        byte[] scaled = Render(chart =>
        {
            chart.Maximum = 0;
            chart.Series = new History(ChartCardPoints);
            for (int i = 0; i < 40; i++)
            {
                chart.Series.Add(3);
            }
            chart.Revision = chart.Series.Revision;
        });

        byte[] fixedScale = Render(chart =>
        {
            chart.Maximum = 100;
            chart.Series = new History(ChartCardPoints);
            for (int i = 0; i < 40; i++)
            {
                chart.Series.Add(3);
            }
            chart.Revision = chart.Series.Revision;
        });

        Assert.IsTrue(BluePixels(scaled) > BluePixels(fixedScale) * 3,
            "an unbounded series should use the height available to it");
    }

    private const int ChartCardPoints = 72;
}
