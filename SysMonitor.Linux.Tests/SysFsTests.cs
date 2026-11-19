using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class SysFsTests
{
    [TestMethod]
    public void DecodeOctal_DecodesStandardKernelEscapes()
    {
        Assert.AreEqual("USB Drive", SysFs.DecodeOctal("USB\\040Drive"));
        Assert.AreEqual("Path\\With\\Backslash", SysFs.DecodeOctal("Path\\134With\\134Backslash"));
        Assert.AreEqual("NormalText", SysFs.DecodeOctal("NormalText"));
        Assert.AreEqual("", SysFs.DecodeOctal(""));
    }

    [TestMethod]
    public void Celsius_ConvertsMilliDegreesAndRejectsInvalidValues()
    {
        Assert.AreEqual(45, SysFs.Celsius(45000));
        Assert.AreEqual(46, SysFs.Celsius(45600)); // Rounds away from zero
        Assert.AreEqual(0, SysFs.Celsius(0));
        Assert.AreEqual(-10, SysFs.Celsius(-10000));

        // Out-of-bounds rejection: <= -20000 mC or >= 130000 mC
        Assert.IsNull(SysFs.Celsius(-25000));
        Assert.IsNull(SysFs.Celsius(-20000));
        Assert.IsNull(SysFs.Celsius(130000));
        Assert.IsNull(SysFs.Celsius(150000));
        Assert.IsNull(SysFs.Celsius(null));
    }

    [TestMethod]
    public void IsSafeName_RejectsUnsafeAndPathTraversingNames()
    {
        Assert.IsTrue(SysFs.IsSafeName("nvme0n1"));
        Assert.IsTrue(SysFs.IsSafeName("eth0"));
        Assert.IsTrue(SysFs.IsSafeName("hwmon0"));

        Assert.IsFalse(SysFs.IsSafeName(""));
        Assert.IsFalse(SysFs.IsSafeName("."));
        Assert.IsFalse(SysFs.IsSafeName(".."));
        Assert.IsFalse(SysFs.IsSafeName("foo/bar"));
        Assert.IsFalse(SysFs.IsSafeName("foo\\bar"));
        Assert.IsFalse(SysFs.IsSafeName("foo\0bar"));
    }

    [TestMethod]
    public void Fields_SplitsOnSpacesTabsAndNewlines()
    {
        string line = "  cpu0   123\t456   789\r";
        string[] fields = SysFs.Fields(line);

        Assert.AreEqual(4, fields.Length);
        Assert.AreEqual("cpu0", fields[0]);
        Assert.AreEqual("123", fields[1]);
        Assert.AreEqual("456", fields[2]);
        Assert.AreEqual("789", fields[3]);
    }
}
