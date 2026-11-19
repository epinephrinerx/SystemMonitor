using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class TrayTextTests
{
    [TestMethod]
    public void TrayText_AppName_IsSysMonitor()
    {
        Assert.AreEqual("SysMonitor", TrayText.AppName);
    }

    [TestMethod]
    public void TrayText_NullSnapshot_ReturnsBareAppName()
    {
        string text = TrayText.Build(null);
        Assert.AreEqual("SysMonitor", text);
    }

    [TestMethod]
    public void TrayText_SnapshotNotReady_ReturnsBareAppName()
    {
        var snapshot = new Snapshot
        {
            Ready = false,
            CpuTotal = 50,
            Ram = new Ram { Usage = 40 }
        };

        string text = TrayText.Build(snapshot);
        Assert.AreEqual("SysMonitor", text);
    }

    [TestMethod]
    public void TrayText_ReadySnapshot_WithoutTemperature_ShowsCpuAndRamPercentages()
    {
        var snapshot = new Snapshot
        {
            Ready = true,
            CpuTotal = 15,
            Ram = new Ram { Usage = 62 },
            CpuTemp = null
        };

        string text = TrayText.Build(snapshot);
        Assert.AreEqual("SysMonitor\nCPU 15%  ·  RAM 62%", text);
    }

    [TestMethod]
    public void TrayText_ReadySnapshot_WithMeasuredTemperature_ShowsTemperatureSuffix()
    {
        var snapshot = new Snapshot
        {
            Ready = true,
            CpuTotal = 25,
            Ram = new Ram { Usage = 48 },
            CpuTemp = 58,
            CpuTempEstimated = false
        };

        string text = TrayText.Build(snapshot);
        Assert.AreEqual("SysMonitor\nCPU 25%  ·  RAM 48%  ·  58°C", text);
    }

    [TestMethod]
    public void TrayText_ReadySnapshot_WithEstimatedTemperature_ShowsTildePrefix()
    {
        var snapshot = new Snapshot
        {
            Ready = true,
            CpuTotal = 40,
            Ram = new Ram { Usage = 70 },
            CpuTemp = 65,
            CpuTempEstimated = true
        };

        string text = TrayText.Build(snapshot);
        Assert.AreEqual("SysMonitor\nCPU 40%  ·  RAM 70%  ·  ~65°C", text);
    }

    [TestMethod]
    public void TrayText_SupportsCustomLanguage()
    {
        var lang = new Lang("th");
        var snapshot = new Snapshot
        {
            Ready = true,
            CpuTotal = 30,
            Ram = new Ram { Usage = 50 },
            CpuTemp = 52,
            CpuTempEstimated = false
        };

        string text = TrayText.Build(snapshot, lang);
        Assert.IsTrue(text.StartsWith("SysMonitor\n"));
        Assert.IsTrue(text.Contains("30%"));
        Assert.IsTrue(text.Contains("50%"));
        Assert.IsTrue(text.EndsWith("52°C"));
    }
}
