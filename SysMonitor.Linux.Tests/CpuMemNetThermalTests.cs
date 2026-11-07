using System;
using System.Globalization;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Sensors;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public class CpuMemNetThermalTests
{
    private const double Delta = 0.000000001;
    private const string Headers = "Inter-| Receive | Transmit\n face |bytes packets errs drop fifo frame compressed multicast|bytes packets errs drop fifo colls carrier compressed\n";

    [TestMethod]
    public void NumericParsing_UsesInvariantCultureRatherThanCurrentPositiveSign()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.PositiveSign = "p";
            CultureInfo.CurrentCulture = culture;
            var cpu = ProcStat.ParseTotal("cpu +1 +2 +3 +4 +5 +6 +7 +8");
            var ram = MemInfo.Parse("MemTotal: +1048576 kB\nMemAvailable: +262144 kB");
            var net = NetDev.Parse("eth0: +100 0 0 0 0 0 0 0 +200");
            // oracle: invariant '+' parsing gives Busy 27/Total 36, memory .75 of 1 GiB (75%), and network 100/200 bytes.
            Assert.AreEqual(new CpuTimes(27, 36), cpu);
            Assert.AreEqual(1.0, ram.TotalGb, Delta);
            Assert.AreEqual(0.75, ram.UsedGb, Delta);
            Assert.AreEqual(75, ram.Usage);
            Assert.AreEqual(1, net.Count);
            Assert.AreEqual(new NetCounters("eth0", 100, 200), net[0]);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestMethod]
    public void Cores_CountersBeyondIntRangeRemainLongs()
    {
        var cores = ProcStat.ParseCores("cpu0 2147483648 0 0 2147483648");
        // oracle: 2147483648 is int.MaxValue+1; Busy = 2147483648 and Total = 4294967296 fit long.
        Assert.AreEqual(1, cores.Count);
        Assert.AreEqual(new CpuTimes(2147483648L, 4294967296L), cores[0]);
    }

    [TestMethod]
    public void Cores_IncludeStealAndExcludeBothGuestColumns()
    {
        var cores = ProcStat.ParseCores("cpu0 1 2 3 4 5 6 7 8 9 10");
        // oracle: Busy = 1+2+3+6+7+8 = 27; Total = 27+4+5 = 36; guests add nothing.
        Assert.AreEqual(1, cores.Count);
        Assert.AreEqual(new CpuTimes(27, 36), cores[0]);
    }

    [TestMethod]
    public void Cores_PreserveAppearanceOrderAndExcludeAggregate()
    {
        var cores = ProcStat.ParseCores("cpu 900 0 0 100\ncpu12 12\ncpu0 3\ncpu2 7");
        // oracle: aggregate is excluded; cpu12, cpu0, cpu2 remain in appearance order.
        Assert.AreEqual(3, cores.Count);
        Assert.AreEqual(new CpuTimes(12, 12), cores[0]);
        Assert.AreEqual(new CpuTimes(3, 3), cores[1]);
        Assert.AreEqual(new CpuTimes(7, 7), cores[2]);
    }

    [TestMethod]
    public void Cores_MissingFieldsAreZero()
    {
        var cores = ProcStat.ParseCores("cpu0 7 2 1 10");
        // oracle: Busy = 7+2+1 = 10; Total = 10+10 = 20; absent fields are zero.
        Assert.AreEqual(new CpuTimes(10, 20), cores[0]);
    }

    [TestMethod]
    public void Cores_UnparsableFieldsKeepTheirPositionsAsZero()
    {
        var cores = ProcStat.ParseCores("cpu0 2 bad 3 10 bad 4 5 bad 999 999");
        // oracle: Busy = 2+0+3+4+5+0 = 14; Total = 14+10+0 = 24.
        Assert.AreEqual(new CpuTimes(14, 24), cores[0]);
    }

    [TestMethod]
    public void Cores_LabelWithoutNumbersStillProducesZeroTimes()
    {
        var cores = ProcStat.ParseCores("cpu1");
        // oracle: cpu1 is a core even with no numeric fields, all of which default to zero.
        Assert.AreEqual(1, cores.Count);
        Assert.AreEqual(new CpuTimes(0, 0), cores[0]);
    }

    [TestMethod]
    public void Cores_RejectNonAsciiAndMixedSuffixes()
    {
        var cores = ProcStat.ParseCores("cpufoo 1\ncpu_x 2\ncpu1x 3\ncpu0foo 4\ncpu\u0663 5\ncpu-1 6\ncpu01 7");
        // oracle: only cpu followed entirely by one or more ASCII digits qualifies; cpu01 qualifies.
        Assert.AreEqual(1, cores.Count);
        Assert.AreEqual(new CpuTimes(7, 7), cores[0]);
    }

    [TestMethod]
    public void Cores_WhitespaceSeparatesFieldsAndOtherRecordsAreSkipped()
    {
        var cores = ProcStat.ParseCores("intr 100\n\ncpu0\t4  2\t3 11\nctxt 6");
        // oracle: Busy = 4+2+3 = 9; Total = 9+11 = 20; intr/ctxt are not cores.
        Assert.AreEqual(1, cores.Count);
        Assert.AreEqual(new CpuTimes(9, 20), cores[0]);
    }

    [TestMethod]
    public void Cores_EmptyInputReturnsEmpty()
    {
        // oracle: empty input has no core lines.
        Assert.AreEqual(0, ProcStat.ParseCores("").Count);
    }

    [TestMethod]
    public void Cores_NullInputReturnsEmpty()
    {
        // oracle: malformed/empty input never throws and yields an empty collection.
        Assert.AreEqual(0, ProcStat.ParseCores(null!).Count);
    }

    [TestMethod]
    public void Total_SelectsAggregateWithMultipleSpaces()
    {
        var total = ProcStat.ParseTotal("cpu0 999\ncpu   10 20 30 40 50 60 70 80 900 1000\ncpu1 888");
        // oracle: aggregate Busy = 10+20+30+60+70+80 = 270; Total = 270+40+50 = 360.
        Assert.AreEqual(new CpuTimes(270, 360), total);
    }

    [TestMethod]
    public void Total_AbsentAggregateDoesNotUseACore()
    {
        // oracle: an absent aggregate yields default CpuTimes, even when cores exist.
        Assert.AreEqual(default(CpuTimes), ProcStat.ParseTotal("cpu0 50 0 0 50\ncpufoo 90"));
    }

    [TestMethod]
    public void Total_MissingAndInvalidFieldsAreZero()
    {
        // oracle: Busy = 5+0+2 = 7; Total = 7+9 = 16, with missing later fields zero.
        Assert.AreEqual(new CpuTimes(7, 16), ProcStat.ParseTotal("cpu 5 bad 2 9"));
    }

    [TestMethod]
    public void Total_EmptyInputReturnsDefault()
    {
        // oracle: empty input has no aggregate.
        Assert.AreEqual(default(CpuTimes), ProcStat.ParseTotal(""));
    }

    [TestMethod]
    public void Total_NullInputReturnsDefault()
    {
        // oracle: null malformed input never throws and has no aggregate.
        Assert.AreEqual(default(CpuTimes), ProcStat.ParseTotal(null!));
    }

    [TestMethod]
    public void Usage_UsesCounterDeltas()
    {
        // oracle: (160-100)*100/(300-200) = 60 percent.
        Assert.AreEqual(60, ProcStat.Usage(new CpuTimes(100, 200), new CpuTimes(160, 300)));
    }

    [TestMethod]
    public void Usage_RoundsFractionBelowHalfDown()
    {
        // oracle: 1*100/3 = 33.333..., nearest integer 33.
        Assert.AreEqual(33, ProcStat.Usage(default, new CpuTimes(1, 3)));
    }

    [TestMethod]
    public void Usage_RoundsFractionAboveHalfUp()
    {
        // oracle: 2*100/3 = 66.666..., nearest integer 67.
        Assert.AreEqual(67, ProcStat.Usage(default, new CpuTimes(2, 3)));
    }

    [TestMethod]
    public void Usage_ZeroTotalDeltaReturnsZero()
    {
        // oracle: Total delta = 100-100 = 0, so usage is zero despite Busy growth.
        Assert.AreEqual(0, ProcStat.Usage(new CpuTimes(10, 100), new CpuTimes(20, 100)));
    }

    [TestMethod]
    public void Usage_NegativeTotalDeltaReturnsZero()
    {
        // oracle: Total delta = 90-100 = -10 <= 0.
        Assert.AreEqual(0, ProcStat.Usage(new CpuTimes(10, 100), new CpuTimes(20, 90)));
    }

    [TestMethod]
    public void Usage_NegativeBusyDeltaReturnsZero()
    {
        // oracle: Busy delta = 10-20 = -10; Total delta = 100; negative Busy yields zero.
        Assert.AreEqual(0, ProcStat.Usage(new CpuTimes(20, 100), new CpuTimes(10, 200)));
    }

    [TestMethod]
    public void Usage_ClampsAboveOneHundred()
    {
        // oracle: 150*100/100 = 150, clamped to 100.
        Assert.AreEqual(100, ProcStat.Usage(default, new CpuTimes(150, 100)));
    }

    [TestMethod]
    public void Usage_UnchangedBusyWithPositiveElapsedTimeIsZero()
    {
        // oracle: (30-30)*100/(200-100) = 0.
        Assert.AreEqual(0, ProcStat.Usage(new CpuTimes(30, 100), new CpuTimes(30, 200)));
    }

    [TestMethod]
    public void Memory_AvailableDeterminesUsageAndBinaryGigabytes()
    {
        var ram = MemInfo.Parse("MemTotal: 8388608 kB\nMemAvailable: 2097152 kB\nMemFree: 1 kB\nBuffers: 2 kB\nCached: 3 kB");
        // oracle: Total = 8388608/1048576 = 8 GiB; Used = (8388608-2097152)/1048576 = 6 GiB; Usage = 75.
        Assert.AreEqual(8.0, ram.TotalGb, Delta);
        Assert.AreEqual(6.0, ram.UsedGb, Delta);
        Assert.AreEqual(75, ram.Usage);
    }

    [TestMethod]
    public void Memory_PresentZeroAvailableDoesNotUseFallback()
    {
        var ram = MemInfo.Parse("MemTotal: 1048576 kB\nMemAvailable: 0 kB\nMemFree: 500000 kB\nBuffers: 200000 kB\nCached: 200000 kB");
        // oracle: present MemAvailable = 0 overrides fallback; Used = 1048576 kB = 1 GiB, Usage = 100.
        Assert.AreEqual(1.0, ram.UsedGb, Delta);
        Assert.AreEqual(100, ram.Usage);
    }

    [TestMethod]
    public void Memory_MissingAvailableUsesAllThreeFallbackTerms()
    {
        var ram = MemInfo.Parse("MemTotal: 4194304 kB\nMemFree: 524288 kB\nBuffers: 262144 kB\nCached: 262144 kB");
        // oracle: available = 524288+262144+262144 = 1048576; Used = 3145728 kB = 3 GiB; Usage = 75.
        Assert.AreEqual(4.0, ram.TotalGb, Delta);
        Assert.AreEqual(3.0, ram.UsedGb, Delta);
        Assert.AreEqual(75, ram.Usage);
    }

    [TestMethod]
    public void Memory_SwapCachedCannotMasqueradeAsCached()
    {
        var ram = MemInfo.Parse("MemTotal: 1048576 kB\nMemFree: 131072 kB\nBuffers: 131072 kB\nSwapCached: 524288 kB");
        // oracle: Cached is absent and SwapCached is excluded; available = 131072+131072 = 262144; Used = .75 GiB, Usage = 75.
        Assert.AreEqual(0.75, ram.UsedGb, Delta);
        Assert.AreEqual(75, ram.Usage);
    }

    [TestMethod]
    public void Memory_MissingFallbackComponentsAreZero()
    {
        var ram = MemInfo.Parse("MemTotal: 1048576 kB\nMemFree: 262144 kB");
        // oracle: absent Buffers/Cached contribute zero; Used = 1048576-262144 = .75 GiB, Usage = 75.
        Assert.AreEqual(0.75, ram.UsedGb, Delta);
        Assert.AreEqual(75, ram.Usage);
    }

    [TestMethod]
    public void Memory_AvailableAboveTotalClampsUsedToZero()
    {
        var ram = MemInfo.Parse("MemTotal: 1048576 kB\nMemAvailable: 1048577 kB");
        // oracle: raw Used = -1 kB, clamped to zero; total remains 1 GiB.
        Assert.AreEqual(1.0, ram.TotalGb, Delta);
        Assert.AreEqual(0.0, ram.UsedGb, Delta);
        Assert.AreEqual(0, ram.Usage);
    }

    [TestMethod]
    public void Memory_NegativeAvailableCannotMakeUsedExceedTotal()
    {
        var ram = MemInfo.Parse("MemTotal: 1048576 kB\nMemAvailable: -1 kB");
        // oracle: Used is clamped to [0, MemTotal], hence at most 1 GiB and Usage 100.
        Assert.AreEqual(1.0, ram.UsedGb, Delta);
        Assert.AreEqual(100, ram.Usage);
    }

    [TestMethod]
    public void Memory_UsageRoundsFractionalPercentage()
    {
        var ram = MemInfo.Parse("MemTotal: 3000 kB\nMemAvailable: 1000 kB");
        // oracle: Used = 2000 kB; Usage = round(2000*100/3000) = 67, preserving fractional GiB.
        Assert.AreEqual(67, ram.Usage);
        Assert.AreEqual(2000.0 / 1048576.0, ram.UsedGb, Delta);
        Assert.AreEqual(3000.0 / 1048576.0, ram.TotalGb, Delta);
    }

    [TestMethod]
    public void Memory_MissingTotalReturnsDefaultRam()
    {
        var ram = MemInfo.Parse("MemAvailable: 500 kB\nMemFree: 10 kB");
        // oracle: absent MemTotal returns new Ram(), with all three numeric properties zero.
        Assert.AreEqual(0, ram.Usage);
        Assert.AreEqual(0.0, ram.UsedGb, Delta);
        Assert.AreEqual(0.0, ram.TotalGb, Delta);
    }

    [TestMethod]
    public void Memory_ZeroTotalReturnsDefaultRam()
    {
        var ram = MemInfo.Parse("MemTotal: 0 kB\nMemAvailable: 1 kB");
        // oracle: zero MemTotal returns new Ram(), without dividing by zero.
        Assert.AreEqual(0, ram.Usage);
        Assert.AreEqual(0.0, ram.UsedGb, Delta);
        Assert.AreEqual(0.0, ram.TotalGb, Delta);
    }

    [TestMethod]
    public void Memory_NonnumericTotalReturnsDefaultRam()
    {
        var ram = MemInfo.Parse("MemTotal: abc kB\nMemAvailable: 1 kB");
        // oracle: the explicit nonnumeric MemTotal clarification requires new Ram().
        Assert.AreEqual(0, ram.Usage);
        Assert.AreEqual(0.0, ram.UsedGb, Delta);
        Assert.AreEqual(0.0, ram.TotalGb, Delta);
    }

    [TestMethod]
    public void Memory_EmptyInputReturnsDefaultRam()
    {
        var ram = MemInfo.Parse("");
        // oracle: empty input has no MemTotal and returns zero properties.
        Assert.AreEqual(0, ram.Usage);
        Assert.AreEqual(0.0, ram.UsedGb, Delta);
        Assert.AreEqual(0.0, ram.TotalGb, Delta);
    }

    [TestMethod]
    public void Memory_NullInputReturnsDefaultRam()
    {
        var ram = MemInfo.Parse(null!);
        // oracle: null input never throws; absent MemTotal yields zero properties.
        Assert.AreEqual(0, ram.Usage);
        Assert.AreEqual(0.0, ram.UsedGb, Delta);
        Assert.AreEqual(0.0, ram.TotalGb, Delta);
    }

    [TestMethod]
    public void Network_ParseTrimsNamesPreservesOrderAndSkipsLoopback()
    {
        var rows = NetDev.Parse(Headers + " lo: 9 0 0 0 0 0 0 0 10\n  eth2 : 1000 11 12 13 14 15 16 17 2000 19\nwlan0:3000 21 22 23 24 25 26 27 4000");
        // oracle: lo and headers are omitted; bytes are fields 1 and 9; interface order is eth2 then wlan0.
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(new NetCounters("eth2", 1000, 2000), rows[0]);
        Assert.AreEqual(new NetCounters("wlan0", 3000, 4000), rows[1]);
    }

    [TestMethod]
    public void Network_DataDoesNotRequireHeaders()
    {
        var rows = NetDev.Parse("eth0: 1 0 0 0 0 0 0 0 2");
        // oracle: a colon data line with nine valid fields qualifies; headerless data is not discarded.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new NetCounters("eth0", 1, 2), rows[0]);
    }

    [TestMethod]
    public void Network_UsesFirstColonInsteadOfLaterColon()
    {
        var rows = NetDev.Parse("eth0:alias: 1 0 0 0 0 0 0 0 2");
        // oracle: first-colon splitting makes field 1 'alias:', which is not an integer, so skip.
        Assert.AreEqual(0, rows.Count);
    }

    [TestMethod]
    public void Network_WhitespaceFieldsAndZeroCountersAreValid()
    {
        var rows = NetDev.Parse("eth0:\t0  1\t2 3 4 5 6 7\t0");
        // oracle: whitespace separates numbers; zero is a valid non-negative integer in both byte columns.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new NetCounters("eth0", 0, 0), rows[0]);
    }

    [TestMethod]
    public void Network_LineWithoutColonIsSkipped()
    {
        // oracle: a colon is mandatory, even when a line otherwise resembles valid data.
        Assert.AreEqual(0, NetDev.Parse("eth0 1 0 0 0 0 0 0 0 2").Count);
    }

    [TestMethod]
    public void Network_EightFieldsAreInsufficient()
    {
        // oracle: eight fields are just below the required minimum of nine.
        Assert.AreEqual(0, NetDev.Parse("eth0: 1 2 3 4 5 6 7 8").Count);
    }

    [TestMethod]
    public void Network_InvalidReceiveIsSkippedWithoutDroppingNextInterface()
    {
        var rows = NetDev.Parse("bad0: abc 0 0 0 0 0 0 0 5\ngood0: 7 0 0 0 0 0 0 0 8");
        // oracle: bad0 has nonnumeric receive bytes; good0 has valid receive/send fields 7 and 8.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new NetCounters("good0", 7, 8), rows[0]);
    }

    [TestMethod]
    public void Network_InvalidTransmitIsSkipped()
    {
        // oracle: ninth field must be an integer; 'abc' is invalid.
        Assert.AreEqual(0, NetDev.Parse("eth0: 1 0 0 0 0 0 0 0 abc").Count);
    }

    [TestMethod]
    public void Network_NegativeReceiveIsSkipped()
    {
        // oracle: -1 is just below the non-negative receive boundary.
        Assert.AreEqual(0, NetDev.Parse("eth0: -1 0 0 0 0 0 0 0 2").Count);
    }

    [TestMethod]
    public void Network_NegativeTransmitIsSkipped()
    {
        // oracle: -1 is just below the non-negative transmit boundary.
        Assert.AreEqual(0, NetDev.Parse("eth0: 1 0 0 0 0 0 0 0 -1").Count);
    }

    [TestMethod]
    public void Network_EmptyInputReturnsEmpty()
    {
        // oracle: empty input contains no data lines.
        Assert.AreEqual(0, NetDev.Parse("").Count);
    }

    [TestMethod]
    public void Network_ByteCountersBeyondIntRangeRemainLongs()
    {
        var rows = NetDev.Parse("eth0: 2147483648 0 0 0 0 0 0 0 4294967296");
        // oracle: receive int.MaxValue+1 and transmit 2*(int.MaxValue+1) are valid non-negative long counters.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new NetCounters("eth0", 2147483648L, 4294967296L), rows[0]);
    }

    [TestMethod]
    public void Network_NullInputReturnsEmpty()
    {
        // oracle: null malformed input never throws and produces an empty collection.
        Assert.AreEqual(0, NetDev.Parse(null!).Count);
    }

    [TestMethod]
    public void Rate_DifferencesBytesAndDividesBySecondsAndMebibytes()
    {
        var rate = NetDev.Rate(new NetCounters("eth0", 100, 200), new NetCounters("eth0", 3145828, 1048776), 2);
        // oracle: Down = (3145828-100)/1048576/2 = 1.5; Up = (1048776-200)/1048576/2 = .5 MiB/s.
        Assert.AreEqual(1.5, rate.DownMb, Delta);
        Assert.AreEqual(0.5, rate.UpMb, Delta);
    }

    [TestMethod]
    public void Rate_ZeroSecondsReturnsBothZero()
    {
        var rate = NetDev.Rate(new NetCounters("e", 0, 0), new NetCounters("e", 1048576, 2097152), 0);
        // oracle: seconds <= 0 forces both rates to zero regardless of positive deltas.
        Assert.AreEqual(0.0, rate.DownMb, Delta);
        Assert.AreEqual(0.0, rate.UpMb, Delta);
    }

    [TestMethod]
    public void Rate_NegativeSecondsReturnsBothZero()
    {
        var rate = NetDev.Rate(new NetCounters("e", 0, 0), new NetCounters("e", 1048576, 2097152), -0.001);
        // oracle: -0.001 <= 0, so both rates are zero.
        Assert.AreEqual(0.0, rate.DownMb, Delta);
        Assert.AreEqual(0.0, rate.UpMb, Delta);
    }

    [TestMethod]
    public void Rate_ReceiveResetDoesNotSuppressTransmit()
    {
        var rate = NetDev.Rate(new NetCounters("e", 100, 200), new NetCounters("e", 99, 1048776), 0.5);
        // oracle: receive delta -1 => 0; transmit delta 1048576/.5/1048576 = 2 MiB/s.
        Assert.AreEqual(0.0, rate.DownMb, Delta);
        Assert.AreEqual(2.0, rate.UpMb, Delta);
    }

    [TestMethod]
    public void Rate_TransmitResetDoesNotSuppressReceive()
    {
        var rate = NetDev.Rate(new NetCounters("e", 100, 200), new NetCounters("e", 2097252, 199), 1);
        // oracle: receive delta 2097152/1048576 = 2; transmit delta -1 => 0.
        Assert.AreEqual(2.0, rate.DownMb, Delta);
        Assert.AreEqual(0.0, rate.UpMb, Delta);
    }

    [TestMethod]
    public void Rate_UnchangedCountersReturnZero()
    {
        var rate = NetDev.Rate(new NetCounters("e", 99, 88), new NetCounters("e", 99, 88), 1);
        // oracle: both byte deltas are zero, so both rates are zero.
        Assert.AreEqual(0.0, rate.DownMb, Delta);
        Assert.AreEqual(0.0, rate.UpMb, Delta);
    }

    [TestMethod]
    public void Wireless_RequiresWirelessDirectoryUnderSelectedInterface()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "wlan0", "wireless"));
            // oracle: <root>/wlan0/wireless is a directory, so wlan0 is wireless.
            Assert.IsTrue(NetDev.IsWireless(root, "wlan0"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Wireless_OtherInterfacesDirectoryDoesNotQualify()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "wlan0", "wireless"));
            Directory.CreateDirectory(Path.Combine(root, "eth0"));
            // oracle: only wlan0 has wireless; the requested eth0 lacks it.
            Assert.IsFalse(NetDev.IsWireless(root, "eth0"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Wireless_FileNamedWirelessIsNotADirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "eth0"));
            File.WriteAllText(Path.Combine(root, "eth0", "wireless"), "present");
            // oracle: a file does not satisfy the required wireless directory existence.
            Assert.IsFalse(NetDev.IsWireless(root, "eth0"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Wireless_MissingRootReturnsFalse()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            // oracle: a missing root cannot contain the requested wireless directory.
            Assert.IsFalse(NetDev.IsWireless(root, "wlan0"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Wireless_NullRootOrNameReturnsFalseWithoutThrowing()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            // oracle: null root/name is malformed input, never throws, and identifies no existing wireless directory.
            Assert.IsFalse(NetDev.IsWireless(null!, "wlan0"));
            Assert.IsFalse(NetDev.IsWireless(root, null!));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void CheckPreferredName(string name)
    {
        var picked = HwmonThermal.PickCpu(new[] { ("acpitz", 90000), (name, 42600), ("board", 110000) });
        // oracle: recognized CPU name wins despite being neither first nor hottest; 42.6 rounds to 43.
        Assert.AreEqual((int?)43, picked);
    }

    [TestMethod]
    public void Thermal_CoretempPreferenceIsCaseInsensitive() { CheckPreferredName("CoReTeMp"); }

    [TestMethod]
    public void Thermal_K10tempPreferenceIsCaseInsensitive() { CheckPreferredName("K10TEMP"); }

    [TestMethod]
    public void Thermal_ZenpowerPreferenceIsCaseInsensitive() { CheckPreferredName("ZenPower"); }

    [TestMethod]
    public void Thermal_X86PackagePreferenceIsCaseInsensitive() { CheckPreferredName("X86_PKG_TEMP"); }

    [TestMethod]
    public void Thermal_CpuUnderscorePreferenceIsCaseInsensitive() { CheckPreferredName("CPU_THERMAL"); }

    [TestMethod]
    public void Thermal_CpuHyphenPreferenceIsCaseInsensitive() { CheckPreferredName("Cpu-Thermal"); }

    [TestMethod]
    public void Thermal_FirstValidPreferredWinsOverLaterPreferred()
    {
        var picked = HwmonThermal.PickCpu(new[] { ("board", 100000), ("zenpower", 41400), ("coretemp", 80000) });
        // oracle: zenpower is the first valid preferred zone; 41.4 rounds to 41, regardless of later preferred heat/name.
        Assert.AreEqual((int?)41, picked);
    }

    [TestMethod]
    public void Thermal_WithoutPreferredNamesChoosesHottest()
    {
        var picked = HwmonThermal.PickCpu(new[] { ("board", 35400), ("acpitz", 62600), ("other", 50100) });
        // oracle: maximum valid reading is 62.6 C, rounded to 63.
        Assert.AreEqual((int?)63, picked);
    }

    [TestMethod]
    public void Thermal_FallbackExcludesInvalidHotterAndColderReadings()
    {
        var picked = HwmonThermal.PickCpu(new[] { ("hot", 130001), ("board", 62400), ("cold", -20001), ("other", 50000) });
        // oracle: 130001 and -20001 are invalid; hottest remaining is 62.4 C, rounded to 62.
        Assert.AreEqual((int?)62, picked);
    }

    [TestMethod]
    public void Thermal_PreferenceRequiresExactName()
    {
        var picked = HwmonThermal.PickCpu(new[] { ("coretemp_extra", 41000), ("board", 70000) });
        // oracle: coretemp_extra is not an exact preferred name; hottest valid zone is board at 70 C.
        Assert.AreEqual((int?)70, picked);
    }

    [TestMethod]
    public void Thermal_InvalidPreferredZoneDoesNotBlockValidPreferred()
    {
        var picked = HwmonThermal.PickCpu(new[] { ("coretemp", 130000), ("board", 95000), ("k10temp", 44400) });
        // oracle: 130000 is invalid; k10temp is the first valid preferred zone; 44.4 rounds to 44.
        Assert.AreEqual((int?)44, picked);
    }

    [TestMethod]
    public void Thermal_LowerBoundaryJustInsideIsValid()
    {
        // oracle: -19999 > -20000, so valid; -19.999 C rounds to -20 C.
        Assert.AreEqual((int?)(-20), HwmonThermal.PickCpu(new[] { ("board", -19999) }));
    }

    [TestMethod]
    public void Thermal_UpperBoundaryJustInsideIsValid()
    {
        // oracle: 129999 < 130000, so valid; 129.999 C rounds to 130 C after validation.
        Assert.AreEqual((int?)130, HwmonThermal.PickCpu(new[] { ("board", 129999) }));
    }

    [TestMethod]
    public void Thermal_ExactAndJustOutsideBoundariesAreInvalid()
    {
        var picked = HwmonThermal.PickCpu(new[] { ("a", -20001), ("b", -20000), ("c", 130000), ("d", 130001) });
        // oracle: valid interval is strictly (-20000,130000); all four readings fall outside it.
        Assert.IsNull(picked);
    }

    [TestMethod]
    public void Thermal_EmptyZonesReturnNull()
    {
        // oracle: no zones means no valid readings.
        Assert.IsNull(HwmonThermal.PickCpu(Array.Empty<(string Name, int MilliCelsius)>()));
    }

    [TestMethod]
    public void Thermal_NullZonesReturnNull()
    {
        // oracle: null malformed input never throws and has no valid zones.
        Assert.IsNull(HwmonThermal.PickCpu(null!));
    }

    [TestMethod]
    public void Cores_CrlfAndFinalCarriageReturnPreserveCounters()
    {
        var cores = ProcStat.ParseCores("cpu0 1 2 3 4 5 6 7 8\r\ncpu1 9 0 0 11\r");
        // oracle: trailing CR is ignored; cpu0 Busy=27, Total=36; cpu1 Busy=9, Total=20, in appearance order.
        Assert.AreEqual(2, cores.Count);
        Assert.AreEqual(new CpuTimes(27, 36), cores[0]);
        Assert.AreEqual(new CpuTimes(9, 20), cores[1]);
    }

    [TestMethod]
    public void Total_TabsAndTrailingCarriageReturnDelimitAggregateFields()
    {
        var total = ProcStat.ParseTotal("cpu0 99\r\ncpu\t 2\t3  4\t11\t5 6\t7 8\r");
        // oracle: any run of spaces/tabs separates fields and CR is ignored; Busy=2+3+4+6+7+8=30; Total=30+11+5=46.
        Assert.AreEqual(new CpuTimes(30, 46), total);
    }

    [TestMethod]
    public void Cpu_WhitespaceOnlyLinesAreSkippedByBothParsers()
    {
        const string whitespace = " \t \r\n\t\n   \r";
        // oracle: whitespace-only lines contain neither cores nor an aggregate and yield empty/default results.
        Assert.AreEqual(0, ProcStat.ParseCores(whitespace).Count);
        Assert.AreEqual(default(CpuTimes), ProcStat.ParseTotal(whitespace));
        var cores = ProcStat.ParseCores(whitespace + "\ncpu2 6 0 0 14\n" + whitespace);
        var total = ProcStat.ParseTotal(whitespace + "\ncpu 8 0 0 12\n" + whitespace);
        // oracle: skipped whitespace cannot hide valid records; core Busy=6/Total=20; aggregate Busy=8/Total=20.
        Assert.AreEqual(1, cores.Count);
        Assert.AreEqual(new CpuTimes(6, 20), cores[0]);
        Assert.AreEqual(new CpuTimes(8, 20), total);
    }

    [TestMethod]
    public void Memory_TabsSeparateTotalAndAvailableValues()
    {
        var ram = MemInfo.Parse("MemTotal:\t1048576 kB\nMemAvailable:\t  262144\t kB");
        // oracle: tab/mixed whitespace fields parse; total=1 GiB, used=(1048576-262144)/1048576=.75 GiB, Usage=75.
        Assert.AreEqual(1.0, ram.TotalGb, Delta);
        Assert.AreEqual(0.75, ram.UsedGb, Delta);
        Assert.AreEqual(75, ram.Usage);
    }

    [TestMethod]
    public void Memory_TabsSeparateEveryFallbackComponent()
    {
        var ram = MemInfo.Parse("MemTotal: 2097152 kB\nMemFree:\t262144 kB\nBuffers:\t 131072\tkB\nCached: \t131072 kB");
        // oracle: absent MemAvailable uses tab-separated 262144+131072+131072=524288; total=2 GiB, used=1.5 GiB, Usage=75.
        Assert.AreEqual(2.0, ram.TotalGb, Delta);
        Assert.AreEqual(1.5, ram.UsedGb, Delta);
        Assert.AreEqual(75, ram.Usage);
    }

    [TestMethod]
    public void Memory_CrlfAndFinalCarriageReturnAreIgnored()
    {
        var ram = MemInfo.Parse("MemTotal: 4194304 kB\r\nMemAvailable: 1048576 kB\r");
        // oracle: trailing CR is ignored on each line; total=4 GiB, used=(4194304-1048576)/1048576=3 GiB, Usage=75.
        Assert.AreEqual(4.0, ram.TotalGb, Delta);
        Assert.AreEqual(3.0, ram.UsedGb, Delta);
        Assert.AreEqual(75, ram.Usage);
    }

    [TestMethod]
    public void Memory_MinimumLongAvailableClampsWithoutSubtractionOverflow()
    {
        var ram = MemInfo.Parse("MemTotal: 1048576 kB\nMemAvailable: -9223372036854775808 kB");
        // oracle: round-4(b) clamps mathematical Used=1048576-(-9223372036854775808) to MemTotal; both GiB values=1, Usage=100.
        Assert.AreEqual(1.0, ram.TotalGb, Delta);
        Assert.AreEqual(1.0, ram.UsedGb, Delta);
        Assert.AreEqual(100, ram.Usage);
    }

    [TestMethod]
    public void Memory_FallbackSaturatesWhenFreePlusBuffersOverflows()
    {
        var ram = MemInfo.Parse("MemTotal: 1048576 kB\nMemFree: 9223372036854775807 kB\nBuffers: 1 kB\nCached: 1 kB");
        // oracle: round-4(b) saturates 9223372036854775807+1+1 instead of wrapping; available exceeds total, used=0 GiB, Usage=0.
        Assert.AreEqual(1.0, ram.TotalGb, Delta);
        Assert.AreEqual(0.0, ram.UsedGb, Delta);
        Assert.AreEqual(0, ram.Usage);
    }

    [TestMethod]
    public void Memory_FallbackSaturatesWhenOnlyAddingCachedOverflows()
    {
        var ram = MemInfo.Parse("MemTotal: 2097152 kB\nMemFree: 9223372036854775806 kB\nBuffers: 1 kB\nCached: 1 kB");
        // oracle: Free+Buffers=long.MaxValue fits; adding Cached exceeds it and saturates; total=2 GiB, used=0 GiB, Usage=0.
        Assert.AreEqual(2.0, ram.TotalGb, Delta);
        Assert.AreEqual(0.0, ram.UsedGb, Delta);
        Assert.AreEqual(0, ram.Usage);
    }

    [TestMethod]
    public void Network_CrlfAndFinalCarriageReturnPreserveNinthCounter()
    {
        var rows = NetDev.Parse("eth0: 17 0 0 0 0 0 0 0 29\r\nwlan0:31 0 0 0 0 0 0 0 43\r");
        // oracle: CRLF and final CR are ignored; receive/transmit fields 1/9 give eth0=17/29 and wlan0=31/43.
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(new NetCounters("eth0", 17, 29), rows[0]);
        Assert.AreEqual(new NetCounters("wlan0", 31, 43), rows[1]);
    }

    [TestMethod]
    public void Network_WhitespaceOnlyLinesAreSkippedWithoutHidingData()
    {
        const string whitespace = " \t \r\n\t\n   \r";
        // oracle: round-4(f) skips all whitespace-only network lines, leaving no entries.
        Assert.AreEqual(0, NetDev.Parse(whitespace).Count);
        var rows = NetDev.Parse(whitespace + "\neth7: 5 0 0 0 0 0 0 0 9\n" + whitespace);
        // oracle: intervening whitespace does not hide the sole valid eth7 record with counters 5/9.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new NetCounters("eth7", 5, 9), rows[0]);
    }

    [TestMethod]
    public void Thermal_SurroundingWhitespaceIsTrimmedBeforePreferenceMatching()
    {
        foreach (string name in new[] { "coretemp\n", " \tcoretemp \r\n" })
        {
            var picked = HwmonThermal.PickCpu(new[] { ("acpitz", 90000), (name, 42600), ("board", 110000) });
            // oracle: round-4(d) trims to coretemp; the second, cooler preferred item wins and 42.6 C rounds to 43.
            Assert.AreEqual((int?)43, picked);
        }
    }
}
