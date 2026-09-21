using SysMonitor.Model;
using SysMonitor.ViewModels;

namespace SysMonitor.Tests;

/// <summary>
/// Mapped shares. They were filtered out at enumeration and never reached the
/// view at all: `LogicalDrives` takes an `includeNetwork` flag that defaults to
/// false, and the sampler was not passing one.
/// </summary>
[TestClass]
public class NetworkDriveTests
{
    private static WidgetViewModel Model() => new(new AppConfig { Lang = "en" });

    private static Disk Local(string letter, int diskNumber) => new()
    {
        Letter = letter,
        Usage = 40,
        UsedGb = 100,
        TotalGb = 250,
        Detail = new DriveDetail
        {
            Letter = letter,
            DiskNumber = diskNumber,
            FileSystem = "NTFS",
            Disk = new PhysicalDisk
            {
                Number = diskNumber,
                Model = "WDS250G3X0C-00SJG0",
                Bus = "NVMe",
                Media = "SSD",
                Bytes = 250_059_350_016,
            },
        },
    };

    private static Disk Share(string letter) => new()
    {
        Letter = letter,
        Usage = 70,
        UsedGb = 700,
        TotalGb = 1000,
        Media = "Network",
        Bus = "SMB",
        IsNetwork = true,
    };

    private static Disk Cloud(string letter) => new()
    {
        Letter = letter,
        Usage = 10,
        UsedGb = 10,
        TotalGb = 100,
    };

    private static Snapshot Sample(params Disk[] disks) => new()
    {
        Ready = true,
        CpuTotal = 10,
        Cores = new[] { new Core { Usage = 10 } },
        Ram = new Ram { Usage = 50, UsedGb = 16, TotalGb = 32 },
        Disks = disks,
    };

    [TestMethod]
    public void Shares_get_their_own_heading()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(Local("C", 0), Share("L"), Share("N"), Share("P")));

        DeviceTab tab = model.Tabs.First(t => t.Key == "disknetwork");

        Assert.AreEqual("Network drives", tab.Title);
        StringAssert.Contains(tab.Detail, "3");
        StringAssert.Contains(tab.Summary, "L:");
        StringAssert.Contains(tab.Summary, "P:");
        Assert.AreEqual(3, tab.Cards.Count, "each share still gets its own graph");
    }

    [TestMethod]
    public void A_share_is_not_counted_as_a_virtual_drive()
    {
        // Both have no physical disk behind them, but Windows itself separates
        // them and so does This PC.
        WidgetViewModel model = Model();
        model.PushHistory(Sample(Share("L"), Cloud("H")));

        Assert.AreEqual(1, model.Tabs.First(t => t.Key == "disknetwork").Cards.Count);
        Assert.AreEqual(1, model.Tabs.First(t => t.Key == "diskvirtual").Cards.Count);
        Assert.AreEqual("H:", model.Tabs.First(t => t.Key == "diskvirtual").Cards[0].Title);
    }

    [TestMethod]
    public void A_share_never_joins_a_physical_disk()
    {
        // A share that happened to carry disk detail must still not be filed
        // under a spindle: it is not on one.
        WidgetViewModel model = Model();
        Disk share = Share("L");
        model.PushHistory(Sample(Local("C", 0), share));

        DeviceTab disk0 = model.Tabs.First(t => t.Key == "disk0");
        CollectionAssert.AreEqual(new[] { "C:" }, disk0.Cards.Select(c => c.Title).ToArray());
    }

    [TestMethod]
    public void The_heading_is_absent_when_nothing_is_mapped()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(Local("C", 0)));

        Assert.IsFalse(model.Tabs.Any(t => t.Key == "disknetwork"));
        Assert.IsFalse(model.Tabs.Any(t => t.Key == "diskvirtual"));
    }

    [TestMethod]
    public void They_are_included_by_default()
    {
        Assert.IsTrue(new AppConfig().IncludeNetwork);
    }
}
