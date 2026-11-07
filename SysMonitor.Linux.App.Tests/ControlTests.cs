using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SysMonitor.Linux.App;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.App.Tests;

[TestClass]
[DoNotParallelize]
public class ControlTests
{
    [TestMethod]
    [DataRow("#12aBcD", (byte)255, (byte)18, (byte)171, (byte)205)]
    [DataRow("#8012abcd", (byte)128, (byte)18, (byte)171, (byte)205)]
    [DataRow("#00000000", (byte)0, (byte)0, (byte)0, (byte)0)]
    [DataRow(null, (byte)255, (byte)128, (byte)128, (byte)128)]
    [DataRow("", (byte)255, (byte)128, (byte)128, (byte)128)]
    [DataRow(" \t\r\n", (byte)255, (byte)128, (byte)128, (byte)128)]
    [DataRow("#gg1122", (byte)255, (byte)128, (byte)128, (byte)128)]
    [DataRow("#12345", (byte)255, (byte)128, (byte)128, (byte)128)]
    [DataRow("definitely-not-a-colour", (byte)255, (byte)128, (byte)128, (byte)128)]
    public void HexBrush_ParsesRgbAndArgb_OrReturnsOpaqueGray(string? text, byte a, byte r, byte g, byte b) => Ui.Run(() =>
    {
        IBrush brush = HexBrush.Parse(text);
        Assert.IsNotNull(brush);
        Assert.AreEqual(Color.FromArgb(a, r, g, b), TestVisuals.BrushColor(brush));
    });

    [TestMethod]
    public void HistoryChart_DefaultsMatchSpecification() => Ui.Run(() =>
    {
        var chart = new HistoryChart();
        Assert.IsNull(chart.Series);
        Assert.AreEqual("#3b82f6", chart.LineColor);
        Assert.AreEqual("#f3f5f9", chart.BackgroundColor);
        Assert.AreEqual("#c8cdd6", chart.GridColor);
        Assert.IsNull(chart.FixedMax);
        Assert.AreEqual(0, chart.Revision);
    });

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public void HistoryChart_NullOrSinglePoint_PaintsBackgroundAtCentre_WithoutPolyline(int count) => Ui.Run(() =>
    {
        var chart = NewChart();
        if (count == 1)
        {
            chart.Series = new History(8);
            chart.Series.Add(30);
        }
        var w = ChartWindow(chart);
        try
        {
            w.Show();
            TestVisuals.Pump(w);
            var frame = TestVisuals.Capture(w);
            Assert.AreEqual(Color.Parse(chart.BackgroundColor), frame.At(frame.Width / 2, frame.Height / 2));
            Assert.AreEqual(0, frame.Count(Color.Parse(chart.LineColor)));
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void HistoryChart_TwoPoints_DrawAtLeastOneExactLineColorPixel() => Ui.Run(() =>
    {
        var chart = NewChart();
        chart.Series = Series(25, 75);
        chart.FixedMax = 100;
        var w = ChartWindow(chart);
        try
        {
            w.Show();
            TestVisuals.Pump(w);
            var frame = TestVisuals.Capture(w);
            Assert.IsTrue(frame.Count(Color.Parse(chart.LineColor)) > 0);
            // Ascending values must put the newest (right-hand) end higher on the screen.
            Assert.IsTrue(LineY(frame, Color.Parse(chart.LineColor), 0.8) < LineY(frame, Color.Parse(chart.LineColor), 0.2));
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void HistoryChart_ZeroSize_RenderDoesNotThrow() => Ui.Run(() =>
    {
        var chart = NewChart();
        chart.Width = 0;
        chart.Height = 0;
        chart.Series = Series(10, 90);
        var w = ChartWindow(chart);
        try
        {
            w.Show();
            TestVisuals.Pump(w);
            chart.Measure(new Size(0, 0));
            chart.Arrange(new Rect(0, 0, 0, 0));
            Assert.AreEqual(0d, chart.Bounds.Width);
            Assert.AreEqual(0d, chart.Bounds.Height);
            // Call Render explicitly: a zero-sized visual could otherwise be culled by the compositor.
            var drawing = new DrawingGroup();
            using (var context = drawing.Open()) chart.Render(context);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void HistoryChart_FixedMaxOverridesAutoMaximum_AndClampsEveryPoint() => Ui.Run(() =>
    {
        var chart = NewChart();
        chart.Series = Series(25, 50);
        var w = ChartWindow(chart);
        try
        {
            w.Show();
            TestVisuals.Pump(w);
            var auto = TestVisuals.Capture(w);
            var autoY = LineY(auto, Color.Parse(chart.LineColor), 0.5);
            Assert.AreEqual(auto.Height * 0.25, autoY, 3d);
            chart.FixedMax = 100;
            TestVisuals.Pump(w);
            var fixedFrame = TestVisuals.Capture(w);
            var fixedY = LineY(fixedFrame, Color.Parse(chart.LineColor), 0.5);
            Assert.AreEqual(fixedFrame.Height * 0.625, fixedY, 3d);
            Assert.IsTrue(fixedY > autoY + 20);
            // Both samples exceed the limit, including the first (review ruling F-06).
            chart.FixedMax = 10;
            TestVisuals.Pump(w);
            var clamped = TestVisuals.Capture(w);
            Assert.IsTrue(LineY(clamped, Color.Parse(chart.LineColor), 0.2) <= 2);
            Assert.IsTrue(LineY(clamped, Color.Parse(chart.LineColor), 0.8) <= 2);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(0d)]
    [DataRow(-4d)]
    public void HistoryChart_InvalidFixedMax_UsesAutoScale(double invalidMax) => Ui.Run(() =>
    {
        var chart = NewChart();
        chart.Series = Series(25, 50);
        chart.FixedMax = invalidMax;
        var w = ChartWindow(chart);
        try
        {
            w.Show();
            TestVisuals.Pump(w);
            var frame = TestVisuals.Capture(w);
            Assert.AreEqual(frame.Height * 0.25, LineY(frame, Color.Parse(chart.LineColor), 0.5), 3d);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void HistoryChart_AutoScaleHasMinimumOfOne() => Ui.Run(() =>
    {
        var chart = NewChart();
        chart.Series = Series(0.25, 0.5);
        var w = ChartWindow(chart);
        try
        {
            w.Show();
            TestVisuals.Pump(w);
            var frame = TestVisuals.Capture(w);
            Assert.AreEqual(frame.Height * 0.625, LineY(frame, Color.Parse(chart.LineColor), 0.5), 3d);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(-25d)]
    public void HistoryChart_NonFiniteAndNegativeFirstSample_DrawsAsZero(double first) => Ui.Run(() =>
    {
        var chart = NewChart();
        chart.Series = Series(first, 50);
        chart.FixedMax = 100;
        var w = ChartWindow(chart);
        try
        {
            w.Show();
            TestVisuals.Pump(w);
            var frame = TestVisuals.Capture(w);
            Assert.AreEqual(frame.Height * 0.9, LineY(frame, Color.Parse(chart.LineColor), 0.2), 3d);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void HistoryChart_ClipsLineStrokeToItsBounds() => Ui.Run(() =>
    {
        var chart = NewChart();
        chart.Margin = new Thickness(30);
        chart.Series = Series(200, 300);
        chart.FixedMax = 100;
        var w = ChartWindow(chart);
        try
        {
            w.Show();
            TestVisuals.Pump(w);
            var frame = TestVisuals.Capture(w);
            var rect = TestVisuals.RectInWindow(chart, w);
            Assert.IsTrue(frame.Count(Color.Parse(chart.LineColor)) > 0, "Clipping must retain the in-bounds part of the line");
            for (var x = (int)rect.Left; x < (int)rect.Right; x++)
                Assert.AreEqual(Colors.Black, frame.At(x, (int)rect.Top - 1));
            for (var y = (int)rect.Top; y < (int)rect.Bottom; y++)
            {
                Assert.AreEqual(Colors.Black, frame.At((int)rect.Left - 1, y));
                Assert.AreEqual(Colors.Black, frame.At((int)rect.Right, y));
            }
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void HistoryChart_PropertyChangesAndRevision_RepaintExistingVisual() => Ui.Run(() =>
    {
        var chart = NewChart();
        var w = ChartWindow(chart);
        try
        {
            w.Show();
            TestVisuals.Pump(w);
            var before = TestVisuals.Capture(w);
            chart.BackgroundColor = "#314159";
            TestVisuals.Pump(w);
            var background = TestVisuals.Capture(w);
            Assert.AreEqual(Color.Parse("#314159"), background.At(background.Width / 2, background.Height / 2));
            Assert.AreNotEqual(before.At(before.Width / 2, before.Height / 2), background.At(background.Width / 2, background.Height / 2));
            chart.Series = Series(25, 50);
            chart.FixedMax = 100;
            TestVisuals.Pump(w);
            Assert.IsTrue(TestVisuals.Capture(w).Count(Color.Parse(chart.LineColor)) > 0);
            var oldLine = Color.Parse(chart.LineColor);
            chart.LineColor = "#00ef7b";
            TestVisuals.Pump(w);
            var recolored = TestVisuals.Capture(w);
            Assert.AreEqual(0, recolored.Count(oldLine));
            Assert.IsTrue(recolored.Count(Color.Parse(chart.LineColor)) > 0);
            chart.Series.Add(100);
            chart.Revision++;
            TestVisuals.Pump(w);
            var revised = TestVisuals.Capture(w);
            Assert.IsTrue(LineY(revised, Color.Parse(chart.LineColor), 0.8) < LineY(recolored, Color.Parse(chart.LineColor), 0.8) - 10);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void HistoryChart_GridColorChange_RepaintsFourHorizontalGridLines() => Ui.Run(() =>
    {
        var chart = NewChart();
        chart.BackgroundColor = "#000000";
        chart.GridColor = "#ffffff";
        var w = ChartWindow(chart);
        try
        {
            w.Show();
            TestVisuals.Pump(w);
            var whiteGrid = TestVisuals.Capture(w);
            var rows = Enumerable.Range(0, whiteGrid.Height)
                .Where(y => whiteGrid.At(whiteGrid.Width / 2, y).R > 0).ToArray();
            Assert.IsTrue(rows.Length > 0);
            var groups = rows.Where((y, i) => i == 0 || y > rows[i - 1] + 1).Count();
            Assert.AreEqual(4, groups);
            foreach (var y in rows)
                Assert.AreEqual(whiteGrid.At(whiteGrid.Width / 4, y), whiteGrid.At(whiteGrid.Width * 3 / 4, y));
            chart.GridColor = "#ff0000";
            TestVisuals.Pump(w);
            var redGrid = TestVisuals.Capture(w);
            foreach (var y in rows)
            {
                var original = whiteGrid.At(whiteGrid.Width / 2, y);
                var updated = redGrid.At(redGrid.Width / 2, y);
                Assert.IsTrue(updated.R > 0);
                Assert.AreEqual((byte)0, updated.G);
                Assert.AreEqual((byte)0, updated.B);
                Assert.AreEqual(original.R, updated.R);
            }
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void MeterCard_BarTracksUsage_AndTextFillAndAccentFollowMetric() => Ui.Run(() =>
    {
        var vm = new MainViewModel(new AppSettings { Mode = "expanded", Opacity = 1 });
        var w = new MainWindow(vm, null, null);
        try
        {
            w.Show();
            vm.Apply(new Snapshot
            {
                CpuTotal = 25, Ram = new Ram { Usage = 40, TotalGb = 16, UsedGb = 6.4 },
                Cores = new[] { new Core { Usage = 25 }, new Core { Usage = 50 } },
                Disks = new[] { new Disk { Letter = "/", TotalGb = 100 }, new Disk { Letter = "/data", TotalGb = 200 } },
                Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 1000 } }
            }, 100);
            TestVisuals.Pump(w);
            var card = TestVisuals.Named<Control>(w, "ExpandedHost").GetVisualDescendants().OfType<MeterCard>().First();
            foreach (var usage in new[] { 0, 25, 50, 100 })
            {
                var item = new MetricItem(MetricKind.Cpu, "cpu", "Card title", "Card subtitle", usage,
                    "Value " + usage, "#12ab34", "#cd762a");
                card.DataContext = item;
                TestVisuals.Pump(w);
                var track = TestVisuals.Named<Control>(card, "BarTrack");
                var fill = TestVisuals.Named<Control>(card, "BarFill");
                Assert.IsTrue(track.Bounds.Width > 0);
                Assert.AreEqual(track.Bounds.Width * usage / 100d, fill.Bounds.Width, 1d);
                Assert.AreEqual(Color.Parse(item.ColorHex), TestVisuals.BrushColor(TestVisuals.Paint(fill)));
                Assert.AreEqual(Color.Parse(vm.Colors.BarEmpty), TestVisuals.BrushColor(TestVisuals.Paint(track)));
                var texts = card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                CollectionAssert.Contains(texts, item.Title);
                CollectionAssert.Contains(texts, item.Subtitle);
                CollectionAssert.Contains(texts, item.ValueText);
                Assert.IsTrue(card.GetVisualDescendants().OfType<Control>().Any(c =>
                    TestVisuals.Paint(c) is ISolidColorBrush brush && brush.Color == Color.Parse(item.AccentHex)));
            }
            card.DataContext = new MetricItem(MetricKind.Cpu, "cpu", "Updated title", "Updated subtitle", 50,
                "Updated value", "#4455cc", "#ff8800");
            TestVisuals.Pump(w);
            Assert.AreEqual(Color.Parse("#4455cc"), TestVisuals.BrushColor(TestVisuals.Paint(TestVisuals.Named<Control>(card, "BarFill"))));
            vm.IsDark = true;
            TestVisuals.Pump(w);
            Assert.AreEqual(Color.Parse(vm.Colors.BarEmpty), TestVisuals.BrushColor(TestVisuals.Paint(TestVisuals.Named<Control>(card, "BarTrack"))));
        }
        finally { w.Close(); Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light; }
    });

    private static HistoryChart NewChart() => new()
    {
        BackgroundColor = "#102030", LineColor = "#fa0080", GridColor = "#607080"
    };

    private static History Series(params double[] values)
    {
        var history = new History(16);
        foreach (var value in values) history.Add(value);
        return history;
    }

    private static Window ChartWindow(HistoryChart chart) => new()
    {
        Width = 240, Height = 120, Background = Brushes.Black, Content = chart,
        CanResize = false
    };

    private static double LineY(TestVisuals.Pixels pixels, Color color, double fraction)
    {
        var middle = (int)Math.Round((pixels.Width - 1) * fraction);
        var rows = (from x in Enumerable.Range(Math.Max(0, middle - 2), 5).Where(x => x < pixels.Width)
                    from y in Enumerable.Range(0, pixels.Height)
                    where pixels.At(x, y) == color
                    select (double)y).ToArray();
        Assert.IsTrue(rows.Length > 0, "No exact line pixels near x=" + middle);
        return rows.Average();
    }
}

// Shared helpers only; the existing test project's Ui/TestAppBuilder are intentionally reused.
internal static class TestVisuals
{
    internal static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    internal static T Named<T>(Visual root, string name) where T : Control
    {
        var matches = root.GetVisualDescendants().Prepend(root).OfType<T>().Where(c => c.Name == name).ToArray();
        Assert.AreEqual(1, matches.Length, "Expected exactly one " + typeof(T).Name + " named " + name);
        return matches[0];
    }

    internal static Rect RectInWindow(Control control, Window window)
    {
        var point = control.TranslatePoint(new Point(0, 0), window);
        Assert.IsTrue(point.HasValue, "Control is not attached to the window");
        return new Rect(point!.Value, control.Bounds.Size);
    }

    internal static IBrush? Paint(Control control) => control switch
    {
        Border border => border.Background,
        Panel panel => panel.Background,
        Shape shape => shape.Fill,
        Avalonia.Controls.Primitives.TemplatedControl templated => templated.Background,
        _ => null
    };

    internal static Color BrushColor(IBrush? brush)
    {
        Assert.IsTrue(brush is ISolidColorBrush, "Expected a solid colour brush");
        return ((ISolidColorBrush)brush!).Color;
    }

    internal static Pixels Capture(Window window)
    {
        Pump(window);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.IsNotNull(bitmap, "Headless Skia must return a rendered frame");
        using var framebuffer = bitmap!.Lock();
        var bgra = framebuffer.Format == PixelFormat.Bgra8888;
        Assert.IsTrue(bgra || framebuffer.Format == PixelFormat.Rgba8888, "Expected a 32-bit BGRA or RGBA capture");
        var bytes = new byte[checked(framebuffer.RowBytes * framebuffer.Size.Height)];
        Marshal.Copy(framebuffer.Address, bytes, 0, bytes.Length);
        return new Pixels(bytes, framebuffer.Size.Width, framebuffer.Size.Height, framebuffer.RowBytes, bgra);
    }

    internal sealed class Pixels(byte[] bytes, int width, int height, int stride, bool bgra)
    {
        internal int Width { get; } = width;
        internal int Height { get; } = height;

        internal Color At(int x, int y)
        {
            Assert.IsTrue(x >= 0 && x < Width && y >= 0 && y < Height, "Pixel outside frame");
            var offset = y * stride + x * 4;
            return Color.FromArgb(bytes[offset + 3], bytes[offset + (bgra ? 2 : 0)], bytes[offset + 1], bytes[offset + (bgra ? 0 : 2)]);
        }

        internal int Count(Color color)
        {
            var count = 0;
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                    if (At(x, y) == color) count++;
            return count;
        }
    }
}
