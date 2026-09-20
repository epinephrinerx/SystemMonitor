using SysMonitor.Sensors;

namespace SysMonitor.Tests;

[TestClass]
public class IoRateTests
{
    [TestMethod]
    public void A_skipped_interval_is_divided_by_its_own_elapsed_time()
    {
        // The drive was not probed for five minutes. 300 MB accumulated over
        // 300 s is 1 MB/s -- the bug this guards against reported 300 MB/s by
        // dividing by the last global tick instead.
        const long mb = 1024 * 1024;
        (double read, double write) = IoRate.PerSecond(
            previous: (0, 0), previousStamp: 0,
            current: (300 * mb, 0), stamp: 300_000);

        Assert.AreEqual(1.0, read, 0.001);
        Assert.AreEqual(0.0, write, 0.001);
    }

    [TestMethod]
    public void A_normal_interval_reports_the_expected_rate()
    {
        const long mb = 1024 * 1024;
        (double read, double write) = IoRate.PerSecond(
            previous: (10 * mb, 4 * mb), previousStamp: 1_000,
            current: (15 * mb, 6 * mb), stamp: 3_500);

        Assert.AreEqual(2.0, read, 0.001);     // 5 MB over 2.5 s
        Assert.AreEqual(0.8, write, 0.001);    // 2 MB over 2.5 s
    }

    [TestMethod]
    public void Two_samples_in_the_same_millisecond_report_nothing()
    {
        Assert.AreEqual((0.0, 0.0),
            IoRate.PerSecond((0, 0), 5_000, (999_999, 999_999), 5_000));
    }

    [TestMethod]
    public void A_counter_reset_is_not_a_negative_rate()
    {
        // Remounting a volume restarts its counters from zero.
        (double read, double write) = IoRate.PerSecond(
            previous: (900_000, 900_000), previousStamp: 0,
            current: (10, 10), stamp: 1_000);

        Assert.AreEqual(0.0, read, 0.001);
        Assert.AreEqual(0.0, write, 0.001);
    }
}
