using SysMonitor.Sensors;

namespace SysMonitor.Tests;

[TestClass]
public class ThermalZoneTests
{
    [TestMethod]
    public void Kelvin_converts_to_celsius()
    {
        // The reading this machine actually reports: 325 K.
        Assert.AreEqual(52, ThermalZone.FromKelvin(325));
        Assert.AreEqual(0, ThermalZone.FromKelvin(273.15));
        Assert.AreEqual(100, ThermalZone.FromKelvin(373.15));
    }

    [TestMethod]
    public void Tenths_of_a_kelvin_survive_the_conversion()
    {
        // HighPrecisionTemperature is tenths, so 3251 is 52.0 C.
        Assert.AreEqual(52, ThermalZone.FromKelvin(3251 / 10.0));
    }

    [TestMethod]
    public void Impossible_readings_are_rejected()
    {
        Assert.IsNull(ThermalZone.FromKelvin(0));        // a zone reporting nothing
        Assert.IsNull(ThermalZone.FromKelvin(100));      // -173 C
        Assert.IsNull(ThermalZone.FromKelvin(500));      // 227 C
    }

    [TestMethod]
    public void No_zones_means_no_reading()
    {
        Assert.IsNull(ThermalZone.Pick(Array.Empty<Zone>()));
    }

    [TestMethod]
    public void A_zone_named_for_the_cpu_wins_over_a_hotter_one()
    {
        var zones = new[]
        {
            new Zone(@"\_TZ.GPUZ", 71),
            new Zone(@"\_TZ.CPUZ", 48),
        };
        Assert.AreEqual(@"\_TZ.CPUZ", ThermalZone.Pick(zones)!.Value.Name);
    }

    [TestMethod]
    public void The_firmwares_first_zone_is_treated_as_the_cpu()
    {
        // TZ00 is the conventional name for it, and the one this machine has.
        var zones = new[]
        {
            new Zone(@"\_TZ.TZ01", 65),
            new Zone(@"\_TZ.TZ00", 52),
        };
        Assert.AreEqual(@"\_TZ.TZ00", ThermalZone.Pick(zones)!.Value.Name);
    }

    [TestMethod]
    public void With_no_recognisable_name_the_hottest_zone_wins()
    {
        var zones = new[]
        {
            new Zone("ZONE_A", 44),
            new Zone("ZONE_B", 67),
            new Zone("ZONE_C", 51),
        };
        Assert.AreEqual("ZONE_B", ThermalZone.Pick(zones)!.Value.Name);
    }

    [TestMethod]
    public void A_measured_reading_is_not_marked_as_an_estimate()
    {
        // The '~' is the whole difference between a sensor and a guess.
        Assert.AreEqual("52°C", Palette.TempText(52, estimated: false));
        Assert.AreEqual("~52°C", Palette.TempText(52, estimated: true));
    }
}
