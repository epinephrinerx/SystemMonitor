using SysMonitor;

namespace SysMonitor.Tests;

/// <summary>
/// The widget's rotation pauses on demand and resumes five seconds after the
/// resume press. The decision lives in MainWindow.ShouldRotate, a pure
/// function, so every rule is pinned here with invented times rather than a
/// live clock.
/// </summary>
[TestClass]
public class RotationPauseTests
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(5);
    private static readonly DateTime T0 = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void A_paused_widget_holds_its_page_however_long_the_user_stares()
    {
        Assert.IsFalse(MainWindow.ShouldRotate(
            inWidgetMode: true, viewCount: 4, paused: true,
            lastRotate: T0, now: T0 + TimeSpan.FromHours(2), every: Every));
    }

    [TestMethod]
    public void Rotation_happens_once_the_interval_has_passed()
    {
        Assert.IsTrue(MainWindow.ShouldRotate(
            inWidgetMode: true, viewCount: 4, paused: false,
            lastRotate: T0, now: T0 + Every, every: Every));
    }

    [TestMethod]
    public void Rotation_waits_out_the_interval()
    {
        Assert.IsFalse(MainWindow.ShouldRotate(
            inWidgetMode: true, viewCount: 4, paused: false,
            lastRotate: T0, now: T0 + Every - TimeSpan.FromMilliseconds(100),
            every: Every));
    }

    [TestMethod]
    public void Resuming_restarts_the_clock_so_the_page_does_not_jump()
    {
        // The toggle stamps lastRotate = now on resume; the next four and a
        // half seconds belong to the page the user asked to keep.
        DateTime resumed = T0 + TimeSpan.FromMinutes(3);
        Assert.IsFalse(MainWindow.ShouldRotate(
            inWidgetMode: true, viewCount: 4, paused: false,
            lastRotate: resumed, now: resumed + TimeSpan.FromSeconds(4.9),
            every: Every));
        Assert.IsTrue(MainWindow.ShouldRotate(
            inWidgetMode: true, viewCount: 4, paused: false,
            lastRotate: resumed, now: resumed + Every,
            every: Every));
    }

    [TestMethod]
    public void Only_the_widget_view_rotates()
    {
        Assert.IsFalse(MainWindow.ShouldRotate(
            inWidgetMode: false, viewCount: 4, paused: false,
            lastRotate: T0, now: T0 + TimeSpan.FromMinutes(10), every: Every));
    }

    [TestMethod]
    public void A_single_view_has_nowhere_to_rotate_to()
    {
        Assert.IsFalse(MainWindow.ShouldRotate(
            inWidgetMode: true, viewCount: 1, paused: false,
            lastRotate: T0, now: T0 + TimeSpan.FromMinutes(10), every: Every));
    }

    [TestMethod]
    public void The_pause_strings_exist_in_both_languages()
    {
        foreach (string key in new[] { "pause_rotation", "resume_rotation", "paused" })
        {
            Assert.AreNotEqual(key, new Lang("th")[key], $"{key} missing from Thai");
            Assert.AreNotEqual(key, new Lang("en")[key], $"{key} missing from English");
            Assert.AreNotEqual(new Lang("th")[key], new Lang("en")[key],
                $"{key} reads the same in both languages, which is what a "
                + "missing key looks like after the fallback");
        }
    }
}
