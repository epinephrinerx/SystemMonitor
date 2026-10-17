using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SysMonitor.Extensibility;
using SysMonitor.Extensions;
using SysMonitor.Tests.DummyExtension;

namespace SysMonitor.Tests.Extensions;

[TestClass]
public class ExtensionApprovalTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SysMon_ExtApprTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        DummyNormalExtension.Initialized = false;
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

    private (ApprovalStore Store, string StorePath) CreateTempStore(Func<string, bool>? aclChecker = null)
    {
        string storePath = Path.Combine(_tempDir, "approved-extensions.json");
        var store = new ApprovalStore(
            storePath,
            aclChecker: aclChecker ?? (_ => true),
            log: _ => { },
            secureDirectory: _ => { },
            secureFile: _ => { });
        return (store, storePath);
    }

    // 1. TryParse accepts a valid id/hash (and lowercases the hash); rejects invalid forms
    [TestMethod]
    public void TryParse_ValidArguments_AcceptsAndLowercasesHash()
    {
        const string validId = "my.cool_ext-1";
        const string upperHash = "A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2";
        string[] args = ["--some-other-flag", ExtensionApproval.Switch, validId, upperHash];

        bool ok = ExtensionApproval.TryParse(args, out string id, out string sha);

        Assert.IsTrue(ok);
        Assert.AreEqual(validId, id);
        Assert.AreEqual(upperHash.ToLowerInvariant(), sha);
    }

    [TestMethod]
    public void TryParse_InvalidArguments_RejectsAll()
    {
        const string validSha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        // No switch
        Assert.IsFalse(ExtensionApproval.TryParse(["foo", "bar"], out _, out _));

        // Switch not followed by exactly 2 args
        Assert.IsFalse(ExtensionApproval.TryParse([ExtensionApproval.Switch], out _, out _));
        Assert.IsFalse(ExtensionApproval.TryParse([ExtensionApproval.Switch, "ext1"], out _, out _));
        Assert.IsFalse(ExtensionApproval.TryParse([ExtensionApproval.Switch, "ext1", validSha, "extra"], out _, out _));

        // Invalid IDs
        Assert.IsFalse(ExtensionApproval.TryParse([ExtensionApproval.Switch, "..", validSha], out _, out _));
        Assert.IsFalse(ExtensionApproval.TryParse([ExtensionApproval.Switch, ".", validSha], out _, out _));
        Assert.IsFalse(ExtensionApproval.TryParse([ExtensionApproval.Switch, "ext/id", validSha], out _, out _));
        Assert.IsFalse(ExtensionApproval.TryParse([ExtensionApproval.Switch, "ext\\id", validSha], out _, out _));
        Assert.IsFalse(ExtensionApproval.TryParse([ExtensionApproval.Switch, "ext id", validSha], out _, out _));
        Assert.IsFalse(ExtensionApproval.TryParse([ExtensionApproval.Switch, "ExtId", validSha], out _, out _));
        Assert.IsFalse(ExtensionApproval.TryParse([ExtensionApproval.Switch, "", validSha], out _, out _));
        Assert.IsFalse(ExtensionApproval.TryParse([ExtensionApproval.Switch, new string('a', 65), validSha], out _, out _));

        // Invalid SHA
        Assert.IsFalse(ExtensionApproval.TryParse([ExtensionApproval.Switch, "ext1", new string('a', 63)], out _, out _));
        Assert.IsFalse(ExtensionApproval.TryParse([ExtensionApproval.Switch, "ext1", new string('a', 65)], out _, out _));
        Assert.IsFalse(ExtensionApproval.TryParse([ExtensionApproval.Switch, "ext1", new string('g', 64)], out _, out _));
    }

    // 2. CreateStartInfo: verb runas, UseShellExecute, arguments exactly --approve-extension <id> <sha>; invalid values throw
    [TestMethod]
    public void CreateStartInfo_ValidArguments_ConfiguredCorrectly()
    {
        string exe = Path.Combine(_tempDir, "SystemMonitor.exe");
        const string id = "ext.one";
        const string shaUpper = "A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2";

        var startInfo = ExtensionApproval.CreateStartInfo(exe, id, shaUpper);

        Assert.AreEqual(exe, startInfo.FileName);
        Assert.IsTrue(startInfo.UseShellExecute);
        Assert.AreEqual("runas", startInfo.Verb);
        Assert.AreEqual(_tempDir, startInfo.WorkingDirectory);
        Assert.AreEqual($"--approve-extension ext.one {shaUpper.ToLowerInvariant()}", startInfo.Arguments);
    }

    [TestMethod]
    public void CreateStartInfo_InvalidArguments_ThrowsArgumentException()
    {
        string exe = Path.Combine(_tempDir, "SystemMonitor.exe");
        const string validSha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        Assert.ThrowsException<ArgumentException>(() => ExtensionApproval.CreateStartInfo(exe, "..", validSha));
        Assert.ThrowsException<ArgumentException>(() => ExtensionApproval.CreateStartInfo(exe, "UPPER", validSha));
        Assert.ThrowsException<ArgumentException>(() => ExtensionApproval.CreateStartInfo(exe, "id", "short"));
        Assert.ThrowsException<ArgumentException>(() => ExtensionApproval.CreateStartInfo("", "id", validSha));
    }

    // 3. Run not elevated -> ExitNotElevated; Confirm not called; store file not created
    [TestMethod]
    public void Run_NotElevated_ReturnsExitNotElevated_ConfirmNotCalled_StoreNotCreated()
    {
        ExtensionTestPackageBuilder.CreateExtensionDirectory(_tempDir, "ext1", out string sha);
        var (store, storePath) = CreateTempStore();

        bool confirmCalled = false;
        var env = new ApprovalEnvironment
        {
            IsElevated = () => false,
            ExtensionsRoot = _tempDir,
            Store = store,
            FirstParty = FirstPartyAllowlist.Empty,
            Confirm = _ => { confirmCalled = true; return true; },
            Log = _ => { },
            HostVersion = new Version(4, 2, 0)
        };

        int exitCode = ExtensionApproval.Run([ExtensionApproval.Switch, "ext1", sha], env);

        Assert.AreEqual(ExtensionApproval.ExitNotElevated, exitCode);
        Assert.IsFalse(confirmCalled);
        Assert.IsFalse(File.Exists(storePath));
    }

    // 4. Run elevated, valid unapproved package, Confirm returns true -> ExitApproved; store.IsApproved true; ApprovalPrompt matches
    [TestMethod]
    public void Run_Elevated_ValidPackage_ConfirmTrue_ReturnsExitApproved_StoreApproved()
    {
        ExtensionTestPackageBuilder.CreateExtensionDirectory(
            _tempDir,
            "ext1",
            out string sha,
            includeNativeFile: true);
        var (store, _) = CreateTempStore();

        ApprovalPrompt? promptReceived = null;
        var env = new ApprovalEnvironment
        {
            IsElevated = () => true,
            ExtensionsRoot = _tempDir,
            Store = store,
            FirstParty = FirstPartyAllowlist.Empty,
            Confirm = p => { promptReceived = p; return true; },
            Log = _ => { },
            HostVersion = new Version(4, 2, 0)
        };

        int exitCode = ExtensionApproval.Run([ExtensionApproval.Switch, "ext1", sha], env);

        Assert.AreEqual(ExtensionApproval.ExitApproved, exitCode);
        Assert.IsTrue(store.IsApproved("ext1", sha));
        Assert.IsNotNull(promptReceived);
        Assert.AreEqual("ext1", promptReceived.Id);
        Assert.AreEqual("Extension ext1", promptReceived.Name);
        Assert.AreEqual("1.0.0", promptReceived.Version);
        Assert.AreEqual(sha[..12], promptReceived.ManifestShaPrefix);
        Assert.AreEqual(2, promptReceived.Files.Count);
        Assert.AreEqual("SysMonitor.Tests.DummyExtension.dll", promptReceived.Files[0]);
        Assert.AreEqual("dummynative.dll", promptReceived.Files[1]);
        Assert.IsTrue(promptReceived.HasNativeFiles);
    }

    // 5. Confirm false -> ExitDeclined; store not written
    [TestMethod]
    public void Run_ConfirmFalse_ReturnsExitDeclined_StoreNotWritten()
    {
        ExtensionTestPackageBuilder.CreateExtensionDirectory(_tempDir, "ext_declined", out string sha);
        var (store, storePath) = CreateTempStore();

        var env = new ApprovalEnvironment
        {
            IsElevated = () => true,
            ExtensionsRoot = _tempDir,
            Store = store,
            FirstParty = FirstPartyAllowlist.Empty,
            Confirm = _ => false,
            Log = _ => { },
            HostVersion = new Version(4, 2, 0)
        };

        int exitCode = ExtensionApproval.Run([ExtensionApproval.Switch, "ext_declined", sha], env);

        Assert.AreEqual(ExtensionApproval.ExitDeclined, exitCode);
        Assert.IsFalse(store.IsApproved("ext_declined", sha));
        Assert.IsFalse(File.Exists(storePath));
    }

    // 6. Manifest changed after hash was computed (argument is old hash) -> ExitInspectFailed; Confirm not called; no write
    [TestMethod]
    public void Run_ManifestChangedAfterHashComputed_ReturnsExitInspectFailed()
    {
        string extDir = ExtensionTestPackageBuilder.CreateExtensionDirectory(_tempDir, "ext_changed", out string oldSha);
        var (store, _) = CreateTempStore();

        // Alter the manifest on disk after computing oldSha
        string manifestPath = Path.Combine(extDir, "extension.json");
        string json = File.ReadAllText(manifestPath);
        json = json.Replace("Extension ext_changed", "Renamed Ext");
        File.WriteAllText(manifestPath, json);

        bool confirmCalled = false;
        var env = new ApprovalEnvironment
        {
            IsElevated = () => true,
            ExtensionsRoot = _tempDir,
            Store = store,
            FirstParty = FirstPartyAllowlist.Empty,
            Confirm = _ => { confirmCalled = true; return true; },
            Log = _ => { },
            HostVersion = new Version(4, 2, 0)
        };

        int exitCode = ExtensionApproval.Run([ExtensionApproval.Switch, "ext_changed", oldSha], env);

        Assert.AreEqual(ExtensionApproval.ExitInspectFailed, exitCode);
        Assert.IsFalse(confirmCalled);
        Assert.IsFalse(store.IsApproved("ext_changed", oldSha));
    }

    // 7. A declared file with a wrong hash, an unlisted file in the folder, a folder missing, an id/folder mismatch -> ExitInspectFailed
    [TestMethod]
    public void Run_StructuralDefects_ReturnsExitInspectFailed()
    {
        var (store, _) = CreateTempStore();
        var env = new ApprovalEnvironment
        {
            IsElevated = () => true,
            ExtensionsRoot = _tempDir,
            Store = store,
            FirstParty = FirstPartyAllowlist.Empty,
            Confirm = _ => true,
            Log = _ => { },
            HostVersion = new Version(4, 2, 0)
        };

        // (a) Declared file with wrong hash
        string extDirA = ExtensionTestPackageBuilder.CreateExtensionDirectory(_tempDir, "ext_corrupt_file", out string shaA);
        File.WriteAllBytes(Path.Combine(extDirA, "SysMonitor.Tests.DummyExtension.dll"), new byte[] { 1, 2, 3 });
        Assert.AreEqual(ExtensionApproval.ExitInspectFailed, ExtensionApproval.Run([ExtensionApproval.Switch, "ext_corrupt_file", shaA], env));

        // (b) Unlisted file in the folder
        string extDirB = ExtensionTestPackageBuilder.CreateExtensionDirectory(_tempDir, "ext_unlisted_file", out string shaB);
        File.WriteAllText(Path.Combine(extDirB, "unlisted.txt"), "forbidden");
        Assert.AreEqual(ExtensionApproval.ExitInspectFailed, ExtensionApproval.Run([ExtensionApproval.Switch, "ext_unlisted_file", shaB], env));

        // (c) Folder missing
        Assert.AreEqual(ExtensionApproval.ExitInspectFailed, ExtensionApproval.Run([ExtensionApproval.Switch, "ext_missing", shaA], env));

        // (d) Id / folder mismatch
        string extDirD = ExtensionTestPackageBuilder.CreateExtensionDirectoryWithManifest(_tempDir, "ext_mismatch_dir", m => m.Id = "different_id");
        byte[] manifestBytes = File.ReadAllBytes(Path.Combine(extDirD, "extension.json"));
        string shaD = ExtensionTestPackageBuilder.ComputeSha256(manifestBytes);
        Assert.AreEqual(ExtensionApproval.ExitInspectFailed, ExtensionApproval.Run([ExtensionApproval.Switch, "ext_mismatch_dir", shaD], env));
    }

    // 8. First-party package -> ExitApproved, nothing written, Confirm not called
    [TestMethod]
    public void Run_FirstPartyPackage_ReturnsExitApproved_NothingWritten_ConfirmNotCalled()
    {
        ExtensionTestPackageBuilder.CreateExtensionDirectory(_tempDir, "ext_firstparty", out string sha);
        var (store, storePath) = CreateTempStore();

        bool confirmCalled = false;
        var env = new ApprovalEnvironment
        {
            IsElevated = () => true,
            ExtensionsRoot = _tempDir,
            Store = store,
            FirstParty = new FirstPartyAllowlist(new[] { sha }),
            Confirm = _ => { confirmCalled = true; return true; },
            Log = _ => { },
            HostVersion = new Version(4, 2, 0)
        };

        int exitCode = ExtensionApproval.Run([ExtensionApproval.Switch, "ext_firstparty", sha], env);

        Assert.AreEqual(ExtensionApproval.ExitApproved, exitCode);
        Assert.IsFalse(confirmCalled);
        Assert.IsFalse(File.Exists(storePath), "First party approvals must not write to store file");
    }

    // 9. No extension code runs: set AlcCreatedForTesting and NativeLoadingForTesting to counters;
    // full approved Run leaves both at zero; dummy extension Initialized stays false
    [TestMethod]
    public void Run_NoExtensionCodeRuns_AlcAndNativeCountersStayZero_InitializedStaysFalse()
    {
        ExtensionTestPackageBuilder.CreateExtensionDirectory(
            _tempDir,
            "ext_no_run",
            out string sha,
            entryType: typeof(DummyViewsExtension).FullName!,
            includeNativeFile: true);
        var (store, _) = CreateTempStore();

        DummyViewsExtension.Reset();
        int alcCount = 0;
        int nativeCount = 0;

        ExtensionLoader.AlcCreatedForTesting = _ => Interlocked.Increment(ref alcCount);
        ExtensionLoader.NativeLoadingForTesting = _ => Interlocked.Increment(ref nativeCount);

        try
        {
            var env = new ApprovalEnvironment
            {
                IsElevated = () => true,
                ExtensionsRoot = _tempDir,
                Store = store,
                FirstParty = FirstPartyAllowlist.Empty,
                Confirm = _ => true,
                Log = _ => { },
                HostVersion = new Version(4, 2, 0)
            };

            int exitCode = ExtensionApproval.Run([ExtensionApproval.Switch, "ext_no_run", sha], env);

            Assert.AreEqual(ExtensionApproval.ExitApproved, exitCode);
            Assert.AreEqual(0, alcCount, "Approval helper must never create an AssemblyLoadContext");
            Assert.AreEqual(0, nativeCount, "Approval helper must never load native libraries");
            Assert.IsFalse(DummyViewsExtension.Initialized, "Extension entry Initialized must remain false across ALC boundary");
        }
        finally
        {
            ExtensionLoader.AlcCreatedForTesting = null;
            ExtensionLoader.NativeLoadingForTesting = null;
            DummyViewsExtension.Reset();
        }
    }

    // 10. Store write failure -> ExitWriteFailed
    [TestMethod]
    public void Run_StoreWriteFailure_ReturnsExitWriteFailed()
    {
        ExtensionTestPackageBuilder.CreateExtensionDirectory(_tempDir, "ext_write_fail", out string sha);

        // Store whose directory check returns false, simulating an ACL write rejection
        var (store, _) = CreateTempStore(aclChecker: _ => false);

        var env = new ApprovalEnvironment
        {
            IsElevated = () => true,
            ExtensionsRoot = _tempDir,
            Store = store,
            FirstParty = FirstPartyAllowlist.Empty,
            Confirm = _ => true,
            Log = _ => { },
            HostVersion = new Version(4, 2, 0)
        };

        int exitCode = ExtensionApproval.Run([ExtensionApproval.Switch, "ext_write_fail", sha], env);

        Assert.AreEqual(ExtensionApproval.ExitWriteFailed, exitCode);
    }

    // 11. ExtensionHost.Reload: unapproved package, host started (status NotApproved) -> approve in temp store ->
    // Reload(id) -> with extension enabled in config becomes Loaded and CollectViews returns its view;
    // with it not enabled becomes Disabled. Reload of unknown id does nothing. Never Save().
    [TestMethod]
    public async Task ExtensionHost_Reload_UpdatesStatusAndViews_UnknownIdNoOp()
    {
        string extDir = ExtensionTestPackageBuilder.CreateExtensionDirectory(
            _tempDir,
            "ext_reload",
            out string sha,
            entryType: typeof(DummyViewsExtension).FullName!);

        var (store, storePath) = CreateTempStore();

        // 11A. NotApproved + not enabled -> approve in temp store -> Reload -> Disabled
        var configDisabled = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_reload"] = new ExtensionSetting { Enabled = false }
            }
        };

        var optionsDisabled = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            FirstParty = FirstPartyAllowlist.Empty,
            Approvals = store
        };

        using (var hostDisabled = new ExtensionHost(configDisabled, optionsDisabled))
        {
            await hostDisabled.StartAsync();
            Assert.AreEqual(1, hostDisabled.Entries.Count);
            Assert.AreEqual(ExtensionStatus.NotApproved, hostDisabled.Entries[0].Status);

            ApprovalResult approveResult = store.Approve("ext_reload", "Ext Reload", sha);
            Assert.IsTrue(approveResult.Success);

            await hostDisabled.Reload("ext_reload");
            Assert.AreEqual(ExtensionStatus.Disabled, hostDisabled.Entries[0].Status);
            Assert.AreEqual(0, hostDisabled.CollectViews("en").Count);
        }

        // 11B. NotApproved + enabled -> approve in temp store -> Reload -> Loaded
        File.Delete(storePath); // 11A approved the package; 11B must start from an empty store
        var (store2, _) = CreateTempStore();
        var configEnabled = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_reload"] = new ExtensionSetting { Enabled = true }
            }
        };

        var optionsEnabled = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            FirstParty = FirstPartyAllowlist.Empty,
            Approvals = store2
        };

        using (var hostEnabled = new ExtensionHost(configEnabled, optionsEnabled))
        {
            await hostEnabled.StartAsync();
            Assert.AreEqual(1, hostEnabled.Entries.Count);
            Assert.AreEqual(ExtensionStatus.NotApproved, hostEnabled.Entries[0].Status);

            ApprovalResult approveResult2 = store2.Approve("ext_reload", "Ext Reload", sha);
            Assert.IsTrue(approveResult2.Success);

            await hostEnabled.Reload("ext_reload");
            Assert.AreEqual(ExtensionStatus.Loaded, hostEnabled.Entries[0].Status);
            var views = hostEnabled.CollectViews("en");
            Assert.AreEqual(1, views.Count);

            // 11C. Unknown id does nothing
            int entriesCountBefore = hostEnabled.Entries.Count;
            await hostEnabled.Reload("unknown_id");
            Assert.AreEqual(entriesCountBefore, hostEnabled.Entries.Count);
        }
    }

    [TestMethod]
    public void SanitizeText_RemovesSupplementaryFormatCharsAndNeverSplitsSurrogatePair()
    {
        // U+E0041 is a tag character (category Format, outside the BMP); it must not survive.
        string tagged = "ab" + char.ConvertFromUtf32(0xE0041) + "cd";
        Assert.AreEqual("abcd", ExtensionApproval.SanitizeText(tagged, 60));

        // The emoji's high surrogate sits at index 59 of the 61-char text: cut before it.
        string text = new string('a', 59) + "\U0001F600" + "zz";
        string cut = ExtensionApproval.SanitizeText(text, 60);
        Assert.AreEqual(new string('a', 59) + "…", cut);
        for (int i = 0; i < cut.Length; i++)
        {
            Assert.IsFalse(char.IsHighSurrogate(cut[i]) && (i + 1 >= cut.Length || !char.IsLowSurrogate(cut[i + 1])), "Lone high surrogate");
        }
    }

    [TestMethod]
    public void Run_SanitizesPromptText_ControlCharsRemovedTruncatedAndFileListCapped()
    {
        string weirdName = "Bad\nName\r\u202E" + new string('X', 5000);
        string weirdVersion = "1.0\n0\r\u202E" + new string('V', 100);

        string extDir = Path.Combine(_tempDir, "ext_sanitize");
        Directory.CreateDirectory(extDir);

        string targetDll = Path.Combine(extDir, "SysMonitor.Tests.DummyExtension.dll");
        File.Copy(ExtensionTestPackageBuilder.DummyDllSourcePath, targetDll, overwrite: true);
        string dllSha = ExtensionTestPackageBuilder.ComputeSha256(File.ReadAllBytes(targetDll));

        var files = new List<ExtensionFile>();
        for (int i = 0; i < 25; i++)
        {
            string fileName = $"file_{i:D2}.txt";
            string filePath = Path.Combine(extDir, fileName);
            File.WriteAllText(filePath, "content " + i);
            files.Add(new ExtensionFile
            {
                Path = fileName,
                Sha256 = ExtensionTestPackageBuilder.ComputeSha256(File.ReadAllBytes(filePath)),
                Native = false
            });
        }
        files.Add(new ExtensionFile
        {
            Path = "SysMonitor.Tests.DummyExtension.dll",
            Sha256 = dllSha,
            Native = false
        });

        var manifest = new ExtensionManifest
        {
            Id = "ext_sanitize",
            Name = weirdName,
            Version = weirdVersion,
            Contract = "1.0",
            Entry = new ExtensionEntry
            {
                Assembly = "SysMonitor.Tests.DummyExtension.dll",
                Type = typeof(DummyNormalExtension).FullName!
            },
            Files = files
        };

        string manifestJson = JsonSerializer.Serialize(manifest, ExtensionManifest.JsonOptions);
        string manifestPath = Path.Combine(extDir, "extension.json");
        File.WriteAllText(manifestPath, manifestJson);
        string sha = ExtensionTestPackageBuilder.ComputeSha256(File.ReadAllBytes(manifestPath));

        var (store, _) = CreateTempStore();
        ApprovalPrompt? promptReceived = null;
        var env = new ApprovalEnvironment
        {
            IsElevated = () => true,
            ExtensionsRoot = _tempDir,
            Store = store,
            FirstParty = FirstPartyAllowlist.Empty,
            Confirm = p => { promptReceived = p; return true; },
            Log = _ => { },
            HostVersion = new Version(4, 2, 0)
        };

        int exit = ExtensionApproval.Run([ExtensionApproval.Switch, "ext_sanitize", sha], env);
        Assert.AreEqual(ExtensionApproval.ExitApproved, exit);
        Assert.IsNotNull(promptReceived);

        Assert.IsFalse(promptReceived.Name.Contains('\n'));
        Assert.IsFalse(promptReceived.Name.Contains('\r'));
        Assert.IsFalse(promptReceived.Name.Contains('\u202E'));
        Assert.IsTrue(promptReceived.Name.Length <= 61);
        Assert.IsTrue(promptReceived.Name.EndsWith("…"));

        Assert.IsFalse(promptReceived.Version.Contains('\n'));
        Assert.IsFalse(promptReceived.Version.Contains('\r'));
        Assert.IsFalse(promptReceived.Version.Contains('\u202E'));
        Assert.IsTrue(promptReceived.Version.Length <= 33);

        // 26 files -> lists 10 + (+16 more)
        Assert.AreEqual(11, promptReceived.Files.Count);
        Assert.AreEqual("(+16 more)", promptReceived.Files[10]);
    }

    [TestMethod]
    public void IdValidation_SwitchLikeAndSpecialPrefixes_RejectedInTryParseAndManifest()
    {
        const string validSha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        string[] invalidIds = ["--elevated-op", "--approve-extension", "-x", ".hidden", "_x"];

        foreach (string id in invalidIds)
        {
            Assert.IsFalse(
                ExtensionApproval.TryParse([ExtensionApproval.Switch, id, validSha], out _, out _),
                $"TryParse should reject switch-like id: '{id}'");

            var manifest = new ExtensionManifest
            {
                Id = id,
                Name = "Test",
                Version = "1.0.0",
                Contract = "1.0",
                Entry = new ExtensionEntry { Assembly = "test.dll", Type = "Test.Type" },
                Files = [new ExtensionFile { Path = "test.dll", Sha256 = validSha }]
            };
            bool valid = ExtensionManifest.Validate(manifest, out string? error);
            Assert.IsFalse(valid, $"Manifest should reject invalid id: '{id}'");
            Assert.IsNotNull(error);
        }

        string[] validIds = ["a", "a-b", "0.x_y"];
        foreach (string id in validIds)
        {
            Assert.IsTrue(
                ExtensionApproval.TryParse([ExtensionApproval.Switch, id, validSha], out string parsedId, out _),
                $"TryParse should accept valid id: '{id}'");
            Assert.AreEqual(id, parsedId);

            var manifest = new ExtensionManifest
            {
                Id = id,
                Name = "Test",
                Version = "1.0.0",
                Contract = "1.0",
                Entry = new ExtensionEntry { Assembly = "test.dll", Type = "Test.Type" },
                Files = [new ExtensionFile { Path = "test.dll", Sha256 = validSha }]
            };
            Assert.IsTrue(ExtensionManifest.Validate(manifest, out _), $"Manifest should accept valid id: '{id}'");
        }
    }

    // 12. ExtensionStatusText.CanApprove truth table; ExtensionEntry.ManifestSha256 is filled for NotApproved
    [TestMethod]
    public async Task CanApprove_TruthTable_AndManifestSha256FilledForNotApproved()
    {
        // Truth table
        Assert.IsTrue(ExtensionStatusText.CanApprove(ExtensionStatus.NotApproved, hostElevated: false));
        Assert.IsFalse(ExtensionStatusText.CanApprove(ExtensionStatus.NotApproved, hostElevated: true));
        Assert.IsFalse(ExtensionStatusText.CanApprove(ExtensionStatus.Loaded, hostElevated: false));
        Assert.IsFalse(ExtensionStatusText.CanApprove(ExtensionStatus.Loaded, hostElevated: true));
        Assert.IsFalse(ExtensionStatusText.CanApprove(ExtensionStatus.Disabled, hostElevated: false));
        Assert.IsFalse(ExtensionStatusText.CanApprove(ExtensionStatus.Disabled, hostElevated: true));
        Assert.IsFalse(ExtensionStatusText.CanApprove(ExtensionStatus.NotTrustedWhenElevated, hostElevated: false));
        Assert.IsFalse(ExtensionStatusText.CanApprove(ExtensionStatus.NotTrustedWhenElevated, hostElevated: true));
        Assert.IsFalse(ExtensionStatusText.CanApprove(ExtensionStatus.HashMismatch, hostElevated: false));
        Assert.IsFalse(ExtensionStatusText.CanApprove(ExtensionStatus.FileMissing, hostElevated: false));

        // ManifestSha256 in ExtensionEntry
        ExtensionTestPackageBuilder.CreateExtensionDirectory(_tempDir, "ext_unapproved_sha", out string sha);
        var (store, _) = CreateTempStore();

        var config = new AppConfig
        {
            Extensions = new Dictionary<string, ExtensionSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["ext_unapproved_sha"] = new ExtensionSetting { Enabled = false }
            }
        };

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            FirstParty = FirstPartyAllowlist.Empty,
            Approvals = store
        };

        using var host = new ExtensionHost(config, options);
        await host.StartAsync();

        Assert.AreEqual(1, host.Entries.Count);
        var entry = host.Entries[0];
        Assert.AreEqual(ExtensionStatus.NotApproved, entry.Status);
        Assert.AreEqual(sha, entry.ManifestSha256, "ManifestSha256 must be populated on NotApproved entry");
    }
}
