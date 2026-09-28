using SysMonitor.Model;
using SysMonitor.ViewModels;

namespace SysMonitor.Tests;

/// <summary>
/// The CPU section of the overall view.
///
/// It used to put a temperature badge on every core row. The chip has one
/// thermal sensor, so in the modelled mode those sixteen badges were
/// `40 + load * 0.4` applied per core -- the bar beside them converted into
/// degrees -- and in the measured mode they were the one package reading
/// printed sixteen times. Neither was a per-core measurement, so the figure
/// moved to the heading, where one reading for the whole chip belongs.
/// </summary>
[TestClass]
public class OverallCpuTests
{
    private static WidgetViewModel Model(bool separated = true) =>
        new(new AppConfig { Lang = "en", CpuMode = separated ? "separated" : "total" });

    private static Snapshot Sample(int? temp = 58, bool estimated = false) => new()
    {
        Ready = true,
        CpuTotal = 25,
        CpuTemp = temp,
        CpuTempEstimated = estimated,
        Cores = Enumerable.Range(0, 8)
            .Select(i => new Core { Usage = 10 + i * 5, Temp = temp }).ToList(),
        Ram = new Ram { Usage = 60, UsedGb = 19.2, TotalGb = 32 },
    };

    private static Section Cpu(WidgetViewModel model) =>
        model.Sections.First(s => s.Title.Contains("CPU"));

    [TestMethod]
    public void A_core_row_carries_no_temperature_badge()
    {
        WidgetViewModel model = Model();
        model.UpdateOverall(Sample());

        Section cpu = Cpu(model);
        Assert.AreEqual(8, cpu.Rows.Count);
        foreach (MeterRow row in cpu.Rows)
        {
            Assert.IsTrue(row.TempHidden, $"{row.Title} still shows a badge");
        }
    }

    [TestMethod]
    public void The_heading_carries_the_package_temperature()
    {
        WidgetViewModel model = Model();
        model.UpdateOverall(Sample());

        StringAssert.Contains(Cpu(model).Note, "58");
    }

    [TestMethod]
    public void The_heading_carries_it_in_the_combined_mode_too()
    {
        // The combined mode has a total row that could hold it, but the
        // heading is the one place that works in both modes.
        WidgetViewModel model = Model(separated: false);
        model.UpdateOverall(Sample());

        StringAssert.Contains(Cpu(model).Note, "58");
    }

    [TestMethod]
    public void A_modelled_reading_is_still_marked_as_one()
    {
        WidgetViewModel model = Model();
        model.UpdateOverall(Sample(temp: 47, estimated: true));

        StringAssert.Contains(Cpu(model).Note, "~",
            "an estimate must not pass for a measurement");
    }

    [TestMethod]
    public void A_machine_with_no_thermal_zone_says_nothing()
    {
        WidgetViewModel model = Model();
        model.UpdateOverall(Sample(temp: null));

        Assert.AreEqual(string.Empty, Cpu(model).Note);
    }
}
