using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class AppConfigTests
{
    [TestMethod]
    public void Defaults_MatchWindowsSpec()
    {
        var config = new AppConfig();

        // General
        Assert.AreEqual("en", config.Lang);
        Assert.AreEqual("light", config.Theme);
        Assert.IsFalse(config.AlwaysOnTop);
        Assert.IsTrue(config.Snap);
        Assert.IsTrue(config.AutoCheckUpdates);
        Assert.AreEqual(1.0, config.Opacity);
        Assert.AreEqual(1.0, config.FontScale);

        // Display
        Assert.IsTrue(config.ShowCpu);
        Assert.IsTrue(config.ShowRam);
        Assert.IsTrue(config.ShowDisk);
        Assert.IsTrue(config.ShowNetwork);
        Assert.IsTrue(config.ShowGpu);
        Assert.IsTrue(config.IncludeWireless);
        Assert.AreEqual("separated", config.NetworkMode);
        Assert.AreEqual("separated", config.CpuMode);
        Assert.AreEqual("separated", config.DiskMode);
        Assert.AreEqual("balanced", config.Speed);
        Assert.AreEqual(2500, config.MetricsIntervalMs);
        Assert.IsTrue(config.TempEstimate);
        Assert.IsTrue(config.CpuTemperature);
        Assert.IsTrue(config.IncludeRemovable);
        Assert.IsTrue(config.IncludeNetwork);
        Assert.IsTrue(config.Diagnostics);
        Assert.AreEqual("tray", config.CloseAction);
        Assert.IsNull(config.ShowSmart);

        // Placement
        Assert.IsNull(config.PosX);
        Assert.IsNull(config.PosY);
        Assert.AreEqual(270.0, config.WidgetW);
        Assert.AreEqual(104.0, config.WidgetH);
        Assert.AreEqual(630.0, config.OverallW);
        Assert.AreEqual(480.0, config.OverallH);
        Assert.IsFalse(config.OverallSized);
        Assert.AreEqual(0.0, config.FullW);
        Assert.AreEqual(0.0, config.FullH);
        Assert.AreEqual("widget", config.LastView);
        Assert.AreEqual(string.Empty, config.FullTab);
        Assert.IsNotNull(config.Extensions);
        Assert.AreEqual(0, config.Extensions.Count);
    }

    [TestMethod]
    public void Speed_MetricsIntervalMs_MapsPerSpec()
    {
        var config = new AppConfig();

        config.Speed = "eco";
        Assert.AreEqual(5000, config.MetricsIntervalMs);

        config.Speed = "fast";
        Assert.AreEqual(1000, config.MetricsIntervalMs);

        config.Speed = "balanced";
        Assert.AreEqual(2500, config.MetricsIntervalMs);

        config.Speed = "unknown";
        Assert.AreEqual(2500, config.MetricsIntervalMs);
    }

    [TestMethod]
    public void SaveAndLoad_RoundTrip_InCustomPath()
    {
        string dir = Path.Combine(Path.GetTempPath(), "appconfig-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "config.json");

        try
        {
            var original = new AppConfig
            {
                Lang = "th",
                Theme = "dark",
                AlwaysOnTop = true,
                Snap = false,
                AutoCheckUpdates = false,
                Opacity = 0.85,
                FontScale = 1.2,
                ShowCpu = true,
                ShowRam = false,
                ShowDisk = true,
                ShowNetwork = false,
                ShowGpu = true,
                IncludeWireless = false,
                NetworkMode = "total",
                CpuMode = "total",
                DiskMode = "total",
                Speed = "fast",
                TempEstimate = false,
                CpuTemperature = false,
                IncludeRemovable = false,
                IncludeNetwork = false,
                Diagnostics = false,
                CloseAction = "exit",
                PosX = 120.5,
                PosY = 240.5,
                WidgetW = 320,
                WidgetH = 150,
                OverallW = 800,
                OverallH = 600,
                OverallSized = true,
                FullW = 1024,
                FullH = 768,
                MainW = 1024,
                MainH = 768,
                MainPage = "cpu",
                SettingsOpen = true,
                SidebarWidth = 260,
                LastView = "main",
            };
            original.Extensions["smart"] = new ExtensionOption { Enabled = true };

            bool saved = original.Save(path);
            Assert.IsTrue(saved, "Save returned false");
            Assert.IsTrue(File.Exists(path), "File does not exist");

            AppConfig loaded = AppConfig.Load(path);

            Assert.AreEqual("th", loaded.Lang);
            Assert.AreEqual("dark", loaded.Theme);
            Assert.IsTrue(loaded.AlwaysOnTop);
            Assert.IsFalse(loaded.Snap);
            Assert.IsFalse(loaded.AutoCheckUpdates);
            Assert.AreEqual(0.85, loaded.Opacity, 0.001);
            Assert.AreEqual(1.2, loaded.FontScale, 0.01);
            Assert.IsTrue(loaded.ShowCpu);
            Assert.IsFalse(loaded.ShowRam);
            Assert.IsTrue(loaded.ShowDisk);
            Assert.IsFalse(loaded.ShowNetwork);
            Assert.IsTrue(loaded.ShowGpu);
            Assert.IsFalse(loaded.IncludeWireless);
            Assert.AreEqual("total", loaded.NetworkMode);
            Assert.AreEqual("total", loaded.CpuMode);
            Assert.AreEqual("total", loaded.DiskMode);
            Assert.AreEqual("fast", loaded.Speed);
            Assert.AreEqual(1000, loaded.MetricsIntervalMs);
            Assert.IsFalse(loaded.TempEstimate);
            Assert.IsFalse(loaded.CpuTemperature);
            Assert.IsFalse(loaded.IncludeRemovable);
            Assert.IsFalse(loaded.IncludeNetwork);
            Assert.IsFalse(loaded.Diagnostics);
            Assert.AreEqual("exit", loaded.CloseAction);
            Assert.AreEqual(120.5, loaded.PosX);
            Assert.AreEqual(240.5, loaded.PosY);
            Assert.AreEqual(320.0, loaded.WidgetW);
            Assert.AreEqual(150.0, loaded.WidgetH);
            Assert.AreEqual(800.0, loaded.OverallW);
            Assert.AreEqual(600.0, loaded.OverallH);
            Assert.IsTrue(loaded.OverallSized);
            Assert.AreEqual(1024.0, loaded.FullW);
            Assert.AreEqual(768.0, loaded.FullH);
            Assert.AreEqual(1024.0, loaded.MainW);
            Assert.AreEqual(768.0, loaded.MainH);
            Assert.AreEqual("cpu", loaded.MainPage);
            Assert.IsTrue(loaded.SettingsOpen);
            Assert.AreEqual(260, loaded.SidebarWidth);
            Assert.AreEqual("main", loaded.LastView);
            Assert.AreEqual(string.Empty, loaded.FullTab);
            Assert.IsTrue(loaded.Extensions.ContainsKey("smart"));
            Assert.IsTrue(loaded.Extensions["smart"].Enabled);
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
    public void SaveAndLoad_RoundTrip_WithXdgConfigHome()
    {
        string dir = Path.Combine(Path.GetTempPath(), "xdg-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string? oldXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", dir);

            Assert.AreEqual(Path.Combine(dir, "SystemMonitor"), AppConfig.ConfigDirectory);
            Assert.AreEqual(Path.Combine(dir, "SystemMonitor", "config.json"), AppConfig.DefaultPath);

            var config = new AppConfig
            {
                Lang = "th",
                Theme = "dark",
                Speed = "eco",
            };

            bool saved = config.Save();
            Assert.IsTrue(saved);
            Assert.IsTrue(File.Exists(AppConfig.DefaultPath));

            AppConfig loaded = AppConfig.Load();
            Assert.AreEqual("th", loaded.Lang);
            Assert.AreEqual("dark", loaded.Theme);
            Assert.AreEqual("eco", loaded.Speed);
            Assert.AreEqual(5000, loaded.MetricsIntervalMs);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oldXdg);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Load_CorruptFile_FallsBackToDefaults_AndRenamesToBad()
    {
        string dir = Path.Combine(Path.GetTempPath(), "corrupt-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "config.json");
        string badPath = path + ".bad";

        try
        {
            File.WriteAllText(path, "{ this is not valid json! ");

            AppConfig loaded = AppConfig.Load(path);

            // Defaults returned
            Assert.AreEqual("en", loaded.Lang);
            Assert.AreEqual("light", loaded.Theme);
            Assert.AreEqual(270.0, loaded.WidgetW);

            // Corrupt file preserved as .bad
            Assert.IsTrue(File.Exists(badPath), "config.json.bad should exist");
            Assert.AreEqual("{ this is not valid json! ", File.ReadAllText(badPath));
            Assert.IsFalse(File.Exists(path), "original config.json should have been renamed");
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
    public void Load_NonObjectJson_FallsBackToDefaults_AndRenamesToBad()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nonobj-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "config.json");
        string badPath = path + ".bad";

        try
        {
            File.WriteAllText(path, "[\"item1\", \"item2\"]");

            AppConfig loaded = AppConfig.Load(path);

            Assert.AreEqual("en", loaded.Lang);
            Assert.IsTrue(File.Exists(badPath));
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
    public void Load_NonExistentFile_ReturnsDefaultsWithoutError()
    {
        string path = Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid().ToString("N"), "config.json");

        AppConfig loaded = AppConfig.Load(path);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("en", loaded.Lang);
        Assert.AreEqual("light", loaded.Theme);
    }

    [TestMethod]
    public void Normalise_ClampsNumericRanges()
    {
        var config = new AppConfig
        {
            Opacity = 0.1, // Below 0.35
            FontScale = 0.5, // Below 0.8
            WidgetW = 100, // Below 200
            WidgetH = 30, // Below 64
            OverallW = 200, // Below 470
            OverallH = 100, // Below 300
            FullW = 300, // Below 640
            FullH = 200, // Below 480
        };

        config.Normalise();

        Assert.AreEqual(0.35, config.Opacity);
        Assert.AreEqual(0.8, config.FontScale);
        Assert.AreEqual(200.0, config.WidgetW);
        Assert.AreEqual(64.0, config.WidgetH);
        Assert.AreEqual(470.0, config.OverallW);
        Assert.AreEqual(300.0, config.OverallH);
        Assert.AreEqual(640.0, config.FullW);
        Assert.AreEqual(480.0, config.FullH);

        // Test upper bounds
        config.Opacity = 2.0;
        config.FontScale = 2.5;
        config.WidgetW = 2000;
        config.WidgetH = 1000;
        config.OverallW = 5000;
        config.OverallH = 4000;
        config.FullW = 5000;
        config.FullH = 4000;

        config.Normalise();

        Assert.AreEqual(1.0, config.Opacity);
        Assert.AreEqual(1.6, config.FontScale);
        Assert.AreEqual(900.0, config.WidgetW);
        Assert.AreEqual(400.0, config.WidgetH);
        Assert.AreEqual(4000.0, config.OverallW);
        Assert.AreEqual(3000.0, config.OverallH);
        Assert.AreEqual(4000.0, config.FullW);
        Assert.AreEqual(3000.0, config.FullH);
    }

    [TestMethod]
    public void Normalise_InvalidValues_FallBackToDefaults()
    {
        var config = new AppConfig
        {
            Opacity = double.NaN,
            FontScale = double.NaN,
            WidgetW = -10,
            WidgetH = 0,
            OverallW = -50,
            OverallH = 0,
            FullW = -100,
            FullH = -50,
            Lang = "french",
            Theme = "neon",
            CloseAction = "minimize",
            Speed = "ultra",
            NetworkMode = "grouped",
            CpuMode = "grouped",
            DiskMode = "grouped",
            LastView = "dashboard",
        };

        config.Normalise();

        Assert.AreEqual(1.0, config.Opacity);
        Assert.AreEqual(1.0, config.FontScale);
        Assert.AreEqual(270.0, config.WidgetW);
        Assert.AreEqual(104.0, config.WidgetH);
        Assert.AreEqual(630.0, config.OverallW);
        Assert.AreEqual(480.0, config.OverallH);
        Assert.AreEqual(0.0, config.FullW);
        Assert.AreEqual(0.0, config.FullH);
        Assert.AreEqual("en", config.Lang);
        Assert.AreEqual("light", config.Theme);
        Assert.AreEqual("tray", config.CloseAction);
        Assert.AreEqual("balanced", config.Speed);
        Assert.AreEqual("separated", config.NetworkMode);
        Assert.AreEqual("separated", config.CpuMode);
        Assert.AreEqual("separated", config.DiskMode);
        Assert.AreEqual("widget", config.LastView);
    }

    [TestMethod]
    public void Normalise_MigratesLegacyShowSmart()
    {
        var config = new AppConfig
        {
            ShowSmart = true,
        };

        config.Normalise();

        Assert.IsNull(config.ShowSmart);
        Assert.IsTrue(config.Extensions.ContainsKey("smart"));
        Assert.IsTrue(config.Extensions["smart"].Enabled);
    }

    [TestMethod]
    public void Normalise_MigratesLegacyFullTab()
    {
        var config1 = new AppConfig { FullTab = "smart" };
        config1.Normalise();
        Assert.AreEqual("ext:smart:status", config1.FullTab);

        var config2 = new AppConfig { FullTab = "smart2" };
        config2.Normalise();
        Assert.AreEqual("ext:smart:disk2", config2.FullTab);
    }

    [TestMethod]
    public void Load_MigratesLegacyDimensionNames()
    {
        string dir = Path.Combine(Path.GetTempPath(), "legacy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "config.json");

        try
        {
            string json = """
            {
              "mini_w": 280,
              "mini_h": 110,
              "exp_w": 650,
              "exp_h": 500,
              "exp_sized": true,
              "show_smart": true
            }
            """;
            File.WriteAllText(path, json);

            AppConfig loaded = AppConfig.Load(path);

            Assert.AreEqual(280.0, loaded.WidgetW);
            Assert.AreEqual(110.0, loaded.WidgetH);
            Assert.AreEqual(650.0, loaded.OverallW);
            Assert.AreEqual(500.0, loaded.OverallH);
            Assert.IsTrue(loaded.OverallSized);
            Assert.IsTrue(loaded.Extensions.ContainsKey("smart"));
            Assert.IsTrue(loaded.Extensions["smart"].Enabled);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
