using SysMonitor.Model;
using SysMonitor.ViewModels;

namespace SysMonitor.Tests;

/// <summary>
/// When a drive deserves a warning: at start-up if it is already past the amber
/// line, again when it gets worse, and not on every reading in between.
/// </summary>
[TestClass]
public class DiskAlertTrackerTests
{
    private static (string, int, bool) Drive(string letter, int usage, bool local = true) =>
        (letter, usage, local);

    [TestMethod]
    public void A_drive_already_past_amber_when_the_program_starts_is_warned_about()
    {
        var tracker = new DiskAlertTracker();
        tracker.Update(new[] { Drive("C", 75), Drive("D", 95), Drive("E", 40) });

        Assert.AreEqual(DiskLevel.Warm, tracker.Pending("C"));
        Assert.AreEqual(DiskLevel.Hot, tracker.Pending("D"));
        Assert.IsNull(tracker.Pending("E"));
    }

    [TestMethod]
    public void A_new_run_warns_again_because_it_starts_with_no_memory()
    {
        var first = new DiskAlertTracker();
        first.Update(new[] { Drive("C", 80) });
        first.Dismiss("C");
        Assert.IsNull(first.Pending("C"));

        var second = new DiskAlertTracker();
        second.Update(new[] { Drive("C", 80) });
        Assert.AreEqual(DiskLevel.Warm, second.Pending("C"));
    }

    [TestMethod]
    public void A_dismissed_warning_stays_quiet_through_every_later_reading()
    {
        var tracker = new DiskAlertTracker();
        tracker.Update(new[] { Drive("C", 75) });
        tracker.Dismiss("C");

        for (int usage = 75; usage <= 78; usage++)
        {
            tracker.Update(new[] { Drive("C", usage) });
            Assert.IsNull(tracker.Pending("C"), $"{usage}%");
        }
    }

    [TestMethod]
    public void Getting_worse_raises_the_warning_again()
    {
        var tracker = new DiskAlertTracker();
        tracker.Update(new[] { Drive("C", 75) });
        tracker.Dismiss("C");

        tracker.Update(new[] { Drive("C", 91) });

        Assert.AreEqual(DiskLevel.Hot, tracker.Pending("C"));
    }

    [TestMethod]
    public void A_reading_that_dips_one_point_below_the_line_does_not_reset_it()
    {
        var tracker = new DiskAlertTracker();
        tracker.Update(new[] { Drive("C", 70) });
        tracker.Dismiss("C");

        tracker.Update(new[] { Drive("C", 69) });      // inside the slack
        tracker.Update(new[] { Drive("C", 70) });

        Assert.IsNull(tracker.Pending("C"));
    }

    [TestMethod]
    public void Dropping_clearly_below_amber_forgets_it_so_the_next_climb_warns()
    {
        var tracker = new DiskAlertTracker();
        tracker.Update(new[] { Drive("C", 75) });
        tracker.Dismiss("C");

        tracker.Update(new[] { Drive("C", 50) });
        Assert.IsNull(tracker.Pending("C"));

        tracker.Update(new[] { Drive("C", 72) });
        Assert.AreEqual(DiskLevel.Warm, tracker.Pending("C"));
    }

    [TestMethod]
    public void A_drive_that_is_not_local_is_never_warned_about()
    {
        var tracker = new DiskAlertTracker();
        tracker.Update(new[] { Drive("Z", 99, local: false) });

        Assert.IsNull(tracker.Pending("Z"));
    }

    [TestMethod]
    public void A_drive_that_goes_away_and_comes_back_is_treated_as_new()
    {
        var tracker = new DiskAlertTracker();
        tracker.Update(new[] { Drive("E", 85) });
        tracker.Dismiss("E");

        tracker.Update(Array.Empty<(string, int, bool)>());
        tracker.Update(new[] { Drive("E", 85) });

        Assert.AreEqual(DiskLevel.Warm, tracker.Pending("E"));
    }

    [TestMethod]
    public void The_thresholds_are_the_ones_the_bars_use()
    {
        Assert.AreEqual(DiskLevel.Ok, DiskAlertTracker.Classify(Palette.LoadWarmAt - 3, DiskLevel.Ok));
        Assert.AreEqual(DiskLevel.Warm, DiskAlertTracker.Classify(Palette.LoadWarmAt, DiskLevel.Ok));
        Assert.AreEqual(DiskLevel.Hot, DiskAlertTracker.Classify(Palette.LoadHotAt, DiskLevel.Ok));
    }
}

/// <summary>What gets launched for a drive -- checked without launching it.</summary>
[TestClass]
public class DiskToolsTests
{
    [TestMethod]
    public void File_Explorer_opens_the_root_of_the_drive()
    {
        Assert.AreEqual("C:\\", DiskTools.Explorer("c")!.FileName);
        Assert.IsTrue(DiskTools.Explorer("D")!.UseShellExecute);
    }

    [TestMethod]
    public void Disk_Cleanup_is_pointed_at_the_drive()
    {
        var start = DiskTools.Cleanup("d")!;

        Assert.AreEqual("cleanmgr.exe", start.FileName);
        Assert.AreEqual("/d D", start.Arguments);
    }

    [TestMethod]
    public void Disk_Management_is_the_msc_snap_in()
    {
        Assert.AreEqual("diskmgmt.msc", DiskTools.Management().FileName);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("CD")]
    [DataRow("1")]
    [DataRow("C:")]
    [DataRow("C /s")]
    [DataRow("é")]
    public void Anything_but_a_single_letter_is_refused_because_it_ends_up_on_a_command_line(string letter)
    {
        Assert.IsNull(DiskTools.Explorer(letter));
        Assert.IsNull(DiskTools.Cleanup(letter));
    }
}

/// <summary>The warning bar, the double-click target and the tab's drives, from the model.</summary>
[TestClass]
public class DiskRowTests
{
    private static Snapshot Snap(params (string Letter, int Usage)[] drives) => new()
    {
        Ready = true,
        CpuTotal = 10,
        Cores = new[] { new Core { Usage = 5 } },
        Ram = new Ram { Usage = 50, UsedGb = 8, TotalGb = 16 },
        Disks = drives.Select(d => new Disk { Letter = d.Letter, Usage = d.Usage }).ToList(),
    };

    private static WidgetViewModel Model(string diskMode = "separated", bool local = true) =>
        new(new AppConfig { Lang = "en", DiskMode = diskMode }) { IsLocalDrive = _ => local };

    private static List<MeterRow> DriveRows(WidgetViewModel model) =>
        model.Sections.SelectMany(s => s.Rows).Where(r => r.DriveLetter is not null).ToList();

    [TestMethod]
    public void Each_drive_row_knows_its_letter_and_a_full_one_carries_the_warning()
    {
        WidgetViewModel model = Model();
        Snapshot snap = Snap(("C", 95), ("D", 50), ("E", 75));
        model.UpdateAlerts(snap);
        model.UpdateOverall(snap);

        List<MeterRow> rows = DriveRows(model);
        CollectionAssert.AreEqual(new[] { "C", "D", "E" }, rows.Select(r => r.DriveLetter).ToList());

        StringAssert.Contains(rows[0].AlertText, "C:");
        StringAssert.Contains(rows[0].AlertText, "95%");
        Assert.IsTrue(rows[0].AlertHot);
        Assert.AreEqual(string.Empty, rows[1].AlertText);
        StringAssert.Contains(rows[2].AlertText, "75%");
        Assert.IsFalse(rows[2].AlertHot);
        Assert.IsTrue(rows[0].HasAlert);
        Assert.IsFalse(rows[1].HasAlert);
    }

    [TestMethod]
    public void A_drive_that_is_not_local_still_opens_in_Explorer_but_is_never_warned_about()
    {
        WidgetViewModel model = Model(local: false);
        Snapshot snap = Snap(("Z", 99));
        model.UpdateAlerts(snap);
        model.UpdateOverall(snap);

        MeterRow row = DriveRows(model).Single();
        Assert.AreEqual("Z", row.DriveLetter);
        Assert.AreEqual(string.Empty, row.AlertText);
    }

    [TestMethod]
    public void Dismissing_takes_the_warning_down_and_it_does_not_come_back_on_the_next_reading()
    {
        WidgetViewModel model = Model();
        Snapshot snap = Snap(("C", 95));
        model.UpdateAlerts(snap);
        model.UpdateOverall(snap);
        Assert.IsTrue(DriveRows(model).Single().HasAlert);

        model.DismissAlert("C");
        model.UpdateAlerts(snap);
        model.UpdateOverall(snap);

        Assert.IsFalse(DriveRows(model).Single().HasAlert);
    }

    [TestMethod]
    public void The_all_drives_row_has_no_letter_so_nothing_opens_and_nothing_warns()
    {
        WidgetViewModel model = Model(diskMode: "total");
        Snapshot snap = Snap(("C", 95), ("D", 50));
        model.UpdateAlerts(snap);
        model.UpdateOverall(snap);

        Assert.AreEqual(0, DriveRows(model).Count);
    }

    [TestMethod]
    public void A_language_change_rewrites_the_warning_in_the_new_language()
    {
        WidgetViewModel model = Model();
        Snapshot snap = Snap(("C", 95));
        model.UpdateAlerts(snap);
        model.UpdateOverall(snap);
        string english = DriveRows(model).Single().AlertText;

        model.SetLanguage("th");
        model.UpdateOverall(snap);

        Assert.AreNotEqual(english, DriveRows(model).Single().AlertText);
        StringAssert.Contains(DriveRows(model).Single().AlertText, "C:");
    }

    [TestMethod]
    public void The_mini_view_carries_the_warning_on_its_detail_line_for_that_drive()
    {
        WidgetViewModel model = Model();
        Snapshot snap = Snap(("C", 95), ("D", 40));
        model.UpdateAlerts(snap);
        model.RebuildViews(snap);

        string? warnedLetter = null;
        for (int i = 0; i < model.ViewCount; i++)
        {
            model.ViewIndex = i;
            model.UpdateWidget(snap);
            if (model.WidgetAlertLetter is not null)
            {
                warnedLetter = model.WidgetAlertLetter;
                StringAssert.Contains(model.WidgetDetail, "C:");
                Assert.AreEqual(Palette.Brush(Palette.LoadHot), model.WidgetDetailBrush);
            }
        }
        Assert.AreEqual("C", warnedLetter);

        // And a page that is not that drive does not keep the warning.
        model.ViewIndex = 0;
        model.UpdateWidget(snap);
        Assert.IsNull(model.WidgetAlertLetter);
    }

    [TestMethod]
    public void A_disk_tab_lists_the_letters_it_covers_and_other_tabs_list_none()
    {
        var model = new WidgetViewModel(new AppConfig { Lang = "en" });
        model.PushHistory(Snap(("C", 40), ("D", 40)));

        // No disk detail in this snapshot, so both land under one heading.
        DeviceTab disks = model.Tabs.Single(t => t.Key == "diskvirtual");
        CollectionAssert.AreEqual(new[] { "C", "D" }, disks.DriveLetters.ToList());
        Assert.AreEqual(0, model.Tabs.Single(t => t.Key == "cpu").DriveLetters.Count);
    }
}
