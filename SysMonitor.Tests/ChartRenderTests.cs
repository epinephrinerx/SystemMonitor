using System.IO;
using System.Windows;
using System.Windows.Controls;
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
/// <summary>
/// Rendering a <see cref="Chart"/> offscreen and reading the pixels back. WPF
/// draws perfectly well without a window, so checking what the graph looks
/// like needs no full-screen window over anybody's desktop.
/// </summary>
internal static class ChartPixels
{
    /// <summary>Render a chart and return its pixels, BGRA, row by row.</summary>
    public static byte[] Render(Action<Chart> setup, int width = 200, int height = 80)
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

    /// <summary>The pixel at a point, so a test can name the place it means.</summary>
    public static (byte R, byte G, byte B) At(byte[] pixels, int x, int y, int width)
    {
        int i = (y * width + x) * 4;
        return (pixels[i + 2], pixels[i + 1], pixels[i]);
    }

    /// <summary>How many pixels satisfy a colour test.</summary>
    public static int CountOf(byte[] pixels, Func<(byte R, byte G, byte B), bool> match)
    {
        int count = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (pixels[i + 3] > 0 && match((pixels[i + 2], pixels[i + 1], pixels[i])))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>How many pixels carry the accent's blue, roughly.</summary>
    public static int BluePixels(byte[] pixels)
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
}

[TestClass]
public class ChartRenderTests
{
    [TestMethod]
    public void A_series_draws_something()
    {
        byte[] pixels = ChartPixels.Render(chart =>
        {
            for (int i = 0; i < 40; i++)
            {
                chart.Series ??= new History(ChartCardPoints);
                chart.Series.Add(20 + i);
            }
            chart.Revision = chart.Series!.Revision;
        });

        Assert.IsTrue(ChartPixels.BluePixels(pixels) > 100,
            "a rising series should paint a visible area in the accent colour");
    }

    [TestMethod]
    public void A_busier_series_paints_more_than_a_quiet_one()
    {
        byte[] quiet = ChartPixels.Render(chart =>
        {
            chart.Series = new History(ChartCardPoints);
            for (int i = 0; i < 40; i++)
            {
                chart.Series.Add(5);
            }
            chart.Revision = chart.Series.Revision;
        });

        byte[] busy = ChartPixels.Render(chart =>
        {
            chart.Series = new History(ChartCardPoints);
            for (int i = 0; i < 40; i++)
            {
                chart.Series.Add(95);
            }
            chart.Revision = chart.Series.Revision;
        });

        Assert.IsTrue(ChartPixels.BluePixels(busy) > ChartPixels.BluePixels(quiet) * 3,
            "a series near 100% should fill far more of the box than one near zero");
    }

    [TestMethod]
    public void One_reading_is_not_enough_to_draw_a_line()
    {
        // A single point has no line to it; the grid is all that shows.
        byte[] pixels = ChartPixels.Render(chart =>
        {
            chart.Series = new History(ChartCardPoints);
            chart.Series.Add(50);
            chart.Revision = chart.Series.Revision;
        });

        Assert.AreEqual(0, ChartPixels.BluePixels(pixels));
    }

    [TestMethod]
    public void An_empty_chart_still_draws_its_grid_without_failing()
    {
        byte[] pixels = ChartPixels.Render(chart => chart.Series = null);

        Assert.AreEqual(200 * 80 * 4, pixels.Length);
        Assert.IsTrue(pixels.Any(b => b != 0), "the grid lines should be visible");
    }

    [TestMethod]
    public void A_rate_series_scales_to_its_own_peak()
    {
        // Maximum 0 means "no ceiling": a series topping out at 3 MB/s should
        // still fill the box rather than hugging the floor of a 0-100 scale.
        byte[] scaled = ChartPixels.Render(chart =>
        {
            chart.Maximum = 0;
            chart.Series = new History(ChartCardPoints);
            for (int i = 0; i < 40; i++)
            {
                chart.Series.Add(3);
            }
            chart.Revision = chart.Series.Revision;
        });

        byte[] fixedScale = ChartPixels.Render(chart =>
        {
            chart.Maximum = 100;
            chart.Series = new History(ChartCardPoints);
            for (int i = 0; i < 40; i++)
            {
                chart.Series.Add(3);
            }
            chart.Revision = chart.Series.Revision;
        });

        Assert.IsTrue(ChartPixels.BluePixels(scaled) > ChartPixels.BluePixels(fixedScale) * 3,
            "an unbounded series should use the height available to it");
    }

    private const int ChartCardPoints = 72;
}

/// <summary>
/// Renders the graph at the size the full-screen view uses and saves a PNG, so
/// its look can be compared against Task Manager without opening a window over
/// anyone's desktop.
/// </summary>
[TestClass]
public class ChartAppearanceTests
{
    [DataTestMethod]
    [DataRow("dark")]
    [DataRow("light")]
    public void The_graph_draws_a_framed_plot_and_saves_a_preview(string theme)
    {
        string path = Path.Combine(Path.GetTempPath(),
                                   $"sysmonitor-chart-{theme}.png");
        var random = new Random(7);

        var thread = new Thread(() =>
        {
            bool dark = theme == "dark";
            var panel = new StackPanel
            {
                Background = new SolidColorBrush(dark
                    ? Color.FromRgb(0x1a, 0x24, 0x38) : Color.FromRgb(0xee, 0xf1, 0xf6)),
                Width = 340,
            };

            // Two shapes worth looking at: a busy core and a quiet one.
            foreach (double load in new[] { 55.0, 6.0 })
            {
                var series = new History(72);
                double value = load;
                for (int i = 0; i < 72; i++)
                {
                    value = Math.Clamp(value + (random.NextDouble() - 0.5) * load, 0, 100);
                    series.Add(value);
                }
                panel.Children.Add(new Chart
                {
                    Height = 96,
                    Margin = new Thickness(10, 10, 10, 6),
                    Series = series,
                    Revision = series.Revision,
                    Maximum = 100,
                    Accent = new SolidColorBrush(Color.FromRgb(0x3b, 0x82, 0xf6)),
                    PlotBrush = new SolidColorBrush(dark
                        ? Color.FromRgb(0x0b, 0x12, 0x20) : Colors.White),
                    GridBrush = new SolidColorBrush(dark
                        ? Color.FromRgb(0x22, 0x30, 0x4a) : Color.FromRgb(0xe2, 0xe6, 0xec)),
                    FrameBrush = new SolidColorBrush(dark
                        ? Color.FromRgb(0x2b, 0x37, 0x4b) : Color.FromRgb(0xd5, 0xda, 0xe1)),
                });
            }

            panel.Measure(new Size(340, 240));
            panel.Arrange(new Rect(0, 0, 340, 240));
            panel.UpdateLayout();

            var target = new RenderTargetBitmap(340, 240, 96, 96, PixelFormats.Pbgra32);
            target.Render(panel);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var file = File.Create(path);
            encoder.Save(file);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "render thread hung");

        Assert.IsTrue(File.Exists(path), path);
        Assert.IsTrue(new FileInfo(path).Length > 1000, "the preview should not be blank");

        // The file existing said almost nothing. These are the two things the
        // Task Manager styling is actually made of.
        byte[] pixels = ChartPixels.Render(chart =>
        {
            chart.PlotBrush = new SolidColorBrush(Colors.Black);
            chart.FrameBrush = new SolidColorBrush(Colors.Red);
            chart.GridBrush = new SolidColorBrush(Colors.Lime);
            chart.Series = null;
        }, 120, 60);

        // Counting red pixels was not the assertion it claimed: one
        // horizontal edge of a 120-wide box clears any such total on its own.
        // Each side is asked for by name.
        foreach ((string side, int x, int y) in new[]
                 {
                     ("top", 60, 0), ("bottom", 60, 59),
                     ("left", 0, 30), ("right", 119, 30),
                 })
        {
            (byte r, byte g, byte _) = ChartPixels.At(pixels, x, y, 120);
            Assert.IsTrue(r > 200 && g < 60, $"the {side} edge of the frame is missing");
        }
        Assert.IsTrue(ChartPixels.CountOf(pixels, c => c.G > 200 && c.R < 60) > 60,
            "the grid should be drawn inside the frame");
    }
}
