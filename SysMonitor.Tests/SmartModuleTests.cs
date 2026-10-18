using SysMonitor.Ext.Smart;
using SysMonitor.Extensibility;

namespace SysMonitor.Tests;

/// <summary>
/// Unit tests for the SMART extension package and its components (SmartBoard, SmartCore,
/// SmartStrings, SmartViewBuilder, SmartExtension). Tests run against deterministic mocks
/// and memory structures; never against real disks or native DLLs.
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

    // ------------------------------------------------------------- SmartBoard
    [TestMethod]
    public void Without_the_library_it_says_so()
    {
        Assert.AreEqual(SmartState.Unavailable,
            SmartBoard.Build(available: false, elevated: true, SmartScanStatus.Ok, Readings()).State);
    }

    [TestMethod]
    public void Without_administrator_rights_it_says_so_and_never_waits_for_a_scan_that_cannot_run()
    {
        Assert.AreEqual(SmartState.NeedAdmin,
            SmartBoard.Build(available: true, elevated: false, null, Readings()).State);
    }

    [TestMethod]
    public void Before_the_first_scan_returns_it_waits()
    {
        Assert.AreEqual(SmartState.Waiting,
            SmartBoard.Build(available: true, elevated: true, null, Readings()).State);
    }

    [TestMethod]
    [DataRow(1, "Unavailable")]
    [DataRow(2, "NeedAdmin")]
    [DataRow(3, "Failed")]
    public void A_scan_that_did_not_succeed_shows_why_and_not_the_old_disks(int status, string expectedName)
    {
        SmartInfo info = SmartBoard.Build(available: true, elevated: true, (SmartScanStatus)status, Readings(Reading(0)));
        Assert.AreEqual(Enum.Parse<SmartState>(expectedName), info.State);
        Assert.AreEqual(0, info.Disks.Count, "a failed pass must not leave the last disks on screen");
    }

    [TestMethod]
    public void A_good_scan_lists_the_disks_in_drive_order()
    {
        SmartInfo info = SmartBoard.Build(available: true, elevated: true, SmartScanStatus.Ok,
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

    // ------------------------------------------------------------- SmartStrings
    [TestMethod]
    public void StringTable_has_full_key_parity_between_en_and_th()
    {
        var missing = SmartStrings.MissingKeys();
        Assert.AreEqual(0, missing.Count, string.Join("; ", missing));
    }

    [TestMethod]
    [DataRow("smart_good")]
    [DataRow("smart_caution")]
    [DataRow("smart_bad")]
    [DataRow("smart_unknown")]
    [DataRow("smart_waiting")]
    [DataRow("smart_need_admin")]
    [DataRow("smart_missing_core")]
    [DataRow("smart_failed")]
    [DataRow("smart_none")]
    public void StringTable_translations_differ_between_languages(string key)
    {
        string en = SmartStrings.Get(key, "en");
        string th = SmartStrings.Get(key, "th");
        Assert.AreNotEqual(key, en);
        Assert.AreNotEqual(key, th);
        Assert.AreNotEqual(en, th);
    }

    // ------------------------------------------------------------- SmartViewBuilder
    [TestMethod]
    public void SmartViewBuilder_builds_views_with_disks()
    {
        SmartInfo info = Info(Disk(0), Disk(1, "TOSHIBA MQ04ABF100", 45, null, hours: 17000, iface: "SATA", media: "HDD"));
        ExtensionView view = SmartViewBuilder.Build(info, "en");

        Assert.IsNotNull(view.WidgetPages);
        Assert.AreEqual(2, view.WidgetPages.Count);
        Assert.AreEqual("SMART Disk 0", view.WidgetPages[0].Title);
        Assert.AreEqual("Good", view.WidgetPages[0].Verdict);
        Assert.AreEqual(51, view.WidgetPages[0].TemperatureC);
        Assert.AreEqual(96, view.WidgetPages[0].Percent);
        Assert.AreEqual(Severity.Good, view.WidgetPages[0].Severity);

        Assert.IsNotNull(view.Overview);
        Assert.AreEqual(2, view.Overview.Rows.Count);
        Assert.AreEqual("Disk 0", view.Overview.Rows[0].Title);
        Assert.AreEqual("Good", view.Overview.Rows[0].ValueText);
        Assert.AreEqual(51, view.Overview.Rows[0].TemperatureC);

        Assert.IsNotNull(view.Tabs);
        Assert.AreEqual(2, view.Tabs.Count);
        Assert.AreEqual("disk0", view.Tabs[0].Key);
        Assert.AreEqual("SMART 0", view.Tabs[0].Title);
        Assert.AreEqual("WDS250G3X0C", view.Tabs[0].Hardware);
        Assert.IsTrue(view.Tabs[0].Facts.Any(f => f.Name == "Life left" && f.Value == "96%"));
        Assert.IsTrue(view.Tabs[0].Facts.Any(f => f.Name == "Power-on time" && f.Value == "947 h (39 days)"));

        Assert.AreEqual("disk1", view.Tabs[1].Key);
        Assert.IsFalse(view.Tabs[1].Facts.Any(f => f.Name == "Life left"));
    }

    [TestMethod]
    [DataRow("NeedAdmin", "administrator")]
    [DataRow("Unavailable", "DiskInfoCore.dll")]
    [DataRow("Waiting", "Reading")]
    [DataRow("Failed", "try again")]
    [DataRow("Ok", "No disk reported")]
    public void SmartViewBuilder_when_no_disks_shows_message_in_page_overview_and_tab(string stateName, string phrase)
    {
        SmartInfo info = new(Enum.Parse<SmartState>(stateName), Array.Empty<SmartDisk>());
        ExtensionView view = SmartViewBuilder.Build(info, "en");

        Assert.AreEqual(1, view.WidgetPages.Count);
        StringAssert.Contains(view.WidgetPages[0].Message, phrase);

        Assert.IsNotNull(view.Overview);
        StringAssert.Contains(view.Overview.Note, phrase);
        Assert.AreEqual(0, view.Overview.Rows.Count);

        Assert.AreEqual(1, view.Tabs.Count);
        Assert.AreEqual("status", view.Tabs[0].Key);
        StringAssert.Contains(view.Tabs[0].Detail, phrase);
    }

    [TestMethod]
    public void SmartViewBuilder_ssd_with_all_values_exact_fields()
    {
        SmartDisk ssd = new(
            Drive: 0,
            Model: "WDS250G3X0C",
            Serial: "20448E802492",
            Firmware: "111110WD",
            Interface: "NVMe",
            Media: "SSD",
            TempC: 51,
            LifePercent: 96,
            Health: DiskHealth.Good,
            PowerOnHours: 947,
            Full: true);

        SmartInfo info = Info(ssd);
        ExtensionView view = SmartViewBuilder.Build(info, "en");

        // Widget page
        Assert.AreEqual(1, view.WidgetPages.Count);
        WidgetPage p = view.WidgetPages[0];
        Assert.AreEqual("SMART Disk 0", p.Title);
        Assert.AreEqual("Good", p.Verdict);
        Assert.AreEqual("Life left 96%  ·  947 h (39 days)", p.Line);
        Assert.AreEqual(51, p.TemperatureC);
        Assert.AreEqual(96, p.Percent);
        Assert.AreEqual(Severity.Good, p.Severity);
        Assert.IsNull(p.Message);

        // Overview
        Assert.IsNotNull(view.Overview);
        Assert.AreEqual(string.Empty, view.Overview.Note);
        Assert.AreEqual(1, view.Overview.Rows.Count);
        OverviewRow r = view.Overview.Rows[0];
        Assert.AreEqual("Disk 0", r.Title);
        Assert.AreEqual("WDS250G3X0C  ·  NVMe SSD  ·  Life left 96%  ·  947 h (39 days)", r.Detail);
        Assert.AreEqual("Good", r.ValueText);
        Assert.AreEqual(96, r.Percent);
        Assert.AreEqual(Severity.Good, r.Severity);
        Assert.AreEqual(51, r.TemperatureC);

        // Detail tab
        Assert.AreEqual(1, view.Tabs.Count);
        DetailTab t = view.Tabs[0];
        Assert.AreEqual("disk0", t.Key);
        Assert.AreEqual("SMART 0", t.Title);
        Assert.AreEqual("Good", t.Subtitle);
        Assert.AreEqual("51°C  ·  96%", t.Detail);
        Assert.AreEqual("WDS250G3X0C", t.Hardware);

        var facts = t.Facts;
        Assert.AreEqual(7, facts.Count);
        Assert.AreEqual("Health", facts[0].Name);
        Assert.AreEqual("Good", facts[0].Value);
        Assert.AreEqual("Temp", facts[1].Name);
        Assert.AreEqual("51°C", facts[1].Value);
        Assert.AreEqual("Life left", facts[2].Name);
        Assert.AreEqual("96%", facts[2].Value);
        Assert.AreEqual("Power-on time", facts[3].Name);
        Assert.AreEqual("947 h (39 days)", facts[3].Value);
        Assert.AreEqual("Type", facts[4].Name);
        Assert.AreEqual("NVMe SSD", facts[4].Value);
        Assert.AreEqual("Serial number", facts[5].Name);
        Assert.AreEqual("20448E802492", facts[5].Value);
        Assert.AreEqual("Firmware", facts[6].Name);
        Assert.AreEqual("111110WD", facts[6].Value);
    }

    [TestMethod]
    public void SmartViewBuilder_hdd_without_life_defaults_to_100_percent()
    {
        SmartDisk hdd = new(
            Drive: 1,
            Model: "ST1000DM010",
            Serial: "W9A12345",
            Firmware: "CC43",
            Interface: "SATA",
            Media: "HDD",
            TempC: 38,
            LifePercent: null,
            Health: DiskHealth.Good,
            PowerOnHours: 12500,
            Full: true);

        SmartInfo info = Info(hdd);
        ExtensionView view = SmartViewBuilder.Build(info, "en");

        Assert.AreEqual(100, view.WidgetPages[0].Percent);
        Assert.AreEqual("Good", view.WidgetPages[0].Verdict);
        Assert.AreEqual("12,500 h (520 days)", view.WidgetPages[0].Line);

        Assert.AreEqual(100, view.Overview.Rows[0].Percent);
        Assert.AreEqual("38°C", view.Tabs[0].Detail);
        Assert.IsFalse(view.Tabs[0].Facts.Any(f => f.Name == "Life left"));
    }

    [TestMethod]
    public void SmartViewBuilder_disk_without_temperature_and_partial_full_false()
    {
        SmartDisk disk = new(
            Drive: 2,
            Model: "Generic USB",
            Serial: "",
            Firmware: "",
            Interface: "USB",
            Media: "",
            TempC: null,
            LifePercent: 80,
            Health: DiskHealth.Caution,
            PowerOnHours: null,
            Full: false);

        SmartInfo info = Info(disk);
        ExtensionView view = SmartViewBuilder.Build(info, "en");

        Assert.IsNull(view.WidgetPages[0].TemperatureC);
        Assert.AreEqual(Severity.Caution, view.WidgetPages[0].Severity);
        Assert.AreEqual("80%", view.Tabs[0].Detail);
        Assert.IsTrue(view.Tabs[0].Facts.Any(f => f.Name == "SMART" && f.Value.Contains("incomplete", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(view.Tabs[0].Facts.Any(f => f.Name == "Temp"));
    }

    [TestMethod]
    public void SmartViewBuilder_severities_caution_and_bad()
    {
        SmartDisk cautionDisk = Disk(0, health: DiskHealth.Caution);
        SmartDisk badDisk = Disk(1, health: DiskHealth.Bad);

        SmartInfo info = Info(cautionDisk, badDisk);
        ExtensionView view = SmartViewBuilder.Build(info, "en");

        Assert.AreEqual(Severity.Caution, view.WidgetPages[0].Severity);
        Assert.AreEqual("Caution", view.WidgetPages[0].Verdict);
        Assert.AreEqual(Severity.Bad, view.WidgetPages[1].Severity);
        Assert.AreEqual("Bad", view.WidgetPages[1].Verdict);
    }

    [TestMethod]
    public void SmartViewBuilder_long_power_on_hours_formatting_both_languages()
    {
        SmartDisk disk = Disk(0, hours: 123456);
        SmartInfo info = Info(disk);

        ExtensionView enView = SmartViewBuilder.Build(info, "en");
        StringAssert.Contains(enView.Tabs[0].Facts.First(f => f.Name == "Power-on time").Value, "123,456 h (5,144 days)");

        ExtensionView thView = SmartViewBuilder.Build(info, "th");
        StringAssert.Contains(thView.Tabs[0].Facts.First(f => f.Name == "เวลาที่เปิดใช้งาน").Value, "123,456 ชม. (5,144 วัน)");
    }

    [TestMethod]
    public void SmartViewBuilder_thai_output_page_overview_and_tab()
    {
        SmartDisk disk = Disk(0, health: DiskHealth.Good);
        SmartInfo info = Info(disk);

        ExtensionView view = SmartViewBuilder.Build(info, "th");
        Assert.AreEqual("ดี", view.WidgetPages[0].Verdict);
        Assert.AreEqual("ดี", view.Overview.Rows[0].ValueText);
        Assert.AreEqual("ดี", view.Tabs[0].Subtitle);
        Assert.IsTrue(view.Tabs[0].Facts.Any(f => f.Name == "สุขภาพ" && f.Value == "ดี"));
    }

    [TestMethod]
    public void SmartViewBuilder_two_disks_keep_drive_order()
    {
        SmartInfo info = Info(Disk(0), Disk(1));
        ExtensionView view = SmartViewBuilder.Build(info, "en");

        Assert.AreEqual("SMART Disk 0", view.WidgetPages[0].Title);
        Assert.AreEqual("SMART Disk 1", view.WidgetPages[1].Title);
        Assert.AreEqual("Disk 0", view.Overview.Rows[0].Title);
        Assert.AreEqual("Disk 1", view.Overview.Rows[1].Title);
        Assert.AreEqual("disk0", view.Tabs[0].Key);
        Assert.AreEqual("disk1", view.Tabs[1].Key);
    }

    // ------------------------------------------------------------- SmartExtension
    private sealed class MockScanner : ISmartScanner
    {
        public bool IsAvailable { get; set; } = true;
        public SmartScanStatus Status { get; set; } = SmartScanStatus.Ok;
        public List<SmartReading> Disks { get; set; } = new();
        public int ScanCount { get; private set; }
        public Func<(SmartScanStatus Status, IReadOnlyList<SmartReading> Disks)>? OnScan { get; set; }

        public (SmartScanStatus Status, IReadOnlyList<SmartReading> Disks) Scan()
        {
            ScanCount++;
            if (OnScan != null)
            {
                return OnScan();
            }
            return (Status, Disks);
        }
    }

    private sealed class MockContext : IExtensionContext
    {
        public string ExtensionDirectory => @"C:\FakeExt";
        public string Language => "en";
        public bool HostIsElevated => true;
        public Version HostContractVersion => new(1, 1);
        public System.Collections.Concurrent.ConcurrentQueue<string> Logs { get; } = new();
        public void Log(string message) => Logs.Enqueue(message);
    }

    [TestMethod]
    public void SmartExtension_elevated_scans_on_demand_and_provides_temperatures()
    {
        var scanner = new MockScanner
        {
            Disks = new List<SmartReading>
            {
                Reading(0, "Micron_3400", temp: 42, life: 98),
                Reading(1, "Samsung_980", temp: 48, life: 95)
            }
        };

        long currentTick = 1000;
        using var ext = new SmartExtension(scanner, clock: () => currentTick, elevatedOverride: true);
        var ctx = new MockContext();
        ext.Initialize(ctx);

        // Before demand, temperatures are empty and demand is None
        Assert.AreEqual(0, ext.GetDiskTemperatures().Count);

        // Set demand
        ext.SetDemand(ExtensionDemand.Views | ExtensionDemand.DiskTemperature);

        // Allow scan thread to process with bounded poll
        for (int i = 0; i < 100 && ext.GetDiskTemperatures().Count == 0; i++)
        {
            Thread.Sleep(20);
        }

        var temps = ext.GetDiskTemperatures();
        Assert.AreEqual(2, temps.Count);
        Assert.AreEqual(42, temps[0]);
        Assert.AreEqual(48, temps[1]);

        ExtensionView view = ext.GetView("en");
        Assert.AreEqual(2, view.WidgetPages.Count);
        Assert.AreEqual(2, view.Tabs.Count);

        ext.Shutdown();
    }

    [TestMethod]
    public void SmartExtension_demand_none_does_zero_scans()
    {
        var scanner = new MockScanner();
        long currentTick = 1000;
        using var passObserved = new ManualResetEventSlim(false);
        using var ext = new SmartExtension(
            scanner,
            clock: () => currentTick,
            elevatedOverride: true,
            pinLibrary: null,
            onLoopPass: () => passObserved.Set());
        var ctx = new MockContext();
        ext.Initialize(ctx);

        ext.SetDemand(ExtensionDemand.None);
        bool passed = passObserved.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(passed, "Loop pass should have been observed");

        Assert.AreEqual(0, scanner.ScanCount);
        Assert.AreEqual(0, ext.GetDiskTemperatures().Count);
        ext.Shutdown();
    }

    [TestMethod]
    public void SmartExtension_disk_temperature_only_scans_and_provides_temps_while_views_still_available()
    {
        var scanner = new MockScanner
        {
            Disks = new List<SmartReading> { Reading(0, temp: 45) }
        };

        using var ext = new SmartExtension(scanner, elevatedOverride: true);
        var ctx = new MockContext();
        ext.Initialize(ctx);

        ext.SetDemand(ExtensionDemand.DiskTemperature);

        for (int i = 0; i < 100 && ext.GetDiskTemperatures().Count == 0; i++)
        {
            Thread.Sleep(20);
        }

        Assert.AreEqual(1, ext.GetDiskTemperatures().Count);
        Assert.AreEqual(45, ext.GetDiskTemperatures()[0]);

        // View is still available from the shared ScanState
        ExtensionView view = ext.GetView("en");
        Assert.AreEqual(1, view.WidgetPages.Count);
        Assert.AreEqual(45, view.WidgetPages[0].TemperatureC);

        ext.Shutdown();
    }

    [TestMethod]
    public void SmartExtension_failed_pass_clears_readings_and_temperatures()
    {
        var scanner = new MockScanner
        {
            Disks = new List<SmartReading> { Reading(0, temp: 45) }
        };

        long tick = 1000;
        using var ext = new SmartExtension(scanner, clock: () => tick, elevatedOverride: true);
        var ctx = new MockContext();
        ext.Initialize(ctx);

        ext.SetDemand(ExtensionDemand.Views | ExtensionDemand.DiskTemperature);

        for (int i = 0; i < 100 && ext.GetDiskTemperatures().Count == 0; i++)
        {
            Thread.Sleep(20);
        }
        Assert.AreEqual(1, ext.GetDiskTemperatures().Count);

        // Next pass fails
        scanner.Status = SmartScanStatus.Failed;
        scanner.Disks.Clear();
        tick += (long)TimeSpan.FromMinutes(3).TotalMilliseconds;
        ext.SetDemand(ExtensionDemand.Views | ExtensionDemand.DiskTemperature);

        for (int i = 0; i < 100 && ext.GetDiskTemperatures().Count > 0; i++)
        {
            Thread.Sleep(20);
        }

        Assert.AreEqual(0, ext.GetDiskTemperatures().Count);
        ExtensionView view = ext.GetView("en");
        StringAssert.Contains(view.WidgetPages[0].Message, "try again");

        ext.Shutdown();
    }

    [TestMethod]
    public void SmartExtension_throwing_scanner_publishes_failed_state_without_crashing()
    {
        var scanner = new MockScanner
        {
            OnScan = () => throw new InvalidOperationException("disk I/O error")
        };

        using var ext = new SmartExtension(scanner, elevatedOverride: true);
        var ctx = new MockContext();
        ext.Initialize(ctx);

        ext.SetDemand(ExtensionDemand.Views);

        for (int i = 0; i < 100 && ctx.Logs.IsEmpty; i++)
        {
            Thread.Sleep(20);
        }

        Assert.IsTrue(ctx.Logs.Any(l => l.Contains("SMART scan failed")));
        ExtensionView view = ext.GetView("en");
        StringAssert.Contains(view.WidgetPages[0].Message, "try again");
        Assert.AreEqual(0, ext.GetDiskTemperatures().Count);

        ext.Shutdown();
    }

    [TestMethod]
    public void SmartExtension_views_off_to_on_discards_old_state_and_rescans_immediately()
    {
        using var unblockSecondScan = new ManualResetEventSlim(false);
        using var secondScanStarted = new ManualResetEventSlim(false);
        int scanIndex = 0;

        var scanner = new MockScanner
        {
            OnScan = () =>
            {
                int current = Interlocked.Increment(ref scanIndex);
                if (current == 1)
                {
                    return (SmartScanStatus.Ok, new List<SmartReading> { Reading(0, temp: 40) });
                }
                secondScanStarted.Set();
                unblockSecondScan.Wait(TimeSpan.FromSeconds(5));
                return (SmartScanStatus.Ok, new List<SmartReading> { Reading(0, temp: 45) });
            }
        };

        using var ext = new SmartExtension(scanner, elevatedOverride: true);
        var ctx = new MockContext();
        ext.Initialize(ctx);

        ext.SetDemand(ExtensionDemand.DiskTemperature);
        for (int i = 0; i < 100 && ext.GetDiskTemperatures().Count == 0; i++)
        {
            Thread.Sleep(20);
        }
        Assert.AreEqual(40, ext.GetDiskTemperatures()[0]);

        // Now turn Views on: state should immediately become Waiting before new scan completes
        ext.SetDemand(ExtensionDemand.Views | ExtensionDemand.DiskTemperature);

        // Right after SetDemand(Views) and before fake scanner is released, assert discard:
        // GetView shows the Waiting sentence and GetDiskTemperatures() is empty.
        ExtensionView viewWhileWaiting = ext.GetView("en");
        StringAssert.Contains(viewWhileWaiting.WidgetPages[0].Message, "Reading SMART for the first time");
        Assert.AreEqual(0, ext.GetDiskTemperatures().Count, "Temps must be discarded and empty while waiting");

        // Release the second scan
        unblockSecondScan.Set();

        // Rescan result appears
        for (int i = 0; i < 100 && ext.GetDiskTemperatures().Count == 0; i++)
        {
            Thread.Sleep(20);
        }

        Assert.IsTrue(scanner.ScanCount >= 2);
        Assert.AreEqual(45, ext.GetDiskTemperatures()[0]);
        ExtensionView viewAfter = ext.GetView("en");
        Assert.AreEqual(45, viewAfter.WidgetPages[0].TemperatureC);
        ext.Shutdown();
    }

    [TestMethod]
    public void SmartExtension_scan_started_before_views_off_to_on_cannot_publish_over_the_discard()
    {
        using var firstStarted = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);
        using var secondStarted = new ManualResetEventSlim(false);
        using var releaseSecond = new ManualResetEventSlim(false);
        int scanIndex = 0;

        var scanner = new MockScanner
        {
            OnScan = () =>
            {
                // The long waits are safety nets; the test releases both scans itself.
                if (Interlocked.Increment(ref scanIndex) == 1)
                {
                    firstStarted.Set();
                    releaseFirst.Wait(TimeSpan.FromSeconds(30));
                    return (SmartScanStatus.Ok, new List<SmartReading> { Reading(0, temp: 40) });
                }

                secondStarted.Set();
                releaseSecond.Wait(TimeSpan.FromSeconds(30));
                return (SmartScanStatus.Ok, new List<SmartReading> { Reading(0, temp: 45) });
            }
        };

        using var ext = new SmartExtension(scanner, elevatedOverride: true);
        var ctx = new MockContext();
        ext.Initialize(ctx);

        // Views is off: only the temperature demand starts the old scan, and it blocks inside the scanner.
        ext.SetDemand(ExtensionDemand.DiskTemperature);
        Assert.IsTrue(firstStarted.Wait(TimeSpan.FromSeconds(5)), "The first scan must start");

        // Views off -> on while that scan runs: the state is discarded.
        ext.SetDemand(ExtensionDemand.Views | ExtensionDemand.DiskTemperature);
        StringAssert.Contains(ext.GetView("en").WidgetPages[0].Message, "Reading SMART for the first time");
        Assert.AreEqual(0, ext.GetDiskTemperatures().Count);

        // The old scan finishes. Its result is stale and must be dropped; the new scan starts and blocks.
        releaseFirst.Set();
        Assert.IsTrue(secondStarted.Wait(TimeSpan.FromSeconds(5)), "A new scan must start after the stale result is dropped");

        StringAssert.Contains(ext.GetView("en").WidgetPages[0].Message, "Reading SMART for the first time");
        Assert.AreEqual(0, ext.GetDiskTemperatures().Count, "The old scan must not publish over the discard");

        // The new scan finishes and its result appears.
        releaseSecond.Set();
        for (int i = 0; i < 250 && ext.GetDiskTemperatures().Count == 0; i++)
        {
            Thread.Sleep(20);
        }

        Assert.AreEqual(45, ext.GetDiskTemperatures()[0]);
        Assert.AreEqual(45, ext.GetView("en").WidgetPages[0].TemperatureC);
        ext.Shutdown();
    }

    [TestMethod]
    public void SmartExtension_shutdown_stops_thread_is_idempotent_and_no_scans_after()
    {
        var scanner = new MockScanner
        {
            Disks = new List<SmartReading> { Reading(0, temp: 40) }
        };

        var ext = new SmartExtension(scanner, elevatedOverride: true);
        var ctx = new MockContext();
        ext.Initialize(ctx);
        ext.SetDemand(ExtensionDemand.Views);

        for (int i = 0; i < 100 && scanner.ScanCount == 0; i++)
        {
            Thread.Sleep(20);
        }

        ext.Shutdown();
        int countAtShutdown = scanner.ScanCount;

        // Idempotent call
        ext.Shutdown();
        ext.Dispose();

        ext.SetDemand(ExtensionDemand.Views);
        Thread.Sleep(50);
        Assert.AreEqual(countAtShutdown, scanner.ScanCount);
    }

    [TestMethod]
    public void SmartExtension_shutdown_with_scan_outliving_bound_returns_and_pins_library()
    {
        using var unblockScan = new ManualResetEventSlim(false);
        using var scanStarted = new ManualResetEventSlim(false);
        var scanner = new MockScanner
        {
            OnScan = () =>
            {
                scanStarted.Set();
                // Released only by the test after Shutdown has returned; the long bound is a safety net
                // so a broken test cannot hang, never what ends the scan.
                unblockScan.Wait(TimeSpan.FromSeconds(60));
                return (SmartScanStatus.Ok, Array.Empty<SmartReading>());
            }
        };

        string? pinnedPath = null;
        Func<string, bool> pinFake = path =>
        {
            pinnedPath = path;
            return true;
        };

        var ext = new SmartExtension(scanner, clock: null, elevatedOverride: true, pinLibrary: pinFake);
        var ctx = new MockContext();
        ext.Initialize(ctx);

        ext.SetDemand(ExtensionDemand.Views);

        // Wait until scanner enters OnScan with 5 s event
        bool started = scanStarted.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(started, "Scan must start before shutdown is initiated");

        // Shutdown will join for 4 seconds, time out, and pin library
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ext.Shutdown();
        sw.Stop();
        bool scanStillBlockedAtReturn = !unblockScan.IsSet;
        unblockScan.Set();

        Assert.IsTrue(scanStillBlockedAtReturn, "The scan must still be running when Shutdown returns");
        Assert.IsTrue(sw.Elapsed >= TimeSpan.FromSeconds(3), $"Shutdown must have waited for the join bound, took {sw.Elapsed}");
        Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(30), "Shutdown must return after the bound, not wait for the scan");
        Assert.IsNotNull(pinnedPath);
        StringAssert.Contains(pinnedPath, "DiskInfoCore.dll");
        Assert.IsTrue(ctx.Logs.Any(l => l.Contains("library pinned")));
    }

    [TestMethod]
    public void SmartExtension_non_elevated_reports_NeedAdmin_without_scanning()
    {
        var scanner = new MockScanner();
        using var ext = new SmartExtension(scanner, elevatedOverride: false);
        var ctx = new MockContext();
        ext.Initialize(ctx);

        ExtensionView view = ext.GetView("en");
        Assert.AreEqual(1, view.WidgetPages.Count);
        StringAssert.Contains(view.WidgetPages[0].Message, "administrator");

        Assert.AreEqual(0, ext.GetDiskTemperatures().Count);
        ext.Shutdown();
    }
}

