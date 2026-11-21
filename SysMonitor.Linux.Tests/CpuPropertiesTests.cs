using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sampling;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class CpuPropertiesTests
{
    private const string SampleCpuinfoIntel =
        "processor\t: 0\n" +
        "vendor_id\t: GenuineIntel\n" +
        "model name\t: Intel(R) Core(TM) i5-1035G1 CPU @ 1.00GHz\n" +
        "physical id\t: 0\n" +
        "core id\t\t: 0\n" +
        "flags\t\t: fpu vme de pse tsc msr pae mce cx8 apic sep mtrr pge mca cmov pat pse36 clflush vmx sse sse2\n";

    private const string SampleCpuinfoAmd =
        "processor\t: 0\n" +
        "vendor_id\t: AuthenticAMD\n" +
        "model name\t: AMD Ryzen 7 5800X\n" +
        "physical id\t: 0\n" +
        "core id\t\t: 0\n" +
        "flags\t\t: fpu vme de pse tsc msr pae mce cx8 apic sep mtrr pge mca cmov pat pse36 clflush svm sse sse2\n";

    private const string SampleCpuinfoNoVirt =
        "processor\t: 0\n" +
        "vendor_id\t: GenuineIntel\n" +
        "model name\t: Intel Pentium\n" +
        "physical id\t: 0\n" +
        "core id\t\t: 0\n" +
        "flags\t\t: fpu vme de pse tsc msr sse sse2\n";

    [TestMethod]
    public void CurrentMhz_ScalingCurFreq_ConvertsKhzToMhzPerCore()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq", "1202000\n");
        fake.WriteFile("sys/devices/system/cpu/cpu1/cpufreq/scaling_cur_freq", "2400000\n");

        Assert.AreEqual(1202, CpuInfoReader.CurrentMhz(fake.Root, 0));
        Assert.AreEqual(2400, CpuInfoReader.CurrentMhz(fake.Root, 1));
    }

    [TestMethod]
    public void CurrentMhz_WhenMissingOrNotPositiveOrGarbage_ReturnsNull()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq", "0\n");
        fake.WriteFile("sys/devices/system/cpu/cpu1/cpufreq/scaling_cur_freq", "-1000\n");
        fake.WriteFile("sys/devices/system/cpu/cpu2/cpufreq/scaling_cur_freq", "garbage\n");

        Assert.IsNull(CpuInfoReader.CurrentMhz(fake.Root, 0));
        Assert.IsNull(CpuInfoReader.CurrentMhz(fake.Root, 1));
        Assert.IsNull(CpuInfoReader.CurrentMhz(fake.Root, 2));
        Assert.IsNull(CpuInfoReader.CurrentMhz(fake.Root, 3)); // File does not exist
    }

    [TestMethod]
    public void MaxMhz_CpuinfoMaxFreq_ConvertsKhzToMhz()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq", "3600000\n");

        Assert.AreEqual(3600, CpuInfoReader.MaxMhz(fake.Root));
    }

    [TestMethod]
    public void MaxMhz_WhenMissingOrNotPositive_ReturnsNull()
    {
        using var fake = new FakeRoot();
        Assert.IsNull(CpuInfoReader.MaxMhz(fake.Root));

        fake.WriteFile("sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq", "0\n");
        Assert.IsNull(CpuInfoReader.MaxMhz(fake.Root));

        fake.WriteFile("sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq", "-500\n");
        Assert.IsNull(CpuInfoReader.MaxMhz(fake.Root));
    }

    [TestMethod]
    public void ArchitectureName_MapsKnownArchitectures()
    {
        Assert.AreEqual("x86_64", CpuInfoReader.ArchitectureName(Architecture.X64));
        Assert.AreEqual("x86", CpuInfoReader.ArchitectureName(Architecture.X86));
        Assert.AreEqual("aarch64", CpuInfoReader.ArchitectureName(Architecture.Arm64));
        Assert.AreEqual("arm", CpuInfoReader.ArchitectureName(Architecture.Arm));
        Assert.AreEqual("s390x", CpuInfoReader.ArchitectureName(Architecture.S390x));
        Assert.AreEqual("loongarch64", CpuInfoReader.ArchitectureName(Architecture.LoongArch64));
    }

    [TestMethod]
    public void VirtualizationType_Helper_VmxIsVtx_SvmIsAmdV_NeitherIsEmpty()
    {
        Assert.AreEqual("VT-x", CpuInfoParser.VirtualizationType(new[] { "fpu", "vmx", "sse" }));
        Assert.AreEqual("AMD-V", CpuInfoParser.VirtualizationType(new[] { "fpu", "svm", "sse" }));
        Assert.AreEqual(string.Empty, CpuInfoParser.VirtualizationType(new[] { "fpu", "sse", "avx" }));
        Assert.AreEqual(string.Empty, CpuInfoParser.VirtualizationType(Array.Empty<string>()));
    }

    [TestMethod]
    public void Parse_SetsVirtualizationTypeAndVirtualization()
    {
        CpuInfo intel = CpuInfoParser.Parse(SampleCpuinfoIntel);
        Assert.AreEqual("VT-x", intel.VirtualizationType);
        Assert.AreEqual(true, intel.Virtualization);

        CpuInfo amd = CpuInfoParser.Parse(SampleCpuinfoAmd);
        Assert.AreEqual("AMD-V", amd.VirtualizationType);
        Assert.AreEqual(true, amd.Virtualization);

        CpuInfo none = CpuInfoParser.Parse(SampleCpuinfoNoVirt);
        Assert.AreEqual(string.Empty, none.VirtualizationType);
        Assert.AreEqual(false, none.Virtualization);
    }

    [TestMethod]
    public void CpuInfoReader_Read_PopulatesMaxMhzAndArchitecture()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/cpuinfo", SampleCpuinfoIntel);
        fake.WriteFile("sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq", "3600000\n");

        CpuInfo info = CpuInfoReader.Read(fake.Root);

        Assert.AreEqual(3600, info.MaxMhz);
        Assert.AreEqual("VT-x", info.VirtualizationType);
        Assert.AreEqual(true, info.Virtualization);
        Assert.IsFalse(string.IsNullOrEmpty(info.Architecture));
    }

    [TestMethod]
    public void LinuxSampler_Sample_ParsesProcUptimeCorrectly()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/stat", "cpu  1000 200 300 4000 500 60 70 80 0 0\n");
        fake.WriteFile("proc/uptime", "123456.78 987654.32\n");

        var sampler = new LinuxSampler(fake.Root);
        Snapshot snapshot = sampler.Sample(includeProcesses: false);

        Assert.IsNotNull(snapshot.Uptime);
        Assert.AreEqual(TimeSpan.FromSeconds(123456.78), snapshot.Uptime);
    }

    [TestMethod]
    public void LinuxSampler_Sample_WhenUptimeMissingOrGarbageOrNegative_ReturnsNull()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/stat", "cpu  1000 200 300 4000 500 60 70 80 0 0\n");

        // Missing /proc/uptime
        var sampler = new LinuxSampler(fake.Root);
        Snapshot snapshot1 = sampler.Sample(includeProcesses: false);
        Assert.IsNull(snapshot1.Uptime);

        // Garbage content
        fake.WriteFile("proc/uptime", "not_a_number garbage\n");
        Snapshot snapshot2 = sampler.Sample(includeProcesses: false);
        Assert.IsNull(snapshot2.Uptime);

        // Negative seconds
        fake.WriteFile("proc/uptime", "-123.45 678.90\n");
        Snapshot snapshot3 = sampler.Sample(includeProcesses: false);
        Assert.IsNull(snapshot3.Uptime);

        // Empty file
        fake.WriteFile("proc/uptime", "\n");
        Snapshot snapshot4 = sampler.Sample(includeProcesses: false);
        Assert.IsNull(snapshot4.Uptime);
    }

    [TestMethod]
    public void LinuxSampler_Sample_PopulatesCoreMhzPerCore()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/stat",
            "cpu  2000 400 600 8000 1000 120 140 160 0 0\n" +
            "cpu0 1000 200 300 4000 500 60 70 80 0 0\n" +
            "cpu1 1000 200 300 4000 500 60 70 80 0 0\n");

        fake.WriteFile("sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq", "1202000\n");
        // cpu1 scaling_cur_freq missing

        var sampler = new LinuxSampler(fake.Root);
        Snapshot snapshot = sampler.Sample(includeProcesses: false);

        Assert.AreEqual(2, snapshot.Cores.Count);
        Assert.AreEqual(1202, snapshot.Cores[0].Mhz);
        Assert.IsNull(snapshot.Cores[1].Mhz);
    }
}
