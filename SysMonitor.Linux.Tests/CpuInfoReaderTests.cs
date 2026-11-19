using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class CpuInfoReaderTests
{
    private const string SampleCpuinfoIntel =
        "processor\t: 0\n" +
        "vendor_id\t: GenuineIntel\n" +
        "model name\t: Intel(R) Core(TM) i5-1035G1 CPU @ 1.00GHz\n" +
        "physical id\t: 0\n" +
        "core id\t\t: 0\n" +
        "flags\t\t: fpu vme de pse tsc msr pae mce cx8 apic sep mtrr pge mca cmov pat pse36 clflush dts acpi mmx fxsr sse sse2 ss ht tm pbe syscall nx pdpe1gb rdtscp lm constant_tsc art arch_perfmon pebs bts rep_good nopl xtopology nonstop_tsc cpuid aperfmperf pni pclmulqdq dtes64 monitor ds_cpl vmx smx est tm2 ssse3 sdbg fma cx16 xtpr pdcm pcid sse4_1 sse4_2 x2apic movbe popcnt tsc_deadline_timer aes xsave avx f16c rdrand lahf_lm abm 3dnowprefetch cpuid_fault epb invpcid_single ssbd ibrs ibpb stibp ibrs_enhanced tpr_shadow vnmi flexpriority ept vpid ept_ad fsgsbase tsc_adjust bmi1 avx2 smep bmi2 erms invpcid mpx rdseed adx smap clflushopt intel_pt xsaveopt xsavec xgetbv1 xsaves dtherm ida arat pln pts hwp hwp_notify hwp_act_window hwp_epp hwp_pkg_req avx512f avx512dq rdpid avx512cd avx512bw avx512vl avx512_vnni md_clear flush_l1d arch_capabilities\n\n" +
        "processor\t: 1\n" +
        "vendor_id\t: GenuineIntel\n" +
        "model name\t: Intel(R) Core(TM) i5-1035G1 CPU @ 1.00GHz\n" +
        "physical id\t: 0\n" +
        "core id\t\t: 1\n" +
        "flags\t\t: fpu vme de pse tsc msr vmx\n\n" +
        "processor\t: 2\n" +
        "vendor_id\t: GenuineIntel\n" +
        "model name\t: Intel(R) Core(TM) i5-1035G1 CPU @ 1.00GHz\n" +
        "physical id\t: 0\n" +
        "core id\t\t: 0\n" + // Hyper-thread of core 0
        "flags\t\t: fpu vme de pse tsc msr vmx\n\n" +
        "processor\t: 3\n" +
        "vendor_id\t: GenuineIntel\n" +
        "model name\t: Intel(R) Core(TM) i5-1035G1 CPU @ 1.00GHz\n" +
        "physical id\t: 0\n" +
        "core id\t\t: 1\n" + // Hyper-thread of core 1
        "flags\t\t: fpu vme de pse tsc msr vmx\n";

    [TestMethod]
    public void Parse_ExtractsCleanNameAndCountsPhysicalVsLogicalCores()
    {
        CpuInfo info = CpuInfoParser.Parse(SampleCpuinfoIntel);

        // Strips " CPU @ 1.00GHz"
        Assert.AreEqual("Intel(R) Core(TM) i5-1035G1", info.Name);
        Assert.AreEqual("GenuineIntel", info.Vendor);
        Assert.AreEqual(1, info.Sockets);
        Assert.AreEqual(2, info.Cores); // Core IDs 0 and 1
        Assert.AreEqual(4, info.Logical); // 4 logical processors
        Assert.AreEqual(true, info.Virtualization); // has "vmx"
    }

    [TestMethod]
    public void Parse_WhenVirtualizationFlagsAbsent_SetsVirtualizationFalse()
    {
        string withoutVmx =
            "processor\t: 0\n" +
            "vendor_id\t: AuthenticAMD\n" +
            "model name\t: AMD Ryzen\n" +
            "flags\t\t: fpu vme de pse\n";

        CpuInfo info = CpuInfoParser.Parse(withoutVmx);
        Assert.AreEqual(false, info.Virtualization);
    }

    [TestMethod]
    public void CpuCaches_DistinctAndSums_DeduplicatesProperly()
    {
        // Suppose 4 logical CPUs, each pointing to L1, L2, L3
        var caches = new List<CpuCacheInfo>
        {
            // Core 0 (HT 0 and 2) shares L2
            new(Level: 2, Type: "Unified", SizeKb: 512, SharedCpuList: "0,2"),
            new(Level: 2, Type: "Unified", SizeKb: 512, SharedCpuList: "0,2"), // duplicate seen from cpu2

            // Core 1 (HT 1 and 3) shares L2
            new(Level: 2, Type: "Unified", SizeKb: 512, SharedCpuList: "1,3"),
            new(Level: 2, Type: "Unified", SizeKb: 512, SharedCpuList: "1,3"), // duplicate seen from cpu3

            // All share L3
            new(Level: 3, Type: "Unified", SizeKb: 6144, SharedCpuList: "0-3"),
            new(Level: 3, Type: "Unified", SizeKb: 6144, SharedCpuList: "0-3"),
            new(Level: 3, Type: "Unified", SizeKb: 6144, SharedCpuList: "0-3"),
            new(Level: 3, Type: "Unified", SizeKb: 6144, SharedCpuList: "0-3"),
        };

        IReadOnlyList<CpuCacheInfo> distinct = CpuCaches.Distinct(caches);
        Assert.AreEqual(3, distinct.Count); // Two distinct L2s + One L3

        int l2Total = CpuCaches.L2Kb(caches);
        Assert.AreEqual(1024, l2Total); // 512 + 512

        int l3Total = CpuCaches.L3Kb(caches);
        Assert.AreEqual(6144, l3Total);
    }

    [TestMethod]
    public void CpuCaches_ParseSizeKb_ParsesVariousUnits()
    {
        Assert.AreEqual(512, CpuCaches.ParseSizeKb("512K"));
        Assert.AreEqual(1024, CpuCaches.ParseSizeKb("1M"));
        Assert.AreEqual(6144, CpuCaches.ParseSizeKb("6M"));
        Assert.AreEqual(1024 * 1024, CpuCaches.ParseSizeKb("1G"));
        Assert.AreEqual(256, CpuCaches.ParseSizeKb("256"));
    }

    [TestMethod]
    public void Read_WithFakeRoot_PopulatesBaseMhzAndCaches()
    {
        using var fake = new FakeRoot();
        fake.WriteFile("proc/cpuinfo", SampleCpuinfoIntel);
        fake.WriteFile("sys/devices/system/cpu/cpu0/cpufreq/base_frequency", "1200000\n"); // 1200 MHz

        // Setup sysfs cache for cpu0 and cpu1
        fake.WriteFile("sys/devices/system/cpu/cpu0/cache/index0/level", "2\n");
        fake.WriteFile("sys/devices/system/cpu/cpu0/cache/index0/size", "512K\n");
        fake.WriteFile("sys/devices/system/cpu/cpu0/cache/index0/type", "Unified\n");
        fake.WriteFile("sys/devices/system/cpu/cpu0/cache/index0/shared_cpu_list", "0,2\n");

        fake.WriteFile("sys/devices/system/cpu/cpu0/cache/index1/level", "3\n");
        fake.WriteFile("sys/devices/system/cpu/cpu0/cache/index1/size", "6M\n");
        fake.WriteFile("sys/devices/system/cpu/cpu0/cache/index1/type", "Unified\n");
        fake.WriteFile("sys/devices/system/cpu/cpu0/cache/index1/shared_cpu_list", "0-3\n");

        CpuInfo info = CpuInfoReader.Read(fake.Root);

        Assert.AreEqual("Intel(R) Core(TM) i5-1035G1", info.Name);
        Assert.AreEqual(1200, info.BaseMhz);
        Assert.AreEqual(512, info.L2Kb);
        Assert.AreEqual(6144, info.L3Kb);
    }
}
