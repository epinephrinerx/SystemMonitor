using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class PaletteTests
{
    [TestMethod]
    public void Palette_ForTheme_ReturnsExpectedTokens()
    {
        Palette light = Palette.For("light");
        Assert.AreEqual(Colour.FromHex("#ffffff"), light.Panel);
        Assert.AreEqual(Colour.FromHex("#f3f5f9"), light.Sidebar);
        Assert.AreEqual(Colour.FromHex("#1e293b"), light.Text);

        Palette lightUpper = Palette.For("LIGHT");
        Assert.AreSame(Palette.Light, lightUpper);

        Palette dark = Palette.For("dark");
        Assert.AreEqual(Colour.FromHex("#0f172a"), dark.Panel);
        Assert.AreEqual(Colour.FromHex("#0b1220"), dark.Sidebar);
        Assert.AreEqual(Colour.FromHex("#ffffff"), dark.Text);

        // Fallback for null or unknown theme is Dark
        Assert.AreSame(Palette.Dark, Palette.For(null));
        Assert.AreSame(Palette.Dark, Palette.For("custom_theme"));
    }

    [TestMethod]
    public void LoadColour_FollowsSpecThresholds()
    {
        Colour cpuAccent = Accents.Cpu;

        // Below 70 uses module accent
        Assert.AreEqual(cpuAccent, Palette.LoadColour(0, cpuAccent));
        Assert.AreEqual(cpuAccent, Palette.LoadColour(50, cpuAccent));
        Assert.AreEqual(cpuAccent, Palette.LoadColour(69, cpuAccent));

        // 70 to 89 uses amber (#eab308)
        Colour warm = Colour.FromHex("#eab308");
        Assert.AreEqual(warm, Palette.LoadColour(70, cpuAccent));
        Assert.AreEqual(warm, Palette.LoadColour(75, cpuAccent));
        Assert.AreEqual(warm, Palette.LoadColour(89, cpuAccent));

        // 90 and above uses red (#ef4444)
        Colour hot = Colour.FromHex("#ef4444");
        Assert.AreEqual(hot, Palette.LoadColour(90, cpuAccent));
        Assert.AreEqual(hot, Palette.LoadColour(95, cpuAccent));
        Assert.AreEqual(hot, Palette.LoadColour(100, cpuAccent));
    }

    [TestMethod]
    public void Badge_NullTemperature_ReturnsNaBadge()
    {
        Palette palette = Palette.Light;
        TempBadge badge = palette.Badge(null);

        Assert.AreEqual(palette.Label, badge.Fore);
        Assert.AreEqual(palette.Tag, badge.Back);
        Assert.AreEqual("n/a", badge.Text);
        Assert.IsFalse(badge.Hot);
        Assert.IsFalse(badge.Estimated);
    }

    [TestMethod]
    public void Badge_TemperatureRanges_FollowSpecThresholds()
    {
        Palette palette = Palette.Light;

        // < 65: Green (#22c55e), BadgeOk
        TempBadge cool = palette.Badge(50);
        Assert.AreEqual(Colour.FromHex("#22c55e"), cool.Fore);
        Assert.AreEqual(palette.BadgeOk, cool.Back);
        Assert.AreEqual("50°C", cool.Text);
        Assert.IsFalse(cool.Hot);
        Assert.IsFalse(cool.Estimated);

        // Boundary: 64 is cool
        Assert.AreEqual(Colour.FromHex("#22c55e"), palette.Badge(64).Fore);

        // 65 to 79: Amber (#eab308), BadgeWarm
        TempBadge warm = palette.Badge(65);
        Assert.AreEqual(Colour.FromHex("#eab308"), warm.Fore);
        Assert.AreEqual(palette.BadgeWarm, warm.Back);
        Assert.AreEqual("65°C", warm.Text);
        Assert.IsFalse(warm.Hot);

        // Boundary: 79 is warm
        Assert.AreEqual(Colour.FromHex("#eab308"), palette.Badge(79).Fore);

        // >= 80: Red (#ef4444), BadgeHot, Hot = true
        TempBadge hot = palette.Badge(80);
        Assert.AreEqual(Colour.FromHex("#ef4444"), hot.Fore);
        Assert.AreEqual(palette.BadgeHot, hot.Back);
        Assert.AreEqual("80°C", hot.Text);
        Assert.IsTrue(hot.Hot);

        TempBadge veryHot = palette.Badge(95);
        Assert.AreEqual(Colour.FromHex("#ef4444"), veryHot.Fore);
        Assert.IsTrue(veryHot.Hot);
    }

    [TestMethod]
    public void Badge_EstimatedTemperature_PrefixesWithTilde()
    {
        Palette palette = Palette.Dark;
        TempBadge estimated = palette.Badge(52, estimated: true);

        Assert.AreEqual("~52°C", estimated.Text);
        Assert.IsTrue(estimated.Estimated);
        Assert.IsFalse(estimated.Hot);
    }

    [TestMethod]
    public void Accents_MatchSpecHexCodes()
    {
        Assert.AreEqual(Colour.FromHex("#3b82f6"), Accents.Cpu);
        Assert.AreEqual(Colour.FromHex("#a855f7"), Accents.Ram);
        Assert.AreEqual(Colour.FromHex("#10b981"), Accents.Disk);
        Assert.AreEqual(Colour.FromHex("#06b6d4"), Accents.Net);
        Assert.AreEqual(Colour.FromHex("#f59e0b"), Accents.Gpu);
    }

    [TestMethod]
    public void Colour_WithOpacity_ClampsBetween40And255()
    {
        Colour white = Colour.FromHex("#ffffff");

        // 1.0 opacity -> 255 alpha
        Colour full = white.WithOpacity(1.0);
        Assert.AreEqual(255, full.A);

        // 0.35 opacity -> 89 alpha
        Colour min = white.WithOpacity(0.35);
        Assert.AreEqual(89, min.A);

        // Opacity below 0.35 is clamped to 0.35 (alpha floor 40)
        Colour clampedLow = white.WithOpacity(0.1);
        Assert.AreEqual(89, clampedLow.A);

        // Opacity above 1.0 is clamped to 1.0
        Colour clampedHigh = white.WithOpacity(1.5);
        Assert.AreEqual(255, clampedHigh.A);
    }

    [TestMethod]
    public void Colour_FromHex_Parses6And8DigitFormats()
    {
        Colour c6 = Colour.FromHex("#1a2438");
        Assert.AreEqual(0x1a, c6.R);
        Assert.AreEqual(0x24, c6.G);
        Assert.AreEqual(0x38, c6.B);
        Assert.AreEqual(255, c6.A);
        Assert.AreEqual("#1a2438", c6.ToHex());

        Colour c8 = Colour.FromHex("#33808080");
        Assert.AreEqual(0x33, c8.R);
        Assert.AreEqual(0x80, c8.G);
        Assert.AreEqual(0x80, c8.B);
        Assert.AreEqual(0x80, c8.A);
        Assert.AreEqual("#33808080", c8.ToHexWithAlpha());
    }

    [TestMethod]
    public void Colour_Mix_And_IsDarkPlot()
    {
        Colour black = new(0, 0, 0);
        Colour white = new(255, 255, 255);

        Assert.IsTrue(black.IsDarkPlot); // 0 < 384
        Assert.IsFalse(white.IsDarkPlot); // 765 >= 384

        // 50% mix
        Colour gray = black.Mix(white, 0.5);
        Assert.AreEqual(128, gray.R);
        Assert.AreEqual(128, gray.G);
        Assert.AreEqual(128, gray.B);
        Assert.IsFalse(gray.IsDarkPlot); // 384 is not < 384
    }
}
