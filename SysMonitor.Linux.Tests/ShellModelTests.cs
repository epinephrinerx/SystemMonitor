using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class ShellModelTests
{
    private static Snapshot CreateSnapshotWithBattery()
    {
        return new Snapshot
        {
            Ready = true,
            CpuTotal = 20,
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
            Batteries = new[]
            {
                new Battery { Id = "BAT0", Model = "Internal Battery", Percent = 80 },
            },
            Processes = new[]
            {
                new ProcessEntry { Pid = 100, Name = "bash", ParentPid = 1, Uid = 1000, StartTime = 1000, WorkingSet = 100L * 1024 * 1024 },
            },
        };
    }

    [TestMethod]
    public void Sidebar_ContainsModulesHeadingWithSixShowChecks_AndNoDeletedRows()
    {
        var model = new ShellModel();
        Snapshot snapshot = CreateSnapshotWithBattery();
        model.SetSnapshot(snapshot);

        IReadOnlyList<UiRow> sidebar = model.Sidebar;

        // Contains a "Modules" heading
        HeadingRow? modulesHeading = sidebar.OfType<HeadingRow>()
            .FirstOrDefault(h => string.Equals(h.Text, model.Language["modules"], StringComparison.Ordinal));
        Assert.IsNotNull(modulesHeading, "Sidebar must contain a 'Modules' heading");

        // Must contain the six show_* checks including show_battery
        var checkRows = sidebar.OfType<CheckRow>().ToList();
        var checkKeys = checkRows.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(checkKeys.Contains(SettingKeys.ShowCpu), "Must contain show_cpu");
        Assert.IsTrue(checkKeys.Contains(SettingKeys.ShowRam), "Must contain show_ram");
        Assert.IsTrue(checkKeys.Contains(SettingKeys.ShowDisk), "Must contain show_disk");
        Assert.IsTrue(checkKeys.Contains(SettingKeys.ShowGpu), "Must contain show_gpu");
        Assert.IsTrue(checkKeys.Contains(SettingKeys.ShowNetwork), "Must contain show_network");
        Assert.IsTrue(checkKeys.Contains(SettingKeys.ShowBattery), "Must contain show_battery");

        // Must NOT contain per-module options that moved to module pages:
        // cpu_mode, cpu_temperature, disk_mode, include_network, network_mode
        Assert.IsFalse(checkKeys.Contains(SettingKeys.PerCore), "Must not contain cpu_mode / per_core");
        Assert.IsFalse(checkKeys.Contains("cpu_mode"), "Must not contain cpu_mode");
        Assert.IsFalse(checkKeys.Contains(SettingKeys.CpuTemp), "Must not contain cpu_temperature / cpu_temp");
        Assert.IsFalse(checkKeys.Contains("cpu_temperature"), "Must not contain cpu_temperature");
        Assert.IsFalse(checkKeys.Contains(SettingKeys.PerDrive), "Must not contain disk_mode / per_drive");
        Assert.IsFalse(checkKeys.Contains("disk_mode"), "Must not contain disk_mode");
        Assert.IsFalse(checkKeys.Contains(SettingKeys.ShowNetworkDrives), "Must not contain include_network / show_network_drives");
        Assert.IsFalse(checkKeys.Contains("include_network"), "Must not contain include_network");
        Assert.IsFalse(checkKeys.Contains(SettingKeys.PerAdapter), "Must not contain network_mode / per_adapter");
        Assert.IsFalse(checkKeys.Contains("network_mode"), "Must not contain network_mode");

        // Must NOT contain "Full Data" or "Open widget" rows
        foreach (UiRow row in sidebar)
        {
            if (row is ActionRow action)
            {
                Assert.AreNotEqual(ActionKeys.OpenFull, action.Action, "Must not contain OpenFull action");
                Assert.AreNotEqual(ActionKeys.OpenWidget, action.Action, "Must not contain OpenWidget action");
                Assert.IsFalse(action.Label.Contains("Full Data", StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(action.Label.Contains("Open widget", StringComparison.OrdinalIgnoreCase));
            }
            else if (row is NavRow nav)
            {
                Assert.AreNotEqual(ActionKeys.OpenFull, nav.Action, "Must not contain OpenFull nav");
                Assert.AreNotEqual(ActionKeys.OpenWidget, nav.Action, "Must not contain OpenWidget nav");
                Assert.IsFalse(nav.Label.Contains("Full Data", StringComparison.OrdinalIgnoreCase));
                Assert.IsFalse(nav.Label.Contains("Open widget", StringComparison.OrdinalIgnoreCase));
            }
        }

        // Initially with show_battery = true, battery page and sidebar item are present
        Assert.IsTrue(model.Full.Tabs.Any(t => t.Key.StartsWith("battery:", StringComparison.Ordinal)));
        Assert.IsTrue(model.MainSidebar.Any(i => i.Key.StartsWith("battery:", StringComparison.Ordinal)));

        // SetBool(show_battery, false) hides the battery page and its sidebar item
        bool set = model.SetBool(SettingKeys.ShowBattery, false);
        Assert.IsTrue(set);
        Assert.IsFalse(model.Config.ShowBattery);
        Assert.IsFalse(model.Full.Tabs.Any(t => t.Key.StartsWith("battery:", StringComparison.Ordinal)),
            "Battery page must be hidden when show_battery is false");
        Assert.IsFalse(model.MainSidebar.Any(i => i.Key.StartsWith("battery:", StringComparison.Ordinal)),
            "Battery sidebar item must be hidden when show_battery is false");
    }

    [TestMethod]
    public void MainProcessesVisible_IsTrueOnlyWhenMainShownAndPageIsProcesses()
    {
        var model = new ShellModel();
        Snapshot snapshot = CreateSnapshotWithBattery();
        model.SetSnapshot(snapshot);

        // Initially in Widget mode -> false
        Assert.AreEqual(ViewMode.Widget, model.View.Mode);
        Assert.IsFalse(model.MainProcessesVisible);

        // Switch to Main, default page is Overall -> false
        model.SwitchTo(ViewMode.Main);
        Assert.AreEqual(ViewMode.Main, model.View.Mode);
        Assert.AreEqual(PageKeys.Overall, model.Main.Page);
        Assert.IsFalse(model.MainProcessesVisible);

        // Select CPU page in Main -> false
        model.SelectMainPage(PageKeys.Cpu);
        Assert.AreEqual(PageKeys.Cpu, model.Main.Page);
        Assert.IsFalse(model.MainProcessesVisible);

        // Select Processes page in Main -> true!
        model.SelectMainPage(PageKeys.Processes);
        Assert.AreEqual(PageKeys.Processes, model.Main.Page);
        Assert.IsTrue(model.MainProcessesVisible);

        // Select Apps page in Main -> false
        model.SelectMainPage(PageKeys.Apps);
        Assert.AreEqual(PageKeys.Apps, model.Main.Page);
        Assert.IsFalse(model.MainProcessesVisible);

        // Select Processes page again -> true
        model.SelectMainPage(PageKeys.Processes);
        Assert.IsTrue(model.MainProcessesVisible);

        // Switch to Widget mode while Processes was selected in Main -> false
        model.SwitchTo(ViewMode.Widget);
        Assert.AreEqual(ViewMode.Widget, model.View.Mode);
        Assert.IsFalse(model.MainProcessesVisible);

        // Switch back to Main -> true again
        model.SwitchTo(ViewMode.Main);
        Assert.AreEqual(ViewMode.Main, model.View.Mode);
        Assert.AreEqual(PageKeys.Processes, model.Main.Page);
        Assert.IsTrue(model.MainProcessesVisible);
    }
}
