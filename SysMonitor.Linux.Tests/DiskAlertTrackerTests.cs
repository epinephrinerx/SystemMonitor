using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class DiskAlertTrackerTests
{
    private static Disk MakeDisk(
        string letter,
        int usage,
        bool isNetwork = false,
        string media = "SSD") =>
        new()
        {
            Letter = letter,
            Usage = usage,
            TotalGb = 100,
            UsedGb = usage,
            IsNetwork = isNetwork,
            Media = media,
        };

    [TestMethod]
    public void Update_Levels_NoneWarmAndHot()
    {
        var tracker = new DiskAlertTracker();

        // 69% -> None
        tracker.Update(new[] { MakeDisk("/", 69) });
        Assert.AreEqual(DiskAlertLevel.None, tracker.LevelOf("/"));
        Assert.AreEqual(0, tracker.Alerts.Count);

        // 70% -> Warm
        tracker.Update(new[] { MakeDisk("/", 70) });
        Assert.AreEqual(DiskAlertLevel.Warm, tracker.LevelOf("/"));
        Assert.AreEqual(1, tracker.Alerts.Count);
        Assert.IsFalse(tracker.Alerts[0].IsHot);

        // 89% -> Warm
        tracker.Update(new[] { MakeDisk("/", 89) });
        Assert.AreEqual(DiskAlertLevel.Warm, tracker.LevelOf("/"));

        // 90% -> Hot
        tracker.Update(new[] { MakeDisk("/", 90) });
        Assert.AreEqual(DiskAlertLevel.Hot, tracker.LevelOf("/"));
        Assert.AreEqual(1, tracker.Alerts.Count);
        Assert.IsTrue(tracker.Alerts[0].IsHot);
    }

    [TestMethod]
    public void Update_TwoPointHysteresis_OnTheWayDown()
    {
        var tracker = new DiskAlertTracker();

        // Start at 90% -> Hot
        tracker.Update(new[] { MakeDisk("/", 90) });
        Assert.AreEqual(DiskAlertLevel.Hot, tracker.LevelOf("/"));

        // Drops to 89% -> stays Hot (holds down to 88%)
        tracker.Update(new[] { MakeDisk("/", 89) });
        Assert.AreEqual(DiskAlertLevel.Hot, tracker.LevelOf("/"));

        // Drops to 88% -> stays Hot
        tracker.Update(new[] { MakeDisk("/", 88) });
        Assert.AreEqual(DiskAlertLevel.Hot, tracker.LevelOf("/"));

        // Drops to 87% -> falls to Warm!
        tracker.Update(new[] { MakeDisk("/", 87) });
        Assert.AreEqual(DiskAlertLevel.Warm, tracker.LevelOf("/"));

        // Drops to 70% -> stays Warm
        tracker.Update(new[] { MakeDisk("/", 70) });
        Assert.AreEqual(DiskAlertLevel.Warm, tracker.LevelOf("/"));

        // Drops to 69% -> stays Warm (holds down to 68%)
        tracker.Update(new[] { MakeDisk("/", 69) });
        Assert.AreEqual(DiskAlertLevel.Warm, tracker.LevelOf("/"));

        // Drops to 68% -> stays Warm
        tracker.Update(new[] { MakeDisk("/", 68) });
        Assert.AreEqual(DiskAlertLevel.Warm, tracker.LevelOf("/"));

        // Drops to 67% -> falls to None!
        tracker.Update(new[] { MakeDisk("/", 67) });
        Assert.AreEqual(DiskAlertLevel.None, tracker.LevelOf("/"));
        Assert.AreEqual(0, tracker.Alerts.Count);
    }

    [TestMethod]
    public void Dismiss_ClearsAlert_UntilLevelRises()
    {
        var tracker = new DiskAlertTracker();

        // 75% -> Warm alert raised
        tracker.Update(new[] { MakeDisk("/", 75) });
        Assert.AreEqual(1, tracker.Alerts.Count);

        // Dismiss alert
        bool dismissed = tracker.Dismiss("/");
        Assert.IsTrue(dismissed);
        Assert.AreEqual(0, tracker.Alerts.Count);
        Assert.AreEqual(DiskAlertLevel.Warm, tracker.LevelOf("/"));

        // Dismiss again -> returns false
        Assert.IsFalse(tracker.Dismiss("/"));

        // Update at 78% (still Warm) -> stays dismissed
        tracker.Update(new[] { MakeDisk("/", 78) });
        Assert.AreEqual(0, tracker.Alerts.Count);
        Assert.AreEqual(DiskAlertLevel.Warm, tracker.LevelOf("/"));

        // Update at 90% (rises to Hot) -> raises pending alert again!
        tracker.Update(new[] { MakeDisk("/", 90) });
        Assert.AreEqual(1, tracker.Alerts.Count);
        Assert.AreEqual(DiskAlertLevel.Hot, tracker.LevelOf("/"));
        Assert.IsTrue(tracker.Alerts[0].IsHot);
    }

    [TestMethod]
    public void FallToOk_ClearsPendingState()
    {
        var tracker = new DiskAlertTracker();

        // Warm alert
        tracker.Update(new[] { MakeDisk("/", 75) });
        Assert.AreEqual(1, tracker.Alerts.Count);

        // Drop to OK (50%)
        tracker.Update(new[] { MakeDisk("/", 50) });
        Assert.AreEqual(0, tracker.Alerts.Count);
        Assert.AreEqual(DiskAlertLevel.None, tracker.LevelOf("/"));

        // Rise to Warm again -> alerts again without needing Hot
        tracker.Update(new[] { MakeDisk("/", 72) });
        Assert.AreEqual(1, tracker.Alerts.Count);
        Assert.AreEqual(DiskAlertLevel.Warm, tracker.LevelOf("/"));
    }

    [TestMethod]
    public void DisappearingDisk_IsForgotten()
    {
        var tracker = new DiskAlertTracker();

        tracker.Update(new[]
        {
            MakeDisk("/", 60),
            MakeDisk("/mnt/backup", 95),
        });
        Assert.AreEqual(1, tracker.Alerts.Count);
        Assert.AreEqual("/mnt/backup", tracker.Alerts[0].Letter);

        // /mnt/backup unmounted
        tracker.Update(new[]
        {
            MakeDisk("/", 60),
        });
        Assert.AreEqual(0, tracker.Alerts.Count);
        Assert.AreEqual(DiskAlertLevel.None, tracker.LevelOf("/mnt/backup"));
    }

    [TestMethod]
    public void RemovableAndNetworkDisks_AreSkipped()
    {
        var tracker = new DiskAlertTracker();

        tracker.Update(new[]
        {
            MakeDisk("/media/usb", 98, isNetwork: false, media: "Removable"),
            MakeDisk("/mnt/nfs", 95, isNetwork: true, media: "SSD"),
        });

        // Both skipped per spec section 4b
        Assert.AreEqual(0, tracker.Alerts.Count);
        Assert.AreEqual(DiskAlertLevel.None, tracker.LevelOf("/media/usb"));
        Assert.AreEqual(DiskAlertLevel.None, tracker.LevelOf("/mnt/nfs"));
    }

    [TestMethod]
    public void DiskAlert_TextAndColour_MatchSpec()
    {
        var lang = new Lang("en");
        var warmAlert = new DiskAlert
        {
            Letter = "/",
            Level = DiskAlertLevel.Warm,
            Usage = 75,
        };

        Assert.IsFalse(warmAlert.IsHot);
        Assert.AreEqual(Colour.FromHex("#eab308"), warmAlert.Colour);
        Assert.AreEqual("⚠ Drive / is running low (75% used) · click to free space", warmAlert.Text(lang));

        var hotAlert = new DiskAlert
        {
            Letter = "/data",
            Level = DiskAlertLevel.Hot,
            Usage = 92,
        };

        Assert.IsTrue(hotAlert.IsHot);
        Assert.AreEqual(Colour.FromHex("#ef4444"), hotAlert.Colour);
        Assert.AreEqual("⚠ Drive /data is almost full (92% used) · click to free space", hotAlert.Text(lang));
    }

    [TestMethod]
    public void DeviceFilter_VisibleDisks_WhenIncludeNetworkIsFalse_ExcludesNetworkDisksFromAlertTracking()
    {
        var localDisk = MakeDisk("/", 95, isNetwork: false);
        var netDisk = MakeDisk("/mnt/share", 95, isNetwork: true);
        var snapshot = new Snapshot
        {
            Ready = true,
            Disks = new[] { localDisk, netDisk },
        };

        var configNoNet = new AppConfig { IncludeNetwork = false };
        IReadOnlyList<Disk> visibleNoNet = DeviceFilter.VisibleDisks(snapshot, configNoNet);
        Assert.AreEqual(1, visibleNoNet.Count);
        Assert.AreEqual("/", visibleNoNet[0].Letter);

        var tracker = new DiskAlertTracker();
        tracker.Update(visibleNoNet);
        Assert.AreEqual(1, tracker.Alerts.Count);
        Assert.AreEqual("/", tracker.Alerts[0].Letter);
        Assert.AreEqual(DiskAlertLevel.None, tracker.LevelOf("/mnt/share"));

        var configWithNet = new AppConfig { IncludeNetwork = true };
        IReadOnlyList<Disk> visibleWithNet = DeviceFilter.VisibleDisks(snapshot, configWithNet);
        Assert.AreEqual(2, visibleWithNet.Count);
    }
}
