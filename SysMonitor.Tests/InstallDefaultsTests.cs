namespace SysMonitor.Tests;

/// <summary>
/// What a machine that has never run this app starts with.
///
/// These are one-way decisions in practice: a config file is written the first
/// time anything changes, and from then on the defaults are never consulted
/// again. Getting them wrong is not something an existing user ever sees, and
/// not something a new one can be told about.
/// </summary>
[TestClass]
public class InstallDefaultsTests
{
    private static readonly AppConfig Fresh = new();

    [TestMethod]
    public void The_widget_does_not_sit_above_everything()
    {
        Assert.IsFalse(Fresh.AlwaysOnTop);
    }

    [TestMethod]
    public void It_snaps_to_the_edges()
    {
        Assert.IsTrue(Fresh.Snap);
    }

    [TestMethod]
    public void It_starts_in_light_mode_at_full_strength()
    {
        Assert.AreEqual("light", Fresh.Theme);
        Assert.AreEqual(1.0, Fresh.Opacity);
        Assert.AreEqual(1.0, Fresh.FontScale);
    }

    [TestMethod]
    public void It_speaks_english_until_told_otherwise()
    {
        Assert.AreEqual("en", Fresh.Lang);
    }

    [TestMethod]
    public void Closing_it_puts_it_in_the_tray()
    {
        Assert.AreEqual("tray", Fresh.CloseAction);
    }

    [TestMethod]
    public void Every_section_is_shown_and_broken_out()
    {
        Assert.IsTrue(Fresh.ShowCpu);
        Assert.IsTrue(Fresh.ShowRam);
        Assert.IsTrue(Fresh.ShowDisk);
        Assert.IsTrue(Fresh.ShowGpu);
        Assert.IsTrue(Fresh.ShowNetwork);

        Assert.AreEqual("separated", Fresh.CpuMode);
        Assert.AreEqual("separated", Fresh.DiskMode);
        Assert.AreEqual("separated", Fresh.NetworkMode);

        Assert.IsTrue(Fresh.CpuTemperature, "the real thermal zone is read");
        Assert.IsTrue(Fresh.IncludeWireless);
        Assert.IsTrue(Fresh.IncludeRemovable);
    }
}
