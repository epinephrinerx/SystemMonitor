using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using SysMonitor.Extensibility;
using SysMonitor.Extensions;
using SysMonitor.Tests.DummyExtension;

namespace SysMonitor.Tests.Extensions;

[TestClass]
public class ExtensionLoaderTests
{
    private string _tempDir = null!;
    private string _dummyDllSourcePath = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SysMon_ExtLoaderTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _dummyDllSourcePath = Path.Combine(AppContext.BaseDirectory, "SysMonitor.Tests.DummyExtension.dll");
        if (!File.Exists(_dummyDllSourcePath))
        {
            _dummyDllSourcePath = typeof(DummyNormalExtension).Assembly.Location;
        }

        Assert.IsTrue(File.Exists(_dummyDllSourcePath), $"DummyExtension DLL not found at: {_dummyDllSourcePath}");
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
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(50);
                }
            }

            // Final attempt: don't swallow failure
            TestJunctionHelper.DeleteTree(_tempDir);
        }
    }

    [TestMethod]
    public void LoadAll_NullOptions_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() => ExtensionLoader.LoadAll(null!));
    }

    // Acceptance criterion 1: Missing extensions root directory -> empty list, no exception
    [TestMethod]
    public void LoadAll_WhenDirectoryDoesNotExist_ReturnsEmptyListWithoutException()
    {
        string nonExistentRoot = Path.Combine(_tempDir, "non_existent_extensions");

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = nonExistentRoot,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false
        };

        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.IsNotNull(results);
        Assert.AreEqual(0, results.Count);
    }

    // Acceptance criterion 2: Normal first-party dummy extension loads, Initialize called with context,
    // Dispose calls Shutdown, ALC unloaded.
    // Context check: language, hostIsElevated, contractVersion, absolute canonical ExtensionDirectory.
    // Acceptance criterion 3: Type identity check (cast to IExtension succeeds)
    [TestMethod]
    public void LoadAll_NormalFirstParty_LoadsAndCallsInitialize_DisposeUnloadsAlc()
    {
        string extDir = CreateExtensionDirectory("dummy_normal", out string manifestSha256);

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            Language = "th",
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        WeakReference alcRef = ExecuteLoadNormalAndDispose(options, extDir);

        // Force GC to verify collectible ALC unloading
        for (int i = 0; i < 10 && alcRef.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.IsFalse(alcRef.IsAlive, "AssemblyLoadContext must be unloaded and collected after Dispose()");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ExecuteLoadNormalAndDispose(ExtensionLoaderOptions options, string extDir)
    {
        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        ExtensionLoadResult result = results[0];
        Assert.AreEqual(ExtensionStatus.Loaded, result.Status, result.Detail);
        Assert.AreEqual("dummy_normal", result.Id);
        Assert.IsNotNull(result.LoadedExtension);

        // Type identity check (Acceptance criterion 3)
        IExtension instance = result.LoadedExtension.Instance;
        Assert.IsNotNull(instance);
        Assert.AreSame(typeof(IExtension).Assembly, instance.GetType().GetInterface("IExtension")!.Assembly);

        // Context checks verified from marker file written by dummy extension (Item 12)
        string markerPath = Path.Combine(extDir, "context_marker.txt");
        Assert.IsTrue(File.Exists(markerPath), "context_marker.txt must be written by DummyNormalExtension");
        string[] lines = File.ReadAllLines(markerPath);
        Assert.AreEqual("th", lines[0], "Language in context must match options");
        Assert.AreEqual("False", lines[1], "HostIsElevated in context must match options");
        Assert.AreEqual(ContractInfo.Version.ToString(), lines[2], "HostContractVersion must match host");
        Assert.IsTrue(Path.IsPathRooted(lines[3]), "ExtensionDirectory must be rooted / absolute");
        Assert.AreEqual(Path.GetFullPath(extDir), lines[3], "ExtensionDirectory must be canonical absolute path");

        Assert.IsTrue(File.Exists(Path.Combine(extDir, "initialized.txt")));

        var alc = result.LoadedExtension.LoadContext;
        var alcRef = new WeakReference(alc);

        result.LoadedExtension.Dispose();

        Assert.IsTrue(File.Exists(Path.Combine(extDir, "shutdown.txt")));

        return alcRef;
    }

    // Acceptance criterion 4: Modified bytes in DLL or native file -> HashMismatch
    [TestMethod]
    public void LoadAll_TamperedManagedDll_ReturnsHashMismatch()
    {
        string extDir = CreateExtensionDirectory("tampered_managed", out string manifestSha256);

        // Tamper with DLL after manifest was created
        string dllPath = Path.Combine(extDir, "SysMonitor.Tests.DummyExtension.dll");
        byte[] bytes = File.ReadAllBytes(dllPath);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(dllPath, bytes);

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.HashMismatch, results[0].Status);
        Assert.IsNull(results[0].LoadedExtension);
    }

    [TestMethod]
    public void LoadAll_TamperedNativeFile_ReturnsHashMismatch()
    {
        string extDir = CreateExtensionDirectory("tampered_native", out string manifestSha256, includeNativeFile: true);

        // Tamper with native file after manifest was created
        string nativePath = Path.Combine(extDir, "dummynative.dll");
        byte[] bytes = File.ReadAllBytes(nativePath);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(nativePath, bytes);

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.HashMismatch, results[0].Status);
        Assert.IsNull(results[0].LoadedExtension);
    }

    // Acceptance criterion 5: extension.json modified after allowlisting -> NotApproved in normal mode, NotTrustedWhenElevated in admin mode
    [TestMethod]
    public void LoadAll_ManifestModifiedAfterAllowlist_RejectsWithAppropriateStatus()
    {
        string extDir = CreateExtensionDirectory("modified_manifest", out string originalManifestHash);

        // Tamper with manifest file
        string manifestPath = Path.Combine(extDir, "extension.json");
        File.AppendAllText(manifestPath, "   \n");

        var allowlist = new FirstPartyAllowlist(new[] { originalManifestHash });

        // Normal mode -> NotApproved
        var normalOptions = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            FirstParty = allowlist
        };

        IReadOnlyList<ExtensionLoadResult> normalResults = ExtensionLoader.LoadAll(normalOptions);
        Assert.AreEqual(1, normalResults.Count);
        Assert.AreEqual(ExtensionStatus.NotApproved, normalResults[0].Status);

        // Elevated mode -> NotTrustedWhenElevated
        var elevatedOptions = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = true,
            FirstParty = allowlist
        };

        IReadOnlyList<ExtensionLoadResult> elevatedResults = ExtensionLoader.LoadAll(elevatedOptions);
        Assert.AreEqual(1, elevatedResults.Count);
        Assert.AreEqual(ExtensionStatus.NotTrustedWhenElevated, elevatedResults[0].Status);
    }

    // Acceptance criterion 6: In admin mode: approved in store but not allowlist -> NotTrustedWhenElevated; normal mode -> Loaded
    [TestMethod]
    public void LoadAll_ApprovedInStoreNotAllowlist_LoadedInNormalMode_RejectedWhenElevated()
    {
        string extDir = CreateExtensionDirectory("approved_ext", out string manifestSha256);

        string storePath = Path.Combine(_tempDir, "test_approvals.json");
        var approvals = new ApprovalStore(storePath, aclChecker: _ => true, secureDirectory: _ => { }, secureFile: _ => { });
        ApprovalResult approved = approvals.Approve("approved_ext", "Approved Extension", manifestSha256);
        Assert.IsTrue(approved.Success, approved.Error);

        // Admin mode: NotTrustedWhenElevated
        var adminOptions = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = true,
            FirstParty = FirstPartyAllowlist.Empty,
            Approvals = approvals
        };

        IReadOnlyList<ExtensionLoadResult> adminResults = ExtensionLoader.LoadAll(adminOptions);
        Assert.AreEqual(1, adminResults.Count);
        Assert.AreEqual(ExtensionStatus.NotTrustedWhenElevated, adminResults[0].Status);

        // Normal mode: Loaded
        var normalOptions = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            FirstParty = FirstPartyAllowlist.Empty,
            Approvals = approvals
        };

        IReadOnlyList<ExtensionLoadResult> normalResults = ExtensionLoader.LoadAll(normalOptions);
        Assert.AreEqual(1, normalResults.Count);
        Assert.AreEqual(ExtensionStatus.Loaded, normalResults[0].Status);
        normalResults[0].LoadedExtension?.Dispose();
    }

    // Acceptance criterion 8: Manifest validation tests
    [TestMethod]
    public void LoadAll_InvalidJson_ReturnsManifestInvalid()
    {
        string folder = Path.Combine(_tempDir, "broken_json");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "extension.json"), "{ invalid JSON content ");

        var options = new ExtensionLoaderOptions { ExtensionsRoot = _tempDir, HostVersion = new Version(4, 2, 0) };
        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.ManifestInvalid, results[0].Status);
    }

    [TestMethod]
    public void LoadAll_InvalidIdFormat_ReturnsManifestInvalid()
    {
        string folder = Path.Combine(_tempDir, "INVALID_UPPERCASE");
        Directory.CreateDirectory(folder);

        string manifest = """
        {
            "id": "INVALID_UPPERCASE",
            "name": "Invalid",
            "version": "1.0.0",
            "contract": "1.0",
            "entry": { "assembly": "test.dll", "type": "Test" },
            "files": [ { "path": "test.dll", "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" } ]
        }
        """;
        File.WriteAllText(Path.Combine(folder, "extension.json"), manifest);

        var options = new ExtensionLoaderOptions { ExtensionsRoot = _tempDir, HostVersion = new Version(4, 2, 0) };
        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.ManifestInvalid, results[0].Status);
    }

    [TestMethod]
    public void LoadAll_Sha256InvalidFormat_ReturnsManifestInvalid()
    {
        string folder = Path.Combine(_tempDir, "bad_sha_ext");
        Directory.CreateDirectory(folder);

        string manifest = """
        {
            "id": "bad_sha_ext",
            "name": "Bad Sha",
            "version": "1.0.0",
            "contract": "1.0",
            "entry": { "assembly": "test.dll", "type": "Test" },
            "files": [ { "path": "test.dll", "sha256": "not_hex_and_too_short" } ]
        }
        """;
        File.WriteAllText(Path.Combine(folder, "extension.json"), manifest);

        var options = new ExtensionLoaderOptions { ExtensionsRoot = _tempDir, HostVersion = new Version(4, 2, 0) };
        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.ManifestInvalid, results[0].Status);
    }

    [TestMethod]
    public void LoadAll_MissingRequiredField_ReturnsManifestInvalid()
    {
        string folder = Path.Combine(_tempDir, "missing_name_ext");
        Directory.CreateDirectory(folder);

        string manifest = """
        {
            "id": "missing_name_ext",
            "version": "1.0.0",
            "contract": "1.0",
            "entry": { "assembly": "test.dll", "type": "Test" },
            "files": [ { "path": "test.dll", "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" } ]
        }
        """;
        File.WriteAllText(Path.Combine(folder, "extension.json"), manifest);

        var options = new ExtensionLoaderOptions { ExtensionsRoot = _tempDir, HostVersion = new Version(4, 2, 0) };
        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.ManifestInvalid, results[0].Status);
    }

    [TestMethod]
    public void LoadAll_FilesArrayContainsNull_ReturnsManifestInvalid()
    {
        string folder = Path.Combine(_tempDir, "null_in_files_ext");
        Directory.CreateDirectory(folder);

        string manifest = """
        {
            "id": "null_in_files_ext",
            "name": "Null Files",
            "version": "1.0.0",
            "contract": "1.0",
            "entry": { "assembly": "test.dll", "type": "Test" },
            "files": [ null ]
        }
        """;
        File.WriteAllText(Path.Combine(folder, "extension.json"), manifest);

        var options = new ExtensionLoaderOptions { ExtensionsRoot = _tempDir, HostVersion = new Version(4, 2, 0) };
        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.ManifestInvalid, results[0].Status);
    }

    [TestMethod]
    public void LoadAll_FolderNameDoesNotMatchId_ReturnsManifestInvalid()
    {
        string folder = Path.Combine(_tempDir, "folder_a");
        Directory.CreateDirectory(folder);

        string manifest = """
        {
            "id": "folder_b",
            "name": "Mismatch",
            "version": "1.0.0",
            "contract": "1.0",
            "entry": { "assembly": "test.dll", "type": "Test" },
            "files": [ { "path": "test.dll", "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" } ]
        }
        """;
        File.WriteAllText(Path.Combine(folder, "extension.json"), manifest);

        var options = new ExtensionLoaderOptions { ExtensionsRoot = _tempDir, HostVersion = new Version(4, 2, 0) };
        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.ManifestInvalid, results[0].Status);
    }

    [TestMethod]
    public void LoadAll_ContractMajorMismatch_ReturnsContractMismatch()
    {
        string extDir = CreateExtensionDirectoryWithManifest("contract_mismatch", manifest => manifest.Contract = "2.0");

        var options = new ExtensionLoaderOptions { ExtensionsRoot = _tempDir, HostVersion = new Version(4, 2, 0) };
        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.ContractMismatch, results[0].Status);
    }

    [TestMethod]
    public void LoadAll_ContractMinorHigherThanHost_ReturnsContractMismatch()
    {
        string extDir = CreateExtensionDirectoryWithManifest("contract_minor_high", manifest => manifest.Contract = "1.5");

        var options = new ExtensionLoaderOptions { ExtensionsRoot = _tempDir, HostVersion = new Version(4, 2, 0) };
        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.ContractMismatch, results[0].Status);
    }

    [TestMethod]
    public void LoadAll_MinHostHigherThanHostVersion_ReturnsHostTooOld()
    {
        string extDir = CreateExtensionDirectoryWithManifest("host_too_old", manifest => manifest.MinHost = "5.0.0");

        var options = new ExtensionLoaderOptions { ExtensionsRoot = _tempDir, HostVersion = new Version(4, 2, 0) };
        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.HostTooOld, results[0].Status);
    }

    [TestMethod]
    public void LoadAll_MinHostNormalizedComparison_Succeeds()
    {
        // Host has 4.2.0.0, manifest has 4.2.0 -> normalized comparison should treat them as equal
        string extDir = CreateExtensionDirectoryWithManifest("min_host_norm", manifest => manifest.MinHost = "4.2.0");
        string manifestPath = Path.Combine(extDir, "extension.json");
        string manifestSha256 = ComputeSha256(File.ReadAllBytes(manifestPath));

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreNotEqual(ExtensionStatus.HostTooOld, results[0].Status);
        Assert.AreEqual(ExtensionStatus.Loaded, results[0].Status, results[0].Detail);
        results[0].LoadedExtension?.Dispose();
    }

    // Acceptance criterion 9: Path safety checks, missing file check, unlisted files check
    [TestMethod]
    [DataRow(@"..\evil.dll")]
    [DataRow(@"C:\evil.dll")]
    [DataRow(@"\\server\share\evil.dll")]
    [DataRow(@"/root/evil.dll")]
    public void LoadAll_UnsafePaths_ReturnsUnsafePath(string unsafePath)
    {
        string extDir = CreateExtensionDirectoryWithManifest("unsafe_path", manifest =>
        {
            manifest.Files.Add(new ExtensionFile
            {
                Path = unsafePath,
                Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
            });
        });

        var options = new ExtensionLoaderOptions { ExtensionsRoot = _tempDir, HostVersion = new Version(4, 2, 0) };
        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.UnsafePath, results[0].Status);
    }

    [TestMethod]
    public void LoadAll_MissingDeclaredFile_ReturnsFileMissing()
    {
        string extDir = CreateExtensionDirectoryWithManifest("missing_file", manifest =>
        {
            manifest.Files.Add(new ExtensionFile
            {
                Path = "non_existent_file.dll",
                Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
            });
        });

        var options = new ExtensionLoaderOptions { ExtensionsRoot = _tempDir, HostVersion = new Version(4, 2, 0) };
        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.FileMissing, results[0].Status);
    }

    [TestMethod]
    public void LoadAll_UnlistedFileInExtensionDirectory_ReturnsUnsafePath()
    {
        string extDir = CreateExtensionDirectory("unlisted_file_ext", out string manifestSha256);
        File.WriteAllText(Path.Combine(extDir, "extra_unexpected.txt"), "surprise");

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.UnsafePath, results[0].Status);
        Assert.IsTrue(results[0].Detail.Contains("extra_unexpected.txt"));
    }

    [TestMethod]
    public void LoadAll_JunctionFolder_ReturnsUnsafePath()
    {
        string realDir = Path.Combine(_tempDir, "real_ext");
        Directory.CreateDirectory(realDir);

        string junctionDir = Path.Combine(_tempDir, "junction_ext");

        if (!TestJunctionHelper.TryCreateJunction(junctionDir, realDir))
        {
            Assert.Inconclusive("Could not create directory junction on this system.");
        }

        var options = new ExtensionLoaderOptions { ExtensionsRoot = _tempDir, HostVersion = new Version(4, 2, 0) };
        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        ExtensionLoadResult? junctionResult = results.FirstOrDefault(r => r.FolderName == "junction_ext");
        Assert.IsNotNull(junctionResult);
        Assert.AreEqual(ExtensionStatus.UnsafePath, junctionResult.Status);
    }

    [TestMethod]
    public void LoadAll_IntermediateSubdirectoryJunction_ReturnsUnsafePath()
    {
        string extDir = CreateExtensionDirectory("ext_sub_junction", out string manifestSha256);
        string realSubDir = Path.Combine(_tempDir, "real_sub");
        Directory.CreateDirectory(realSubDir);

        string junctionSubDir = Path.Combine(extDir, "subjunction");
        if (!TestJunctionHelper.TryCreateJunction(junctionSubDir, realSubDir))
        {
            Assert.Inconclusive("Could not create directory junction on this system.");
        }

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);
        ExtensionLoadResult? res = results.FirstOrDefault(r => r.FolderName == "ext_sub_junction");
        Assert.IsNotNull(res);
        Assert.AreEqual(ExtensionStatus.UnsafePath, res.Status, res.Detail);
    }

    [TestMethod]
    public void LoadAll_FileLevelSymlink_ReturnsUnsafePath()
    {
        string realFile = Path.Combine(_tempDir, "real_target.dll");
        File.WriteAllText(realFile, "content");

        string extDir = CreateExtensionDirectory("ext_symlink_file", out string manifestSha256);
        string symlinkFile = Path.Combine(extDir, "symlinked_file.dll");

        if (!TestJunctionHelper.TryCreateFileSymlink(symlinkFile, realFile))
        {
            Assert.Inconclusive("Could not create file symbolic link on this system (requires developer mode or elevation).");
        }

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);
        ExtensionLoadResult? res = results.FirstOrDefault(r => r.FolderName == "ext_symlink_file");
        Assert.IsNotNull(res);
        Assert.AreEqual(ExtensionStatus.UnsafePath, res.Status, res.Detail);
    }

    // Acceptance criterion 10: Throwing extension -> LoadFailed and ALC unloaded;
    // other extensions in same run load normally; DuplicateId works
    [TestMethod]
    public void LoadAll_InitializeThrows_ReturnsLoadFailedAndUnloadsAlc()
    {
        string extDir = CreateExtensionDirectory(
            "throwing_ext",
            out string manifestSha256,
            entryType: typeof(DummyThrowingExtension).FullName!);

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        WeakReference alcRef = ExecuteLoadThrowing(options);

        for (int i = 0; i < 10 && alcRef.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.IsFalse(alcRef.IsAlive, "ALC must be unloaded and collected when Initialize throws");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ExecuteLoadThrowing(ExtensionLoaderOptions options)
    {
        WeakReference? alcRef = null;
        ExtensionLoader.AlcCreatedForTesting = alc => alcRef = new WeakReference(alc);

        try
        {
            IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ExtensionStatus.LoadFailed, results[0].Status);
            Assert.IsNull(results[0].LoadedExtension);
            Assert.IsNotNull(alcRef);
            return alcRef;
        }
        finally
        {
            ExtensionLoader.AlcCreatedForTesting = null;
        }
    }

    [TestMethod]
    public void LoadAll_LoggerThrowsException_DoesNotThrowAndAlcIsUnloaded()
    {
        string extDir = CreateExtensionDirectory("throwing_logger_ext", out string manifestSha256);

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 }),
            Log = _ => throw new InvalidOperationException("Simulated logger failure")
        };

        WeakReference alcRef = LoadAndDisposeWithThrowingLogger(options);

        // The load path logs ("Extension loaded..."), so a throwing logger was exercised
        // for real; the extension still has to come back Loaded and unload cleanly.
        for (int i = 0; i < 10 && alcRef.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.IsFalse(alcRef.IsAlive, "ALC must be collected even when logger throws exceptions");
    }

    // Separate non-inlined frame so no local in the test method keeps the ALC reachable.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndDisposeWithThrowingLogger(ExtensionLoaderOptions options)
    {
        WeakReference? alcRef = null;
        ExtensionLoader.AlcCreatedForTesting = alc => alcRef = new WeakReference(alc);
        try
        {
            IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ExtensionStatus.Loaded, results[0].Status, results[0].Detail);
            results[0].LoadedExtension!.Dispose();
            Assert.IsNotNull(alcRef);
            return alcRef;
        }
        finally
        {
            ExtensionLoader.AlcCreatedForTesting = null;
        }
    }

    [TestMethod]
    public void LoadAll_MultipleExtensions_OneFailureDoesNotBlockOthers()
    {
        string throwingDir = CreateExtensionDirectory(
            "ext_a_throwing",
            out string throwingHash,
            entryType: typeof(DummyThrowingExtension).FullName!);

        string normalDir = CreateExtensionDirectory(
            "ext_b_normal",
            out string normalHash,
            entryType: typeof(DummyNormalExtension).FullName!);

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            FirstParty = new FirstPartyAllowlist(new[] { throwingHash, normalHash })
        };

        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual("ext_a_throwing", results[0].FolderName);
        Assert.AreEqual(ExtensionStatus.LoadFailed, results[0].Status);

        Assert.AreEqual("ext_b_normal", results[1].FolderName);
        Assert.AreEqual(ExtensionStatus.Loaded, results[1].Status);
        results[1].LoadedExtension?.Dispose();
    }

    [TestMethod]
    public void LoadAll_DuplicateId_ReturnsDuplicateId()
    {
        string extDir = CreateExtensionDirectory("dup_ext", out string manifestSha256);

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        var loadedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dup_ext" };
        ExtensionLoadResult result = ExtensionLoader.LoadSingleFolder(extDir, options, loadedIds);

        Assert.AreEqual(ExtensionStatus.DuplicateId, result.Status);
    }

    // Acceptance criterion 11: Native file with native: true passes hash and loads via ALC;
    // Native file not in manifest is rejected as UnsafePath per Rule 5(a)
    [TestMethod]
    public void LoadAll_NativeFileInManifest_LoadsAndExecutesSuccessfully()
    {
        string extDir = CreateExtensionDirectory(
            "native_ext",
            out string manifestSha256,
            entryType: typeof(DummyNativeExtension).FullName!,
            includeNativeFile: true);

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        try
        {
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(ExtensionStatus.Loaded, results[0].Status, results[0].Detail);

            object instance = results[0].LoadedExtension!.Instance;
            var result = (int)instance.GetType().GetProperty(nameof(DummyNativeExtension.NativeResult))!.GetValue(instance)!;
            Assert.AreEqual(42, result);
        }
        finally
        {
            foreach (var r in results)
            {
                r.LoadedExtension?.Dispose();
            }
        }
    }

    [TestMethod]
    public void LoadAll_NativeFileNotInManifest_RejectedAsUnsafePath()
    {
        // When an extra native DLL exists on disk but is omitted from manifest 'files',
        // rule 5(a) rejects the folder as UnsafePath because unlisted files are forbidden.
        string extDir = CreateExtensionDirectory(
            "native_unlisted",
            out string manifestSha256,
            entryType: typeof(DummyNativeExtension).FullName!,
            includeNativeFile: true,
            omitNativeFromManifest: true,
            nativeFileName: "dummyunlisted.dll");

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.UnsafePath, results[0].Status);
        Assert.IsTrue(results[0].Detail.Contains("dummyunlisted.dll"));
    }

    [TestMethod]
    public void LoadAll_EntryTypeNotFoundOrNotImplementing_ReturnsEntryNotFound()
    {
        string extDir = CreateExtensionDirectory(
            "not_implementing",
            out string manifestSha256,
            entryType: typeof(DummyNotImplementingExtension).FullName!);

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            HostIsElevated = false,
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.EntryNotFound, results[0].Status);
    }

    // A native library must not run before every file in the package has been verified:
    // its imports may be resolved from the extension folder.
    [TestMethod]
    public void LoadAll_BadManagedHashAfterNative_LoadsNoNativeCode()
    {
        string extDir = CreateExtensionDirectory(
            "native_then_bad",
            out string manifestSha256,
            includeNativeFile: true,
            nativeFirst: true);

        string managed = Path.Combine(extDir, "SysMonitor.Tests.DummyExtension.dll");
        byte[] bytes = File.ReadAllBytes(managed);
        bytes[bytes.Length - 1] ^= 0xFF;
        File.WriteAllBytes(managed, bytes);

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        var nativeLoads = new List<string>();
        ExtensionLoader.NativeLoadingForTesting = nativeLoads.Add;
        try
        {
            IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);
            Assert.AreEqual(ExtensionStatus.HashMismatch, results[0].Status, results[0].Detail);
        }
        finally
        {
            ExtensionLoader.NativeLoadingForTesting = null;
        }

        // The hook fires right before LoadLibraryEx, so this also catches load-then-free.
        Assert.AreEqual(0, nativeLoads.Count, "the native library was loaded before the package was fully verified");
    }

    [TestMethod]
    public void LoadAll_NativeLibrary_IsPreloadedFromTheExtensionFolder()
    {
        string extDir = CreateExtensionDirectory("native_hook", out string manifestSha256,
            entryType: typeof(DummyNativeExtension).FullName!, includeNativeFile: true);
        var nativeLoads = new List<string>();
        ExtensionLoader.NativeLoadingForTesting = nativeLoads.Add;
        try
        {
            var options = new ExtensionLoaderOptions
            {
                ExtensionsRoot = _tempDir,
                HostVersion = new Version(4, 2, 0),
                FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
            };
            IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);
            Assert.AreEqual(ExtensionStatus.Loaded, results[0].Status, results[0].Detail);
            results[0].LoadedExtension!.Dispose();
        }
        finally
        {
            ExtensionLoader.NativeLoadingForTesting = null;
        }

        Assert.AreEqual(1, nativeLoads.Count);
        Assert.AreEqual(Path.Combine(extDir, "dummynative.dll"), nativeLoads[0], ignoreCase: true);
    }

    [TestMethod]
    public void LoadAll_ExceptionWithThrowingMessage_ReturnsLoadFailedAndDoesNotThrow()
    {
        CreateExtensionDirectory("broken_message", out string manifestSha256,
            entryType: typeof(DummyBrokenMessageExtension).FullName!);
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(ExtensionStatus.LoadFailed, results[0].Status);
    }

    [TestMethod]
    public void Dispose_WhenUnloadingHandlerThrows_DoesNotThrowAndStillFreesNativeLibraries()
    {
        string extDir = CreateExtensionDirectory("unloading_throws", out string manifestSha256,
            entryType: typeof(DummyThrowingUnloadingExtension).FullName!, includeNativeFile: true);
        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);
        Assert.AreEqual(ExtensionStatus.Loaded, results[0].Status, results[0].Detail);

        results[0].LoadedExtension!.Dispose();

        // FreeLibrary released the module, so the file is no longer mapped and can be deleted.
        File.Delete(Path.Combine(extDir, "dummynative.dll"));
    }

    [TestMethod]
    public void LoadAll_DuplicateFileEntries_ReturnManifestInvalid()
    {
        string extDir = CreateExtensionDirectory("dup_files", out _);
        string manifestPath = Path.Combine(extDir, "extension.json");
        var manifest = JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(manifestPath), ExtensionManifest.JsonOptions)!;
        manifest.Files.Add(new ExtensionFile
        {
            Path = manifest.Files[0].Path.ToUpperInvariant(),
            Sha256 = manifest.Files[0].Sha256,
            Native = true
        });
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, ExtensionManifest.JsonOptions));
        string hash = ComputeSha256(File.ReadAllBytes(manifestPath));

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { hash })
        };

        IReadOnlyList<ExtensionLoadResult> results = ExtensionLoader.LoadAll(options);

        Assert.AreEqual(ExtensionStatus.ManifestInvalid, results[0].Status, results[0].Detail);
    }

    [TestMethod]
    public void LoadFolder_DisabledExtension_ReturnsDisabledWithoutReadingFiles()
    {
        string extDir = CreateExtensionDirectory("disabled_loader", out string manifestSha256);

        // Corrupt the dll: a disabled extension is never read or hashed, so this must still
        // report Disabled and not HashMismatch.
        string dllPath = Path.Combine(extDir, "SysMonitor.Tests.DummyExtension.dll");
        File.WriteAllBytes(dllPath, new byte[] { 1, 2, 3, 4 });

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 }),
            IsEnabled = id => id != "disabled_loader"
        };

        var result = ExtensionLoader.LoadFolder(extDir, options);
        Assert.AreEqual(ExtensionStatus.Disabled, result.Status);
        Assert.AreEqual("disabled_loader", result.Id);
        Assert.IsNotNull(result.Manifest);
        Assert.IsNull(result.LoadedExtension);
    }

    [TestMethod]
    public void LoadFolder_EnabledExtension_LoadsSuccessfully()
    {
        string extDir = CreateExtensionDirectory("enabled_loader", out string manifestSha256);

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 }),
            IsEnabled = _ => true
        };

        var result = ExtensionLoader.LoadFolder(extDir, options);
        Assert.AreEqual(ExtensionStatus.Loaded, result.Status, result.Detail);
        Assert.AreEqual("enabled_loader", result.Id);
        Assert.IsNotNull(result.Manifest);
        Assert.IsNotNull(result.LoadedExtension);

        result.LoadedExtension.Dispose();
    }

    [TestMethod]
    public void LoadFolder_OutsideExtensionsRoot_RejectedWithUnsafePath()
    {
        string rootDir = Path.Combine(_tempDir, "extensions_root");
        string siblingDir = Path.Combine(_tempDir, "sibling_folder");
        Directory.CreateDirectory(rootDir);
        Directory.CreateDirectory(siblingDir);

        // Put a valid package in the sibling directory
        string siblingExt = Path.Combine(siblingDir, "ext_outside");
        Directory.CreateDirectory(siblingExt);
        string targetDll = Path.Combine(siblingExt, "SysMonitor.Tests.DummyExtension.dll");
        File.Copy(_dummyDllSourcePath, targetDll, overwrite: true);
        string dllSha256 = ComputeSha256(File.ReadAllBytes(targetDll));

        var manifest = new ExtensionManifest
        {
            Id = "ext_outside",
            Name = "Extension Outside",
            Version = "1.0.0",
            Contract = "1.0",
            Entry = new ExtensionEntry
            {
                Assembly = "SysMonitor.Tests.DummyExtension.dll",
                Type = typeof(DummyNormalExtension).FullName!
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
        File.WriteAllText(Path.Combine(siblingExt, "extension.json"), manifestJson);
        string manifestSha256 = ComputeSha256(File.ReadAllBytes(Path.Combine(siblingExt, "extension.json")));

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = rootDir,
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 }),
            IsEnabled = _ => true
        };

        // 1. Refused with UnsafePath when pointing outside root
        var resultOutside = ExtensionLoader.LoadFolder(siblingExt, options);
        Assert.AreEqual(ExtensionStatus.UnsafePath, resultOutside.Status);
        Assert.IsNull(resultOutside.LoadedExtension);
        StringAssert.Contains(resultOutside.Detail, "outside");

        // 2. Same package inside root loads
        string insideExt = Path.Combine(rootDir, "ext_inside");
        Directory.CreateDirectory(insideExt);
        File.Copy(targetDll, Path.Combine(insideExt, "SysMonitor.Tests.DummyExtension.dll"));
        manifest.Id = "ext_inside";
        File.WriteAllText(Path.Combine(insideExt, "extension.json"), JsonSerializer.Serialize(manifest, ExtensionManifest.JsonOptions));
        string insideHash = ComputeSha256(File.ReadAllBytes(Path.Combine(insideExt, "extension.json")));

        var insideOptions = new ExtensionLoaderOptions
        {
            ExtensionsRoot = rootDir,
            FirstParty = new FirstPartyAllowlist(new[] { insideHash }),
            IsEnabled = _ => true
        };

        var resultInside = ExtensionLoader.LoadFolder(insideExt, insideOptions);
        Assert.AreEqual(ExtensionStatus.Loaded, resultInside.Status, resultInside.Detail);
        Assert.IsNotNull(resultInside.LoadedExtension);
        resultInside.LoadedExtension.Dispose();
    }

    [TestMethod]
    public void Inspect_ValidPackage_ReturnsLoadedWithManifestAndHash_NoAlcCreated()
    {
        string extDir = CreateExtensionDirectory("inspect_valid", out string manifestSha256);

        int alcCount = 0;
        ExtensionLoader.AlcCreatedForTesting = _ => Interlocked.Increment(ref alcCount);
        try
        {
            var options = new ExtensionLoaderOptions
            {
                ExtensionsRoot = _tempDir,
                HostVersion = new Version(4, 2, 0),
                FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
            };

            ExtensionInspection inspection = ExtensionLoader.Inspect(extDir, options);

            Assert.AreEqual(ExtensionStatus.Loaded, inspection.Status, inspection.Detail);
            Assert.IsNotNull(inspection.Manifest);
            Assert.AreEqual("inspect_valid", inspection.Manifest.Id);
            Assert.AreEqual(manifestSha256, inspection.ManifestSha256);
            Assert.IsTrue(inspection.IsFirstParty);
            Assert.AreEqual(0, alcCount, "Inspect must never create an AssemblyLoadContext");
        }
        finally
        {
            ExtensionLoader.AlcCreatedForTesting = null;
        }
    }

    [TestMethod]
    public void Inspect_WrongFileHash_ReturnsHashMismatch()
    {
        string extDir = CreateExtensionDirectory("inspect_corrupt", out string manifestSha256);

        // Corrupt the DLL file
        string targetDll = Path.Combine(extDir, "SysMonitor.Tests.DummyExtension.dll");
        File.WriteAllBytes(targetDll, new byte[] { 99, 98, 97 });

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = new FirstPartyAllowlist(new[] { manifestSha256 })
        };

        ExtensionInspection inspection = ExtensionLoader.Inspect(extDir, options);

        Assert.AreEqual(ExtensionStatus.HashMismatch, inspection.Status);
        StringAssert.Contains(inspection.Detail, "hash mismatch");
    }

    [TestMethod]
    public void Inspect_FolderOutsideRoot_ReturnsUnsafePath()
    {
        string rootDir = Path.Combine(_tempDir, "root_a");
        string siblingDir = Path.Combine(_tempDir, "root_b");
        Directory.CreateDirectory(rootDir);
        Directory.CreateDirectory(siblingDir);

        string extDir = Path.Combine(siblingDir, "ext_out");
        Directory.CreateDirectory(extDir);

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = rootDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = FirstPartyAllowlist.Empty
        };

        ExtensionInspection inspection = ExtensionLoader.Inspect(extDir, options);

        Assert.AreEqual(ExtensionStatus.UnsafePath, inspection.Status);
    }

    [TestMethod]
    public void Inspect_NotApprovedPackage_ReturnsLoaded()
    {
        // Not in first-party allowlist and Approvals store is empty
        string extDir = CreateExtensionDirectory("inspect_unapproved", out string manifestSha256);

        var options = new ExtensionLoaderOptions
        {
            ExtensionsRoot = _tempDir,
            HostVersion = new Version(4, 2, 0),
            FirstParty = FirstPartyAllowlist.Empty,
            Approvals = null
        };

        ExtensionInspection inspection = ExtensionLoader.Inspect(extDir, options);

        Assert.AreEqual(ExtensionStatus.Loaded, inspection.Status, "Unapproved package must inspect as Loaded structurally");
        Assert.AreEqual(manifestSha256, inspection.ManifestSha256);
        Assert.IsFalse(inspection.IsFirstParty);
    }

    #region Helper Methods

    private string CreateExtensionDirectory(
        string id,
        out string manifestSha256,
        string? entryType = null,
        bool includeNativeFile = false,
        bool omitNativeFromManifest = false,
        string nativeFileName = "dummynative.dll",
        bool nativeFirst = false) =>
        ExtensionTestPackageBuilder.CreateExtensionDirectory(
            _tempDir,
            id,
            out manifestSha256,
            entryType,
            includeNativeFile,
            omitNativeFromManifest,
            nativeFileName,
            nativeFirst);

    private string CreateExtensionDirectoryWithManifest(string id, Action<ExtensionManifest> configureManifest) =>
        ExtensionTestPackageBuilder.CreateExtensionDirectoryWithManifest(_tempDir, id, configureManifest);

    private static string ComputeSha256(byte[] data) =>
        ExtensionTestPackageBuilder.ComputeSha256(data);

    #endregion
}
