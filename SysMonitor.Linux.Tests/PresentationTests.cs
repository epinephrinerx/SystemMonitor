using System;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public class PresentationTests
{
    [TestMethod]
    public void Hex_JustBelowWarm_UsesAccent()
    {
        // oracle: value < 70 uses the valid accent.
        Assert.AreEqual("#123aBc", LoadColor.Hex(69.999, "#123aBc"));
    }

    [TestMethod]
    public void Hex_AtWarm_IsYellow()
    {
        // oracle: 70 <= value < 90 is yellow.
        Assert.AreEqual("#eab308", LoadColor.Hex(70, "#123456"));
    }

    [TestMethod]
    public void Hex_JustBelowHot_IsYellow()
    {
        // oracle: 89.999 is below 90 and at least 70.
        Assert.AreEqual("#eab308", LoadColor.Hex(89.999, "#123456"));
    }

    [TestMethod]
    public void Hex_AtHot_IsRed()
    {
        // oracle: value >= 90 is red.
        Assert.AreEqual("#ef4444", LoadColor.Hex(90, "#123456"));
    }

    [TestMethod]
    public void Hex_AboveHot_IsRed()
    {
        // oracle: 900 >= 90.
        Assert.AreEqual("#ef4444", LoadColor.Hex(900, "#123456"));
    }

    [TestMethod]
    public void Hex_Negative_UsesAccent()
    {
        // oracle: negative values use the accent.
        Assert.AreEqual("#abcdef", LoadColor.Hex(-1, "#abcdef"));
    }

    [TestMethod]
    public void Hex_NaN_UsesAccent()
    {
        // oracle: NaN uses the accent.
        Assert.AreEqual("#abcdef", LoadColor.Hex(double.NaN, "#abcdef"));
    }

    [TestMethod]
    public void Hex_PositiveInfinity_IsRed()
    {
        // oracle: positive infinity >= 90.
        Assert.AreEqual("#ef4444", LoadColor.Hex(double.PositiveInfinity, "#abcdef"));
    }

    [TestMethod]
    public void Hex_NegativeInfinity_UsesAccent()
    {
        // oracle: negative infinity uses the accent.
        Assert.AreEqual("#abcdef", LoadColor.Hex(double.NegativeInfinity, "#abcdef"));
    }

    [TestMethod]
    public void Hex_NullAccent_UsesFallback()
    {
        // oracle: null accent is unusable.
        Assert.AreEqual("#3b82f6", LoadColor.Hex(0, null!));
    }

    [TestMethod]
    public void Hex_EmptyAccent_UsesFallback()
    {
        // oracle: empty accent is unusable.
        Assert.AreEqual("#3b82f6", LoadColor.Hex(0, ""));
    }

    [TestMethod]
    public void Hex_ValidMixedCaseAccent_IsPreserved()
    {
        // oracle: valid six-digit hex accent is returned exactly as supplied.
        Assert.AreEqual("#aBcD09", LoadColor.Hex(0, "#aBcD09"));
    }

    [TestMethod]
    public void Hex_NamedAccent_UsesFallback()
    {
        // oracle: accent must be # plus exactly six hexadecimal digits.
        Assert.AreEqual("#3b82f6", LoadColor.Hex(0, "red"));
    }

    [TestMethod]
    public void Hex_WhitespaceAccent_UsesFallback()
    {
        // oracle: whitespace is not a hex accent.
        Assert.AreEqual("#3b82f6", LoadColor.Hex(0, "  "));
    }

    [TestMethod]
    public void Hex_ShortAccent_UsesFallback()
    {
        // oracle: three hexadecimal digits are insufficient.
        Assert.AreEqual("#3b82f6", LoadColor.Hex(0, "#fff"));
    }

    [TestMethod]
    public void Hex_LongAccent_UsesFallback()
    {
        // oracle: seven hexadecimal digits are too many.
        Assert.AreEqual("#3b82f6", LoadColor.Hex(0, "#1234567"));
    }

    [TestMethod]
    public void Hex_NonHexDigit_UsesFallback()
    {
        // oracle: g is not a hexadecimal digit.
        Assert.AreEqual("#3b82f6", LoadColor.Hex(0, "#12345g"));
    }

    [TestMethod]
    public void Hex_PaddedAccent_UsesFallback()
    {
        // oracle: validation requires the exact #RRGGBB shape without surrounding spaces.
        Assert.AreEqual("#3b82f6", LoadColor.Hex(0, " #123456 "));
    }

    [TestMethod]
    public void Hex_HotColor_TakesPriorityOverSuppliedAccent()
    {
        // oracle: value 90 selects red regardless of the supplied accent; this does not observe accent validation.
        Assert.AreEqual("#ef4444", LoadColor.Hex(90, "red"));
    }

    [TestMethod]
    public void Hex_NaNWithInvalidAccent_UsesFallback()
    {
        // oracle: round-4 (v) requires fallback when NaN would return the invalid accent red.
        Assert.AreEqual("#3b82f6", LoadColor.Hex(double.NaN, "red"));
    }

    [TestMethod]
    public void Hex_NegativeInfinityWithInvalidAccent_UsesFallback()
    {
        // oracle: round-4 (v) requires fallback for negative infinity with an invalid accent.
        Assert.AreEqual("#3b82f6", LoadColor.Hex(double.NegativeInfinity, "red"));
    }

    [TestMethod]
    public void Hex_NegativeFiveWithInvalidAccent_UsesFallback()
    {
        // oracle: round-4 (v) requires fallback for a negative value with an invalid accent.
        Assert.AreEqual("#3b82f6", LoadColor.Hex(-5, "red"));
    }

    [TestMethod]
    public void Hex_NaNWithUppercaseAccent_PreservesCase()
    {
        // oracle: NaN returns a valid accent verbatim, including all six uppercase hexadecimal digits.
        Assert.AreEqual("#ABCDEF", LoadColor.Hex(double.NaN, "#ABCDEF"));
    }

    [TestMethod]
    public void Level_JustBelowWarm_IsNormal()
    {
        // oracle: 69.999 < 70.
        Assert.AreEqual(LoadLevel.Normal, LoadColor.Level(69.999));
    }

    [TestMethod]
    public void Level_AtWarm_IsWarm()
    {
        // oracle: 70 <= value < 90.
        Assert.AreEqual(LoadLevel.Warm, LoadColor.Level(70));
    }

    [TestMethod]
    public void Level_JustBelowHot_IsWarm()
    {
        // oracle: 89.999 < 90.
        Assert.AreEqual(LoadLevel.Warm, LoadColor.Level(89.999));
    }

    [TestMethod]
    public void Level_AtHot_IsHot()
    {
        // oracle: 90 >= 90.
        Assert.AreEqual(LoadLevel.Hot, LoadColor.Level(90));
    }

    [TestMethod]
    public void Level_NaN_IsNormal()
    {
        // oracle: NaN maps to Normal.
        Assert.AreEqual(LoadLevel.Normal, LoadColor.Level(double.NaN));
    }

    [TestMethod]
    public void Level_PositiveInfinity_IsHot()
    {
        // oracle: positive infinity >= 90.
        Assert.AreEqual(LoadLevel.Hot, LoadColor.Level(double.PositiveInfinity));
    }

    [TestMethod]
    public void Level_NegativeInfinity_IsNormal()
    {
        // oracle: negative infinity is below 70.
        Assert.AreEqual(LoadLevel.Normal, LoadColor.Level(double.NegativeInfinity));
    }

    [TestMethod]
    public void Level_Negative_IsNormal()
    {
        // oracle: -1 < 70.
        Assert.AreEqual(LoadLevel.Normal, LoadColor.Level(-1));
    }

    [TestMethod]
    public void Temperature_Null()
    {
        // oracle: null temperature maps to None.
        Assert.AreEqual(TempLevel.None, TempLevels.Of(null));
    }

    [TestMethod]
    public void Temperature_Minimum()
    {
        // oracle: every temperature below 65 maps to Ok.
        Assert.AreEqual(TempLevel.Ok, TempLevels.Of(int.MinValue));
    }

    [TestMethod]
    public void Temperature_BelowWarm()
    {
        // oracle: 64 < 65.
        Assert.AreEqual(TempLevel.Ok, TempLevels.Of(64));
    }

    [TestMethod]
    public void Temperature_AtWarm()
    {
        // oracle: 65 is the first Warm temperature.
        Assert.AreEqual(TempLevel.Warm, TempLevels.Of(65));
    }

    [TestMethod]
    public void Temperature_BelowHot()
    {
        // oracle: 79 is the last Warm temperature.
        Assert.AreEqual(TempLevel.Warm, TempLevels.Of(79));
    }

    [TestMethod]
    public void Temperature_AtHot()
    {
        // oracle: 80 is the first Hot temperature.
        Assert.AreEqual(TempLevel.Hot, TempLevels.Of(80));
    }

    [TestMethod]
    public void Temperature_Maximum()
    {
        // oracle: every temperature >= 80 maps to Hot.
        Assert.AreEqual(TempLevel.Hot, TempLevels.Of(int.MaxValue));
    }

    [TestMethod]
    public void Theme_DefaultName_IsLight()
    {
        // oracle: R-011 sets DefaultName to light.
        Assert.AreEqual("light", Theme.DefaultName);
    }

    [TestMethod]
    public void Theme_Light_HasSpecifiedPalette()
    {
        // oracle: the seven Light palette literals are specified in phase 4 section 3.
        Assert.AreEqual("#ffffff", Theme.Light.Panel);
        Assert.AreEqual("#f3f5f9", Theme.Light.Sidebar);
        Assert.AreEqual("#eef1f6", Theme.Light.Control);
        Assert.AreEqual("#1e293b", Theme.Light.Text);
        Assert.AreEqual("#64748b", Theme.Light.Muted);
        Assert.AreEqual("#d5dae1", Theme.Light.Border);
        Assert.AreEqual("#dfe4ec", Theme.Light.BarEmpty);
    }

    [TestMethod]
    public void Theme_Dark_HasSpecifiedPalette()
    {
        // oracle: the seven Dark palette literals are specified in phase 4 section 3.
        Assert.AreEqual("#0f172a", Theme.Dark.Panel);
        Assert.AreEqual("#0b1220", Theme.Dark.Sidebar);
        Assert.AreEqual("#1a2438", Theme.Dark.Control);
        Assert.AreEqual("#ffffff", Theme.Dark.Text);
        Assert.AreEqual("#cbd5e1", Theme.Dark.Muted);
        Assert.AreEqual("#2b374b", Theme.Dark.Border);
        Assert.AreEqual("#070c17", Theme.Dark.BarEmpty);
    }

    [TestMethod]
    public void Theme_ResolveTrimmedMixedCase()
    {
        // oracle: only trimmed case-insensitive dark selects Dark; this input selects Dark.
        Assert.AreEqual(Theme.Dark, Theme.Resolve(" \tDaRk\r\n"));
    }

    [TestMethod]
    public void Theme_ResolveNull()
    {
        // oracle: only trimmed case-insensitive dark selects Dark; this input selects Light.
        Assert.AreEqual(Theme.Light, Theme.Resolve(null));
    }

    [TestMethod]
    public void Theme_ResolveEmpty()
    {
        // oracle: only trimmed case-insensitive dark selects Dark; this input selects Light.
        Assert.AreEqual(Theme.Light, Theme.Resolve(""));
    }

    [TestMethod]
    public void Theme_ResolveUnknown()
    {
        // oracle: only trimmed case-insensitive dark selects Dark; this input selects Light.
        Assert.AreEqual(Theme.Light, Theme.Resolve("system"));
    }

    [TestMethod]
    public void Theme_ResolveWhitespace()
    {
        // oracle: only trimmed case-insensitive dark selects Dark; this input selects Light.
        Assert.AreEqual(Theme.Light, Theme.Resolve("   "));
    }

    [TestMethod]
    public void Theme_ResolveExplicitLight()
    {
        // oracle: only trimmed case-insensitive dark selects Dark; this input selects Light.
        Assert.AreEqual(Theme.Light, Theme.Resolve("light"));
    }

    [TestMethod]
    public void Layout_DefaultColumns_PlacesThreeInRowMajorOrder()
    {
        var cells = CardLayout.Place(3);
        // oracle: two default columns give (i / 2, i % 2).
        Assert.AreEqual(3, cells.Count);
        Assert.AreEqual((0, 0), cells[0]);
        Assert.AreEqual((0, 1), cells[1]);
        Assert.AreEqual((1, 0), cells[2]);
        Assert.AreEqual(2, CardLayout.RowCount(3));
    }

    [TestMethod]
    public void Layout_CustomColumns_PlacesPartialLastRow()
    {
        var cells = CardLayout.Place(7, 3);
        // oracle: seven items in three columns occupy ceil(7/3)=3 rows.
        Assert.AreEqual(7, cells.Count);
        for (int i = 0; i < cells.Count; i++)
        {
            // oracle: row-major placement in three columns is (i/3,i%3).
            Assert.AreEqual((i / 3, i % 3), cells[i]);
        }
        // oracle: ceil(7/3)=3 rows.
        Assert.AreEqual(3, CardLayout.RowCount(7, 3));
    }

    [TestMethod]
    public void Layout_ZeroItems()
    {
        // oracle: itemCount <= 0 produces no cells and no rows, independently of columns.
        Assert.AreEqual(0, CardLayout.Place(0, 2).Count);
        Assert.AreEqual(0, CardLayout.RowCount(0, 2));
    }

    [TestMethod]
    public void Layout_NegativeItems()
    {
        // oracle: itemCount <= 0 produces no cells and no rows, independently of columns.
        Assert.AreEqual(0, CardLayout.Place(-7, 2).Count);
        Assert.AreEqual(0, CardLayout.RowCount(-7, 2));
    }

    [TestMethod]
    public void Layout_MinimumItems()
    {
        // oracle: itemCount <= 0 produces no cells and no rows, independently of columns.
        Assert.AreEqual(0, CardLayout.Place(int.MinValue, 0).Count);
        Assert.AreEqual(0, CardLayout.RowCount(int.MinValue, 0));
    }

    [TestMethod]
    public void Layout_ZeroColumns_UsesOneColumn()
    {
        var cells = CardLayout.Place(3, 0);
        // oracle: columns < 1 becomes 1, so cell i is (i,0) and there are three rows.
        Assert.AreEqual(3, cells.Count);
        Assert.AreEqual((0, 0), cells[0]);
        Assert.AreEqual((1, 0), cells[1]);
        Assert.AreEqual((2, 0), cells[2]);
        Assert.AreEqual(3, CardLayout.RowCount(3, 0));
    }

    [TestMethod]
    public void Layout_NegativeColumns_UsesOneColumn()
    {
        var cells = CardLayout.Place(3, -3);
        // oracle: columns < 1 becomes 1, so cell i is (i,0) and there are three rows.
        Assert.AreEqual(3, cells.Count);
        Assert.AreEqual((0, 0), cells[0]);
        Assert.AreEqual((1, 0), cells[1]);
        Assert.AreEqual((2, 0), cells[2]);
        Assert.AreEqual(3, CardLayout.RowCount(3, -3));
    }

    [TestMethod]
    public void Layout_MinimumColumns_UsesOneColumn()
    {
        var cells = CardLayout.Place(3, int.MinValue);
        // oracle: columns < 1 becomes 1, so cell i is (i,0) and there are three rows.
        Assert.AreEqual(3, cells.Count);
        Assert.AreEqual((0, 0), cells[0]);
        Assert.AreEqual((1, 0), cells[1]);
        Assert.AreEqual((2, 0), cells[2]);
        Assert.AreEqual(3, CardLayout.RowCount(3, int.MinValue));
    }

    [TestMethod]
    public void Layout_AtCap_HasOneHundredThousandCells()
    {
        var cells = CardLayout.Place(100000, 3);
        // oracle: count is capped at 100000; last index 99999 gives (33333,0), ceil(100000/3)=33334.
        Assert.AreEqual(100000, cells.Count);
        Assert.AreEqual((33333, 0), cells[99999]);
        Assert.AreEqual(33334, CardLayout.RowCount(100000, 3));
    }

    [TestMethod]
    public void Layout_AboveCap_HasOneHundredThousandCells()
    {
        var cells = CardLayout.Place(100001, 3);
        // oracle: count is capped at 100000; last index 99999 gives (33333,0), ceil(100000/3)=33334.
        Assert.AreEqual(100000, cells.Count);
        Assert.AreEqual((33333, 0), cells[99999]);
        Assert.AreEqual(33334, CardLayout.RowCount(100001, 3));
    }

    [TestMethod]
    public void Layout_MaximumCount_HasOneHundredThousandCells()
    {
        var cells = CardLayout.Place(int.MaxValue, 3);
        // oracle: count is capped at 100000; last index 99999 gives (33333,0), ceil(100000/3)=33334.
        Assert.AreEqual(100000, cells.Count);
        Assert.AreEqual((33333, 0), cells[99999]);
        Assert.AreEqual(33334, CardLayout.RowCount(int.MaxValue, 3));
    }

    [TestMethod]
    public void Layout_MoreColumnsThanItems_HasOneRow()
    {
        var cells = CardLayout.Place(2, int.MaxValue);
        // oracle: both indices are below the column count, so they occupy row zero.
        Assert.AreEqual((0, 0), cells[0]);
        Assert.AreEqual((0, 1), cells[1]);
        Assert.AreEqual(1, CardLayout.RowCount(2, int.MaxValue));
    }

    [TestMethod]
    public void Layout_ExactRows_DoesNotAddExtraRow()
    {
        // oracle: 6 / 3 = 2 exactly.
        Assert.AreEqual(2, CardLayout.RowCount(6, 3));
    }

    [TestMethod]
    public void Navigator_Default_IsEmptyAtVersionZero()
    {
        var n = new WidgetNavigator();
        // oracle: default count, index and version are all zero.
        Assert.AreEqual(0, n.Count);
        Assert.AreEqual(0, n.Index);
        Assert.AreEqual(0, n.Version);
    }

    [TestMethod]
    public void Navigator_NegativeCount_IsNormalized()
    {
        var n = new WidgetNavigator(-2);
        // oracle: negative constructor count becomes zero without changing initial version.
        Assert.AreEqual(0, n.Count);
        Assert.AreEqual(0, n.Index);
        Assert.AreEqual(0, n.Version);
    }

    [TestMethod]
    public void Navigator_Next_AdvancesImmediately()
    {
        var n = new WidgetNavigator(3);
        n.Next();
        // oracle: (0+1)%3=1 and one changed call increments version once.
        Assert.AreEqual(1, n.Index);
        Assert.AreEqual(1, n.Version);
        Assert.AreEqual(3, n.Count);
    }

    [TestMethod]
    public void Navigator_Next_Wraps()
    {
        var n = new WidgetNavigator(2);
        n.Next();
        n.Next();
        // oracle: 0 -> 1 -> 0 is two actual index changes.
        Assert.AreEqual(0, n.Index);
        Assert.AreEqual(2, n.Version);
    }

    [TestMethod]
    public void Navigator_Previous_WrapsImmediately()
    {
        var n = new WidgetNavigator(3);
        n.Previous();
        // oracle: (0-1+3)%3=2, one index change.
        Assert.AreEqual(2, n.Index);
        Assert.AreEqual(1, n.Version);
    }

    [TestMethod]
    public void Navigator_Previous_Decrements()
    {
        var n = new WidgetNavigator(4);
        n.Previous();
        n.Previous();
        // oracle: 0 -> 3 -> 2 is two index changes.
        Assert.AreEqual(2, n.Index);
        Assert.AreEqual(2, n.Version);
    }

    [TestMethod]
    public void Navigator_Empty_NavigationDoesNotChangeVersion()
    {
        var n = new WidgetNavigator(0);
        n.Next();
        n.Previous();
        // oracle: count <= 1 makes both navigation methods no-ops.
        Assert.AreEqual(0, n.Count);
        Assert.AreEqual(0, n.Index);
        Assert.AreEqual(0, n.Version);
    }

    [TestMethod]
    public void Navigator_Singleton_NavigationDoesNotChangeVersion()
    {
        var n = new WidgetNavigator(1);
        n.Next();
        n.Previous();
        // oracle: count <= 1 makes both navigation methods no-ops.
        Assert.AreEqual(1, n.Count);
        Assert.AreEqual(0, n.Index);
        Assert.AreEqual(0, n.Version);
    }

    [TestMethod]
    public void Navigator_GrowingCount_PreservesIndex()
    {
        var n = new WidgetNavigator(3);
        n.Next();
        n.SetCount(8);
        // oracle: index 1 remains valid; one Next and one changed SetCount produce version 2.
        Assert.AreEqual(8, n.Count);
        Assert.AreEqual(1, n.Index);
        Assert.AreEqual(2, n.Version);
    }

    [TestMethod]
    public void Navigator_ShrinkingCount_ClampsWithOneIncrement()
    {
        var n = new WidgetNavigator(5);
        n.Previous();
        n.SetCount(2);
        // oracle: index 4 clamps to 1; changing both Count and Index in SetCount adds exactly one.
        Assert.AreEqual(2, n.Count);
        Assert.AreEqual(1, n.Index);
        Assert.AreEqual(2, n.Version);
    }

    [TestMethod]
    public void Navigator_SetCountZero_ResetsIndex()
    {
        var n = new WidgetNavigator(4);
        n.Previous();
        n.SetCount(0);
        // oracle: empty count forces index zero, with one increment for SetCount.
        Assert.AreEqual(0, n.Count);
        Assert.AreEqual(0, n.Index);
        Assert.AreEqual(2, n.Version);
    }

    [TestMethod]
    public void Navigator_SetNegativeCount_NormalizesAndResets()
    {
        var n = new WidgetNavigator(3);
        n.Next();
        n.SetCount(int.MinValue);
        // oracle: negative count normalizes to zero and clamps index in one changed call.
        Assert.AreEqual(0, n.Count);
        Assert.AreEqual(0, n.Index);
        Assert.AreEqual(2, n.Version);
    }

    [TestMethod]
    public void Navigator_UnchangedCount_PreservesIndexAndVersion()
    {
        var n = new WidgetNavigator(3);
        n.Next();
        n.SetCount(3);
        // oracle: unchanged SetCount is a no-op even when index is nonzero.
        Assert.AreEqual(3, n.Count);
        Assert.AreEqual(1, n.Index);
        Assert.AreEqual(1, n.Version);
    }

    [TestMethod]
    public void Navigator_NegativeCountOnEmpty_IsNoOp()
    {
        var n = new WidgetNavigator();
        n.SetCount(-1);
        // oracle: normalized zero equals current zero, so version stays zero.
        Assert.AreEqual(0, n.Count);
        Assert.AreEqual(0, n.Index);
        Assert.AreEqual(0, n.Version);
    }

    [TestMethod]
    public void Navigator_SetPositiveCountOnEmpty_ChangesOnce()
    {
        var n = new WidgetNavigator();
        n.SetCount(3);
        // oracle: Count changes 0 -> 3 while index remains zero, adding one version.
        Assert.AreEqual(3, n.Count);
        Assert.AreEqual(0, n.Index);
        Assert.AreEqual(1, n.Version);
    }

    [TestMethod]
    public void Navigator_PreviousAtMaximumCount_DecrementsWithoutOverflow()
    {
        var n = new WidgetNavigator(int.MaxValue);
        n.Previous();
        // oracle: round-3 (e): Previous from 0 wraps to Count-1 = 2147483646.
        Assert.AreEqual(2147483646, n.Index);
        Assert.AreEqual(int.MaxValue, n.Count);
        Assert.AreEqual(1, n.Version);
        n.Previous();
        // oracle: Previous again is 2147483645; each changed call adds one version without overflowing Index-1+Count.
        Assert.AreEqual(2147483645, n.Index);
        Assert.AreEqual(2, n.Version);
    }

    [TestMethod]
    public void Navigator_NextAtMaximumCount_WrapsAndAdvancesWithoutNegativeIndex()
    {
        var n = new WidgetNavigator(int.MaxValue);
        n.Previous();
        n.Next();
        // oracle: round-3 (e): Next from Count-1 wraps to 0; Previous and Next are two changes.
        Assert.AreEqual(0, n.Index);
        Assert.AreEqual(2, n.Version);
        n.Next();
        // oracle: Next from 0 gives 1, still in [0,Count), with a third version increment.
        Assert.AreEqual(1, n.Index);
        Assert.AreEqual(3, n.Version);
        Assert.AreEqual(int.MaxValue, n.Count);
    }

}
