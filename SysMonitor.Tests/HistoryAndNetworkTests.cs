using System.Windows.Media;
using SysMonitor;
using SysMonitor.Model;

namespace SysMonitor.Tests;

[TestClass]
public class HistoryTests
{
    [TestMethod]
    public void An_empty_history_has_nothing_to_draw()
    {
        var history = new History(8);
        Assert.AreEqual(0, history.Count);
        Assert.AreEqual(0, history.CopyTo(new double[8]));
        Assert.AreEqual(0, history.Latest);
    }

    [TestMethod]
    public void A_partly_filled_history_reads_oldest_first()
    {
        var history = new History(8);
        foreach (int value in new[] { 1, 2, 3 })
        {
            history.Add(value);
        }

        var into = new double[8];
        Assert.AreEqual(3, history.CopyTo(into));
        CollectionAssert.AreEqual(new double[] { 1, 2, 3 }, into[..3]);
        Assert.AreEqual(3, history.Latest);
    }

    [TestMethod]
    public void Past_capacity_the_oldest_readings_fall_off()
    {
        // The whole point: a widget running for days must not grow a list.
        var history = new History(4);
        for (int value = 1; value <= 10; value++)
        {
            history.Add(value);
        }

        var into = new double[4];
        Assert.AreEqual(4, history.Count);
        Assert.AreEqual(4, history.CopyTo(into));
        CollectionAssert.AreEqual(new double[] { 7, 8, 9, 10 }, into);
        Assert.AreEqual(10, history.Latest);
    }

    [TestMethod]
    public void Wrapping_many_times_still_reads_in_order()
    {
        var history = new History(3);
        for (int value = 0; value < 100; value++)
        {
            history.Add(value);
        }

        var into = new double[3];
        history.CopyTo(into);
        CollectionAssert.AreEqual(new double[] { 97, 98, 99 }, into);
    }

    [TestMethod]
    public void A_smaller_buffer_gets_the_most_recent_readings()
    {
        var history = new History(8);
        for (int value = 1; value <= 8; value++)
        {
            history.Add(value);
        }

        var into = new double[3];
        Assert.AreEqual(3, history.CopyTo(into));
        CollectionAssert.AreEqual(new double[] { 6, 7, 8 }, into);
    }

    [TestMethod]
    public void The_revision_moves_on_every_push()
    {
        var history = new History(4);
        int before = history.Revision;
        history.Add(1);
        history.Add(2);
        Assert.AreEqual(before + 2, history.Revision);
    }

    [TestMethod]
    public void Max_covers_only_what_is_still_in_the_buffer()
    {
        var history = new History(3);
        history.Add(90);            // falls off
        history.Add(10);
        history.Add(20);
        history.Add(30);
        Assert.AreEqual(30, history.Max);
    }
}

[TestClass]
public class AdapterTests
{
    private static Adapter At(double speedMbps, double downMb, double upMb) => new()
    {
        Id = "test",
        SpeedMbps = speedMbps,
        DownMb = downMb,
        UpMb = upMb,
    };

    [TestMethod]
    public void Throughput_is_measured_against_the_link_rate()
    {
        // 1 Gbps link, 12.5 MB/s down = 100 Mbps = 10%.
        Assert.AreEqual(10, At(1000, 12.5, 0).Usage);
    }

    [TestMethod]
    public void Both_directions_count_towards_the_bar()
    {
        // A saturated uplink matters as much as a saturated downlink.
        Assert.AreEqual(20, At(1000, 12.5, 12.5).Usage);
    }

    [TestMethod]
    public void An_adapter_reporting_no_link_speed_shows_no_percentage()
    {
        // Rather than dividing by zero and drawing a full bar.
        Assert.AreEqual(0, At(0, 50, 50).Usage);
    }

    [TestMethod]
    public void The_bar_cannot_exceed_full()
    {
        Assert.AreEqual(100, At(100, 50, 50).Usage);
    }
}

[TestClass]
public class ModuleTests
{
    [TestMethod]
    public void A_module_reads_as_size_type_speed_and_maker()
    {
        var module = new Module
        {
            Slot = "DIMM 0",
            Gb = 16,
            Kind = "DDR5",
            Mhz = 5600,
            Manufacturer = "Samsung",
        };
        Assert.AreEqual("DIMM 0", module.Title);
        Assert.AreEqual("16 GB · DDR5 · 5600 MHz · Samsung", module.Detail);
    }

    [TestMethod]
    public void Missing_facts_are_left_out_rather_than_shown_empty()
    {
        var module = new Module { Slot = "DIMM 1", Gb = 8 };
        Assert.AreEqual("8 GB", module.Detail);
    }

    [TestMethod]
    public void The_bank_label_names_a_module_with_no_slot()
    {
        Assert.AreEqual("BANK 2", new Module { Bank = "BANK 2", Gb = 8 }.Title);
    }
}

[TestClass]
public class ChartPenTests
{
    [TestMethod]
    public void Building_a_pen_does_not_freeze_the_brush_it_was_given()
    {
        // Freezing a Pen freezes its brush. The view model recolours its
        // brushes on every theme switch, so a chart that froze one turned the
        // next switch into a crash.
        var shared = new SolidColorBrush(Colors.SteelBlue);

        Pen pen = SysMonitor.Controls.Chart.PenFor(shared, 1.4);

        Assert.IsTrue(pen.IsFrozen, "the pen itself should still be frozen");
        Assert.IsFalse(shared.IsFrozen, "the caller's brush must stay writable");
        shared.Color = Colors.Firebrick;          // would throw before the fix
        Assert.AreEqual(Colors.Firebrick, shared.Color);
    }

    [TestMethod]
    public void The_pen_still_draws_in_the_colour_it_was_asked_for()
    {
        var shared = new SolidColorBrush(Colors.SteelBlue);
        Pen pen = SysMonitor.Controls.Chart.PenFor(shared, 1.0);
        Assert.AreEqual(Colors.SteelBlue, ((SolidColorBrush)pen.Brush).Color);
    }
}

[TestClass]
public class ModuleSummaryTests
{
    private static Module Of(double gb, string kind = "DDR4", int mhz = 2667) =>
        new() { Gb = gb, Kind = kind, Mhz = mhz };

    [TestMethod]
    public void Matching_modules_are_counted_not_listed()
    {
        // This machine: two 16 GB DDR4-2667.
        Assert.AreEqual("2 x 16 GB DDR4 2667 MHz",
            Module.Summarise(new[] { Of(16), Of(16) }));
    }

    [TestMethod]
    public void A_single_module_is_not_counted()
    {
        Assert.AreEqual("16 GB DDR4 2667 MHz", Module.Summarise(new[] { Of(16) }));
    }

    [TestMethod]
    public void Mixed_sizes_are_listed_largest_first()
    {
        // One 16 and one 8 is worth seeing, not averaging away.
        Assert.AreEqual("16 GB + 8 GB DDR4 2667 MHz",
            Module.Summarise(new[] { Of(8), Of(16) }));
    }

    [TestMethod]
    public void A_disagreeing_speed_is_left_out_rather_than_guessed()
    {
        Assert.AreEqual("2 x 16 GB DDR4",
            Module.Summarise(new[] { Of(16, mhz: 2667), Of(16, mhz: 2400) }));
    }

    [TestMethod]
    public void A_disagreeing_type_is_left_out_too()
    {
        Assert.AreEqual("2 x 16 GB 2667 MHz",
            Module.Summarise(new[] { Of(16, kind: "DDR4"), Of(16, kind: "DDR5") }));
    }

    [TestMethod]
    public void No_modules_reported_means_nothing_to_append()
    {
        Assert.AreEqual(string.Empty, Module.Summarise(Array.Empty<Module>()));
    }
}

[TestClass]
public class TrayTooltipTests
{
    [TestMethod]
    public void A_short_tooltip_is_left_alone()
    {
        const string text = "SysMonitor\nCPU 12%  ·  RAM 68%";
        Assert.AreEqual(text, TrayIcon.Trim(text));
    }

    [TestMethod]
    public void A_tooltip_at_the_limit_is_left_alone()
    {
        string text = new('x', 63);
        Assert.AreEqual(63, TrayIcon.Trim(text).Length);
    }

    [TestMethod]
    public void A_longer_tooltip_is_cut_rather_than_rejected()
    {
        // Windows does not truncate an over-long tooltip, it drops it, and
        // the icon ends up with none at all.
        string trimmed = TrayIcon.Trim(new string('x', 200));
        Assert.AreEqual(63, trimmed.Length);
        Assert.IsTrue(trimmed.EndsWith('…'));
    }
}

[TestClass]
public class ChartBrushTests
{
    [TestMethod]
    public void A_gradient_brush_is_not_frozen_by_building_a_pen()
    {
        // The first fix only cloned SolidColorBrush. Anything else went
        // straight into the frozen Pen, taking the caller's brush with it --
        // the very bug it was written to prevent.
        var gradient = new LinearGradientBrush(Colors.SteelBlue, Colors.White, 90);

        Pen pen = SysMonitor.Controls.Chart.PenFor(gradient, 1.0);

        Assert.IsTrue(pen.IsFrozen);
        Assert.IsFalse(gradient.IsFrozen, "the caller's gradient must stay writable");
        gradient.Opacity = 0.5;                      // would throw before the fix
        Assert.AreEqual(0.5, gradient.Opacity);
    }

    [TestMethod]
    public void An_already_frozen_brush_is_used_as_it_is()
    {
        var frozen = new SolidColorBrush(Colors.SteelBlue);
        frozen.Freeze();
        Pen pen = SysMonitor.Controls.Chart.PenFor(frozen, 1.0);
        Assert.AreEqual(Colors.SteelBlue, ((SolidColorBrush)pen.Brush).Color);
    }

    [TestMethod]
    public void The_same_brush_and_width_hand_back_the_same_pen()
    {
        // A repaint built three pens every frame, which is not the "allocates
        // almost nothing per frame" the class claims.
        var brush = new SolidColorBrush(Colors.Goldenrod);
        Assert.AreSame(SysMonitor.Controls.Chart.PenFor(brush, 1.2),
                       SysMonitor.Controls.Chart.PenFor(brush, 1.2));
    }

    [TestMethod]
    public void A_different_width_is_a_different_pen()
    {
        var brush = new SolidColorBrush(Colors.Goldenrod);
        Assert.AreNotSame(SysMonitor.Controls.Chart.PenFor(brush, 1.0),
                          SysMonitor.Controls.Chart.PenFor(brush, 2.0));
    }
}

[TestClass]
public class SnapshotSealingTests
{
    [TestMethod]
    public void A_published_snapshot_cannot_be_written_to_through_its_lists()
    {
        // IReadOnlyList<T> said read-only; a List<T> behind it cast straight
        // back. The snapshot crosses a thread boundary, so the claim has to
        // hold rather than merely be stated.
        var cores = new List<Core> { new() { Usage = 10 } };
        var snap = new Snapshot { Ready = true, Cores = cores };

        Assert.AreEqual(1, snap.Cores.Count);
        Assert.IsFalse(snap.Cores is List<Core>, "the list must not be handed out as itself");
        Assert.ThrowsException<NotSupportedException>(
            () => ((IList<Core>)snap.Cores).Add(new Core()));
    }

    [TestMethod]
    public void The_sampler_can_still_keep_filling_its_own_list()
    {
        // Sealing must copy nothing: the caller's list stays usable, it is
        // only the published view that is closed.
        var disks = new List<Disk> { new() { Letter = "C" } };
        var snap = new Snapshot { Ready = true, Disks = disks };
        disks.Add(new Disk { Letter = "D" });

        Assert.AreEqual(2, snap.Disks.Count, "the wrapper is a view, not a copy");
    }

    [TestMethod]
    public void An_array_is_sealed_too()
    {
        var snap = new Snapshot { Adapters = new[] { new Adapter { Id = "nic" } } };
        Assert.ThrowsException<NotSupportedException>(
            () => ((IList<Adapter>)snap.Adapters).Add(new Adapter { Id = "x" }));
    }
}
