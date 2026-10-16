using System.Windows.Media;
using SysMonitor.Extensibility;
using SysMonitor.Extensions;
using SysMonitor.Model;
using SysMonitor.ViewModels;

namespace SysMonitor.Tests.Extensions;

[TestClass]
public class WidgetViewModelExtensionsTests
{
    [TestMethod]
    public void WidgetViewModel_NoExtensions_BehavesIdenticallyToOriginal()
    {
        var config = new AppConfig
        {
            ShowCpu = true,
            ShowRam = true,
            ShowDisk = true,
            ShowNetwork = true,
            ShowSmart = false,
        };

        var model = new WidgetViewModel(config);
        var snap = new Snapshot
        {
            Ready = true,
            Extensions = Array.Empty<ExtensionViewData>()
        };

        model.RebuildViews(snap);
        model.UpdateWidget(snap);
        model.UpdateOverall(snap);
        model.PushHistory(snap);

        // Core sections present and match standard CPU heading without any extension views
        Assert.IsTrue(model.ViewCount > 0);
        StringAssert.StartsWith(model.WidgetTitle, "CPU");
        Assert.IsTrue(model.Sections.Count > 0);
        Assert.IsFalse(model.Tabs.Any(t => t.Key.StartsWith("ext:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void WidgetViewModel_WithExtensions_RendersWidgetPageAndOverallSection()
    {
        var config = new AppConfig
        {
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = false,
            ShowNetwork = false,
            ShowSmart = false,
        };

        var model = new WidgetViewModel(config);

        var extView = new ExtensionView(
            new[]
            {
                new WidgetPage("GPU TEMP", "45°C", "Fan 30%", 45, 45, Severity.Good)
            },
            new OverviewSection(
                "Normal",
                new[]
                {
                    new OverviewRow("Pump", "Optimal", "2200 RPM", 70, Severity.Good, 30)
                }),
            new[]
            {
                new DetailTab(
                    "cooling",
                    "Liquid Cooler",
                    "Pump 2200 RPM",
                    "AIO 360mm",
                    "AIO Cooler",
                    new[]
                    {
                        new Fact("Coolant Temp", "32°C"),
                        new Fact("Flow Rate", "1.5 L/min")
                    })
            });

        var extData = new ExtensionViewData("cooler_ext", "AIO Cooler", extView);
        var snap = new Snapshot
        {
            Ready = true,
            Extensions = new[] { extData }
        };

        model.RebuildViews(snap);
        Assert.AreEqual(1, model.ViewCount);

        model.UpdateWidget(snap);
        Assert.AreEqual("GPU TEMP", model.WidgetTitle);
        Assert.AreEqual("45°C", model.WidgetValue);
        Assert.AreEqual("Fan 30%", model.WidgetDetail);
        Assert.AreEqual(45, model.WidgetHeader.Temp);
        Assert.AreEqual(1, model.WidgetRows.Count);
        Assert.AreEqual(45, model.WidgetRows[0].Percent);

        model.UpdateOverall(snap);
        Assert.AreEqual(1, model.Sections.Count);
        Section section = model.Sections[0];
        Assert.AreEqual("AIO Cooler", section.Title);
        Assert.AreEqual("Normal", section.Note);
        Assert.AreEqual(1, section.Rows.Count);
        Assert.AreEqual("Pump", section.Rows[0].Title);
        Assert.AreEqual("2200 RPM", section.Rows[0].ValueText);

        model.PushHistory(snap);
        Assert.AreEqual(1, model.Tabs.Count);
        DeviceTab tab = model.Tabs[0];
        Assert.AreEqual("ext:cooler_ext:cooling", tab.Key);
        Assert.AreEqual("Liquid Cooler", tab.Title);
        Assert.AreEqual("AIO Cooler", tab.Hardware);
        Assert.AreEqual(2, tab.Facts.Count);
        Assert.AreEqual("Coolant Temp", tab.Facts[0].Name);
        Assert.AreEqual("32°C", tab.Facts[0].Value);
    }

    [TestMethod]
    public void WidgetViewModel_ReconcileTabs_RemembersExtensionTabFallback()
    {
        var config = new AppConfig
        {
            ShowCpu = true,
            FullTab = "ext:my_ext:old_sub_tab"
        };

        var model = new WidgetViewModel(config);

        var extView = new ExtensionView(
            Array.Empty<WidgetPage>(),
            null,
            new[]
            {
                new DetailTab(
                    "new_sub_tab",
                    "New Sub Tab",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    Array.Empty<Fact>())
            });

        var extData = new ExtensionViewData("my_ext", "My Extension", extView);
        var snap = new Snapshot
        {
            Ready = true,
            Extensions = new[] { extData }
        };

        model.PushHistory(snap);

        // Fallback rule: ext:my_ext:old_sub_tab was not found,
        // so it must fall back to the first tab matching ext:my_ext: (which is ext:my_ext:new_sub_tab)
        Assert.IsNotNull(model.SelectedTab);
        Assert.AreEqual("ext:my_ext:new_sub_tab", model.SelectedTab.Key);
    }

    [TestMethod]
    public void WidgetViewModel_RebuildsWhenPageSetChangesWithSameCount()
    {
        var config = new AppConfig
        {
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = false,
            ShowNetwork = false,
        };
        var model = new WidgetViewModel(config);

        var view1 = new ExtensionView(
            new[]
            {
                new WidgetPage("PAGE 1", "OK", "Line 1", null, 10, Severity.Good),
                new WidgetPage("PAGE 2", "OK", "Line 2", null, 20, Severity.Good)
            },
            null,
            Array.Empty<DetailTab>());

        var snap1 = new Snapshot
        {
            Ready = true,
            Extensions = new[] { new ExtensionViewData("ext_a", "Ext A", view1) }
        };

        model.RebuildViews(snap1);
        Assert.AreEqual(2, model.ViewCount);
        model.UpdateWidget(snap1);
        Assert.AreEqual("PAGE 1", model.WidgetTitle);

        // Switch to different extension with the same page count (2 pages)
        var view2 = new ExtensionView(
            new[]
            {
                new WidgetPage("PAGE X", "OK", "Line X", null, 80, Severity.Good),
                new WidgetPage("PAGE Y", "OK", "Line Y", null, 90, Severity.Good)
            },
            null,
            Array.Empty<DetailTab>());

        var snap2 = new Snapshot
        {
            Ready = true,
            Extensions = new[] { new ExtensionViewData("ext_b", "Ext B", view2) }
        };

        // Feed the second snapshot through UpdateWidget (as the app does) and assert the pages were rebuilt
        model.UpdateWidget(snap2);
        Assert.AreEqual(2, model.ViewCount);
        Assert.AreEqual("PAGE X", model.WidgetTitle);
    }

    [TestMethod]
    public void WidgetViewModel_ExtensionPagesDisappearWhenExtensionDisabled()
    {
        var config = new AppConfig
        {
            ShowCpu = true,
            ShowRam = false,
            ShowDisk = false,
            ShowNetwork = false,
        };
        var model = new WidgetViewModel(config);

        var extView = new ExtensionView(
            new[] { new WidgetPage("EXT PAGE", "OK", "Line", null, 50, Severity.Good) },
            new OverviewSection("Note", new[] { new OverviewRow("Row", "D", "V", 50, Severity.Good, null) }),
            new[] { new DetailTab("tab1", "T", "S", "D", "H", Array.Empty<Fact>()) });

        var snapEnabled = new Snapshot
        {
            Ready = true,
            Extensions = new[] { new ExtensionViewData("ext_1", "Ext 1", extView) }
        };

        model.RebuildViews(snapEnabled);
        model.UpdateOverall(snapEnabled);
        model.PushHistory(snapEnabled);

        int viewsBefore = model.ViewCount;
        int sectionsBefore = model.Sections.Count;
        int tabsBefore = model.Tabs.Count;

        // Extension disabled: snapshot has no extension views
        var snapDisabled = new Snapshot
        {
            Ready = true,
            Extensions = Array.Empty<ExtensionViewData>()
        };

        model.RebuildViews(snapDisabled);
        model.UpdateOverall(snapDisabled);
        model.PushHistory(snapDisabled);

        Assert.AreEqual(viewsBefore - 1, model.ViewCount);
        Assert.AreEqual(sectionsBefore - 1, model.Sections.Count);
        Assert.AreEqual(tabsBefore - 1, model.Tabs.Count);
        Assert.IsFalse(model.Tabs.Any(t => t.Key.StartsWith("ext:ext_1:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void WidgetViewModel_PageWithMessage_HasNoPercentBarValue()
    {
        var config = new AppConfig
        {
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = false,
            ShowNetwork = false,
        };
        var model = new WidgetViewModel(config);

        var extView = new ExtensionView(
            new[]
            {
                new WidgetPage("DRIVE", "SLEEP", "Standby", null, 75, Severity.Neutral, Message: "Drive is sleeping")
            },
            null,
            Array.Empty<DetailTab>());

        var snap = new Snapshot
        {
            Ready = true,
            Extensions = new[] { new ExtensionViewData("ext_msg", "Ext Msg", extView) }
        };

        model.RebuildViews(snap);
        model.UpdateWidget(snap);

        Assert.IsTrue(model.ShowMessage);
        Assert.AreEqual("Drive is sleeping", model.Message);
        Assert.AreEqual(0, model.WidgetRows.Count);
    }

    [TestMethod]
    public void WidgetViewModel_Severity_MapsToExpectedColorBrushes()
    {
        var config = new AppConfig
        {
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = false,
            ShowNetwork = false,
        };
        var model = new WidgetViewModel(config);

        var extView = new ExtensionView(
            Array.Empty<WidgetPage>(),
            new OverviewSection("Severities", new[]
            {
                new OverviewRow("Good", "D", "V", 10, Severity.Good, null),
                new OverviewRow("Warning", "D", "V", 50, Severity.Caution, null),
                new OverviewRow("Critical", "D", "V", 90, Severity.Bad, null),
                new OverviewRow("Neutral", "D", "V", 0, Severity.Neutral, null),
            }),
            Array.Empty<DetailTab>());

        var snap = new Snapshot
        {
            Ready = true,
            Extensions = new[] { new ExtensionViewData("ext_sev", "Ext Sev", extView) }
        };

        model.UpdateOverall(snap);
        Assert.AreEqual(1, model.Sections.Count);
        var rows = model.Sections[0].Rows;
        Assert.AreEqual(4, rows.Count);

        // Verify colors match Palette constants
        Assert.AreEqual(Palette.TempOk, ((SolidColorBrush)rows[0].Accent).Color);
        Assert.AreEqual(Palette.TempWarm, ((SolidColorBrush)rows[1].Accent).Color);
        Assert.AreEqual(Palette.TempHot, ((SolidColorBrush)rows[2].Accent).Color);
        Assert.AreEqual(Palette.AccentDisk, ((SolidColorBrush)rows[3].Accent).Color);
    }

    [TestMethod]
    public void WidgetViewModel_EmptyExtensionSnapshot_ClearsSelectedTabWhenNoTabsRemain()
    {
        var config = new AppConfig
        {
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = false,
            ShowNetwork = false,
        };
        var model = new WidgetViewModel(config);

        var extView = new ExtensionView(
            Array.Empty<WidgetPage>(),
            null,
            new[] { new DetailTab("tab1", "Tab 1", "Sub", "Det", "Hw", Array.Empty<Fact>()) });

        var snapWithExt = new Snapshot
        {
            Ready = true,
            Extensions = new[] { new ExtensionViewData("ext_1", "Ext 1", extView) }
        };

        model.PushHistory(snapWithExt);
        Assert.AreEqual(1, model.Tabs.Count);
        Assert.IsNotNull(model.SelectedTab);
        Assert.AreEqual("ext:ext_1:tab1", model.SelectedTab.Key);

        // Snapshot with empty extensions
        var snapEmpty = new Snapshot
        {
            Ready = true,
            Extensions = Array.Empty<ExtensionViewData>()
        };

        model.PushHistory(snapEmpty);
        Assert.AreEqual(0, model.Tabs.Count);
        Assert.IsNull(model.SelectedTab, "SelectedTab must be cleared when no tabs remain");
    }

    [TestMethod]
    public void WidgetViewModel_RememberedTab_ArrivesLate_SwitchesFromFallback()
    {
        var config = new AppConfig
        {
            ShowCpu = true,
            ShowRam = false,
            ShowDisk = false,
            ShowNetwork = false,
            FullTab = "ext:my_ext:tab1"
        };
        var model = new WidgetViewModel(config);

        // First snapshot: core only (CPU)
        var snapCore = new Snapshot
        {
            Ready = true,
            Cores = new[] { new Core { Usage = 25 } }
        };

        model.PushHistory(snapCore);
        Assert.IsTrue(model.Tabs.Count > 0);
        Assert.IsNotNull(model.SelectedTab);
        Assert.AreEqual("cpu", model.SelectedTab.Key, "CPU should be selected as fallback");

        // Second snapshot: adds extension with tab1
        var extView = new ExtensionView(
            Array.Empty<WidgetPage>(),
            null,
            new[] { new DetailTab("tab1", "Tab 1", "Sub", "Det", "Hw", Array.Empty<Fact>()) });

        var snapWithExt = new Snapshot
        {
            Ready = true,
            Cores = new[] { new Core { Usage = 25 } },
            Extensions = new[] { new ExtensionViewData("my_ext", "My Extension", extView) }
        };

        model.PushHistory(snapWithExt);
        Assert.IsNotNull(model.SelectedTab);
        Assert.AreEqual("ext:my_ext:tab1", model.SelectedTab.Key, "Selection should move to remembered extension tab");
    }

    [TestMethod]
    public void WidgetViewModel_RememberedTab_UserExplicitlySelectedTab_DoesNotSwitchWhenExtensionArrives()
    {
        var config = new AppConfig
        {
            ShowCpu = true,
            ShowRam = false,
            ShowDisk = false,
            ShowNetwork = false,
            FullTab = "ext:my_ext:tab1"
        };
        var model = new WidgetViewModel(config);

        // First snapshot: core only (CPU)
        var snapCore = new Snapshot
        {
            Ready = true,
            Cores = new[] { new Core { Usage = 25 } }
        };

        model.PushHistory(snapCore);
        Assert.AreEqual("cpu", model.SelectedTab?.Key);

        // User explicitly selects CPU
        model.Select("cpu");

        // Second snapshot: adds extension with tab1
        var extView = new ExtensionView(
            Array.Empty<WidgetPage>(),
            null,
            new[] { new DetailTab("tab1", "Tab 1", "Sub", "Det", "Hw", Array.Empty<Fact>()) });

        var snapWithExt = new Snapshot
        {
            Ready = true,
            Cores = new[] { new Core { Usage = 25 } },
            Extensions = new[] { new ExtensionViewData("my_ext", "My Extension", extView) }
        };

        model.PushHistory(snapWithExt);
        Assert.IsNotNull(model.SelectedTab);
        Assert.AreEqual("cpu", model.SelectedTab.Key, "Selection must stay on explicitly chosen CPU tab");
    }

    [TestMethod]
    public void WidgetViewModel_SectionReuse_IdentityMismatch_RebuildsSectionAndResetsNote()
    {
        var config = new AppConfig
        {
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = false,
            ShowNetwork = false,
        };
        var model = new WidgetViewModel(config);
        var lang = new Lang(config.Lang);
        string diskHeading = lang["disk"];

        // Extension overview whose display name equals the localized disk heading
        var extView = new ExtensionView(
            Array.Empty<WidgetPage>(),
            new OverviewSection("Custom Extension Note", new[]
            {
                new OverviewRow("Ext Row", "Detail", "50%", 50, Severity.Good, null)
            }),
            Array.Empty<DetailTab>());

        var snapExtOnly = new Snapshot
        {
            Ready = true,
            Disks = Array.Empty<Disk>(),
            Extensions = new[] { new ExtensionViewData("ext_fake_disk", diskHeading, extView) }
        };

        model.UpdateOverall(snapExtOnly);
        Assert.AreEqual(1, model.Sections.Count);
        Assert.AreEqual(diskHeading, model.Sections[0].Title);
        Assert.AreEqual("Custom Extension Note", model.Sections[0].Note);
        Assert.AreEqual("Ext Row", model.Sections[0].Rows[0].Title);

        // Snapshot 2: Replaced by core disk section
        config.ShowDisk = true;
        var snapCoreDisk = new Snapshot
        {
            Ready = true,
            Disks = new[]
            {
                new Disk { Letter = "C:", Media = "SSD", Bus = "NVMe", TotalGb = 500, UsedGb = 200, Usage = 40 }
            },
            Extensions = Array.Empty<ExtensionViewData>()
        };

        model.UpdateOverall(snapCoreDisk);
        Assert.AreEqual(1, model.Sections.Count);
        Assert.AreEqual(diskHeading, model.Sections[0].Title);
        Assert.AreEqual(string.Empty, model.Sections[0].Note, "Note must be empty after section rebuild");
        Assert.AreNotEqual("Ext Row", model.Sections[0].Rows[0].Title, "Rows must be core rows, not extension rows");
    }
}
