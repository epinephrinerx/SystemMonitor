using System;
using System.Collections.Generic;
using System.Linq;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public class RotationTests
{
    private static MetricItem Item(string key) =>
        new MetricItem(MetricKind.Cpu, key, key, "", 0, "0%", "#3b82f6", "#3b82f6");

    private static IReadOnlyList<MetricItem> Items(params string[] keys) =>
        keys.Select(Item).ToArray();

    [TestMethod]
    public void Constructor_EmptyState()
    {
        var rotation = new MiniRotation();
        // oracle: Construction starts with no items, zero index and null Current.
        Assert.AreEqual(0, rotation.Count);
        Assert.AreEqual(0, rotation.Index);
        Assert.IsNull(rotation.Current);
        // oracle: Construction starts Version at zero.
        Assert.AreEqual(0, rotation.Version);
    }

    [TestMethod]
    public void Update_FirstListSelectsFirst()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        // oracle: First non-empty Update selects index zero.
        Assert.AreEqual(3, rotation.Count);
        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("a", rotation.Current?.Key);
        // oracle: Selecting the first key changes null to a exactly once, so Version is one.
        Assert.AreEqual(1, rotation.Version);
    }

    [TestMethod]
    public void Tick_DefaultIntervalBoundary()
    {
        var rotation = new MiniRotation();
        rotation.Update(Items("a", "b"), 0);
        rotation.Tick(4999);
        // oracle: Default interval is 5000 ms: 4999 is not due, 5000 is due.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(5000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Constructor_ZeroUsesDefault()
    {
        var rotation = new MiniRotation(0);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Tick(4999);
        // oracle: Non-positive interval becomes 5000 ms.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(5000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Constructor_NegativeUsesDefault()
    {
        var rotation = new MiniRotation(-1);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Tick(4999);
        // oracle: Non-positive interval becomes 5000 ms.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(5000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Constructor_MinimumLongUsesDefault()
    {
        var rotation = new MiniRotation(long.MinValue);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Tick(4999);
        // oracle: Non-positive interval becomes 5000 ms.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(5000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Tick_CustomIntervalBoundary()
    {
        var rotation = new MiniRotation(10);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Tick(9);
        // oracle: A custom interval of 10 ms is due at 10, not at 9.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(10);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Tick_OneMillisecondInterval()
    {
        var rotation = new MiniRotation(1);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Tick(0);
        // oracle: The smallest positive interval is one millisecond.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(1);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Update_FirstCallStartsAtProvidedClock()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 100000);
        rotation.Tick(104999);
        // oracle: The first Update starts the timer at its supplied clock, not at zero.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(105000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Next_AdvancesImmediately()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Next(100);
        // oracle: Next immediately selects the following metric.
        Assert.AreEqual(1, rotation.Index);
        Assert.AreEqual("b", rotation.Current?.Key);
        // oracle: Initial selection and Next each change the selection once.
        Assert.AreEqual(2, rotation.Version);
    }

    [TestMethod]
    public void Next_WrapsToFirst()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Next(1);
        rotation.Next(2);
        rotation.Next(3);
        // oracle: Three Next calls wrap a three-item list back to index zero.
        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("a", rotation.Current?.Key);
        // oracle: Initial selection plus three moves, including the wrap, gives Version four.
        Assert.AreEqual(4, rotation.Version);
    }

    [TestMethod]
    public void Previous_WrapsFromFirst()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Previous(100);
        // oracle: Previous from index zero wraps to Count - 1.
        Assert.AreEqual(2, rotation.Index);
        Assert.AreEqual("c", rotation.Current?.Key);
        // oracle: Initial selection and the Previous wrap each increment Version once.
        Assert.AreEqual(2, rotation.Version);
    }

    [TestMethod]
    public void Previous_MovesBackOne()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Next(1);
        rotation.Next(2);
        rotation.Previous(3);
        // oracle: Previous subtracts one when not at zero.
        Assert.AreEqual(1, rotation.Index);
        Assert.AreEqual("b", rotation.Current?.Key);
        // oracle: Initial selection, two Next calls and one Previous call give four changes.
        Assert.AreEqual(4, rotation.Version);
    }

    [TestMethod]
    public void Next_RestartsTimer()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Next(1000);
        rotation.Tick(5999);
        // oracle: Next at 1000 restarts the 5000 ms timer: due at 6000.
        Assert.AreEqual(1, rotation.Index);
        rotation.Tick(6000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(2, rotation.Index);
    }

    [TestMethod]
    public void Previous_RestartsTimer()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Previous(1000);
        rotation.Tick(5999);
        // oracle: Previous at 1000 restarts the timer and the next due tick wraps from c to a.
        Assert.AreEqual(2, rotation.Index);
        rotation.Tick(6000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(0, rotation.Index);
    }

    [TestMethod]
    public void Next_EmptyIsNoOp()
    {
        var rotation = new MiniRotation();
        rotation.Next(100);
        // oracle: Next on an empty list leaves its empty selection unchanged.
        Assert.AreEqual(0, rotation.Count);
        Assert.AreEqual(0, rotation.Index);
        Assert.IsNull(rotation.Current);
        // oracle: Next with Count zero cannot change key or index and leaves Version zero.
        Assert.AreEqual(0, rotation.Version);
    }

    [TestMethod]
    public void Previous_EmptyIsNoOp()
    {
        var rotation = new MiniRotation();
        rotation.Previous(100);
        // oracle: Previous on an empty list leaves its empty selection unchanged.
        Assert.AreEqual(0, rotation.Count);
        Assert.AreEqual(0, rotation.Index);
        Assert.IsNull(rotation.Current);
        // oracle: Previous with Count zero cannot change key or index and leaves Version zero.
        Assert.AreEqual(0, rotation.Version);
    }

    [TestMethod]
    public void Next_SingleIsNoOp()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a"), 0);
        rotation.Next(100);
        // oracle: Next cannot move a single-item selection.
        Assert.AreEqual(1, rotation.Count);
        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("a", rotation.Current?.Key);
        // oracle: Initial selection increments once; Next with Count one does not increment Version.
        Assert.AreEqual(1, rotation.Version);
    }

    [TestMethod]
    public void Previous_SingleIsNoOp()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a"), 0);
        rotation.Previous(100);
        // oracle: Previous cannot move a single-item selection.
        Assert.AreEqual(1, rotation.Count);
        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("a", rotation.Current?.Key);
        // oracle: Initial selection increments once; Previous with Count one does not increment Version.
        Assert.AreEqual(1, rotation.Version);
    }

    [TestMethod]
    public void Next_SingleStillRestartsTimer()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a"), 0);
        rotation.Next(1000);
        rotation.Update(Items("a", "b"), 2000);
        rotation.Tick(5999);
        // oracle: Next on one item at 1000 still resets the timer; retaining a on Update does not reset it.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(6000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Previous_SingleStillRestartsTimer()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a"), 0);
        rotation.Previous(1000);
        rotation.Update(Items("a", "b"), 2000);
        rotation.Tick(5999);
        // oracle: Previous on one item at 1000 still resets the timer.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(6000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Tick_EmptyStaysEmpty()
    {
        var rotation = new MiniRotation();
        rotation.Update(null!, 0);
        rotation.Tick(long.MaxValue);
        // oracle: A due tick cannot advance an empty list.
        Assert.AreEqual(0, rotation.Count);
        Assert.AreEqual(0, rotation.Index);
        Assert.IsNull(rotation.Current);
        // oracle: Empty Update and due Tick leave both key and index unchanged.
        Assert.AreEqual(0, rotation.Version);
    }

    [TestMethod]
    public void Tick_SingleStaysSelected()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a"), 0);
        rotation.Tick(5000);
        // oracle: A due tick cannot advance a one-item list.
        Assert.AreEqual(1, rotation.Count);
        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("a", rotation.Current?.Key);
        // oracle: The due Tick on one item cannot change the selection or its Version.
        Assert.AreEqual(1, rotation.Version);
    }

    [TestMethod]
    public void Tick_SingleDueStillRestartsTimer()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a"), 0);
        rotation.Tick(5000);
        rotation.Update(Items("a", "b"), 6000);
        rotation.Tick(9999);
        // oracle: Due tick with one item resets at 5000, so adding b with a retained is due at 10000.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(10000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Tick_LongDelayAdvancesExactlyOne()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Tick(25000);
        // oracle: A delay of five intervals still advances only one item.
        Assert.AreEqual(1, rotation.Index);
        Assert.AreEqual("b", rotation.Current?.Key);
        // oracle: A long-delay Tick makes just one change after the initial selection.
        Assert.AreEqual(2, rotation.Version);
    }

    [TestMethod]
    public void Tick_DueResetsToNow()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Tick(25000);
        rotation.Tick(29999);
        // oracle: The long-delay tick resets to now = 25000, not to a scheduled interval boundary.
        Assert.AreEqual(1, rotation.Index);
        rotation.Tick(30000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(2, rotation.Index);
    }

    [TestMethod]
    public void Tick_RepeatedSameTimeDoesNotAdvanceAgain()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Tick(5000);
        rotation.Tick(5000);
        // oracle: The first tick at 5000 resets the timer; the second has elapsed zero.
        Assert.AreEqual(1, rotation.Index);
        // oracle: Only initial selection and the first due Tick change Version.
        Assert.AreEqual(2, rotation.Version);
    }

    [TestMethod]
    public void Tick_WrapsFromLast()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Previous(0);
        rotation.Tick(5000);
        // oracle: A due tick wraps Count - 1 back to zero.
        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("a", rotation.Current?.Key);
        // oracle: Initial selection, Previous and the due Tick wrap each increment once.
        Assert.AreEqual(3, rotation.Version);
    }

    [TestMethod]
    public void Update_FollowsKeyToNewIndex()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Next(100);
        rotation.Update(Items("b", "c", "a"), 1000);
        // oracle: Update follows the current key b from index one to index zero.
        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("b", rotation.Current?.Key);
        // oracle: Initial selection, Next and following b to another index each change Version once.
        Assert.AreEqual(3, rotation.Version);
    }

    [TestMethod]
    public void Update_FollowingMovedKeyKeepsTimer()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Next(100);
        rotation.Update(Items("b", "c", "a"), 1000);
        rotation.Tick(5099);
        // oracle: Following b to another index preserves the timer started by Next at 100.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(5100);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Update_SameIndexSameKeyKeepsTimer()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Update(Items("a", "d"), 4000);
        rotation.Tick(4999);
        // oracle: Retaining a at index zero does not restart the initial timer.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(5000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Update_ReplacesCurrentObjectWithMatchingKey()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        var replacement = new MetricItem(MetricKind.Ram, "a", "New title", "New subtitle", 17, "17%", "#a855f7", "#a855f7");
        rotation.Update(new[] { replacement, Item("b") }, 1000);
        // oracle: Update replaces the metric objects; following a key selects the replacement object.
        Assert.AreSame(replacement, rotation.Current);
        Assert.AreEqual("New title", rotation.Current?.Title);
        // oracle: Replacing the object with the same key at the same index leaves Version unchanged.
        Assert.AreEqual(1, rotation.Version);
    }

    [TestMethod]
    public void Update_MissingKeyClampsToLast()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Previous(0);
        rotation.Update(Items("x", "y"), 1000);
        // oracle: Missing c at index two clamps to new Count - 1 = 1.
        Assert.AreEqual(2, rotation.Count);
        Assert.AreEqual(1, rotation.Index);
        Assert.AreEqual("y", rotation.Current?.Key);
        // oracle: The clamping Update changes both key and index but adds exactly one after the first two changes.
        Assert.AreEqual(3, rotation.Version);
    }

    [TestMethod]
    public void Update_MissingKeyKeepsInRangeIndex()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Next(0);
        rotation.Update(Items("x", "y", "z"), 1000);
        // oracle: Missing b leaves the in-range index one and selects y.
        Assert.AreEqual(1, rotation.Index);
        Assert.AreEqual("y", rotation.Current?.Key);
        // oracle: Re-targeting b to y increments once even though the index stays one.
        Assert.AreEqual(3, rotation.Version);
    }

    [TestMethod]
    public void Update_KeyChangedAtSameIndexRestartsTimer()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Next(0);
        rotation.Update(Items("x", "y", "z"), 4000);
        rotation.Tick(8999);
        // oracle: The key changes from b to y at the same index, so the timer restarts at 4000.
        Assert.AreEqual(1, rotation.Index);
        rotation.Tick(9000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(2, rotation.Index);
    }

    [TestMethod]
    public void Update_ClampedSelectionRestartsTimer()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Previous(0);
        rotation.Update(Items("x", "y"), 4000);
        rotation.Tick(8999);
        // oracle: Clamping c to y changes the selected key and restarts at 4000.
        Assert.AreEqual(1, rotation.Index);
        rotation.Tick(9000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(0, rotation.Index);
    }

    [TestMethod]
    public void Update_NullClearsSelection()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Next(10);
        rotation.Update(null!, 100);
        // oracle: Null replacement list is empty and index becomes zero.
        Assert.AreEqual(0, rotation.Count);
        Assert.AreEqual(0, rotation.Index);
        Assert.IsNull(rotation.Current);
        // oracle: Initial selection, Next, and clearing both key and index give exactly three changes.
        Assert.AreEqual(3, rotation.Version);
    }

    [TestMethod]
    public void Update_EmptyClearsSelection()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Previous(10);
        rotation.Update(Array.Empty<MetricItem>(), 100);
        // oracle: An empty replacement clears Current and resets Index to zero.
        Assert.AreEqual(0, rotation.Count);
        Assert.AreEqual(0, rotation.Index);
        Assert.IsNull(rotation.Current);
        // oracle: Initial selection, Previous and clearing the selection give exactly three changes.
        Assert.AreEqual(3, rotation.Version);
    }

    [TestMethod]
    public void Update_EmptyToNonemptyRestartsTimer()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Update(Array.Empty<MetricItem>(), 1000);
        rotation.Update(Items("a", "b"), 4000);
        rotation.Tick(8999);
        // oracle: Transition from empty to non-empty restarts the timer at 4000.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(9000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Update_FirstEmptyThenNonemptyStartsFresh()
    {
        var rotation = new MiniRotation();
        rotation.Update(null!, 0);
        rotation.Update(Items("a", "b"), 10000);
        rotation.Tick(14999);
        // oracle: Non-empty selection after the first empty Update starts at 10000.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(15000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Update_DuplicateKeyFollowsFirstMatch()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "b"), 0);
        rotation.Previous(0);
        rotation.Update(Items("b", "x", "b"), 1000);
        // oracle: When b occurs twice, Update follows the first b even if the second b has the old index.
        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("b", rotation.Current?.Key);
        // oracle: Following the first duplicate changes index from two to zero while retaining b, incrementing once.
        Assert.AreEqual(3, rotation.Version);
    }

    [TestMethod]
    public void Update_DuplicateFollowDoesNotRestart()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "b"), 0);
        rotation.Previous(0);
        rotation.Update(Items("b", "x", "b"), 4000);
        rotation.Tick(4999);
        // oracle: Moving to the first matching duplicate preserves the selected key and timer.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(5000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Update_KeyComparisonIsCaseSensitive()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "B"), 0);
        rotation.Next(0);
        rotation.Update(Items("b", "x"), 1000);
        // oracle: B does not ordinally equal b; absent key falls back to the old in-range index.
        Assert.AreEqual(1, rotation.Index);
        Assert.AreEqual("x", rotation.Current?.Key);
    }

    [TestMethod]
    public void Update_DoesNotSanitiseMetricKeys()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a ", "b"), 0);
        rotation.Update(Items("a", "a "), 1000);
        // oracle: MiniRotation compares MetricItem keys exactly; sanitisation belongs to builders.
        Assert.AreEqual(1, rotation.Index);
        Assert.AreEqual("a ", rotation.Current?.Key);
    }

    [TestMethod]
    public void Tick_BackwardsDoesNotAdvance()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 10000);
        rotation.Tick(0);
        // oracle: A backwards clock cannot advance rotation.
        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("a", rotation.Current?.Key);
        // oracle: The backwards Tick does not change key, index or Version after initial selection.
        Assert.AreEqual(1, rotation.Version);
    }

    [TestMethod]
    public void Tick_BackwardsDoesNotReplaceTimer()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 10000);
        rotation.Tick(0);
        rotation.Tick(14999);
        // oracle: A backwards tick leaves the last-change clock at 10000.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(15000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Tick_NegativeClocksAreValid()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), -10000);
        rotation.Tick(-5001);
        // oracle: Elapsed time is 4999 then 5000 even though the clocks are negative.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(-5000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Tick_MinToMaxDoesNotOverflow()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), long.MinValue);
        rotation.Tick(long.MaxValue);
        // oracle: Max - Min is positive and greater than 5000; arithmetic must not overflow.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Tick_MaxToMinIsBackwards()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), long.MaxValue);
        rotation.Tick(long.MinValue);
        // oracle: Min < Max is backwards even if a signed subtraction would overflow.
        Assert.AreEqual(0, rotation.Index);
    }

    [TestMethod]
    public void Tick_NearMinimumClockExactBoundary()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), long.MinValue);
        rotation.Tick(long.MinValue + 4999);
        // oracle: At long.MinValue, exact elapsed times 4999 and 5000 straddle the interval.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(long.MinValue + 5000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Tick_NearMaximumClockExactBoundary()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), long.MaxValue - 5000);
        rotation.Tick(long.MaxValue - 1);
        // oracle: Near long.MaxValue, elapsed 4999 is not due and elapsed 5000 is due.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(long.MaxValue);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Tick_MaximumIntervalExactBoundary()
    {
        var rotation = new MiniRotation(long.MaxValue);
        rotation.Update(Items("a", "b", "c"), 0);
        rotation.Tick(long.MaxValue - 1);
        // oracle: A positive long.MaxValue interval is preserved; exact equality is due.
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(long.MaxValue);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Tick_MaximumIntervalAcrossZero()
    {
        var rotation = new MiniRotation(long.MaxValue);
        rotation.Update(Items("a", "b", "c"), long.MinValue);
        rotation.Tick(0);
        // oracle: 0 - long.MinValue = 9223372036854775808, greater than long.MaxValue without overflow.
        Assert.AreEqual(1, rotation.Index);
    }

    [TestMethod]
    public void Next_BackwardsClockRestartsAtProvidedTime()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 10000);
        rotation.Next(-1000);
        rotation.Tick(3999);
        // oracle: Next explicitly restarts at its supplied -1000 clock; due at 4000.
        Assert.AreEqual(1, rotation.Index);
        rotation.Tick(4000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(2, rotation.Index);
    }

    [TestMethod]
    public void Previous_BackwardsClockRestartsAtProvidedTime()
    {
        var rotation = new MiniRotation(5000);
        rotation.Update(Items("a", "b", "c"), 10000);
        rotation.Previous(-1000);
        rotation.Tick(3999);
        // oracle: Previous explicitly restarts at its supplied -1000 clock; due at 4000.
        Assert.AreEqual(2, rotation.Index);
        rotation.Tick(4000);
        // oracle: The first due tick advances exactly once.
        Assert.AreEqual(0, rotation.Index);
    }


    [TestMethod]
    public void Version_EmptyUpdatesAndCallsRemainZero()
    {
        var rotation = new MiniRotation(10);
        rotation.Update(null!, 0);
        rotation.Next(1);
        rotation.Previous(2);
        rotation.Tick(12);
        rotation.Update(Array.Empty<MetricItem>(), 20);
        // oracle: Each operation leaves the absent key and index zero unchanged; Version stays zero.
        Assert.AreEqual(0, rotation.Version);
        Assert.AreEqual(0, rotation.Index);
        Assert.IsNull(rotation.Current);
    }

    [TestMethod]
    public void Version_SameKeyAndIndexRemainUnchangedAcrossUpdates()
    {
        var rotation = new MiniRotation(10);
        rotation.Update(Items("a", "b"), 0);
        // oracle: Null to a is the first selection change.
        Assert.AreEqual(1, rotation.Version);
        rotation.Update(Items("a", "c", "d"), 1);
        // oracle: Count, other keys and object identities changed; selected key a and index zero did not.
        Assert.AreEqual(1, rotation.Version);
        rotation.Update(Items("a"), 2);
        rotation.Next(3);
        rotation.Previous(4);
        rotation.Tick(14);
        rotation.Tick(14);
        // oracle: Shrinking with a retained and every Count-one call leave the same selection, so Version stays one.
        Assert.AreEqual(1, rotation.Version);
        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("a", rotation.Current?.Key);
    }

    [TestMethod]
    public void Version_DuplicateKeysStillIncrementForEveryIndexMove()
    {
        var rotation = new MiniRotation(10);
        rotation.Update(Items("a", "a"), 0);
        // oracle: The initial selection changes the absent key to a.
        Assert.AreEqual(1, rotation.Version);
        rotation.Next(1);
        // oracle: a at index zero to a at index one changes only index, adding exactly one.
        Assert.AreEqual(2, rotation.Version);
        Assert.AreEqual(1, rotation.Index);
        rotation.Previous(2);
        // oracle: Returning to index zero adds exactly one although the key is still a.
        Assert.AreEqual(3, rotation.Version);
        Assert.AreEqual(0, rotation.Index);
        rotation.Tick(12);
        // oracle: A due Tick changes index to one and increments exactly once.
        Assert.AreEqual(4, rotation.Version);
        Assert.AreEqual(1, rotation.Index);
        rotation.Update(Items("a", "a"), 13);
        // oracle: Update follows the first matching a, moving back to zero and adding one.
        Assert.AreEqual(5, rotation.Version);
        Assert.AreEqual(0, rotation.Index);
        rotation.Update(Items("a", "a"), 14);
        // oracle: Following the first a again changes neither key nor index.
        Assert.AreEqual(5, rotation.Version);
    }

    [TestMethod]
    public void Version_EmptyTransitionsAndSameIndexRetargetIncrementOnce()
    {
        var rotation = new MiniRotation();
        rotation.Update(Items("a"), 0);
        // oracle: Absent key to a at index zero adds one.
        Assert.AreEqual(1, rotation.Version);
        rotation.Update(null!, 1);
        // oracle: Clearing a changes the selected key even though the index remains zero.
        Assert.AreEqual(2, rotation.Version);
        rotation.Update(Array.Empty<MetricItem>(), 2);
        // oracle: Empty to empty changes nothing.
        Assert.AreEqual(2, rotation.Version);
        rotation.Update(Items("b"), 3);
        // oracle: Absent key to b adds one.
        Assert.AreEqual(3, rotation.Version);
        rotation.Update(Items("c"), 4);
        // oracle: b to c at the same index adds one.
        Assert.AreEqual(4, rotation.Version);
        rotation.Update(Items("c"), 5);
        // oracle: New item object with the same key and index adds nothing.
        Assert.AreEqual(4, rotation.Version);
    }

    [TestMethod]
    public void Version_TickNotDueAndBackwardsCallsDoNotIncrement()
    {
        var rotation = new MiniRotation(10);
        rotation.Update(Items("a", "b"), 100);
        rotation.Tick(109);
        rotation.Tick(99);
        // oracle: Neither the not-due nor backwards Tick moves from the initial a.
        Assert.AreEqual(1, rotation.Version);
        rotation.Tick(110);
        // oracle: At elapsed ten Tick moves once, adding exactly one.
        Assert.AreEqual(2, rotation.Version);
        Assert.AreEqual("b", rotation.Current?.Key);
        rotation.Tick(110);
        rotation.Tick(119);
        // oracle: Repeated and not-due ticks keep the same selection and Version.
        Assert.AreEqual(2, rotation.Version);
        rotation.Tick(120);
        // oracle: The due wrap from b to a adds exactly one.
        Assert.AreEqual(3, rotation.Version);
        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("a", rotation.Current?.Key);
    }

}
