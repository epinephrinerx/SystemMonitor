using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class DrmFdinfoTests
{
    private const string SampleFdinfo =
        "pos:\t0\n" +
        "flags:\t02400002\n" +
        "mnt_id:\t15\n" +
        "ino:\t803\n" +
        "drm-driver:\ti915\n" +
        "drm-pdev:\t0000:00:02.0\n" +
        "drm-client-id:\t42\n" +
        "drm-engine-render:\t1500000000 ns\n" +
        "drm-engine-copy:\t250000000 ns\n" +
        "drm-engine-video:\t0 ns\n";

    [TestMethod]
    public void Parse_ReadsClientIdPciDeviceAndEngines()
    {
        DrmClient? client = DrmFdinfo.Parse(SampleFdinfo);

        Assert.IsNotNull(client);
        Assert.AreEqual(42L, client.ClientId);
        Assert.AreEqual("0000:00:02.0", client.PciDevice);

        Assert.AreEqual(3, client.Engines.Count);
        Assert.AreEqual(1500000000L, client.Engines["render"]);
        Assert.AreEqual(250000000L, client.Engines["copy"]);
        Assert.AreEqual(0L, client.Engines["video"]);
    }

    [TestMethod]
    public void Parse_WhenPdevOrClientIdMissing_ReturnsNull()
    {
        string withoutPdev =
            "drm-client-id:\t42\n" +
            "drm-engine-render:\t1000 ns\n";
        Assert.IsNull(DrmFdinfo.Parse(withoutPdev));

        string withoutClient =
            "drm-pdev:\t0000:00:02.0\n" +
            "drm-engine-render:\t1000 ns\n";
        Assert.IsNull(DrmFdinfo.Parse(withoutClient));
    }
}
