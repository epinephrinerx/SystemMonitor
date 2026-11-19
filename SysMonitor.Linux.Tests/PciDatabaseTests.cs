using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class PciDatabaseTests
{
    private const string SamplePciIds =
        "# PCI IDs sample database\n" +
        "10de  NVIDIA Corporation\n" +
        "\t1d13  GP108BM [GeForce MX250]\n" +
        "\t\t1025 1331  Aspire A515-54G\n" + // subsystem line: 2 tabs, must be skipped
        "\t1f95  TU117M [GeForce GTX 1650 Ti Mobile]\n" +
        "8086  Intel Corporation\n" +
        "\t8a56  Iris Plus Graphics G1 (Ice Lake)\n" +
        "\t02f0  Wi-Fi 6 AX201\n";

    [TestMethod]
    public void Parse_LoadsVendorsAndDevices_IgnoringSubsystemsAndComments()
    {
        PciDatabase db = PciDatabase.Parse(SamplePciIds);

        Assert.AreEqual("NVIDIA Corporation", db.VendorName(0x10de));
        Assert.AreEqual("Intel Corporation", db.VendorName(0x8086));

        Assert.AreEqual("GP108BM [GeForce MX250]", db.DeviceName(0x10de, 0x1d13));
        Assert.AreEqual("Iris Plus Graphics G1 (Ice Lake)", db.DeviceName(0x8086, 0x8a56));
        Assert.AreEqual("Wi-Fi 6 AX201", db.DeviceName(0x8086, 0x02f0));

        // Unknown
        Assert.AreEqual("", db.VendorName(0x9999));
        Assert.AreEqual("", db.DeviceName(0x10de, 0x9999));
    }

    [TestMethod]
    public void Describe_CombinesVendorAndDevice()
    {
        PciDatabase db = PciDatabase.Parse(SamplePciIds);

        string desc = db.Describe(0x10de, 0x1d13);
        Assert.AreEqual("NVIDIA Corporation GP108BM [GeForce MX250]", desc);

        string intelGpu = db.Describe(0x8086, 0x8a56);
        Assert.AreEqual("Intel Corporation Iris Plus Graphics G1 (Ice Lake)", intelGpu);
    }
}
