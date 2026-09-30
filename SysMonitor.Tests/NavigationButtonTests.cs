using SysMonitor;

namespace SysMonitor.Tests;

/// <summary>
/// The navigation buttons on the overall and full headers say where they go
/// only through their tooltips, so the strings behind them are the feature.
/// The context-menu keys that the buttons' redesign replaced on screen must
/// survive, because the right-click menu still uses them.
/// </summary>
[TestClass]
public class NavigationButtonTests
{
    private static readonly string[] NavigationKeys =
    {
        "go_overall", "open_widget", "full_data",
    };

    [TestMethod]
    public void The_navigation_keys_exist_in_both_languages()
    {
        // Lang falls back to en and finally to the key's own name, so a key
        // that is gone reads back as the key itself -- the one value that is
        // never a real translation.
        foreach (string key in NavigationKeys)
        {
            Assert.AreNotEqual(key, new Lang("th")[key],
                $"{key} is missing from the Thai strings");
            Assert.AreNotEqual(key, new Lang("en")[key],
                $"{key} is missing from the English strings");
        }
    }

    [TestMethod]
    public void The_translations_differ_so_no_key_has_fallen_back()
    {
        foreach (string key in NavigationKeys)
        {
            Assert.AreNotEqual(new Lang("th")[key], new Lang("en")[key],
                $"{key} reads the same in both languages, which is what a "
                + "missing key looks like after the fallback");
        }
    }

    [TestMethod]
    public void The_keys_are_distinct_from_each_other_and_from_close()
    {
        var lang = new Lang("en");
        Assert.AreNotEqual(lang["go_overall"], lang["open_widget"]);
        Assert.AreNotEqual(lang["go_overall"], lang["full_data"]);
        Assert.AreNotEqual(lang["open_widget"], lang["full_data"]);
        Assert.AreNotEqual(lang["go_overall"], lang["close"]);
        Assert.AreNotEqual(lang["open_widget"], lang["close"]);
    }

    [TestMethod]
    public void The_context_menu_keys_the_headers_stopped_using_survive()
    {
        foreach (string key in new[] { "collapse", "expand_hint", "fullscreen", "exit_fullscreen" })
        {
            Assert.AreNotEqual(key, new Lang("th")[key],
                $"{key} is gone from the Thai strings, but the context menu still uses it");
            Assert.AreNotEqual(key, new Lang("en")[key],
                $"{key} is gone from the English strings, but the context menu still uses it");
            // A key missing from Thai alone silently falls back to English,
            // which both key-name checks above accept -- the two translations
            // being the same is the only visible symptom.
            Assert.AreNotEqual(new Lang("th")[key], new Lang("en")[key],
                $"{key} reads the same in both languages, which is what a "
                + "Thai string falling back to English looks like");
        }
    }
}
