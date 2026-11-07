using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SysMonitor.Linux.App;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.App.Tests;

[TestClass]
[DoNotParallelize]
public class ViewTests
{
    [TestMethod]
    [DataRow("mini", ViewMode.Mini)]
    [DataRow("expanded", ViewMode.Expanded)]
    [DataRow("full", ViewMode.Full)]
    public void LightModes_ShowExactlyOneHost_AndOpaqueWhiteTopRight(string mode, ViewMode expected) => Ui.Run(() =>
    {
        // Leave FullW/FullH unset here so headless work-area sizing is exercised without assuming a size.
        var vm = new MainViewModel(new AppSettings { Opacity = 1.0, Mode = mode });
        var w = new MainWindow(vm, null, null);
        try
        {
            w.Show();
            vm.Apply(Sample(), 100);
            TestVisuals.Pump(w);
            Assert.AreSame(vm, w.DataContext);
            Assert.AreEqual("SysMonitor", w.Title);
            AssertHosts(w, expected);
            Assert.AreEqual(WindowState.Normal, w.WindowState);
            var pixels = TestVisuals.Capture(w);
            Assert.AreEqual(Color.FromArgb(255, 255, 255, 255), pixels.At(pixels.Width - 3, 2));
            Assert.AreEqual(Colors.White, TestVisuals.BrushColor(w.Background));
            Assert.AreEqual(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
            if (expected == ViewMode.Full)
            {
                Assert.IsTrue(w.ClientSize.Width >= 640 && w.ClientSize.Height >= 480);
                Assert.AreEqual(0d, w.MinWidth);
                Assert.AreEqual(0d, w.MinHeight);
            }
            else
            {
                Assert.AreEqual(expected == ViewMode.Mini ? 200d : 470d, w.MinWidth);
                Assert.AreEqual(expected == ViewMode.Mini ? 64d : 300d, w.MinHeight);
            }
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void ModeChanges_ApplyConfiguredClientSizes_AndSwapHosts() => Ui.Run(() =>
    {
        var vm = CreateVm("mini");
        var w = new MainWindow(vm, null, null);
        try
        {
            w.Show();
            vm.Apply(Sample(), 100);
            foreach (var (mode, width, height) in new[]
            {
                (ViewMode.Mini, 270d, 104d), (ViewMode.Expanded, 630d, 480d),
                (ViewMode.Full, 900d, 700d), (ViewMode.Mini, 270d, 104d)
            })
            {
                vm.SetMode(mode);
                TestVisuals.Pump(w);
                AssertHosts(w, mode);
                Assert.AreEqual(width, w.ClientSize.Width, 1d);
                Assert.AreEqual(height, w.ClientSize.Height, 1d);
                Assert.AreEqual(w.ClientSize.Width, vm.WindowWidth, 1d);
                Assert.AreEqual(w.ClientSize.Height, vm.WindowHeight, 1d);
            }
        }
        finally { w.Close(); }
    });

    [TestMethod]
    [DataRow("SetMode")]
    [DataRow("FullDataButton")]
    [DataRow("CardDoubleTap")]
    public void FullBelowThreshold_FallsBack_AndReentersWithMinimumClientSize(string trigger) => Ui.Run(() =>
    {
        var vm = new MainViewModel(new AppSettings { Mode = "full", Opacity = 1, FullW = 600, FullH = 400 });
        var w = new MainWindow(vm, null, null);
        try
        {
            w.Show();
            vm.Apply(Sample(), 100);
            TestVisuals.Pump(w);
            // The TESTS section explicitly requires this VM-driven fallback as well as a real resize below.
            vm.SetWindowSize(600, 400);
            TestVisuals.Pump(w);
            Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
            AssertHosts(w, ViewMode.Expanded);
            w.Width = 600;
            w.Height = 400;
            TestVisuals.Pump(w);
            Assert.AreEqual(600d, vm.Settings.FullW, 1d);
            Assert.AreEqual(400d, vm.Settings.FullH, 1d);
            Assert.AreEqual(630d, vm.Settings.ExpW);
            Assert.AreEqual(480d, vm.Settings.ExpH);
            AssertHosts(w, ViewMode.Expanded);
            if (trigger == "SetMode") vm.SetMode(ViewMode.Full);
            else if (trigger == "FullDataButton") Click(TestVisuals.Named<Button>(w, "FullDataButton"));
            else DoubleTap(ExpandedCards(w)[0]);
            TestVisuals.Pump(w);
            Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
            AssertHosts(w, ViewMode.Full);
            Assert.IsTrue(w.ClientSize.Width >= 640 && w.ClientSize.Height >= 480);
            Assert.IsTrue(vm.WindowWidth >= 640 && vm.WindowHeight >= 480);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void ExpandedCards_FormTwoColumns_AndFullDataPrecedesSettingsAndScroll() => Ui.Run(() =>
    {
        var vm = CreateVm("expanded");
        var w = new MainWindow(vm, null, null);
        try
        {
            w.Show();
            vm.Apply(Sample(), 100);
            TestVisuals.Pump(w);
            var cards = ExpandedCards(w);
            Assert.AreEqual(vm.Items.Count, cards.Length);
            Assert.IsTrue(cards.Length >= 3);
            var a = TestVisuals.RectInWindow(cards[0], w);
            var b = TestVisuals.RectInWindow(cards[1], w);
            var c = TestVisuals.RectInWindow(cards[2], w);
            Assert.AreEqual(a.Top, b.Top, 0.5);
            Assert.IsTrue(a.Left < b.Left);
            Assert.IsTrue(c.Top > a.Top);
            var host = TestVisuals.Named<Control>(w, "ExpandedHost");
            var grid = host.GetVisualDescendants().OfType<UniformGrid>().Single(g => g.Columns == 2);
            Assert.IsTrue(grid.Bounds.Width > 0);
            var button = TestVisuals.Named<Button>(w, "FullDataButton");
            Assert.AreEqual("Full Data", button.Content);
            var full = TestVisuals.RectInWindow(button, w);
            foreach (var name in new[] { "ThemeToggle", "TopmostToggle", "OpacitySlider", "SpeedBox" })
            {
                var setting = TestVisuals.RectInWindow(TestVisuals.Named<Control>(w, name), w);
                Assert.IsTrue(full.Bottom <= setting.Top + 0.5, name + " must be below Full Data");
            }
            Assert.IsTrue(full.Left <= TestVisuals.RectInWindow(TestVisuals.Named<Control>(w, "ThemeToggle"), w).Left + 0.5);
            Assert.IsTrue(full.Right <= TestVisuals.RectInWindow(TestVisuals.Named<ScrollViewer>(w, "ItemsScroll"), w).Left + 0.5);
            var sidebar = TestVisuals.Named<Control>(host, "Sidebar");
            Assert.AreEqual(150d, sidebar.Bounds.Width, 0.5);
            var sidebarRect = TestVisuals.RectInWindow(sidebar, w);
            var frame = TestVisuals.Capture(w);
            Assert.AreEqual(Color.Parse(vm.Colors.Sidebar), frame.At((int)sidebarRect.Left + 2, (int)sidebarRect.Bottom - 3));
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void TabStrip_HasOrderedHeaders_AndSynchronizesSelectionBothWays() => Ui.Run(() =>
    {
        var vm = CreateVm("full");
        var w = new MainWindow(vm, null, null);
        try
        {
            w.Show();
            vm.Apply(Sample(), 100);
            TestVisuals.Pump(w);
            var strip = TestVisuals.Named<TabControl>(w, "TabStrip");
            CollectionAssert.AreEqual(new[] { "cpu", "memory", "disk", "disk:/", "disk:/data" }, vm.Tabs.Select(t => t.Key).ToArray());
            Assert.AreEqual(vm.Tabs.Count, strip.Items.Count);
            var containers = strip.GetVisualDescendants().OfType<TabItem>().ToArray();
            Assert.AreEqual(vm.Tabs.Count, containers.Length);
            for (var i = 0; i < vm.Tabs.Count; i++)
            {
                Assert.AreEqual(vm.Tabs[i], strip.Items[i]);
                Assert.AreSame(containers[i], strip.ContainerFromIndex(i));
                var headerTexts = containers[i].GetVisualDescendants().OfType<TextBlock>();
                Assert.IsTrue(headerTexts.Any(t => t.Text == vm.Tabs[i].Title), "Missing tab header " + vm.Tabs[i].Title);
                if (i > 0)
                    Assert.AreEqual(TestVisuals.RectInWindow(containers[0], w).Top, TestVisuals.RectInWindow(containers[i], w).Top, 0.5);
            }
            vm.SelectedTabKey = "memory";
            TestVisuals.Pump(w);
            Assert.AreEqual(1, strip.SelectedIndex);
            Assert.AreEqual("memory", vm.Settings.FullTab);
            strip.SelectedIndex = 4;
            TestVisuals.Pump(w);
            Assert.AreEqual("disk:/data", vm.SelectedTabKey);
            Assert.AreEqual(vm.Tabs[4], vm.SelectedTab);
            Assert.AreEqual("disk:/data", vm.Settings.FullTab);
            Click(TestVisuals.Named<Button>(w, "BackButton"));
            TestVisuals.Pump(w);
            AssertHosts(w, ViewMode.Expanded);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ApplyNewSnapshot_PreservesSelectedDiskTab_AcrossReplacementAndCollectionReset(bool addDisk) => Ui.Run(() =>
    {
        var vm = CreateVm("full");
        var w = new MainWindow(vm, null, null);
        try
        {
            w.Show();
            vm.Apply(Sample(), 100);
            TestVisuals.Pump(w);
            var strip = TestVisuals.Named<TabControl>(w, "TabStrip");
            strip.SelectedIndex = 4;
            TestVisuals.Pump(w);
            Assert.AreEqual("disk:/data", vm.SelectedTabKey);
            vm.Apply(Sample(67, addDisk), 200);
            TestVisuals.Pump(w);
            Assert.AreEqual("disk:/data", vm.SelectedTabKey);
            Assert.AreEqual("disk:/data", vm.Settings.FullTab);
            Assert.AreEqual(4, strip.SelectedIndex);
            Assert.AreEqual(vm.Tabs[4], strip.SelectedItem);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void PersistedDiskTab_SurvivesShowBeforeFirstSnapshot() => Ui.Run(() =>
    {
        var settings = new AppSettings { Opacity = 1, Mode = "full", FullTab = "disk:/data", FullW = 900, FullH = 700 };
        var vm = new MainViewModel(settings);
        var w = new MainWindow(vm, null, null);
        try
        {
            w.Show();
            TestVisuals.Pump(w);
            Assert.AreEqual("disk:/data", vm.SelectedTabKey);
            vm.Apply(Sample(), 100);
            TestVisuals.Pump(w);
            Assert.AreEqual("disk:/data", vm.SelectedTabKey);
            Assert.AreEqual(4, TestVisuals.Named<TabControl>(w, "TabStrip").SelectedIndex);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void Mini_EmptyState_ThenCurrentCard_AndNavigationWraps() => Ui.Run(() =>
    {
        var vm = CreateVm("mini");
        var w = new MainWindow(vm, null, null);
        try
        {
            w.Show();
            TestVisuals.Pump(w);
            var mini = TestVisuals.Named<Control>(w, "MiniHost");
            var empty = TestVisuals.Named<TextBlock>(mini, "EmptyText");
            var card = TestVisuals.Named<MeterCard>(mini, "Card");
            Assert.IsTrue(empty.IsVisible);
            Assert.AreEqual("No data", empty.Text);
            Assert.IsFalse(card.IsVisible);
            vm.Apply(Sample(), 100);
            TestVisuals.Pump(w);
            Assert.IsFalse(empty.IsVisible);
            Assert.IsTrue(card.IsVisible);
            Assert.AreEqual(vm.Current, card.DataContext);
            var next = TestVisuals.Named<Button>(mini, "NextButton");
            var previous = TestVisuals.Named<Button>(mini, "PrevButton");
            Assert.AreEqual("\u25b6", next.Content);
            Assert.AreEqual("\u25c0", previous.Content);
            for (var i = 1; i <= vm.Items.Count; i++)
            {
                Click(next);
                TestVisuals.Pump(w);
                Assert.AreEqual(vm.Items[i % vm.Items.Count], vm.Current);
                Assert.AreEqual(vm.Current, card.DataContext);
            }
            Click(previous);
            Assert.AreEqual(vm.Items[^1], vm.Current);
            Click(next);
            Assert.AreEqual(vm.Items[0], vm.Current);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void DoubleTap_OnNavigationButtonOrItsChild_DoesNotCycle_ButCardBodyDoes() => Ui.Run(() =>
    {
        var vm = CreateVm("mini");
        var w = new MainWindow(vm, null, null);
        try
        {
            w.Show();
            vm.Apply(Sample(), 100);
            TestVisuals.Pump(w);
            var next = TestVisuals.Named<Button>(w, "NextButton");
            var before = vm.Current;
            Click(next);
            Assert.AreNotEqual(before, vm.Current);
            DoubleTap(next);
            var child = next.GetVisualDescendants().OfType<TextBlock>().First();
            DoubleTap(child);
            Assert.AreEqual(ViewMode.Mini, vm.RequestedMode);
            DoubleTap(TestVisuals.Named<MeterCard>(TestVisuals.Named<Control>(w, "MiniHost"), "Card"));
            TestVisuals.Pump(w);
            Assert.AreEqual(ViewMode.Expanded, vm.RequestedMode);
            AssertHosts(w, ViewMode.Expanded);
            DoubleTap(ExpandedCards(w)[0]);
            TestVisuals.Pump(w);
            Assert.AreEqual(ViewMode.Full, vm.RequestedMode);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void ExpandedInteractiveControls_AndTheirChildren_SuppressModeDoubleTap() => Ui.Run(() =>
    {
        var vm = CreateVm("expanded");
        var w = new MainWindow(vm, null, null);
        try
        {
            w.Show();
            vm.Apply(Sample(), 100);
            TestVisuals.Pump(w);
            foreach (var name in new[] { "FullDataButton", "ThemeToggle", "TopmostToggle", "OpacitySlider", "SpeedBox" })
            {
                var control = TestVisuals.Named<Control>(w, name);
                DoubleTap(control);
                var child = control.GetVisualDescendants().OfType<Control>().FirstOrDefault();
                if (child != null) DoubleTap(child);
                Assert.AreEqual(ViewMode.Expanded, vm.RequestedMode, name);
            }
            foreach (var bar in TestVisuals.Named<Control>(w, "ExpandedHost").GetVisualDescendants().OfType<ScrollBar>())
            {
                DoubleTap(bar);
                Assert.AreEqual(ViewMode.Expanded, vm.RequestedMode);
            }
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void EscapeInFull_ReturnsToExpanded() => Ui.Run(() =>
    {
        var vm = CreateVm("full");
        var w = new MainWindow(vm, null, null);
        try
        {
            w.Show();
            vm.Apply(Sample(), 100);
            TestVisuals.Pump(w);
            w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            w.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            TestVisuals.Pump(w);
            Assert.AreEqual(ViewMode.Expanded, vm.RequestedMode);
            AssertHosts(w, ViewMode.Expanded);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void SidebarBindings_EditSettings_AndThemeChangesBackgroundAndVariant() => Ui.Run(() =>
    {
        var vm = CreateVm("expanded");
        var w = new MainWindow(vm, null, null);
        try
        {
            w.Show();
            vm.Apply(Sample(), 100);
            TestVisuals.Pump(w);
            var theme = TestVisuals.Named<CheckBox>(w, "ThemeToggle");
            var topmost = TestVisuals.Named<CheckBox>(w, "TopmostToggle");
            var opacity = TestVisuals.Named<Slider>(w, "OpacitySlider");
            var speed = TestVisuals.Named<ComboBox>(w, "SpeedBox");
            Assert.AreEqual(0.3, opacity.Minimum);
            Assert.AreEqual(1d, opacity.Maximum);
            Assert.AreEqual(3, speed.Items.Count);
            Assert.IsTrue(topmost.IsChecked == true);
            vm.IsDark = true;
            TestVisuals.Pump(w);
            Assert.IsTrue(theme.IsChecked == true);
            Assert.AreEqual(Color.Parse(vm.Colors.Panel), TestVisuals.BrushColor(w.Background));
            Assert.AreEqual(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
            var dark = TestVisuals.Capture(w);
            Assert.AreEqual(Color.Parse(vm.Colors.Panel), dark.At(dark.Width - 3, 2));
            theme.IsChecked = false;
            topmost.IsChecked = false;
            opacity.Value = 0.6;
            speed.SelectedIndex = 2;
            TestVisuals.Pump(w);
            Assert.IsFalse(vm.IsDark);
            Assert.IsFalse(vm.Settings.AlwaysOnTop);
            Assert.AreEqual(0.6, vm.Settings.Opacity, 0.0001);
            Assert.AreEqual("fast", vm.Settings.Speed);
            Assert.AreEqual(ThemeVariant.Light, Application.Current.RequestedThemeVariant);
            Assert.AreEqual(Colors.White, TestVisuals.BrushColor(w.Background));
            vm.AlwaysOnTop = true;
            vm.Opacity = 0.8;
            vm.SpeedIndex = 0;
            TestVisuals.Pump(w);
            Assert.IsTrue(topmost.IsChecked == true);
            Assert.AreEqual(0.8, opacity.Value, 0.0001);
            Assert.AreEqual(0, speed.SelectedIndex);
        }
        finally { w.Close(); Application.Current!.RequestedThemeVariant = ThemeVariant.Light; }
    });

    [TestMethod]
    [DataRow("mini", 320d, 130d)]
    [DataRow("expanded", 700d, 520d)]
    [DataRow("full", 800d, 600d)]
    public void ClientSizeChanged_WritesOnlyRequestedModesSettings(string mode, double width, double height) => Ui.Run(() =>
    {
        var vm = CreateVm(mode);
        var w = new MainWindow(vm, null, null);
        try
        {
            w.Show();
            vm.Apply(Sample(), 100);
            TestVisuals.Pump(w);
            var before = new[] { vm.Settings.MiniW, vm.Settings.MiniH, vm.Settings.ExpW, vm.Settings.ExpH, vm.Settings.FullW, vm.Settings.FullH };
            w.Width = width;
            w.Height = height;
            TestVisuals.Pump(w);
            Assert.AreEqual(width, w.ClientSize.Width, 1d);
            Assert.AreEqual(height, w.ClientSize.Height, 1d);
            Assert.AreEqual(w.ClientSize.Width, vm.WindowWidth, 1d);
            Assert.AreEqual(w.ClientSize.Height, vm.WindowHeight, 1d);
            var after = new[] { vm.Settings.MiniW, vm.Settings.MiniH, vm.Settings.ExpW, vm.Settings.ExpH, vm.Settings.FullW, vm.Settings.FullH };
            var offset = mode == "mini" ? 0 : mode == "expanded" ? 2 : 4;
            for (var i = 0; i < after.Length; i++)
                Assert.AreEqual(i == offset ? w.ClientSize.Width : i == offset + 1 ? w.ClientSize.Height : before[i], after[i], 1d);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void CloseWithNullConfigPath_LeavesIsolatedCurrentDirectoryUnchanged() => Ui.Run(() =>
    {
        var directory = NewTempDirectory();
        var previous = Environment.CurrentDirectory;
        var vm = CreateVm("mini");
        var w = new MainWindow(vm, null, null);
        try
        {
            try
            {
                Environment.CurrentDirectory = directory;
                w.Show();
                vm.Apply(Sample(), 100);
                TestVisuals.Pump(w);
            }
            finally
            {
                try { w.Close(); }
                finally { Environment.CurrentDirectory = previous; }
            }
            Assert.AreEqual(0, Directory.GetFileSystemEntries(directory).Length);
        }
        finally { Directory.Delete(directory, true); }
    });

    [TestMethod]
    public void CloseWithConfigPath_SavesCurrentSettingsAsJson_InTemporaryDirectory() => Ui.Run(() =>
    {
        var directory = NewTempDirectory();
        var path = Path.Combine(directory, "nested", "config.linux.json");
        var vm = CreateVm("expanded");
        var w = new MainWindow(vm, path, null);
        try
        {
            try
            {
                w.Show();
                vm.Apply(Sample(), 100);
                vm.SelectedTabKey = "disk:/data";
                vm.SpeedIndex = 2;
                vm.Opacity = 0.75;
                TestVisuals.Pump(w);
                Assert.IsFalse(File.Exists(path));
            }
            finally { w.Close(); }
            Assert.IsTrue(File.Exists(path));
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.AreEqual("expanded", json.RootElement.GetProperty("mode").GetString());
            Assert.AreEqual("fast", json.RootElement.GetProperty("speed").GetString());
            Assert.AreEqual("disk:/data", json.RootElement.GetProperty("full_tab").GetString());
            Assert.AreEqual(0.75, json.RootElement.GetProperty("opacity").GetDouble(), 0.0001);
            Assert.IsTrue(vm.Settings.ValueEquals(AppSettings.Parse(File.ReadAllText(path))));
            Assert.IsFalse(File.Exists(path + ".tmp"));
        }
        finally { Directory.Delete(directory, true); }
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SamplerDelegate_PostsSnapshotToViewModel_RegardlessOfReady(bool ready) => Ui.Run(() =>
    {
        var vm = CreateVm("mini");
        var sample = Sample(53, ready: ready);
        var calls = 0;
        var sampledOnUi = 0;
        var appliedOnUi = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == "Snapshot") appliedOnUi = Dispatcher.UIThread.CheckAccess(); };
        var w = new MainWindow(vm, null, _ =>
        {
            Interlocked.Increment(ref calls);
            if (Dispatcher.UIThread.CheckAccess()) Interlocked.Exchange(ref sampledOnUi, 1);
            return sample;
        });
        try
        {
            w.Show();
            PumpUntil(w, () => ReferenceEquals(sample, vm.Snapshot), 4500);
            Assert.AreSame(sample, vm.Snapshot);
            Assert.IsTrue(Volatile.Read(ref calls) >= 1);
            Assert.AreEqual(0, Volatile.Read(ref sampledOnUi));
            Assert.IsTrue(appliedOnUi);
            Assert.IsTrue(vm.Items.Count >= 5);
            Assert.IsTrue(vm.HistoryVersion > 0);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void SamplerExceptionAndNull_AreTolerated_AndLaterSnapshotArrives() => Ui.Run(() =>
    {
        var vm = CreateVm("mini");
        vm.SpeedIndex = 2;
        var sample = Sample();
        var calls = 0;
        var w = new MainWindow(vm, null, _ =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1) throw new InvalidOperationException("Test sampler failure");
            return call == 2 ? null : sample;
        });
        try
        {
            w.Show();
            PumpUntil(w, () => ReferenceEquals(sample, vm.Snapshot), 4500);
            Assert.AreSame(sample, vm.Snapshot);
            Assert.IsTrue(Volatile.Read(ref calls) >= 3);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void ClosingWindow_StopsSamplingAndFurtherSnapshotUpdates() => Ui.Run(() =>
    {
        var vm = CreateVm("mini");
        vm.SpeedIndex = 2;
        var calls = 0;
        var sample = Sample();
        var w = new MainWindow(vm, null, _ => { Interlocked.Increment(ref calls); return sample; });
        try
        {
            w.Show();
            PumpUntil(w, () => vm.Snapshot != null, 2500);
            Assert.AreSame(sample, vm.Snapshot);
            w.Close();
            Dispatcher.UIThread.RunJobs();
            var callsAtClose = Volatile.Read(ref calls);
            var versionAtClose = vm.HistoryVersion;
            var elapsed = Stopwatch.StartNew();
            // Allows more than one fast interval, with total explicit sleep bounded below four seconds.
            while (elapsed.ElapsedMilliseconds < 1300)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(callsAtClose, Volatile.Read(ref calls));
            Assert.AreEqual(versionAtClose, vm.HistoryVersion);
        }
        finally { w.Close(); }
    });

    [TestMethod]
    public void FullContent_BindsOrderedLinesAndCharts_AndRefreshesHistory() => Ui.Run(() =>
    {
        var vm = CreateVm("full");
        var w = new MainWindow(vm, null, null);
        try
        {
            w.Show();
            vm.Apply(Sample(), 100);
            vm.Apply(Sample(64), 200);
            foreach (var key in new[] { "cpu", "memory", "disk", "disk:/data" })
            {
                vm.SelectedTabKey = key;
                TestVisuals.Pump(w);
                var full = TestVisuals.Named<Control>(w, "FullHost");
                var chartsHost = TestVisuals.Named<StackPanel>(full, "ChartsHost");
                Assert.AreEqual(vm.SelectedCharts.Count * 2, chartsHost.Children.Count);
                var charts = chartsHost.Children.OfType<HistoryChart>().ToArray();
                Assert.AreEqual(vm.SelectedCharts.Count, charts.Length);
                for (var i = 0; i < charts.Length; i++)
                {
                    var spec = vm.SelectedCharts[i];
                    Assert.IsTrue(chartsHost.Children[2 * i] is TextBlock);
                    Assert.AreEqual(spec.Title, ((TextBlock)chartsHost.Children[2 * i]).Text);
                    Assert.AreSame(charts[i], chartsHost.Children[2 * i + 1]);
                    Assert.AreSame(vm.History.Get(spec.Key), charts[i].Series);
                    Assert.AreEqual(spec.ColorHex, charts[i].LineColor);
                    Assert.AreEqual(spec.FixedMax, charts[i].FixedMax);
                    Assert.AreEqual(vm.Colors.BarEmpty, charts[i].BackgroundColor);
                    Assert.AreEqual(vm.Colors.Border, charts[i].GridColor);
                    Assert.AreEqual(vm.HistoryVersion, charts[i].Revision);
                    Assert.AreEqual(120d, charts[i].Bounds.Height, 0.5);
                    Assert.IsTrue(charts[i].Bounds.Width > 0);
                }
                var strip = TestVisuals.Named<TabControl>(full, "TabStrip");
                var allTexts = strip.GetVisualDescendants().OfType<TextBlock>().ToArray();
                var previousTop = double.NegativeInfinity;
                foreach (var line in vm.SelectedTab!.Lines)
                {
                    var text = allTexts.First(t => t.Text == line && t.IsEffectivelyVisible);
                    var top = TestVisuals.RectInWindow(text, w).Top;
                    Assert.IsTrue(top >= previousTop);
                    previousTop = top;
                }
                Assert.IsTrue(strip.GetVisualDescendants().OfType<Border>().Any(b =>
                    b.BorderBrush is ISolidColorBrush brush && brush.Color == Color.Parse(vm.Colors.Border)
                    && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == vm.SelectedTab.Lines[0])));
            }
            var previousCharts = TestVisuals.Named<StackPanel>(w, "ChartsHost").Children.OfType<HistoryChart>().ToArray();
            vm.Apply(Sample(78), 300);
            TestVisuals.Pump(w);
            var refreshedCharts = TestVisuals.Named<StackPanel>(w, "ChartsHost").Children.OfType<HistoryChart>().ToArray();
            Assert.AreEqual(previousCharts.Length, refreshedCharts.Length);
            for (var i = 0; i < refreshedCharts.Length; i++)
            {
                Assert.AreSame(previousCharts[i], refreshedCharts[i]);
                Assert.AreEqual(vm.HistoryVersion, refreshedCharts[i].Revision);
            }
        }
        finally { w.Close(); }
    });

    private static MainViewModel CreateVm(string mode) => new(new AppSettings
    {
        Opacity = 1.0, Mode = mode, FullW = 900, FullH = 700
    });

    private static Snapshot Sample(int usage = 31, bool addDisk = false, bool ready = true) => new()
    {
        Ready = ready, CpuTotal = usage, CpuTemp = 51,
        Cores = new[] { new Core { Usage = usage, Temp = 50 }, new Core { Usage = 22 }, new Core { Usage = 45, Temp = 53 } },
        Ram = new Ram { Usage = 40, UsedGb = 6.4, TotalGb = 16 },
        Disks = (addDisk ? new[] { "/", "/data", "/extra" } : new[] { "/", "/data" }).Select((mount, i) => new Disk
        {
            Letter = mount, Label = i == 0 ? "System" : "Data " + i, Usage = usage + i,
            TotalGb = 200, UsedGb = 60 + usage, ReadMb = usage / 10d, WriteMb = usage / 20d,
            Media = "SSD", Bus = "SATA", Detail = new DriveDetail { Letter = mount, FileSystem = "ext4", PartitionNumber = i + 1 }
        }).ToArray(),
        Adapters = new[] { new Adapter { Id = "eth0", SpeedMbps = 1000, DownMb = 2, UpMb = 1, TotalReceived = 4096, TotalSent = 2048 } }
    };

    private static MeterCard[] ExpandedCards(MainWindow w) => TestVisuals.Named<Control>(w, "ExpandedHost")
        .GetVisualDescendants().OfType<MeterCard>().ToArray();

    private static void AssertHosts(MainWindow w, ViewMode expected)
    {
        var names = new[] { "MiniHost", "ExpandedHost", "FullHost" };
        var hosts = names.Select(n => TestVisuals.Named<Control>(w, n)).ToArray();
        Assert.AreEqual(1, hosts.Count(h => h.IsVisible));
        for (var i = 0; i < hosts.Length; i++) Assert.AreEqual(i == (int)expected, hosts[i].IsVisible, names[i]);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void DoubleTap(Control source)
    {
        // Only routing/Source is under test; no pointer position is consumed by the specified handler.
        // Allocate the genuine typed args without depending on a version-specific pointer constructor.
        var args = (TappedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(TappedEventArgs));
        args.RoutedEvent = InputElement.DoubleTappedEvent;
        args.Source = source;
        source.RaiseEvent(args);
    }

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "SysMonitor-ViewTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void PumpUntil(Window w, Func<bool> condition, int timeoutMillis)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition() && elapsed.ElapsedMilliseconds < timeoutMillis)
        {
            TestVisuals.Pump(w);
            Thread.Sleep(5);
        }
        TestVisuals.Pump(w);
    }
}
