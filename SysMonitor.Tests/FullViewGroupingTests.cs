using System.Windows;
using SysMonitor.Model;
using SysMonitor.ViewModels;

namespace SysMonitor.Tests;

/// <summary>
/// The full-screen view groups its graphs by device kind rather than dropping
/// them all into one flat wrap of cards.
/// </summary>
[TestClass]
public class FullViewGroupingTests
{
    private static Snapshot Sample(int cores = 4, int disks = 2, int adapters = 1) => new()
    {
        Ready = true,
        CpuTotal = 25,
        CpuTemp = 52,
        Cores = Enumerable.Range(0, cores)
            .Select(i => new Core { Usage = 10 + i, Temp = 52 }).ToList(),
        Ram = new Ram { Usage = 60, UsedGb = 19.2, TotalGb = 32 },
        Disks = Enumerable.Range(0, disks)
            .Select(i => new Disk { Letter = ((char)('C' + i)).ToString(), Usage = 40 + i })
            .ToList(),
        Adapters = Enumerable.Range(0, adapters)
            .Select(i => new Adapter { Id = "nic" + i, Name = "Wi-Fi " + i, SpeedMbps = 1000 })
            .ToList(),
    };

    private static WidgetViewModel Model(AppConfig? config = null)
    {
        config ??= new AppConfig();
        config.Lang = "en";
        return new WidgetViewModel(config);
    }

    [TestMethod]
    public void Graphs_are_grouped_by_device_kind()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample());

        CollectionAssert.AreEqual(new[] { "CPU", "Memory", "Disk Storage", "Network" },
            model.Groups.Select(g => g.Title).ToArray(),
            "the full view should read CPU, memory, disks, network");
    }

    [TestMethod]
    public void The_cpu_group_holds_the_package_and_every_core()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(cores: 8));

        ChartGroup cpu = model.Groups.First(g => g.Title == "CPU");
        Assert.AreEqual(9, cpu.Cards.Count, "one package graph plus eight cores");
        Assert.IsFalse(cpu.Cards[0].Small, "the package graph is the large one");
        Assert.IsTrue(cpu.Cards.Skip(1).All(c => c.Small),
            "per-core graphs are the small ones, as in Task Manager");
    }

    [TestMethod]
    public void Combined_cpu_mode_drops_the_per_core_graphs()
    {
        WidgetViewModel model = Model(new AppConfig { CpuMode = "total" });
        model.PushHistory(Sample(cores: 8));

        Assert.AreEqual(1, model.Groups.First(g => g.Title == "CPU").Cards.Count);
    }

    [TestMethod]
    public void A_drive_gets_both_a_usage_graph_and_a_throughput_graph()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 3));

        ChartGroup disk = model.Groups.First(g => g.Title == "Disk Storage");
        Assert.AreEqual(6, disk.Cards.Count);
        Assert.AreEqual(3, disk.Cards.Count(c => c.Maximum == 100), "usage is a percentage");
        Assert.AreEqual(3, disk.Cards.Count(c => c.Maximum == 0),
            "throughput has no ceiling and scales to its own peak");
    }

    [TestMethod]
    public void A_hidden_section_leaves_no_group_behind()
    {
        var config = new AppConfig();
        WidgetViewModel model = Model(config);
        model.PushHistory(Sample());
        Assert.IsTrue(model.Groups.Any(g => g.Title == "Network"));

        config.ShowNetwork = false;
        model.PushHistory(Sample());

        Assert.IsFalse(model.Groups.Any(g => g.Title == "Network"));
    }

    [TestMethod]
    public void A_graph_keeps_its_history_when_the_list_around_it_changes()
    {
        WidgetViewModel model = Model();
        for (int i = 0; i < 5; i++)
        {
            model.PushHistory(Sample(disks: 2));
        }
        ChartCard cpu = model.Groups.First(g => g.Title == "CPU").Cards[0];
        Assert.AreEqual(5, cpu.Series.Count);

        // A drive disappears; the CPU graph must not restart.
        model.PushHistory(Sample(disks: 1));

        Assert.AreSame(cpu, model.Groups.First(g => g.Title == "CPU").Cards[0]);
        Assert.AreEqual(6, cpu.Series.Count);
    }

    [TestMethod]
    public void A_percentage_graph_is_labelled_against_a_fixed_ceiling()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample());

        ChartCard cpu = model.Groups.First(g => g.Title == "CPU").Cards[0];
        Assert.AreEqual("100%", cpu.Ceiling);
        Assert.AreEqual("% utilisation", cpu.Unit);
    }

    [TestMethod]
    public void A_throughput_graph_reports_the_peak_it_is_scaled_to()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample());

        ChartCard io = model.Groups.First(g => g.Title == "Disk Storage")
                            .Cards.First(c => c.Maximum == 0);
        Assert.AreEqual("MB/s", io.Unit);
        Assert.IsTrue(io.Ceiling.EndsWith("MB/s"), io.Ceiling);
    }
}

/// <summary>
/// The expanded view's column count, which is not one rule for every section:
/// a core is a label and a bar, a drive carries a line of detail under it.
/// </summary>
[TestClass]
public class ExpandedColumnTests
{
    private static Snapshot Sample() => new()
    {
        Ready = true,
        CpuTotal = 25,
        Cores = Enumerable.Range(0, 16).Select(i => new Core { Usage = 10 }).ToList(),
        Ram = new Ram { Usage = 60, UsedGb = 19.2, TotalGb = 32 },
        Disks = new[] { "C", "D", "E", "F" }
            .Select(letter => new Disk
            {
                Letter = letter,
                Usage = 40,
                Media = "SSD",
                Bus = "NVMe",
                UsedGb = 183,
                TotalGb = 231,
            }).ToList(),
        Adapters = new[]
        {
            new Adapter { Id = "nic", Name = "Wi-Fi", SpeedMbps = 1000 },
            new Adapter { Id = "eth", Name = "Ethernet", SpeedMbps = 1000 },
        },
        Modules = new[]
        {
            new Module { Slot = "DIMM 0", Gb = 16, Kind = "DDR5", Mhz = 5600 },
            new Module { Slot = "DIMM 1", Gb = 16, Kind = "DDR5", Mhz = 5600 },
        },
    };

    private static WidgetViewModel Model(AppConfig? config = null)
    {
        config ??= new AppConfig();
        config.Lang = "en";
        var model = new WidgetViewModel(config);
        model.UpdateExpanded(Sample());
        return model;
    }

    private static Section Named(WidgetViewModel model, string title) =>
        model.Sections.First(s => s.Title.StartsWith(title, StringComparison.Ordinal));

    [TestMethod]
    public void Cores_sit_two_to_a_line()
    {
        Assert.AreEqual(2, Named(Model(), "CPU").Columns);
    }

    [TestMethod]
    public void Drives_run_down_the_page()
    {
        // Media, bus, capacity and both I/O rates do not fit at half width.
        Assert.AreEqual(1, Named(Model(), "Disk Storage").Columns);
    }

    [TestMethod]
    public void Adapters_run_down_the_page_too()
    {
        WidgetViewModel model = Model();
        Assert.AreEqual(1, Named(model, "Network").Columns);
    }

    [TestMethod]
    public void Combined_cpu_mode_is_a_single_row_not_a_pair()
    {
        WidgetViewModel model = Model(new AppConfig { CpuMode = "total" });
        Assert.AreEqual(1, Named(model, "CPU").Columns);
    }
}

/// <summary>
/// Dragging an edge or a corner. Resizing is the part the user reported
/// broken twice, so the arithmetic is pinned rather than trusted.
/// </summary>
[TestClass]
public class WindowGeometryTests
{
    private const double Pad = WindowGeometry.ShadowPad * 2;
    private static readonly Rect Origin = new(100, 200, 500, 400);

    private static Rect Drag(Edge edge, double dx, double dy, bool expanded = true)
    {
        (var min, var max) = WindowGeometry.Limits(expanded);
        return WindowGeometry.Resize(Origin, edge, new Vector(dx, dy), min, max);
    }

    // ------------------------------------------------------------- hit test
    [TestMethod]
    public void Every_edge_is_grabbable()
    {
        Assert.AreEqual(Edge.Left, WindowGeometry.HitTest(new Point(2, 200), 500, 400));
        Assert.AreEqual(Edge.Right, WindowGeometry.HitTest(new Point(498, 200), 500, 400));
        Assert.AreEqual(Edge.Top, WindowGeometry.HitTest(new Point(250, 2), 500, 400));
        Assert.AreEqual(Edge.Bottom, WindowGeometry.HitTest(new Point(250, 398), 500, 400));
    }

    [TestMethod]
    public void Every_corner_claims_both_its_edges()
    {
        Assert.AreEqual(Edge.Left | Edge.Top,
            WindowGeometry.HitTest(new Point(4, 4), 500, 400));
        Assert.AreEqual(Edge.Right | Edge.Top,
            WindowGeometry.HitTest(new Point(496, 4), 500, 400));
        Assert.AreEqual(Edge.Left | Edge.Bottom,
            WindowGeometry.HitTest(new Point(4, 396), 500, 400));
        Assert.AreEqual(Edge.Right | Edge.Bottom,
            WindowGeometry.HitTest(new Point(496, 396), 500, 400));
    }

    [TestMethod]
    public void The_middle_of_the_window_is_for_dragging_not_resizing()
    {
        Assert.AreEqual(Edge.None, WindowGeometry.HitTest(new Point(250, 200), 500, 400));
    }

    // --------------------------------------------------------------- resize
    [TestMethod]
    public void Dragging_the_right_edge_leaves_the_left_where_it_was()
    {
        Rect result = Drag(Edge.Right, 60, 0);
        Assert.AreEqual(Origin.Left, result.Left);
        Assert.AreEqual(Origin.Top, result.Top);
        Assert.AreEqual(560, result.Width);
        Assert.AreEqual(Origin.Height, result.Height);
    }

    [TestMethod]
    public void Dragging_the_left_edge_moves_the_window_and_pins_the_right()
    {
        // This is what "remember where it was" is for: the far side must not
        // creep while the near one is dragged.
        Rect result = Drag(Edge.Left, -60, 0);
        Assert.AreEqual(40, result.Left);
        Assert.AreEqual(560, result.Width);
        Assert.AreEqual(Origin.Right, result.Right);
    }

    [TestMethod]
    public void Dragging_the_top_edge_moves_the_window_and_pins_the_bottom()
    {
        Rect result = Drag(Edge.Top, 0, -50);
        Assert.AreEqual(150, result.Top);
        Assert.AreEqual(450, result.Height);
        Assert.AreEqual(Origin.Bottom, result.Bottom);
    }

    [TestMethod]
    public void A_corner_moves_both_axes_at_once()
    {
        Rect result = Drag(Edge.Left | Edge.Top, -40, -30);
        Assert.AreEqual(60, result.Left);
        Assert.AreEqual(170, result.Top);
        Assert.AreEqual(Origin.Right, result.Right);
        Assert.AreEqual(Origin.Bottom, result.Bottom);
    }

    [TestMethod]
    public void A_left_edge_pushed_past_the_minimum_still_pins_the_right()
    {
        // Clamping is where an incremental implementation drifts: the size
        // stops but the window keeps sliding.
        Rect result = Drag(Edge.Left, 5000, 0);
        Assert.AreEqual(AppConfig.MinExp.W + Pad, result.Width);
        Assert.AreEqual(Origin.Right, result.Right,
            "the right edge must not move once the width has stopped shrinking");
    }

    [TestMethod]
    public void A_top_edge_pushed_past_the_minimum_still_pins_the_bottom()
    {
        Rect result = Drag(Edge.Top, 0, 5000);
        Assert.AreEqual(AppConfig.MinExp.H + Pad, result.Height);
        Assert.AreEqual(Origin.Bottom, result.Bottom);
    }

    [TestMethod]
    public void The_mini_strip_stays_capped_while_the_panel_does_not()
    {
        (var miniMin, var miniMax) = WindowGeometry.Limits(expanded: false);
        Assert.AreEqual(AppConfig.MinMini, miniMin);
        Assert.AreEqual(AppConfig.MaxMini, miniMax);

        (_, var expandedMax) = WindowGeometry.Limits(expanded: true);
        Assert.IsTrue(expandedMax.W >= 3840, $"width cap {expandedMax.W} is below 4K");
        Assert.IsTrue(expandedMax.H >= 2160, $"height cap {expandedMax.H} is below 4K");
    }

    [TestMethod]
    public void The_panel_size_excludes_the_shadow_margin()
    {
        // Named for what it checks. Whether MainWindow then writes this to the
        // config is not something this can reach; it takes a window.
        Size panel = WindowGeometry.Panel(654, 504);
        Assert.AreEqual(654 - Pad, panel.Width);
        Assert.AreEqual(504 - Pad, panel.Height);
    }

    [TestMethod]
    public void A_resize_round_trips_through_the_panel_size()
    {
        // What the config stores has to be what restores the same window.
        (var min, var max) = WindowGeometry.Limits(expanded: true);
        Rect window = WindowGeometry.Resize(Origin, Edge.Right | Edge.Bottom,
                                            new Vector(120, 80), min, max);
        Size panel = WindowGeometry.Panel(window.Width, window.Height);

        Assert.AreEqual(window.Width, panel.Width + Pad);
        Assert.AreEqual(window.Height, panel.Height + Pad);
    }
}
