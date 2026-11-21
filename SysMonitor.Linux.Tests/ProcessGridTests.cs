using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class ProcessGridTests
{
    private static ProcessRow MakeProc(
        int pid,
        string name,
        string user = "alice",
        double? cpu = null,
        long mem = 1000,
        double? gpu = null,
        long? gpuMem = null,
        string cmd = "my-cmd") =>
        new()
        {
            Pid = pid,
            Name = name,
            User = user,
            CpuPercent = cpu,
            MemoryBytes = mem,
            GpuPercent = gpu,
            GpuMemoryBytes = gpuMem,
            Command = cmd,
        };

    private static AppEntry MakeApp(
        string id,
        string name,
        double? cpu = null,
        long mem = 1000,
        double? gpu = null,
        long? gpuMem = null,
        int instances = 1)
    {
        var list = new List<AppInstance>();
        for (int i = 0; i < instances; i++)
        {
            list.Add(new AppInstance
            {
                Unit = $"app-{id}-{i}.scope",
                Processes = Array.Empty<ProcessRow>(),
            });
        }
        return new AppEntry
        {
            Id = id,
            Name = name,
            CpuPercent = cpu,
            MemoryBytes = mem,
            GpuPercent = gpu,
            GpuMemoryBytes = gpuMem,
            Instances = list,
        };
    }

    [TestMethod]
    public void Sort_Processes_ByAllColumnsBothDirections()
    {
        var rows = new[]
        {
            MakeProc(pid: 20, name: "bravo", user: "charlie", cpu: 5.0, mem: 200, gpu: 10.0, gpuMem: 2048),
            MakeProc(pid: 10, name: "alpha", user: "alice", cpu: 15.0, mem: 100, gpu: 30.0, gpuMem: 1024),
            MakeProc(pid: 30, name: "charlie", user: "bob", cpu: 10.0, mem: 300, gpu: 20.0, gpuMem: 4096),
        };

        // Name
        IReadOnlyList<ProcessRow> byNameAsc = ProcessGrid.Sort(rows, GridColumn.Name, descending: false);
        Assert.AreEqual("alpha", byNameAsc[0].Name);
        Assert.AreEqual("charlie", byNameAsc[2].Name);

        IReadOnlyList<ProcessRow> byNameDesc = ProcessGrid.Sort(rows, GridColumn.Name, descending: true);
        Assert.AreEqual("charlie", byNameDesc[0].Name);
        Assert.AreEqual("alpha", byNameDesc[2].Name);

        // Pid
        IReadOnlyList<ProcessRow> byPidAsc = ProcessGrid.Sort(rows, GridColumn.Pid, descending: false);
        Assert.AreEqual(10, byPidAsc[0].Pid);
        Assert.AreEqual(30, byPidAsc[2].Pid);

        IReadOnlyList<ProcessRow> byPidDesc = ProcessGrid.Sort(rows, GridColumn.Pid, descending: true);
        Assert.AreEqual(30, byPidDesc[0].Pid);
        Assert.AreEqual(10, byPidDesc[2].Pid);

        // User
        IReadOnlyList<ProcessRow> byUserAsc = ProcessGrid.Sort(rows, GridColumn.User, descending: false);
        Assert.AreEqual("alice", byUserAsc[0].User);
        Assert.AreEqual("charlie", byUserAsc[2].User);

        IReadOnlyList<ProcessRow> byUserDesc = ProcessGrid.Sort(rows, GridColumn.User, descending: true);
        Assert.AreEqual("charlie", byUserDesc[0].User);
        Assert.AreEqual("alice", byUserDesc[2].User);

        // Cpu
        IReadOnlyList<ProcessRow> byCpuAsc = ProcessGrid.Sort(rows, GridColumn.Cpu, descending: false);
        Assert.AreEqual(5.0, byCpuAsc[0].CpuPercent);
        Assert.AreEqual(15.0, byCpuAsc[2].CpuPercent);

        IReadOnlyList<ProcessRow> byCpuDesc = ProcessGrid.Sort(rows, GridColumn.Cpu, descending: true);
        Assert.AreEqual(15.0, byCpuDesc[0].CpuPercent);
        Assert.AreEqual(5.0, byCpuDesc[2].CpuPercent);

        // Memory
        IReadOnlyList<ProcessRow> byMemAsc = ProcessGrid.Sort(rows, GridColumn.Memory, descending: false);
        Assert.AreEqual(100L, byMemAsc[0].MemoryBytes);
        Assert.AreEqual(300L, byMemAsc[2].MemoryBytes);

        IReadOnlyList<ProcessRow> byMemDesc = ProcessGrid.Sort(rows, GridColumn.Memory, descending: true);
        Assert.AreEqual(300L, byMemDesc[0].MemoryBytes);
        Assert.AreEqual(100L, byMemDesc[2].MemoryBytes);

        // Gpu
        IReadOnlyList<ProcessRow> byGpuAsc = ProcessGrid.Sort(rows, GridColumn.Gpu, descending: false);
        Assert.AreEqual(10.0, byGpuAsc[0].GpuPercent);
        Assert.AreEqual(30.0, byGpuAsc[2].GpuPercent);

        IReadOnlyList<ProcessRow> byGpuDesc = ProcessGrid.Sort(rows, GridColumn.Gpu, descending: true);
        Assert.AreEqual(30.0, byGpuDesc[0].GpuPercent);
        Assert.AreEqual(10.0, byGpuDesc[2].GpuPercent);

        // GpuMemory
        IReadOnlyList<ProcessRow> byGpuMemAsc = ProcessGrid.Sort(rows, GridColumn.GpuMemory, descending: false);
        Assert.AreEqual(1024L, byGpuMemAsc[0].GpuMemoryBytes);
        Assert.AreEqual(4096L, byGpuMemAsc[2].GpuMemoryBytes);

        IReadOnlyList<ProcessRow> byGpuMemDesc = ProcessGrid.Sort(rows, GridColumn.GpuMemory, descending: true);
        Assert.AreEqual(4096L, byGpuMemDesc[0].GpuMemoryBytes);
        Assert.AreEqual(1024L, byGpuMemDesc[2].GpuMemoryBytes);
    }

    [TestMethod]
    public void Sort_Apps_ByAllColumnsBothDirections()
    {
        var apps = new[]
        {
            MakeApp("app-b", "Bravo", cpu: 5.0, mem: 200, gpu: 10.0, gpuMem: 2048, instances: 1),
            MakeApp("app-a", "Alpha", cpu: 15.0, mem: 100, gpu: 30.0, gpuMem: 1024, instances: 3),
            MakeApp("app-c", "Charlie", cpu: 10.0, mem: 300, gpu: 20.0, gpuMem: 4096, instances: 2),
        };

        // Instances column
        IReadOnlyList<AppEntry> byInstAsc = ProcessGrid.Sort(apps, GridColumn.Instances, descending: false);
        Assert.AreEqual(1, byInstAsc[0].Instances.Count);
        Assert.AreEqual(3, byInstAsc[2].Instances.Count);

        IReadOnlyList<AppEntry> byInstDesc = ProcessGrid.Sort(apps, GridColumn.Instances, descending: true);
        Assert.AreEqual(3, byInstDesc[0].Instances.Count);
        Assert.AreEqual(1, byInstDesc[2].Instances.Count);

        // Memory
        IReadOnlyList<AppEntry> byMemDesc = ProcessGrid.Sort(apps, GridColumn.Memory, descending: true);
        Assert.AreEqual(300L, byMemDesc[0].MemoryBytes);
        Assert.AreEqual(100L, byMemDesc[2].MemoryBytes);
    }

    [TestMethod]
    public void Sort_NullsLastInBothDirections()
    {
        var rows = new[]
        {
            MakeProc(pid: 1, name: "has-10", cpu: 10.0, gpu: 10.0, gpuMem: 100),
            MakeProc(pid: 2, name: "has-null", cpu: null, gpu: null, gpuMem: null),
            MakeProc(pid: 3, name: "has-50", cpu: 50.0, gpu: 50.0, gpuMem: 500),
        };

        // Ascending CPU: 10.0, 50.0, null
        IReadOnlyList<ProcessRow> cpuAsc = ProcessGrid.Sort(rows, GridColumn.Cpu, descending: false);
        Assert.AreEqual(10.0, cpuAsc[0].CpuPercent);
        Assert.AreEqual(50.0, cpuAsc[1].CpuPercent);
        Assert.IsNull(cpuAsc[2].CpuPercent, "Null CPU must be last when sorting ascending");

        // Descending CPU: 50.0, 10.0, null
        IReadOnlyList<ProcessRow> cpuDesc = ProcessGrid.Sort(rows, GridColumn.Cpu, descending: true);
        Assert.AreEqual(50.0, cpuDesc[0].CpuPercent);
        Assert.AreEqual(10.0, cpuDesc[1].CpuPercent);
        Assert.IsNull(cpuDesc[2].CpuPercent, "Null CPU must be last when sorting descending");

        // Ascending GPU: 10.0, 50.0, null
        IReadOnlyList<ProcessRow> gpuAsc = ProcessGrid.Sort(rows, GridColumn.Gpu, descending: false);
        Assert.AreEqual(10.0, gpuAsc[0].GpuPercent);
        Assert.IsNull(gpuAsc[2].GpuPercent);

        // Descending GPU: 50.0, 10.0, null
        IReadOnlyList<ProcessRow> gpuDesc = ProcessGrid.Sort(rows, GridColumn.Gpu, descending: true);
        Assert.AreEqual(50.0, gpuDesc[0].GpuPercent);
        Assert.IsNull(gpuDesc[2].GpuPercent);

        // Ascending GpuMemory: 100, 500, null
        IReadOnlyList<ProcessRow> gpuMemAsc = ProcessGrid.Sort(rows, GridColumn.GpuMemory, descending: false);
        Assert.AreEqual(100L, gpuMemAsc[0].GpuMemoryBytes);
        Assert.IsNull(gpuMemAsc[2].GpuMemoryBytes);

        // Descending GpuMemory: 500, 100, null
        IReadOnlyList<ProcessRow> gpuMemDesc = ProcessGrid.Sort(rows, GridColumn.GpuMemory, descending: true);
        Assert.AreEqual(500L, gpuMemDesc[0].GpuMemoryBytes);
        Assert.IsNull(gpuMemDesc[2].GpuMemoryBytes);
    }

    [TestMethod]
    public void Sort_TieBreaksByNameThenPid()
    {
        var rows = new[]
        {
            MakeProc(pid: 200, name: "worker", mem: 500),
            MakeProc(pid: 100, name: "worker", mem: 500),
            MakeProc(pid: 300, name: "alpha", mem: 500),
        };

        // When memory is equal, sort by Name (alpha first), then by PID (100 before 200)
        IReadOnlyList<ProcessRow> sorted = ProcessGrid.Sort(rows, GridColumn.Memory, descending: false);
        Assert.AreEqual(300, sorted[0].Pid); // alpha
        Assert.AreEqual(100, sorted[1].Pid); // worker (100)
        Assert.AreEqual(200, sorted[2].Pid); // worker (200)
    }

    [TestMethod]
    public void Filter_MatchesNameCommandUserOrPid_CaseInsensitive()
    {
        var rows = new[]
        {
            MakeProc(pid: 1234, name: "WebBrowser", user: "alice", cmd: "/usr/bin/browser --tab"),
            MakeProc(pid: 5678, name: "python", user: "bob", cmd: "python main.py"),
            MakeProc(pid: 9999, name: "daemon", user: "root", cmd: "/opt/daemon/run"),
        };

        // Filter by PID substring
        Assert.AreEqual(1, ProcessGrid.Filter(rows, "123").Count);
        Assert.AreEqual(1234, ProcessGrid.Filter(rows, "123")[0].Pid);

        // Filter by Name substring (case-insensitive)
        Assert.AreEqual(1, ProcessGrid.Filter(rows, "web").Count);
        Assert.AreEqual("WebBrowser", ProcessGrid.Filter(rows, "web")[0].Name);

        // Filter by User
        Assert.AreEqual(1, ProcessGrid.Filter(rows, "BOB").Count);
        Assert.AreEqual("bob", ProcessGrid.Filter(rows, "BOB")[0].User);

        // Filter by Command
        Assert.AreEqual(1, ProcessGrid.Filter(rows, "main.py").Count);
        Assert.AreEqual(5678, ProcessGrid.Filter(rows, "main.py")[0].Pid);
    }

    [TestMethod]
    public void Filter_EmptyOrWhitespace_ReturnsAll()
    {
        var rows = new[]
        {
            MakeProc(pid: 1, name: "one"),
            MakeProc(pid: 2, name: "two"),
        };

        Assert.AreEqual(2, ProcessGrid.Filter(rows, null).Count);
        Assert.AreEqual(2, ProcessGrid.Filter(rows, "").Count);
        Assert.AreEqual(2, ProcessGrid.Filter(rows, "   ").Count);
    }

    [TestMethod]
    public void AppConfig_GridKeys_DefaultsRoundTripAndFallback()
    {
        // 1. Defaults
        var config = new AppConfig();
        Assert.AreEqual("memory", config.AppsSort);
        Assert.IsTrue(config.AppsSortDesc);
        Assert.AreEqual("memory", config.ProcsSort);
        Assert.IsTrue(config.ProcsSortDesc);

        // 2. Round-trip in custom path
        string dir = Path.Combine(Path.GetTempPath(), "gridcfg-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "config.json");

        try
        {
            config.AppsSort = "cpu";
            config.AppsSortDesc = false;
            config.ProcsSort = "name";
            config.ProcsSortDesc = true;
            Assert.IsTrue(config.Save(path));

            AppConfig loaded = AppConfig.Load(path);
            Assert.AreEqual("cpu", loaded.AppsSort);
            Assert.IsFalse(loaded.AppsSortDesc);
            Assert.AreEqual("name", loaded.ProcsSort);
            Assert.IsTrue(loaded.ProcsSortDesc);

            // 3. Unknown value falls back to default ("memory")
            loaded.AppsSort = "invalid_col";
            loaded.ProcsSort = "nonsense";
            loaded.Normalise();
            Assert.AreEqual("memory", loaded.AppsSort);
            Assert.AreEqual("memory", loaded.ProcsSort);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
