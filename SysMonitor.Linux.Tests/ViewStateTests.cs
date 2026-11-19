using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class ViewStateTests
{
    [TestMethod]
    public void ViewModeExtensions_ToKey_And_Parse()
    {
        Assert.AreEqual("widget", ViewMode.Widget.ToKey());
        Assert.AreEqual("overall", ViewMode.Overall.ToKey());
        Assert.AreEqual("full", ViewMode.Full.ToKey());

        Assert.AreEqual(ViewMode.Widget, ViewModeExtensions.Parse("widget", ViewMode.Full));
        Assert.AreEqual(ViewMode.Overall, ViewModeExtensions.Parse("overall", ViewMode.Widget));
        Assert.AreEqual(ViewMode.Full, ViewModeExtensions.Parse("full", ViewMode.Overall));
        Assert.AreEqual(ViewMode.Widget, ViewModeExtensions.Parse("unknown", ViewMode.Widget));
    }

    [TestMethod]
    public void DoubleClick_FollowsSpecSwitchingCycle()
    {
        var state = new ViewState(ViewMode.Widget);

        // Widget -> Overall
        Assert.IsTrue(state.DoubleClick());
        Assert.AreEqual(ViewMode.Overall, state.Mode);

        // Overall -> Full
        Assert.IsTrue(state.DoubleClick());
        Assert.AreEqual(ViewMode.Full, state.Mode);

        // Full -> Widget
        Assert.IsTrue(state.DoubleClick());
        Assert.AreEqual(ViewMode.Widget, state.Mode);
    }

    [TestMethod]
    public void F11_StepsToFull_AndBackToOverall()
    {
        var state = new ViewState(ViewMode.Widget);

        // Widget -> Full
        Assert.IsTrue(state.F11());
        Assert.AreEqual(ViewMode.Full, state.Mode);

        // Full -> Overall
        Assert.IsTrue(state.F11());
        Assert.AreEqual(ViewMode.Overall, state.Mode);

        // Overall -> Full
        Assert.IsTrue(state.F11());
        Assert.AreEqual(ViewMode.Full, state.Mode);
    }

    [TestMethod]
    public void Escape_StepsBackOneLevel_AndIsNoOpInWidget()
    {
        var state = new ViewState(ViewMode.Full);

        // Full -> Overall
        Assert.IsTrue(state.Escape());
        Assert.AreEqual(ViewMode.Overall, state.Mode);

        // Overall -> Widget
        Assert.IsTrue(state.Escape());
        Assert.AreEqual(ViewMode.Widget, state.Mode);

        // Widget -> no-op
        Assert.IsFalse(state.Escape());
        Assert.AreEqual(ViewMode.Widget, state.Mode);
    }

    [TestMethod]
    public void ContextMenu_ExpandAndFullScreen_FollowSpec()
    {
        var state = new ViewState(ViewMode.Widget);

        // ContextExpand: Widget -> Overall
        Assert.IsTrue(state.ContextExpand());
        Assert.AreEqual(ViewMode.Overall, state.Mode);

        // ContextExpand: Overall -> Widget
        Assert.IsTrue(state.ContextExpand());
        Assert.AreEqual(ViewMode.Widget, state.Mode);

        // ContextExpand: Full -> Overall
        state.Move(ViewMode.Full);
        Assert.IsTrue(state.ContextExpand());
        Assert.AreEqual(ViewMode.Overall, state.Mode);

        // ContextFullScreen: Widget -> Full
        state.Move(ViewMode.Widget);
        Assert.IsTrue(state.ContextFullScreen());
        Assert.AreEqual(ViewMode.Full, state.Mode);

        // ContextFullScreen: Full -> Overall
        Assert.IsTrue(state.ContextFullScreen());
        Assert.AreEqual(ViewMode.Overall, state.Mode);

        // ContextFullScreen: Overall -> Full
        Assert.IsTrue(state.ContextFullScreen());
        Assert.AreEqual(ViewMode.Full, state.Mode);
    }

    [TestMethod]
    public void HeaderAndSidebar_DirectNavigationMethods()
    {
        var state = new ViewState(ViewMode.Widget);

        Assert.IsTrue(state.OverallHeaderFull());
        Assert.AreEqual(ViewMode.Full, state.Mode);
        Assert.IsFalse(state.OverallHeaderFull(), "Moving to current mode should return false");

        Assert.IsTrue(state.OverallHeaderWidget());
        Assert.AreEqual(ViewMode.Widget, state.Mode);

        Assert.IsTrue(state.SidebarFullData());
        Assert.AreEqual(ViewMode.Full, state.Mode);

        Assert.IsTrue(state.SidebarOpenWidget());
        Assert.AreEqual(ViewMode.Widget, state.Mode);

        Assert.IsTrue(state.FullHeaderOverall());
        Assert.AreEqual(ViewMode.Overall, state.Mode);

        Assert.IsTrue(state.FullHeaderWidget());
        Assert.AreEqual(ViewMode.Widget, state.Mode);

        Assert.IsTrue(state.FullRailOverall());
        Assert.AreEqual(ViewMode.Overall, state.Mode);

        Assert.IsTrue(state.FullRailWidget());
        Assert.AreEqual(ViewMode.Widget, state.Mode);
    }

    [TestMethod]
    public void ViewState_Properties_ReflectCurrentMode()
    {
        var state = new ViewState(ViewMode.Widget);
        Assert.IsTrue(state.IsWidget);
        Assert.AreEqual("expand_hint", state.FirstMenuItemKey);
        Assert.AreEqual("fullscreen", state.FullScreenItemKey);

        state.Move(ViewMode.Overall);
        Assert.IsFalse(state.IsWidget);
        Assert.AreEqual("collapse", state.FirstMenuItemKey);
        Assert.AreEqual("fullscreen", state.FullScreenItemKey);

        state.Move(ViewMode.Full);
        Assert.IsFalse(state.IsWidget);
        Assert.AreEqual("expand_hint", state.FirstMenuItemKey);
        Assert.AreEqual("exit_fullscreen", state.FullScreenItemKey);
    }

    [TestMethod]
    public void Rotation_5SecondInterval_AdvancesAndWraps()
    {
        var rotation = new RotationState();
        var pages = new[]
        {
            new WidgetPage { Key = "cpu", Kind = WidgetPageKind.CpuTotal },
            new WidgetPage { Key = "ram", Kind = WidgetPageKind.Ram },
            new WidgetPage { Key = "disk", Kind = WidgetPageKind.DiskTotal },
        };
        rotation.SetPages(pages);
        rotation.ResetTimer(1000);

        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("cpu", rotation.Page?.Key);

        // Before 5 seconds: no tick
        Assert.IsFalse(rotation.Tick(nowMs: 5000, isWidgetView: true));
        Assert.IsFalse(rotation.IsDue(nowMs: 5000, isWidgetView: true));
        Assert.AreEqual(0, rotation.Index);

        // Not widget view: no tick even if time reached
        Assert.IsFalse(rotation.Tick(nowMs: 6000, isWidgetView: false));
        Assert.IsFalse(rotation.IsDue(nowMs: 6000, isWidgetView: false));
        Assert.AreEqual(0, rotation.Index);

        // Exactly 5000ms elapsed in Widget view: advances to RAM
        Assert.IsTrue(rotation.IsDue(nowMs: 6000, isWidgetView: true));
        Assert.IsTrue(rotation.Tick(nowMs: 6000, isWidgetView: true));
        Assert.AreEqual(1, rotation.Index);
        Assert.AreEqual("ram", rotation.Page?.Key);

        // Next 5s: advances to Disk
        Assert.IsTrue(rotation.Tick(nowMs: 11000, isWidgetView: true));
        Assert.AreEqual(2, rotation.Index);
        Assert.AreEqual("disk", rotation.Page?.Key);

        // Next 5s: wraps back to CPU
        Assert.IsTrue(rotation.Tick(nowMs: 16000, isWidgetView: true));
        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("cpu", rotation.Page?.Key);
    }

    [TestMethod]
    public void Rotation_Pause_PreventsRotation_AndStepKeepsPause()
    {
        var rotation = new RotationState();
        var pages = new[]
        {
            new WidgetPage { Key = "cpu", Kind = WidgetPageKind.CpuTotal },
            new WidgetPage { Key = "ram", Kind = WidgetPageKind.Ram },
        };
        rotation.SetPages(pages);
        rotation.ResetTimer(1000);

        rotation.TogglePause(nowMs: 2000);
        Assert.IsTrue(rotation.Paused);
        Assert.IsFalse(rotation.IsDue(nowMs: 8000, isWidgetView: true));
        Assert.IsFalse(rotation.Tick(nowMs: 8000, isWidgetView: true));
        Assert.AreEqual(0, rotation.Index);

        // Stepping next/prev while paused changes index but preserves pause
        rotation.Next(nowMs: 3000);
        Assert.AreEqual(1, rotation.Index);
        Assert.IsTrue(rotation.Paused);

        rotation.Prev(nowMs: 4000);
        Assert.AreEqual(0, rotation.Index);
        Assert.IsTrue(rotation.Paused);

        // Unpausing resumes rotation
        rotation.TogglePause(nowMs: 5000);
        Assert.IsFalse(rotation.Paused);
        Assert.AreEqual(5000, rotation.LastRotateMs);
    }

    [TestMethod]
    public void Rotation_SetPages_KeepsPageByKey()
    {
        var rotation = new RotationState();
        var initialPages = new[]
        {
            new WidgetPage { Key = "cpu", Kind = WidgetPageKind.CpuTotal },
            new WidgetPage { Key = "ram", Kind = WidgetPageKind.Ram },
            new WidgetPage { Key = "disk:/", Kind = WidgetPageKind.DiskOne },
        };
        rotation.SetPages(initialPages);
        rotation.Next(1000);
        rotation.Next(2000);
        Assert.AreEqual("disk:/", rotation.Page?.Key);
        Assert.AreEqual(2, rotation.Index);

        // Reinstall pages where "disk:/" is now at index 1
        var newPages = new[]
        {
            new WidgetPage { Key = "ram", Kind = WidgetPageKind.Ram },
            new WidgetPage { Key = "disk:/", Kind = WidgetPageKind.DiskOne },
            new WidgetPage { Key = "net:eth0", Kind = WidgetPageKind.NetworkOne },
        };
        rotation.SetPages(newPages);

        // Should keep page by key
        Assert.AreEqual(1, rotation.Index);
        Assert.AreEqual("disk:/", rotation.Page?.Key);

        // If key no longer exists, clamp index to valid range
        var shorterPages = new[]
        {
            new WidgetPage { Key = "cpu", Kind = WidgetPageKind.CpuTotal },
        };
        rotation.SetPages(shorterPages);
        Assert.AreEqual(0, rotation.Index);
        Assert.AreEqual("cpu", rotation.Page?.Key);
    }

    [TestMethod]
    public void Rotation_WithZeroOrOnePage_DoesNotRotate()
    {
        var rotation = new RotationState();
        rotation.SetPages(new[] { new WidgetPage { Key = "single", Kind = WidgetPageKind.Empty } });
        rotation.ResetTimer(1000);

        Assert.IsFalse(rotation.IsDue(nowMs: 10000, isWidgetView: true));
        Assert.IsFalse(rotation.Tick(nowMs: 10000, isWidgetView: true));

        rotation.Next(2000);
        Assert.AreEqual(0, rotation.Index);
        rotation.Prev(3000);
        Assert.AreEqual(0, rotation.Index);
    }
}
