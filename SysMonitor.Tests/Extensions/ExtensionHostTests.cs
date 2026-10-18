using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SysMonitor.Extensibility;
using SysMonitor.Extensions;
using SysMonitor.Model;
using SysMonitor.ViewModels;
using SysMonitor.Tests.DummyExtension;

namespace SysMonitor.Tests.Extensions;

[TestClass]
public class ExtensionHostTests
{
    private string _tempDir = null!;
    private string _dummyDllSourcePath = null!;

    [TestInitialize]
    public void Setup()
    {
        DummyViewsExtension.Reset();
        _tempDir = Path.Combine(Path.GetTempPath(), "SysMon_ExtHostTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _dummyDllSourcePath = Path.Combine(AppContext.BaseDirectory, "SysMonitor.Tests.DummyExtension.dll");
        if (!File.Exists(_dummyDllSourcePath))
        {
            _dummyDllSourcePath = typeof(DummyViewsExtension).Assembly.Location;
        }

        Assert.IsTrue(File.Exists(_dummyDllSourcePath), $"DummyExtension DLL not found at: {_dummyDllSourcePath}");
    }

    [TestCleanup]
    public void Cleanup()
    {
        DummyViewsExtension.Reset();
        if (Directory.Exists(_tempDir))
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    TestJunctionHelper.DeleteTree(_tempDir);
                    return;
                }
                catch
                {
                    Thread.Sleep(50);
                }
            }
            TestJunctionHelper.DeleteTree(_tempDir);
        }
    }

    [TestMethod]
    public async Task ExtensionHost_DisabledExtension_StatusIsDisabledAndNotLoaded()
    {
        string extDir = CreateExtension("ext_disabled", out string manifestHash);

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_disabled"] = new ExtensionSetting { Enabled = false }
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { manifestHash });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };
        using var host = new ExtensionHost(config, options);

        await host.StartAsync();

        Assert.AreEqual(1, host.Entries.Count);
        var entry = host.Entries[0];
        Assert.AreEqual("ext_disabled", entry.Id);
        Assert.AreEqual(ExtensionStatus.Disabled, entry.Status);
        Assert.IsFalse(entry.Enabled);
        Assert.IsFalse(DummyViewsExtension.Initialized, "Disabled extension must never be initialized");

        var views = host.CollectViews("en");
        Assert.AreEqual(0, views.Count, "Disabled extension must not produce views");
    }

    [TestMethod]
    public async Task ExtensionHost_ToggleEnabled_UnloadsAndReloads()
    {
        string extDir = CreateExtension("ext_toggle", out string manifestHash);

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_toggle"] = new ExtensionSetting { Enabled = true }
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { manifestHash });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };
        using var host = new ExtensionHost(config, options);

        await host.StartAsync();

        Assert.AreEqual(1, host.Entries.Count);
        Assert.AreEqual(ExtensionStatus.Loaded, host.Entries[0].Status);
        Assert.IsTrue(host.Entries[0].Enabled);
        Assert.IsTrue(DummyViewsExtension.Initialized);

        var viewsBefore = host.CollectViews("en");
        Assert.AreEqual(1, viewsBefore.Count);

        // Toggle to Disabled
        await host.SetEnabled("ext_toggle", false);
        Assert.IsFalse(config.Extensions["ext_toggle"].Enabled);
        Assert.AreEqual(ExtensionStatus.Disabled, host.Entries[0].Status);
        Assert.IsTrue(DummyViewsExtension.ShutdownCalled);

        var viewsAfterDisable = host.CollectViews("en");
        Assert.AreEqual(0, viewsAfterDisable.Count);

        // Toggle back to Enabled
        DummyViewsExtension.Reset();
        await host.SetEnabled("ext_toggle", true);
        Assert.IsTrue(config.Extensions["ext_toggle"].Enabled);
        Assert.AreEqual(ExtensionStatus.Loaded, host.Entries[0].Status);
        Assert.IsTrue(DummyViewsExtension.Initialized);

        var viewsAfterEnable = host.CollectViews("en");
        Assert.AreEqual(1, viewsAfterEnable.Count);
    }

    [TestMethod]
    public async Task ExtensionHost_CollectViews_EnforcesCeilings()
    {
        string extDir = CreateExtension("ext_ceilings", out string manifestHash);

        DummyViewsExtension.ViewFunc = _ => new ExtensionView(
            // Exceeds 32 pages
            Enumerable.Range(0, 50).Select(i => new WidgetPage(
                Title: new string('A', 600), // Exceeds 512
                Verdict: "Val",
                Line: "Det",
                TemperatureC: 200, // Outside -50..150: dropped
                Percent: 150, // Clamped to 100
                Severity: Severity.Good
            )).ToList(),

            // Exceeds 64 overview rows
            new OverviewSection(
                Note: "Overview",
                Rows: Enumerable.Range(0, 100).Select(i => new OverviewRow(
                    Title: "Row " + i,
                    Detail: "Det",
                    ValueText: "Val",
                    Percent: -50, // Clamped to 0
                    Severity: Severity.Caution,
                    TemperatureC: -100 // Outside -50..150: dropped
                )).ToList()
            ),

            // Exceeds 32 detail tabs
            Enumerable.Range(0, 50).Select(t => new DetailTab(
                Key: "tab" + t,
                Title: "Tab " + t,
                Subtitle: "Sub " + t,
                Detail: "Det " + t,
                Hardware: "Hw " + t,
                Facts: Enumerable.Range(0, 100).Select(f => new Fact(
                    Name: "Label " + f,
                    Value: "Val " + f
                )).ToList() // Exceeds 64 facts
            )).ToList()
        );

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_ceilings"] = new ExtensionSetting { Enabled = true }
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { manifestHash });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };
        using var host = new ExtensionHost(config, options);

        await host.StartAsync();

        var views = host.CollectViews("en");
        Assert.AreEqual(1, views.Count);

        var view = views[0].View;
        Assert.AreEqual(ExtensionHost.MaxWidgetPages, view.WidgetPages.Count);
        Assert.AreEqual(ExtensionHost.MaxTextLength, view.WidgetPages[0].Title.Length);
        Assert.AreEqual(100, view.WidgetPages[0].Percent);
        Assert.IsNull(view.WidgetPages[0].TemperatureC, "an implausible temperature is dropped, not clamped");

        Assert.IsNotNull(view.Overview);
        Assert.AreEqual(ExtensionHost.MaxOverviewRows, view.Overview.Rows.Count);
        Assert.AreEqual(0, view.Overview.Rows[0].Percent);
        Assert.IsNull(view.Overview.Rows[0].TemperatureC);

        Assert.AreEqual(ExtensionHost.MaxDetailTabs, view.Tabs.Count);
        Assert.AreEqual(ExtensionHost.MaxFactsPerTab, view.Tabs[0].Facts.Count);
    }

    [TestMethod]
    public async Task ExtensionHost_CollectViews_HandlesProviderExceptionSafely()
    {
        string extDir = CreateExtension("ext_throwing", out string manifestHash);

        DummyViewsExtension.ViewFunc = _ => throw new InvalidOperationException("Boom in GetView");

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_throwing"] = new ExtensionSetting { Enabled = true }
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { manifestHash });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };
        using var host = new ExtensionHost(config, options);

        await host.StartAsync();

        // Must not crash; a provider that throws is skipped for that round.
        var views = host.CollectViews("en");
        Assert.AreEqual(0, views.Count);
    }

    [TestMethod]
    public async Task ExtensionHost_SetEnabled_LeavesRealConfigUntouched()
    {
        string realPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SystemMonitor", "config.wpf.json");

        bool existsBefore = File.Exists(realPath);
        byte[]? bytesBefore = existsBefore ? File.ReadAllBytes(realPath) : null;
        DateTime? writeTimeBefore = existsBefore ? File.GetLastWriteTimeUtc(realPath) : null;

        string extDir = CreateExtension("ext_real_cfg", out string manifestHash);
        var config = new AppConfig();
        var allowlist = new FirstPartyAllowlist(new[] { manifestHash });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };

        using (var host = new ExtensionHost(config, options))
        {
            await host.StartAsync();

            await host.SetEnabled("ext_real_cfg", true);
            await host.SetEnabled("ext_real_cfg", false);
        }

        bool existsAfter = File.Exists(realPath);
        Assert.AreEqual(existsBefore, existsAfter, "Real config file existence changed");

        if (existsBefore)
        {
            byte[] bytesAfter = File.ReadAllBytes(realPath);
            DateTime writeTimeAfter = File.GetLastWriteTimeUtc(realPath);
            CollectionAssert.AreEqual(bytesBefore, bytesAfter, "Real config bytes modified");
            Assert.AreEqual(writeTimeBefore, writeTimeAfter, "Real config timestamp modified");
        }
    }

    private sealed class ThrowingList<T> : IReadOnlyList<T>
    {
        public int Count => 100_000;
        public T this[int index] => throw new InvalidOperationException("Hostile list indexer threw");
        public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException("Hostile enumerator threw");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [TestMethod]
    public async Task ExtensionHost_CollectViews_HostileListDoesNotCrashAndLogsOnce()
    {
        string extDir = CreateExtension("ext_hostile", out string hash);

        int logCount = 0;
        Action<string> logger = _ => Interlocked.Increment(ref logCount);

        DummyViewsExtension.ViewFunc = _ => new ExtensionView(
            new ThrowingList<WidgetPage>(),
            null,
            Array.Empty<DetailTab>());

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_hostile"] = new ExtensionSetting { Enabled = true },
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { hash });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
            Log = logger,
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        for (int i = 0; i < 5; i++)
        {
            var views = host.CollectViews("en");
            Assert.IsNotNull(views);
            Assert.AreEqual(0, views.Count);
        }

        Assert.AreEqual(1, logCount, "Hostile list failure must be logged exactly once across repeated calls");
    }

    private sealed class EndlessBlankTabsList : IReadOnlyList<DetailTab>
    {
        public int Count => int.MaxValue;
        public DetailTab this[int index] => new DetailTab(string.Empty, "Blank", "Sub", "Det", "Hw", Array.Empty<Fact>());
        public IEnumerator<DetailTab> GetEnumerator()
        {
            while (true)
            {
                yield return new DetailTab(string.Empty, "Blank", "Sub", "Det", "Hw", Array.Empty<Fact>());
            }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [TestMethod]
    public async Task ExtensionHost_CollectViews_EndlessTabsTerminatesAndFiltersKeysAndCutsText()
    {
        string extDir = CreateExtension("ext_tabs", out string hash);

        string longText = new string('z', 600);

        DummyViewsExtension.ViewFunc = _ => new ExtensionView(
            Array.Empty<WidgetPage>(),
            new OverviewSection("Note", new[]
            {
                new OverviewRow("Title", longText, longText, 50, Severity.Good, null)
            }),
            new[]
            {
                new DetailTab("tab1", "Tab 1", longText, longText, longText,
                    Enumerable.Range(0, 100).Select(_ => new Fact(longText, longText)).ToList()),
                new DetailTab("", "Blank Key", "Sub", "Det", "Hw", Array.Empty<Fact>()),
                new DetailTab("   ", "Whitespace Key", "Sub", "Det", "Hw", Array.Empty<Fact>()),
                new DetailTab("tab1", "Duplicate Key", "Sub", "Det", "Hw", Array.Empty<Fact>()),
                new DetailTab("tab2", "Tab 2", "Sub 2", "Det 2", "Hw 2", Array.Empty<Fact>())
            });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_tabs"] = new ExtensionSetting { Enabled = true }
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { hash });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var views = host.CollectViews("en");
        Assert.AreEqual(1, views.Count);

        var view = views[0].View;
        Assert.AreEqual(2, view.Tabs.Count);
        Assert.AreEqual("tab1", view.Tabs[0].Key);
        Assert.AreEqual("tab2", view.Tabs[1].Key);

        Assert.AreEqual(ExtensionHost.MaxFactsPerTab, view.Tabs[0].Facts.Count);
        Assert.AreEqual(ExtensionHost.MaxTextLength, view.Tabs[0].Subtitle.Length);
        Assert.AreEqual(ExtensionHost.MaxTextLength, view.Tabs[0].Detail.Length);
        Assert.AreEqual(ExtensionHost.MaxTextLength, view.Tabs[0].Hardware.Length);
        Assert.AreEqual(ExtensionHost.MaxTextLength, view.Tabs[0].Facts[0].Name.Length);
        Assert.AreEqual(ExtensionHost.MaxTextLength, view.Tabs[0].Facts[0].Value.Length);
        Assert.AreEqual(ExtensionHost.MaxTextLength, view.Overview!.Rows[0].ValueText.Length);
        Assert.AreEqual(ExtensionHost.MaxTextLength, view.Overview.Rows[0].Detail.Length);

        // Endless tabs termination check with 5s timeout guard
        DummyViewsExtension.ViewFunc = _ => new ExtensionView(
            Array.Empty<WidgetPage>(),
            null,
            new EndlessBlankTabsList());

        bool completed = Task.Run(() => host.CollectViews("en")).Wait(5000);
        Assert.IsTrue(completed, "Endless blank tabs list did not terminate within 5 seconds");
    }

    [TestMethod]
    public async Task ExtensionHost_CollectViews_TemperatureAndPercentBoundaries()
    {
        string extDir = CreateExtension("ext_bounds", out string hash);

        DummyViewsExtension.ViewFunc = _ => new ExtensionView(
            new[]
            {
                new WidgetPage("P1", "OK", "L", -50, -5, Severity.Good),
                new WidgetPage("P2", "OK", "L", 150, 105, Severity.Good),
                new WidgetPage("P3", "OK", "L", -51, 50, Severity.Good),
                new WidgetPage("P4", "OK", "L", 151, 50, Severity.Good),
            },
            new OverviewSection("Note", new[]
            {
                new OverviewRow("R1", "D", "V", -5, Severity.Good, -50),
                new OverviewRow("R2", "D", "V", 105, Severity.Good, 150),
                new OverviewRow("R3", "D", "V", 50, Severity.Good, -51),
                new OverviewRow("R4", "D", "V", 50, Severity.Good, 151),
            }),
            Array.Empty<DetailTab>());

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_bounds"] = new ExtensionSetting { Enabled = true }
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { hash });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var views = host.CollectViews("en");
        var p = views[0].View.WidgetPages;
        var r = views[0].View.Overview!.Rows;

        Assert.AreEqual(-50, p[0].TemperatureC);
        Assert.AreEqual(0, p[0].Percent);

        Assert.AreEqual(150, p[1].TemperatureC);
        Assert.AreEqual(100, p[1].Percent);

        Assert.IsNull(p[2].TemperatureC);
        Assert.IsNull(p[3].TemperatureC);

        Assert.AreEqual(-50, r[0].TemperatureC);
        Assert.AreEqual(0, r[0].Percent);

        Assert.AreEqual(150, r[1].TemperatureC);
        Assert.AreEqual(100, r[1].Percent);

        Assert.IsNull(r[2].TemperatureC);
        Assert.IsNull(r[3].TemperatureC);
    }

    [TestMethod]
    public async Task ExtensionHost_LifetimeGate_ShutdownWaitsForInFlightViewsCall()
    {
        string extDir = CreateExtension("ext_gate", out string hash);

        using var blockGetView = new ManualResetEventSlim(false);
        using var enteredGetView = new ManualResetEventSlim(false);

        AppDomain.CurrentDomain.SetData("DummyViews.GateEvent", blockGetView);
        AppDomain.CurrentDomain.SetData("DummyViews.EnteredEvent", enteredGetView);

        DummyViewsExtension.ViewFunc = _ =>
        {
            var ent = AppDomain.CurrentDomain.GetData("DummyViews.EnteredEvent") as ManualResetEventSlim;
            ent?.Set();
            var blk = AppDomain.CurrentDomain.GetData("DummyViews.GateEvent") as ManualResetEventSlim;
            blk?.Wait(5000);
            return new ExtensionView(
                new[] { new WidgetPage("BLOCKED", "OK", "L", null, 50, Severity.Good) },
                null,
                Array.Empty<DetailTab>());
        };

        try
        {
            var config = new AppConfig
            {
                Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ext_gate"] = new ExtensionSetting { Enabled = true }
                }
            };

            var allowlist = new FirstPartyAllowlist(new[] { hash });
            var options = new ExtensionLoaderOptions
            {
                ExtensionsRoot = _tempDir,
                FirstParty = allowlist,
                HostIsElevated = false,
            };

            using var host = new ExtensionHost(config, options);
            await host.StartAsync();

            var collectTask = Task.Run(() => host.CollectViews("en"));
            Assert.IsTrue(enteredGetView.Wait(5000), "GetView was not entered within 5s");

            var disableTask = host.SetEnabled("ext_gate", false);

            // Wait bounded (5 s) until host.Entries shows Disabled (retirement has started, slot removed)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 5000)
            {
                var entry = host.Entries.FirstOrDefault(e => e.Id == "ext_gate");
                if (entry is { Status: ExtensionStatus.Disabled, Enabled: false })
                {
                    break;
                }
                Thread.Sleep(10);
            }

            var disabledEntry = host.Entries.FirstOrDefault(e => e.Id == "ext_gate");
            Assert.AreEqual(ExtensionStatus.Disabled, disabledEntry?.Status, "Disabled must be published while GetView is still blocked");
            Assert.IsTrue(sw.ElapsedMilliseconds < 2000, $"Disabled took {sw.ElapsedMilliseconds} ms to show; it must not wait for the in-flight call");
            Assert.IsFalse(DummyViewsExtension.ShutdownCalled, "Shutdown called while GetView was still in flight");

            blockGetView.Set();

            var views = await collectTask;
            await disableTask;

            Assert.IsTrue(DummyViewsExtension.ShutdownCalled, "Shutdown not called after in-flight call completed");
            Assert.AreEqual(1, views.Count);
            Assert.AreEqual("BLOCKED", views[0].View.WidgetPages[0].Title);
        }
        finally
        {
            blockGetView.Set();
            AppDomain.CurrentDomain.SetData("DummyViews.GateEvent", null);
            AppDomain.CurrentDomain.SetData("DummyViews.EnteredEvent", null);
            DummyViewsExtension.Reset();
        }
    }

    [TestMethod]
    public async Task ExtensionHost_SamplerNotStalledByLoading()
    {
        string extDir1 = CreateExtension("ext_fast", out string hash1);
        string extDir2 = CreateExtension("ext_blocking_load", out string hash2);

        using var blockInit = new ManualResetEventSlim(false);
        using var enteredInit = new ManualResetEventSlim(false);

        AppDomain.CurrentDomain.SetData("DummyViews.BlockInit", blockInit);
        AppDomain.CurrentDomain.SetData("DummyViews.EnteredInit", enteredInit);

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_fast"] = new ExtensionSetting { Enabled = true },
                ["ext_blocking_load"] = new ExtensionSetting { Enabled = false },
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { hash1, hash2 });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        DummyViewsExtension.InitializeAction = () =>
        {
            var ent = AppDomain.CurrentDomain.GetData("DummyViews.EnteredInit") as ManualResetEventSlim;
            ent?.Set();
            var blk = AppDomain.CurrentDomain.GetData("DummyViews.BlockInit") as ManualResetEventSlim;
            blk?.Wait(5000);
        };

        try
        {
            var enableTask = host.SetEnabled("ext_blocking_load", true);
            Assert.IsTrue(enteredInit.Wait(5000), "Initialize was not entered within 5s");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var views = host.CollectViews("en");
            sw.Stop();

            Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(1), $"CollectViews took {sw.Elapsed.TotalMilliseconds}ms, expected < 1000ms");
            Assert.AreEqual(1, views.Count, "Fast extension views should be collected");

            blockInit.Set();
            await enableTask;
        }
        finally
        {
            blockInit.Set();
            AppDomain.CurrentDomain.SetData("DummyViews.BlockInit", null);
            AppDomain.CurrentDomain.SetData("DummyViews.EnteredInit", null);
            DummyViewsExtension.Reset();
        }
    }

    [TestMethod]
    public void ExtensionStatusText_CanToggle_FollowsSidebarRules()
    {
        Assert.IsTrue(ExtensionStatusText.CanToggle(ExtensionStatus.Loaded, true));
        Assert.IsTrue(ExtensionStatusText.CanToggle(ExtensionStatus.Loaded, false));
        Assert.IsTrue(ExtensionStatusText.CanToggle(ExtensionStatus.Disabled, true));
        Assert.IsTrue(ExtensionStatusText.CanToggle(ExtensionStatus.Disabled, false));

        Assert.IsTrue(ExtensionStatusText.CanToggle(ExtensionStatus.NotApproved, true));
        Assert.IsFalse(ExtensionStatusText.CanToggle(ExtensionStatus.NotApproved, false));

        Assert.IsTrue(ExtensionStatusText.CanToggle(ExtensionStatus.ManifestInvalid, true));
        Assert.IsFalse(ExtensionStatusText.CanToggle(ExtensionStatus.ManifestInvalid, false));
        Assert.IsTrue(ExtensionStatusText.CanToggle(ExtensionStatus.LoadFailed, true));
        Assert.IsFalse(ExtensionStatusText.CanToggle(ExtensionStatus.LoadFailed, false));
    }

    [TestMethod]
    public async Task ExtensionHost_LanguageChange_UpdatesEntryNameWithoutReload()
    {
        string extDir = CreateExtension("ext_i18n", out string hash);

        DummyViewsExtension.DisplayNameFunc = lang => lang == "th" ? "ชื่อไทย" : "English Name";

        var config = new AppConfig
        {
            Lang = "th",
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_i18n"] = new ExtensionSetting { Enabled = true }
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { hash });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        host.CollectViews("th");
        Assert.AreEqual("ชื่อไทย", host.Entries[0].Name);

        host.CollectViews("en");
        Assert.AreEqual("English Name", host.Entries[0].Name);
    }

    [TestMethod]
    public void AppConfig_Extensions_NormalizedAndCaseInsensitiveWithoutSave()
    {
        string tempFile = Path.Combine(_tempDir, "temp_config.json");

        // 1. JSON with extensions: null
        File.WriteAllText(tempFile, "{\"extensions\": null}");
        var cfg1 = AppConfig.LoadFrom(tempFile);
        Assert.IsNotNull(cfg1.Extensions);
        Assert.AreEqual(0, cfg1.Extensions.Count);

        // 2. JSON with null values inside extensions
        File.WriteAllText(tempFile, "{\"extensions\": {\"x\": null, \"y\": {\"enabled\": true}}}");
        var cfg2 = AppConfig.LoadFrom(tempFile);
        Assert.IsNotNull(cfg2.Extensions);
        Assert.AreEqual(1, cfg2.Extensions.Count);
        Assert.IsTrue(cfg2.Extensions["y"].Enabled);

        // 3. Old config without extensions key
        File.WriteAllText(tempFile, "{\"theme\": \"dark\"}");
        var cfg3 = AppConfig.LoadFrom(tempFile);
        Assert.IsNotNull(cfg3.Extensions);
        Assert.AreEqual(0, cfg3.Extensions.Count);

        // 4. Round trip keeps {"Ext.One": {"enabled": true}} and lookups are case-insensitive
        var roundTripCfg = new AppConfig();
        roundTripCfg.Extensions["Ext.One"] = new ExtensionSetting { Enabled = true };
        string json = JsonSerializer.Serialize(roundTripCfg, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true
        });
        File.WriteAllText(tempFile, json);

        var loaded = AppConfig.LoadFrom(tempFile);
        Assert.IsTrue(loaded.Extensions.ContainsKey("Ext.One"));
        Assert.IsTrue(loaded.Extensions.ContainsKey("ext.one"));
        Assert.IsTrue(loaded.Extensions.ContainsKey("EXT.ONE"));
        Assert.IsTrue(loaded.Extensions["ext.one"].Enabled);
    }

    [TestMethod]
    public async Task ExtensionHost_SetEnabled_OrderIndependence_FinalStateMatchesLatestSetting()
    {
        string extDir = CreateExtension("ext_order", out string hash);

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_order"] = new ExtensionSetting { Enabled = true }
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { hash });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        // 1. Call SetEnabled false/true 20 pairs, ending with true
        var tasks = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(host.SetEnabled("ext_order", false));
            tasks.Add(host.SetEnabled("ext_order", true));
        }
        await Task.WhenAll(tasks);

        var entry = host.Entries.First(e => e.Id == "ext_order");
        Assert.AreEqual(ExtensionStatus.Loaded, entry.Status);
        Assert.IsTrue(entry.Enabled);

        var views = host.CollectViews("en");
        Assert.AreEqual(1, views.Count, "CollectViews should return the view when final state is enabled");

        // 2. Repeat with final false
        tasks.Clear();
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(host.SetEnabled("ext_order", true));
            tasks.Add(host.SetEnabled("ext_order", false));
        }
        await Task.WhenAll(tasks);

        entry = host.Entries.First(e => e.Id == "ext_order");
        Assert.AreEqual(ExtensionStatus.Disabled, entry.Status);
        Assert.IsFalse(entry.Enabled);

        views = host.CollectViews("en");
        Assert.AreEqual(0, views.Count, "CollectViews should return no views when final state is disabled");
    }

    [TestMethod]
    public async Task ExtensionHost_SetEnabledFalse_KeepsFailureStatus()
    {
        // Extension not in allowlist -> NotApproved
        string extDir = CreateExtension("ext_not_approved", out string _);

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_not_approved"] = new ExtensionSetting { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = FirstPartyAllowlist.Empty,
            Approvals = null,
            HostIsElevated = false,
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var entry = host.Entries.First(e => e.Id == "ext_not_approved");
        Assert.AreEqual(ExtensionStatus.NotApproved, entry.Status);
        Assert.IsTrue(entry.Enabled);
        Assert.IsTrue(ExtensionStatusText.CanToggle(entry.Status, entry.Enabled));

        // Toggle off
        await host.SetEnabled("ext_not_approved", false);

        entry = host.Entries.First(e => e.Id == "ext_not_approved");
        Assert.AreEqual(ExtensionStatus.NotApproved, entry.Status, "Status must remain NotApproved when toggled off");
        Assert.IsFalse(entry.Enabled);
        Assert.IsFalse(ExtensionStatusText.CanToggle(entry.Status, entry.Enabled), "CanToggle must stay false for disabled NotApproved extension");
    }

    [TestMethod]
    public async Task ExtensionHost_SanitizeTab_LargeWhitespaceKey_BoundedQuickly()
    {
        string extDir = CreateExtension("ext_large_ws", out string hash);

        string hugeWhitespace = new string(' ', 5 * 1024 * 1024);
        var tabs = Enumerable.Range(0, 128)
            .Select(i => new DetailTab(hugeWhitespace, $"Tab {i}", "Sub", "Det", "Hw", Array.Empty<Fact>()))
            .ToList();

        DummyViewsExtension.ViewFunc = _ => new ExtensionView(Array.Empty<WidgetPage>(), null, tabs);

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_large_ws"] = new ExtensionSetting { Enabled = true }
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { hash });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var collectTask = Task.Run(() => host.CollectViews("en"));
        bool finishedInTime = collectTask.Wait(5000);
        Assert.IsTrue(finishedInTime, "CollectViews with 128 large whitespace tabs took longer than 5 seconds");

        var views = collectTask.Result;
        Assert.AreEqual(1, views.Count);
        Assert.AreEqual(0, views[0].View.Tabs.Count, "No tabs should survive large whitespace keys");
    }

    [TestMethod]
    public async Task ExtensionHost_CollectViews_DisplayNameFollowsLanguageEvenWhenGetViewThrows()
    {
        string extDir = CreateExtension("ext_throwing_view", out string hash);

        DummyViewsExtension.DisplayNameFunc = lang => lang == "th" ? "ชื่อไทย" : "English Name";
        DummyViewsExtension.ViewFunc = _ => throw new InvalidOperationException("ViewFunc boom");

        var config = new AppConfig
        {
            Lang = "th",
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_throwing_view"] = new ExtensionSetting { Enabled = true }
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { hash });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        host.CollectViews("th");
        Assert.AreEqual("ชื่อไทย", host.Entries[0].Name);

        host.CollectViews("en");
        Assert.AreEqual("English Name", host.Entries[0].Name, "Name must follow language even when GetView throws");
    }

    [TestMethod]
    public async Task ExtensionHost_CollectViews_OneHostileAndOneHealthyProvider_ReturnsHealthyView()
    {
        string hostileDir = CreateExtension("ext_hostile_1", out string hash1);
        string healthyDir = CreateExtension("ext_healthy_2", out string hash2);

        int callCount = 0;
        DummyViewsExtension.ViewFunc = _ =>
        {
            if (Interlocked.Increment(ref callCount) == 1)
            {
                return new ExtensionView(new ThrowingList<WidgetPage>(), null, Array.Empty<DetailTab>());
            }
            return new ExtensionView(
                new[] { new WidgetPage("HEALTHY", "OK", "Healthy line", null, 50, Severity.Good) },
                null,
                Array.Empty<DetailTab>());
        };

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_hostile_1"] = new ExtensionSetting { Enabled = true },
                ["ext_healthy_2"] = new ExtensionSetting { Enabled = true },
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { hash1, hash2 });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var views = host.CollectViews("en");
        Assert.AreEqual(1, views.Count, "Healthy view should be returned despite hostile provider throwing");
        Assert.AreEqual("HEALTHY", views[0].View.WidgetPages[0].Title);
    }

    [TestMethod]
    public async Task ExtensionHost_SanitizeView_NullElementsInLists_SkippedWithoutException()
    {
        string extDir = CreateExtension("ext_nulls", out string hash);

        DummyViewsExtension.ViewFunc = _ => new ExtensionView(
            new WidgetPage?[]
            {
                null,
                new WidgetPage("VALID PAGE", "OK", "Line", null, 50, Severity.Good),
                null
            }!,
            new OverviewSection("Note", new OverviewRow?[]
            {
                null,
                new OverviewRow("VALID ROW", "Detail", "50%", 50, Severity.Good, null),
                null
            }!),
            new DetailTab?[]
            {
                null,
                new DetailTab(
                    "valid_tab", "Tab", "Sub", "Det", "Hw",
                    new Fact?[] { null, new Fact("FactName", "FactValue"), null }!),
                null
            }!
        );

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_nulls"] = new ExtensionSetting { Enabled = true }
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { hash });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var views = host.CollectViews("en");
        Assert.AreEqual(1, views.Count);

        var v = views[0].View;
        Assert.AreEqual(1, v.WidgetPages.Count);
        Assert.AreEqual("VALID PAGE", v.WidgetPages[0].Title);

        Assert.IsNotNull(v.Overview);
        Assert.AreEqual(1, v.Overview.Rows.Count);
        Assert.AreEqual("VALID ROW", v.Overview.Rows[0].Title);

        Assert.AreEqual(1, v.Tabs.Count);
        Assert.AreEqual("valid_tab", v.Tabs[0].Key);
        Assert.AreEqual(1, v.Tabs[0].Facts.Count);
        Assert.AreEqual("FactName", v.Tabs[0].Facts[0].Name);
    }

    [TestMethod]
    public async Task ExtensionHost_SetEnabledTrueAgain_DoesNotShutdownOrReinitialize()
    {
        string extDir = CreateExtension("ext_noop_reconcile", out string hash);

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_noop_reconcile"] = new ExtensionSetting { Enabled = true }
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { hash });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var entry = host.Entries.FirstOrDefault(e => e.Id == "ext_noop_reconcile");
        Assert.IsNotNull(entry);
        Assert.AreEqual(ExtensionStatus.Loaded, entry.Status);
        Assert.AreEqual(1, DummyViewsExtension.InitializeCount, "Initialize should have run once upon start");
        Assert.IsFalse(DummyViewsExtension.ShutdownCalled, "Shutdown should not have been called yet");

        // Reconcile with refresh == false (SetEnabled true again)
        await host.SetEnabled("ext_noop_reconcile", true);

        Assert.AreEqual(1, DummyViewsExtension.InitializeCount, "Initialize should not run again on no-op reconcile");
        Assert.IsFalse(DummyViewsExtension.ShutdownCalled, "Shutdown should not be called on no-op reconcile");
        Assert.AreEqual(ExtensionStatus.Loaded, host.Entries.First(e => e.Id == "ext_noop_reconcile").Status);
    }

    [TestMethod]
    public async Task ExtensionHost_HashMismatch_SetEnabledFalse_KeepsHashMismatch()
    {
        string extDir = CreateExtension("ext_corrupted", out string hash);

        // Corrupt the DLL to cause a HashMismatch
        string dllPath = Path.Combine(extDir, "SysMonitor.Tests.DummyExtension.dll");
        File.WriteAllBytes(dllPath, new byte[] { 0x00, 0x01, 0x02, 0x03 });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_corrupted"] = new ExtensionSetting { Enabled = true }
            }
        };

        var allowlist = new FirstPartyAllowlist(new[] { hash });
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = allowlist,
            HostIsElevated = false,
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var entry = host.Entries.FirstOrDefault(e => e.Id == "ext_corrupted");
        Assert.IsNotNull(entry);
        Assert.AreEqual(ExtensionStatus.HashMismatch, entry.Status);

        // Disabling an extension that failed with HashMismatch should keep HashMismatch status
        await host.SetEnabled("ext_corrupted", false);

        var updatedEntry = host.Entries.FirstOrDefault(e => e.Id == "ext_corrupted");
        Assert.IsNotNull(updatedEntry);
        Assert.AreEqual(ExtensionStatus.HashMismatch, updatedEntry.Status);
        Assert.IsFalse(updatedEntry.Enabled);
    }

    [TestMethod]
    public async Task ExtensionHost_KeptLoaded_FirstPartyProvider_StaysLoadedWhenDisabled_AnswersDiskTemps_SuppressesViews()
    {
        DummyDemandAndTempExtension.Reset();
        DummyDemandAndTempExtension.TemperaturesToReturn = new Dictionary<int, int> { [0] = 42 };

        string extDir = CreateExtensionWithManifest("first_party_temp", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyDemandAndTempExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["first_party_temp"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            Language = "en",
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var entry = host.Entries.FirstOrDefault(e => e.Id == "first_party_temp");
        Assert.IsNotNull(entry);
        Assert.AreEqual(ExtensionStatus.Loaded, entry.Status);
        Assert.IsTrue(entry.Enabled);

        // Active: views are collected, disk temperatures returned
        var views = host.CollectViews("en");
        Assert.AreEqual(1, views.Count);
        var temps = host.GetDiskTemperatures();
        Assert.AreEqual(42, temps[0]);

        // Disable it: because it is first-party and provides "disk-temperature", it stays Loaded!
        await host.SetEnabled("first_party_temp", false);

        var disabledEntry = host.Entries.FirstOrDefault(e => e.Id == "first_party_temp");
        Assert.IsNotNull(disabledEntry);
        Assert.AreEqual(ExtensionStatus.Loaded, disabledEntry.Status, "Kept-loaded extension must stay Loaded when disabled");
        Assert.IsFalse(disabledEntry.Enabled, "Enabled flag must be false");

        // Views must NOT be collected for disabled kept-loaded extension
        var viewsAfterDisable = host.CollectViews("en");
        Assert.AreEqual(0, viewsAfterDisable.Count, "Views must be suppressed when extension is disabled");

        // But GetDiskTemperatures() must still work!
        var tempsAfterDisable = host.GetDiskTemperatures();
        Assert.IsTrue(tempsAfterDisable.ContainsKey(0));
        Assert.AreEqual(42, tempsAfterDisable[0], "Kept-loaded provider must still answer disk temperatures");
    }

    [TestMethod]
    public async Task ExtensionHost_KeptLoaded_ThirdPartyProvider_DisabledWhenToggledOff()
    {
        DummyDemandAndTempExtension.Reset();
        string extDir = CreateExtensionWithManifest("third_party_temp", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyDemandAndTempExtension).FullName!;
        });

        string storePath = Path.Combine(_tempDir, "test_approvals.json");
        var approvals = new ApprovalStore(storePath, _ => true, _ => { }, _ => { }, _ => { });
        approvals.Approve("third_party_temp", "Third Party Temp", manifestHash);

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["third_party_temp"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            Language = "en",
            FirstParty = FirstPartyAllowlist.Empty, // Third party
            Approvals = approvals
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var entry = host.Entries.FirstOrDefault(e => e.Id == "third_party_temp");
        Assert.IsNotNull(entry);
        Assert.AreEqual(ExtensionStatus.Loaded, entry.Status);

        // Disable third party provider: must become Disabled (not kept loaded)
        await host.SetEnabled("third_party_temp", false);

        var disabledEntry = host.Entries.FirstOrDefault(e => e.Id == "third_party_temp");
        Assert.IsNotNull(disabledEntry);
        Assert.AreEqual(ExtensionStatus.Disabled, disabledEntry.Status, "Third party provider must be Disabled when toggled off");
    }

    [TestMethod]
    public async Task ExtensionHost_KeptLoaded_FirstPartyNonProvider_DisabledWhenToggledOff()
    {
        string extDir = CreateExtensionWithManifest("first_party_views_only", out string manifestHash, m =>
        {
            m.Contract = "1.0";
            m.Provides = null; // No disk-temperature
            m.Entry.Type = typeof(DummyViewsExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["first_party_views_only"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            Language = "en",
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var entry = host.Entries.FirstOrDefault(e => e.Id == "first_party_views_only");
        Assert.IsNotNull(entry);
        Assert.AreEqual(ExtensionStatus.Loaded, entry.Status);

        await host.SetEnabled("first_party_views_only", false);

        var disabledEntry = host.Entries.FirstOrDefault(e => e.Id == "first_party_views_only");
        Assert.IsNotNull(disabledEntry);
        Assert.AreEqual(ExtensionStatus.Disabled, disabledEntry.Status, "First-party non-provider must be Disabled when toggled off");
    }

    [TestMethod]
    public async Task ExtensionHost_GetDiskTemperatures_SanitizationAndLimits()
    {
        DummyDemandAndTempExtension.Reset();
        // Return 70 items with invalid drives and temperatures:
        // Drive -1 and 256 invalid.
        // Temp 0, -5, 150 invalid.
        var dict = new Dictionary<int, int>();
        dict[-1] = 40;  // invalid drive
        dict[256] = 40; // invalid drive
        dict[0] = 0;    // invalid temp (0)
        dict[1] = -5;   // invalid temp (-5)
        dict[2] = 150;  // invalid temp (150)
        for (int i = 10; i < 80; i++) // 70 items
        {
            dict[i] = 45;
        }
        DummyDemandAndTempExtension.TemperaturesToReturn = dict;

        string extDir = CreateExtensionWithManifest("temp_sanitize", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyDemandAndTempExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["temp_sanitize"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var temps = host.GetDiskTemperatures();
        Assert.IsFalse(temps.ContainsKey(-1), "Drive -1 must be filtered");
        Assert.IsFalse(temps.ContainsKey(256), "Drive 256 must be filtered");
        Assert.IsFalse(temps.ContainsKey(0), "Temp 0 must be filtered");
        Assert.IsFalse(temps.ContainsKey(1), "Temp -5 must be filtered");
        Assert.IsFalse(temps.ContainsKey(2), "Temp 150 must be filtered");

        // 70 valid readings are left after the 5 invalid ones; the host keeps the first 64 of them
        // (drives 10..73 in the order the provider enumerates them).
        Assert.AreEqual(64, temps.Count, "Exactly 64 valid readings must be retained");
        Assert.AreEqual(45, temps[10], "The first valid reading is kept");
        Assert.AreEqual(45, temps[73], "The 64th valid reading is kept");
        Assert.IsFalse(temps.ContainsKey(74), "The 65th valid reading is cut");
        Assert.IsFalse(temps.ContainsKey(79), "The last valid reading is cut");
    }

    [TestMethod]
    public async Task ExtensionHost_GetDiskTemperatures_SeventyValidReadings_KeepsExactlySixtyFour()
    {
        DummyDemandAndTempExtension.Reset();
        var dict = new Dictionary<int, int>();
        for (int i = 0; i < 70; i++)
        {
            dict[i] = 30 + i % 20;
        }
        DummyDemandAndTempExtension.TemperaturesToReturn = dict;

        string extDir = CreateExtensionWithManifest("temp_seventy", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyDemandAndTempExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["temp_seventy"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var temps = host.GetDiskTemperatures();
        Assert.AreEqual(64, temps.Count);
        Assert.AreEqual(30, temps[0], "First reading is kept");
        Assert.AreEqual(30 + 63 % 20, temps[63], "64th reading is kept with its own value");
        Assert.IsFalse(temps.ContainsKey(64), "65th reading is cut");
        Assert.IsFalse(temps.ContainsKey(69), "Last reading is cut");
        foreach (var kv in temps)
        {
            Assert.AreEqual(30 + kv.Key % 20, kv.Value, $"Value for drive {kv.Key} must be passed through unchanged");
        }
    }

    [TestMethod]
    public async Task ExtensionHost_GetDiskTemperatures_HostileProvider_DoesNotCrashHost()
    {
        DummyDemandAndTempExtension.Reset();
        DummyDemandAndTempExtension.ThrowOnGetTemperatures = true;

        string extDir = CreateExtensionWithManifest("temp_throw", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyDemandAndTempExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["temp_throw"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var temps = host.GetDiskTemperatures();
        Assert.IsNotNull(temps);
        Assert.AreEqual(0, temps.Count);
    }

    [TestMethod]
    public async Task ExtensionHost_SetDemand_PushesOnChangeOnlyAndOnToggle()
    {
        DummyDemandAndTempExtension.Reset();

        string extDir = CreateExtensionWithManifest("demand_test", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyDemandAndTempExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["demand_test"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        // After start, initial demand (Views only because enabled) was pushed
        Assert.AreEqual(1, DummyDemandAndTempExtension.SetDemandCallCount);
        Assert.AreEqual((int)ExtensionDemand.Views, DummyDemandAndTempExtension.DemandHistory[0]);

        // Set demand with diskTemperatureNeeded = true -> demand becomes Views | DiskTemperature
        host.SetDemand(diskTemperatureNeeded: true);
        Assert.AreEqual(2, DummyDemandAndTempExtension.SetDemandCallCount);
        Assert.AreEqual((int)(ExtensionDemand.Views | ExtensionDemand.DiskTemperature), DummyDemandAndTempExtension.DemandHistory[1]);

        // Same demand again -> NO push
        host.SetDemand(diskTemperatureNeeded: true);
        Assert.AreEqual(2, DummyDemandAndTempExtension.SetDemandCallCount, "SetDemand must not push when demand is unchanged");

        // Toggle disabled -> Views bit drops -> demand becomes DiskTemperature only
        await host.SetEnabled("demand_test", false);
        Assert.AreEqual(3, DummyDemandAndTempExtension.SetDemandCallCount);
        Assert.AreEqual((int)ExtensionDemand.DiskTemperature, DummyDemandAndTempExtension.DemandHistory[2]);

        // Toggle enabled -> Views bit restored -> Views | DiskTemperature
        await host.SetEnabled("demand_test", true);
        Assert.AreEqual(4, DummyDemandAndTempExtension.SetDemandCallCount);
        Assert.AreEqual((int)(ExtensionDemand.Views | ExtensionDemand.DiskTemperature), DummyDemandAndTempExtension.DemandHistory[3]);
    }

    [TestMethod]
    public async Task ExtensionHost_EndToEnd_RealSmartExtension_LoadsAndPresentsNeedAdmin()
    {
        // Build real package from SysMonitor.Ext.Smart assembly
        string realDllSource = typeof(SysMonitor.Ext.Smart.SmartExtension).Assembly.Location;
        Assert.IsTrue(File.Exists(realDllSource), $"SysMonitor.Ext.Smart.dll not found at: {realDllSource}");

        string extDir = Path.Combine(_tempDir, "smart");
        Directory.CreateDirectory(extDir);

        string targetDll = Path.Combine(extDir, "SysMonitor.Ext.Smart.dll");
        File.Copy(realDllSource, targetDll, overwrite: true);

        byte[] dllBytes = File.ReadAllBytes(targetDll);
        string dllSha256 = ComputeSha256(dllBytes);

        var manifest = new ExtensionManifest
        {
            Id = "smart",
            Name = "SMART",
            Version = "1.0.0",
            Contract = "1.1",
            Provides = new List<string> { "disk-temperature" },
            PlaceAfter = "disk",
            Entry = new ExtensionEntry
            {
                Assembly = "SysMonitor.Ext.Smart.dll",
                Type = typeof(SysMonitor.Ext.Smart.SmartExtension).FullName!
            },
            Files = new List<ExtensionFile>
            {
                new()
                {
                    Path = "SysMonitor.Ext.Smart.dll",
                    Sha256 = dllSha256,
                    Native = false
                }
            }
        };

        string manifestJson = JsonSerializer.Serialize(manifest, ExtensionManifest.JsonOptions);
        string manifestPath = Path.Combine(extDir, "extension.json");
        File.WriteAllText(manifestPath, manifestJson);

        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        string manifestSha256 = ComputeSha256(manifestBytes);

        var config = new AppConfig
        {
            ShowCpu = false, ShowRam = false, ShowDisk = false, ShowNetwork = false, ShowGpu = false,
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["smart"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            Language = "en",
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var entry = host.Entries.FirstOrDefault(e => e.Id == "smart");
        Assert.IsNotNull(entry);
        Assert.AreEqual(ExtensionStatus.Loaded, entry.Status);

        // Collect views: should contain missing-core message because DiskInfoCore.dll is absent
        var views = host.CollectViews("en");
        Assert.AreEqual(1, views.Count);
        Assert.AreEqual("smart", views[0].Id);
        Assert.AreEqual("disk", views[0].PlaceAfter);
        Assert.IsNotNull(views[0].View.Overview);
        Assert.IsTrue(views[0].View.Tabs.Count > 0);

        StringAssert.Contains(views[0].View.WidgetPages[0].Message, "DiskInfoCore.dll");
        StringAssert.Contains(views[0].View.Overview.Note, "DiskInfoCore.dll");
        Assert.AreEqual("status", views[0].View.Tabs[0].Key);
        StringAssert.Contains(views[0].View.Tabs[0].Detail, "DiskInfoCore.dll");

        // Now verify WidgetViewModel integrates and renders this view
        var vm = new SysMonitor.ViewModels.WidgetViewModel(config);
        var snapshot = new SysMonitor.Model.Snapshot
        {
            Ready = true,
            Extensions = views
        };

        vm.RebuildViews(snapshot);
        vm.UpdateWidget(snapshot);
        StringAssert.Contains(vm.Message, "DiskInfoCore.dll");
        Assert.IsTrue(vm.ShowMessage);

        vm.UpdateOverall(snapshot);
        Assert.IsTrue(vm.Sections.Any(s => s.Id == "ext:smart"));
        StringAssert.Contains(vm.Sections.First(s => s.Id == "ext:smart").Note, "DiskInfoCore.dll");

        vm.PushHistory(snapshot);
        Assert.IsTrue(vm.Tabs.Any(t => t.Key == "ext:smart:status"));
        StringAssert.Contains(vm.Tabs.First(t => t.Key == "ext:smart:status").Detail, "DiskInfoCore.dll");
    }

    [TestMethod]
    public void AppConfig_Migration_LegacyShowSmart_And_FullTab()
    {
        string json = """
        {
            "show_smart": true,
            "full_tab": "smart"
        }
        """;

        string tempConfigPath = Path.Combine(_tempDir, "legacy_config.json");
        File.WriteAllText(tempConfigPath, json);

        var config = AppConfig.LoadFrom(tempConfigPath);

        // Legacy show_smart should migrate to Extensions["smart"]
        Assert.IsTrue(config.Extensions.ContainsKey("smart"));
        Assert.IsTrue(config.Extensions["smart"].Enabled);

        // full_tab "smart" should migrate to "ext:smart:status"
        Assert.AreEqual("ext:smart:status", config.FullTab);

        // Saving config back should NOT contain "show_smart"
        string savePath = Path.Combine(_tempDir, "saved_config.json");
        config.SaveTo(savePath);

        string savedJson = File.ReadAllText(savePath);
        Assert.IsFalse(savedJson.Contains("show_smart"), "Saved JSON must omit legacy show_smart property");
    }

    [TestMethod]
    public void AppConfig_Migration_LegacyShowSmart_False_And_DiskTab()
    {
        string json = """
        {
            "show_smart": false,
            "full_tab": "smart0"
        }
        """;

        string tempConfigPath = Path.Combine(_tempDir, "legacy_config_off.json");
        File.WriteAllText(tempConfigPath, json);

        var config = AppConfig.LoadFrom(tempConfigPath);

        Assert.IsTrue(config.Extensions.ContainsKey("smart"));
        Assert.IsFalse(config.Extensions["smart"].Enabled);
        Assert.AreEqual("ext:smart:disk0", config.FullTab);
    }

    [TestMethod]
    [DataRow("cpu", true)]
    [DataRow("ram", true)]
    [DataRow("disk", true)]
    [DataRow("gpu", true)]
    [DataRow("network", true)]
    [DataRow(null, true)]
    [DataRow("foo", false)]
    [DataRow("", false)]
    [DataRow("Disk", false)]
    public void Manifest_PlaceAfter_Validation(string? placeAfter, bool expectedValid)
    {
        var manifest = new ExtensionManifest
        {
            Id = "test_ext",
            Name = "Test Ext",
            Version = "1.0.0",
            Contract = "1.1",
            PlaceAfter = placeAfter,
            Entry = new ExtensionEntry
            {
                Assembly = "test.dll",
                Type = "Test.Type"
            },
            Files = new List<ExtensionFile>
            {
                new()
                {
                    Path = "test.dll",
                    Sha256 = new string('a', 64),
                    Native = false
                }
            }
        };

        bool valid = ExtensionManifest.Validate(manifest, out string? error);
        if (expectedValid)
        {
            Assert.IsTrue(valid, $"Validation should succeed for place_after '{placeAfter ?? "<null>"}', but failed with: {error}");
            Assert.IsNull(error);
        }
        else
        {
            Assert.IsFalse(valid, $"Validation should fail for place_after '{placeAfter ?? "<null>"}'");
            Assert.IsNotNull(error);
            StringAssert.Contains(error, "place_after");
        }
    }

    [TestMethod]
    [DataRow("foo")]
    [DataRow("")]
    [DataRow("Disk")]
    public async Task ExtensionHost_PlaceAfter_InvalidValue_ResultsInManifestInvalidStatus(string invalidPlaceAfter)
    {
        string extDir = CreateExtensionWithManifest("ext_invalid_place", out string manifestHash, m =>
        {
            m.PlaceAfter = invalidPlaceAfter;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_invalid_place"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        Assert.AreEqual(1, host.Entries.Count);
        Assert.AreEqual(ExtensionStatus.ManifestInvalid, host.Entries[0].Status);
    }

    [TestMethod]
    public async Task ExtensionHost_GetDiskTemperatures_ThrowingEnumerator_DoesNotStopHealthyProvider()
    {
        DummyViewsExtension.Reset();
        DummyDemandAndTempExtension.Reset();

        // Faulty provider with throwing enumerator
        DummyViewsExtension.DiskTemperaturesFunc = () => new ThrowingEnumeratorDictionary();
        string extDirFaulty = CreateExtensionWithManifest("ext_a_faulty", out string hashFaulty, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyViewsExtension).FullName!;
        });

        // Healthy provider returning drive 0 -> 42°C
        DummyDemandAndTempExtension.TemperaturesToReturn = new Dictionary<int, int> { [0] = 42 };
        string extDirHealthy = CreateExtensionWithManifest("ext_b_healthy", out string hashHealthy, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyDemandAndTempExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_a_faulty"] = new() { Enabled = true },
                ["ext_b_healthy"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { hashFaulty, hashHealthy })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var temps = host.GetDiskTemperatures();
        Assert.IsNotNull(temps);
        Assert.AreEqual(1, temps.Count, "Throwing enumerator on provider 'ext_a_faulty' must not stop healthy provider 'ext_b_healthy'");
        Assert.AreEqual(42, temps[0]);
    }

    [TestMethod]
    public async Task ExtensionHost_GetDiskTemperatures_EndlessInvalidPairs_ReturnsWithin256Cap()
    {
        DummyViewsExtension.Reset();
        DummyViewsExtension.DiskTemperaturesFunc = () => new EndlessInvalidDictionary();

        string extDir = CreateExtensionWithManifest("ext_endless", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyViewsExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_endless"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var tempTask = Task.Run(() => host.GetDiskTemperatures());
        bool completed = tempTask.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(completed, "GetDiskTemperatures must return within 5 seconds bounded wait under 256 inspected pairs cap");

        var temps = tempTask.Result;
        Assert.IsNotNull(temps);
        Assert.AreEqual(0, temps.Count, "All pairs were invalid so no temperatures should be recorded");
    }

    [TestMethod]
    public async Task ExtensionHost_GetDiskTemperatures_DuplicateKeysAcrossProviders_OrdinalFirstIdWins()
    {
        DummyViewsExtension.Reset();
        DummyDemandAndTempExtension.Reset();

        // "ext_a" comes before "ext_b" in StringComparer.Ordinal
        DummyViewsExtension.DiskTemperaturesFunc = () => new Dictionary<int, int> { [0] = 40, [1] = 45 };
        string extDirA = CreateExtensionWithManifest("ext_a", out string hashA, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyViewsExtension).FullName!;
        });

        DummyDemandAndTempExtension.TemperaturesToReturn = new Dictionary<int, int> { [0] = 70, [2] = 50 };
        string extDirB = CreateExtensionWithManifest("ext_b", out string hashB, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyDemandAndTempExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_a"] = new() { Enabled = true },
                ["ext_b"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { hashA, hashB })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var temps = host.GetDiskTemperatures();
        Assert.IsNotNull(temps);
        Assert.AreEqual(3, temps.Count);
        Assert.AreEqual(40, temps[0], "Ordinal-first provider id ('ext_a') must win for drive 0");
        Assert.AreEqual(45, temps[1]);
        Assert.AreEqual(50, temps[2]);
    }

    [TestMethod]
    public async Task ExtensionHost_GetDiskTemperatures_NoExtensionsLoaded_ReturnsEmptyDictionary()
    {
        var config = new AppConfig();
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0)
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        var temps = host.GetDiskTemperatures();
        Assert.IsNotNull(temps);
        Assert.AreEqual(0, temps.Count, "GetDiskTemperatures with no extensions loaded must return empty dictionary");
    }

    [TestMethod]
    public async Task ExtensionHost_Demand_AfterReload_NewInstanceGetsSetDemandOnceWithCurrentDemand()
    {
        DummyViewsExtension.Reset();

        string extDir = CreateExtensionWithManifest("demand_reload", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Entry.Type = typeof(DummyViewsExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["demand_reload"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        // Initial load: 1 initialize, 1 SetDemand with Views
        Assert.AreEqual(1, DummyViewsExtension.InitializeCount);
        Assert.AreEqual(1, DummyViewsExtension.SetDemandCount);
        Assert.AreEqual(ExtensionDemand.Views, DummyViewsExtension.LastDemand);

        // Reload the extension
        await host.Reload("demand_reload");

        // New instance must have been initialized and received SetDemand once
        Assert.AreEqual(2, DummyViewsExtension.InitializeCount, "New instance must be initialized upon reload");
        Assert.AreEqual(2, DummyViewsExtension.SetDemandCount, "New instance must receive SetDemand once upon reload");
        Assert.AreEqual(ExtensionDemand.Views, DummyViewsExtension.LastDemand);
    }

    [TestMethod]
    public async Task ExtensionHost_Demand_NonKeptExtension_SwitchedOffThenOn_NewInstanceGetsSetDemand()
    {
        DummyViewsExtension.Reset();

        // Non-kept: does NOT declare "disk-temperature" in provides, so disabling unloads it completely
        string extDir = CreateExtensionWithManifest("demand_non_kept", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string>();
            m.Entry.Type = typeof(DummyViewsExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["demand_non_kept"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        Assert.AreEqual(1, DummyViewsExtension.InitializeCount);
        Assert.AreEqual(1, DummyViewsExtension.SetDemandCount);
        Assert.AreEqual(ExtensionDemand.Views, DummyViewsExtension.LastDemand);

        // Switch off
        await host.SetEnabled("demand_non_kept", false);
        Assert.AreEqual(ExtensionStatus.Disabled, host.Entries[0].Status);
        Assert.IsTrue(DummyViewsExtension.ShutdownCalled, "Non-kept extension must be shut down when disabled");

        // Switch on again: loads a new instance and delivers SetDemand
        await host.SetEnabled("demand_non_kept", true);
        Assert.AreEqual(ExtensionStatus.Loaded, host.Entries[0].Status);
        Assert.AreEqual(2, DummyViewsExtension.InitializeCount, "New instance must be initialized after being re-enabled");
        Assert.AreEqual(2, DummyViewsExtension.SetDemandCount, "New instance must receive SetDemand after being re-enabled");
        Assert.AreEqual(ExtensionDemand.Views, DummyViewsExtension.LastDemand);
    }

    [TestMethod]
    public async Task ExtensionHost_Demand_BlockingCallback_SecondCallReturnsWithoutWaitingAndEndsOnFinalValue()
    {
        DummyViewsExtension.Reset();

        string extDir = CreateExtensionWithManifest("demand_blocking", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyViewsExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["demand_blocking"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var callbackEntered = new ManualResetEventSlim(false);
        using var unblockCallback = new ManualResetEventSlim(false);

        int callIndex = 0;
        DummyViewsExtension.SetDemandAction = d =>
        {
            if (Interlocked.Increment(ref callIndex) == 1)
            {
                callbackEntered.Set();
                unblockCallback.Wait(TimeSpan.FromSeconds(5));
            }
        };

        using var host = new ExtensionHost(config, options);
        var startTask = host.StartAsync();

        bool entered = callbackEntered.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(entered, "First SetDemand callback must be entered");

        // While first callback is blocked, flip demand on another thread
        var sw = System.Diagnostics.Stopwatch.StartNew();
        host.SetDemand(diskTemperatureNeeded: true);
        sw.Stop();
        Assert.IsTrue(sw.ElapsedMilliseconds < 1000, "Second SetDemand call must return immediately without waiting for the blocked callback");

        // Release the blocked first callback
        unblockCallback.Set();
        await startTask;

        // Instance must end on final value (Views | DiskTemperature)
        Assert.AreEqual(ExtensionDemand.Views | ExtensionDemand.DiskTemperature, DummyViewsExtension.LastDemand);
        Assert.AreEqual(2, DummyViewsExtension.SetDemandCount);
    }

    [TestMethod]
    public async Task ExtensionHost_Demand_ReloadCompletingWhileSetDemandPaused_NeverDeliversSetDemandAfterShutdown()
    {
        DummyViewsExtension.Reset();

        string extDir = CreateExtensionWithManifest("demand_shutdown_guard", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyViewsExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["demand_shutdown_guard"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        using var callbackEntered = new ManualResetEventSlim(false);
        using var unblockCallback = new ManualResetEventSlim(false);

        DummyViewsExtension.SetDemandAction = d =>
        {
            callbackEntered.Set();
            unblockCallback.Wait(TimeSpan.FromSeconds(5));
        };

        // Trigger SetDemand on a background thread so it pauses inside SetDemandAction
        var demandTask = Task.Run(() => host.SetDemand(diskTemperatureNeeded: true));
        bool entered = callbackEntered.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(entered, "SetDemand callback must pause inside action");

        using var reloadWaiting = new ManualResetEventSlim(false);
        host.OnReloadRetiring = id =>
        {
            if (id == "demand_shutdown_guard")
            {
                reloadWaiting.Set();
            }
        };

        // Trigger Reload while SetDemand is paused
        var reloadTask = host.Reload("demand_shutdown_guard");
        bool reloadReachedRetirement = reloadWaiting.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(reloadReachedRetirement, "Reload must enter retirement block while SetDemand is paused");

        // Unblock paused callback
        unblockCallback.Set();

        await demandTask;
        await reloadTask;

        Assert.IsTrue(DummyViewsExtension.ShutdownCalled, "Old instance must have been shut down");
        Assert.IsFalse(DummyViewsExtension.DemandDeliveredAfterShutdown, "Old instance must never receive SetDemand after Shutdown");
        Assert.AreEqual(2, DummyViewsExtension.InitializeCount, "New instance must be initialized");
        // Old instance: the initial push and the paused one; the new instance: exactly one more.
        Assert.AreEqual(3, DummyViewsExtension.SetDemandCount, "New instance must receive SetDemand once");
        Assert.AreEqual(ExtensionDemand.DiskTemperature | ExtensionDemand.Views, DummyViewsExtension.LastDemand, "New instance must receive correct demand flags");
    }

    [TestMethod]
    public async Task ExtensionHost_Fix1_Reload_EarlyFailure_RecoversWhenFileFixedAndAnswersDiskTemperatures()
    {
        DummyDemandAndTempExtension.Reset();
        DummyDemandAndTempExtension.TemperaturesToReturn = new Dictionary<int, int> { [0] = 46 };

        string extDir = CreateExtensionWithManifest("firstparty_early_fail", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyDemandAndTempExtension).FullName!;
        });

        // Corrupt the DLL file so hash mismatches during load
        string dllPath = Path.Combine(extDir, "SysMonitor.Tests.DummyExtension.dll");
        byte[] originalDllBytes = File.ReadAllBytes(dllPath);
        byte[] corruptedBytes = (byte[])originalDllBytes.Clone();
        corruptedBytes[0] ^= 0xFF;
        File.WriteAllBytes(dllPath, corruptedBytes);

        // Switched off in config
        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["firstparty_early_fail"] = new() { Enabled = false }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        // 1. Initial StartAsync gave HashMismatch
        Assert.AreEqual(1, host.Entries.Count);
        Assert.AreEqual(ExtensionStatus.HashMismatch, host.Entries[0].Status);
        Assert.AreEqual(0, host.GetDiskTemperatures().Count);

        // 2. Fix the file
        File.WriteAllBytes(dllPath, originalDllBytes);

        // 3. Reload(id)
        await host.Reload("firstparty_early_fail");

        // 4. Status Loaded, GetDiskTemperatures() answers using dummy
        Assert.AreEqual(1, host.Entries.Count);
        Assert.AreEqual(ExtensionStatus.Loaded, host.Entries[0].Status);
        var temps = host.GetDiskTemperatures();
        Assert.AreEqual(1, temps.Count);
        Assert.AreEqual(46, temps[0]);
    }

    [TestMethod]
    public void ExtensionHost_Fix2_PlaceAfter_CanonicalOrderFallback_OverviewAndTabs()
    {
        var config = new AppConfig
        {
            ShowCpu = true,
            ShowRam = true,
            ShowDisk = false,
            ShowGpu = true,
            ShowNetwork = true,
        };

        var extView = new ExtensionView(
            new[] { new WidgetPage("Ext", "Detail", "Note", 0, 0, Severity.Good) },
            new OverviewSection("Ext Note", new[] { new OverviewRow("R1", "D1", "V1", 50, Severity.Good, 50) }),
            new[] { new DetailTab("tab1", "Ext Tab", "Sum", "Det", "Hw", Array.Empty<Fact>()) }
        );

        var extData = new ExtensionViewData(
            "ext_smart",
            "SMART",
            extView,
            PlaceAfter: "disk"
        );

        var snap = new Snapshot
        {
            Ready = true,
            CpuInfo = new CpuInfo { Name = "Test CPU", Cores = 4, Logical = 8 },
            Cores = new[] { new Core { Usage = 10 } },
            Ram = new Ram { Usage = 50, UsedGb = 8, TotalGb = 16 },
            Gpu = new Gpu { Present = true, Name = "Test GPU", Usage = 20 },
            Adapters = new[] { new Adapter { Id = "eth0", Name = "Ethernet", SpeedMbps = 1000 } },
            Extensions = new[] { extData }
        };

        // Test 1: ShowDisk = false with GPU + network shown -> order: cpu, ram, ext, gpu, net
        var vm = new WidgetViewModel(config);
        vm.UpdateOverall(snap);

        var sectionIds = vm.Sections.Select(s => s.Id).ToList();
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "ext:ext_smart", "gpu", "net" }, sectionIds);

        vm.PushHistory(snap);
        var tabKeys = vm.Tabs.Select(t => t.Key).ToList();
        CollectionAssert.AreEqual(new[] { "cpu", "ram", "ext:ext_smart:tab1", "gpu", "neteth0" }, tabKeys);

        // Test 2: Only GPU shown -> order starts with ext: ext, gpu, ...
        var configGpuOnly = new AppConfig
        {
            ShowCpu = false,
            ShowRam = false,
            ShowDisk = false,
            ShowGpu = true,
            ShowNetwork = false,
        };
        var vmGpuOnly = new WidgetViewModel(configGpuOnly);
        vmGpuOnly.UpdateOverall(snap);
        var sectionIdsGpuOnly = vmGpuOnly.Sections.Select(s => s.Id).ToList();
        CollectionAssert.AreEqual(new[] { "ext:ext_smart", "gpu" }, sectionIdsGpuOnly);

        vmGpuOnly.PushHistory(snap);
        var tabKeysGpuOnly = vmGpuOnly.Tabs.Select(t => t.Key).ToList();
        CollectionAssert.AreEqual(new[] { "ext:ext_smart:tab1", "gpu" }, tabKeysGpuOnly);

        // Test 3: place_after = "ram" in full view goes right after the RAM tab
        var extRamData = new ExtensionViewData(
            "ext_ram_target",
            "RAM Extension",
            extView,
            PlaceAfter: "ram"
        );
        var snapRam = new Snapshot
        {
            Ready = true,
            CpuInfo = new CpuInfo { Name = "Test CPU", Cores = 4, Logical = 8 },
            Cores = new[] { new Core { Usage = 10 } },
            Ram = new Ram { Usage = 50, UsedGb = 8, TotalGb = 16 },
            Disks = new[] { new Disk { Letter = "C", Usage = 30 } },
            Gpu = new Gpu { Present = true, Name = "Test GPU", Usage = 20 },
            Adapters = new[] { new Adapter { Id = "eth0", Name = "Ethernet", SpeedMbps = 1000 } },
            Extensions = new[] { extRamData }
        };
        var configAll = new AppConfig
        {
            ShowCpu = true,
            ShowRam = true,
            ShowDisk = true,
            ShowGpu = true,
            ShowNetwork = true,
        };
        var vmAll = new WidgetViewModel(configAll);
        vmAll.PushHistory(snapRam);
        var tabKeysAll = vmAll.Tabs.Select(t => t.Key).ToList();
        int ramIdx = tabKeysAll.IndexOf("ram");
        int extRamIdx = tabKeysAll.IndexOf("ext:ext_ram_target:tab1");
        Assert.IsTrue(ramIdx >= 0, "RAM tab must exist");
        Assert.AreEqual(ramIdx + 1, extRamIdx, "ext with place_after='ram' must go right after RAM tab");
    }

    [TestMethod]
    public async Task ExtensionHost_Fix3_FirstSetDemand_BeforeFirstViewRead_ViewSuppressedUntilDemandApplied()
    {
        DummyViewsExtension.Reset();

        using var setDemandStarted = new ManualResetEventSlim(false);
        using var unblockSetDemand = new ManualResetEventSlim(false);
        int callCount = 0;
        int getViewCallCount = 0;

        DummyViewsExtension.SetDemandAction = d =>
        {
            if (Interlocked.Increment(ref callCount) == 1)
            {
                setDemandStarted.Set();
                unblockSetDemand.Wait(TimeSpan.FromSeconds(5));
            }
        };

        DummyViewsExtension.ViewFunc = lang =>
        {
            Interlocked.Increment(ref getViewCallCount);
            return new ExtensionView(
                new[] { new WidgetPage("DUMMY", "OK", "Sample", 10, 20, Severity.Good) },
                new OverviewSection("Sec", new[] { new OverviewRow("R", "D", "V", 1, Severity.Good, 1) }),
                new[] { new DetailTab("t1", "Tab1", "S", "D", "H", Array.Empty<Fact>()) }
            );
        };

        string extDir = CreateExtensionWithManifest("demand_gate_view", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "views" };
            m.Entry.Type = typeof(DummyViewsExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["demand_gate_view"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);

        // StartAsync invokes PushDemand asynchronously
        var startTask = host.StartAsync();

        bool paused = setDemandStarted.Wait(TimeSpan.FromSeconds(5));
        Assert.IsTrue(paused, "PushDemand must pause inside DummyViewsExtension.SetDemandAction");

        // During the block, CollectViews returns no view for it and GetView was not called
        var viewsWhileBlocked = host.CollectViews("en");
        Assert.IsNotNull(viewsWhileBlocked);
        Assert.IsFalse(viewsWhileBlocked.Any(v => v.Id == "demand_gate_view"), "View must be suppressed while DemandApplied is false");
        Assert.AreEqual(0, getViewCallCount, "GetView must NOT have been called while DemandApplied is false");

        // Release the SetDemand action
        unblockSetDemand.Set();
        await startTask;

        // After release the view appears and GetView was called
        var viewsAfterRelease = host.CollectViews("en");
        Assert.IsNotNull(viewsAfterRelease);
        Assert.IsTrue(viewsAfterRelease.Any(v => v.Id == "demand_gate_view"), "View must appear after DemandApplied becomes true");
        Assert.IsTrue(getViewCallCount > 0, "GetView must be called after DemandApplied becomes true");
    }

    [TestMethod]
    public async Task ExtensionHost_Fix4_DemandGateBelongsToSlot_BlockedCallbackOnRetiredInstanceDoesNotStarveReplacement()
    {
        DummyViewsExtension.Reset();

        string extDir = CreateExtensionWithManifest("demand_gate_slot", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyViewsExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["demand_gate_slot"] = new() { Enabled = true }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);
        // The retirement wait elapses while the old callback is blocked; keep it short.
        host.ReloadRetireWait = TimeSpan.FromMilliseconds(300);
        await host.StartAsync();

        Assert.AreEqual(1, DummyViewsExtension.InitializeCount);
        Assert.AreEqual(1, DummyViewsExtension.SetDemandCount);

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int calls = 0;
        DummyViewsExtension.SetDemandAction = d =>
        {
            // Only the first call (on the old instance) blocks; the long bound is a safety net,
            // the test releases it at the very end.
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(60));
            }
        };

        Task blocked = Task.Run(() => host.SetDemand(diskTemperatureNeeded: true));
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)), "The demand callback on the old instance must block");

            // The old instance is retired and disposed anyway once the wait elapses.
            await host.Reload("demand_gate_slot");

            Assert.AreEqual(2, DummyViewsExtension.InitializeCount, "A new instance must be loaded");
            Assert.AreEqual(3, DummyViewsExtension.SetDemandCount,
                "The new instance must receive its demand although the old callback still blocks");
            Assert.AreEqual(ExtensionDemand.Views | ExtensionDemand.DiskTemperature, DummyViewsExtension.LastDemand);

            var views = host.CollectViews("en");
            Assert.IsTrue(views.Any(v => v.Id == "demand_gate_slot"),
                "DemandApplied must be true for the new instance so its view is collected");
        }
        finally
        {
            release.Set();
        }

        await blocked;
    }

    [TestMethod]
    public async Task ExtensionHost_Fix4_Reload_DllDeleted_PublishesFileMissingAndDropsSlot()
    {
        DummyDemandAndTempExtension.Reset();
        DummyDemandAndTempExtension.TemperaturesToReturn = new Dictionary<int, int> { [0] = 44 };

        string extDir = CreateExtensionWithManifest("reload_fail_missing", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyDemandAndTempExtension).FullName!;
        });

        // Switched off, but a first-party disk-temperature provider stays loaded.
        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["reload_fail_missing"] = new() { Enabled = false }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        Assert.AreEqual(ExtensionStatus.Loaded, host.Entries[0].Status);
        Assert.AreEqual(44, host.GetDiskTemperatures()[0]);

        File.Delete(Path.Combine(extDir, "SysMonitor.Tests.DummyExtension.dll"));
        await host.Reload("reload_fail_missing");

        Assert.AreEqual(1, host.Entries.Count);
        Assert.AreEqual(ExtensionStatus.FileMissing, host.Entries[0].Status, "Reload must publish the failure");
        Assert.AreEqual(0, host.GetDiskTemperatures().Count, "No slot may be left behind");
    }

    [TestMethod]
    public async Task ExtensionHost_Fix4_Reload_ManifestNoLongerFirstParty_PublishesNotApproved()
    {
        DummyDemandAndTempExtension.Reset();
        DummyDemandAndTempExtension.TemperaturesToReturn = new Dictionary<int, int> { [0] = 44 };

        string extDir = CreateExtensionWithManifest("reload_fail_untrusted", out string manifestHash, m =>
        {
            m.Contract = "1.1";
            m.Provides = new List<string> { "disk-temperature" };
            m.Entry.Type = typeof(DummyDemandAndTempExtension).FullName!;
        });

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["reload_fail_untrusted"] = new() { Enabled = false }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestHash })
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        Assert.AreEqual(ExtensionStatus.Loaded, host.Entries[0].Status);
        Assert.AreEqual(44, host.GetDiskTemperatures()[0]);

        // A changed manifest has another hash, so it is no longer in the first-party allowlist.
        File.AppendAllText(Path.Combine(extDir, "extension.json"), Environment.NewLine);
        await host.Reload("reload_fail_untrusted");

        Assert.AreEqual(1, host.Entries.Count);
        Assert.AreEqual(ExtensionStatus.NotApproved, host.Entries[0].Status, "Reload must publish the trust failure");
        Assert.AreEqual(0, host.GetDiskTemperatures().Count, "No slot may be left behind");
    }

    [TestMethod]
    public void ExtensionHost_Fix4_PlaceAfter_TargetsCollapsingOntoOneAnchor_SameOrderInOverviewAndTabs()
    {
        // Disk is hidden: extension a (after disk) and b (after ram) both fall back to the RAM anchor.
        // b targets ram, which comes before disk, so b goes first in both views.
        var config = new AppConfig
        {
            ShowCpu = true,
            ShowRam = true,
            ShowDisk = false,
            ShowGpu = true,
            ShowNetwork = false,
        };

        ExtensionView MakeView() => new(
            new[] { new WidgetPage("Ext", "Detail", "Note", 0, 0, Severity.Good) },
            new OverviewSection("Ext Note", new[] { new OverviewRow("R1", "D1", "V1", 50, Severity.Good, 50) }),
            new[] { new DetailTab("tab1", "Ext Tab", "Sum", "Det", "Hw", Array.Empty<Fact>()) });

        var extA = new ExtensionViewData("a", "A", MakeView(), PlaceAfter: "disk");
        var extB = new ExtensionViewData("b", "B", MakeView(), PlaceAfter: "ram");

        var snap = new Snapshot
        {
            Ready = true,
            CpuInfo = new CpuInfo { Name = "Test CPU", Cores = 4, Logical = 8 },
            Cores = new[] { new Core { Usage = 10 } },
            Ram = new Ram { Usage = 50, UsedGb = 8, TotalGb = 16 },
            Gpu = new Gpu { Present = true, Name = "Test GPU", Usage = 20 },
            Extensions = new[] { extA, extB }
        };

        var vm = new WidgetViewModel(config);
        vm.UpdateOverall(snap);
        CollectionAssert.AreEqual(
            new[] { "cpu", "ram", "ext:b", "ext:a", "gpu" },
            vm.Sections.Select(s => s.Id).ToList(),
            "Overview: b (target ram) before a (target disk)");

        vm.PushHistory(snap);
        CollectionAssert.AreEqual(
            new[] { "cpu", "ram", "ext:b:tab1", "ext:a:tab1", "gpu" },
            vm.Tabs.Select(t => t.Key).ToList(),
            "Tabs must use the same order as the overview");
    }

    private string CreateExtension(string id, out string manifestSha256) =>
        CreateExtensionWithManifest(id, out manifestSha256);

    private string CreateExtensionWithManifest(
        string id,
        out string manifestSha256,
        Action<ExtensionManifest>? configure = null,
        string? dllSource = null,
        string? dllName = null)
    {
        string extDir = Path.Combine(_tempDir, id);
        Directory.CreateDirectory(extDir);

        string fileName = dllName ?? "SysMonitor.Tests.DummyExtension.dll";
        string targetDll = Path.Combine(extDir, fileName);
        File.Copy(dllSource ?? _dummyDllSourcePath, targetDll, overwrite: true);

        byte[] dllBytes = File.ReadAllBytes(targetDll);
        string dllSha256 = ComputeSha256(dllBytes);

        var manifest = new ExtensionManifest
        {
            Id = id,
            Name = "Extension " + id,
            Version = "1.0.0",
            Contract = "1.0",
            Entry = new ExtensionEntry
            {
                Assembly = fileName,
                Type = typeof(DummyViewsExtension).FullName!
            },
            Files = new List<ExtensionFile>
            {
                new()
                {
                    Path = fileName,
                    Sha256 = dllSha256,
                    Native = false
                }
            }
        };

        configure?.Invoke(manifest);

        string manifestJson = JsonSerializer.Serialize(manifest, ExtensionManifest.JsonOptions);
        string manifestPath = Path.Combine(extDir, "extension.json");
        File.WriteAllText(manifestPath, manifestJson);

        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        manifestSha256 = ComputeSha256(manifestBytes);

        return extDir;
    }

    private static string ComputeSha256(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
