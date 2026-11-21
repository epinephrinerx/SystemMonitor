using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class PrivacyTests
{
    [TestMethod]
    public void MaskInterface_EnxMacName_MasksToEllipsis()
    {
        // Lowercase enx with 12 hex digits
        Assert.AreEqual("enx…", Privacy.MaskInterface("enx00e04c68221a"));

        // Uppercase ENX with 12 hex digits
        Assert.AreEqual("enx…", Privacy.MaskInterface("ENX00E04C68221A"));

        // Mixed case
        Assert.AreEqual("enx…", Privacy.MaskInterface("Enx00E04c68221A"));
    }

    [TestMethod]
    public void MaskInterface_NonMacOrShortNames_RemainUnchanged()
    {
        // Too short
        Assert.AreEqual("enx123", Privacy.MaskInterface("enx123"));
        Assert.AreEqual("enx00e04c68221", Privacy.MaskInterface("enx00e04c68221"));

        // Too long
        Assert.AreEqual("enx00e04c68221aa", Privacy.MaskInterface("enx00e04c68221aa"));

        // Non-hex characters
        Assert.AreEqual("enx00e04c68221g", Privacy.MaskInterface("enx00e04c68221g"));

        // Standard ethernet / wifi names
        Assert.AreEqual("wlp0s20f3", Privacy.MaskInterface("wlp0s20f3"));
        Assert.AreEqual("eth0", Privacy.MaskInterface("eth0"));
        Assert.AreEqual("lo", Privacy.MaskInterface("lo"));
    }

    [TestMethod]
    public void MaskInterface_EmptyOrNull_ReturnsEmptyString()
    {
        Assert.AreEqual(string.Empty, Privacy.MaskInterface(string.Empty));
        Assert.AreEqual(string.Empty, Privacy.MaskInterface(null!));
    }

    [TestMethod]
    public void MaskInterfaces_SingleMaskedInterface_StaysBare()
    {
        var input = new[] { "eth0", "enx00e04c68221a", "wlp0s20f3" };
        var expected = new[] { "eth0", "enx…", "wlp0s20f3" };

        IReadOnlyList<string> actual = Privacy.MaskInterfaces(input);

        CollectionAssert.AreEqual(expected, actual.ToArray());
    }

    [TestMethod]
    public void MaskInterfaces_MultipleMaskedInterfaces_AppendsIndexNumber()
    {
        var input = new[] { "enx00e04c68221a", "eth0", "ENX112233445566" };
        var expected = new[] { "enx…#1", "eth0", "enx…#2" };

        IReadOnlyList<string> actual = Privacy.MaskInterfaces(input);

        CollectionAssert.AreEqual(expected, actual.ToArray());
    }

    [TestMethod]
    public void MaskInterfaces_NoMaskedInterfaces_ReturnsAllUnchanged()
    {
        var input = new[] { "eth0", "wlp0s20f3", "lo" };

        IReadOnlyList<string> actual = Privacy.MaskInterfaces(input);

        CollectionAssert.AreEqual(input, actual.ToArray());
    }
}
