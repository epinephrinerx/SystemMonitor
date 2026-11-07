#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class AppSettingsTests
{
    private static void AssertDefaults(AppSettings actual)
    {
        // oracle: section 1 enumerates these nineteen literal defaults; section 2 failure loads return them.
        Assert.AreEqual("light", actual.Theme);
        Assert.AreEqual("balanced", actual.Speed);
        Assert.AreEqual("mini", actual.Mode);
        Assert.AreEqual(true, actual.AlwaysOnTop);
        Assert.AreEqual(true, actual.ShowCpu);
        Assert.AreEqual(true, actual.ShowRam);
        Assert.AreEqual(true, actual.ShowDisk);
        Assert.AreEqual(true, actual.ShowNetwork);
        Assert.AreEqual(0.92, actual.Opacity);
        Assert.AreEqual(1.0, actual.FontScale);
        Assert.AreEqual(270.0, actual.MiniW);
        Assert.AreEqual(104.0, actual.MiniH);
        Assert.AreEqual(630.0, actual.ExpW);
        Assert.AreEqual(480.0, actual.ExpH);
        Assert.AreEqual(0.0, actual.FullW);
        Assert.AreEqual(0.0, actual.FullH);
        Assert.IsNull(actual.PosX);
        Assert.IsNull(actual.PosY);
        Assert.AreEqual("", actual.FullTab);
    }

    [TestMethod]
    public void Defaults_AllNineteenProperties()
    {
        var s = new AppSettings();
        // oracle: section 1 lists all nineteen defaults.
        Assert.AreEqual("light", s.Theme);
        Assert.AreEqual("balanced", s.Speed);
        Assert.AreEqual("mini", s.Mode);
        Assert.AreEqual(true, s.AlwaysOnTop);
        Assert.AreEqual(true, s.ShowCpu);
        Assert.AreEqual(true, s.ShowRam);
        Assert.AreEqual(true, s.ShowDisk);
        Assert.AreEqual(true, s.ShowNetwork);
        Assert.AreEqual(0.92, s.Opacity);
        Assert.AreEqual(1.0, s.FontScale);
        Assert.AreEqual(270.0, s.MiniW);
        Assert.AreEqual(104.0, s.MiniH);
        Assert.AreEqual(630.0, s.ExpW);
        Assert.AreEqual(480.0, s.ExpH);
        Assert.AreEqual(0.0, s.FullW);
        Assert.AreEqual(0.0, s.FullH);
        Assert.IsNull(s.PosX);
        Assert.IsNull(s.PosY);
        Assert.AreEqual("", s.FullTab);
    }

    [TestMethod]
    public void Sanitize_Theme_light_NormalizesCaseAndWhitespace()
    {
        var s = new AppSettings { Theme = " \tLIGHT\r\n " };
        s.Sanitize();
        // oracle: section 1 Theme uses Trim then ToLowerInvariant.
        Assert.AreEqual("light", s.Theme);
    }

    [TestMethod]
    public void Sanitize_Theme_dark_NormalizesCaseAndWhitespace()
    {
        var s = new AppSettings { Theme = " \tDARK\r\n " };
        s.Sanitize();
        // oracle: section 1 Theme uses Trim then ToLowerInvariant.
        Assert.AreEqual("dark", s.Theme);
    }

    [TestMethod]
    public void Sanitize_Theme_Null_UsesDefault()
    {
        var s = new AppSettings { Theme = null! };
        s.Sanitize();
        // oracle: section 1 invalid or null Theme defaults to light.
        Assert.AreEqual("light", s.Theme);
    }

    [TestMethod]
    public void Sanitize_Theme_Empty_UsesDefault()
    {
        var s = new AppSettings { Theme = "" };
        s.Sanitize();
        // oracle: section 1 invalid or null Theme defaults to light.
        Assert.AreEqual("light", s.Theme);
    }

    [TestMethod]
    public void Sanitize_Theme_Blank_UsesDefault()
    {
        var s = new AppSettings { Theme = " \t " };
        s.Sanitize();
        // oracle: section 1 invalid or null Theme defaults to light.
        Assert.AreEqual("light", s.Theme);
    }

    [TestMethod]
    public void Sanitize_Theme_Unknown_UsesDefault()
    {
        var s = new AppSettings { Theme = "invalid" };
        s.Sanitize();
        // oracle: section 1 invalid or null Theme defaults to light.
        Assert.AreEqual("light", s.Theme);
    }

    [TestMethod]
    public void Sanitize_Speed_eco_NormalizesCaseAndWhitespace()
    {
        var s = new AppSettings { Speed = " \tECO\r\n " };
        s.Sanitize();
        // oracle: section 1 Speed uses Trim then ToLowerInvariant.
        Assert.AreEqual("eco", s.Speed);
    }

    [TestMethod]
    public void Sanitize_Speed_balanced_NormalizesCaseAndWhitespace()
    {
        var s = new AppSettings { Speed = " \tBALANCED\r\n " };
        s.Sanitize();
        // oracle: section 1 Speed uses Trim then ToLowerInvariant.
        Assert.AreEqual("balanced", s.Speed);
    }

    [TestMethod]
    public void Sanitize_Speed_fast_NormalizesCaseAndWhitespace()
    {
        var s = new AppSettings { Speed = " \tFAST\r\n " };
        s.Sanitize();
        // oracle: section 1 Speed uses Trim then ToLowerInvariant.
        Assert.AreEqual("fast", s.Speed);
    }

    [TestMethod]
    public void Sanitize_Speed_Null_UsesDefault()
    {
        var s = new AppSettings { Speed = null! };
        s.Sanitize();
        // oracle: section 1 invalid or null Speed defaults to balanced.
        Assert.AreEqual("balanced", s.Speed);
    }

    [TestMethod]
    public void Sanitize_Speed_Empty_UsesDefault()
    {
        var s = new AppSettings { Speed = "" };
        s.Sanitize();
        // oracle: section 1 invalid or null Speed defaults to balanced.
        Assert.AreEqual("balanced", s.Speed);
    }

    [TestMethod]
    public void Sanitize_Speed_Blank_UsesDefault()
    {
        var s = new AppSettings { Speed = " \t " };
        s.Sanitize();
        // oracle: section 1 invalid or null Speed defaults to balanced.
        Assert.AreEqual("balanced", s.Speed);
    }

    [TestMethod]
    public void Sanitize_Speed_Unknown_UsesDefault()
    {
        var s = new AppSettings { Speed = "invalid" };
        s.Sanitize();
        // oracle: section 1 invalid or null Speed defaults to balanced.
        Assert.AreEqual("balanced", s.Speed);
    }

    [TestMethod]
    public void Sanitize_Mode_mini_NormalizesCaseAndWhitespace()
    {
        var s = new AppSettings { Mode = " \tMINI\r\n " };
        s.Sanitize();
        // oracle: section 1 Mode uses Trim then ToLowerInvariant.
        Assert.AreEqual("mini", s.Mode);
    }

    [TestMethod]
    public void Sanitize_Mode_expanded_NormalizesCaseAndWhitespace()
    {
        var s = new AppSettings { Mode = " \tEXPANDED\r\n " };
        s.Sanitize();
        // oracle: section 1 Mode uses Trim then ToLowerInvariant.
        Assert.AreEqual("expanded", s.Mode);
    }

    [TestMethod]
    public void Sanitize_Mode_full_NormalizesCaseAndWhitespace()
    {
        var s = new AppSettings { Mode = " \tFULL\r\n " };
        s.Sanitize();
        // oracle: section 1 Mode uses Trim then ToLowerInvariant.
        Assert.AreEqual("full", s.Mode);
    }

    [TestMethod]
    public void Sanitize_Mode_Null_UsesDefault()
    {
        var s = new AppSettings { Mode = null! };
        s.Sanitize();
        // oracle: section 1 invalid or null Mode defaults to mini.
        Assert.AreEqual("mini", s.Mode);
    }

    [TestMethod]
    public void Sanitize_Mode_Empty_UsesDefault()
    {
        var s = new AppSettings { Mode = "" };
        s.Sanitize();
        // oracle: section 1 invalid or null Mode defaults to mini.
        Assert.AreEqual("mini", s.Mode);
    }

    [TestMethod]
    public void Sanitize_Mode_Blank_UsesDefault()
    {
        var s = new AppSettings { Mode = " \t " };
        s.Sanitize();
        // oracle: section 1 invalid or null Mode defaults to mini.
        Assert.AreEqual("mini", s.Mode);
    }

    [TestMethod]
    public void Sanitize_Mode_Unknown_UsesDefault()
    {
        var s = new AppSettings { Mode = "invalid" };
        s.Sanitize();
        // oracle: section 1 invalid or null Mode defaults to mini.
        Assert.AreEqual("mini", s.Mode);
    }

    [TestMethod]
    public void Sanitize_Opacity_NaN()
    {
        var s = new AppSettings { Opacity = double.NaN };
        s.Sanitize();
        // oracle: section 1 Opacity: NaN defaults; otherwise clamp 0.3..1.
        Assert.AreEqual(0.92, s.Opacity);
    }

    [TestMethod]
    public void Sanitize_Opacity_NegativeInfinity()
    {
        var s = new AppSettings { Opacity = double.NegativeInfinity };
        s.Sanitize();
        // oracle: section 1 Opacity: NaN defaults; otherwise clamp 0.3..1.
        Assert.AreEqual(0.3, s.Opacity);
    }

    [TestMethod]
    public void Sanitize_Opacity_PositiveInfinity()
    {
        var s = new AppSettings { Opacity = double.PositiveInfinity };
        s.Sanitize();
        // oracle: section 1 Opacity: NaN defaults; otherwise clamp 0.3..1.
        Assert.AreEqual(1.0, s.Opacity);
    }

    [TestMethod]
    public void Sanitize_Opacity_Below()
    {
        var s = new AppSettings { Opacity = 0.29 };
        s.Sanitize();
        // oracle: section 1 Opacity: NaN defaults; otherwise clamp 0.3..1.
        Assert.AreEqual(0.3, s.Opacity);
    }

    [TestMethod]
    public void Sanitize_Opacity_Minimum()
    {
        var s = new AppSettings { Opacity = 0.3 };
        s.Sanitize();
        // oracle: section 1 Opacity: NaN defaults; otherwise clamp 0.3..1.
        Assert.AreEqual(0.3, s.Opacity);
    }

    [TestMethod]
    public void Sanitize_Opacity_Inside()
    {
        var s = new AppSettings { Opacity = 0.65 };
        s.Sanitize();
        // oracle: section 1 Opacity: NaN defaults; otherwise clamp 0.3..1.
        Assert.AreEqual(0.65, s.Opacity);
    }

    [TestMethod]
    public void Sanitize_Opacity_Maximum()
    {
        var s = new AppSettings { Opacity = 1 };
        s.Sanitize();
        // oracle: section 1 Opacity: NaN defaults; otherwise clamp 0.3..1.
        Assert.AreEqual(1.0, s.Opacity);
    }

    [TestMethod]
    public void Sanitize_Opacity_Above()
    {
        var s = new AppSettings { Opacity = 1.01 };
        s.Sanitize();
        // oracle: section 1 Opacity: NaN defaults; otherwise clamp 0.3..1.
        Assert.AreEqual(1.0, s.Opacity);
    }

    [TestMethod]
    public void Sanitize_FontScale_NaN()
    {
        var s = new AppSettings { FontScale = double.NaN };
        s.Sanitize();
        // oracle: section 1 FontScale: NaN defaults; otherwise clamp 0.8..1.6.
        Assert.AreEqual(1.0, s.FontScale);
    }

    [TestMethod]
    public void Sanitize_FontScale_NegativeInfinity()
    {
        var s = new AppSettings { FontScale = double.NegativeInfinity };
        s.Sanitize();
        // oracle: section 1 FontScale: NaN defaults; otherwise clamp 0.8..1.6.
        Assert.AreEqual(0.8, s.FontScale);
    }

    [TestMethod]
    public void Sanitize_FontScale_PositiveInfinity()
    {
        var s = new AppSettings { FontScale = double.PositiveInfinity };
        s.Sanitize();
        // oracle: section 1 FontScale: NaN defaults; otherwise clamp 0.8..1.6.
        Assert.AreEqual(1.6, s.FontScale);
    }

    [TestMethod]
    public void Sanitize_FontScale_Below()
    {
        var s = new AppSettings { FontScale = 0.79 };
        s.Sanitize();
        // oracle: section 1 FontScale: NaN defaults; otherwise clamp 0.8..1.6.
        Assert.AreEqual(0.8, s.FontScale);
    }

    [TestMethod]
    public void Sanitize_FontScale_Minimum()
    {
        var s = new AppSettings { FontScale = 0.8 };
        s.Sanitize();
        // oracle: section 1 FontScale: NaN defaults; otherwise clamp 0.8..1.6.
        Assert.AreEqual(0.8, s.FontScale);
    }

    [TestMethod]
    public void Sanitize_FontScale_Inside()
    {
        var s = new AppSettings { FontScale = 1.2000000000000002 };
        s.Sanitize();
        // oracle: section 1 FontScale: NaN defaults; otherwise clamp 0.8..1.6.
        Assert.AreEqual(1.2000000000000002, s.FontScale);
    }

    [TestMethod]
    public void Sanitize_FontScale_Maximum()
    {
        var s = new AppSettings { FontScale = 1.6 };
        s.Sanitize();
        // oracle: section 1 FontScale: NaN defaults; otherwise clamp 0.8..1.6.
        Assert.AreEqual(1.6, s.FontScale);
    }

    [TestMethod]
    public void Sanitize_FontScale_Above()
    {
        var s = new AppSettings { FontScale = 1.61 };
        s.Sanitize();
        // oracle: section 1 FontScale: NaN defaults; otherwise clamp 0.8..1.6.
        Assert.AreEqual(1.6, s.FontScale);
    }

    [TestMethod]
    public void Sanitize_MiniW_Zero()
    {
        var s = new AppSettings { MiniW = 0 };
        s.Sanitize();
        // oracle: section 1 MiniW: NaN/nonpositive => 270; positive values clamp to 100..10000.
        Assert.AreEqual(270.0, s.MiniW);
    }

    [TestMethod]
    public void Sanitize_MiniW_Negative()
    {
        var s = new AppSettings { MiniW = -1 };
        s.Sanitize();
        // oracle: section 1 MiniW: NaN/nonpositive => 270; positive values clamp to 100..10000.
        Assert.AreEqual(270.0, s.MiniW);
    }

    [TestMethod]
    public void Sanitize_MiniW_NaN()
    {
        var s = new AppSettings { MiniW = double.NaN };
        s.Sanitize();
        // oracle: section 1 MiniW: NaN/nonpositive => 270; positive values clamp to 100..10000.
        Assert.AreEqual(270.0, s.MiniW);
    }

    [TestMethod]
    public void Sanitize_MiniW_NegativeInfinity()
    {
        var s = new AppSettings { MiniW = double.NegativeInfinity };
        s.Sanitize();
        // oracle: section 1 MiniW: NaN/nonpositive => 270; positive values clamp to 100..10000.
        Assert.AreEqual(270.0, s.MiniW);
    }

    [TestMethod]
    public void Sanitize_MiniW_PositiveInfinity()
    {
        var s = new AppSettings { MiniW = double.PositiveInfinity };
        s.Sanitize();
        // oracle: section 1 MiniW: NaN/nonpositive => 270; positive values clamp to 100..10000.
        Assert.AreEqual(10000.0, s.MiniW);
    }

    [TestMethod]
    public void Sanitize_MiniW_Fifty()
    {
        var s = new AppSettings { MiniW = 50 };
        s.Sanitize();
        // oracle: section 1 MiniW: NaN/nonpositive => 270; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.MiniW);
    }

    [TestMethod]
    public void Sanitize_MiniW_JustPositive()
    {
        var s = new AppSettings { MiniW = 0.001 };
        s.Sanitize();
        // oracle: section 1 MiniW: NaN/nonpositive => 270; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.MiniW);
    }

    [TestMethod]
    public void Sanitize_MiniW_JustBelowMinimum()
    {
        var s = new AppSettings { MiniW = 99.999 };
        s.Sanitize();
        // oracle: section 1 MiniW: NaN/nonpositive => 270; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.MiniW);
    }

    [TestMethod]
    public void Sanitize_MiniW_Minimum()
    {
        var s = new AppSettings { MiniW = 100 };
        s.Sanitize();
        // oracle: section 1 MiniW: NaN/nonpositive => 270; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.MiniW);
    }

    [TestMethod]
    public void Sanitize_MiniW_Interior()
    {
        var s = new AppSettings { MiniW = 321.25 };
        s.Sanitize();
        // oracle: section 1 MiniW: NaN/nonpositive => 270; positive values clamp to 100..10000.
        Assert.AreEqual(321.25, s.MiniW);
    }

    [TestMethod]
    public void Sanitize_MiniW_Maximum()
    {
        var s = new AppSettings { MiniW = 10000 };
        s.Sanitize();
        // oracle: section 1 MiniW: NaN/nonpositive => 270; positive values clamp to 100..10000.
        Assert.AreEqual(10000.0, s.MiniW);
    }

    [TestMethod]
    public void Sanitize_MiniW_AboveMaximum()
    {
        var s = new AppSettings { MiniW = 10000.001 };
        s.Sanitize();
        // oracle: section 1 MiniW: NaN/nonpositive => 270; positive values clamp to 100..10000.
        Assert.AreEqual(10000.0, s.MiniW);
    }

    [TestMethod]
    public void Sanitize_MiniH_Zero()
    {
        var s = new AppSettings { MiniH = 0 };
        s.Sanitize();
        // oracle: section 1 MiniH: NaN/nonpositive => 104; positive values clamp to 100..10000.
        Assert.AreEqual(104.0, s.MiniH);
    }

    [TestMethod]
    public void Sanitize_MiniH_Negative()
    {
        var s = new AppSettings { MiniH = -1 };
        s.Sanitize();
        // oracle: section 1 MiniH: NaN/nonpositive => 104; positive values clamp to 100..10000.
        Assert.AreEqual(104.0, s.MiniH);
    }

    [TestMethod]
    public void Sanitize_MiniH_NaN()
    {
        var s = new AppSettings { MiniH = double.NaN };
        s.Sanitize();
        // oracle: section 1 MiniH: NaN/nonpositive => 104; positive values clamp to 100..10000.
        Assert.AreEqual(104.0, s.MiniH);
    }

    [TestMethod]
    public void Sanitize_MiniH_NegativeInfinity()
    {
        var s = new AppSettings { MiniH = double.NegativeInfinity };
        s.Sanitize();
        // oracle: section 1 MiniH: NaN/nonpositive => 104; positive values clamp to 100..10000.
        Assert.AreEqual(104.0, s.MiniH);
    }

    [TestMethod]
    public void Sanitize_MiniH_PositiveInfinity()
    {
        var s = new AppSettings { MiniH = double.PositiveInfinity };
        s.Sanitize();
        // oracle: section 1 MiniH: NaN/nonpositive => 104; positive values clamp to 100..10000.
        Assert.AreEqual(10000.0, s.MiniH);
    }

    [TestMethod]
    public void Sanitize_MiniH_Fifty()
    {
        var s = new AppSettings { MiniH = 50 };
        s.Sanitize();
        // oracle: section 1 MiniH: NaN/nonpositive => 104; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.MiniH);
    }

    [TestMethod]
    public void Sanitize_MiniH_JustPositive()
    {
        var s = new AppSettings { MiniH = 0.001 };
        s.Sanitize();
        // oracle: section 1 MiniH: NaN/nonpositive => 104; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.MiniH);
    }

    [TestMethod]
    public void Sanitize_MiniH_JustBelowMinimum()
    {
        var s = new AppSettings { MiniH = 99.999 };
        s.Sanitize();
        // oracle: section 1 MiniH: NaN/nonpositive => 104; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.MiniH);
    }

    [TestMethod]
    public void Sanitize_MiniH_Minimum()
    {
        var s = new AppSettings { MiniH = 100 };
        s.Sanitize();
        // oracle: section 1 MiniH: NaN/nonpositive => 104; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.MiniH);
    }

    [TestMethod]
    public void Sanitize_MiniH_Interior()
    {
        var s = new AppSettings { MiniH = 321.25 };
        s.Sanitize();
        // oracle: section 1 MiniH: NaN/nonpositive => 104; positive values clamp to 100..10000.
        Assert.AreEqual(321.25, s.MiniH);
    }

    [TestMethod]
    public void Sanitize_MiniH_Maximum()
    {
        var s = new AppSettings { MiniH = 10000 };
        s.Sanitize();
        // oracle: section 1 MiniH: NaN/nonpositive => 104; positive values clamp to 100..10000.
        Assert.AreEqual(10000.0, s.MiniH);
    }

    [TestMethod]
    public void Sanitize_MiniH_AboveMaximum()
    {
        var s = new AppSettings { MiniH = 10000.001 };
        s.Sanitize();
        // oracle: section 1 MiniH: NaN/nonpositive => 104; positive values clamp to 100..10000.
        Assert.AreEqual(10000.0, s.MiniH);
    }

    [TestMethod]
    public void Sanitize_ExpW_Zero()
    {
        var s = new AppSettings { ExpW = 0 };
        s.Sanitize();
        // oracle: section 1 ExpW: NaN/nonpositive => 630; positive values clamp to 100..10000.
        Assert.AreEqual(630.0, s.ExpW);
    }

    [TestMethod]
    public void Sanitize_ExpW_Negative()
    {
        var s = new AppSettings { ExpW = -1 };
        s.Sanitize();
        // oracle: section 1 ExpW: NaN/nonpositive => 630; positive values clamp to 100..10000.
        Assert.AreEqual(630.0, s.ExpW);
    }

    [TestMethod]
    public void Sanitize_ExpW_NaN()
    {
        var s = new AppSettings { ExpW = double.NaN };
        s.Sanitize();
        // oracle: section 1 ExpW: NaN/nonpositive => 630; positive values clamp to 100..10000.
        Assert.AreEqual(630.0, s.ExpW);
    }

    [TestMethod]
    public void Sanitize_ExpW_NegativeInfinity()
    {
        var s = new AppSettings { ExpW = double.NegativeInfinity };
        s.Sanitize();
        // oracle: section 1 ExpW: NaN/nonpositive => 630; positive values clamp to 100..10000.
        Assert.AreEqual(630.0, s.ExpW);
    }

    [TestMethod]
    public void Sanitize_ExpW_PositiveInfinity()
    {
        var s = new AppSettings { ExpW = double.PositiveInfinity };
        s.Sanitize();
        // oracle: section 1 ExpW: NaN/nonpositive => 630; positive values clamp to 100..10000.
        Assert.AreEqual(10000.0, s.ExpW);
    }

    [TestMethod]
    public void Sanitize_ExpW_Fifty()
    {
        var s = new AppSettings { ExpW = 50 };
        s.Sanitize();
        // oracle: section 1 ExpW: NaN/nonpositive => 630; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.ExpW);
    }

    [TestMethod]
    public void Sanitize_ExpW_JustPositive()
    {
        var s = new AppSettings { ExpW = 0.001 };
        s.Sanitize();
        // oracle: section 1 ExpW: NaN/nonpositive => 630; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.ExpW);
    }

    [TestMethod]
    public void Sanitize_ExpW_JustBelowMinimum()
    {
        var s = new AppSettings { ExpW = 99.999 };
        s.Sanitize();
        // oracle: section 1 ExpW: NaN/nonpositive => 630; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.ExpW);
    }

    [TestMethod]
    public void Sanitize_ExpW_Minimum()
    {
        var s = new AppSettings { ExpW = 100 };
        s.Sanitize();
        // oracle: section 1 ExpW: NaN/nonpositive => 630; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.ExpW);
    }

    [TestMethod]
    public void Sanitize_ExpW_Interior()
    {
        var s = new AppSettings { ExpW = 321.25 };
        s.Sanitize();
        // oracle: section 1 ExpW: NaN/nonpositive => 630; positive values clamp to 100..10000.
        Assert.AreEqual(321.25, s.ExpW);
    }

    [TestMethod]
    public void Sanitize_ExpW_Maximum()
    {
        var s = new AppSettings { ExpW = 10000 };
        s.Sanitize();
        // oracle: section 1 ExpW: NaN/nonpositive => 630; positive values clamp to 100..10000.
        Assert.AreEqual(10000.0, s.ExpW);
    }

    [TestMethod]
    public void Sanitize_ExpW_AboveMaximum()
    {
        var s = new AppSettings { ExpW = 10000.001 };
        s.Sanitize();
        // oracle: section 1 ExpW: NaN/nonpositive => 630; positive values clamp to 100..10000.
        Assert.AreEqual(10000.0, s.ExpW);
    }

    [TestMethod]
    public void Sanitize_ExpH_Zero()
    {
        var s = new AppSettings { ExpH = 0 };
        s.Sanitize();
        // oracle: section 1 ExpH: NaN/nonpositive => 480; positive values clamp to 100..10000.
        Assert.AreEqual(480.0, s.ExpH);
    }

    [TestMethod]
    public void Sanitize_ExpH_Negative()
    {
        var s = new AppSettings { ExpH = -1 };
        s.Sanitize();
        // oracle: section 1 ExpH: NaN/nonpositive => 480; positive values clamp to 100..10000.
        Assert.AreEqual(480.0, s.ExpH);
    }

    [TestMethod]
    public void Sanitize_ExpH_NaN()
    {
        var s = new AppSettings { ExpH = double.NaN };
        s.Sanitize();
        // oracle: section 1 ExpH: NaN/nonpositive => 480; positive values clamp to 100..10000.
        Assert.AreEqual(480.0, s.ExpH);
    }

    [TestMethod]
    public void Sanitize_ExpH_NegativeInfinity()
    {
        var s = new AppSettings { ExpH = double.NegativeInfinity };
        s.Sanitize();
        // oracle: section 1 ExpH: NaN/nonpositive => 480; positive values clamp to 100..10000.
        Assert.AreEqual(480.0, s.ExpH);
    }

    [TestMethod]
    public void Sanitize_ExpH_PositiveInfinity()
    {
        var s = new AppSettings { ExpH = double.PositiveInfinity };
        s.Sanitize();
        // oracle: section 1 ExpH: NaN/nonpositive => 480; positive values clamp to 100..10000.
        Assert.AreEqual(10000.0, s.ExpH);
    }

    [TestMethod]
    public void Sanitize_ExpH_Fifty()
    {
        var s = new AppSettings { ExpH = 50 };
        s.Sanitize();
        // oracle: section 1 ExpH: NaN/nonpositive => 480; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.ExpH);
    }

    [TestMethod]
    public void Sanitize_ExpH_JustPositive()
    {
        var s = new AppSettings { ExpH = 0.001 };
        s.Sanitize();
        // oracle: section 1 ExpH: NaN/nonpositive => 480; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.ExpH);
    }

    [TestMethod]
    public void Sanitize_ExpH_JustBelowMinimum()
    {
        var s = new AppSettings { ExpH = 99.999 };
        s.Sanitize();
        // oracle: section 1 ExpH: NaN/nonpositive => 480; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.ExpH);
    }

    [TestMethod]
    public void Sanitize_ExpH_Minimum()
    {
        var s = new AppSettings { ExpH = 100 };
        s.Sanitize();
        // oracle: section 1 ExpH: NaN/nonpositive => 480; positive values clamp to 100..10000.
        Assert.AreEqual(100.0, s.ExpH);
    }

    [TestMethod]
    public void Sanitize_ExpH_Interior()
    {
        var s = new AppSettings { ExpH = 321.25 };
        s.Sanitize();
        // oracle: section 1 ExpH: NaN/nonpositive => 480; positive values clamp to 100..10000.
        Assert.AreEqual(321.25, s.ExpH);
    }

    [TestMethod]
    public void Sanitize_ExpH_Maximum()
    {
        var s = new AppSettings { ExpH = 10000 };
        s.Sanitize();
        // oracle: section 1 ExpH: NaN/nonpositive => 480; positive values clamp to 100..10000.
        Assert.AreEqual(10000.0, s.ExpH);
    }

    [TestMethod]
    public void Sanitize_ExpH_AboveMaximum()
    {
        var s = new AppSettings { ExpH = 10000.001 };
        s.Sanitize();
        // oracle: section 1 ExpH: NaN/nonpositive => 480; positive values clamp to 100..10000.
        Assert.AreEqual(10000.0, s.ExpH);
    }

    [TestMethod]
    public void Sanitize_FullW_Zero()
    {
        var s = new AppSettings { FullW = 0 };
        s.Sanitize();
        // oracle: section 1 FullW: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(0.0, s.FullW);
    }

    [TestMethod]
    public void Sanitize_FullW_Negative()
    {
        var s = new AppSettings { FullW = -1 };
        s.Sanitize();
        // oracle: section 1 FullW: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(0.0, s.FullW);
    }

    [TestMethod]
    public void Sanitize_FullW_NaN()
    {
        var s = new AppSettings { FullW = double.NaN };
        s.Sanitize();
        // oracle: section 1 FullW: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(0.0, s.FullW);
    }

    [TestMethod]
    public void Sanitize_FullW_NegativeInfinity()
    {
        var s = new AppSettings { FullW = double.NegativeInfinity };
        s.Sanitize();
        // oracle: section 1 FullW: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(0.0, s.FullW);
    }

    [TestMethod]
    public void Sanitize_FullW_PositiveInfinity()
    {
        var s = new AppSettings { FullW = double.PositiveInfinity };
        s.Sanitize();
        // oracle: section 1 FullW: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(20000.0, s.FullW);
    }

    [TestMethod]
    public void Sanitize_FullW_Fifty()
    {
        var s = new AppSettings { FullW = 50 };
        s.Sanitize();
        // oracle: section 1 FullW: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(100.0, s.FullW);
    }

    [TestMethod]
    public void Sanitize_FullW_JustPositive()
    {
        var s = new AppSettings { FullW = 0.001 };
        s.Sanitize();
        // oracle: section 1 FullW: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(100.0, s.FullW);
    }

    [TestMethod]
    public void Sanitize_FullW_JustBelowMinimum()
    {
        var s = new AppSettings { FullW = 99.999 };
        s.Sanitize();
        // oracle: section 1 FullW: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(100.0, s.FullW);
    }

    [TestMethod]
    public void Sanitize_FullW_Minimum()
    {
        var s = new AppSettings { FullW = 100 };
        s.Sanitize();
        // oracle: section 1 FullW: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(100.0, s.FullW);
    }

    [TestMethod]
    public void Sanitize_FullW_Interior()
    {
        var s = new AppSettings { FullW = 321.25 };
        s.Sanitize();
        // oracle: section 1 FullW: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(321.25, s.FullW);
    }

    [TestMethod]
    public void Sanitize_FullW_Maximum()
    {
        var s = new AppSettings { FullW = 20000 };
        s.Sanitize();
        // oracle: section 1 FullW: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(20000.0, s.FullW);
    }

    [TestMethod]
    public void Sanitize_FullW_AboveMaximum()
    {
        var s = new AppSettings { FullW = 20000.001 };
        s.Sanitize();
        // oracle: section 1 FullW: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(20000.0, s.FullW);
    }

    [TestMethod]
    public void Sanitize_FullH_Zero()
    {
        var s = new AppSettings { FullH = 0 };
        s.Sanitize();
        // oracle: section 1 FullH: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(0.0, s.FullH);
    }

    [TestMethod]
    public void Sanitize_FullH_Negative()
    {
        var s = new AppSettings { FullH = -1 };
        s.Sanitize();
        // oracle: section 1 FullH: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(0.0, s.FullH);
    }

    [TestMethod]
    public void Sanitize_FullH_NaN()
    {
        var s = new AppSettings { FullH = double.NaN };
        s.Sanitize();
        // oracle: section 1 FullH: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(0.0, s.FullH);
    }

    [TestMethod]
    public void Sanitize_FullH_NegativeInfinity()
    {
        var s = new AppSettings { FullH = double.NegativeInfinity };
        s.Sanitize();
        // oracle: section 1 FullH: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(0.0, s.FullH);
    }

    [TestMethod]
    public void Sanitize_FullH_PositiveInfinity()
    {
        var s = new AppSettings { FullH = double.PositiveInfinity };
        s.Sanitize();
        // oracle: section 1 FullH: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(20000.0, s.FullH);
    }

    [TestMethod]
    public void Sanitize_FullH_Fifty()
    {
        var s = new AppSettings { FullH = 50 };
        s.Sanitize();
        // oracle: section 1 FullH: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(100.0, s.FullH);
    }

    [TestMethod]
    public void Sanitize_FullH_JustPositive()
    {
        var s = new AppSettings { FullH = 0.001 };
        s.Sanitize();
        // oracle: section 1 FullH: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(100.0, s.FullH);
    }

    [TestMethod]
    public void Sanitize_FullH_JustBelowMinimum()
    {
        var s = new AppSettings { FullH = 99.999 };
        s.Sanitize();
        // oracle: section 1 FullH: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(100.0, s.FullH);
    }

    [TestMethod]
    public void Sanitize_FullH_Minimum()
    {
        var s = new AppSettings { FullH = 100 };
        s.Sanitize();
        // oracle: section 1 FullH: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(100.0, s.FullH);
    }

    [TestMethod]
    public void Sanitize_FullH_Interior()
    {
        var s = new AppSettings { FullH = 321.25 };
        s.Sanitize();
        // oracle: section 1 FullH: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(321.25, s.FullH);
    }

    [TestMethod]
    public void Sanitize_FullH_Maximum()
    {
        var s = new AppSettings { FullH = 20000 };
        s.Sanitize();
        // oracle: section 1 FullH: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(20000.0, s.FullH);
    }

    [TestMethod]
    public void Sanitize_FullH_AboveMaximum()
    {
        var s = new AppSettings { FullH = 20000.001 };
        s.Sanitize();
        // oracle: section 1 FullH: NaN/nonpositive => 0; positive values clamp to 100..20000.
        Assert.AreEqual(20000.0, s.FullH);
    }

    [TestMethod]
    public void Sanitize_PosX_Null()
    {
        var s = new AppSettings { PosX = null };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.IsNull(s.PosX);
    }

    [TestMethod]
    public void Sanitize_PosX_NaN()
    {
        var s = new AppSettings { PosX = double.NaN };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.IsNull(s.PosX);
    }

    [TestMethod]
    public void Sanitize_PosX_PositiveInfinity()
    {
        var s = new AppSettings { PosX = double.PositiveInfinity };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.IsNull(s.PosX);
    }

    [TestMethod]
    public void Sanitize_PosX_NegativeInfinity()
    {
        var s = new AppSettings { PosX = double.NegativeInfinity };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.IsNull(s.PosX);
    }

    [TestMethod]
    public void Sanitize_PosX_Below()
    {
        var s = new AppSettings { PosX = -100000.001 };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.IsNull(s.PosX);
    }

    [TestMethod]
    public void Sanitize_PosX_Minimum()
    {
        var s = new AppSettings { PosX = -100000 };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.AreEqual((double?)-100000.0, s.PosX);
    }

    [TestMethod]
    public void Sanitize_PosX_Inside()
    {
        var s = new AppSettings { PosX = 12.5 };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.AreEqual((double?)12.5, s.PosX);
    }

    [TestMethod]
    public void Sanitize_PosX_Maximum()
    {
        var s = new AppSettings { PosX = 100000 };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.AreEqual((double?)100000.0, s.PosX);
    }

    [TestMethod]
    public void Sanitize_PosX_Above()
    {
        var s = new AppSettings { PosX = 100000.001 };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.IsNull(s.PosX);
    }

    [TestMethod]
    public void Sanitize_PosY_Null()
    {
        var s = new AppSettings { PosY = null };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.IsNull(s.PosY);
    }

    [TestMethod]
    public void Sanitize_PosY_NaN()
    {
        var s = new AppSettings { PosY = double.NaN };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.IsNull(s.PosY);
    }

    [TestMethod]
    public void Sanitize_PosY_PositiveInfinity()
    {
        var s = new AppSettings { PosY = double.PositiveInfinity };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.IsNull(s.PosY);
    }

    [TestMethod]
    public void Sanitize_PosY_NegativeInfinity()
    {
        var s = new AppSettings { PosY = double.NegativeInfinity };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.IsNull(s.PosY);
    }

    [TestMethod]
    public void Sanitize_PosY_Below()
    {
        var s = new AppSettings { PosY = -100000.001 };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.IsNull(s.PosY);
    }

    [TestMethod]
    public void Sanitize_PosY_Minimum()
    {
        var s = new AppSettings { PosY = -100000 };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.AreEqual((double?)-100000.0, s.PosY);
    }

    [TestMethod]
    public void Sanitize_PosY_Inside()
    {
        var s = new AppSettings { PosY = 12.5 };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.AreEqual((double?)12.5, s.PosY);
    }

    [TestMethod]
    public void Sanitize_PosY_Maximum()
    {
        var s = new AppSettings { PosY = 100000 };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.AreEqual((double?)100000.0, s.PosY);
    }

    [TestMethod]
    public void Sanitize_PosY_Above()
    {
        var s = new AppSettings { PosY = 100000.001 };
        s.Sanitize();
        // oracle: section 1 positions are nullable, finite and inclusive within -100000..100000.
        Assert.IsNull(s.PosY);
    }

    [TestMethod]
    public void Sanitize_FullTab_Null()
    {
        var s = new AppSettings { FullTab = null! };
        s.Sanitize();
        // oracle: section 1 FullTab: null becomes empty.
        Assert.AreEqual("", s.FullTab);
    }

    [TestMethod]
    public void Sanitize_FullTab_Controls()
    {
        var s = new AppSettings { FullTab = "a\0\t\r\n\u007fb\u0085c" };
        s.Sanitize();
        // oracle: section 1 FullTab: each char.IsControl becomes one space.
        Assert.AreEqual("a     b c", s.FullTab);
    }

    [TestMethod]
    public void Sanitize_FullTab_UnpairedSurrogates()
    {
        var s = new AppSettings { FullTab = "a\ud800b\udc00c" };
        s.Sanitize();
        // oracle: section 1 FullTab sanitisation replaces each unpaired surrogate with one space, independently of JSON encoder replacement.
        Assert.AreEqual("a b c", s.FullTab);
    }

    [TestMethod]
    public void Sanitize_FullTab_ValidSurrogatePair()
    {
        var s = new AppSettings { FullTab = "a\ud83d\ude00b" };
        s.Sanitize();
        // oracle: section 1 FullTab: a valid surrogate pair is preserved.
        Assert.AreEqual("a\ud83d\ude00b", s.FullTab);
    }

    [TestMethod]
    public void Sanitize_FullTab_Exactly200()
    {
        var s = new AppSettings { FullTab = new string('x', 200) };
        s.Sanitize();
        // oracle: section 1 FullTab: 200 UTF-16 chars are retained.
        Assert.AreEqual(new string('x', 200), s.FullTab);
    }

    [TestMethod]
    public void Sanitize_FullTab_Over200()
    {
        var s = new AppSettings { FullTab = new string('x', 201) };
        s.Sanitize();
        // oracle: section 1 FullTab: truncate at 200 UTF-16 chars.
        Assert.AreEqual(new string('x', 200), s.FullTab);
    }

    [TestMethod]
    public void Sanitize_FullTab_CutPair()
    {
        var s = new AppSettings { FullTab = new string('x', 199) + "\ud83d\ude00tail" };
        s.Sanitize();
        // oracle: section 1 FullTab: a cut between surrogate halves backs up to 199.
        Assert.AreEqual(new string('x', 199), s.FullTab);
    }

    [TestMethod]
    public void Sanitize_FullTab_PairFits()
    {
        var s = new AppSettings { FullTab = new string('x', 198) + "\ud83d\ude00tail" };
        s.Sanitize();
        // oracle: section 1 FullTab: a pair ending exactly at 200 is retained.
        Assert.AreEqual(new string('x', 198) + "\ud83d\ude00", s.FullTab);
    }

    [TestMethod]
    public void Sanitize_FullTab_TruncateBeforeTrim()
    {
        var s = new AppSettings { FullTab = " " + new string('x', 200) };
        s.Sanitize();
        // oracle: section 1 FullTab: truncate THEN trim; leading space consumes one char.
        Assert.AreEqual(new string('x', 199), s.FullTab);
    }

    [TestMethod]
    public void Sanitize_FullTab_TrimAfterReplacement()
    {
        var s = new AppSettings { FullTab = "\ud800  memory\n\udc00" };
        s.Sanitize();
        // oracle: section 1 FullTab: replacement is before trimming.
        Assert.AreEqual("memory", s.FullTab);
    }

    [TestMethod]
    public void Sanitize_FullTab_UnicodeWhitespace()
    {
        var s = new AppSettings { FullTab = "\u2003memory\u2003" };
        s.Sanitize();
        // oracle: section 1 FullTab: Trim removes Unicode whitespace.
        Assert.AreEqual("memory", s.FullTab);
    }

    [TestMethod]
    public void Sanitize_FullTab_InteriorWhitespace()
    {
        var s = new AppSettings { FullTab = " disk: / data " };
        s.Sanitize();
        // oracle: section 1 FullTab: interior whitespace is preserved.
        Assert.AreEqual("disk: / data", s.FullTab);
    }

    [TestMethod]
    public void Sanitize_IsIdempotent()
    {
        var s = new AppSettings { Theme = " DARK ", MiniW = 50, PosX = double.NaN, FullTab = "\ud800" + new string('x', 199) + "\ud83d\ude00" };
        s.Sanitize();
        var before = s.ToJson();
        s.Sanitize();
        // oracle: section 1 Sanitize is idempotent.
        Assert.AreEqual(before, s.ToJson());
    }

    [TestMethod]
    public void Parse_Null_Defaults()
    {
        var s = AppSettings.Parse(null);
        // oracle: section 1 null/blank/invalid/non-object JSON yields all defaults.
        AssertDefaults(s);
    }

    [TestMethod]
    public void Parse_Empty_Defaults()
    {
        var s = AppSettings.Parse("");
        // oracle: section 1 null/blank/invalid/non-object JSON yields all defaults.
        AssertDefaults(s);
    }

    [TestMethod]
    public void Parse_Whitespace_Defaults()
    {
        var s = AppSettings.Parse(" \r\n\t ");
        // oracle: section 1 null/blank/invalid/non-object JSON yields all defaults.
        AssertDefaults(s);
    }

    [TestMethod]
    public void Parse_Invalid_Defaults()
    {
        var s = AppSettings.Parse("{broken");
        // oracle: section 1 null/blank/invalid/non-object JSON yields all defaults.
        AssertDefaults(s);
    }

    [TestMethod]
    public void Parse_Array_Defaults()
    {
        var s = AppSettings.Parse("[]");
        // oracle: section 1 null/blank/invalid/non-object JSON yields all defaults.
        AssertDefaults(s);
    }

    [TestMethod]
    public void Parse_String_Defaults()
    {
        var s = AppSettings.Parse("\"hello\"");
        // oracle: section 1 null/blank/invalid/non-object JSON yields all defaults.
        AssertDefaults(s);
    }

    [TestMethod]
    public void Parse_Number_Defaults()
    {
        var s = AppSettings.Parse("42");
        // oracle: section 1 null/blank/invalid/non-object JSON yields all defaults.
        AssertDefaults(s);
    }

    [TestMethod]
    public void Parse_Boolean_Defaults()
    {
        var s = AppSettings.Parse("true");
        // oracle: section 1 null/blank/invalid/non-object JSON yields all defaults.
        AssertDefaults(s);
    }

    [TestMethod]
    public void Parse_JsonNull_Defaults()
    {
        var s = AppSettings.Parse("null");
        // oracle: section 1 null/blank/invalid/non-object JSON yields all defaults.
        AssertDefaults(s);
    }

    [TestMethod]
    public void Parse_NestedRootArray_Defaults()
    {
        var s = AppSettings.Parse("[{\"theme\":\"dark\"}]");
        // oracle: section 1 null/blank/invalid/non-object JSON yields all defaults.
        AssertDefaults(s);
    }

    [TestMethod]
    public void Parse_UnknownNestedPropertiesAreIgnored()
    {
        var s = AppSettings.Parse("{\"unknown\":{\"theme\":\"dark\"},\"extra\":[1,2],\"speed\":\"fast\"}");
        // oracle: section 1 unknown properties are ignored, including nested content.
        Assert.AreEqual("light", s.Theme);
        Assert.AreEqual("fast", s.Speed);
    }

    [TestMethod]
    public void Parse_Theme_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"theme\":23,\"speed\":\"fast\"}", "{\"theme\":true,\"speed\":\"fast\"}", "{\"theme\":[],\"speed\":\"fast\"}", "{\"theme\":{},\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual("light", s.Theme);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_Theme_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"theme\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual("light", s.Theme);
    }

    [TestMethod]
    public void Parse_Theme_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"theme\":\"dark\",\"theme\":null,\"theme\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual("dark", s.Theme);
    }

    [TestMethod]
    public void Parse_Theme_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"THEME\":\"dark\"}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual("light", s.Theme);
    }

    [TestMethod]
    public void ValueEquals_Theme_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { Theme = "dark" };
        // oracle: section 1 ValueEquals includes Theme.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_Speed_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"speed\":23,\"theme\":\"dark\"}", "{\"speed\":true,\"theme\":\"dark\"}", "{\"speed\":[],\"theme\":\"dark\"}", "{\"speed\":{},\"theme\":\"dark\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual("balanced", s.Speed);
            Assert.AreEqual("dark", s.Theme);
        }
    }

    [TestMethod]
    public void Parse_Speed_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"speed\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual("balanced", s.Speed);
    }

    [TestMethod]
    public void Parse_Speed_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"speed\":\"fast\",\"speed\":null,\"speed\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual("fast", s.Speed);
    }

    [TestMethod]
    public void Parse_Speed_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"SPEED\":\"fast\"}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual("balanced", s.Speed);
    }

    [TestMethod]
    public void ValueEquals_Speed_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { Speed = "fast" };
        // oracle: section 1 ValueEquals includes Speed.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_Mode_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"mode\":23,\"speed\":\"fast\"}", "{\"mode\":true,\"speed\":\"fast\"}", "{\"mode\":[],\"speed\":\"fast\"}", "{\"mode\":{},\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual("mini", s.Mode);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_Mode_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"mode\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual("mini", s.Mode);
    }

    [TestMethod]
    public void Parse_Mode_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"mode\":\"full\",\"mode\":null,\"mode\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual("full", s.Mode);
    }

    [TestMethod]
    public void Parse_Mode_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"MODE\":\"full\"}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual("mini", s.Mode);
    }

    [TestMethod]
    public void ValueEquals_Mode_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { Mode = "full" };
        // oracle: section 1 ValueEquals includes Mode.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_AlwaysOnTop_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"always_on_top\":23,\"speed\":\"fast\"}", "{\"always_on_top\":\"false\",\"speed\":\"fast\"}", "{\"always_on_top\":[],\"speed\":\"fast\"}", "{\"always_on_top\":{},\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual(true, s.AlwaysOnTop);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_AlwaysOnTop_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"always_on_top\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual(true, s.AlwaysOnTop);
    }

    [TestMethod]
    public void Parse_AlwaysOnTop_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"always_on_top\":false,\"always_on_top\":null,\"always_on_top\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual(false, s.AlwaysOnTop);
    }

    [TestMethod]
    public void Parse_AlwaysOnTop_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"ALWAYS_ON_TOP\":false}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual(true, s.AlwaysOnTop);
    }

    [TestMethod]
    public void ValueEquals_AlwaysOnTop_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { AlwaysOnTop = false };
        // oracle: section 1 ValueEquals includes AlwaysOnTop.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_ShowCpu_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"show_cpu\":23,\"speed\":\"fast\"}", "{\"show_cpu\":\"false\",\"speed\":\"fast\"}", "{\"show_cpu\":[],\"speed\":\"fast\"}", "{\"show_cpu\":{},\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual(true, s.ShowCpu);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_ShowCpu_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"show_cpu\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual(true, s.ShowCpu);
    }

    [TestMethod]
    public void Parse_ShowCpu_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"show_cpu\":false,\"show_cpu\":null,\"show_cpu\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual(false, s.ShowCpu);
    }

    [TestMethod]
    public void Parse_ShowCpu_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"SHOW_CPU\":false}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual(true, s.ShowCpu);
    }

    [TestMethod]
    public void ValueEquals_ShowCpu_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { ShowCpu = false };
        // oracle: section 1 ValueEquals includes ShowCpu.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_ShowRam_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"show_ram\":23,\"speed\":\"fast\"}", "{\"show_ram\":\"false\",\"speed\":\"fast\"}", "{\"show_ram\":[],\"speed\":\"fast\"}", "{\"show_ram\":{},\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual(true, s.ShowRam);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_ShowRam_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"show_ram\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual(true, s.ShowRam);
    }

    [TestMethod]
    public void Parse_ShowRam_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"show_ram\":false,\"show_ram\":null,\"show_ram\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual(false, s.ShowRam);
    }

    [TestMethod]
    public void Parse_ShowRam_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"SHOW_RAM\":false}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual(true, s.ShowRam);
    }

    [TestMethod]
    public void ValueEquals_ShowRam_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { ShowRam = false };
        // oracle: section 1 ValueEquals includes ShowRam.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_ShowDisk_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"show_disk\":23,\"speed\":\"fast\"}", "{\"show_disk\":\"false\",\"speed\":\"fast\"}", "{\"show_disk\":[],\"speed\":\"fast\"}", "{\"show_disk\":{},\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual(true, s.ShowDisk);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_ShowDisk_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"show_disk\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual(true, s.ShowDisk);
    }

    [TestMethod]
    public void Parse_ShowDisk_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"show_disk\":false,\"show_disk\":null,\"show_disk\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual(false, s.ShowDisk);
    }

    [TestMethod]
    public void Parse_ShowDisk_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"SHOW_DISK\":false}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual(true, s.ShowDisk);
    }

    [TestMethod]
    public void ValueEquals_ShowDisk_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { ShowDisk = false };
        // oracle: section 1 ValueEquals includes ShowDisk.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_ShowNetwork_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"show_network\":23,\"speed\":\"fast\"}", "{\"show_network\":\"false\",\"speed\":\"fast\"}", "{\"show_network\":[],\"speed\":\"fast\"}", "{\"show_network\":{},\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual(true, s.ShowNetwork);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_ShowNetwork_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"show_network\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual(true, s.ShowNetwork);
    }

    [TestMethod]
    public void Parse_ShowNetwork_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"show_network\":false,\"show_network\":null,\"show_network\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual(false, s.ShowNetwork);
    }

    [TestMethod]
    public void Parse_ShowNetwork_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"SHOW_NETWORK\":false}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual(true, s.ShowNetwork);
    }

    [TestMethod]
    public void ValueEquals_ShowNetwork_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { ShowNetwork = false };
        // oracle: section 1 ValueEquals includes ShowNetwork.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_Opacity_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"opacity\":\"123\",\"speed\":\"fast\"}", "{\"opacity\":true,\"speed\":\"fast\"}", "{\"opacity\":[],\"speed\":\"fast\"}", "{\"opacity\":{},\"speed\":\"fast\"}", "{\"opacity\":1e400,\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual(0.92, s.Opacity);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_Opacity_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"opacity\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual(0.92, s.Opacity);
    }

    [TestMethod]
    public void Parse_Opacity_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"opacity\":0.75,\"opacity\":null,\"opacity\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual(0.75, s.Opacity);
    }

    [TestMethod]
    public void Parse_Opacity_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"OPACITY\":0.75}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual(0.92, s.Opacity);
    }

    [TestMethod]
    public void ValueEquals_Opacity_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { Opacity = 0.75 };
        // oracle: section 1 ValueEquals includes Opacity.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_FontScale_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"font_scale\":\"123\",\"speed\":\"fast\"}", "{\"font_scale\":true,\"speed\":\"fast\"}", "{\"font_scale\":[],\"speed\":\"fast\"}", "{\"font_scale\":{},\"speed\":\"fast\"}", "{\"font_scale\":1e400,\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual(1.0, s.FontScale);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_FontScale_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"font_scale\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual(1.0, s.FontScale);
    }

    [TestMethod]
    public void Parse_FontScale_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"font_scale\":1.25,\"font_scale\":null,\"font_scale\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual(1.25, s.FontScale);
    }

    [TestMethod]
    public void Parse_FontScale_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"FONT_SCALE\":1.25}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual(1.0, s.FontScale);
    }

    [TestMethod]
    public void ValueEquals_FontScale_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { FontScale = 1.25 };
        // oracle: section 1 ValueEquals includes FontScale.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_MiniW_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"mini_w\":\"123\",\"speed\":\"fast\"}", "{\"mini_w\":true,\"speed\":\"fast\"}", "{\"mini_w\":[],\"speed\":\"fast\"}", "{\"mini_w\":{},\"speed\":\"fast\"}", "{\"mini_w\":1e400,\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual(270.0, s.MiniW);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_MiniW_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"mini_w\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual(270.0, s.MiniW);
    }

    [TestMethod]
    public void Parse_MiniW_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"mini_w\":333.0,\"mini_w\":null,\"mini_w\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual(333.0, s.MiniW);
    }

    [TestMethod]
    public void Parse_MiniW_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"MINI_W\":333.0}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual(270.0, s.MiniW);
    }

    [TestMethod]
    public void ValueEquals_MiniW_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { MiniW = 333.0 };
        // oracle: section 1 ValueEquals includes MiniW.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_MiniH_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"mini_h\":\"123\",\"speed\":\"fast\"}", "{\"mini_h\":true,\"speed\":\"fast\"}", "{\"mini_h\":[],\"speed\":\"fast\"}", "{\"mini_h\":{},\"speed\":\"fast\"}", "{\"mini_h\":1e400,\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual(104.0, s.MiniH);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_MiniH_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"mini_h\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual(104.0, s.MiniH);
    }

    [TestMethod]
    public void Parse_MiniH_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"mini_h\":222.0,\"mini_h\":null,\"mini_h\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual(222.0, s.MiniH);
    }

    [TestMethod]
    public void Parse_MiniH_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"MINI_H\":222.0}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual(104.0, s.MiniH);
    }

    [TestMethod]
    public void ValueEquals_MiniH_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { MiniH = 222.0 };
        // oracle: section 1 ValueEquals includes MiniH.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_ExpW_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"exp_w\":\"123\",\"speed\":\"fast\"}", "{\"exp_w\":true,\"speed\":\"fast\"}", "{\"exp_w\":[],\"speed\":\"fast\"}", "{\"exp_w\":{},\"speed\":\"fast\"}", "{\"exp_w\":1e400,\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual(630.0, s.ExpW);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_ExpW_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"exp_w\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual(630.0, s.ExpW);
    }

    [TestMethod]
    public void Parse_ExpW_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"exp_w\":777.0,\"exp_w\":null,\"exp_w\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual(777.0, s.ExpW);
    }

    [TestMethod]
    public void Parse_ExpW_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"EXP_W\":777.0}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual(630.0, s.ExpW);
    }

    [TestMethod]
    public void ValueEquals_ExpW_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { ExpW = 777.0 };
        // oracle: section 1 ValueEquals includes ExpW.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_ExpH_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"exp_h\":\"123\",\"speed\":\"fast\"}", "{\"exp_h\":true,\"speed\":\"fast\"}", "{\"exp_h\":[],\"speed\":\"fast\"}", "{\"exp_h\":{},\"speed\":\"fast\"}", "{\"exp_h\":1e400,\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual(480.0, s.ExpH);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_ExpH_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"exp_h\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual(480.0, s.ExpH);
    }

    [TestMethod]
    public void Parse_ExpH_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"exp_h\":555.0,\"exp_h\":null,\"exp_h\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual(555.0, s.ExpH);
    }

    [TestMethod]
    public void Parse_ExpH_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"EXP_H\":555.0}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual(480.0, s.ExpH);
    }

    [TestMethod]
    public void ValueEquals_ExpH_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { ExpH = 555.0 };
        // oracle: section 1 ValueEquals includes ExpH.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_FullW_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"full_w\":\"123\",\"speed\":\"fast\"}", "{\"full_w\":true,\"speed\":\"fast\"}", "{\"full_w\":[],\"speed\":\"fast\"}", "{\"full_w\":{},\"speed\":\"fast\"}", "{\"full_w\":1e400,\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual(0.0, s.FullW);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_FullW_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"full_w\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual(0.0, s.FullW);
    }

    [TestMethod]
    public void Parse_FullW_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"full_w\":1280.0,\"full_w\":null,\"full_w\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual(1280.0, s.FullW);
    }

    [TestMethod]
    public void Parse_FullW_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"FULL_W\":1280.0}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual(0.0, s.FullW);
    }

    [TestMethod]
    public void ValueEquals_FullW_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { FullW = 1280.0 };
        // oracle: section 1 ValueEquals includes FullW.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_FullH_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"full_h\":\"123\",\"speed\":\"fast\"}", "{\"full_h\":true,\"speed\":\"fast\"}", "{\"full_h\":[],\"speed\":\"fast\"}", "{\"full_h\":{},\"speed\":\"fast\"}", "{\"full_h\":1e400,\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual(0.0, s.FullH);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_FullH_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"full_h\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual(0.0, s.FullH);
    }

    [TestMethod]
    public void Parse_FullH_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"full_h\":900.0,\"full_h\":null,\"full_h\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual(900.0, s.FullH);
    }

    [TestMethod]
    public void Parse_FullH_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"FULL_H\":900.0}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual(0.0, s.FullH);
    }

    [TestMethod]
    public void ValueEquals_FullH_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { FullH = 900.0 };
        // oracle: section 1 ValueEquals includes FullH.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_PosX_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"pos_x\":\"123\",\"speed\":\"fast\"}", "{\"pos_x\":true,\"speed\":\"fast\"}", "{\"pos_x\":[],\"speed\":\"fast\"}", "{\"pos_x\":{},\"speed\":\"fast\"}", "{\"pos_x\":1e400,\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.IsNull(s.PosX);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_PosX_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"pos_x\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.IsNull(s.PosX);
    }

    [TestMethod]
    public void Parse_PosX_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"pos_x\":123.0,\"pos_x\":null,\"pos_x\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.IsNull(s.PosX);
    }

    [TestMethod]
    public void Parse_PosX_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"POS_X\":123.0}");
        // oracle: section 1 property names match case-sensitively.
        Assert.IsNull(s.PosX);
    }

    [TestMethod]
    public void ValueEquals_PosX_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { PosX = 123.0 };
        // oracle: section 1 ValueEquals includes PosX.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_PosY_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"pos_y\":\"123\",\"speed\":\"fast\"}", "{\"pos_y\":true,\"speed\":\"fast\"}", "{\"pos_y\":[],\"speed\":\"fast\"}", "{\"pos_y\":{},\"speed\":\"fast\"}", "{\"pos_y\":1e400,\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.IsNull(s.PosY);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_PosY_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"pos_y\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.IsNull(s.PosY);
    }

    [TestMethod]
    public void Parse_PosY_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"pos_y\":-456.0,\"pos_y\":null,\"pos_y\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.IsNull(s.PosY);
    }

    [TestMethod]
    public void Parse_PosY_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"POS_Y\":-456.0}");
        // oracle: section 1 property names match case-sensitively.
        Assert.IsNull(s.PosY);
    }

    [TestMethod]
    public void ValueEquals_PosY_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { PosY = -456.0 };
        // oracle: section 1 ValueEquals includes PosY.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_FullTab_WrongTypesDoNotBlockOtherProperties()
    {
        foreach (var json in new[] { "{\"full_tab\":23,\"speed\":\"fast\"}", "{\"full_tab\":true,\"speed\":\"fast\"}", "{\"full_tab\":[],\"speed\":\"fast\"}", "{\"full_tab\":{},\"speed\":\"fast\"}" })
        {
            var s = AppSettings.Parse(json);
            // oracle: section 1 wrong types default only this property; the valid sibling is read.
            Assert.AreEqual("", s.FullTab);
            Assert.AreEqual("fast", s.Speed);
        }
    }

    [TestMethod]
    public void Parse_FullTab_JsonNullDefaults()
    {
        var s = AppSettings.Parse("{\"full_tab\":null}");
        // oracle: section 1 JSON null is absent/default, positions become null.
        Assert.AreEqual("", s.FullTab);
    }

    [TestMethod]
    public void Parse_FullTab_DuplicateInvalidAndNullFollowValid()
    {
        var s = AppSettings.Parse("{\"full_tab\":\"memory\",\"full_tab\":null,\"full_tab\":{}}");
        // oracle: M04 last VALID occurrence wins; wrong type and null ignored except position null.
        Assert.AreEqual("memory", s.FullTab);
    }

    [TestMethod]
    public void Parse_FullTab_NamesAreCaseSensitive()
    {
        var s = AppSettings.Parse("{\"FULL_TAB\":\"memory\"}");
        // oracle: section 1 property names match case-sensitively.
        Assert.AreEqual("", s.FullTab);
    }

    [TestMethod]
    public void ValueEquals_FullTab_DifferenceIsDetected()
    {
        var a = new AppSettings();
        var b = new AppSettings { FullTab = "memory" };
        // oracle: section 1 ValueEquals includes FullTab.
        Assert.IsFalse(a.ValueEquals(b));
        Assert.IsFalse(b.ValueEquals(a));
    }

    [TestMethod]
    public void Parse_DuplicateValidLastWins()
    {
        var s = AppSettings.Parse("{\"theme\":\"dark\",\"theme\":\"light\",\"opacity\":0.4,\"opacity\":0.8}");
        // oracle: M04 last valid occurrence wins.
        Assert.AreEqual("light", s.Theme);
        Assert.AreEqual(0.8, s.Opacity);
    }

    [TestMethod]
    public void Parse_CallsSanitize()
    {
        var s = AppSettings.Parse("{\"theme\":\" DARK \",\"mini_w\":50,\"opacity\":2,\"full_tab\":\" x\\ny \"}");
        // oracle: section 1 Parse sanitises enumerations, numbers and FullTab.
        Assert.AreEqual("dark", s.Theme);
        Assert.AreEqual(100.0, s.MiniW);
        Assert.AreEqual(1.0, s.Opacity);
        Assert.AreEqual("x y", s.FullTab);
    }

    [TestMethod]
    public void RoundTrip_AllPropertiesNonDefault()
    {
        var s = new AppSettings
        {
            Theme = "dark",
            Speed = "fast",
            Mode = "full",
            AlwaysOnTop = false,
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = false,
            ShowNetwork = false,
            Opacity = 0.75,
            FontScale = 1.25,
            MiniW = 333.0,
            MiniH = 222.0,
            ExpW = 777.0,
            ExpH = 555.0,
            FullW = 1280.0,
            FullH = 900.0,
            PosX = 123.0,
            PosY = -456.0,
            FullTab = "memory"
        };
        s.Sanitize();
        var copy = AppSettings.Parse(s.ToJson());
        // oracle: H04 sanitised settings round trip with equality of all nineteen properties.
        Assert.IsTrue(s.ValueEquals(copy));
        Assert.IsTrue(copy.ValueEquals(s));
    }

    [TestMethod]
    public void RoundTrip_UnsetPositionsAndFullSize()
    {
        var s = new AppSettings();
        using var doc = JsonDocument.Parse(s.ToJson());
        var r = doc.RootElement;
        var copy = AppSettings.Parse(s.ToJson());
        // oracle: section 1 unset positions serialize as null and unset full dimensions as zero.
        Assert.AreEqual(JsonValueKind.Null, r.GetProperty("pos_x").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, r.GetProperty("pos_y").ValueKind);
        Assert.AreEqual(0.0, r.GetProperty("full_w").GetDouble());
        Assert.AreEqual(0.0, r.GetProperty("full_h").GetDouble());
        Assert.IsTrue(s.ValueEquals(copy));
    }

    [TestMethod]
    public void ToJson_IndentedSnakeCaseNames()
    {
        var text = new AppSettings().ToJson();
        using var doc = JsonDocument.Parse(text);
        // oracle: section 1 ToJson is indented and contains exactly the nineteen snake_case properties.
        Assert.IsTrue(text.Contains("\n"));
        CollectionAssert.AreEquivalent(new[] { "theme", "speed", "mode", "always_on_top", "show_cpu", "show_ram", "show_disk", "show_network", "opacity", "font_scale", "mini_w", "mini_h", "exp_w", "exp_h", "full_w", "full_h", "pos_x", "pos_y", "full_tab" }, doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [TestMethod]
    public void ToJson_LoneSurrogatesNeverThrow()
    {
        var text = new AppSettings { FullTab = "a\ud800b\udc00c" }.ToJson();
        using var doc = JsonDocument.Parse(text);
        // oracle: S12 ToJson produces valid JSON with no raw lone surrogate; U+FFFD substitution is allowed and no particular escape is required.
        Assert.AreEqual(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.AreEqual(JsonValueKind.String, doc.RootElement.GetProperty("full_tab").ValueKind);
        Assert.IsFalse(text.Contains("\ud800"));
        Assert.IsFalse(text.Contains("\udc00"));
    }

    [TestMethod]
    public void ToJson_NonFiniteNumbersWrittenAsZero()
    {
        var s = new AppSettings { Opacity = double.NaN, FontScale = double.PositiveInfinity, MiniW = double.NegativeInfinity, MiniH = double.NaN, ExpW = double.NaN, ExpH = double.PositiveInfinity, FullW = double.NaN, FullH = double.PositiveInfinity, PosX = double.NaN, PosY = double.NegativeInfinity };
        using var doc = JsonDocument.Parse(s.ToJson());
        // oracle: H04 ToJson writes zero for non-finite numbers without throwing.
        foreach (var name in new[] { "opacity", "font_scale", "mini_w", "mini_h", "exp_w", "exp_h", "full_w", "full_h", "pos_x", "pos_y" })
        {
            // oracle: H04 non-finite numeric serialization is zero.
            Assert.AreEqual(0.0, doc.RootElement.GetProperty(name).GetDouble());
        }
    }

    [TestMethod]
    public void ValueEquals_NullIsFalse()
    {
        var s = new AppSettings();
        // oracle: section 1 ValueEquals(null) is false.
        Assert.IsFalse(s.ValueEquals(null));
    }

    [TestMethod]
    public void ValueEquals_EqualSeparateInstancesAndBothNullPositions()
    {
        var a = new AppSettings();
        var b = new AppSettings();
        // oracle: section 1 equality is by values, including both nullable positions being null.
        Assert.IsTrue(a.ValueEquals(b));
        Assert.IsTrue(a.ValueEquals(a));
    }

    [TestMethod]
    public void ValueEquals_DoubleNaNUsesOperatorEquality()
    {
        var a = new AppSettings { Opacity = double.NaN };
        var b = new AppSettings { Opacity = double.NaN };
        // oracle: section 1 doubles use ==; NaN == NaN is false.
        Assert.IsFalse(a.ValueEquals(b));
    }

    [TestMethod]
    public void IntervalsMillis_Eco()
    {
        var s = new AppSettings { Speed = "eco" };
        // oracle: section 1 intervals use Speed as-is; unknown falls back to balanced.
        Assert.AreEqual((5000L, 30000L, 60000L), s.IntervalsMillis);
    }

    [TestMethod]
    public void IntervalsMillis_Balanced()
    {
        var s = new AppSettings { Speed = "balanced" };
        // oracle: section 1 intervals use Speed as-is; unknown falls back to balanced.
        Assert.AreEqual((2500L, 15000L, 30000L), s.IntervalsMillis);
    }

    [TestMethod]
    public void IntervalsMillis_Fast()
    {
        var s = new AppSettings { Speed = "fast" };
        // oracle: section 1 intervals use Speed as-is; unknown falls back to balanced.
        Assert.AreEqual((1000L, 10000L, 30000L), s.IntervalsMillis);
    }

    [TestMethod]
    public void IntervalsMillis_Unknown()
    {
        var s = new AppSettings { Speed = "unknown" };
        // oracle: section 1 intervals use Speed as-is; unknown falls back to balanced.
        Assert.AreEqual((2500L, 15000L, 30000L), s.IntervalsMillis);
    }

    [TestMethod]
    public void IntervalsMillis_Null()
    {
        var s = new AppSettings { Speed = null! };
        // oracle: section 1 intervals use Speed as-is; unknown falls back to balanced.
        Assert.AreEqual((2500L, 15000L, 30000L), s.IntervalsMillis);
    }

    [TestMethod]
    public void IntervalsMillis_NotNormalized()
    {
        var s = new AppSettings { Speed = " FAST " };
        // oracle: section 1 intervals use Speed as-is; unknown falls back to balanced.
        Assert.AreEqual((2500L, 15000L, 30000L), s.IntervalsMillis);
    }

    [TestMethod]
    public void ModeValue_Mini()
    {
        var s = new AppSettings { Mode = "mini" };
        // oracle: section 1 ModeValue recognises only exact expanded/full, else Mini.
        Assert.AreEqual(ViewMode.Mini, s.ModeValue);
    }

    [TestMethod]
    public void ModeValue_Expanded()
    {
        var s = new AppSettings { Mode = "expanded" };
        // oracle: section 1 ModeValue recognises only exact expanded/full, else Mini.
        Assert.AreEqual(ViewMode.Expanded, s.ModeValue);
    }

    [TestMethod]
    public void ModeValue_Full()
    {
        var s = new AppSettings { Mode = "full" };
        // oracle: section 1 ModeValue recognises only exact expanded/full, else Mini.
        Assert.AreEqual(ViewMode.Full, s.ModeValue);
    }

    [TestMethod]
    public void ModeValue_Null()
    {
        var s = new AppSettings { Mode = null! };
        // oracle: section 1 ModeValue recognises only exact expanded/full, else Mini.
        Assert.AreEqual(ViewMode.Mini, s.ModeValue);
    }

    [TestMethod]
    public void ModeValue_Unknown()
    {
        var s = new AppSettings { Mode = "unknown" };
        // oracle: section 1 ModeValue recognises only exact expanded/full, else Mini.
        Assert.AreEqual(ViewMode.Mini, s.ModeValue);
    }

    [TestMethod]
    public void ModeValue_NotNormalized()
    {
        var s = new AppSettings { Mode = " FULL " };
        // oracle: section 1 ModeValue recognises only exact expanded/full, else Mini.
        Assert.AreEqual(ViewMode.Mini, s.ModeValue);
    }

    [TestMethod]
    public void Sanitize_FalseBooleanFlagsArePreserved()
    {
        var s = new AppSettings { AlwaysOnTop = false, ShowCpu = false, ShowRam = false, ShowDisk = false, ShowNetwork = false };
        s.Sanitize();
        // oracle: section 1 boolean values have no sanitisation; false remains false.
        Assert.IsFalse(s.AlwaysOnTop);
        Assert.IsFalse(s.ShowCpu);
        Assert.IsFalse(s.ShowRam);
        Assert.IsFalse(s.ShowDisk);
        Assert.IsFalse(s.ShowNetwork);
    }

    [TestMethod]
    public void Sanitize_FullTab_AllControlRanges()
    {
        var controls = new string(Enumerable.Range(0, 32).Concat(Enumerable.Range(127, 33)).Select(i => (char)i).ToArray());
        var s = new AppSettings { FullTab = "a" + controls + "b" };
        s.Sanitize();
        // oracle: section 1 char.IsControl covers U+0000..001F (32) and U+007F..009F (33), giving 65 interior spaces.
        Assert.AreEqual("a" + new string(' ', 65) + "b", s.FullTab);
    }

    [TestMethod]
    public void Sanitize_FullTab_AdjacentSurrogates()
    {
        var s = new AppSettings { FullTab = "x\ud800\ud83d\ude00\udc00y" };
        s.Sanitize();
        // oracle: section 1 replace the first lone high and last lone low, retaining the middle valid pair.
        Assert.AreEqual("x \ud83d\ude00 y", s.FullTab);
    }

    [TestMethod]
    public void RoundTrip_SanitizedUnicodeTabAndPositions()
    {
        var s = new AppSettings { FullTab = "  disk:/\u03b1\ud83d\ude00  ", PosX = -100000, PosY = 100000 };
        s.Sanitize();
        var copy = AppSettings.Parse(s.ToJson());
        // oracle: H04 sanitised Unicode including valid pairs round trips; position endpoints are retained.
        Assert.AreEqual("disk:/\u03b1\ud83d\ude00", copy.FullTab);
        Assert.AreEqual((double?)-100000, copy.PosX);
        Assert.AreEqual((double?)100000, copy.PosY);
        Assert.IsTrue(s.ValueEquals(copy));
    }

    [TestMethod]
    public void Parse_PosX_WrongDuplicateKeepsEarlierValid()
    {
        var s = AppSettings.Parse("{\"pos_x\":123,\"pos_x\":[]}");
        // oracle: M04 a wrong-type duplicate is ignored even for nullable positions.
        Assert.AreEqual((double?)123, s.PosX);
    }

    [TestMethod]
    public void Parse_PosY_WrongDuplicateKeepsEarlierValid()
    {
        var s = AppSettings.Parse("{\"pos_y\":-456,\"pos_y\":[]}");
        // oracle: M04 a wrong-type duplicate is ignored even for nullable positions.
        Assert.AreEqual((double?)-456, s.PosY);
    }

    [TestMethod]
    public void Parse_OverflowDuplicateKeepsEarlierFinite()
    {
        var s = AppSettings.Parse("{\"opacity\":0.7,\"opacity\":1e400,\"font_scale\":1.25,\"font_scale\":-1e400}");
        // oracle: M04 invalid overflow occurrences are ignored; earlier valid numeric values survive.
        Assert.AreEqual(0.7, s.Opacity);
        Assert.AreEqual(1.25, s.FontScale);
    }

    [TestMethod]
    public void Parse_ValidValueAfterNullAndWrongTypesWins()
    {
        var s = AppSettings.Parse("{\"theme\":null,\"theme\":[],\"theme\":\"dark\",\"pos_x\":null,\"pos_x\":{},\"pos_x\":25}");
        // oracle: M04 later valid values supersede earlier absent or invalid occurrences.
        Assert.AreEqual("dark", s.Theme);
        Assert.AreEqual((double?)25, s.PosX);
    }

    [TestMethod]
    public void Parse_KnownFieldsNestedDoNotLeakToRoot()
    {
        var s = AppSettings.Parse("{\"theme\":{\"speed\":\"fast\"},\"speed\":[{\"mode\":\"full\"}],\"mode\":\"expanded\"}");
        // oracle: section 1 wrong nested field types default independently and nested known names are not root properties.
        Assert.AreEqual("light", s.Theme);
        Assert.AreEqual("balanced", s.Speed);
        Assert.AreEqual("expanded", s.Mode);
    }

    [TestMethod]
    public void ValueEquals_NullStringsUseValueEquality()
    {
        var a = new AppSettings { Theme = null!, Speed = null!, Mode = null!, FullTab = null! };
        var b = new AppSettings { Theme = null!, Speed = null!, Mode = null!, FullTab = null! };
        // oracle: section 1 all properties compare their values; both null strings compare equal without sanitising.
        Assert.IsTrue(a.ValueEquals(b));
        Assert.IsFalse(a.ValueEquals(new AppSettings()));
    }

}
