using System;
using System.Globalization;
using SysMonitor.Linux.Sensors;

namespace SysMonitor.Linux.Tests;

[TestClass]
public class DiskStatsTests
{
    [TestMethod]
    public void Parse_SevenCounters_UsesSectorsNotOperationCounts()
    {
        var rows = DiskStats.Parse("8 0 sda 91 92 3 94 95 96 5");
        // oracle: sectors are counters 3 and 7, so read=3*512=1536 and write=5*512=2560.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("sda", rows[0].Name);
        Assert.AreEqual(1536L, rows[0].BytesRead);
        Assert.AreEqual(2560L, rows[0].BytesWritten);
    }

    [TestMethod]
    public void Parse_TabsSpacesAndCrLf_AreAccepted()
    {
        var rows = DiskStats.Parse(" \t8\t0  sda\t1 2 4 5 6 7 8\r\n");
        // oracle: arbitrary spaces/tabs separate fields and trailing CR is ignored; 4*512 and 8*512.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(2048L, rows[0].BytesRead);
        Assert.AreEqual(4096L, rows[0].BytesWritten);
    }

    [TestMethod]
    public void Parse_Null_ReturnsEmpty()
    {
        // oracle: null/empty or blank input returns no rows.
        Assert.AreEqual(0, DiskStats.Parse(null!).Count);
    }

    [TestMethod]
    public void Parse_Empty_ReturnsEmpty()
    {
        // oracle: null/empty or blank input returns no rows.
        Assert.AreEqual(0, DiskStats.Parse("").Count);
    }

    [TestMethod]
    public void Parse_WhitespaceOnly_ReturnsEmpty()
    {
        // oracle: null/empty or blank input returns no rows.
        Assert.AreEqual(0, DiskStats.Parse(" \t\r\n\t\n").Count);
    }

    [TestMethod]
    public void Parse_FewerThanThreeFields_ReturnsEmpty()
    {
        // oracle: a valid line needs major, minor, name and seven counters.
        Assert.AreEqual(0, DiskStats.Parse("8 0").Count);
    }

    [TestMethod]
    public void Parse_ThreeFieldsWithoutCounters_ReturnsEmpty()
    {
        // oracle: a valid line needs major, minor, name and seven counters.
        Assert.AreEqual(0, DiskStats.Parse("8 0 sda").Count);
    }

    [TestMethod]
    public void Parse_OnlySixCounters_ReturnsEmpty()
    {
        // oracle: a valid line needs major, minor, name and seven counters.
        Assert.AreEqual(0, DiskStats.Parse("8 0 sda 0 0 1 0 0 0").Count);
    }

    [TestMethod]
    public void Parse_MalformedMajor_ReturnsEmpty()
    {
        // oracle: major and minor must be non-negative integers.
        Assert.AreEqual(0, DiskStats.Parse("x 0 sda 0 0 1 0 0 0 1").Count);
    }

    [TestMethod]
    public void Parse_MalformedMinor_ReturnsEmpty()
    {
        // oracle: major and minor must be non-negative integers.
        Assert.AreEqual(0, DiskStats.Parse("8 x sda 0 0 1 0 0 0 1").Count);
    }

    [TestMethod]
    public void Parse_NegativeMajor_ReturnsEmpty()
    {
        // oracle: major and minor must be non-negative integers.
        Assert.AreEqual(0, DiskStats.Parse("-1 0 sda 0 0 1 0 0 0 1").Count);
    }

    [TestMethod]
    public void Parse_NegativeMinor_ReturnsEmpty()
    {
        // oracle: major and minor must be non-negative integers.
        Assert.AreEqual(0, DiskStats.Parse("8 -1 sda 0 0 1 0 0 0 1").Count);
    }

    [TestMethod]
    public void Parse_EachRequiredCounterRejectsNegative()
    {
        for (int index = 0; index < 7; index++)
        {
            var counters = new[] { "0", "0", "1", "0", "0", "0", "1" };
            counters[index] = "-1";
            // oracle: every one of counters 1..7 must be a non-negative long.
            Assert.AreEqual(0, DiskStats.Parse("8 0 sda " + string.Join(" ", counters)).Count, $"counter {index + 1}");
        }
    }

    [TestMethod]
    public void Parse_EachRequiredCounterRejectsNonNumeric()
    {
        for (int index = 0; index < 7; index++)
        {
            var counters = new[] { "0", "0", "1", "0", "0", "0", "1" };
            counters[index] = "oops";
            // oracle: every one of counters 1..7 must parse as a long, including unused operation counts.
            Assert.AreEqual(0, DiskStats.Parse("8 0 sda " + string.Join(" ", counters)).Count, $"counter {index + 1}");
        }
    }

    [TestMethod]
    public void Parse_EachRequiredCounterRejectsLongOverflow()
    {
        for (int index = 0; index < 7; index++)
        {
            var counters = new[] { "0", "0", "1", "0", "0", "0", "1" };
            counters[index] = "9223372036854775808";
            // oracle: long.MaxValue+1 cannot parse as a long in any required counter.
            Assert.AreEqual(0, DiskStats.Parse("8 0 sda " + string.Join(" ", counters)).Count, $"counter {index + 1}");
        }
    }

    [TestMethod]
    public void Parse_FieldsAfterSeventhCounter_AreIgnored()
    {
        var rows = DiskStats.Parse("8 0 sda 0 0 2 0 0 0 3 garbage -99 9223372036854775808");
        // oracle: fields after counter 7 are ignored; 2*512=1024 and 3*512=1536.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(1024L, rows[0].BytesRead);
        Assert.AreEqual(1536L, rows[0].BytesWritten);
    }

    [TestMethod]
    public void Parse_DuplicateNames_PreserveAllEntriesAndOrder()
    {
        var rows = DiskStats.Parse("8 0 sda 0 0 1 0 0 0 2\n8 0 sda 0 0 3 0 0 0 4");
        // oracle: duplicate names are retained in appearance order, not merged or overwritten.
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(new DiskCounters("sda", 512, 1024), rows[0]);
        Assert.AreEqual(new DiskCounters("sda", 1536, 2048), rows[1]);
    }

    [TestMethod]
    public void Parse_DeviceNames_AreUnfilteredAndUnchanged()
    {
        var rows = DiskStats.Parse("8 1 sda1 0 0 1 0 0 0 0\n259 1 nvme0n1p1 0 0 2 0 0 0 0\n7 0 loop0 0 0 3 0 0 0 0\n253 0 dm-0 0 0 4 0 0 0 0\n8 0 SdA 0 0 5 0 0 0 0");
        // oracle: no name filtering or case normalization occurs and order is preserved.
        Assert.AreEqual(5, rows.Count);
        Assert.AreEqual("sda1", rows[0].Name);
        Assert.AreEqual("nvme0n1p1", rows[1].Name);
        Assert.AreEqual("loop0", rows[2].Name);
        Assert.AreEqual("dm-0", rows[3].Name);
        Assert.AreEqual("SdA", rows[4].Name);
    }

    [TestMethod]
    public void Parse_InvalidLines_DoNotHideValidNeighbors()
    {
        var rows = DiskStats.Parse("8 0 sda 0 0 1 0 0 0 0\ninvalid\n8 16 sdb 0 0 2 0 0 0 0\n\n");
        // oracle: malformed and blank lines are skipped independently.
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("sda", rows[0].Name);
        Assert.AreEqual("sdb", rows[1].Name);
    }

    [TestMethod]
    public void Parse_ZeroSectors_AreZeroBytes()
    {
        var rows = DiskStats.Parse("0 0 zero 0 0 0 0 0 0 0");
        // oracle: major/minor/counters may be zero; 0*512=0.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(0L, rows[0].BytesRead);
        Assert.AreEqual(0L, rows[0].BytesWritten);
    }

    [TestMethod]
    public void Parse_LastNonSaturatingSectorCount_IsExact()
    {
        var rows = DiskStats.Parse("8 0 sda 0 0 18014398509481983 0 0 0 18014398509481983");
        // oracle: floor(long.MaxValue/512)*512 = 9223372036854775296, below long.MaxValue by 511.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(9223372036854775296L, rows[0].BytesRead);
        Assert.AreEqual(9223372036854775296L, rows[0].BytesWritten);
    }

    [TestMethod]
    public void Parse_FirstSaturatingSectorCount_ClampsBothFields()
    {
        var rows = DiskStats.Parse("8 0 sda 0 0 18014398509481984 0 0 0 18014398509481984");
        // oracle: 18014398509481984*512 = 2^63, so both byte counts saturate at long.MaxValue.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(long.MaxValue, rows[0].BytesRead);
        Assert.AreEqual(long.MaxValue, rows[0].BytesWritten);
    }

    [TestMethod]
    public void Parse_MaximumLongSectors_SaturatesWithoutMultiplicationOverflow()
    {
        var rows = DiskStats.Parse("8 0 sda 0 0 9223372036854775807 0 0 0 1");
        // oracle: long.MaxValue sectors is parseable but saturates on conversion; one written sector is 512 bytes.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(long.MaxValue, rows[0].BytesRead);
        Assert.AreEqual(512L, rows[0].BytesWritten);
    }

    [TestMethod]
    public void Parse_InvariantNumericInput_IsIndependentOfCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.PositiveSign = "p";
            CultureInfo.CurrentCulture = culture;
            var rows = DiskStats.Parse("+8 +0 sda +0 +0 +2048 +0 +0 +0 +4096");
            // oracle: invariant + signs remain valid despite current-culture sign p; 2048/4096 sectors give 1/2 MiB.
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(1048576L, rows[0].BytesRead);
            Assert.AreEqual(2097152L, rows[0].BytesWritten);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void Parse_NegativeZeroInEveryRequiredNumericField_RejectsOnlyThatLine()
    {
        int[] numericFields = [0, 1, 3, 4, 5, 6, 7, 8, 9];
        foreach (int field in numericFields)
        {
            string[] fields = ["8", "0", "bad", "0", "0", "1", "0", "0", "0", "2"];
            fields[field] = "-0";
            var rows = DiskStats.Parse(string.Join(" ", fields) + "\n8 0 good 0 0 1 0 0 0 2");
            // oracle: round-4 (i) rejects any leading '-' including -0; the valid neighbor has 512/1024 bytes.
            Assert.AreEqual(1, rows.Count, $"field {field}");
            Assert.AreEqual(new DiskCounters("good", 512, 1024), rows[0]);
        }
    }

    [TestMethod]
    public void Parse_MajorAboveIntMax_IsAccepted()
    {
        var rows = DiskStats.Parse("3000000000 0 sda 0 0 3 0 0 0 5");
        // oracle: round-4 (i) permits 3000000000 as major; sectors 3/5 give 1536/2560 bytes.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new DiskCounters("sda", 1536, 2560), rows[0]);
    }

    [TestMethod]
    public void Parse_MinorAboveIntMax_IsAccepted()
    {
        var rows = DiskStats.Parse("8 3000000000 sdb 0 0 2 0 0 0 4");
        // oracle: round-4 (i) permits 3000000000 as minor; sectors 2/4 give 1024/2048 bytes.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new DiskCounters("sdb", 1024, 2048), rows[0]);
    }

    [TestMethod]
    public void Parse_MajorAndMinorAtLongMax_AreAccepted()
    {
        var rows = DiskStats.Parse("+9223372036854775807 +9223372036854775807 disk 0 0 1 0 0 0 2");
        // oracle: round-4 (i) includes long.MaxValue and round-2 permits '+'; 1/2 sectors give 512/1024 bytes.
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(new DiskCounters("disk", 512, 1024), rows[0]);
    }

    [TestMethod]
    public void Parse_MajorOrMinorAboveLongMax_RejectsOnlyOverflowingLine()
    {
        for (int field = 0; field < 2; field++)
        {
            string[] fields = ["8", "0", "bad", "0", "0", "1", "0", "0", "0", "2"];
            fields[field] = "9223372036854775808";
            var rows = DiskStats.Parse(string.Join(" ", fields) + "\n8 0 good 0 0 1 0 0 0 2");
            // oracle: round-4 (i) stops at long.MaxValue; the overflowing row is skipped, leaving only good.
            Assert.AreEqual(1, rows.Count, $"field {field}");
            Assert.AreEqual(new DiskCounters("good", 512, 1024), rows[0]);
        }
    }

    [TestMethod]
    public void Parse_ExtraSignsAndSeparatorsInNumericFields_AreRejected()
    {
        foreach (string value in new[] { "+", "++1", "+-0", "--0", "1,000", "1.0" })
        {
            foreach (int field in new[] { 0, 1, 3, 4, 5, 6, 7, 8, 9 })
            {
                string[] fields = ["8", "0", "disk", "0", "0", "1", "0", "0", "0", "2"];
                fields[field] = value;
                // oracle: round-2 allows only digits with at most one leading '+'; extra signs/separators are invalid.
                Assert.AreEqual(0, DiskStats.Parse(string.Join(" ", fields)).Count, $"field {field}: {value}");
            }
        }
    }

    [TestMethod]
    public void Rate_UsesMibPerSecondAndIgnoresNames()
    {
        var previous = new DiskCounters("before", 1048576, 2097152);
        var current = new DiskCounters("after", 5242880, 10485760);
        var rate = DiskStats.Rate(previous, current, 2);
        // oracle: read=(5-1)/2=2 MiB/s; write=(10-2)/2=4 MiB/s; names are ignored.
        Assert.AreEqual(2.0, rate.ReadMb, 0.0);
        Assert.AreEqual(4.0, rate.WriteMb, 0.0);
    }

    [TestMethod]
    public void Rate_FractionalSeconds_AreNotTruncated()
    {
        var previous = new DiskCounters("before", 0, 0);
        var current = new DiskCounters("after", 262144, 131072);
        var rate = DiskStats.Rate(previous, current, 0.5);
        // oracle: read=0.25/0.5=0.5 and write=0.125/0.5=0.25 MiB/s.
        Assert.AreEqual(0.5, rate.ReadMb, 0.0);
        Assert.AreEqual(0.25, rate.WriteMb, 0.0);
    }

    [TestMethod]
    public void Rate_ReadReset_DoesNotSuppressWrite()
    {
        var previous = new DiskCounters("before", 2097152, 1048576);
        var current = new DiskCounters("after", 1048576, 3145728);
        var rate = DiskStats.Rate(previous, current, 1);
        // oracle: negative read delta clamps independently; write delta is 2 MiB.
        Assert.AreEqual(0.0, rate.ReadMb, 0.0);
        Assert.AreEqual(2.0, rate.WriteMb, 0.0);
    }

    [TestMethod]
    public void Rate_WriteReset_DoesNotSuppressRead()
    {
        var previous = new DiskCounters("before", 0, 2097152);
        var current = new DiskCounters("after", 1048576, 0);
        var rate = DiskStats.Rate(previous, current, 1);
        // oracle: read delta is 1 MiB; negative write delta clamps independently.
        Assert.AreEqual(1.0, rate.ReadMb, 0.0);
        Assert.AreEqual(0.0, rate.WriteMb, 0.0);
    }

    [TestMethod]
    public void Rate_UnchangedCounters_AreZero()
    {
        var previous = new DiskCounters("before", 1048576, 2097152);
        var current = new DiskCounters("after", 1048576, 2097152);
        var rate = DiskStats.Rate(previous, current, 1);
        // oracle: both deltas equal zero.
        Assert.AreEqual(0.0, rate.ReadMb, 0.0);
        Assert.AreEqual(0.0, rate.WriteMb, 0.0);
    }

    [TestMethod]
    public void Rate_ZeroSeconds_ReturnsZero()
    {
        var previous = new DiskCounters("before", 0, 0);
        var current = new DiskCounters("after", 1048576, 2097152);
        var rate = DiskStats.Rate(previous, current, 0);
        // oracle: seconds <= 0 or NaN forces both rates to zero.
        Assert.AreEqual(0.0, rate.ReadMb, 0.0);
        Assert.AreEqual(0.0, rate.WriteMb, 0.0);
    }

    [TestMethod]
    public void Rate_NegativeSeconds_ReturnsZero()
    {
        var previous = new DiskCounters("before", 0, 0);
        var current = new DiskCounters("after", 1048576, 2097152);
        var rate = DiskStats.Rate(previous, current, -1);
        // oracle: seconds <= 0 or NaN forces both rates to zero.
        Assert.AreEqual(0.0, rate.ReadMb, 0.0);
        Assert.AreEqual(0.0, rate.WriteMb, 0.0);
    }

    [TestMethod]
    public void Rate_NaNSeconds_ReturnsZero()
    {
        var previous = new DiskCounters("before", 0, 0);
        var current = new DiskCounters("after", 1048576, 2097152);
        var rate = DiskStats.Rate(previous, current, double.NaN);
        // oracle: seconds <= 0 or NaN forces both rates to zero.
        Assert.AreEqual(0.0, rate.ReadMb, 0.0);
        Assert.AreEqual(0.0, rate.WriteMb, 0.0);
    }

    [TestMethod]
    public void Rate_NegativeInfiniteSeconds_ReturnsZero()
    {
        var previous = new DiskCounters("before", 0, 0);
        var current = new DiskCounters("after", 1048576, 2097152);
        var rate = DiskStats.Rate(previous, current, double.NegativeInfinity);
        // oracle: seconds <= 0 or NaN forces both rates to zero.
        Assert.AreEqual(0.0, rate.ReadMb, 0.0);
        Assert.AreEqual(0.0, rate.WriteMb, 0.0);
    }

    [TestMethod]
    public void Rate_PositiveInfiniteSeconds_ReturnsZero()
    {
        var previous = new DiskCounters("before", 0, 0);
        var current = new DiskCounters("after", 1048576, 2097152);
        var rate = DiskStats.Rate(previous, current, double.PositiveInfinity);
        // oracle: finite deltas divided by positive infinity are zero.
        Assert.AreEqual(0.0, rate.ReadMb, 0.0);
        Assert.AreEqual(0.0, rate.WriteMb, 0.0);
    }

    [TestMethod]
    public void Rate_EpsilonSeconds_OverflowIsZero()
    {
        var previous = new DiskCounters("before", 0, 0);
        var current = new DiskCounters("after", 1048576, 2097152);
        var rate = DiskStats.Rate(previous, current, double.Epsilon);
        // oracle: positive MiB deltas divided by double.Epsilon overflow; infinite final rates become zero.
        Assert.AreEqual(0.0, rate.ReadMb, 0.0);
        Assert.AreEqual(0.0, rate.WriteMb, 0.0);
    }

    [TestMethod]
    public void Rate_EpsilonSeconds_ZeroDeltasStayFiniteZero()
    {
        var previous = new DiskCounters("before", 0, 0);
        var current = new DiskCounters("after", 0, 0);
        var rate = DiskStats.Rate(previous, current, double.Epsilon);
        // oracle: zero divided by positive double.Epsilon remains finite zero.
        Assert.AreEqual(0.0, rate.ReadMb, 0.0);
        Assert.AreEqual(0.0, rate.WriteMb, 0.0);
    }

    [TestMethod]
    public void Rate_SubtractsAfterCastingToDouble()
    {
        var previous = new DiskCounters("before", long.MaxValue - 1, long.MaxValue - 1);
        var current = new DiskCounters("after", long.MaxValue, long.MaxValue);
        var rate = DiskStats.Rate(previous, current, 1);
        // oracle: both neighboring long values round to the same double 2^63 before subtraction.
        Assert.AreEqual(0.0, rate.ReadMb, 0.0);
        Assert.AreEqual(0.0, rate.WriteMb, 0.0);
    }

    [TestMethod]
    public void Rate_LongRangeDelta_DoesNotOverflowIntegerSubtraction()
    {
        var previous = new DiskCounters("before", long.MinValue, long.MinValue);
        var current = new DiskCounters("after", long.MaxValue, long.MaxValue);
        var rate = DiskStats.Rate(previous, current, 1);
        // oracle: double subtraction gives 2^64 bytes; 2^64/2^20=2^44=17592186044416.
        Assert.AreEqual(17592186044416.0, rate.ReadMb, 0.0);
        Assert.AreEqual(17592186044416.0, rate.WriteMb, 0.0);
    }

}
