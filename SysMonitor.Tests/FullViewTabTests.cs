using SysMonitor.Model;
using SysMonitor.ViewModels;

namespace SysMonitor.Tests;

/// <summary>
/// The full view is a tab per device now, not one scrolling page of every
/// graph at once. A machine with sixteen cores, six drives and two adapters
/// put all of it on one page and gave none of it room.
/// </summary>
[TestClass]
public class FullViewTabTests
{
    private static Snapshot Sample(int cores = 4, int disks = 2, int adapters = 1,
                                   bool withDetail = true) => new()
    {
        Ready = true,
        CpuTotal = 25,
        CpuTemp = 52,
        Cores = Enumerable.Range(0, cores)
            .Select(i => new Core { Usage = 10 + i, Temp = 52 }).ToList(),
        Ram = new Ram { Usage = 60, UsedGb = 19.2, TotalGb = 32 },
        Disks = Enumerable.Range(0, disks).Select(i =>
        {
            string letter = ((char)('C' + i)).ToString();
            return new Disk
            {
                Letter = letter,
                Usage = 40 + i,
                UsedGb = 100,
                TotalGb = 250,
                Detail = withDetail ? Detail(letter, i) : null,
            };
        }).ToList(),
        Adapters = Enumerable.Range(0, adapters)
            .Select(i => new Adapter { Id = "nic" + i, Name = "Wi-Fi " + i, SpeedMbps = 1000 })
            .ToList(),
        Modules = new[] { new Module { Slot = "DIMM 0", Gb = 16, Kind = "DDR4", Mhz = 2667 } },
    };

    private static DriveDetail Detail(string letter, int index) => new()
    {
        Letter = letter,
        DiskNumber = index,
        PartitionNumber = 3,
        PartitionBytes = 250_000_000_000,
        IsBoot = index == 0,
        FileSystem = "NTFS",
        Disk = new PhysicalDisk
        {
            Number = index,
            Model = "WDS250G3X0C-00SJG0",
            Serial = "E823_8FA6",
            Firmware = "102000WD",
            Bus = "NVMe",
            Media = "SSD",
            PartitionStyle = "GPT",
            Bytes = 250_059_350_016,
            PartitionCount = 3,
        },
    };

    private static WidgetViewModel Model(AppConfig? config = null)
    {
        config ??= new AppConfig();
        config.Lang = "en";
        return new WidgetViewModel(config);
    }

    [TestMethod]
    public void There_is_a_tab_per_device_in_the_order_of_the_middle_view()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 2, adapters: 1));

        CollectionAssert.AreEqual(new[] { "cpu", "ram", "diskC", "diskD", "netnic0" },
            model.Tabs.Select(t => t.Key).ToArray(),
            "CPU, Memory, then a tab per drive, then the adapters");
    }

    [TestMethod]
    public void Each_logical_disk_gets_its_own_tab()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 4));

        string[] disks = model.Tabs.Where(t => t.Key.StartsWith("disk"))
                                   .Select(t => t.Title).ToArray();
        CollectionAssert.AreEqual(new[] { "C:", "D:", "E:", "F:" }, disks);
    }

    [TestMethod]
    public void A_drive_tab_says_which_physical_disk_it_is_on()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 1));

        DeviceTab tab = model.Tabs.First(t => t.Key == "diskC");
        string facts = string.Join(" | ", tab.Facts.Select(f => $"{f.Name}={f.Value}"));

        StringAssert.Contains(facts, "of disk 0");
        StringAssert.Contains(facts, "WDS250G3X0C-00SJG0");
        StringAssert.Contains(facts, "NTFS");
        StringAssert.Contains(facts, "SSD");
        StringAssert.Contains(facts, "NVMe");
        StringAssert.Contains(facts, "102000WD", "the firmware revision");
        StringAssert.Contains(facts, "E823_8FA6", "the serial");
    }

    [TestMethod]
    public void A_drive_tab_states_the_partition_against_the_whole_disk()
    {
        // "250.0 / 232.9 GB (100%)" reads very differently from a partition
        // that is a quarter of its disk, and the share is the point.
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 1));

        DeviceTab tab = model.Tabs.First(t => t.Key == "diskC");
        string size = tab.Facts.First(f => f.Name == "Partition size").Value;
        StringAssert.Contains(size, "/");
        StringAssert.Contains(size, "%");
    }

    [TestMethod]
    public void A_drive_with_no_physical_disk_behind_it_still_gets_a_tab()
    {
        // A network or virtual drive has no MSFT_Disk to report.
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 1, withDetail: false));

        DeviceTab tab = model.Tabs.First(t => t.Key == "diskC");
        Assert.IsTrue(tab.Facts.Count >= 2, "the letter and its usage are always known");
        Assert.IsFalse(tab.Facts.Any(f => f.Name == "Serial"));
    }

    [TestMethod]
    public void A_drive_tab_graphs_both_its_space_and_its_throughput()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 1));

        DeviceTab tab = model.Tabs.First(t => t.Key == "diskC");
        Assert.AreEqual(2, tab.Cards.Count);
        Assert.AreEqual(1, tab.Cards.Count(c => c.Maximum == 100), "space is a percentage");
        Assert.AreEqual(1, tab.Cards.Count(c => c.Maximum == 0),
            "throughput has no ceiling and scales to its own peak");
    }

    [TestMethod]
    public void The_cpu_tab_holds_the_package_and_every_core()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(cores: 8));

        DeviceTab cpu = model.Tabs.First(t => t.Key == "cpu");
        Assert.AreEqual(9, cpu.Cards.Count, "one package graph plus eight cores");
    }

    [TestMethod]
    public void The_memory_tab_lists_what_is_fitted()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample());

        DeviceTab ram = model.Tabs.First(t => t.Key == "ram");
        Assert.IsTrue(ram.Facts.Any(f => f.Value.Contains("DDR4")));
    }

    [TestMethod]
    public void Something_is_always_selected()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample());

        Assert.AreEqual(1, model.Tabs.Count(t => t.Selected));
        Assert.AreEqual("cpu", model.SelectedTab!.Key, "the CPU is the first tab");
    }

    [TestMethod]
    public void Selecting_a_tab_puts_the_others_away()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 2));

        model.Select("diskD");

        Assert.AreEqual("diskD", model.SelectedTab!.Key);
        Assert.AreEqual(1, model.Tabs.Count(t => t.Selected));
    }

    [TestMethod]
    public void The_selection_survives_a_refresh()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 2));
        model.Select("diskD");

        model.PushHistory(Sample(disks: 2));

        Assert.AreEqual("diskD", model.SelectedTab!.Key);
    }

    [TestMethod]
    public void A_tab_keeps_its_history_when_the_list_around_it_changes()
    {
        WidgetViewModel model = Model();
        for (int i = 0; i < 5; i++)
        {
            model.PushHistory(Sample(disks: 2));
        }
        ChartCard cpu = model.Tabs.First(t => t.Key == "cpu").Cards[0];
        Assert.AreEqual(5, cpu.Series.Count);

        model.PushHistory(Sample(disks: 1));       // a drive goes away

        Assert.AreSame(cpu, model.Tabs.First(t => t.Key == "cpu").Cards[0]);
        Assert.AreEqual(6, cpu.Series.Count);
    }

    [TestMethod]
    public void A_hidden_section_leaves_no_tab_behind()
    {
        var config = new AppConfig();
        WidgetViewModel model = Model(config);
        model.PushHistory(Sample());
        Assert.IsTrue(model.Tabs.Any(t => t.Key.StartsWith("net")));

        config.ShowNetwork = false;
        model.PushHistory(Sample());

        Assert.IsFalse(model.Tabs.Any(t => t.Key.StartsWith("net")));
    }

    [TestMethod]
    public void Every_tab_carries_its_headline_figure()
    {
        // The strip has to report while only one tab is open, or the other
        // devices go unwatched.
        WidgetViewModel model = Model();
        model.PushHistory(Sample());

        foreach (DeviceTab tab in model.Tabs)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(tab.Summary), tab.Key);
        }
    }
}
