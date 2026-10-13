using System.Runtime.InteropServices;
using System.Text;
using SysMonitor.Model;
using SysMonitor.Native;
using SysMonitor.Sensors;

namespace SysMonitor.Tests;

/// <summary>
/// The SMART fallback for disks the storage stack cannot read. On the
/// development machine the storage query failed with error 1117 on a USB
/// disk and 87 on an external NVMe, and WMI reported 0 C for the NVMe while
/// SMART read 34 C -- which is why zero is rejected here.
/// </summary>
[TestClass]
public class SmartFallbackTests
{
    // ---------------------------------------------------------------- merge
    [TestMethod]
    public void The_storage_reading_wins_and_needs_no_elevation()
    {
        TempResult r = DiskTemperature.Merge(storage: 45, smart: 51, elevated: false, smartAvailable: false);

        Assert.AreEqual(45, r.Celsius);
        Assert.AreEqual(TempSource.Storage, r.Source);
    }

    [TestMethod]
    public void Smart_fills_in_when_the_storage_stack_has_nothing_and_the_app_is_elevated()
    {
        TempResult r = DiskTemperature.Merge(storage: null, smart: 34, elevated: true, smartAvailable: true);

        Assert.AreEqual(34, r.Celsius);
        Assert.AreEqual(TempSource.Smart, r.Source);
    }

    [TestMethod]
    public void Smart_is_ignored_when_not_elevated_or_when_the_library_is_missing()
    {
        Assert.IsNull(DiskTemperature.Merge(null, 34, elevated: false, smartAvailable: true).Celsius);
        Assert.IsNull(DiskTemperature.Merge(null, 34, elevated: true, smartAvailable: false).Celsius);
    }

    [TestMethod]
    public void A_zero_from_a_bridge_is_not_a_temperature()
    {
        TempResult r = DiskTemperature.Merge(storage: null, smart: 0, elevated: true, smartAvailable: true);

        Assert.IsNull(r.Celsius, "WMI said 0 C for a disk SMART read at 34 C");
        Assert.AreEqual(TempSource.None, r.Source);
    }

    [TestMethod]
    public void A_zero_from_the_storage_stack_does_not_shut_smart_out()
    {
        // The storage parser accepts 0, and a bridge that answers 0 there would
        // otherwise show 0 C and stop SMART from ever being asked.
        TempResult r = DiskTemperature.Merge(storage: 0, smart: 34, elevated: true, smartAvailable: true);

        Assert.AreEqual(34, r.Celsius);
        Assert.AreEqual(TempSource.Smart, r.Source);
        Assert.IsNull(DiskTemperature.Merge(storage: 0, smart: null, elevated: false, smartAvailable: true).Celsius);
    }

    [TestMethod]
    public void Only_a_believable_reading_is_plausible()
    {
        Assert.IsFalse(DiskTemperature.Plausible(null));
        Assert.IsFalse(DiskTemperature.Plausible(0));
        Assert.IsFalse(DiskTemperature.Plausible(-5));
        Assert.IsFalse(DiskTemperature.Plausible(150));
        Assert.IsTrue(DiskTemperature.Plausible(1));
        Assert.IsTrue(DiskTemperature.Plausible(34));
        Assert.IsTrue(DiskTemperature.Plausible(149));
    }

    [TestMethod]
    public void No_reading_anywhere_stays_null_rather_than_being_estimated()
    {
        TempResult r = DiskTemperature.Merge(null, null, elevated: true, smartAvailable: true);

        Assert.IsNull(r.Celsius);
        Assert.AreEqual(TempSource.None, r.Source);
    }

    // ------------------------------------------------------- library layout
    [TestMethod]
    public void The_native_disk_struct_is_the_160_bytes_the_library_hands_back()
    {
        // docs/CORE_ABI.md: struct_size is the first field and the v1 layout is
        // 160 bytes (power_on_hours at 144). If this moves, di_disk_info answers
        // DI_E_STRUCT_SIZE and every scan comes back empty.
        Assert.AreEqual(160, Marshal.SizeOf<SmartCore.NativeDisk>());
        Assert.AreEqual(144, (int)Marshal.OffsetOf<SmartCore.NativeDisk>(nameof(SmartCore.NativeDisk.PowerOnHours)));
    }

    private static SmartCore.NativeDisk Native(int temp = 34, int life = 96, int health = 1,
                                               uint access = 0, string model = "Micron_3400")
    {
        var model64 = new byte[64];
        Encoding.UTF8.GetBytes(model).CopyTo(model64, 0);
        return new SmartCore.NativeDisk
        {
            StructSize = 160,
            PhysicalDriveId = 2,
            Model = model64,
            Serial = new byte[32],
            Firmware = new byte[16],
            TemperatureC = temp,
            LifePercent = life,
            Health = health,
            Access = access,
        };
    }

    [TestMethod]
    public void A_reading_carries_what_the_library_reported()
    {
        SmartReading r = SmartCore.ToReading(Native());

        Assert.AreEqual(2, r.Drive);
        Assert.AreEqual("Micron_3400", r.Model);
        Assert.AreEqual(34, r.TempC);
        Assert.AreEqual(96, r.LifePercent);
        Assert.AreEqual(SmartHealth.Good, r.Health);
        Assert.IsTrue(r.Full);
    }

    [TestMethod]
    public void The_no_value_marker_becomes_null_but_zero_stays_a_value()
    {
        SmartReading none = SmartCore.ToReading(Native(temp: int.MinValue, life: int.MinValue));
        Assert.IsNull(none.TempC);
        Assert.IsNull(none.LifePercent);

        // A worn-out NVMe really does report 0% life.
        Assert.AreEqual(0, SmartCore.ToReading(Native(life: 0)).LifePercent);
    }

    [TestMethod]
    public void A_health_code_outside_the_range_reads_as_unknown_and_partial_access_is_not_full()
    {
        Assert.AreEqual(SmartHealth.Unknown, SmartCore.ToReading(Native(health: 9)).Health);
        Assert.AreEqual(SmartHealth.Caution, SmartCore.ToReading(Native(health: 2)).Health);
        Assert.IsFalse(SmartCore.ToReading(Native(access: 1)).Full);
    }

    [TestMethod]
    public void Text_stops_at_the_terminator_and_survives_a_full_buffer_and_a_missing_one()
    {
        Assert.AreEqual("abc", SmartCore.Utf8(new byte[] { (byte)'a', (byte)'b', (byte)'c', 0, (byte)'z' }));
        Assert.AreEqual("abc", SmartCore.Utf8(new byte[] { (byte)'a', (byte)'b', (byte)'c' }));
        Assert.AreEqual(string.Empty, SmartCore.Utf8(null));
        Assert.AreEqual("ไทย", SmartCore.Utf8(Encoding.UTF8.GetBytes("ไทย\0")));
    }

    [TestMethod]
    public void A_missing_library_is_reported_once_as_unavailable_and_never_throws()
    {
        // The test host has no DiskInfoCore.dll beside it unless a build placed one there.
        (SmartScanStatus status, IReadOnlyList<SmartReading> disks) = SmartCore.Scan();

        if (!SmartCore.IsAvailable)
        {
            Assert.AreEqual(SmartScanStatus.Unavailable, status);
            Assert.AreEqual(0, disks.Count);
        }
        else
        {
            // With the library present a non-elevated test run must be told to elevate.
            Assert.IsTrue(status is SmartScanStatus.Ok or SmartScanStatus.NeedAdmin);
        }
    }

    // ------------------------------------------------------------ elevation
    [TestMethod]
    public void Restarting_as_administrator_asks_windows_for_the_prompt_and_runs_from_the_same_folder()
    {
        var start = AdminRestart.Start(@"C:\Apps\SystemMonitor\SystemMonitor.exe");

        Assert.AreEqual("runas", start.Verb);
        Assert.IsTrue(start.UseShellExecute, "runas only works through the shell");
        Assert.AreEqual(@"C:\Apps\SystemMonitor", start.WorkingDirectory);
    }

    [TestMethod]
    public void Declining_the_prompt_is_told_apart_from_a_real_failure()
    {
        // ERROR_CANCELLED is what Windows returns when the user presses No on UAC.
        Assert.IsTrue(AdminRestart.IsDeclined(new System.ComponentModel.Win32Exception(1223)));
        Assert.IsFalse(AdminRestart.IsDeclined(new System.ComponentModel.Win32Exception(5)),
            "access denied is a failure to report, not a cancelled prompt");
        Assert.IsFalse(AdminRestart.IsDeclined(new System.ComponentModel.Win32Exception(2)));
    }

    [TestMethod]
    public void An_elevated_token_implies_an_administrator_and_the_sampler_reports_the_same_answer()
    {
        bool elevated = Win32.IsElevated();
        if (elevated)
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            Assert.IsTrue(new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));
        }

        using var sampler = new Sampler(new AppConfig());
        Assert.AreEqual(elevated, sampler.Elevated);
    }

    // ------------------------------------------------------------ installer
    [TestMethod]
    public void The_installer_owns_the_library_so_uninstall_removes_it_and_nothing_else_is_touched()
    {
        string[] owned = SysMonitor.Setup.Installer.OwnedPaths(includeSettings: false).ToArray();

        Assert.IsTrue(owned.Any(p => System.IO.Path.GetFileName(p) == SysMonitor.Setup.Installer.CoreName),
            "an unlisted DiskInfoCore.dll would be left behind and keep the folder from being removed");
        Assert.IsTrue(owned.All(p => System.IO.Path.GetFileName(p) is not (null or "")));
    }

    // ------------------------------------------------------------- language
    [TestMethod]
    [DataRow("disk_smart")]
    [DataRow("disk_smart_hint")]
    [DataRow("restart_admin")]
    [DataRow("admin_active")]
    [DataRow("admin_missing_core")]
    public void The_administrator_option_is_worded_in_both_languages(string key)
    {
        string th = new Lang("th")[key];
        string en = new Lang("en")[key];

        Assert.AreNotEqual(string.Empty, th);
        Assert.AreNotEqual(key, th, "a missing key reads as its own name");
        Assert.AreNotEqual(th, en, "a missing Thai string falls back to the English one");
    }

    [TestMethod]
    public void The_hint_promises_the_app_never_elevates_on_its_own()
    {
        StringAssert.Contains(new Lang("en")["disk_smart_hint"], "never asks");
    }
}
