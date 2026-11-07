#nullable enable
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class Round3Tests
{
    // Stateful presentation types are exercised sequentially on one thread, per spec/04_clarifications.txt.
    [TestMethod]
    public void HistoryKeys_RetainedCollectionDoesNotAcquireNewSeries()
    {
        var history = new HistoryStore(5);
        history.Record(new Snapshot
        {
            CpuTotal = 10, Ram = new Ram(), Cores = System.Array.Empty<Core>(),
            Disks = System.Array.Empty<Disk>(), Adapters = System.Array.Empty<Adapter>(),
            Modules = System.Array.Empty<Module>()
        });
        var retained = history.Keys;
        // oracle: 04(d) creates cpu then ram when there are no cores, disks or included adapters.
        CollectionAssert.AreEqual(new[] { "cpu", "ram" }, new System.Collections.Generic.List<string>(retained));

        history.Record(new Snapshot
        {
            CpuTotal = 20, Ram = new Ram(), Cores = new[] { new Core { Usage = 30 } },
            Disks = new[] { new Disk { Letter = "/data", Label = "Data", TotalGb = 100, Usage = 40 } },
            Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 100, DownMb = 1, UpMb = 2 } },
            Modules = System.Array.Empty<Module>()
        });
        // oracle: Inspector-B (5) Keys returns a copy; recording new series cannot change the retained collection.
        Assert.AreEqual(2, retained.Count);
        CollectionAssert.AreEqual(new[] { "cpu", "ram" }, new System.Collections.Generic.List<string>(retained));
        // oracle: 04(d) a fresh Keys includes newly created core, disk usage/read/write, then network down/up in creation order.
        CollectionAssert.AreEqual(new[]
        {
            "cpu", "ram", "core:0", "disk:/data", "disk:/data:read", "disk:/data:write", "net:eth0:down", "net:eth0:up"
        }, new System.Collections.Generic.List<string>(history.Keys));
    }

    [TestMethod]
    public void HistoryKeys_RetainedEmptyCollectionRemainsEmpty()
    {
        var history = new HistoryStore(5);
        var retained = history.Keys;
        history.Record(new Snapshot
        {
            Ram = new Ram(), Cores = System.Array.Empty<Core>(), Disks = System.Array.Empty<Disk>(),
            Adapters = System.Array.Empty<Adapter>(), Modules = System.Array.Empty<Module>()
        });
        // oracle: Inspector-B (5) an empty Keys result is also a copy and stays empty after the first Record adds cpu and ram.
        Assert.AreEqual(0, retained.Count);
        CollectionAssert.AreEqual(new string[0], new System.Collections.Generic.List<string>(retained));
        CollectionAssert.AreEqual(new[] { "cpu", "ram" }, new System.Collections.Generic.List<string>(history.Keys));
    }

    [TestMethod]
    public void MetricBuilder_CleanDiskLabelPreservesStringReference()
    {
        var label = new string(new[] { 'D', 'a', 't', 'a', ' ', 'v', 'o', 'l', 'u', 'm', 'e' });
        var snapshot = new Snapshot
        {
            Ram = new Ram(), Cores = System.Array.Empty<Core>(),
            Disks = new[] { new Disk { Letter = "/data", Label = label, TotalGb = 100 } },
            Adapters = System.Array.Empty<Adapter>(), Modules = System.Array.Empty<Module>()
        };
        var items = MetricBuilder.Build(snapshot);
        // oracle: 5a/3 orders CPU, RAM, disk; Inspector-B (6) and 04(g) return the same string instance for an already-clean label.
        Assert.AreEqual(3, items.Count);
        Assert.AreEqual("disk:/data", items[2].Key);
        Assert.AreEqual("Data volume", items[2].Title);
        Assert.AreSame(snapshot.Disks[0].Label, items[2].Title);
        Assert.AreSame(label, items[2].Title);
    }

    [TestMethod]
    public void MiniRotation_VersionSaturatesWhenPropertySetterIsAvailable()
    {
        var rotation = new MiniRotation();
        var cpu = new MetricItem(MetricKind.Cpu, "cpu", "CPU", "0 cores", 10, "10%", "#3b82f6", "#3b82f6");
        var ram = new MetricItem(MetricKind.Ram, "ram", "Memory", "0 MB / 0 MB", 0, "0%", "#a855f7", "#a855f7");
        rotation.Update(new[] { cpu, ram }, 0);
        var setter = typeof(MiniRotation).GetProperty("Version")!.GetSetMethod(true);
        if (setter is null)
        {
            // oracle: Inspector-B (7) permits only a Version property setter or public API; a getter-only implementation cannot be seeded practically.
            Assert.Inconclusive("MiniRotation.Version has no property setter; reaching saturation publicly requires over two billion moves (see COVERAGE.md).");
            return;
        }
        setter.Invoke(rotation, new object[] { int.MaxValue });

        rotation.Next(1);
        // oracle: 04(f) a moving Next keeps Version at int.MaxValue while selecting RAM at index 1.
        Assert.AreEqual(int.MaxValue, rotation.Version);
        Assert.AreEqual(1, rotation.Index);
        Assert.AreEqual("ram", rotation.Current!.Key);
        rotation.Previous(2);
        // oracle: 04(f) a moving Previous also saturates and selects CPU at index 0.
        Assert.AreEqual(int.MaxValue, rotation.Version);
        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("cpu", rotation.Current!.Key);
        rotation.Tick(5002);
        // oracle: the exact 5000 ms interval moves to RAM; 04(f) keeps Version saturated.
        Assert.AreEqual(int.MaxValue, rotation.Version);
        Assert.AreEqual(1, rotation.Index);
        Assert.AreEqual("ram", rotation.Current!.Key);
        rotation.Update(new[] { cpu }, 5003);
        // oracle: removing RAM retargets CPU at index 0; 04(f) saturates on Update as on the navigation operations.
        Assert.AreEqual(int.MaxValue, rotation.Version);
        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("cpu", rotation.Current!.Key);
    }
}
