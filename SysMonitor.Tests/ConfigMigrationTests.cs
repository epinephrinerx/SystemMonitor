using System.IO;
using System.Text.Json;

namespace SysMonitor.Tests;

/// <summary>
/// Renaming the three views widget / overall / full renamed their settings
/// too, because the key names in config.wpf.json are generated from the
/// property names. Without a migration every existing install would have
/// opened one morning with its window back at the default size.
/// </summary>
[TestClass]
public class ConfigMigrationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Load() reads a fixed path, so the migration is exercised through the
    /// same deserialise-then-migrate pair that Load() performs.
    /// </summary>
    private static AppConfig Load(string json)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "sysmon-config-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        try
        {
            return AppConfig.LoadFrom(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void The_old_key_names_are_carried_over()
    {
        AppConfig config = Load("""
        {
          "lang": "th",
          "mini_w": 252,
          "mini_h": 109,
          "exp_w": 637,
          "exp_h": 851,
          "exp_sized": true
        }
        """);

        Assert.AreEqual(252, config.WidgetW);
        Assert.AreEqual(109, config.WidgetH);
        Assert.AreEqual(637, config.OverallW);
        Assert.AreEqual(851, config.OverallH);
        Assert.IsTrue(config.OverallSized, "a hand-sized window stays hand-sized");
    }

    [TestMethod]
    public void A_new_key_wins_over_a_leftover_old_one()
    {
        // A file written by this version beside one left by the last must
        // follow the new name, or an upgrade would undo itself.
        AppConfig config = Load("""
        { "exp_w": 637, "exp_h": 851, "overall_w": 900, "overall_h": 700 }
        """);

        Assert.AreEqual(900, config.OverallW);
        Assert.AreEqual(700, config.OverallH);
    }

    [TestMethod]
    public void A_file_with_neither_gets_the_defaults()
    {
        AppConfig config = Load("""{ "lang": "en" }""");

        Assert.AreEqual(new AppConfig().OverallW, config.OverallW);
        Assert.AreEqual(new AppConfig().WidgetW, config.WidgetW);
        Assert.IsFalse(config.OverallSized);
    }

    [TestMethod]
    public void A_corrupt_file_does_not_throw()
    {
        Assert.AreEqual(new AppConfig().OverallW, Load("{ not json").OverallW);
    }

    [TestMethod]
    public void A_wrongly_typed_old_key_is_ignored_rather_than_believed()
    {
        AppConfig config = Load("""{ "exp_w": "wide", "exp_sized": "yes" }""");

        Assert.AreEqual(new AppConfig().OverallW, config.OverallW);
        Assert.IsFalse(config.OverallSized);
    }

    [TestMethod]
    public void Closing_defaults_to_the_tray()
    {
        // A monitor that quits when you dismiss its window is one you have to
        // keep restarting. Only a fresh install sees this.
        Assert.AreEqual("tray", new AppConfig().CloseAction);
        Assert.AreEqual("exit", Load("""{ "close_action": "exit" }""").CloseAction,
            "an answer already saved is left alone");
    }
}
