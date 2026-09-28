using SysMonitor.Model;
using SysMonitor.ViewModels;
using System.Text.RegularExpressions;

namespace SysMonitor.Tests;

/// <summary>
/// The full view is a tab per device now, not one scrolling page of every
/// graph at once. A machine with sixteen cores, six drives and two adapters
/// put all of it on one page and gave none of it room.
/// </summary>
[TestClass]
public class FullViewTabTests
{
    /// <summary>
    /// A machine to plan tabs from. `onDisk` maps each drive to a physical
    /// disk number; the default puts every drive on its own, and passing
    /// something like [0, 0, 1] is how the grouping gets exercised.
    /// </summary>
    private static Snapshot Sample(int cores = 4, int disks = 2, int adapters = 1,
                                   bool withDetail = true, int? driveTemp = null,
                                   int[]? onDisk = null) => new()
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
                Temp = driveTemp,
                Detail = withDetail
                    ? Detail(letter, onDisk is not null && i < onDisk.Length ? onDisk[i] : i)
                    : null,
            };
        }).ToList(),
        Adapters = Enumerable.Range(0, adapters)
            .Select(i => new Adapter { Id = "nic" + i, Name = "Wi-Fi " + i, SpeedMbps = 1000 })
            .ToList(),
        Modules = new[] { new Module { Slot = "DIMM 0", Gb = 16, Kind = "DDR4", Mhz = 2667 } },
        CpuInfo = new CpuInfo
        {
            Name = "Intel(R) Core(TM) i7-1165G7",
            Vendor = "GenuineIntel",
            Sockets = 1,
            Cores = 4,
            Logical = 8,
            BaseMhz = 2800,
            L2Kb = 5120,
            L3Kb = 12288,
            Virtualization = true,
        },
    };

    private static DriveDetail Detail(string letter, int diskNumber) => new()
    {
        Letter = letter,
        DiskNumber = diskNumber,
        PartitionNumber = 3,
        PartitionBytes = 250_000_000_000,
        IsBoot = diskNumber == 0,
        FileSystem = "NTFS",
        Disk = new PhysicalDisk
        {
            Number = diskNumber,
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

        CollectionAssert.AreEqual(new[] { "cpu", "ram", "disk0", "disk1", "netnic0" },
            model.Tabs.Select(t => t.Key).ToArray(),
            "CPU, RAM, then a tab per physical disk, then the adapters");
    }

    [TestMethod]
    public void The_cpu_tab_says_which_processor_this_is()
    {
        // The tab a person opens first had a core count and a temperature on
        // it and nothing else -- not even the name of the chip.
        WidgetViewModel model = Model();
        model.PushHistory(Sample());

        DeviceTab tab = model.Tabs.First(t => t.Key == "cpu");
        string facts = string.Join(" | ", tab.Facts.Select(f => $"{f.Name}={f.Value}"));

        StringAssert.Contains(tab.Hardware, "i7-1165G7", "the model heads the panel");
        StringAssert.Contains(tab.Detail, "4C/8T", "the shape of the chip, on the rail");
        StringAssert.Contains(tab.Detail, "2.80 GHz", "the rated clock, on the rail");
        StringAssert.Contains(facts, "12 MB", "the L3 cache");
    }

    [TestMethod]
    public void The_cpu_facts_hold_only_what_is_nowhere_else()
    {
        // The rail carries the counts, the clock and the temperature, and the
        // model heads the panel. Anything else in the box is said twice.
        WidgetViewModel model = Model();
        model.PushHistory(Sample());

        DeviceTab tab = model.Tabs.First(t => t.Key == "cpu");
        CollectionAssert.AreEquivalent(
            new[] { "Virtualization", "L2", "L3" },
            tab.Facts.Select(f => f.Name).ToArray());
    }

    [TestMethod]
    public void The_cpu_rail_carries_the_temperature_beside_the_load()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample());

        DeviceTab tab = model.Tabs.First(t => t.Key == "cpu");
        StringAssert.Contains(tab.Summary, "25%");
        StringAssert.Contains(tab.Summary, "52", "the package temperature");
    }

    [TestMethod]
    public void A_single_socket_machine_is_not_told_it_has_one_socket()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample());

        DeviceTab tab = model.Tabs.First(t => t.Key == "cpu");
        Assert.IsFalse(tab.Facts.Any(f => f.Name.Contains("ocket")));
    }

    [TestMethod]
    public void The_memory_tab_is_called_RAM()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample());

        DeviceTab tab = model.Tabs.First(t => t.Key == "ram");
        Assert.AreEqual("RAM", tab.Title);
        StringAssert.Contains(tab.Detail, "GB", "the size sits under the name");
    }

    [TestMethod]
    public void Drives_that_share_a_disk_share_a_tab()
    {
        // Seven letters were seven tabs on this machine; they are three
        // disks. Which letters sit on the same spindle is the thing a person
        // wants to know when one of them is busy.
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 4, onDisk: new[] { 0, 1, 1, 1 }));

        string[] disks = model.Tabs.Where(t => t.Key.StartsWith("disk"))
                                   .Select(t => t.Title).ToArray();
        CollectionAssert.AreEqual(new[] { "Disk 0", "Disk 1" }, disks);

        DeviceTab shared = model.Tabs.First(t => t.Key == "disk1");
        CollectionAssert.AreEqual(new[] { "D:", "E:", "F:" },
            shared.Cards.Select(c => c.Title).ToArray(),
            "one card per drive, in letter order");
    }

    [TestMethod]
    public void A_disk_rail_says_what_the_disk_is()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 1));

        DeviceTab tab = model.Tabs.First(t => t.Key == "disk0");

        Assert.AreEqual("Disk 0", tab.Title);
        StringAssert.Contains(tab.Detail, "SSD (NVMe)");
        StringAssert.Contains(tab.Detail, "GB");
        StringAssert.Contains(tab.Detail, "Online");
        StringAssert.Contains(tab.Summary, "WDS250G3X0C-00SJG0", "the model, on the rail");
        Assert.AreEqual(0, tab.Facts.Count, "the facts box collapses itself");
    }

    [TestMethod]
    public void An_offline_disk_says_so()
    {
        WidgetViewModel model = Model();
        Snapshot snap = Sample(disks: 1);
        snap.Disks[0].Detail!.Disk!.Online = false;
        model.PushHistory(snap);

        StringAssert.Contains(model.Tabs.First(t => t.Key == "disk0").Detail, "Offline");
    }

    [TestMethod]
    public void Unallocated_space_is_named_only_when_there_is_some()
    {
        WidgetViewModel model = Model();
        Snapshot snap = Sample(disks: 1);
        PhysicalDisk physical = snap.Disks[0].Detail!.Disk!;

        // Fully partitioned: saying "0.0 GB unallocated" is noise.
        physical.AllocatedBytes = physical.Bytes;
        model.PushHistory(snap);
        StringAssert.DoesNotMatch(model.Tabs.First(t => t.Key == "disk0").Detail,
                                  new Regex("unallocated"));

        physical.AllocatedBytes = physical.Bytes - 20_000_000_000;
        model = Model();
        model.PushHistory(snap);
        StringAssert.Contains(model.Tabs.First(t => t.Key == "disk0").Detail, "unallocated");
    }

    [TestMethod]
    public void A_drive_card_states_its_size_rather_than_graphing_it()
    {
        // Space in use moves by a gigabyte a week. On a three-minute chart it
        // is a flat line, so the figure is written out and the graph is the
        // throughput, which is the part that actually moves.
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 1));

        ChartCard card = model.Tabs.First(t => t.Key == "disk0").Cards[0];

        Assert.AreEqual("C:", card.Title);
        StringAssert.Contains(card.Detail, "NTFS");
        StringAssert.Contains(card.Detail, "100.0 GB / 250.0 GB");
        StringAssert.Contains(card.Detail, "40%");
        Assert.AreEqual(0, card.Maximum,
            "throughput has no ceiling and scales to its own peak");
    }

    [TestMethod]
    public void A_drive_card_shows_its_temperature_where_there_is_a_sensor()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 1, driveTemp: 41));
        StringAssert.Contains(model.Tabs.First(t => t.Key == "disk0").Cards[0].Detail, "41");

        // A drive with no sensor says nothing rather than filling the line
        // with "n/a", which is most of the drives on a machine with USB disks.
        model = Model();
        model.PushHistory(Sample(disks: 1));
        StringAssert.DoesNotMatch(model.Tabs.First(t => t.Key == "disk0").Cards[0].Detail,
                                  new Regex("n/a"));
    }

    [TestMethod]
    public void Drives_with_no_disk_behind_them_share_one_heading()
    {
        // A cloud filesystem, a mapped share, a subst: real to the user and
        // invisible to the storage stack. They would otherwise vanish.
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 2, withDetail: false));

        DeviceTab tab = model.Tabs.First(t => t.Key == "diskvirtual");

        Assert.AreEqual("Virtual drives", tab.Title);
        StringAssert.Contains(tab.Detail, "2");
        StringAssert.Contains(tab.Summary, "C:");
        StringAssert.Contains(tab.Summary, "D:");
        Assert.AreEqual(2, tab.Cards.Count, "each still gets its own graph");
        Assert.IsFalse(model.Tabs.Any(t => t.Key.StartsWith("disk") && t.Key != "diskvirtual"),
            "and no empty physical disk is invented for them");
    }

    [TestMethod]
    public void The_virtual_heading_appears_only_when_it_has_members()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 2));

        Assert.IsFalse(model.Tabs.Any(t => t.Key == "diskvirtual"));
    }

    [TestMethod]
    public void The_cpu_tab_holds_the_package_and_every_core()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(cores: 8));

        DeviceTab cpu = model.Tabs.First(t => t.Key == "cpu");
        Assert.AreEqual(1, cpu.Cards.Count, "the package graph stands alone");
        Assert.AreEqual(8, cpu.Cores.Count, "one square per logical processor");
    }

    [TestMethod]
    public void A_core_square_is_half_the_height_of_the_package_graph()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(cores: 8));

        DeviceTab cpu = model.Tabs.First(t => t.Key == "cpu");
        ChartCard core = cpu.Cores[0];

        Assert.AreEqual(cpu.Cards[0].CardHeight / 2, core.CardHeight);
        Assert.AreEqual(core.CardHeight, core.CardWidth, "and square");
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

        model.Select("disk1");

        Assert.AreEqual("disk1", model.SelectedTab!.Key);
        Assert.AreEqual(1, model.Tabs.Count(t => t.Selected));
    }

    [TestMethod]
    public void The_selection_survives_a_refresh()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(disks: 2));
        model.Select("disk1");

        model.PushHistory(Sample(disks: 2));

        Assert.AreEqual("disk1", model.SelectedTab!.Key);
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

/// <summary>
/// Restoring the chosen tab. The full view is usually opened before the first
/// sample has landed, so the choice has to survive being asked for at a moment
/// when there were no tabs to put it on.
/// </summary>
[TestClass]
public class TabRestoreTests
{
    private static Snapshot Ready() => new()
    {
        Ready = true,
        CpuTotal = 10,
        Cores = new[] { new Core { Usage = 5 } },
        Ram = new Ram { Usage = 50, UsedGb = 8, TotalGb = 16 },
        Disks = new[] { "C", "D" }.Select(l => new Disk { Letter = l, Usage = 40 }).ToList(),
    };

    private static WidgetViewModel Model(string lastTab)
    {
        var config = new AppConfig { Lang = "en", FullTab = lastTab };
        return new WidgetViewModel(config);
    }

    [TestMethod]
    public void The_tab_from_last_time_is_chosen_when_the_tabs_arrive()
    {
        // These drives carry no disk detail, so they land under the virtual
        // heading -- which is a key like any other and has to be restorable.
        WidgetViewModel model = Model("diskvirtual");

        model.PushHistory(new Snapshot());     // not ready: nothing to select onto
        Assert.IsNull(model.SelectedTab);

        model.PushHistory(Ready());

        Assert.AreEqual("diskvirtual", model.SelectedTab!.Key);
    }

    [TestMethod]
    public void A_tab_that_no_longer_exists_falls_back_to_the_first()
    {
        // The drive it was on has been unplugged since.
        WidgetViewModel model = Model("diskZ");
        model.PushHistory(Ready());

        Assert.AreEqual("cpu", model.SelectedTab!.Key);
    }

    [TestMethod]
    public void With_nothing_remembered_the_first_tab_is_chosen()
    {
        WidgetViewModel model = Model(string.Empty);
        model.PushHistory(Ready());

        Assert.AreEqual("cpu", model.SelectedTab!.Key);
    }
}
