using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Linux.Sampling;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class BatterySnapshotAndConfigTests
{
    private const string MouseDumpFixture = """
    Device: /org/freedesktop/UPower/devices/mouse_dev_AA_BB_CC_DD_EE_11
      native-path: hid-AA:BB:CC:DD:EE:11-battery
      model: Test Mouse
      mouse
        present: yes
        state: discharging
        percentage: 75%
    """;

    [TestMethod]
    public void LinuxSampler_Sample_PopulatesBatteriesPowerSourcesAndPeripherals()
    {
        using var fake = new FakeRoot();

        // 1. Sysfs Battery BAT0
        fake.WriteFile("sys/class/power_supply/BAT0/type", "Battery\n");
        fake.WriteFile("sys/class/power_supply/BAT0/present", "1\n");
        fake.WriteFile("sys/class/power_supply/BAT0/model_name", "AP18C4K\n");
        fake.WriteFile("sys/class/power_supply/BAT0/capacity", "85\n");
        fake.WriteFile("sys/class/power_supply/BAT0/status", "Discharging\n");

        // 2. Sysfs PowerSource AC
        fake.WriteFile("sys/class/power_supply/AC/type", "Mains\n");
        fake.WriteFile("sys/class/power_supply/AC/online", "1\n");

        // 3. Proc files for sampler
        fake.WriteFile("proc/stat", "cpu  100 0 100 800\ncpu0 50 0 50 400\ncpu1 50 0 50 400\n");
        fake.WriteFile("proc/meminfo", "MemTotal: 16000000 kB\nMemAvailable: 8000000 kB\n");

        // 4. Runner for UPower mouse
        var runner = new FakeCommandRunner();
        runner.CommandOutputs["upower --dump"] = MouseDumpFixture;

        var clock = new FakeClock();
        var sampler = new LinuxSampler(
            root: fake.Root,
            runner: runner,
            clock: clock.Now);

        Snapshot snapshot = sampler.Sample(includeProcesses: false);

        // Batteries
        Assert.AreEqual(1, snapshot.Batteries.Count);
        Assert.AreEqual("BAT0", snapshot.Batteries[0].Id);
        Assert.AreEqual("AP18C4K", snapshot.Batteries[0].Model);
        Assert.AreEqual(85, snapshot.Batteries[0].Percent);

        // PowerSources
        Assert.AreEqual(1, snapshot.PowerSources.Count);
        Assert.AreEqual("AC", snapshot.PowerSources[0].Id);
        Assert.AreEqual(PowerSourceKind.Mains, snapshot.PowerSources[0].Kind);
        Assert.IsTrue(snapshot.PowerSources[0].Online);

        // Peripherals
        Assert.AreEqual(1, snapshot.Peripherals.Count);
        Assert.AreEqual("Test Mouse", snapshot.Peripherals[0].Model);
        Assert.AreEqual(75, snapshot.Peripherals[0].Percent);
        Assert.AreEqual(PeripheralKind.Mouse, snapshot.Peripherals[0].Kind);

        // Derived OnAcPower
        Assert.IsTrue(snapshot.OnAcPower);
    }

    [TestMethod]
    public void Snapshot_OnAcPower_DerivedFromPowerSources()
    {
        // 1. One online power source -> OnAcPower is true
        var snapshotWithOnline = new Snapshot
        {
            PowerSources = new[]
            {
                new PowerSource("AC", PowerSourceKind.Mains, Online: true),
                new PowerSource("USB_C", PowerSourceKind.Usb, Online: false),
            }
        };
        Assert.IsTrue(snapshotWithOnline.OnAcPower);

        // 2. All offline power sources -> OnAcPower is false
        var snapshotWithOffline = new Snapshot
        {
            PowerSources = new[]
            {
                new PowerSource("AC", PowerSourceKind.Mains, Online: false),
                new PowerSource("USB_C", PowerSourceKind.Usb, Online: false),
            }
        };
        Assert.IsFalse(snapshotWithOffline.OnAcPower);

        // 3. No power sources -> OnAcPower is false
        var snapshotEmpty = new Snapshot();
        Assert.IsFalse(snapshotEmpty.OnAcPower);
    }

    [TestMethod]
    public void AppConfig_ShowBattery_DefaultsToTrue()
    {
        var config = new AppConfig();
        Assert.IsTrue(config.ShowBattery, "ShowBattery should default to true.");
    }

    [TestMethod]
    public void AppConfig_ShowBattery_RoundTripsThroughSaveAndLoad()
    {
        string dir = Path.Combine(Path.GetTempPath(), "appconfig-battery-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "config.json");

        try
        {
            // Test 1: Save with ShowBattery = false
            var config1 = new AppConfig { ShowBattery = false };
            bool saved1 = config1.Save(path);
            Assert.IsTrue(saved1);

            AppConfig loaded1 = AppConfig.Load(path);
            Assert.IsFalse(loaded1.ShowBattery, "ShowBattery should load as false.");

            // Test 2: Save with ShowBattery = true
            var config2 = new AppConfig { ShowBattery = true };
            bool saved2 = config2.Save(path);
            Assert.IsTrue(saved2);

            AppConfig loaded2 = AppConfig.Load(path);
            Assert.IsTrue(loaded2.ShowBattery, "ShowBattery should load as true.");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Lang_BatteryKeys_AllExistInThaiAndEnglish()
    {
        var langTh = new Lang("th");
        var langEn = new Lang("en");

        Assert.AreEqual(15, StringData.BatteryKeys.Length, "Expected 15 battery string keys.");

        foreach ((string key, string expectedThai, string expectedEnglish) in StringData.BatteryKeys)
        {
            // Verify Thai
            Assert.IsTrue(langTh.Has(key), $"Lang('th') missing key: {key}");
            string thaiValue = langTh[key];
            Assert.IsFalse(string.IsNullOrEmpty(thaiValue), $"Thai translation for '{key}' is empty.");
            Assert.AreNotEqual(key, thaiValue, $"Thai translation for '{key}' fell back to key name.");
            Assert.AreEqual(expectedThai, thaiValue, $"Thai translation for '{key}' mismatch.");

            // Verify English
            Assert.IsTrue(langEn.Has(key), $"Lang('en') missing key: {key}");
            string englishValue = langEn[key];
            Assert.IsFalse(string.IsNullOrEmpty(englishValue), $"English translation for '{key}' is empty.");
            Assert.AreNotEqual(key, englishValue, $"English translation for '{key}' fell back to key name.");
            Assert.AreEqual(expectedEnglish, englishValue, $"English translation for '{key}' mismatch.");
        }
    }
}
