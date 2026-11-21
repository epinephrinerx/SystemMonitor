using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class BatteryOverallWidgetTests
{
    private static Snapshot CreateSnapshot(
        bool ready = true,
        int? cpuTemp = 50,
        int? diskTemp = 40,
        bool onAc = false,
        IReadOnlyList<Battery>? batteries = null,
        IReadOnlyList<PeripheralBattery>? peripherals = null)
    {
        var cores = new List<Core>
        {
            new() { Usage = 25 },
            new() { Usage = 30 },
        };

        var disks = new List<Disk>
        {
            new()
            {
                Letter = "/",
                Usage = 50,
                UsedGb = 50.0,
                TotalGb = 100.0,
                Temp = diskTemp,
            },
        };

        var adapters = new List<Adapter>
        {
            new()
            {
                Id = "wlan0",
                Name = "Wi-Fi",
                DownMb = 2.0,
                UpMb = 0.5,
                SpeedMbps = 150,
                Wireless = true,
            },
        };

        var powerSources = new List<PowerSource>();
        if (onAc)
        {
            powerSources.Add(new PowerSource("AC", PowerSourceKind.Mains, true));
        }

        return new Snapshot
        {
            Ready = ready,
            CpuTotal = 25,
            CpuTemp = cpuTemp,
            Cores = cores,
            Ram = new Ram
            {
                Usage = 40,
                UsedGb = 3.2,
                TotalGb = 8.0,
            },
            Disks = disks,
            Adapters = adapters,
            Batteries = batteries ?? Array.Empty<Battery>(),
            Peripherals = peripherals ?? Array.Empty<PeripheralBattery>(),
            PowerSources = powerSources,
        };
    }

    // =========================================================================
    // 1. OverallBoard Battery Section Tests
    // =========================================================================

    [TestMethod]
    public void Overall_BatterySection_PositionedAfterNetwork_WithKeyBattery()
    {
        var config = new AppConfig
        {
            ShowCpu = true,
            ShowRam = true,
            ShowDisk = true,
            ShowNetwork = true,
            ShowBattery = true,
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        var batteries = new[]
        {
            new Battery
            {
                Id = "BAT0",
                Model = "Primary Cell",
                Percent = 85,
                State = BatteryState.Discharging,
            },
        };

        Snapshot snapshot = CreateSnapshot(batteries: batteries);
        OverallModel model = OverallBoard.Build(snapshot, config, lang, palette, alerts);

        Assert.AreEqual(5, model.Sections.Count);
        Assert.AreEqual("cpu", model.Sections[0].Key);
        Assert.AreEqual("ram", model.Sections[1].Key);
        Assert.AreEqual("disk", model.Sections[2].Key);
        Assert.AreEqual("network", model.Sections[3].Key);

        SectionModel batterySection = model.Sections[4];
        Assert.AreEqual("battery", batterySection.Key);
        Assert.AreEqual("Battery", batterySection.Title);
        Assert.AreEqual(1, batterySection.Columns);
    }

    [TestMethod]
    public void Overall_ShowBatteryFalse_OmitsBatterySection()
    {
        var config = new AppConfig
        {
            ShowCpu = true,
            ShowRam = true,
            ShowDisk = true,
            ShowNetwork = true,
            ShowBattery = false, // Disabled
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        var batteries = new[]
        {
            new Battery
            {
                Id = "BAT0",
                Model = "Cell",
                Percent = 90,
                State = BatteryState.Charging,
            },
        };

        Snapshot snapshot = CreateSnapshot(batteries: batteries);
        OverallModel model = OverallBoard.Build(snapshot, config, lang, palette, alerts);

        Assert.IsFalse(model.Sections.Any(s => s.Key == "battery"));
    }

    [TestMethod]
    public void Overall_EmptyBatteries_OmitsBatterySection_EvenWhenPeripheralsPresent()
    {
        var config = new AppConfig
        {
            ShowBattery = true,
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        var peripherals = new[]
        {
            new PeripheralBattery
            {
                Id = "mouse-1",
                Kind = PeripheralKind.Mouse,
                Model = "Wireless Mouse",
                Percent = 75,
            },
        };

        // Batteries list is empty, only peripherals present
        Snapshot snapshot = CreateSnapshot(batteries: Array.Empty<Battery>(), peripherals: peripherals);
        OverallModel model = OverallBoard.Build(snapshot, config, lang, palette, alerts);

        Assert.IsFalse(model.Sections.Any(s => s.Key == "battery"),
            "Battery section must not appear when machine batteries list is empty, even with peripherals");
    }

    [TestMethod]
    public void Overall_BatterySection_RowOrdering_BatteriesFirstThenPeripherals()
    {
        var config = new AppConfig { ShowBattery = true };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        var batteries = new[]
        {
            new Battery { Id = "BAT0", Model = "Internal Battery", Percent = 80, State = BatteryState.Discharging },
            new Battery { Id = "BAT1", Model = "Modular Bay", Percent = 50, State = BatteryState.Discharging },
        };
        var peripherals = new[]
        {
            new PeripheralBattery { Id = "p1", Kind = PeripheralKind.Mouse, Model = "Pro Mouse", Percent = 70 },
            new PeripheralBattery { Id = "p2", Kind = PeripheralKind.Keyboard, Model = "Ergo Key", Percent = 95 },
        };

        Snapshot snapshot = CreateSnapshot(batteries: batteries, peripherals: peripherals);
        OverallModel model = OverallBoard.Build(snapshot, config, lang, palette, alerts);

        SectionModel batterySection = model.Sections.First(s => s.Key == "battery");
        Assert.AreEqual(4, batterySection.Rows.Count);

        // First two rows are system batteries
        Assert.AreEqual("Internal Battery", batterySection.Rows[0].Title);
        Assert.AreEqual(80, batterySection.Rows[0].Percent);
        Assert.AreEqual("Modular Bay", batterySection.Rows[1].Title);
        Assert.AreEqual(50, batterySection.Rows[1].Percent);

        // Next two rows are peripherals (kind as title, model as detail)
        Assert.AreEqual("Mouse", batterySection.Rows[2].Title);
        Assert.AreEqual("Pro Mouse", batterySection.Rows[2].Detail);
        Assert.AreEqual(70, batterySection.Rows[2].Percent);

        Assert.AreEqual("Keyboard", batterySection.Rows[3].Title);
        Assert.AreEqual("Ergo Key", batterySection.Rows[3].Detail);
        Assert.AreEqual(95, batterySection.Rows[3].Percent);
    }

    [TestMethod]
    public void Overall_BatteryRow_ModelVsFallbackTitle()
    {
        var config = new AppConfig { ShowBattery = true };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        var batteries = new[]
        {
            new Battery { Id = "BAT0", Model = "CustomModel", Percent = 80, State = BatteryState.Discharging },
            new Battery { Id = "BAT1", Model = null, Percent = 60, State = BatteryState.Discharging },
            new Battery { Id = "BAT2", Model = string.Empty, Percent = 40, State = BatteryState.Discharging },
        };

        Snapshot snapshot = CreateSnapshot(batteries: batteries);
        OverallModel model = OverallBoard.Build(snapshot, config, lang, palette, alerts);

        SectionModel batterySection = model.Sections.First(s => s.Key == "battery");
        Assert.AreEqual("CustomModel", batterySection.Rows[0].Title);
        Assert.AreEqual("Battery", batterySection.Rows[1].Title);
        Assert.AreEqual("Battery", batterySection.Rows[2].Title);
    }

    [TestMethod]
    public void Overall_BatteryRow_Percent_And_NullPercentYieldsNaAndZeroBar()
    {
        var config = new AppConfig { ShowBattery = true };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        var batteries = new[]
        {
            new Battery { Id = "BAT0", Percent = 88, State = BatteryState.Discharging },
            new Battery { Id = "BAT1", Percent = null, State = BatteryState.Discharging },
        };

        Snapshot snapshot = CreateSnapshot(batteries: batteries);
        OverallModel model = OverallBoard.Build(snapshot, config, lang, palette, alerts);

        SectionModel batterySection = model.Sections.First(s => s.Key == "battery");

        // Battery with percent: 88%, bar 88, accent colour
        MeterRow row0 = batterySection.Rows[0];
        Assert.AreEqual(88, row0.Percent);
        Assert.AreEqual("88%", row0.ValueText);
        Assert.AreEqual(Accents.Battery, row0.BarColour);

        // Battery with null percent: N/A, bar 0, accent colour
        MeterRow row1 = batterySection.Rows[1];
        Assert.AreEqual(0, row1.Percent);
        Assert.AreEqual("N/A", row1.ValueText);
        Assert.AreEqual(Accents.Battery, row1.BarColour);
    }

    [TestMethod]
    public void Overall_BatteryRow_PowerBadgeWhenChargingOrOnAc_NullWhenDischargingOffAc()
    {
        var config = new AppConfig { ShowBattery = true };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        var chargingBattery = new[]
        {
            new Battery { Id = "BAT0", Percent = 60, State = BatteryState.Charging },
        };
        var dischargingBattery = new[]
        {
            new Battery { Id = "BAT0", Percent = 60, State = BatteryState.Discharging },
        };

        // 1. Charging off AC -> power badge
        Snapshot snapChargingOffAc = CreateSnapshot(onAc: false, batteries: chargingBattery);
        OverallModel model1 = OverallBoard.Build(snapChargingOffAc, config, lang, palette, alerts);
        TempBadge? badge1 = model1.Sections.First(s => s.Key == "battery").Rows[0].Badge;
        Assert.IsNotNull(badge1);
        Assert.AreEqual(BadgeKind.Power, badge1.Value.Kind);
        Assert.AreEqual(Accents.Battery, badge1.Value.Fore);
        Assert.AreEqual(palette.BadgeOk, badge1.Value.Back);
        Assert.AreEqual(string.Empty, badge1.Value.Text);

        // 2. Discharging on AC -> power badge
        Snapshot snapDischargingOnAc = CreateSnapshot(onAc: true, batteries: dischargingBattery);
        OverallModel model2 = OverallBoard.Build(snapDischargingOnAc, config, lang, palette, alerts);
        TempBadge? badge2 = model2.Sections.First(s => s.Key == "battery").Rows[0].Badge;
        Assert.IsNotNull(badge2);
        Assert.AreEqual(BadgeKind.Power, badge2.Value.Kind);

        // 3. Discharging off AC -> no badge
        Snapshot snapDischargingOffAc = CreateSnapshot(onAc: false, batteries: dischargingBattery);
        OverallModel model3 = OverallBoard.Build(snapDischargingOffAc, config, lang, palette, alerts);
        TempBadge? badge3 = model3.Sections.First(s => s.Key == "battery").Rows[0].Badge;
        Assert.IsNull(badge3, "Discharging off AC must not display a power badge");
    }

    [TestMethod]
    public void Overall_PeripheralRows_KindModelPercent_AndNullPercentYieldsNaAndZeroBar()
    {
        var config = new AppConfig { ShowBattery = true };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        var batteries = new[]
        {
            new Battery { Id = "BAT0", Percent = 100, State = BatteryState.Full },
        };
        var peripherals = new[]
        {
            new PeripheralBattery { Id = "p1", Kind = PeripheralKind.Mouse, Model = "Logi Mouse", Percent = 65 },
            new PeripheralBattery { Id = "p2", Kind = PeripheralKind.Headset, Model = null, Percent = null },
        };

        Snapshot snapshot = CreateSnapshot(batteries: batteries, peripherals: peripherals);
        OverallModel model = OverallBoard.Build(snapshot, config, lang, palette, alerts);

        SectionModel section = model.Sections.First(s => s.Key == "battery");
        Assert.AreEqual(3, section.Rows.Count);

        MeterRow mouseRow = section.Rows[1];
        Assert.AreEqual("Mouse", mouseRow.Title);
        Assert.AreEqual(65, mouseRow.Percent);
        Assert.AreEqual("65%", mouseRow.ValueText);
        Assert.AreEqual("Logi Mouse", mouseRow.Detail);
        Assert.AreEqual(Accents.Battery, mouseRow.BarColour);

        MeterRow headsetRow = section.Rows[2];
        Assert.AreEqual("Headset", headsetRow.Title);
        Assert.AreEqual(0, headsetRow.Percent);
        Assert.AreEqual("N/A", headsetRow.ValueText);
        Assert.AreEqual(string.Empty, headsetRow.Detail);
        Assert.AreEqual(Accents.Battery, headsetRow.BarColour);
    }

    [TestMethod]
    public void Overall_TemperatureBadgesElsewhere_RetainTemperatureKind()
    {
        var config = new AppConfig
        {
            ShowCpu = true,
            ShowDisk = true,
            ShowBattery = true,
            CpuMode = "total",
            CpuTemperature = true,
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        var batteries = new[]
        {
            new Battery { Id = "BAT0", Percent = 80, State = BatteryState.Charging },
        };

        Snapshot snapshot = CreateSnapshot(cpuTemp: 52, diskTemp: 41, batteries: batteries);
        OverallModel model = OverallBoard.Build(snapshot, config, lang, palette, alerts);

        // CPU row badge has BadgeKind.Temperature
        SectionModel cpuSection = model.Sections.First(s => s.Key == "cpu");
        Assert.IsNotNull(cpuSection.Rows[0].Badge);
        Assert.AreEqual(BadgeKind.Temperature, cpuSection.Rows[0].Badge?.Kind);

        // Disk row badge has BadgeKind.Temperature
        SectionModel diskSection = model.Sections.First(s => s.Key == "disk");
        Assert.IsNotNull(diskSection.Rows[0].Badge);
        Assert.AreEqual(BadgeKind.Temperature, diskSection.Rows[0].Badge?.Kind);

        // Battery row badge has BadgeKind.Power
        SectionModel batterySection = model.Sections.First(s => s.Key == "battery");
        Assert.IsNotNull(batterySection.Rows[0].Badge);
        Assert.AreEqual(BadgeKind.Power, batterySection.Rows[0].Badge?.Kind);
    }

    // =========================================================================
    // 2. WidgetBoard Battery Page Tests
    // =========================================================================

    [TestMethod]
    public void Widget_BatteryPages_PositionedAfterNetworkPages()
    {
        var config = new AppConfig
        {
            ShowCpu = true,
            ShowRam = true,
            ShowDisk = true,
            ShowNetwork = true,
            ShowBattery = true,
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        var batteries = new[]
        {
            new Battery { Id = "BAT0", Percent = 90, State = BatteryState.Discharging },
        };

        Snapshot snapshot = CreateSnapshot(batteries: batteries);
        IReadOnlyList<WidgetPage> pages = WidgetBoard.Build(snapshot, config, lang, palette, alerts);

        int lastNetworkIndex = -1;
        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i].Kind is WidgetPageKind.NetworkTotal or WidgetPageKind.NetworkOne)
            {
                lastNetworkIndex = i;
            }
        }

        Assert.IsTrue(lastNetworkIndex >= 0, "Network page should be present.");

        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i].Kind == WidgetPageKind.Battery)
            {
                Assert.IsTrue(i > lastNetworkIndex, "Battery page should come after the last network page.");
            }
        }

        WidgetPage batteryPage = pages[lastNetworkIndex + 1];
        Assert.AreEqual("battery:BAT0", batteryPage.Key);
        Assert.AreEqual(WidgetPageKind.Battery, batteryPage.Kind);
    }

    [TestMethod]
    public void Widget_MultipleBatteries_ProducesOnePagePerBattery()
    {
        var config = new AppConfig { ShowBattery = true };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        var batteries = new[]
        {
            new Battery { Id = "BAT0", Percent = 80, State = BatteryState.Discharging },
            new Battery { Id = "BAT1", Percent = 45, State = BatteryState.Discharging },
        };

        Snapshot snapshot = CreateSnapshot(batteries: batteries);
        IReadOnlyList<WidgetPage> pages = WidgetBoard.Build(snapshot, config, lang, palette, alerts);

        List<WidgetPage> batteryPages = pages.Where(p => p.Kind == WidgetPageKind.Battery).ToList();
        Assert.AreEqual(2, batteryPages.Count);
        Assert.AreEqual("battery:BAT0", batteryPages[0].Key);
        Assert.AreEqual("battery:BAT1", batteryPages[1].Key);
    }

    [TestMethod]
    public void Widget_ShowBatteryFalse_OmitsBatteryPages()
    {
        var config = new AppConfig
        {
            ShowCpu = true,
            ShowBattery = false, // Disabled
        };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        var batteries = new[]
        {
            new Battery { Id = "BAT0", Percent = 95, State = BatteryState.Charging },
        };

        Snapshot snapshot = CreateSnapshot(batteries: batteries);
        IReadOnlyList<WidgetPage> pages = WidgetBoard.Build(snapshot, config, lang, palette, alerts);

        Assert.IsFalse(pages.Any(p => p.Kind == WidgetPageKind.Battery));
        Assert.IsFalse(pages.Any(p => p.Key.StartsWith("battery:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Widget_EmptyBatteries_OmitsBatteryPages()
    {
        var config = new AppConfig { ShowBattery = true };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        Snapshot snapshot = CreateSnapshot(batteries: Array.Empty<Battery>());
        IReadOnlyList<WidgetPage> pages = WidgetBoard.Build(snapshot, config, lang, palette, alerts);

        Assert.IsFalse(pages.Any(p => p.Kind == WidgetPageKind.Battery));
    }

    [TestMethod]
    public void Widget_BatteryPage_PropertiesAndSingleCompactRow()
    {
        var config = new AppConfig { ShowBattery = true };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        var batteries = new[]
        {
            new Battery
            {
                Id = "BAT0",
                Percent = 78,
                State = BatteryState.Charging,
                PowerW = 20.0,
            },
        };

        Snapshot snapshot = CreateSnapshot(batteries: batteries);
        IReadOnlyList<WidgetPage> pages = WidgetBoard.Build(snapshot, config, lang, palette, alerts);

        WidgetPage batteryPage = pages.First(p => p.Kind == WidgetPageKind.Battery);
        Assert.AreEqual("battery:BAT0", batteryPage.Key);
        Assert.AreEqual("78%", batteryPage.Value);
        Assert.AreEqual(Accents.Battery, batteryPage.Rows[0].BarColour);
        Assert.IsTrue(batteryPage.Rows[0].Compact);
        Assert.AreEqual(78, batteryPage.Rows[0].Percent);
        Assert.AreEqual(string.Empty, batteryPage.Rows[0].Title);

        Assert.IsNotNull(batteryPage.Badge);
        Assert.AreEqual(BadgeKind.Power, batteryPage.Badge?.Kind);
    }

    [TestMethod]
    public void Widget_BatteryPage_NullPercent_ShowsNaAndZeroBar()
    {
        var config = new AppConfig { ShowBattery = true };
        var lang = new Lang("en");
        var palette = Palette.Light;
        var alerts = new DiskAlertTracker();

        var batteries = new[]
        {
            new Battery { Id = "BAT0", Percent = null, State = BatteryState.Discharging },
        };

        Snapshot snapshot = CreateSnapshot(batteries: batteries);
        IReadOnlyList<WidgetPage> pages = WidgetBoard.Build(snapshot, config, lang, palette, alerts);

        WidgetPage batteryPage = pages.First(p => p.Kind == WidgetPageKind.Battery);
        Assert.AreEqual("N/A", batteryPage.Value);
        Assert.AreEqual(0, batteryPage.Rows[0].Percent);
    }

    // =========================================================================
    // 3. BatteryText Helper Tests
    // =========================================================================

    [TestMethod]
    public void BatteryText_Detail_TimeToEmptyVsTimeToFull_Precedence()
    {
        var lang = new Lang("en");
        var snapshot = CreateSnapshot();

        // Battery with BOTH TimeToEmpty and TimeToFull populated
        var batteryBothTimes = new Battery
        {
            Id = "BAT0",
            State = BatteryState.Discharging,
            TimeToEmpty = TimeSpan.FromHours(2) + TimeSpan.FromMinutes(15),
            TimeToFull = TimeSpan.FromHours(1) + TimeSpan.FromMinutes(30),
        };

        string detail = BatteryText.Detail(batteryBothTimes, snapshot, lang);

        // TimeToEmpty must take precedence over TimeToFull
        StringAssert.Contains(detail, "Time left 2h 15m");
        Assert.IsFalse(detail.Contains("Time to full", StringComparison.Ordinal),
            "Detail should not contain Time to full when TimeToEmpty is present");
    }

    [TestMethod]
    public void BatteryText_Detail_OnlyTimeToFull_WhenTimeToEmptyIsNull()
    {
        var lang = new Lang("en");
        var snapshot = CreateSnapshot();

        var batteryTimeToFullOnly = new Battery
        {
            Id = "BAT0",
            State = BatteryState.Charging,
            TimeToEmpty = null,
            TimeToFull = TimeSpan.FromHours(1) + TimeSpan.FromMinutes(30),
        };

        string detail = BatteryText.Detail(batteryTimeToFullOnly, snapshot, lang);

        StringAssert.Contains(detail, "Time to full 1h 30m");
        Assert.IsFalse(detail.Contains("Time left", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BatteryText_Detail_NullOmission_OnlyStatePresent()
    {
        var lang = new Lang("en");
        var snapshot = CreateSnapshot();

        // All nullable metrics left null
        var batteryBare = new Battery
        {
            Id = "BAT0",
            State = BatteryState.Discharging,
            PowerW = null,
            TimeToEmpty = null,
            TimeToFull = null,
            EnergyFullWh = null,
            EnergyDesignWh = null,
        };

        string detail = BatteryText.Detail(batteryBare, snapshot, lang);

        // Should produce solely the state name without stray dots or null strings
        Assert.AreEqual("Discharging", detail);
    }

    [TestMethod]
    public void BatteryText_Detail_SelectiveInclusion_FormatsCorrectly()
    {
        var lang = new Lang("en");
        var snapshot = CreateSnapshot();

        // 1. All fields present
        var batteryAll = new Battery
        {
            Id = "BAT0",
            State = BatteryState.Charging,
            PowerW = 25.5,
            TimeToFull = TimeSpan.FromMinutes(45),
            EnergyFullWh = 47.0,
            EnergyDesignWh = 50.0, // 47 / 50 * 100 = 94% health
        };

        string detailAll = BatteryText.Detail(batteryAll, snapshot, lang);
        Assert.AreEqual("Charging · Power 25.5 W · Time to full 45m · Battery health 94%", detailAll);

        // 2. Power omitted, Time left and health present
        var batteryNoPower = new Battery
        {
            Id = "BAT0",
            State = BatteryState.Discharging,
            PowerW = null,
            TimeToEmpty = TimeSpan.FromHours(3),
            EnergyFullWh = 44.0,
            EnergyDesignWh = 50.0, // 88% health
        };

        string detailNoPower = BatteryText.Detail(batteryNoPower, snapshot, lang);
        Assert.AreEqual("Discharging · Time left 3h 0m · Battery health 88%", detailNoPower);

        // 3. Time omitted, Power and health present
        var batteryNoTime = new Battery
        {
            Id = "BAT0",
            State = BatteryState.Full,
            PowerW = 0.5,
            TimeToEmpty = null,
            TimeToFull = null,
            EnergyFullWh = 50.0,
            EnergyDesignWh = 50.0, // 100% health
        };

        string detailNoTime = BatteryText.Detail(batteryNoTime, snapshot, lang);
        Assert.AreEqual("Full · Power 0.5 W · Battery health 100%", detailNoTime);
    }

    [TestMethod]
    public void BatteryText_AcBadge_TruthTable_AllCombinations()
    {
        var palette = Palette.Light;
        var onAcSnapshot = CreateSnapshot(onAc: true);
        var offAcSnapshot = CreateSnapshot(onAc: false);

        // 1. Charging + OnAcPower -> BadgeKind.Power
        var chargingBat = new Battery { Id = "BAT0", State = BatteryState.Charging };
        TempBadge? badge1 = BatteryText.AcBadge(chargingBat, onAcSnapshot, palette);
        Assert.IsNotNull(badge1);
        Assert.AreEqual(BadgeKind.Power, badge1.Value.Kind);
        Assert.AreEqual(Accents.Battery, badge1.Value.Fore);
        Assert.AreEqual(palette.BadgeOk, badge1.Value.Back);
        Assert.AreEqual(string.Empty, badge1.Value.Text);
        Assert.IsFalse(badge1.Value.Hot);
        Assert.IsFalse(badge1.Value.Estimated);

        // 2. Charging + Off AC -> BadgeKind.Power
        TempBadge? badge2 = BatteryText.AcBadge(chargingBat, offAcSnapshot, palette);
        Assert.IsNotNull(badge2);
        Assert.AreEqual(BadgeKind.Power, badge2.Value.Kind);

        // 3. Discharging + OnAcPower -> BadgeKind.Power
        var dischargingBat = new Battery { Id = "BAT0", State = BatteryState.Discharging };
        TempBadge? badge3 = BatteryText.AcBadge(dischargingBat, onAcSnapshot, palette);
        Assert.IsNotNull(badge3);
        Assert.AreEqual(BadgeKind.Power, badge3.Value.Kind);

        // 4. Discharging + Off AC -> null
        TempBadge? badge4 = BatteryText.AcBadge(dischargingBat, offAcSnapshot, palette);
        Assert.IsNull(badge4);

        // 5. Full + Off AC -> null; Full + OnAcPower -> BadgeKind.Power
        var fullBat = new Battery { Id = "BAT0", State = BatteryState.Full };
        Assert.IsNull(BatteryText.AcBadge(fullBat, offAcSnapshot, palette));
        Assert.IsNotNull(BatteryText.AcBadge(fullBat, onAcSnapshot, palette));

        // 6. NotCharging + Off AC -> null; NotCharging + OnAcPower -> BadgeKind.Power
        var notChargingBat = new Battery { Id = "BAT0", State = BatteryState.NotCharging };
        Assert.IsNull(BatteryText.AcBadge(notChargingBat, offAcSnapshot, palette));
        Assert.IsNotNull(BatteryText.AcBadge(notChargingBat, onAcSnapshot, palette));

        // 7. Unknown + Off AC -> null; Unknown + OnAcPower -> BadgeKind.Power
        var unknownBat = new Battery { Id = "BAT0", State = BatteryState.Unknown };
        Assert.IsNull(BatteryText.AcBadge(unknownBat, offAcSnapshot, palette));
        Assert.IsNotNull(BatteryText.AcBadge(unknownBat, onAcSnapshot, palette));
    }

    [TestMethod]
    public void BatteryText_Charge_FormatsPercentOrNa()
    {
        var lang = new Lang("en");

        var batWithValue = new Battery { Id = "BAT0", Percent = 75 };
        Assert.AreEqual("75%", BatteryText.Charge(batWithValue, lang));

        var batNull = new Battery { Id = "BAT0", Percent = null };
        Assert.AreEqual("N/A", BatteryText.Charge(batNull, lang));
    }

    [TestMethod]
    public void BatteryText_PeripheralName_ReturnsExpectedLocalisedNames()
    {
        var langEn = new Lang("en");
        Assert.AreEqual("Mouse", BatteryText.PeripheralName(PeripheralKind.Mouse, langEn));
        Assert.AreEqual("Keyboard", BatteryText.PeripheralName(PeripheralKind.Keyboard, langEn));
        Assert.AreEqual("Headset", BatteryText.PeripheralName(PeripheralKind.Headset, langEn));
        Assert.AreEqual("Gamepad", BatteryText.PeripheralName(PeripheralKind.Gamepad, langEn));
        Assert.AreEqual("Phone", BatteryText.PeripheralName(PeripheralKind.Phone, langEn));
        Assert.AreEqual("Tablet", BatteryText.PeripheralName(PeripheralKind.Tablet, langEn));
        Assert.AreEqual("Other device", BatteryText.PeripheralName(PeripheralKind.Other, langEn));
        Assert.AreEqual("Other device", BatteryText.PeripheralName((PeripheralKind)999, langEn));

        var langTh = new Lang("th");
        Assert.AreEqual("เมาส์", BatteryText.PeripheralName(PeripheralKind.Mouse, langTh));
        Assert.AreEqual("คีย์บอร์ด", BatteryText.PeripheralName(PeripheralKind.Keyboard, langTh));
        Assert.AreEqual("หูฟัง", BatteryText.PeripheralName(PeripheralKind.Headset, langTh));
        Assert.AreEqual("เกมแพด", BatteryText.PeripheralName(PeripheralKind.Gamepad, langTh));
        Assert.AreEqual("โทรศัพท์", BatteryText.PeripheralName(PeripheralKind.Phone, langTh));
        Assert.AreEqual("แท็บเล็ต", BatteryText.PeripheralName(PeripheralKind.Tablet, langTh));
        Assert.AreEqual("อุปกรณ์อื่น", BatteryText.PeripheralName(PeripheralKind.Other, langTh));
    }
}
