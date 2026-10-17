using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SysMonitor.Extensibility;
using SysMonitor.Extensions;
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

    private string CreateExtension(string id, out string manifestSha256)
    {
        string extDir = Path.Combine(_tempDir, id);
        Directory.CreateDirectory(extDir);

        string targetDll = Path.Combine(extDir, "SysMonitor.Tests.DummyExtension.dll");
        File.Copy(_dummyDllSourcePath, targetDll, overwrite: true);

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
                Assembly = "SysMonitor.Tests.DummyExtension.dll",
                Type = typeof(DummyViewsExtension).FullName!
            },
            Files = new List<ExtensionFile>
            {
                new()
                {
                    Path = "SysMonitor.Tests.DummyExtension.dll",
                    Sha256 = dllSha256,
                    Native = false
                }
            }
        };

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
