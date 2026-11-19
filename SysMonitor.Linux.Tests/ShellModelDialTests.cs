using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

/// <summary>
/// Regression test for the dial commit path in ShellModel.SetDial:
/// live drag updates in-memory model without writing to disk (save: false),
/// and the pointer release commits the same value to disk (save: true).
/// </summary>
[TestClass]
public sealed class ShellModelDialTests
{
    [TestMethod]
    public void SetDial_CommitPath_WithXdgConfigHome_PersistsOpacityAndFontSize()
    {
        string dir = Path.Combine(Path.GetTempPath(), "shellmodel-dial-xdg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string? oldXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", dir);

            var model = new ShellModel();

            // 1. Opacity: live drag with save: false
            bool dragOpacityResult = model.SetDial(SettingKeys.Opacity, 0.75, save: false);
            Assert.IsTrue(dragOpacityResult);
            Assert.AreEqual(0.75, model.Config.Opacity, 0.001);
            Assert.AreEqual(0.75, model.DialValue(SettingKeys.Opacity), 0.001);
            Assert.IsFalse(File.Exists(AppConfig.DefaultPath), "Config file must not be written during live drag (save: false)");

            // Commit the same value on release with save: true
            bool commitOpacityResult = model.SetDial(SettingKeys.Opacity, 0.75, save: true);
            Assert.IsTrue(commitOpacityResult);
            Assert.IsTrue(File.Exists(AppConfig.DefaultPath), "Config file must exist after dial release commit (save: true)");

            // Reload from disk and assert opacity persisted
            AppConfig reloadedOpacity = AppConfig.Load();
            Assert.AreEqual(0.75, reloadedOpacity.Opacity, 0.001);

            // 2. Font size: live drag with save: false
            bool dragFontResult = model.SetDial(SettingKeys.FontSize, 1.3, save: false);
            Assert.IsTrue(dragFontResult);
            Assert.AreEqual(1.3, model.Config.FontScale, 0.01);
            Assert.AreEqual(1.3, model.DialValue(SettingKeys.FontSize), 0.01);

            // Verify disk config still has the uncommitted font scale (default 1.0)
            AppConfig diskBeforeFontCommit = AppConfig.Load();
            Assert.AreEqual(1.0, diskBeforeFontCommit.FontScale, 0.01);

            // Commit the same value on release with save: true
            bool commitFontResult = model.SetDial(SettingKeys.FontSize, 1.3, save: true);
            Assert.IsTrue(commitFontResult);

            // Reload from disk and assert font scale persisted
            AppConfig reloadedFont = AppConfig.Load();
            Assert.AreEqual(1.3, reloadedFont.FontScale, 0.01);
            Assert.AreEqual(0.75, reloadedFont.Opacity, 0.001);
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
    public void SetDial_CommitPath_WithExplicitConfigPath_PersistsOpacityAndFontSize()
    {
        string dir = Path.Combine(Path.GetTempPath(), "shellmodel-dial-custom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string configPath = Path.Combine(dir, "custom-config.json");

        try
        {
            var model = new ShellModel(configPath: configPath);

            // 1. Opacity: live drag then commit
            Assert.IsTrue(model.SetDial(SettingKeys.Opacity, 0.65, save: false));
            Assert.AreEqual(0.65, model.Config.Opacity, 0.001);
            Assert.IsFalse(File.Exists(configPath), "Config file must not exist before commit");

            Assert.IsTrue(model.SetDial(SettingKeys.Opacity, 0.65, save: true));
            Assert.IsTrue(File.Exists(configPath), "Config file must exist after commit");

            AppConfig loadedOpacity = AppConfig.Load(configPath);
            Assert.AreEqual(0.65, loadedOpacity.Opacity, 0.001);

            // 2. Font size: live drag then commit
            Assert.IsTrue(model.SetDial(SettingKeys.FontSize, 1.4, save: false));
            Assert.AreEqual(1.4, model.Config.FontScale, 0.01);

            AppConfig diskBeforeFontCommit = AppConfig.Load(configPath);
            Assert.AreEqual(1.0, diskBeforeFontCommit.FontScale, 0.01);

            Assert.IsTrue(model.SetDial(SettingKeys.FontSize, 1.4, save: true));

            AppConfig loadedFont = AppConfig.Load(configPath);
            Assert.AreEqual(1.4, loadedFont.FontScale, 0.01);
            Assert.AreEqual(0.65, loadedFont.Opacity, 0.001);
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
