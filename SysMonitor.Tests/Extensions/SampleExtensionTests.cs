extern alias PackToolAsm;
using System.IO;
using SysMonitor.Ext.Sample;
using SysMonitor.Extensibility;
using SysMonitor.Extensions;
using SysMonitor.Model;
using PackToolAsm::SysMonitor.PackTool;

namespace SysMonitor.Tests.Extensions;

[TestClass]
public class SampleExtensionTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SysMon_SampleExtTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
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

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "build-wpf.cmd")) ||
                Directory.Exists(Path.Combine(dir, "SysMonitor.Ext.Sample")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find repository root from " + AppContext.BaseDirectory);
    }

    [TestMethod]
    public void SampleStrings_KeyParity_ThaiAndEnglishMatch()
    {
        var missing = SampleStrings.MissingKeys();
        Assert.AreEqual(0, missing.Count, "Sample strings dictionary has missing keys: " + string.Join(", ", missing));
    }

    [TestMethod]
    public void SampleExtension_DisplayNames_MatchLanguage()
    {
        var sample = new SampleExtension();
        Assert.AreEqual("Sample", sample.GetDisplayName("en"));
        Assert.AreEqual("ตัวอย่าง", sample.GetDisplayName("th"));
    }

    [TestMethod]
    public void SampleExtension_DirectGetView_ProducesAllSections()
    {
        var sample = new SampleExtension();

        var thView = sample.GetView("th");
        Assert.IsNotNull(thView);
        Assert.AreEqual(1, thView.WidgetPages.Count);
        Assert.AreEqual("ตัวอย่าง", thView.WidgetPages[0].Title);
        Assert.IsNotNull(thView.Overview);
        Assert.AreEqual(1, thView.Overview.Rows.Count);
        Assert.AreEqual(1, thView.Tabs.Count);
        Assert.AreEqual("ตัวอย่าง", thView.Tabs[0].Title);

        var enView = sample.GetView("en");
        Assert.IsNotNull(enView);
        Assert.AreEqual(1, enView.WidgetPages.Count);
        Assert.AreEqual("Sample", enView.WidgetPages[0].Title);
        Assert.IsNotNull(enView.Overview);
        Assert.AreEqual(1, enView.Overview.Rows.Count);
        Assert.AreEqual(1, enView.Tabs.Count);
        Assert.AreEqual("Sample", enView.Tabs[0].Title);
    }

    [TestMethod]
    public async Task SampleExtension_PackedWithRealDll_LoadsViaExtensionHostWithApproval_AndRejectsWithout()
    {
        string repoRoot = FindRepoRoot();
        string templatePath = Path.Combine(repoRoot, "SysMonitor.Ext.Sample", "extension.template.json");
        Assert.IsTrue(File.Exists(templatePath), $"Sample template not found at {templatePath}");

        string extsRoot = Path.Combine(_tempDir, "extensions");
        Directory.CreateDirectory(extsRoot);
        string sampleExtDir = Path.Combine(extsRoot, "sample");

        string sampleDllPath = typeof(SampleExtension).Assembly.Location;
        Assert.IsTrue(File.Exists(sampleDllPath), $"Sample DLL not found at {sampleDllPath}");

        // Pack the real sample extension
        var packResult = ExtensionPacker.Pack(
            templatePath: templatePath,
            outDir: sampleExtDir,
            version: "4.2.0",
            fileSources: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SysMonitor.Ext.Sample.dll"] = sampleDllPath
            });

        Assert.AreEqual("sample", packResult.Id);
        Assert.IsFalse(string.IsNullOrWhiteSpace(packResult.ManifestSha256));

        string approvalStorePath = Path.Combine(_tempDir, "approved-extensions.json");
        var store = new ApprovalStore(
            approvalStorePath,
            aclChecker: _ => true,
            log: null,
            secureDirectory: _ => { },
            secureFile: _ => { });

        var loaderOptions = new ExtensionLoaderOptions
        {
            ExtensionsRoot = extsRoot,
            HostVersion = new Version(4, 2, 0),
            FirstParty = FirstPartyAllowlist.Empty,
            Approvals = store,
            HostIsElevated = false
        };

        // 1. Without approval -> NotApproved, 0 views
        using (var hostWithoutApproval = new ExtensionHost(new AppConfig(), loaderOptions))
        {
            await hostWithoutApproval.StartAsync();

            Assert.AreEqual(1, hostWithoutApproval.Entries.Count);
            var entry = hostWithoutApproval.Entries[0];
            Assert.AreEqual("sample", entry.Id);
            Assert.AreEqual(ExtensionStatus.NotApproved, entry.Status);

            var views = hostWithoutApproval.CollectViews("en");
            Assert.AreEqual(0, views.Count, "Unapproved extension must not produce views");
        }

        // 2. Approve extension in store
        var appResult = store.Approve("sample", "Sample extension", packResult.ManifestSha256);
        Assert.IsTrue(appResult.Success, $"Approval failed: {appResult.Error}");
        Assert.IsTrue(store.IsApproved("sample", packResult.ManifestSha256));

        // 3. With approval -> Loaded, CollectViews returns views in requested languages
        // An extension absent from the config is off until the user switches it on.
        var enabledConfig = new AppConfig();
        enabledConfig.Extensions["sample"] = new ExtensionSetting { Enabled = true };
        using (var hostWithApproval = new ExtensionHost(enabledConfig, loaderOptions))
        {
            await hostWithApproval.StartAsync();

            Assert.AreEqual(1, hostWithApproval.Entries.Count);
            var entry = hostWithApproval.Entries[0];
            Assert.AreEqual("sample", entry.Id);
            Assert.AreEqual(ExtensionStatus.Loaded, entry.Status);

            // Thai view
            var thViews = hostWithApproval.CollectViews("th");
            Assert.AreEqual(1, thViews.Count);
            Assert.AreEqual("sample", thViews[0].Id);
            Assert.AreEqual("ตัวอย่าง", thViews[0].DisplayName);
            Assert.AreEqual(1, thViews[0].View.WidgetPages.Count);
            Assert.IsNotNull(thViews[0].View.Overview);
            Assert.AreEqual(1, thViews[0].View.Tabs.Count);

            // English view
            var enViews = hostWithApproval.CollectViews("en");
            Assert.AreEqual(1, enViews.Count);
            Assert.AreEqual("sample", enViews[0].Id);
            Assert.AreEqual("Sample", enViews[0].DisplayName);
            Assert.AreEqual(1, enViews[0].View.WidgetPages.Count);
            Assert.IsNotNull(enViews[0].View.Overview);
            Assert.AreEqual(1, enViews[0].View.Tabs.Count);
        }
    }
}
