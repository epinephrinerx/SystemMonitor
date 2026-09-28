using System.Diagnostics;
using SysMonitor.Model;
using SysMonitor.Sensors;

namespace SysMonitor.Tests;

/// <summary>
/// The GPU counters, read the way Task Manager reads them.
///
/// These run against the real machine rather than a fake, because the thing
/// worth knowing is whether PDH answers at all and how long it takes -- and
/// neither can be learned from a stub. They assert shape, not values: a test
/// that insisted the GPU was busy would fail on an idle machine and a test
/// that insisted it was idle would fail during a build.
/// </summary>
[TestClass]
public class GpuSensorTests
{
    [TestMethod]
    public void A_reading_is_internally_consistent()
    {
        using var sensor = new GpuSensor();
        sensor.Read();                      // rate counters need a first pass
        Gpu gpu = sensor.Read();

        if (!gpu.Present)
        {
            Assert.Inconclusive("no WDDM counters on this machine");
            return;
        }

        Assert.IsTrue(gpu.Usage is >= 0 and <= 100, $"usage was {gpu.Usage}");
        foreach (GpuEngine engine in gpu.Engines)
        {
            Assert.IsTrue(engine.Usage is > 0 and <= 100,
                $"{engine.Name} was {engine.Usage}");
            Assert.AreNotEqual(string.Empty, engine.Name);
        }
        Assert.AreEqual(gpu.Engines.Count == 0 ? 0 : gpu.Engines.Max(e => e.Usage),
            gpu.Usage, "the headline figure is the busiest engine");
        Assert.IsTrue(gpu.DedicatedGb >= 0 && gpu.SharedGb >= 0);
    }

    [TestMethod]
    public void The_engine_list_is_ordered_and_free_of_idle_entries()
    {
        using var sensor = new GpuSensor();
        sensor.Read();
        Gpu gpu = sensor.Read();

        if (!gpu.Present)
        {
            Assert.Inconclusive("no WDDM counters on this machine");
            return;
        }

        CollectionAssert.AreEqual(
            gpu.Engines.OrderByDescending(e => e.Usage).Select(e => e.Name).ToArray(),
            gpu.Engines.Select(e => e.Name).ToArray(),
            "busiest first, so the panel reads top down");
        CollectionAssert.AllItemsAreUnique(gpu.Engines.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void A_sample_is_cheap_enough_for_the_metrics_cadence()
    {
        // The counter is published per process per engine, so this machine has
        // upwards of a thousand instances of it. That is the whole question:
        // if one collection cost a large fraction of the 2.5s sampling period
        // it would have to move to the slow pass, or go.
        using var sensor = new GpuSensor();
        sensor.Read();

        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++)
        {
            sensor.Read();
        }
        double each = clock.Elapsed.TotalMilliseconds / 5;

        Console.WriteLine($"GPU sample: {each:F1} ms");
        Assert.IsTrue(each < 250,
            $"a GPU sample took {each:F0} ms, too much for a 2.5s cadence");
    }

    [TestMethod]
    public void Reading_without_a_first_pass_does_not_throw()
    {
        // Rate counters have no value until the second collection. The first
        // read must come back empty rather than blowing up.
        using var sensor = new GpuSensor();
        Gpu gpu = sensor.Read();

        Assert.IsTrue(gpu.Usage is >= 0 and <= 100);
    }

    [TestMethod]
    public void Disposing_twice_is_harmless()
    {
        var sensor = new GpuSensor();
        sensor.Read();
        sensor.Dispose();
        sensor.Dispose();
    }
}
