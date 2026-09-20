using System.IO;
using System.Text.Json;

namespace SysMonitor.Tests;

[TestClass]
public class PaletteTests
{
    [TestMethod]
    public void Temperature_text_marks_an_estimate_and_a_missing_sensor()
    {
        Assert.AreEqual("n/a", Palette.TempText(null, estimated: false));
        Assert.AreEqual("n/a", Palette.TempText(null, estimated: true));
        Assert.AreEqual("44°C", Palette.TempText(44, estimated: false));
        Assert.AreEqual("~44°C", Palette.TempText(44, estimated: true));
    }

    [TestMethod]
    public void Hot_starts_at_the_threshold_not_past_it()
    {
        Assert.IsFalse(Palette.IsHot(Palette.HotAt - 1));
        Assert.IsTrue(Palette.IsHot(Palette.HotAt));
        Assert.IsFalse(Palette.IsHot(null));
    }

    [TestMethod]
    public void A_missing_reading_gets_the_neutral_badge()
    {
        var pal = Palette.For("dark");
        (var fore, var back) = Palette.TempColors(null, pal);
        Assert.AreEqual(pal.Label, fore);
        Assert.AreEqual(pal.Tag, back);
    }

    [TestMethod]
    public void Badge_colours_step_at_warm_and_hot()
    {
        var pal = Palette.For("dark");
        Assert.AreEqual(Palette.TempOk, Palette.TempColors(40, pal).Fore);
        Assert.AreEqual(Palette.TempWarm, Palette.TempColors(Palette.WarmAt, pal).Fore);
        Assert.AreEqual(Palette.TempHot, Palette.TempColors(Palette.HotAt, pal).Fore);
    }

    [TestMethod]
    public void Usage_bars_turn_amber_at_70_and_red_at_90()
    {
        Assert.AreEqual(Palette.AccentCpu, Palette.LoadColor(0));
        Assert.AreEqual(Palette.AccentCpu, Palette.LoadColor(69));
        Assert.AreEqual(Palette.LoadWarm, Palette.LoadColor(70));
        Assert.AreEqual(Palette.LoadWarm, Palette.LoadColor(89));
        Assert.AreEqual(Palette.LoadHot, Palette.LoadColor(90));
        Assert.AreEqual(Palette.LoadHot, Palette.LoadColor(100));
    }

    [TestMethod]
    public void The_usage_thresholds_are_not_the_temperature_ones()
    {
        // Changing where a bar turns amber must never move the thermometer.
        Assert.AreEqual(65, Palette.WarmAt);
        Assert.AreEqual(80, Palette.HotAt);
        Assert.AreEqual(70, Palette.LoadWarmAt);
        Assert.AreEqual(90, Palette.LoadHotAt);

        // A CPU at 85% is a red bar; a drive at 85 C is a hot badge; the two
        // decisions are independent.
        Assert.AreEqual(Palette.LoadWarm, Palette.LoadColor(85));
        Assert.AreEqual(Palette.TempHot, Palette.TempColors(85, Palette.For("dark")).Fore);
    }

    [TestMethod]
    public void The_same_colour_hands_back_the_same_frozen_brush()
    {
        var first = Palette.Brush(Palette.AccentRam);
        var second = Palette.Brush(Palette.AccentRam);
        Assert.AreSame(first, second);
        Assert.IsTrue(first.IsFrozen);
    }
}

[TestClass]
public class ConfigTests
{
    [TestMethod]
    public void Speed_selects_the_sampling_cadence()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(5),
            new AppConfig { Speed = "eco" }.Intervals.Metrics);
        Assert.AreEqual(TimeSpan.FromSeconds(2.5),
            new AppConfig { Speed = "balanced" }.Intervals.Metrics);
        Assert.AreEqual(TimeSpan.FromSeconds(1),
            new AppConfig { Speed = "fast" }.Intervals.Metrics);
    }

    [TestMethod]
    public void An_unknown_speed_falls_back_to_balanced()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(2.5),
            new AppConfig { Speed = "turbo" }.Intervals.Metrics);
    }

    [TestMethod]
    public void Settings_survive_a_json_round_trip()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        var original = new AppConfig
        {
            Lang = "en",
            Theme = "light",
            Opacity = 0.5,
            CpuMode = "total",
            PosX = 1049,
            PosY = 46,
            ExpW = 763,
            ExpH = 715,
        };

        var restored = JsonSerializer.Deserialize<AppConfig>(
            JsonSerializer.Serialize(original, options), options)!;

        Assert.AreEqual("en", restored.Lang);
        Assert.AreEqual("light", restored.Theme);
        Assert.AreEqual(0.5, restored.Opacity);
        Assert.AreEqual("total", restored.CpuMode);
        Assert.AreEqual(1049, restored.PosX);
        Assert.AreEqual(46, restored.PosY);
        Assert.AreEqual(763, restored.ExpW);
        Assert.AreEqual(715, restored.ExpH);
    }

    [TestMethod]
    public void The_config_file_is_separate_from_the_python_build()
    {
        // Both live in %APPDATA%\SysMonitor; they must not share a filename.
        Assert.AreEqual("config.wpf.json", Path.GetFileName(AppConfig.Path));
    }

    [TestMethod]
    public void A_fresh_config_has_no_saved_position()
    {
        var config = new AppConfig();
        Assert.IsNull(config.PosX);
        Assert.IsNull(config.PosY);
    }
}

[TestClass]
public class StartupTests
{
    [TestMethod]
    public void The_run_command_is_quoted_for_a_path_with_spaces()
    {
        Assert.AreEqual("\"C:\\Program Files\\SysMonitor.exe\"",
            Startup.Command(@"C:\Program Files\SysMonitor.exe"));
    }
}

[TestClass]
public class TrafficLightTests
{
    [TestMethod]
    public void Every_meter_keeps_its_own_colour_until_the_warning_point()
    {
        // Memory is purple, disk green, network cyan -- right up to 70%.
        Assert.AreEqual(Palette.AccentRam, Palette.LoadColor(69, Palette.AccentRam));
        Assert.AreEqual(Palette.AccentDisk, Palette.LoadColor(69, Palette.AccentDisk));
        Assert.AreEqual(Palette.AccentNet, Palette.LoadColor(69, Palette.AccentNet));
    }

    [TestMethod]
    public void Past_the_warning_point_every_meter_turns_the_same_colour()
    {
        // A drive at 79% has to read as a warning, not as a long green bar.
        Assert.AreEqual(Palette.LoadWarm, Palette.LoadColor(79, Palette.AccentDisk));
        Assert.AreEqual(Palette.LoadWarm, Palette.LoadColor(79, Palette.AccentRam));
        Assert.AreEqual(Palette.LoadHot, Palette.LoadColor(95, Palette.AccentNet));
    }

    [TestMethod]
    public void With_no_accent_given_the_cpu_colour_is_the_default()
    {
        Assert.AreEqual(Palette.AccentCpu, Palette.LoadColor(10));
    }
}
