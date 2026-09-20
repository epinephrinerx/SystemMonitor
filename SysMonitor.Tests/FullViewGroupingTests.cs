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
