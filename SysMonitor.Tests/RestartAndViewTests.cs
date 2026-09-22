namespace SysMonitor.Tests;

/// <summary>
/// Two things the user asked for after living with the app: a resize must not
/// change which view is showing, and there must be a way to restart it.
/// </summary>
[TestClass]
public class RestartAndViewTests
{
    [TestMethod]
    public void Nothing_decides_a_view_from_a_size_any_more()
    {
        // The switch lived behind a `TooSmallForFull` threshold. Its absence is
        // the guarantee: if it comes back, a drag can replace the contents of
        // the window again, which is the thing being fixed.
        Assert.IsNull(typeof(WindowGeometry).GetMethod("TooSmallForFull"),
            "a size threshold that changes view is what was removed");
    }

    [TestMethod]
    public void Each_view_keeps_a_minimum_to_stop_at_instead()
    {
        // Resizing still works; it just runs out of room rather than handing
        // you a different view.
        foreach (WindowGeometry.View view in Enum.GetValues<WindowGeometry.View>())
        {
            (var min, var max) = WindowGeometry.Limits(view);
            Assert.IsTrue(min.W > 0 && min.H > 0, $"{view} has no minimum");
            Assert.IsTrue(max.W > min.W && max.H > min.H, $"{view} cannot grow");
        }
    }

    [TestMethod]
    public void The_full_view_stops_at_the_size_its_tabs_need()
    {
        (var min, _) = WindowGeometry.Limits(WindowGeometry.View.Full);
        Assert.AreEqual(AppConfig.MinFull, min);
    }

    [TestMethod]
    public void Restart_is_offered_in_both_languages()
    {
        Assert.AreNotEqual(string.Empty, new Lang("en")["restart"]);
        Assert.AreNotEqual(string.Empty, new Lang("th")["restart"]);
        Assert.AreNotEqual(new Lang("en")["restart"], new Lang("th")["restart"],
            "a key that is missing falls back, and both would read the same");
    }

    [TestMethod]
    public void Restart_is_not_the_same_command_as_closing_or_resizing()
    {
        var lang = new Lang("en");
        Assert.AreNotEqual(lang["close"], lang["restart"]);
        Assert.AreNotEqual(lang["reset_size"], lang["restart"]);
    }
}
