using SysMonitor.Model;
using SysMonitor.Native;
using SysMonitor.Sensors;
using SysMonitor.ViewModels;

namespace SysMonitor.Tests;

/// <summary>
/// The SMART module (R-002): what the sampler decides to show, and where it
/// appears -- the widget rotation, the overall view and the full view.
/// </summary>
[TestClass]
public class SmartModuleTests
{
    private static SmartReading Reading(int drive, string model = "Disk", int? temp = 40, int? life = 90,
                                        SmartHealth health = SmartHealth.Good, bool full = true,
                                        long? hours = 1200, int kind = 6, int media = 2) =>
        new(drive, model, temp, life, health, full, "SN" + drive, "FW1", kind, media, hours);

    private static IReadOnlyDictionary<int, SmartReading> Readings(params SmartReading[] r) =>
        r.ToDictionary(x => x.Drive);

    private static SmartInfo Info(params SmartDisk[] disks) => new(SmartState.Ok, disks);

    private static SmartDisk Disk(int drive, string model = "WDS250G3X0C", int? temp = 51, int? life = 96,
                                  DiskHealth health = DiskHealth.Good, long? hours = 947,
                                  string iface = "NVMe", string media = "SSD", bool full = true) =>
        new(drive, model, "SER" + drive, "FW" + drive, iface, media, temp, life, health, hours, full);

    private static Snapshot Sample(SmartInfo smart) => new()
    {
        Ready = true,
        CpuTotal = 10,
        Cores = new[] { new Core { Usage = 10 } },
        Ram = new Ram { Usage = 40, UsedGb = 8, TotalGb = 16 },
        Smart = smart,
    };

    /// <summary>How many pages the rotation has with the SMART module off.</summary>
    private static int BaselinePages(Snapshot snap)
    {
        var off = Model(show: false);
        off.RebuildViews(snap);
        return off.ViewCount;
    }

    private static WidgetViewModel Model(bool show = true, string lang = "en") =>
        new(new AppConfig { Lang = lang, ShowSmart = show });

    // ------------------------------------------------------------- the state
    [TestMethod]
    public void Switched_off_it_is_off_whatever_else_is_true()
    {
        SmartInfo info = SmartBoard.Build(false, true, true, SmartScanStatus.Ok, Readings(Reading(0)));
        Assert.AreEqual(SmartState.Off, info.State);
        Assert.AreEqual(0, info.Disks.Count);
    }

    [TestMethod]
    public void Without_the_library_it_says_so()
    {
        Assert.AreEqual(SmartState.Unavailable,
            SmartBoard.Build(true, false, true, SmartScanStatus.Ok, Readings()).State);
    }

    [TestMethod]
    public void Without_administrator_rights_it_says_so_and_never_waits_for_a_scan_that_cannot_run()
    {
        Assert.AreEqual(SmartState.NeedAdmin,
            SmartBoard.Build(true, true, false, null, Readings()).State);
    }

    [TestMethod]
    public void Before_the_first_scan_returns_it_waits()
    {
        Assert.AreEqual(SmartState.Waiting,
            SmartBoard.Build(true, true, true, null, Readings()).State);
    }

    [TestMethod]
    [DataRow(1, SmartState.Unavailable)]
    [DataRow(2, SmartState.NeedAdmin)]
    [DataRow(3, SmartState.Failed)]
    public void A_scan_that_did_not_succeed_shows_why_and_not_the_old_disks(int status, SmartState expected)
    {
        // 1..3 are SmartScanStatus.Unavailable, NeedAdmin, Failed (Ok is 0).
        SmartInfo info = SmartBoard.Build(true, true, true, (SmartScanStatus)status, Readings(Reading(0)));
        Assert.AreEqual(expected, info.State);
        Assert.AreEqual(0, info.Disks.Count, "a failed pass must not leave the last disks on screen");
    }

    [TestMethod]
    public void A_good_scan_lists_the_disks_in_drive_order()
    {
        SmartInfo info = SmartBoard.Build(true, true, true, SmartScanStatus.Ok,
            Readings(Reading(3, "C"), Reading(0, "A"), Reading(1, "B")));

        Assert.AreEqual(SmartState.Ok, info.State);
        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, info.Disks.Select(d => d.Model).ToArray());
    }

    [TestMethod]
    public void A_zero_temperature_is_not_a_reading_but_zero_life_is()
    {
        SmartDisk dead = SmartBoard.ToDisk(Reading(0, temp: 0, life: 0));
        Assert.IsNull(dead.TempC, "a bridge that has no sensor says 0");
        Assert.AreEqual(0, dead.LifePercent, "a worn-out SSD really has none left");
    }

    [TestMethod]
    public void Missing_numbers_stay_missing()
    {
        SmartDisk hdd = SmartBoard.ToDisk(Reading(0, temp: null, life: null, hours: null, kind: 2, media: 1));
        Assert.IsNull(hdd.TempC);
        Assert.IsNull(hdd.LifePercent);
        Assert.IsNull(hdd.PowerOnHours);
        Assert.AreEqual("SATA", hdd.Interface);
        Assert.AreEqual("HDD", hdd.Media);
    }

    [TestMethod]
    public void An_interface_the_library_does_not_name_is_left_blank_not_guessed()
    {
        Assert.AreEqual(string.Empty, SmartBoard.InterfaceName(0));
        Assert.AreEqual(string.Empty, SmartBoard.InterfaceName(7));
        Assert.AreEqual(string.Empty, SmartBoard.MediaName(0));
        Assert.AreEqual("NVMe", SmartBoard.InterfaceName(6));
        Assert.AreEqual("USB", SmartBoard.InterfaceName(3));
    }

    [TestMethod]
    public void The_extra_native_fields_survive_the_trip()
    {
        var native = new SmartCore.NativeDisk
        {
            StructSize = 160,
            PhysicalDriveId = 2,
            Model = Pad("Micron 3400", 64),
            Serial = Pad("ABC123", 32),
            Firmware = Pad("P1.2", 16),
            InterfaceKind = 6,
            Media = 2,
            TemperatureC = 34,
            Health = 1,
            LifePercent = 96,
            PowerOnHours = 947,
            Access = 0,
        };
        SmartReading r = SmartCore.ToReading(native);

        Assert.AreEqual("ABC123", r.Serial);
        Assert.AreEqual("P1.2", r.Firmware);
        Assert.AreEqual(6, r.InterfaceKind);
        Assert.AreEqual(2, r.Media);
        Assert.AreEqual(947L, r.PowerOnHours);
    }

    [TestMethod]
    public void The_64_bit_no_value_marker_becomes_null_hours()
    {
        var native = new SmartCore.NativeDisk
        {
            Model = Pad("x", 64), Serial = Pad("", 32), Firmware = Pad("", 16),
            TemperatureC = 30, LifePercent = int.MinValue, PowerOnHours = long.MinValue,
        };
        Assert.IsNull(SmartCore.ToReading(native).PowerOnHours);
    }

    private static byte[] Pad(string text, int size)
    {
        var bytes = new byte[size];
        System.Text.Encoding.UTF8.GetBytes(text).CopyTo(bytes, 0);
        return bytes;
    }

    // ------------------------------------------------------------ the views
    [TestMethod]
    public void The_full_view_gets_a_tab_per_disk_with_the_facts_that_were_read()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(Info(Disk(0), Disk(1, "TOSHIBA MQ04ABF100", 45, null, hours: 17000,
                                              iface: "SATA", media: "HDD"))));

        DeviceTab nvme = model.Tabs.First(t => t.Key == "smart0");
        Assert.AreEqual("SMART 0", nvme.Title);
        Assert.AreEqual("Good", nvme.Summary);
        Assert.AreEqual("WDS250G3X0C", nvme.Hardware);
        Assert.IsTrue(nvme.Facts.Any(f => f.Name == "Life left" && f.Value == "96%"));
        Assert.IsTrue(nvme.Facts.Any(f => f.Name == "Power-on time" && f.Value == "947 h (39 days)"));
        Assert.IsTrue(nvme.Facts.Any(f => f.Value == "NVMe SSD"));
        Assert.IsTrue(nvme.Facts.Any(f => f.Value == "SER0"));

        DeviceTab hdd = model.Tabs.First(t => t.Key == "smart1");
        Assert.IsFalse(hdd.Facts.Any(f => f.Name == "Life left"), "a hard disk has no wear gauge; nothing is invented");
        Assert.IsTrue(hdd.Facts.Any(f => f.Value == "17,000 h (708 days)"));
    }

    [TestMethod]
    public void A_disk_whose_reading_is_partial_says_so()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(Info(Disk(0, full: false))));

        Assert.IsTrue(model.Tabs.First(t => t.Key == "smart0").Facts
            .Any(f => f.Value.Contains("incomplete")));
    }

    [TestMethod]
    public void Switched_off_it_leaves_no_tab_section_or_page()
    {
        WidgetViewModel model = Model(show: false);
        Snapshot snap = Sample(Info(Disk(0)));
        model.PushHistory(snap);
        model.UpdateOverall(snap);
        model.RebuildViews(snap);

        Assert.IsFalse(model.Tabs.Any(t => t.Key.StartsWith("smart", StringComparison.Ordinal)));
        Assert.IsFalse(model.Sections.Any(s => s.Title == "SMART"));
        Assert.AreEqual(BaselinePages(snap), model.ViewCount, "only the pages the other modules give");
    }

    [TestMethod]
    [DataRow(SmartState.NeedAdmin, "administrator")]
    [DataRow(SmartState.Unavailable, "DiskInfoCore.dll")]
    [DataRow(SmartState.Waiting, "Reading")]
    [DataRow(SmartState.Failed, "try again")]
    public void With_no_disks_the_tab_and_section_say_why(SmartState state, string phrase)
    {
        WidgetViewModel model = Model();
        Snapshot snap = Sample(new SmartInfo(state, Array.Empty<SmartDisk>()));
        model.PushHistory(snap);
        model.UpdateOverall(snap);

        DeviceTab tab = model.Tabs.First(t => t.Key == "smart");
        StringAssert.Contains(tab.Detail, phrase);
        Section section = model.Sections.First(s => s.Title == "SMART");
        StringAssert.Contains(section.Note, phrase);
        Assert.AreEqual(0, section.Rows.Count);
    }

    [TestMethod]
    public void A_status_tab_gives_way_to_the_disk_tabs_when_the_scan_returns()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample(new SmartInfo(SmartState.Waiting, Array.Empty<SmartDisk>())));
        Assert.IsTrue(model.Tabs.Any(t => t.Key == "smart"));

        model.PushHistory(Sample(Info(Disk(0))));
        Assert.IsFalse(model.Tabs.Any(t => t.Key == "smart"));
        Assert.IsTrue(model.Tabs.Any(t => t.Key == "smart0"));
    }

    [TestMethod]
    public void The_overall_view_has_a_row_per_disk_with_the_verdict_and_temperature()
    {
        WidgetViewModel model = Model();
        model.UpdateOverall(Sample(Info(Disk(0), Disk(1, health: DiskHealth.Caution, life: null, temp: null))));

        Section section = model.Sections.First(s => s.Title == "SMART");
        Assert.AreEqual(2, section.Rows.Count);

        Assert.AreEqual("Disk 0", section.Rows[0].Title);
        Assert.AreEqual(96, section.Rows[0].Percent);
        Assert.AreEqual("Good", section.Rows[0].ValueText);
        Assert.AreEqual(51, section.Rows[0].Temp);

        Assert.AreEqual(100, section.Rows[1].Percent, "no gauge: a full bar coloured by the verdict");
        Assert.AreEqual("Caution", section.Rows[1].ValueText, "and the text is the verdict, not '100%'");
        Assert.IsNull(section.Rows[1].Temp);
    }

    [TestMethod]
    public void The_widget_rotation_gets_a_page_per_disk()
    {
        WidgetViewModel model = Model();
        Snapshot snap = Sample(Info(Disk(0), Disk(1, "TOSHIBA", 45, null, iface: "SATA", media: "HDD")));
        model.RebuildViews(snap);

        Assert.AreEqual(BaselinePages(snap) + 2, model.ViewCount, "the other modules' pages, then one per disk");
    }

    [TestMethod]
    public void A_smart_page_carries_the_verdict_and_the_temperature()
    {
        WidgetViewModel model = Model();
        Snapshot snap = Sample(Info(Disk(0)));
        model.RebuildViews(snap);
        model.ViewIndex = model.ViewCount - 1;
        model.UpdateWidget(snap);

        Assert.AreEqual("SMART DISK 0", model.WidgetTitle);
        Assert.AreEqual("Good", model.WidgetValue);
        Assert.AreEqual(51, model.WidgetHeader.Temp);
        StringAssert.Contains(model.WidgetDetail, "947 h");
        Assert.AreEqual(96, model.WidgetRows[0].Percent);
    }

    [TestMethod]
    public void A_smart_page_with_no_disks_says_why_instead_of_drawing_a_bar()
    {
        WidgetViewModel model = Model();
        Snapshot snap = Sample(new SmartInfo(SmartState.NeedAdmin, Array.Empty<SmartDisk>()));
        model.RebuildViews(snap);
        model.ViewIndex = model.ViewCount - 1;
        model.UpdateWidget(snap);

        Assert.IsTrue(model.ShowMessage);
        StringAssert.Contains(model.Message, "administrator");
    }

    [TestMethod]
    public void The_pages_follow_the_disks_when_the_first_scan_returns()
    {
        WidgetViewModel model = Model();
        Snapshot waiting = Sample(new SmartInfo(SmartState.Waiting, Array.Empty<SmartDisk>()));
        model.RebuildViews(waiting);
        int before = model.ViewCount;

        model.UpdateWidget(Sample(Info(Disk(0), Disk(1), Disk(2))));

        Assert.AreEqual(before + 2, model.ViewCount, "one page said 'reading'; three disks now have a page each");
    }

    [TestMethod]
    public void One_status_page_becoming_one_disk_page_is_noticed_although_the_count_is_the_same()
    {
        WidgetViewModel model = Model();
        model.RebuildViews(Sample(new SmartInfo(SmartState.Waiting, Array.Empty<SmartDisk>())));
        model.ViewIndex = model.ViewCount - 1;

        model.UpdateWidget(Sample(Info(Disk(0))));

        Assert.IsFalse(model.ShowMessage, "the page now draws the disk instead of the waiting message");
        Assert.AreEqual("Good", model.WidgetValue);
    }

    [TestMethod]
    public void Turning_the_module_off_drops_its_pages_at_the_next_update()
    {
        var config = new AppConfig { Lang = "en", ShowSmart = true };
        var model = new WidgetViewModel(config);
        Snapshot snap = Sample(Info(Disk(0)));
        model.RebuildViews(snap);
        int with = model.ViewCount;

        config.ShowSmart = false;
        model.UpdateWidget(snap);

        Assert.AreEqual(with - 1, model.ViewCount);
    }

    [TestMethod]
    public void A_real_sampler_with_the_module_on_reports_a_state_that_matches_the_machine()
    {
        var config = new AppConfig { ShowSmart = true };
        using var sampler = new Sampler(config);
        sampler.Start();
        Snapshot snap = sampler.Current;
        for (int i = 0; i < 300 && !snap.Ready; i++)
        {
            Thread.Sleep(100);
            snap = sampler.Current;
        }

        Assert.IsTrue(snap.Ready, "the sampler published a snapshot");
        if (!SmartCore.IsAvailable)
        {
            Assert.AreEqual(SmartState.Unavailable, snap.Smart.State);
        }
        else if (!sampler.Elevated)
        {
            Assert.AreEqual(SmartState.NeedAdmin, snap.Smart.State,
                "a normal run says it needs administrator rather than waiting for a scan that cannot run");
        }
        else
        {
            Assert.IsTrue(snap.Smart.State is SmartState.Waiting or SmartState.Ok or SmartState.Failed);
        }
    }

    [TestMethod]
    public void A_real_sampler_with_the_module_off_reports_it_off()
    {
        using var sampler = new Sampler(new AppConfig { ShowSmart = false });
        sampler.Start();
        Snapshot snap = sampler.Current;
        for (int i = 0; i < 300 && !snap.Ready; i++)
        {
            Thread.Sleep(100);
            snap = sampler.Current;
        }
        Assert.IsTrue(snap.Ready, "without a published snapshot the default (Off) would pass by itself");
        Assert.AreEqual(SmartState.Off, snap.Smart.State);
    }

    [TestMethod]
    public void The_chosen_smart_tab_survives_its_key_changing_as_the_scan_comes_and_goes()
    {
        var config = new AppConfig { Lang = "en", ShowSmart = true, FullTab = "smart" };
        var model = new WidgetViewModel(config);

        // Waiting: the only SMART tab is "smart", and it is the one the user is on.
        model.PushHistory(Sample(new SmartInfo(SmartState.Waiting, Array.Empty<SmartDisk>())));
        Assert.AreEqual("smart", model.SelectedTab!.Key);

        // The scan returns: "smart" is replaced by "smart0". The view stays on SMART, not CPU.
        model.PushHistory(Sample(Info(Disk(0))));
        Assert.AreEqual("smart0", model.SelectedTab!.Key);

        // A failed pass swaps back, and the choice is still honoured.
        config.FullTab = "smart0";
        model.PushHistory(Sample(new SmartInfo(SmartState.Failed, Array.Empty<SmartDisk>())));
        Assert.AreEqual("smart", model.SelectedTab!.Key);
    }

    [TestMethod]
    public void A_remembered_smart_tab_that_does_not_exist_yet_does_not_leave_the_pane_blank()
    {
        var model = new WidgetViewModel(new AppConfig { Lang = "en", ShowSmart = true, FullTab = "smart0" });
        model.PushHistory(Sample(new SmartInfo(SmartState.Waiting, Array.Empty<SmartDisk>())));

        Assert.AreEqual("smart", model.SelectedTab!.Key);
    }

    [TestMethod]
    public void A_snapshot_from_before_the_module_was_switched_on_says_reading_not_no_disks()
    {
        WidgetViewModel model = Model();
        model.UpdateOverall(Sample(SmartInfo.Off));

        Section section = model.Sections.First(s => s.Title == "SMART");
        StringAssert.Contains(section.Note, "Reading");
    }

    [TestMethod]
    public void Every_import_may_load_the_library_s_own_dependencies_from_system32()
    {
        // Found by running the real app: with only the app folder searched, a DiskInfoCore.dll that
        // sits right beside the exe still failed to load, because its imports (setupapi, wintrust,
        // oleacc...) are in System32. Nothing else in the suite loads the real library.
        var imports = typeof(SmartCore).GetMethods(System.Reflection.BindingFlags.Static
                                                    | System.Reflection.BindingFlags.NonPublic
                                                    | System.Reflection.BindingFlags.Public)
            .Where(m => m.GetCustomAttributes(typeof(System.Runtime.InteropServices.DllImportAttribute), false).Length > 0)
            .ToList();

        Assert.IsTrue(imports.Count >= 6, "di_abi_version, di_open, di_close, di_scan, di_disk_count, di_disk_info");
        foreach (var import in imports)
        {
            var search = (System.Runtime.InteropServices.DefaultDllImportSearchPathsAttribute?)import
                .GetCustomAttributes(typeof(System.Runtime.InteropServices.DefaultDllImportSearchPathsAttribute), false)
                .FirstOrDefault();
            Assert.IsNotNull(search, import.Name);
            Assert.IsTrue(search!.Paths.HasFlag(System.Runtime.InteropServices.DllImportSearchPath.ApplicationDirectory), import.Name);
            Assert.IsTrue(search.Paths.HasFlag(System.Runtime.InteropServices.DllImportSearchPath.System32), import.Name);
        }
    }

    // ------------------------------------------------------------ the config
    [TestMethod]
    public void It_is_off_by_default_because_it_needs_administrator_and_can_wake_a_drive()
    {
        Assert.IsFalse(new AppConfig().ShowSmart);
    }

    [TestMethod]
    public void A_config_written_before_it_existed_loads_with_it_off()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"smart-{Guid.NewGuid():N}.json");
        try
        {
            System.IO.File.WriteAllText(path, "{\"lang\":\"th\",\"show_gpu\":false}");
            AppConfig loaded = AppConfig.LoadFrom(path);
            Assert.IsFalse(loaded.ShowSmart);
            Assert.IsFalse(loaded.ShowGpu, "and the other keys still count");
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [TestMethod]
    public void The_setting_round_trips_under_its_snake_case_name()
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        };
        string json = System.Text.Json.JsonSerializer.Serialize(new AppConfig { ShowSmart = true }, options);
        StringAssert.Contains(json, "\"show_smart\":true");
        Assert.IsTrue(System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json, options)!.ShowSmart);
    }

    // ------------------------------------------------------------ the wording
    [TestMethod]
    [DataRow("show_smart")]
    [DataRow("smart_health")]
    [DataRow("smart_life")]
    [DataRow("smart_hours")]
    [DataRow("smart_hours_fmt")]
    [DataRow("smart_serial")]
    [DataRow("smart_firmware")]
    [DataRow("smart_good")]
    [DataRow("smart_caution")]
    [DataRow("smart_bad")]
    [DataRow("smart_unknown")]
    [DataRow("smart_partial")]
    [DataRow("smart_waiting")]
    [DataRow("smart_need_admin")]
    [DataRow("smart_missing_core")]
    [DataRow("smart_failed")]
    [DataRow("smart_none")]
    [DataRow("manual_10_h")]
    [DataRow("manual_10_t")]
    public void Every_word_of_the_module_exists_in_both_languages(string key)
    {
        string th = new Lang("th")[key];
        string en = new Lang("en")[key];

        Assert.AreNotEqual(key, th, "a missing key reads as its own name");
        Assert.AreNotEqual(key, en);
        Assert.AreNotEqual(th, en, "a Thai string that fell back to English would be equal");
    }

    [TestMethod]
    public void The_thai_tab_is_worded_in_thai()
    {
        WidgetViewModel model = Model(lang: "th");
        model.PushHistory(Sample(Info(Disk(0))));

        DeviceTab tab = model.Tabs.First(t => t.Key == "smart0");
        Assert.AreEqual("ดี", tab.Summary);
        Assert.IsTrue(tab.Facts.Any(f => f.Name == "อายุที่เหลือ"));
        Assert.IsTrue(tab.Facts.Any(f => f.Value == "947 ชม. (39 วัน)"));
    }

    [TestMethod]
    public void The_hours_format_takes_both_arguments_in_both_languages()
    {
        foreach (string code in new[] { "th", "en" })
        {
            string text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                        new Lang(code)["smart_hours_fmt"], 48L, 2L);
            StringAssert.Contains(text, "48");
            StringAssert.Contains(text, "2");
        }
    }
}
