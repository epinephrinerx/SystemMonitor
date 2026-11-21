using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class MainStateTests
{
    [TestMethod]
    public void Defaults_MatchSpec()
    {
        var state = new MainState();

        Assert.AreEqual(PageKeys.Overall, state.Page);
        Assert.AreEqual(PageKeys.Overall, state.RememberedPage);
        Assert.IsFalse(state.SettingsOpen);
        Assert.AreEqual(MainState.DefaultSidebarWidth, state.SidebarWidth);
        Assert.IsNotNull(state.Peaks);
        Assert.AreEqual(0, state.Peaks.Count);
    }

    [TestMethod]
    public void Select_SetsCurrentAndRememberedPage_AndResetsPeaks()
    {
        var state = new MainState();
        state.Peaks.Track("cpu", 50.0, 40.0);
        Assert.AreEqual(1, state.Peaks.Count);

        state.Select("cpu");

        Assert.AreEqual("cpu", state.Page);
        Assert.AreEqual("cpu", state.RememberedPage);
        Assert.AreEqual(0, state.Peaks.Count);
    }

    [TestMethod]
    public void Select_EmptyOrNullKey_IsIgnored()
    {
        var state = new MainState("ram");

        state.Select(string.Empty);
        Assert.AreEqual("ram", state.Page);

        state.Select(null!);
        Assert.AreEqual("ram", state.Page);
    }

    [TestMethod]
    public void Reconcile_RememberedPageAvailable_SelectsRemembered()
    {
        var state = new MainState();
        var pages = new[] { PageKeys.Overall, PageKeys.Cpu, PageKeys.Ram };

        string selected = state.Reconcile(pages, remembered: PageKeys.Ram);

        Assert.AreEqual(PageKeys.Ram, selected);
        Assert.AreEqual(PageKeys.Ram, state.Page);
        Assert.AreEqual(PageKeys.Ram, state.RememberedPage);
    }

    [TestMethod]
    public void Reconcile_VanishedModule_FallsBackToOverall_AndKeepsRemembered()
    {
        var state = new MainState();
        state.Select("battery:BAT0");
        Assert.AreEqual("battery:BAT0", state.RememberedPage);

        var pagesWithoutBattery = new[] { PageKeys.Overall, PageKeys.Cpu, PageKeys.Ram };

        string selected = state.Reconcile(pagesWithoutBattery, remembered: null);

        Assert.AreEqual(PageKeys.Overall, selected);
        Assert.AreEqual(PageKeys.Overall, state.Page);
        Assert.AreEqual("battery:BAT0", state.RememberedPage, "Remembered key must be retained for replug");
    }

    [TestMethod]
    public void Reconcile_AppsAndProcesses_AlwaysValid()
    {
        var state = new MainState();
        var pages = new[] { PageKeys.Overall, PageKeys.Apps, PageKeys.Processes, PageKeys.Cpu };

        state.Select(PageKeys.Apps);
        Assert.AreEqual(PageKeys.Apps, state.Reconcile(pages, remembered: null));

        state.Select(PageKeys.Processes);
        Assert.AreEqual(PageKeys.Processes, state.Reconcile(pages, remembered: null));
    }

    [TestMethod]
    public void SidebarWidth_ClampLimits_AndDefaults()
    {
        Assert.AreEqual(200, MainState.ClampWidth(150));
        Assert.AreEqual(200, MainState.ClampWidth(200));
        Assert.AreEqual(250, MainState.ClampWidth(250));
        Assert.AreEqual(320, MainState.ClampWidth(320));
        Assert.AreEqual(320, MainState.ClampWidth(500));
        Assert.AreEqual(230, MainState.ClampWidth(0));
        Assert.AreEqual(230, MainState.ClampWidth(-20));

        var state = new MainState(sidebarWidth: 999);
        Assert.AreEqual(320, state.SidebarWidth);

        state.SetSidebarWidth(100);
        Assert.AreEqual(200, state.SidebarWidth);

        state.SetSidebarWidth(280);
        Assert.AreEqual(280, state.SidebarWidth);
    }

    [TestMethod]
    public void SettingsOpen_TogglesState()
    {
        var state = new MainState(settingsOpen: false);
        Assert.IsFalse(state.SettingsOpen);

        state.SetSettingsOpen(true);
        Assert.IsTrue(state.SettingsOpen);

        state.SetSettingsOpen(false);
        Assert.IsFalse(state.SettingsOpen);
    }

    [TestMethod]
    public void PeakTracker_Reset_ClearsAllPeaks()
    {
        var tracker = new PeakTracker();
        tracker.Track("key1", 20.0, 10.0);
        tracker.Track("key2", 40.0, 30.0);

        Assert.AreEqual(2, tracker.Count);
        Assert.IsTrue(tracker.Contains("key1"));
        Assert.AreEqual(20.0, tracker.Highest("key1"));

        tracker.Reset();

        Assert.AreEqual(0, tracker.Count);
        Assert.IsFalse(tracker.Contains("key1"));
        Assert.IsNull(tracker.Highest("key1"));
    }

    [TestMethod]
    public void PeakTracker_FirstBuildAfterReset_UsesHistoryMax()
    {
        var tracker = new PeakTracker();

        // Visible history has maximum 85, current sample is 30 -> seeds to 85
        double peak = tracker.Track("cpu", value: 30.0, historyMax: 85.0);
        Assert.AreEqual(85.0, peak);
        Assert.AreEqual(85.0, tracker.Highest("cpu"));

        // Subsequent lower value retains peak 85
        double peak2 = tracker.Track("cpu", value: 45.0, historyMax: 50.0);
        Assert.AreEqual(85.0, peak2);

        // Higher value updates peak to 95
        double peak3 = tracker.Track("cpu", value: 95.0, historyMax: 50.0);
        Assert.AreEqual(95.0, peak3);
        Assert.AreEqual(95.0, tracker.Highest("cpu"));
    }

    [TestMethod]
    public void PeakTracker_HandlesNonFiniteValues()
    {
        var tracker = new PeakTracker();

        double peak = tracker.Track("bad", double.NaN, double.PositiveInfinity);
        Assert.AreEqual(0.0, peak);
        Assert.AreEqual(0.0, tracker.Highest("bad"));
    }
}
