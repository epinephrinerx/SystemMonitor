using SysMonitor.Model;
using SysMonitor.ViewModels;

namespace SysMonitor.Tests;

/// <summary>
/// Where the GPU shows up once the counters have been read: a section in the
/// overall view and a tab in the full one.
/// </summary>
[TestClass]
public class GpuViewTests
{
    private static WidgetViewModel Model(bool show = true) =>
        new(new AppConfig { Lang = "en", ShowGpu = show });

    private static Snapshot Sample(bool present = true) => new()
    {
        Ready = true,
        CpuTotal = 20,
        Cores = new[] { new Core { Usage = 20 } },
        Ram = new Ram { Usage = 50, UsedGb = 16, TotalGb = 32 },
        Gpu = present
            ? new Gpu
            {
                Present = true,
                Name = "Intel(R) Iris(R) Xe Graphics",
                Driver = "32.0.101.7079",
                Usage = 31,
                Engines = new[]
                {
                    new GpuEngine { Name = "3D", Usage = 31 },
                    new GpuEngine { Name = "Video decode", Usage = 4 },
                },
                SharedGb = 1.25,
            }
            : new Gpu(),
    };

    [TestMethod]
    public void The_overall_view_gets_a_gpu_section()
    {
        WidgetViewModel model = Model();
        model.UpdateOverall(Sample());

        Section gpu = model.Sections.First(s => s.Title == "GPU");

        Assert.AreEqual(3, gpu.Rows.Count, "the adapter, then a row per busy engine");
        Assert.AreEqual("GPU", gpu.Rows[0].Title);
        Assert.AreEqual(31, gpu.Rows[0].Percent);
        Assert.AreEqual("3D", gpu.Rows[1].Title);
        StringAssert.Contains(gpu.Note, "Iris", "the adapter names its own heading");
    }

    [TestMethod]
    public void No_gpu_row_wears_a_temperature_badge()
    {
        // Windows hands out no GPU temperature that does not need an
        // undocumented WDDM call; Task Manager shows N/A here for the same
        // reason, and a badge would be inventing one.
        WidgetViewModel model = Model();
        model.UpdateOverall(Sample());

        foreach (MeterRow row in model.Sections.First(s => s.Title == "GPU").Rows)
        {
            Assert.IsTrue(row.TempHidden, row.Title);
        }
    }

    [TestMethod]
    public void A_machine_with_no_counters_gets_no_section()
    {
        WidgetViewModel model = Model();
        model.UpdateOverall(Sample(present: false));

        Assert.IsFalse(model.Sections.Any(s => s.Title == "GPU"),
            "an empty heading helps nobody");
    }

    [TestMethod]
    public void Turning_it_off_removes_it()
    {
        WidgetViewModel model = Model(show: false);
        model.UpdateOverall(Sample());

        Assert.IsFalse(model.Sections.Any(s => s.Title == "GPU"));
    }

    [TestMethod]
    public void The_full_view_gets_a_gpu_tab()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample());

        DeviceTab tab = model.Tabs.First(t => t.Key == "gpu");

        Assert.AreEqual("GPU", tab.Title);
        Assert.AreEqual("31%", tab.Summary);
        StringAssert.Contains(tab.Hardware, "Iris");
        StringAssert.Contains(tab.Detail, "1.2 GB", "the memory it has borrowed");
        StringAssert.Contains(tab.Detail, "Shared");
    }

    [TestMethod]
    public void Each_engine_gets_a_square_beneath_the_adapter()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample());

        DeviceTab tab = model.Tabs.First(t => t.Key == "gpu");

        Assert.AreEqual(1, tab.Cards.Count, "the adapter graph stands alone");
        CollectionAssert.AreEqual(new[] { "3D", "Video decode" },
            tab.Cores.Select(c => c.Title).ToArray());
        Assert.AreEqual(tab.Cards[0].CardHeight / 2, tab.Cores[0].CardHeight);
    }

    [TestMethod]
    public void The_driver_version_is_a_fact_rather_than_a_rail_line()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample());

        DeviceTab tab = model.Tabs.First(t => t.Key == "gpu");
        Assert.IsTrue(tab.Facts.Any(f => f.Value == "32.0.101.7079"));
    }

    [TestMethod]
    public void An_adapter_with_no_dedicated_memory_does_not_say_zero()
    {
        WidgetViewModel model = Model();
        model.PushHistory(Sample());

        DeviceTab tab = model.Tabs.First(t => t.Key == "gpu");
        Assert.IsFalse(tab.Facts.Any(f => f.Name == "Dedicated"),
            "an integrated adapter borrows and owns nothing");
    }
}
