using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class ConfigMigrationTests
{
    [TestMethod]
    public void Migration_LastView_OverallOrFull_MigratesToMain()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cfg-mig-lastview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string pathOverall = Path.Combine(dir, "overall.json");
        string pathFull = Path.Combine(dir, "full.json");

        try
        {
            // Old three-view build without main_page: last_view = overall
            File.WriteAllText(pathOverall, @"{
                ""last_view"": ""overall"",
                ""full_w"": 800,
                ""full_h"": 600
            }");

            AppConfig loadedOverall = AppConfig.Load(pathOverall);
            Assert.AreEqual("main", loadedOverall.LastView);

            // Old three-view build without main_page: last_view = full
            File.WriteAllText(pathFull, @"{
                ""last_view"": ""full"",
                ""full_w"": 800,
                ""full_h"": 600
            }");

            AppConfig loadedFull = AppConfig.Load(pathFull);
            Assert.AreEqual("main", loadedFull.LastView);
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
    public void Migration_FullTab_MigratesToMainPage_WhenMainPageAbsent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cfg-mig-fulltab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "config.json");

        try
        {
            File.WriteAllText(path, @"{
                ""full_tab"": ""disk:nvme0n1""
            }");

            AppConfig loaded = AppConfig.Load(path);
            Assert.AreEqual("disk:nvme0n1", loaded.MainPage);
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
    public void Migration_ExistingMainPage_WinsOverFullTab()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cfg-mig-existing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "config.json");

        try
        {
            File.WriteAllText(path, @"{
                ""main_page"": ""ram"",
                ""full_tab"": ""disk:nvme0n1""
            }");

            AppConfig loaded = AppConfig.Load(path);
            Assert.AreEqual("ram", loaded.MainPage);
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
    public void Migration_FullGeometry_SeedsMainGeometry_Once()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cfg-mig-geom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string pathWithoutMain = Path.Combine(dir, "without_main.json");
        string pathWithMain = Path.Combine(dir, "with_main.json");

        try
        {
            File.WriteAllText(pathWithoutMain, @"{
                ""full_w"": 960,
                ""full_h"": 640
            }");

            AppConfig loadedSeed = AppConfig.Load(pathWithoutMain);
            Assert.AreEqual(960.0, loadedSeed.MainW);
            Assert.AreEqual(640.0, loadedSeed.MainH);

            // Existing main_* keys win over full_* keys
            File.WriteAllText(pathWithMain, @"{
                ""main_page"": ""cpu"",
                ""main_w"": 1100,
                ""main_h"": 750,
                ""full_w"": 960,
                ""full_h"": 640
            }");

            AppConfig loadedMain = AppConfig.Load(pathWithMain);
            Assert.AreEqual(1100.0, loadedMain.MainW);
            Assert.AreEqual(750.0, loadedMain.MainH);
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
    public void Save_DoesNotWrite_FullTab()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cfg-mig-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "config.json");

        try
        {
            var config = new AppConfig
            {
                MainPage = "cpu",
                FullTab = "disk:sda",
                LastView = "main",
            };

            bool saved = config.Save(path);
            Assert.IsTrue(saved);

            string json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            Assert.IsFalse(root.TryGetProperty("full_tab", out _), "full_tab must not be written on save");
            Assert.IsTrue(root.TryGetProperty("main_page", out JsonElement mainPage));
            Assert.AreEqual("cpu", mainPage.GetString());
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
    public void Normalise_UnknownOrGarbageMainPage_FallsBackToOverall()
    {
        var config = new AppConfig { MainPage = "unknown_garbage_page" };
        config.Normalise();
        Assert.AreEqual(PageKeys.Overall, config.MainPage);

        // Valid module pages survive Normalise
        string[] validPages = { "overall", "apps", "processes", "cpu", "ram", "disk:sda", "gpu0", "gpu:0000:01:00.0", "neteth0", "battery:BAT0", PageKeys.DiskVirtual, PageKeys.DiskNetwork };
        foreach (string valid in validPages)
        {
            config.MainPage = valid;
            config.Normalise();
            Assert.AreEqual(valid, config.MainPage);
        }
    }

    [TestMethod]
    public void Migration_FullTab_DiskVirtualAndDiskNetwork_KeepsMainPage_AndSurvivesSaveLoadRoundTrip()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cfg-mig-virtual-net-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string pathVirtual = Path.Combine(dir, "virtual.json");
        string pathNetwork = Path.Combine(dir, "network.json");

        try
        {
            // diskvirtual migration
            File.WriteAllText(pathVirtual, @"{
                ""full_tab"": ""diskvirtual""
            }");

            AppConfig loadedVirtual = AppConfig.Load(pathVirtual);
            Assert.AreEqual(PageKeys.DiskVirtual, loadedVirtual.MainPage);

            // Save and reload round-trip
            Assert.IsTrue(loadedVirtual.Save(pathVirtual));
            AppConfig roundTripVirtual = AppConfig.Load(pathVirtual);
            Assert.AreEqual(PageKeys.DiskVirtual, roundTripVirtual.MainPage);

            // disknetwork migration
            File.WriteAllText(pathNetwork, @"{
                ""full_tab"": ""disknetwork""
            }");

            AppConfig loadedNetwork = AppConfig.Load(pathNetwork);
            Assert.AreEqual(PageKeys.DiskNetwork, loadedNetwork.MainPage);

            // Save and reload round-trip
            Assert.IsTrue(loadedNetwork.Save(pathNetwork));
            AppConfig roundTripNetwork = AppConfig.Load(pathNetwork);
            Assert.AreEqual(PageKeys.DiskNetwork, roundTripNetwork.MainPage);

            // Verify PageKeys.IsPageKey accepts both
            Assert.IsTrue(PageKeys.IsPageKey(PageKeys.DiskVirtual));
            Assert.IsTrue(PageKeys.IsPageKey(PageKeys.DiskNetwork));
            Assert.AreEqual("diskvirtual", PageKeys.DiskVirtual);
            Assert.AreEqual("disknetwork", PageKeys.DiskNetwork);
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
    public void Normalise_SidebarWidth_ClampedTo200And320()
    {
        var config = new AppConfig { SidebarWidth = 999 };
        config.Normalise();
        Assert.AreEqual(320, config.SidebarWidth);

        config.SidebarWidth = 50;
        config.Normalise();
        Assert.AreEqual(200, config.SidebarWidth);

        config.SidebarWidth = -1;
        config.Normalise();
        Assert.AreEqual(230, config.SidebarWidth);
    }
}
