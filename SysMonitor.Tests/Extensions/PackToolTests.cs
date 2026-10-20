extern alias PackToolAsm;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SysMonitor.Ext.Sample;
using SysMonitor.Ext.Smart;
using SysMonitor.Extensibility;
using SysMonitor.Extensions;
using PackToolAsm::SysMonitor.PackTool;
using SysMonitor.Tests.DummyExtension;

namespace SysMonitor.Tests.Extensions;

[TestClass]
public class PackToolTests
{
    private string _tempDir = null!;
    private string _dummyDllPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SysMon_PackToolTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _dummyDllPath = ExtensionTestPackageBuilder.DummyDllSourcePath;
        Assert.IsTrue(File.Exists(_dummyDllPath), $"DummyExtension DLL not found at: {_dummyDllPath}");
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
                Directory.Exists(Path.Combine(dir, "SysMonitor.Ext.Smart")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find repository root from " + AppContext.BaseDirectory);
    }

    [TestMethod]
    public void Pack_FakeDllAndNativeFile_ProducesPackageLoadableByRealExtensionLoader()
    {
        string extsRoot = Path.Combine(_tempDir, "extensions");
        Directory.CreateDirectory(extsRoot);
        string outDir = Path.Combine(extsRoot, "fake_ext");

        string systemNative = Path.Combine(Environment.SystemDirectory, "shlwapi.dll");
        Assert.IsTrue(File.Exists(systemNative), "shlwapi.dll must exist in system directory");

        string templatePath = Path.Combine(_tempDir, "extension.template.json");
        string templateJson = $$"""
        {
          "id": "fake_ext",
          "name": "Fake Extension",
          "version": "$VERSION",
          "contract": "{{ContractInfo.Current.Major}}.{{ContractInfo.Current.Minor}}",
          "entry": {
            "assembly": "SysMonitor.Tests.DummyExtension.dll",
            "type": "{{typeof(DummyNormalExtension).FullName}}"
          },
          "files": [
            { "path": "SysMonitor.Tests.DummyExtension.dll" },
            { "path": "shlwapi.dll", "native": true }
          ]
        }
        """;
        File.WriteAllText(templatePath, templateJson);

        var packResult = ExtensionPacker.Pack(
            templatePath: templatePath,
            outDir: outDir,
            version: "1.0.0",
            fileSources: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SysMonitor.Tests.DummyExtension.dll"] = _dummyDllPath,
                ["shlwapi.dll"] = systemNative
            });

        Assert.AreEqual("fake_ext", packResult.Id);
        Assert.IsFalse(string.IsNullOrWhiteSpace(packResult.ManifestSha256));
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "extension.json")));

        // 1. Emitted manifest hash in FirstPartyAllowlist -> Loaded
        var allowlist = new FirstPartyAllowlist(new[] { packResult.ManifestSha256 });
        var loaderOptions = new ExtensionLoaderOptions
        {
            ExtensionsRoot = extsRoot,
            FirstParty = allowlist,
            HostIsElevated = false
        };

        var loadResult = ExtensionLoader.LoadFolder(outDir, loaderOptions);
        Assert.AreEqual(ExtensionStatus.Loaded, loadResult.Status, loadResult.Detail);
        Assert.IsNotNull(loadResult.LoadedExtension);
        loadResult.LoadedExtension?.Dispose();
        // The dummy extension writes marker files into its own folder when it runs; the folder
        // must be back to exactly the packed files before the next load.
        foreach (string leftover in Directory.GetFiles(outDir))
        {
            if (!packResult.WrittenFiles.Contains(Path.GetFileName(leftover), StringComparer.OrdinalIgnoreCase))
            {
                File.Delete(leftover);
            }
        }

        // 2. Emitted manifest hash NOT in FirstPartyAllowlist, non-elevated, no approval -> NotApproved
        var emptyAllowlistOptions = new ExtensionLoaderOptions
        {
            ExtensionsRoot = extsRoot,
            FirstParty = FirstPartyAllowlist.Empty,
            HostIsElevated = false
        };

        var notApprovedResult = ExtensionLoader.LoadFolder(outDir, emptyAllowlistOptions);
        Assert.AreEqual(ExtensionStatus.NotApproved, notApprovedResult.Status, notApprovedResult.Detail);

        // 3. Changing one byte of a file afterwards -> HashMismatch
        string dllTarget = Path.Combine(outDir, "SysMonitor.Tests.DummyExtension.dll");
        byte[] bytes = File.ReadAllBytes(dllTarget);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(dllTarget, bytes);

        var mismatchResult = ExtensionLoader.LoadFolder(outDir, loaderOptions);
        Assert.AreEqual(ExtensionStatus.HashMismatch, mismatchResult.Status);
    }

    [TestMethod]
    public void Pack_OptionalFile_Absent_OmittedFromManifestAndNotCopied()
    {
        string outDir = Path.Combine(_tempDir, "opt_absent");
        string templatePath = Path.Combine(_tempDir, "opt_absent.template.json");

        string templateJson = $$"""
        {
          "id": "opt_absent",
          "name": "Opt Absent",
          "version": "1.0.0",
          "contract": "{{ContractInfo.Current.Major}}.{{ContractInfo.Current.Minor}}",
          "entry": {
            "assembly": "SysMonitor.Tests.DummyExtension.dll",
            "type": "{{typeof(DummyNormalExtension).FullName}}"
          },
          "files": [
            { "path": "SysMonitor.Tests.DummyExtension.dll" },
            { "path": "optional.dll", "native": true, "optional": true }
          ]
        }
        """;
        File.WriteAllText(templatePath, templateJson);

        var result = ExtensionPacker.Pack(
            templatePath: templatePath,
            outDir: outDir,
            version: "1.0.0",
            fileSources: new Dictionary<string, string>
            {
                ["SysMonitor.Tests.DummyExtension.dll"] = _dummyDllPath
            });

        Assert.AreEqual(1, result.Manifest.Files.Count);
        Assert.IsFalse(File.Exists(Path.Combine(outDir, "optional.dll")));

        string manifestJson = File.ReadAllText(Path.Combine(outDir, "extension.json"));
        Assert.IsFalse(manifestJson.Contains("\"optional\"", StringComparison.OrdinalIgnoreCase),
            "'optional' property must never appear in generated extension.json");

        Assert.IsTrue(ExtensionManifest.TryParse(File.ReadAllBytes(Path.Combine(outDir, "extension.json")), out _, out _));
    }

    [TestMethod]
    public void Pack_OptionalFile_Present_IncludedInManifestAndCopied()
    {
        string outDir = Path.Combine(_tempDir, "opt_present");
        string templatePath = Path.Combine(_tempDir, "opt_present.template.json");
        string optSource = Path.Combine(_tempDir, "optional_source.dll");
        File.WriteAllText(optSource, "dummy native content");

        string templateJson = $$"""
        {
          "id": "opt_present",
          "name": "Opt Present",
          "version": "1.0.0",
          "contract": "{{ContractInfo.Current.Major}}.{{ContractInfo.Current.Minor}}",
          "entry": {
            "assembly": "SysMonitor.Tests.DummyExtension.dll",
            "type": "{{typeof(DummyNormalExtension).FullName}}"
          },
          "files": [
            { "path": "SysMonitor.Tests.DummyExtension.dll" },
            { "path": "optional.dll", "native": true, "optional": true }
          ]
        }
        """;
        File.WriteAllText(templatePath, templateJson);

        var result = ExtensionPacker.Pack(
            templatePath: templatePath,
            outDir: outDir,
            version: "1.0.0",
            fileSources: new Dictionary<string, string>
            {
                ["SysMonitor.Tests.DummyExtension.dll"] = _dummyDllPath,
                ["optional.dll"] = optSource
            });

        Assert.AreEqual(2, result.Manifest.Files.Count);
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "optional.dll")));

        string manifestJson = File.ReadAllText(Path.Combine(outDir, "extension.json"));
        Assert.IsFalse(manifestJson.Contains("\"optional\"", StringComparison.OrdinalIgnoreCase),
            "the tool-only 'optional' key must never reach the manifest");
    }

    [TestMethod]
    public void Pack_NonOptionalFile_Absent_ThrowsNamingFile()
    {
        string outDir = Path.Combine(_tempDir, "req_absent");
        string templatePath = Path.Combine(_tempDir, "req_absent.template.json");

        string templateJson = $$"""
        {
          "id": "req_absent",
          "name": "Req Absent",
          "version": "1.0.0",
          "contract": "{{ContractInfo.Current.Major}}.{{ContractInfo.Current.Minor}}",
          "entry": {
            "assembly": "SysMonitor.Tests.DummyExtension.dll",
            "type": "{{typeof(DummyNormalExtension).FullName}}"
          },
          "files": [
            { "path": "SysMonitor.Tests.DummyExtension.dll" },
            { "path": "missing_required.dll" }
          ]
        }
        """;
        File.WriteAllText(templatePath, templateJson);

        var ex = Assert.ThrowsException<PackException>(() =>
        {
            ExtensionPacker.Pack(
                templatePath: templatePath,
                outDir: outDir,
                version: "1.0.0",
                fileSources: new Dictionary<string, string>
                {
                    ["SysMonitor.Tests.DummyExtension.dll"] = _dummyDllPath
                });
        });

        StringAssert.Contains(ex.Message, "missing_required.dll");
    }

    [TestMethod]
    public void Pack_SourcePathDoesNotExist_ThrowsNamingFile()
    {
        string outDir = Path.Combine(_tempDir, "nonexistent_source");
        string templatePath = Path.Combine(_tempDir, "nonexistent_source.template.json");

        string templateJson = $$"""
        {
          "id": "nonexistent_source",
          "name": "Nonexistent Source",
          "version": "1.0.0",
          "contract": "{{ContractInfo.Current.Major}}.{{ContractInfo.Current.Minor}}",
          "entry": {
            "assembly": "target_file.dll",
            "type": "{{typeof(DummyNormalExtension).FullName}}"
          },
          "files": [
            { "path": "target_file.dll" }
          ]
        }
        """;
        File.WriteAllText(templatePath, templateJson);

        var ex = Assert.ThrowsException<PackException>(() =>
        {
            ExtensionPacker.Pack(
                templatePath: templatePath,
                outDir: outDir,
                version: "1.0.0",
                fileSources: new Dictionary<string, string>
                {
                    ["target_file.dll"] = Path.Combine(_tempDir, "nonexistent_file_on_disk.dll")
                });
        });

        StringAssert.Contains(ex.Message, "target_file.dll");
    }

    [TestMethod]
    public void Pack_UnknownFileNameInSources_ThrowsNamingFile()
    {
        string outDir = Path.Combine(_tempDir, "unknown_source");
        string templatePath = Path.Combine(_tempDir, "unknown_source.template.json");

        string templateJson = $$"""
        {
          "id": "unknown_source",
          "name": "Unknown Source",
          "version": "1.0.0",
          "contract": "{{ContractInfo.Current.Major}}.{{ContractInfo.Current.Minor}}",
          "entry": {
            "assembly": "SysMonitor.Tests.DummyExtension.dll",
            "type": "{{typeof(DummyNormalExtension).FullName}}"
          },
          "files": [
            { "path": "SysMonitor.Tests.DummyExtension.dll" }
          ]
        }
        """;
        File.WriteAllText(templatePath, templateJson);

        var ex = Assert.ThrowsException<PackException>(() =>
        {
            ExtensionPacker.Pack(
                templatePath: templatePath,
                outDir: outDir,
                version: "1.0.0",
                fileSources: new Dictionary<string, string>
                {
                    ["SysMonitor.Tests.DummyExtension.dll"] = _dummyDllPath,
                    ["rogue_file.dll"] = _dummyDllPath
                });
        });

        StringAssert.Contains(ex.Message, "rogue_file.dll");
    }

    [TestMethod]
    public void Pack_NonEmptyOutDir_Throws()
    {
        string outDir = Path.Combine(_tempDir, "non_empty_out");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "stale_file.txt"), "stale");

        string templatePath = Path.Combine(_tempDir, "non_empty.template.json");
        string templateJson = $$"""
        {
          "id": "non_empty",
          "name": "Non Empty",
          "version": "1.0.0",
          "contract": "{{ContractInfo.Current.Major}}.{{ContractInfo.Current.Minor}}",
          "entry": {
            "assembly": "SysMonitor.Tests.DummyExtension.dll",
            "type": "{{typeof(DummyNormalExtension).FullName}}"
          },
          "files": [
            { "path": "SysMonitor.Tests.DummyExtension.dll" }
          ]
        }
        """;
        File.WriteAllText(templatePath, templateJson);

        var ex = Assert.ThrowsException<PackException>(() =>
        {
            ExtensionPacker.Pack(
                templatePath: templatePath,
                outDir: outDir,
                version: "1.0.0",
                fileSources: new Dictionary<string, string>
                {
                    ["SysMonitor.Tests.DummyExtension.dll"] = _dummyDllPath
                });
        });

        StringAssert.Contains(ex.Message, "not empty");
    }

    [TestMethod]
    public void Pack_VersionReplacement_ReplacesDollarVersion()
    {
        string outDir = Path.Combine(_tempDir, "ver_replace");
        string templatePath = Path.Combine(_tempDir, "ver_replace.template.json");

        string templateJson = $$"""
        {
          "id": "ver_replace",
          "name": "Ver Replace",
          "version": "$VERSION",
          "contract": "{{ContractInfo.Current.Major}}.{{ContractInfo.Current.Minor}}",
          "entry": {
            "assembly": "SysMonitor.Tests.DummyExtension.dll",
            "type": "{{typeof(DummyNormalExtension).FullName}}"
          },
          "files": [
            { "path": "SysMonitor.Tests.DummyExtension.dll" }
          ]
        }
        """;
        File.WriteAllText(templatePath, templateJson);

        var result = ExtensionPacker.Pack(
            templatePath: templatePath,
            outDir: outDir,
            version: "9.8.7",
            fileSources: new Dictionary<string, string>
            {
                ["SysMonitor.Tests.DummyExtension.dll"] = _dummyDllPath
            });

        Assert.AreEqual("9.8.7", result.Manifest.Version);
        byte[] bytes = File.ReadAllBytes(Path.Combine(outDir, "extension.json"));
        Assert.IsTrue(ExtensionManifest.TryParse(bytes, out var manifest, out _));
        Assert.AreEqual("9.8.7", manifest?.Version);
    }

    [TestMethod]
    public void Pack_InvalidTemplates_Rejected()
    {
        void AssertInvalidTemplate(string jsonSnippet, string expectedSnippet)
        {
            string outDir = Path.Combine(_tempDir, "inv_" + Guid.NewGuid().ToString("N"));
            string templatePath = Path.Combine(_tempDir, "inv_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(templatePath, jsonSnippet);

            var ex = Assert.ThrowsException<PackException>(() =>
            {
                ExtensionPacker.Pack(
                    templatePath: templatePath,
                    outDir: outDir,
                    version: "1.0.0",
                    fileSources: new Dictionary<string, string>
                    {
                        ["SysMonitor.Tests.DummyExtension.dll"] = _dummyDllPath
                    });
            });

            StringAssert.Contains(ex.Message.ToLowerInvariant(), expectedSnippet.ToLowerInvariant());
        }

        // Bad id
        AssertInvalidTemplate($$"""
        {
          "id": "INVALID_UPPERCASE",
          "name": "Bad ID",
          "version": "1.0.0",
          "contract": "{{ContractInfo.Current.Major}}.{{ContractInfo.Current.Minor}}",
          "entry": { "assembly": "SysMonitor.Tests.DummyExtension.dll", "type": "T" },
          "files": [{ "path": "SysMonitor.Tests.DummyExtension.dll" }]
        }
        """, "id");

        // Bad contract major
        AssertInvalidTemplate($$"""
        {
          "id": "bad_contract_major",
          "name": "Bad Major",
          "version": "1.0.0",
          "contract": "{{ContractInfo.Current.Major + 1}}.0",
          "entry": { "assembly": "SysMonitor.Tests.DummyExtension.dll", "type": "T" },
          "files": [{ "path": "SysMonitor.Tests.DummyExtension.dll" }]
        }
        """, "incompatible");

        // Contract minor above current
        AssertInvalidTemplate($$"""
        {
          "id": "bad_contract_minor",
          "name": "Bad Minor",
          "version": "1.0.0",
          "contract": "{{ContractInfo.Current.Major}}.{{ContractInfo.Current.Minor + 5}}",
          "entry": { "assembly": "SysMonitor.Tests.DummyExtension.dll", "type": "T" },
          "files": [{ "path": "SysMonitor.Tests.DummyExtension.dll" }]
        }
        """, "incompatible");
    }

    private string TemplateWith(string filesJson, string entryAssembly = "SysMonitor.Tests.DummyExtension.dll")
    {
        string path = Path.Combine(_tempDir, "tpl_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, $$"""
        {
          "id": "rules_ext",
          "name": "Rules",
          "version": "1.0.0",
          "contract": "{{ContractInfo.Current.Major}}.{{ContractInfo.Current.Minor}}",
          "entry": { "assembly": "{{entryAssembly}}", "type": "T" },
          "files": {{filesJson}}
        }
        """);
        return path;
    }

    private PackException PackExpectingFailure(string templatePath, string? allowlist = null, string? outDir = null,
        Dictionary<string, string>? sources = null)
    {
        return Assert.ThrowsException<PackException>(() => ExtensionPacker.Pack(
            templatePath: templatePath,
            outDir: outDir ?? Path.Combine(_tempDir, "out_" + Guid.NewGuid().ToString("N")),
            version: "1.0.0",
            fileSources: sources ?? new Dictionary<string, string>
            {
                ["SysMonitor.Tests.DummyExtension.dll"] = _dummyDllPath,
                ["extension.json"] = _dummyDllPath,
            },
            allowlistPath: allowlist));
    }

    [TestMethod]
    public void Pack_TemplateListingExtensionJson_IsRejected()
    {
        string tpl = TemplateWith("""[{ "path": "SysMonitor.Tests.DummyExtension.dll" }, { "path": "Extension.JSON" }]""");
        StringAssert.Contains(PackExpectingFailure(tpl).Message, "extension.json");
    }

    [TestMethod]
    public void Pack_PathsWindowsRewritesOrTheLoaderForbids_AreRejected()
    {
        foreach (string bad in new[] { "x.dll.", "x.dll ", "a:b.dll", "sub/../x.dll", "/x.dll" })
        {
            string tpl = TemplateWith($$"""[{ "path": "SysMonitor.Tests.DummyExtension.dll" }, { "path": "{{bad}}" }]""");
            PackExpectingFailure(tpl);
        }
    }

    [TestMethod]
    public void Pack_EntryAssemblyMustBeListedManagedAndRequired()
    {
        // Not listed at all: the loader would answer EntryNotFound.
        PackExpectingFailure(TemplateWith("""[{ "path": "other.dll" }]"""));
        // Listed but native.
        PackExpectingFailure(TemplateWith("""[{ "path": "SysMonitor.Tests.DummyExtension.dll", "native": true }]"""));
        // Listed but optional: a build without it would produce an extension that cannot start.
        PackExpectingFailure(TemplateWith("""[{ "path": "SysMonitor.Tests.DummyExtension.dll", "optional": true }]"""));
    }

    [TestMethod]
    public void Pack_AllowlistInsideThePackageFolder_IsRejectedBeforeAnythingIsWritten()
    {
        string tpl = TemplateWith("""[{ "path": "SysMonitor.Tests.DummyExtension.dll" }]""");
        string outDir = Path.Combine(_tempDir, "pkg");
        PackExpectingFailure(tpl, allowlist: Path.Combine(outDir, "allow.json"), outDir: outDir);
        Assert.IsFalse(Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any(),
            "nothing may be left behind by a rejected pack");
    }

    [TestMethod]
    public void Allowlist_EntryThatIsNotHex_IsRejected()
    {
        string allow = Path.Combine(_tempDir, "allow.json");
        File.WriteAllText(allow, "[\"" + new string('z', 64) + "\"]");
        string tpl = TemplateWith("""[{ "path": "SysMonitor.Tests.DummyExtension.dll" }]""");
        PackExpectingFailure(tpl, allowlist: allow,
            sources: new Dictionary<string, string> { ["SysMonitor.Tests.DummyExtension.dll"] = _dummyDllPath });
    }

    [TestMethod]
    public void Allowlist_CreatedWhenMissing_Merged_Sorted_Idempotent_MalformedRejected()
    {
        string allowlistPath = Path.Combine(_tempDir, "allowlist.json");
        string hashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        string hashB = "1111111111111111111111111111111111111111111111111111111111111111";

        // 1. Created when missing
        Assert.IsFalse(File.Exists(allowlistPath));
        ExtensionPacker.UpdateAllowlist(allowlistPath, hashA);
        Assert.IsTrue(File.Exists(allowlistPath));

        string json1 = File.ReadAllText(allowlistPath);
        var list1 = JsonSerializer.Deserialize<List<string>>(json1);
        Assert.IsNotNull(list1);
        Assert.AreEqual(1, list1.Count);
        Assert.AreEqual(hashA, list1[0]);

        // 2. Merged and sorted ordinal
        ExtensionPacker.UpdateAllowlist(allowlistPath, hashB);
        string json2 = File.ReadAllText(allowlistPath);
        var list2 = JsonSerializer.Deserialize<List<string>>(json2);
        Assert.IsNotNull(list2);
        Assert.AreEqual(2, list2.Count);
        Assert.AreEqual(hashB, list2[0]); // '1' precedes 'a'
        Assert.AreEqual(hashA, list2[1]);

        // 3. Idempotent
        ExtensionPacker.UpdateAllowlist(allowlistPath, hashA);
        string json3 = File.ReadAllText(allowlistPath);
        var list3 = JsonSerializer.Deserialize<List<string>>(json3);
        Assert.IsNotNull(list3);
        Assert.AreEqual(2, list3.Count);
        Assert.AreEqual(json2, json3);

        // 4. Accepted by FirstPartyAllowlist
        var firstParty = new FirstPartyAllowlist(list3);
        Assert.IsTrue(firstParty.IsFirstParty(hashA));
        Assert.IsTrue(firstParty.IsFirstParty(hashB));
        Assert.IsFalse(firstParty.IsFirstParty("cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"));

        // 5. Malformed file rejected
        File.WriteAllText(allowlistPath, "{ not a valid list");
        var ex = Assert.ThrowsException<PackException>(() =>
        {
            ExtensionPacker.UpdateAllowlist(allowlistPath, hashA);
        });
        StringAssert.Contains(ex.Message.ToLowerInvariant(), "malformed");
    }

    [TestMethod]
    public void TemplateParity_SmartAndSampleTemplates_PassValidation_ContractAndTypesMatch()
    {
        string repoRoot = FindRepoRoot();

        // 1. SMART template
        string smartTemplatePath = Path.Combine(repoRoot, "SysMonitor.Ext.Smart", "extension.template.json");
        Assert.IsTrue(File.Exists(smartTemplatePath), $"Smart template not found at: {smartTemplatePath}");

        string smartJson = File.ReadAllText(smartTemplatePath);
        var smartTemplate = JsonSerializer.Deserialize<ExtensionTemplate>(smartJson, ExtensionManifest.JsonOptions);
        Assert.IsNotNull(smartTemplate);

        Assert.AreEqual("smart", smartTemplate.Id);
        Assert.AreEqual("SMART", smartTemplate.Name);
        Assert.AreEqual("$VERSION", smartTemplate.Version);
        Assert.AreEqual($"{ContractInfo.Current.Major}.{ContractInfo.Current.Minor}", smartTemplate.Contract);
        Assert.AreEqual("4.2.0", smartTemplate.MinHost);
        Assert.AreEqual("SysMonitor.Ext.Smart.dll", smartTemplate.Entry.Assembly);
        Assert.AreEqual("SysMonitor.Ext.Smart.SmartExtension", smartTemplate.Entry.Type);
        Assert.AreEqual("disk", smartTemplate.PlaceAfter);
        Assert.IsNotNull(smartTemplate.Provides);
        CollectionAssert.Contains(smartTemplate.Provides, "disk-temperature");

        Type? smartType = typeof(SmartExtension).Assembly.GetType(smartTemplate.Entry.Type);
        Assert.IsNotNull(smartType, $"Smart entry type '{smartTemplate.Entry.Type}' could not be resolved");
        Assert.IsTrue(typeof(IExtension).IsAssignableFrom(smartType));

        // Test pack validation with real built dll
        string smartOut = Path.Combine(_tempDir, "smart_pack");
        var smartPack = ExtensionPacker.Pack(
            templatePath: smartTemplatePath,
            outDir: smartOut,
            version: "4.2.0",
            fileSources: new Dictionary<string, string>
            {
                ["SysMonitor.Ext.Smart.dll"] = typeof(SmartExtension).Assembly.Location
            });
        Assert.AreEqual("smart", smartPack.Id);
        Assert.AreEqual(1, smartPack.Manifest.Files.Count);

        // 2. SAMPLE template
        string sampleTemplatePath = Path.Combine(repoRoot, "SysMonitor.Ext.Sample", "extension.template.json");
        Assert.IsTrue(File.Exists(sampleTemplatePath), $"Sample template not found at: {sampleTemplatePath}");

        string sampleJson = File.ReadAllText(sampleTemplatePath);
        var sampleTemplate = JsonSerializer.Deserialize<ExtensionTemplate>(sampleJson, ExtensionManifest.JsonOptions);
        Assert.IsNotNull(sampleTemplate);

        Assert.AreEqual("sample", sampleTemplate.Id);
        Assert.AreEqual("Sample extension", sampleTemplate.Name);
        Assert.AreEqual("$VERSION", sampleTemplate.Version);
        Assert.AreEqual($"{ContractInfo.Current.Major}.{ContractInfo.Current.Minor}", sampleTemplate.Contract);
        Assert.AreEqual("4.2.0", sampleTemplate.MinHost);
        Assert.AreEqual("SysMonitor.Ext.Sample.dll", sampleTemplate.Entry.Assembly);
        Assert.AreEqual("SysMonitor.Ext.Sample.SampleExtension", sampleTemplate.Entry.Type);
        Assert.IsNull(sampleTemplate.PlaceAfter);

        Type? sampleType = typeof(SampleExtension).Assembly.GetType(sampleTemplate.Entry.Type);
        Assert.IsNotNull(sampleType, $"Sample entry type '{sampleTemplate.Entry.Type}' could not be resolved");
        Assert.IsTrue(typeof(IExtension).IsAssignableFrom(sampleType));
        Assert.IsTrue(typeof(IViewProvider).IsAssignableFrom(sampleType));

        // Test pack validation with real built dll
        string sampleOut = Path.Combine(_tempDir, "sample_pack");
        var samplePack = ExtensionPacker.Pack(
            templatePath: sampleTemplatePath,
            outDir: sampleOut,
            version: "4.2.0",
            fileSources: new Dictionary<string, string>
            {
                ["SysMonitor.Ext.Sample.dll"] = typeof(SampleExtension).Assembly.Location
            });
        Assert.AreEqual("sample", samplePack.Id);
        Assert.AreEqual(1, samplePack.Manifest.Files.Count);
    }

    [TestMethod]
    public void Program_Main_CommandLineParsingAndExitCodes()
    {
        // 1. Missing args -> exit code 2 (usage)
        int codeEmpty = Program.Main(Array.Empty<string>());
        Assert.AreEqual(2, codeEmpty);

        // 2. Help -> exit code 2
        int codeHelp = Program.Main(new[] { "--help" });
        Assert.AreEqual(2, codeHelp);

        // 3. Missing required option -> exit code 2
        int codeMissing = Program.Main(new[] { "--template", "foo.json" });
        Assert.AreEqual(2, codeMissing);

        // 4. Invalid template file -> exit code 1
        int codeInvalid = Program.Main(new[]
        {
            "--template", Path.Combine(_tempDir, "does_not_exist.json"),
            "--out", Path.Combine(_tempDir, "out"),
            "--version", "1.0.0"
        });
        Assert.AreEqual(1, codeInvalid);

        // 5. Successful pack -> exit code 0
        string tPath = Path.Combine(_tempDir, "cli_success.template.json");
        string outDir = Path.Combine(_tempDir, "cli_success_out");
        File.WriteAllText(tPath, $$"""
        {
          "id": "cli_succ",
          "name": "CLI Succ",
          "version": "1.0.0",
          "contract": "{{ContractInfo.Current.Major}}.{{ContractInfo.Current.Minor}}",
          "entry": {
            "assembly": "SysMonitor.Tests.DummyExtension.dll",
            "type": "{{typeof(DummyNormalExtension).FullName}}"
          },
          "files": [
            { "path": "SysMonitor.Tests.DummyExtension.dll" }
          ]
        }
        """);

        int codeSucc = Program.Main(new[]
        {
            "--template", tPath,
            "--out", outDir,
            "--version", "1.0.0",
            "--file", $"SysMonitor.Tests.DummyExtension.dll={_dummyDllPath}"
        });
        Assert.AreEqual(0, codeSucc);
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "extension.json")));
    }
}
