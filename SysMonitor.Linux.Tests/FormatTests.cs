using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Tests;

[TestClass]
public class FormatTests
{
    private static CultureInfo NonInvariantCulture()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("de-DE").Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NegativeSign = "\u2212";
        culture.NumberFormat.NativeDigits = new[]
        {
            "\u0660", "\u0661", "\u0662", "\u0663", "\u0664",
            "\u0665", "\u0666", "\u0667", "\u0668", "\u0669"
        };
        return culture;
    }

    [TestMethod]
    public void Next_Mini()
    {
        // oracle: Mini -> Expanded -> Full -> Mini; undefined Next returns Mini.
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Next(ViewMode.Mini));
    }

    [TestMethod]
    public void Next_Expanded()
    {
        // oracle: Mini -> Expanded -> Full -> Mini; undefined Next returns Mini.
        Assert.AreEqual(ViewMode.Full, ViewModes.Next(ViewMode.Expanded));
    }

    [TestMethod]
    public void Next_Full()
    {
        // oracle: Mini -> Expanded -> Full -> Mini; undefined Next returns Mini.
        Assert.AreEqual(ViewMode.Mini, ViewModes.Next(ViewMode.Full));
    }

    [TestMethod]
    public void Next_UndefinedPositive()
    {
        // oracle: Mini -> Expanded -> Full -> Mini; undefined Next returns Mini.
        Assert.AreEqual(ViewMode.Mini, ViewModes.Next((ViewMode)99));
    }

    [TestMethod]
    public void Next_UndefinedNegative()
    {
        // oracle: Mini -> Expanded -> Full -> Mini; undefined Next returns Mini.
        Assert.AreEqual(ViewMode.Mini, ViewModes.Next((ViewMode)(-1)));
    }

    [TestMethod]
    public void Effective_FullAtMinimum()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Full, ViewModes.Effective(ViewMode.Full, 640, 480));
    }

    [TestMethod]
    public void Effective_FullBelowWidth()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective(ViewMode.Full, 639.999, 480));
    }

    [TestMethod]
    public void Effective_FullBelowHeight()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective(ViewMode.Full, 640, 479.999));
    }

    [TestMethod]
    public void Effective_FullAboveMinimum()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Full, ViewModes.Effective(ViewMode.Full, 640.001, 480.001));
    }

    [TestMethod]
    public void Effective_FullZeroWidth()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective(ViewMode.Full, 0, 480));
    }

    [TestMethod]
    public void Effective_FullZeroHeight()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective(ViewMode.Full, 640, 0));
    }

    [TestMethod]
    public void Effective_FullNegativeWidth()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective(ViewMode.Full, -1, 480));
    }

    [TestMethod]
    public void Effective_FullNegativeHeight()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective(ViewMode.Full, 640, -1));
    }

    [TestMethod]
    public void Effective_FullNanWidth()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective(ViewMode.Full, double.NaN, 480));
    }

    [TestMethod]
    public void Effective_FullNanHeight()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective(ViewMode.Full, 640, double.NaN));
    }

    [TestMethod]
    public void Effective_FullNegativeInfinityWidth()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective(ViewMode.Full, double.NegativeInfinity, 480));
    }

    [TestMethod]
    public void Effective_FullNegativeInfinityHeight()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective(ViewMode.Full, 640, double.NegativeInfinity));
    }

    [TestMethod]
    public void Effective_FullPositiveInfinities()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Full, ViewModes.Effective(ViewMode.Full, double.PositiveInfinity, double.PositiveInfinity));
    }

    [TestMethod]
    public void Effective_MiniWithInvalidSizes()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Mini, ViewModes.Effective(ViewMode.Mini, double.NaN, double.NegativeInfinity));
        Assert.AreEqual(ViewMode.Mini, ViewModes.Effective(ViewMode.Mini, 640, double.NaN));
    }

    [TestMethod]
    public void Effective_ExpandedWithInvalidSizes()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective(ViewMode.Expanded, 0, -1));
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective(ViewMode.Expanded, double.NaN, 480));
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective(ViewMode.Expanded, 640, double.NaN));
    }

    [TestMethod]
    public void Effective_UndefinedAtLargeSize()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective((ViewMode)99, 1000, 1000));
    }

    [TestMethod]
    public void Effective_UndefinedWithInvalidSize()
    {
        // oracle: Full requires width >= 640 and height >= 480; Mini/Expanded are unchanged; undefined returns Expanded.
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective((ViewMode)(-1), double.NaN, 0));
    }

    [TestMethod]
    public void Percent_MinimumInt()
    {
        // oracle: Percent clamps the integer to 0..100 and appends %.
        Assert.AreEqual("0%", Fmt.Percent(int.MinValue));
    }

    [TestMethod]
    public void Percent_Negative()
    {
        // oracle: Percent clamps the integer to 0..100 and appends %.
        Assert.AreEqual("0%", Fmt.Percent(-1));
    }

    [TestMethod]
    public void Percent_Zero()
    {
        // oracle: Percent clamps the integer to 0..100 and appends %.
        Assert.AreEqual("0%", Fmt.Percent(0));
    }

    [TestMethod]
    public void Percent_One()
    {
        // oracle: Percent clamps the integer to 0..100 and appends %.
        Assert.AreEqual("1%", Fmt.Percent(1));
    }

    [TestMethod]
    public void Percent_Ordinary()
    {
        // oracle: Percent clamps the integer to 0..100 and appends %.
        Assert.AreEqual("73%", Fmt.Percent(73));
    }

    [TestMethod]
    public void Percent_Hundred()
    {
        // oracle: Percent clamps the integer to 0..100 and appends %.
        Assert.AreEqual("100%", Fmt.Percent(100));
    }

    [TestMethod]
    public void Percent_AboveHundred()
    {
        // oracle: Percent clamps the integer to 0..100 and appends %.
        Assert.AreEqual("100%", Fmt.Percent(101));
    }

    [TestMethod]
    public void Percent_MaximumInt()
    {
        // oracle: Percent clamps the integer to 0..100 and appends %.
        Assert.AreEqual("100%", Fmt.Percent(int.MaxValue));
    }

    [TestMethod]
    public void Size_NaN()
    {
        // oracle: NaN is invalid.
        Assert.AreEqual("N/A", Fmt.Size(double.NaN));
    }

    [TestMethod]
    public void Size_NegativeInfinity()
    {
        // oracle: Infinity is invalid.
        Assert.AreEqual("N/A", Fmt.Size(double.NegativeInfinity));
    }

    [TestMethod]
    public void Size_PositiveInfinity()
    {
        // oracle: Infinity is invalid.
        Assert.AreEqual("N/A", Fmt.Size(double.PositiveInfinity));
    }

    [TestMethod]
    public void Size_Negative()
    {
        // oracle: Negative size is invalid.
        Assert.AreEqual("N/A", Fmt.Size(-0.001));
    }

    [TestMethod]
    public void Size_Zero()
    {
        // oracle: 0 * 1024 = 0 MB.
        Assert.AreEqual("0 MB", Fmt.Size(0));
    }

    [TestMethod]
    public void Size_NegativeZero()
    {
        // oracle: Negative zero equals zero.
        Assert.AreEqual("0 MB", Fmt.Size(-0.0));
    }

    [TestMethod]
    public void Size_SmallestPositive()
    {
        // oracle: Epsilon * 1024 rounds to 0 MB.
        Assert.AreEqual("0 MB", Fmt.Size(double.Epsilon));
    }

    [TestMethod]
    public void Size_HalfMbBelow()
    {
        // oracle: Below 0.5 MB rounds to 0.
        Assert.AreEqual("0 MB", Fmt.Size(0.5 / 1024 - 0.00000001));
    }

    [TestMethod]
    public void Size_HalfMbAt()
    {
        // oracle: 0.5 MB rounds away from zero to 1.
        Assert.AreEqual("1 MB", Fmt.Size(0.5 / 1024));
    }

    [TestMethod]
    public void Size_HalfMbAbove()
    {
        // oracle: Above 0.5 MB rounds to 1.
        Assert.AreEqual("1 MB", Fmt.Size(0.5 / 1024 + 0.00000001));
    }

    [TestMethod]
    public void Size_HalfGb()
    {
        // oracle: 0.5 * 1024 = 512 MB.
        Assert.AreEqual("512 MB", Fmt.Size(0.5));
    }

    [TestMethod]
    public void Size_MbCarryBelow()
    {
        // oracle: Below 1023.5 MB rounds to 1023.
        Assert.AreEqual("1023 MB", Fmt.Size(1023.5 / 1024 - 0.00000001));
    }

    [TestMethod]
    public void Size_MbCarryAt()
    {
        // oracle: 1023.5 MB rounds to 1024, carried to 1.0 GB.
        Assert.AreEqual("1.0 GB", Fmt.Size(1023.5 / 1024));
    }

    [TestMethod]
    public void Size_MbCarryAbove()
    {
        // oracle: Above 1023.5 MB rounds to 1024, carried to GB.
        Assert.AreEqual("1.0 GB", Fmt.Size(1023.5 / 1024 + 0.00000001));
    }

    [TestMethod]
    public void Size_ExactlyOneGb()
    {
        // oracle: 1 GB uses one decimal.
        Assert.AreEqual("1.0 GB", Fmt.Size(1));
    }

    [TestMethod]
    public void Size_GbRoundingBelow()
    {
        // oracle: Below 5.25 rounds to 5.2.
        Assert.AreEqual("5.2 GB", Fmt.Size(5.249999));
    }

    [TestMethod]
    public void Size_GbRoundingAt()
    {
        // oracle: 5.25 rounds away from zero to 5.3.
        Assert.AreEqual("5.3 GB", Fmt.Size(5.25));
    }

    [TestMethod]
    public void Size_GbRoundingAbove()
    {
        // oracle: Above 5.25 rounds to 5.3.
        Assert.AreEqual("5.3 GB", Fmt.Size(5.250001));
    }

    [TestMethod]
    public void Size_GbCarryBelow()
    {
        // oracle: Below 1023.95 rounds to 1023.9.
        Assert.AreEqual("1023.9 GB", Fmt.Size(1023.949999));
    }

    [TestMethod]
    public void Size_GbCarryAt()
    {
        // oracle: 1023.95 rounds to 1024.0 GB, carried to TB.
        Assert.AreEqual("1.0 TB", Fmt.Size(1023.95));
    }

    [TestMethod]
    public void Size_GbCarryAbove()
    {
        // oracle: Above 1023.95 rounds to 1024.0 GB, carried to TB.
        Assert.AreEqual("1.0 TB", Fmt.Size(1023.950001));
    }

    [TestMethod]
    public void Size_ExactlyOneTb()
    {
        // oracle: 1024 / 1024 = 1 TB.
        Assert.AreEqual("1.0 TB", Fmt.Size(1024));
    }

    [TestMethod]
    public void Size_TbRoundingBelow()
    {
        // oracle: 1.249999 TB rounds to 1.2.
        Assert.AreEqual("1.2 TB", Fmt.Size(1.249999 * 1024));
    }

    [TestMethod]
    public void Size_TbRoundingAt()
    {
        // oracle: 1.25 TB rounds away from zero to 1.3.
        Assert.AreEqual("1.3 TB", Fmt.Size(1.25 * 1024));
    }

    [TestMethod]
    public void Size_TbRoundingAbove()
    {
        // oracle: 1.250001 TB rounds to 1.3.
        Assert.AreEqual("1.3 TB", Fmt.Size(1.250001 * 1024));
    }

    [TestMethod]
    public void Size_MultipleTb()
    {
        // oracle: 2560 / 1024 = 2.5 TB.
        Assert.AreEqual("2.5 TB", Fmt.Size(2560));
    }

    [TestMethod]
    public void Rate_NaN()
    {
        // oracle: NaN uses the rate fallback.
        Assert.AreEqual("0 KB/s", Fmt.Rate(double.NaN));
    }

    [TestMethod]
    public void Rate_PositiveInfinity()
    {
        // oracle: Infinity uses the rate fallback.
        Assert.AreEqual("0 KB/s", Fmt.Rate(double.PositiveInfinity));
    }

    [TestMethod]
    public void Rate_NegativeInfinity()
    {
        // oracle: Infinity uses the rate fallback.
        Assert.AreEqual("0 KB/s", Fmt.Rate(double.NegativeInfinity));
    }

    [TestMethod]
    public void Rate_Negative()
    {
        // oracle: Negative rate uses the fallback.
        Assert.AreEqual("0 KB/s", Fmt.Rate(-1));
    }

    [TestMethod]
    public void Rate_Zero()
    {
        // oracle: 0 * 1024 = 0 KB/s.
        Assert.AreEqual("0 KB/s", Fmt.Rate(0));
    }

    [TestMethod]
    public void Rate_HalfKbBelow()
    {
        // oracle: Below 0.5 KB/s rounds to 0.
        Assert.AreEqual("0 KB/s", Fmt.Rate(0.5 / 1024 - 0.00000001));
    }

    [TestMethod]
    public void Rate_HalfKbAt()
    {
        // oracle: 0.5 KB/s rounds away from zero to 1.
        Assert.AreEqual("1 KB/s", Fmt.Rate(0.5 / 1024));
    }

    [TestMethod]
    public void Rate_HalfKbAbove()
    {
        // oracle: Above 0.5 KB/s rounds to 1.
        Assert.AreEqual("1 KB/s", Fmt.Rate(0.5 / 1024 + 0.00000001));
    }

    [TestMethod]
    public void Rate_HalfMb()
    {
        // oracle: 0.5 * 1024 = 512 KB/s.
        Assert.AreEqual("512 KB/s", Fmt.Rate(0.5));
    }

    [TestMethod]
    public void Rate_CarryBelow()
    {
        // oracle: Below 1023.5 KB/s rounds to 1023.
        Assert.AreEqual("1023 KB/s", Fmt.Rate(1023.5 / 1024 - 0.00000001));
    }

    [TestMethod]
    public void Rate_CarryAt()
    {
        // oracle: 1023.5 KB/s rounds to 1024 and carries.
        Assert.AreEqual("1.0 MB/s", Fmt.Rate(1023.5 / 1024));
    }

    [TestMethod]
    public void Rate_CarryAbove()
    {
        // oracle: Above 1023.5 KB/s rounds to 1024 and carries.
        Assert.AreEqual("1.0 MB/s", Fmt.Rate(1023.5 / 1024 + 0.00000001));
    }

    [TestMethod]
    public void Rate_ExactlyOne()
    {
        // oracle: 1 MB/s uses one decimal.
        Assert.AreEqual("1.0 MB/s", Fmt.Rate(1));
    }

    [TestMethod]
    public void Rate_RoundingBelow()
    {
        // oracle: Below 1.25 rounds to 1.2.
        Assert.AreEqual("1.2 MB/s", Fmt.Rate(1.249999));
    }

    [TestMethod]
    public void Rate_RoundingAt()
    {
        // oracle: 1.25 rounds away from zero to 1.3.
        Assert.AreEqual("1.3 MB/s", Fmt.Rate(1.25));
    }

    [TestMethod]
    public void Rate_RoundingAbove()
    {
        // oracle: Above 1.25 rounds to 1.3.
        Assert.AreEqual("1.3 MB/s", Fmt.Rate(1.250001));
    }

    [TestMethod]
    public void Rate_NoGbUnit()
    {
        // oracle: Rate has no GB/s unit.
        Assert.AreEqual("1024.0 MB/s", Fmt.Rate(1024));
    }

    [TestMethod]
    public void Temp_Missing()
    {
        // oracle: Null temperature is N/A; a value is the signed integer followed by ASCII C.
        Assert.AreEqual("N/A", Fmt.Temp(null));
    }

    [TestMethod]
    public void Temp_Zero()
    {
        // oracle: Null temperature is N/A; a value is the signed integer followed by ASCII C.
        Assert.AreEqual("0 C", Fmt.Temp(0));
    }

    [TestMethod]
    public void Temp_Negative()
    {
        // oracle: Null temperature is N/A; a value is the signed integer followed by ASCII C.
        Assert.AreEqual("-20 C", Fmt.Temp(-20));
    }

    [TestMethod]
    public void Temp_Positive()
    {
        // oracle: Null temperature is N/A; a value is the signed integer followed by ASCII C.
        Assert.AreEqual("65 C", Fmt.Temp(65));
    }

    [TestMethod]
    public void Temp_MinimumInt()
    {
        // oracle: Null temperature is N/A; a value is the signed integer followed by ASCII C.
        Assert.AreEqual("-2147483648 C", Fmt.Temp(int.MinValue));
    }

    [TestMethod]
    public void Temp_MaximumInt()
    {
        // oracle: Null temperature is N/A; a value is the signed integer followed by ASCII C.
        Assert.AreEqual("2147483647 C", Fmt.Temp(int.MaxValue));
    }

    [TestMethod]
    public void Speed_NaN()
    {
        // oracle: NaN speed is invalid.
        Assert.AreEqual("N/A", Fmt.Speed(double.NaN));
    }

    [TestMethod]
    public void Speed_PositiveInfinity()
    {
        // oracle: All non-finite speeds are invalid.
        Assert.AreEqual("N/A", Fmt.Speed(double.PositiveInfinity));
    }

    [TestMethod]
    public void Speed_NegativeInfinity()
    {
        // oracle: All non-finite speeds are invalid.
        Assert.AreEqual("N/A", Fmt.Speed(double.NegativeInfinity));
    }

    [TestMethod]
    public void Speed_Zero()
    {
        // oracle: Speed <= 0 is invalid.
        Assert.AreEqual("N/A", Fmt.Speed(0));
    }

    [TestMethod]
    public void Speed_Negative()
    {
        // oracle: Speed <= 0 is invalid.
        Assert.AreEqual("N/A", Fmt.Speed(-1));
    }

    [TestMethod]
    public void Speed_SmallPositive()
    {
        // oracle: Positive speed rounds to an integer even when it rounds to zero.
        Assert.AreEqual("0 Mbps", Fmt.Speed(double.Epsilon));
    }

    [TestMethod]
    public void Speed_IntegerMidpointBelow()
    {
        // oracle: Below 12.5 rounds to 12.
        Assert.AreEqual("12 Mbps", Fmt.Speed(12.499999));
    }

    [TestMethod]
    public void Speed_IntegerMidpointAt()
    {
        // oracle: 12.5 rounds away from zero to 13.
        Assert.AreEqual("13 Mbps", Fmt.Speed(12.5));
    }

    [TestMethod]
    public void Speed_IntegerMidpointAbove()
    {
        // oracle: Above 12.5 rounds to 13.
        Assert.AreEqual("13 Mbps", Fmt.Speed(12.500001));
    }

    [TestMethod]
    public void Speed_CarryBelow()
    {
        // oracle: Below 999.5 rounds to 999.
        Assert.AreEqual("999 Mbps", Fmt.Speed(999.499999));
    }

    [TestMethod]
    public void Speed_CarryAt()
    {
        // oracle: 999.5 rounds to 1000 Mbps and carries.
        Assert.AreEqual("1.0 Gbps", Fmt.Speed(999.5));
    }

    [TestMethod]
    public void Speed_CarryAbove()
    {
        // oracle: Above 999.5 rounds to 1000 Mbps and carries.
        Assert.AreEqual("1.0 Gbps", Fmt.Speed(999.500001));
    }

    [TestMethod]
    public void Speed_ExactlyThousand()
    {
        // oracle: 1000 / 1000 = 1.0 Gbps.
        Assert.AreEqual("1.0 Gbps", Fmt.Speed(1000));
    }

    [TestMethod]
    public void Speed_GbpsMidpointBelow()
    {
        // oracle: 1249.999 / 1000 rounds to 1.2.
        Assert.AreEqual("1.2 Gbps", Fmt.Speed(1249.999));
    }

    [TestMethod]
    public void Speed_GbpsMidpointAt()
    {
        // oracle: 1250 / 1000 = 1.25, rounded away from zero.
        Assert.AreEqual("1.3 Gbps", Fmt.Speed(1250));
    }

    [TestMethod]
    public void Speed_GbpsMidpointAbove()
    {
        // oracle: 1250.001 / 1000 rounds to 1.3.
        Assert.AreEqual("1.3 Gbps", Fmt.Speed(1250.001));
    }

    [TestMethod]
    public void Speed_OrdinaryGbps()
    {
        // oracle: 2500 / 1000 = 2.5 Gbps.
        Assert.AreEqual("2.5 Gbps", Fmt.Speed(2500));
    }

    [TestMethod]
    public void Culture_Percent()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = NonInvariantCulture();
            // oracle: Fmt uses ASCII digits, minus sign and decimal point despite comma decimals, Arabic native digits and U+2212 negative sign.
            Assert.AreEqual("73%", Fmt.Percent(73));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [TestMethod]
    public void Culture_Size()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = NonInvariantCulture();
            // oracle: Fmt uses ASCII digits, minus sign and decimal point despite comma decimals, Arabic native digits and U+2212 negative sign.
            Assert.AreEqual("5.3 GB", Fmt.Size(5.25));
            // oracle: The MB integer and TB decimal branches also use invariant formatting.
            Assert.AreEqual("512 MB", Fmt.Size(0.5));
            Assert.AreEqual("1.3 TB", Fmt.Size(1280));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [TestMethod]
    public void Culture_Rate()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = NonInvariantCulture();
            // oracle: Fmt uses ASCII digits, minus sign and decimal point despite comma decimals, Arabic native digits and U+2212 negative sign.
            Assert.AreEqual("1.3 MB/s", Fmt.Rate(1.25));
            // oracle: The KB integer branch is invariant too; 0.5 MiB/s = 512 KiB/s.
            Assert.AreEqual("512 KB/s", Fmt.Rate(0.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [TestMethod]
    public void Culture_Temp()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = NonInvariantCulture();
            // oracle: Fmt uses ASCII digits, minus sign and decimal point despite comma decimals, Arabic native digits and U+2212 negative sign.
            Assert.AreEqual("-65 C", Fmt.Temp(-65));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [TestMethod]
    public void Culture_Speed()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = NonInvariantCulture();
            // oracle: Fmt uses ASCII digits, minus sign and decimal point despite comma decimals, Arabic native digits and U+2212 negative sign.
            Assert.AreEqual("1.3 Gbps", Fmt.Speed(1250));
            // oracle: The Mbps integer branch is invariant; binary-exact 12.5 rounds away to 13.
            Assert.AreEqual("13 Mbps", Fmt.Speed(12.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [TestMethod]
    public void Effective_MiniAtLargeSize()
    {
        // oracle: Mini and Expanded are returned unchanged whatever the size, including a large window.
        Assert.AreEqual(ViewMode.Mini, ViewModes.Effective(ViewMode.Mini, 2000, 2000));
    }

    [TestMethod]
    public void Effective_ExpandedAtLargeSize()
    {
        // oracle: Mini and Expanded are returned unchanged whatever the size, including a large window.
        Assert.AreEqual(ViewMode.Expanded, ViewModes.Effective(ViewMode.Expanded, 2000, 2000));
    }


    [TestMethod]
    public void Size_ImmediatelyBelowAndAboveOneGb()
    {
        // oracle: The adjacent doubles around 1 are less than half an MB away;
        // below 1 carries 1024 rounded MB to 1.0 GB, above 1 rounds to 1.0 GB.
        Assert.AreEqual("1.0 GB", Fmt.Size(Math.BitDecrement(1.0)));
        Assert.AreEqual("1.0 GB", Fmt.Size(Math.BitIncrement(1.0)));
    }

    [TestMethod]
    public void Size_ImmediatelyBelowAndAboveOneTb()
    {
        // oracle: The adjacent doubles around 1024 are less than 0.05 GB away;
        // below 1024 carries rounded 1024.0 GB, above 1024 rounds to 1.0 TB.
        Assert.AreEqual("1.0 TB", Fmt.Size(Math.BitDecrement(1024.0)));
        Assert.AreEqual("1.0 TB", Fmt.Size(Math.BitIncrement(1024.0)));
    }

    [TestMethod]
    public void Size_BinaryExactEvenTenthsMidpointsRoundAway()
    {
        // oracle: 2.25 is binary-exact and halfway between 2.2 and 2.3; away gives 2.3.
        Assert.AreEqual("2.3 GB", Fmt.Size(2.25));
        // oracle: 2304 / 1024 = 2.25 TB exactly; away gives 2.3, not even 2.2.
        Assert.AreEqual("2.3 TB", Fmt.Size(2304));
    }

    [TestMethod]
    public void Rate_NegativeZeroAndImmediatelyNegativeUseZeroText()
    {
        // oracle: IEEE negative zero compares equal to zero, and v < 0 uses the same zero fallback.
        var negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
        Assert.AreEqual("0 KB/s", Fmt.Rate(negativeZero));
        Assert.AreEqual("0 KB/s", Fmt.Rate(Math.BitDecrement(0.0)));
    }

}
