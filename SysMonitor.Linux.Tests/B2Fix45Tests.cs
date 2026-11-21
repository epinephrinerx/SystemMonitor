using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class B2Fix45Tests
{
    private static Snapshot CreateSnapshot(
        IReadOnlyList<Core>? cores = null,
        IReadOnlyList<Battery>? batteries = null,
        bool onAc = false,
        bool cpuTempEstimated = false)
    {
        var powerSources = new List<PowerSource>();
        if (onAc)
        {
            powerSources.Add(new PowerSource("AC", PowerSourceKind.Mains, true));
        }

        return new Snapshot
        {
            Ready = true,
            CpuTotal = 25,
            CpuTemp = 55,
            CpuTempEstimated = cpuTempEstimated,
            CpuInfo = new CpuInfo
            {
                Name = "Test CPU",
                Cores = cores?.Count ?? 2,
                Logical = cores?.Count ?? 2,
                BaseMhz = 1200,
            },
            Cores = cores ?? new List<Core>
            {
                new() { Usage = 22, Temp = 85, Mhz = 1200 },
                new() { Usage = 15, Temp = 70, Mhz = null },
            },
            Ram = new Ram { Usage = 40, UsedGb = 3.2, TotalGb = 8.0 },
            Disks = new[]
            {
                new Disk
                {
                    Letter = "/",
                    Usage = 50,
                    Detail = new DriveDetail
                    {
                        Letter = "/",
                        Disk = new PhysicalDisk { Name = "nvme0n1", Model = "NVMe SSD", Online = true },
                    },
                },
            },
            Adapters = new[]
            {
                new Adapter { Id = "eth0", Name = "Ethernet", DownMb = 1.0, UpMb = 0.5 },
            },
            Batteries = batteries ?? Array.Empty<Battery>(),
            PowerSources = powerSources,
        };
    }

    // -------------------------------------------------------------------------
    // 1. CPU page small core cards
    // -------------------------------------------------------------------------

    [TestMethod]
    public void CpuPage_SmallCoreCards_TitlesAreC0C1_ValueHasPercentTempAndClock_NoGhzWhenNull()
    {
        var cores = new List<Core>
        {
            new() { Usage = 22, Temp = 85, Mhz = 1200 },
            new() { Usage = 45, Temp = 60, Mhz = null },
            new() { Usage = 10, Temp = 40, Mhz = 2400 },
        };
        Snapshot snapshot = CreateSnapshot(cores: cores);

        var config = new AppConfig
        {
            ShowCpu = true,
            CpuMode = "separated", // per-core small cards shown
            CpuTemperature = true,
        };
        var lang = new Lang("en");
        var palette = Palette.Dark;
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel model = FullBoard.Build(
            snapshot, config, lang, palette, history, selection, rememberedTab: PageKeys.Cpu);

        FullTab cpuTab = model.Tabs.First(t => t.Key == PageKeys.Cpu);
        Assert.AreEqual(3, cpuTab.SmallCards.Count);

        // Core 0: C0, 22%, 85°C, 1.2 GHz
        FullCard card0 = cpuTab.SmallCards[0];
        Assert.AreEqual("C0", card0.Title);
        Assert.AreEqual("core0", card0.Key);
        StringAssert.Contains(card0.Value, "22%");
        StringAssert.Contains(card0.Value, "85°C");
        StringAssert.Contains(card0.Value, "1.2 GHz");

        // Core 1: C1, 45%, 60°C, Mhz is null -> no GHz
        FullCard card1 = cpuTab.SmallCards[1];
        Assert.AreEqual("C1", card1.Title);
        Assert.AreEqual("core1", card1.Key);
        StringAssert.Contains(card1.Value, "45%");
        StringAssert.Contains(card1.Value, "60°C");
        Assert.IsFalse(card1.Value.Contains("GHz", StringComparison.OrdinalIgnoreCase),
            "Card value must not contain GHz when Mhz is null");

        // Core 2: C2, 10%, 40°C, 2.4 GHz
        FullCard card2 = cpuTab.SmallCards[2];
        Assert.AreEqual("C2", card2.Title);
        Assert.AreEqual("core2", card2.Key);
        StringAssert.Contains(card2.Value, "10%");
        StringAssert.Contains(card2.Value, "40°C");
        StringAssert.Contains(card2.Value, "2.4 GHz");
    }

    [TestMethod]
    public void CpuPage_SmallCoreCards_WhenCpuTemperatureFalse_OmitsTemperatureFromValue()
    {
        var cores = new List<Core>
        {
            new() { Usage = 22, Temp = 85, Mhz = 1200 },
        };
        Snapshot snapshot = CreateSnapshot(cores: cores);

        var config = new AppConfig
        {
            ShowCpu = true,
            CpuMode = "separated",
            CpuTemperature = false, // Temperature disabled
        };
        var lang = new Lang("en");
        var palette = Palette.Dark;
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel model = FullBoard.Build(
            snapshot, config, lang, palette, history, selection, rememberedTab: PageKeys.Cpu);

        FullTab cpuTab = model.Tabs.First(t => t.Key == PageKeys.Cpu);
        FullCard card0 = cpuTab.SmallCards[0];
        Assert.AreEqual("C0", card0.Title);
        StringAssert.Contains(card0.Value, "22%");
        StringAssert.Contains(card0.Value, "1.2 GHz");
        Assert.IsFalse(card0.Value.Contains("°C", StringComparison.Ordinal),
            "Card value must not contain temperature when CpuTemperature is false");
    }

    // -------------------------------------------------------------------------
    // 2. Battery page summary/value and sidebar battery value contain no ⚡;
    //    badge is BadgeKind.Power while charging / on AC, null when discharging off AC
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Battery_FullTabAndSidebarItem_ContainNoLightningGlyph_InSummaryOrValue()
    {
        var batteries = new[]
        {
            new Battery { Id = "BAT0", Percent = 80, State = BatteryState.Charging },
            new Battery { Id = "BAT1", Percent = null, State = BatteryState.Discharging },
        };
        Snapshot snapshot = CreateSnapshot(batteries: batteries, onAc: true);

        var config = new AppConfig { ShowBattery = true };
        var lang = new Lang("en");
        var palette = Palette.Dark;
        var history = new HistoryBoard();
        var selection = new FullSelection();

        FullModel modules = FullBoard.Build(
            snapshot, config, lang, palette, history, selection, rememberedTab: "");
        IReadOnlyList<SidebarItem> sidebarItems = SidebarBoard.Build(modules, snapshot, history, lang);

        FullTab tab0 = modules.Tabs.First(t => t.Key == "battery:BAT0");
        Assert.IsFalse(tab0.Summary.Contains("⚡", StringComparison.Ordinal), "Summary must not contain ⚡");
        Assert.IsFalse(tab0.BigCards[0].Value.Contains("⚡", StringComparison.Ordinal), "BigCard Value must not contain ⚡");
        StringAssert.Contains(tab0.Summary, "80%");

        SidebarItem item0 = sidebarItems.First(i => i.Key == "battery:BAT0");
        Assert.IsFalse(item0.Value.Contains("⚡", StringComparison.Ordinal), "Sidebar item Value must not contain ⚡");
        StringAssert.Contains(item0.Value, "80%");

        FullTab tab1 = modules.Tabs.First(t => t.Key == "battery:BAT1");
        Assert.IsFalse(tab1.Summary.Contains("⚡", StringComparison.Ordinal), "Null percent summary must not contain ⚡");
        SidebarItem item1 = sidebarItems.First(i => i.Key == "battery:BAT1");
        Assert.IsFalse(item1.Value.Contains("⚡", StringComparison.Ordinal), "Null percent sidebar value must not contain ⚡");
    }

    [TestMethod]
    public void Battery_Badge_IsPowerKindWhileChargingOrOnAc_AndNullWhenDischargingOffAc()
    {
        var chargingBat = new[]
        {
            new Battery { Id = "BAT0", Percent = 70, State = BatteryState.Charging },
        };
        var dischargingBat = new[]
        {
            new Battery { Id = "BAT0", Percent = 70, State = BatteryState.Discharging },
        };

        var config = new AppConfig { ShowBattery = true };
        var lang = new Lang("en");
        var palette = Palette.Dark;
        var history = new HistoryBoard();
        var selection = new FullSelection();

        // 1. Charging off AC -> BadgeKind.Power on both FullTab and SidebarItem
        Snapshot snapChargingOffAc = CreateSnapshot(batteries: chargingBat, onAc: false);
        FullModel model1 = FullBoard.Build(snapChargingOffAc, config, lang, palette, history, selection, "");
        IReadOnlyList<SidebarItem> items1 = SidebarBoard.Build(model1, snapChargingOffAc, history, lang);

        FullTab tab1 = model1.Tabs.First(t => t.Key == "battery:BAT0");
        SidebarItem item1 = items1.First(i => i.Key == "battery:BAT0");
        Assert.IsNotNull(tab1.Badge, "Tab badge should exist when charging");
        Assert.AreEqual(BadgeKind.Power, tab1.Badge.Value.Kind);
        Assert.IsNotNull(item1.Badge, "Sidebar badge should exist when charging");
        Assert.AreEqual(BadgeKind.Power, item1.Badge.Value.Kind);

        // 2. Discharging on AC -> BadgeKind.Power on both FullTab and SidebarItem
        Snapshot snapDischargingOnAc = CreateSnapshot(batteries: dischargingBat, onAc: true);
        FullModel model2 = FullBoard.Build(snapDischargingOnAc, config, lang, palette, history, selection, "");
        IReadOnlyList<SidebarItem> items2 = SidebarBoard.Build(model2, snapDischargingOnAc, history, lang);

        FullTab tab2 = model2.Tabs.First(t => t.Key == "battery:BAT0");
        SidebarItem item2 = items2.First(i => i.Key == "battery:BAT0");
        Assert.IsNotNull(tab2.Badge, "Tab badge should exist when on AC");
        Assert.AreEqual(BadgeKind.Power, tab2.Badge.Value.Kind);
        Assert.IsNotNull(item2.Badge, "Sidebar badge should exist when on AC");
        Assert.AreEqual(BadgeKind.Power, item2.Badge.Value.Kind);

        // 3. Discharging off AC -> null on both FullTab and SidebarItem
        Snapshot snapDischargingOffAc = CreateSnapshot(batteries: dischargingBat, onAc: false);
        FullModel model3 = FullBoard.Build(snapDischargingOffAc, config, lang, palette, history, selection, "");
        IReadOnlyList<SidebarItem> items3 = SidebarBoard.Build(model3, snapDischargingOffAc, history, lang);

        FullTab tab3 = model3.Tabs.First(t => t.Key == "battery:BAT0");
        SidebarItem item3 = items3.First(i => i.Key == "battery:BAT0");
        Assert.IsNull(tab3.Badge, "Tab badge must be null when discharging off AC");
        Assert.IsNull(item3.Badge, "Sidebar badge must be null when discharging off AC");
    }

    // -------------------------------------------------------------------------
    // 3. Settings expander "settings_more"
    // -------------------------------------------------------------------------

    [TestMethod]
    public void SettingsExpander_KeySettingsMore_ExistsInBothLanguages()
    {
        var langEn = new Lang("en");
        Assert.IsTrue(langEn.Has("settings_more"), "Language 'en' must contain key 'settings_more'");
        Assert.AreEqual("More settings…", langEn["settings_more"]);

        var langTh = new Lang("th");
        Assert.IsTrue(langTh.Has("settings_more"), "Language 'th' must contain key 'settings_more'");
        Assert.AreEqual("ตั้งค่าเพิ่มเติม…", langTh["settings_more"]);
    }

    [TestMethod]
    public void SettingsExpander_OpenSettingsRow_LabelledMoreSettings_InCurrentLanguage()
    {
        // In English
        var modelEn = new ShellModel(new AppConfig { Lang = "en" });
        NavRow? settingsRowEn = modelEn.Sidebar.OfType<NavRow>()
            .FirstOrDefault(r => r.Action == ActionKeys.OpenSettings);
        Assert.IsNotNull(settingsRowEn, "OpenSettings nav row must exist in Sidebar");
        Assert.AreEqual(IconName.Settings, settingsRowEn.Icon);
        Assert.AreEqual("More settings…", settingsRowEn.Label);

        // In Thai
        var modelTh = new ShellModel(new AppConfig { Lang = "th" });
        NavRow? settingsRowTh = modelTh.Sidebar.OfType<NavRow>()
            .FirstOrDefault(r => r.Action == ActionKeys.OpenSettings);
        Assert.IsNotNull(settingsRowTh, "OpenSettings nav row must exist in Sidebar");
        Assert.AreEqual(IconName.Settings, settingsRowTh.Icon);
        Assert.AreEqual("ตั้งค่าเพิ่มเติม…", settingsRowTh.Label);
    }

    // -------------------------------------------------------------------------
    // 4. Fix 5: ShellModel sidebar/expander rows contain no Restart, Manual or About
    //    while retaining heading, six show_* checks, auto-check-updates, OpenSettings and Updates action
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ShellModel_SidebarExpander_ContainsNoRestartManualOrAbout_RetainsExpectedRows()
    {
        var model = new ShellModel();
        Snapshot snapshot = CreateSnapshot();
        model.SetSnapshot(snapshot);

        IReadOnlyList<UiRow> sidebar = model.Sidebar;

        // 1. Must NOT contain Restart, Manual or About rows
        // By Action
        Assert.IsFalse(sidebar.OfType<ActionRow>().Any(a => a.Action == ActionKeys.Restart),
            "Sidebar expander must not contain ActionKeys.Restart");
        Assert.IsFalse(sidebar.OfType<NavRow>().Any(n => n.Action == ActionKeys.Restart),
            "Sidebar expander must not contain ActionKeys.Restart in NavRow");
        Assert.IsFalse(sidebar.OfType<ActionRow>().Any(a => a.Action == ActionKeys.OpenManual),
            "Sidebar expander must not contain ActionKeys.OpenManual");
        Assert.IsFalse(sidebar.OfType<NavRow>().Any(n => n.Action == ActionKeys.OpenManual),
            "Sidebar expander must not contain ActionKeys.OpenManual in NavRow");
        Assert.IsFalse(sidebar.OfType<ActionRow>().Any(a => a.Action == ActionKeys.OpenAbout),
            "Sidebar expander must not contain ActionKeys.OpenAbout");
        Assert.IsFalse(sidebar.OfType<NavRow>().Any(n => n.Action == ActionKeys.OpenAbout),
            "Sidebar expander must not contain ActionKeys.OpenAbout in NavRow");

        // By Icon
        Assert.IsFalse(sidebar.OfType<NavRow>().Any(n => n.Icon is IconName.Restart or IconName.Manual or IconName.About),
            "Sidebar expander must not contain NavRow with Restart, Manual, or About icons");

        // By Label
        string restartLabel = model.Language["restart"];
        string manualLabel = model.Language["manual"];
        string aboutLabel = model.Language["about"];
        foreach (UiRow row in sidebar)
        {
            if (row is ActionRow a)
            {
                Assert.AreNotEqual(restartLabel, a.Label);
                Assert.AreNotEqual(manualLabel, a.Label);
                Assert.AreNotEqual(aboutLabel, a.Label);
            }
            else if (row is NavRow n)
            {
                Assert.AreNotEqual(restartLabel, n.Label);
                Assert.AreNotEqual(manualLabel, n.Label);
                Assert.AreNotEqual(aboutLabel, n.Label);
            }
        }

        // 2. Must still contain window heading
        HeadingRow? windowHeading = sidebar.OfType<HeadingRow>()
            .FirstOrDefault(h => string.Equals(h.Text, model.Language["window_settings"], StringComparison.Ordinal));
        Assert.IsNotNull(windowHeading, "Sidebar expander must contain window_settings heading");

        // 3. Must still contain the six show_* checks
        var checkRows = sidebar.OfType<CheckRow>().ToList();
        var checkKeys = checkRows.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(checkKeys.Contains(SettingKeys.ShowCpu), "Must contain show_cpu");
        Assert.IsTrue(checkKeys.Contains(SettingKeys.ShowRam), "Must contain show_ram");
        Assert.IsTrue(checkKeys.Contains(SettingKeys.ShowDisk), "Must contain show_disk");
        Assert.IsTrue(checkKeys.Contains(SettingKeys.ShowGpu), "Must contain show_gpu");
        Assert.IsTrue(checkKeys.Contains(SettingKeys.ShowNetwork), "Must contain show_network");
        Assert.IsTrue(checkKeys.Contains(SettingKeys.ShowBattery), "Must contain show_battery");

        // 4. Must still contain auto-check-updates
        Assert.IsTrue(checkKeys.Contains(SettingKeys.AutoCheckUpdates), "Must contain auto_check_updates check");

        // 5. Must still contain OpenSettings nav row
        NavRow? openSettingsRow = sidebar.OfType<NavRow>()
            .FirstOrDefault(n => n.Action == ActionKeys.OpenSettings);
        Assert.IsNotNull(openSettingsRow, "Sidebar expander must contain OpenSettings nav row");

        // 6. Must still contain Updates action
        ActionRow? updatesAction = sidebar.OfType<ActionRow>()
            .FirstOrDefault(a => a.Action is ActionKeys.CheckUpdates or ActionKeys.DownloadInstall);
        Assert.IsNotNull(updatesAction, "Sidebar expander must contain Updates action row");
    }
}
