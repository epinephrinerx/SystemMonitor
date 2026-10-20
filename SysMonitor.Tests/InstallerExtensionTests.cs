using System.IO;
using System.Reflection;
using System.Text;
using SysMonitor.Setup;
using SysMonitor.Tests.Extensions;

namespace SysMonitor.Tests;

/// <summary>
/// Tests for the extension-aware installer helpers using a temporary directory
/// and fake PayloadFile records.
///
/// Under no circumstances do these tests call Installer.Install/Uninstall or
/// touch the real install folder, registry, %APPDATA%, or %ProgramData%.
/// </summary>
[TestClass]
public class InstallerExtensionTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sysmon-installer-test-" + Guid.NewGuid().ToString("N"));
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

    [TestMethod]
    public void The_embedded_resource_reader_returns_empty_list_for_payload_less_assembly()
    {
        // Test assembly contains no ext: embedded resources; reader must return an empty list without error.
        var files = Installer.ReadExtensionPayload(typeof(InstallerExtensionTests).Assembly);
        Assert.IsNotNull(files);
        Assert.AreEqual(0, files.Count);
    }

    [TestMethod]
    public void Writes_extensions_and_extension_json_is_written_last()
    {
        var openOrder = new List<string>();
        var files = new List<PayloadFile>
        {
            // Intentionally provide extension.json first in the list to verify it gets reordered to the end
            new PayloadFile("smart", "extension.json", () =>
            {
                openOrder.Add("extension.json");
                return new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":\"smart\"}"));
            }),
            new PayloadFile("smart", "SysMonitor.Ext.Smart.dll", () =>
            {
                openOrder.Add("SysMonitor.Ext.Smart.dll");
                return new MemoryStream(new byte[] { 1, 2, 3 });
            }),
            new PayloadFile("smart", "DiskInfoCore.dll", () =>
            {
                openOrder.Add("DiskInfoCore.dll");
                return new MemoryStream(new byte[] { 4, 5, 6 });
            }),
        };

        var failed = Installer.WriteExtensions(_tempDir, files);
        Assert.AreEqual(0, failed.Count);

        string extDir = Path.Combine(_tempDir, "extensions", "smart");
        Assert.IsTrue(File.Exists(Path.Combine(extDir, "extension.json")));
        Assert.IsTrue(File.Exists(Path.Combine(extDir, "SysMonitor.Ext.Smart.dll")));
        Assert.IsTrue(File.Exists(Path.Combine(extDir, "DiskInfoCore.dll")));

        // Stream open order must have opened extension.json last
        Assert.AreEqual(3, openOrder.Count);
        Assert.AreEqual("extension.json", openOrder.Last(),
            "extension.json must be written last so an incomplete install is never loaded");
    }

    [TestMethod]
    public void Stale_unlisted_file_in_extension_dir_is_removed_subfolder_kept_sibling_untouched()
    {
        string smartDir = Path.Combine(_tempDir, "extensions", "smart");
        Directory.CreateDirectory(smartDir);
        string staleFile = Path.Combine(smartDir, "stale_unlisted.dll");
        File.WriteAllText(staleFile, "stale");

        string subDir = Path.Combine(smartDir, "subfolder");
        Directory.CreateDirectory(subDir);
        string subFile = Path.Combine(subDir, "inner.txt");
        File.WriteAllText(subFile, "inner");

        string sampleDir = Path.Combine(_tempDir, "extensions", "sample");
        Directory.CreateDirectory(sampleDir);
        string sampleManifest = Path.Combine(sampleDir, "extension.json");
        File.WriteAllText(sampleManifest, "{\"id\":\"sample\"}");

        var files = new List<PayloadFile>
        {
            new PayloadFile("smart", "extension.json", "{\"id\":\"smart\"}"),
            new PayloadFile("smart", "SysMonitor.Ext.Smart.dll", "dummy"),
        };

        var failed = Installer.WriteExtensions(_tempDir, files);
        Assert.AreEqual(0, failed.Count);

        // Stale unlisted file in extensions\smart removed
        Assert.IsFalse(File.Exists(staleFile), "stale unlisted file directly in extensions\\smart must be deleted");

        // Subfolder kept
        Assert.IsTrue(Directory.Exists(subDir), "subfolders inside extensions\\smart must be kept untouched");
        Assert.IsTrue(File.Exists(subFile), "files inside subfolders must be kept untouched");

        // Sibling extensions\sample untouched
        Assert.IsTrue(Directory.Exists(sampleDir), "sibling extension folders must not be touched");
        Assert.IsTrue(File.Exists(sampleManifest), "sibling extension files must not be touched");
    }

    [TestMethod]
    [DataRow("..\\x", "Directory traversal in file name")]
    [DataRow("a/b", "Path separator in file name")]
    [DataRow(@"C:\Windows\System32\evil.dll", "Absolute path in file name")]
    [DataRow("", "Empty file name")]
    [DataRow("   ", "Whitespace file name")]
    public void Hostile_file_names_are_rejected_before_writing(string hostileName, string reason)
    {
        var files = new List<PayloadFile>
        {
            new PayloadFile("smart", hostileName, "content"),
        };

        Assert.ThrowsException<InvalidOperationException>(() =>
            Installer.WriteExtensions(_tempDir, files),
            reason);

        // Nothing must have been written
        Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "extensions")));
    }

    [TestMethod]
    [DataRow("SMART", "Uppercase extension ID")]
    [DataRow("", "Empty extension ID")]
    [DataRow("-smart", "Extension ID starting with dash")]
    [DataRow("smart/sub", "Extension ID with path separator")]
    public void Hostile_extension_ids_are_rejected_before_writing(string hostileId, string reason)
    {
        var files = new List<PayloadFile>
        {
            new PayloadFile(hostileId, "extension.json", "content"),
        };

        Assert.ThrowsException<InvalidOperationException>(() =>
            Installer.WriteExtensions(_tempDir, files),
            reason);

        Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "extensions")));
    }

    [TestMethod]
    public void Duplicate_file_names_case_insensitive_are_rejected_before_writing()
    {
        var files = new List<PayloadFile>
        {
            new PayloadFile("smart", "extension.json", "content 1"),
            new PayloadFile("smart", "EXTENSION.JSON", "content 2"),
        };

        Assert.ThrowsException<InvalidOperationException>(() =>
            Installer.WriteExtensions(_tempDir, files));

        Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "extensions")));
    }

    [TestMethod]
    public void Junction_at_extensions_folder_is_refused()
    {
        string outside = Path.Combine(_tempDir, "outside_dir");
        Directory.CreateDirectory(outside);

        string extRoot = Path.Combine(_tempDir, "extensions");
        if (!TestJunctionHelper.TryCreateJunction(extRoot, outside))
        {
            Assert.Inconclusive("Junction creation is not supported in this environment");
        }

        var files = new List<PayloadFile>
        {
            new PayloadFile("smart", "extension.json", "content"),
        };

        Assert.ThrowsException<InvalidOperationException>(() =>
            Installer.WriteExtensions(_tempDir, files));

        // outside directory must not have had anything written to it
        Assert.AreEqual(0, Directory.GetFileSystemEntries(outside).Length);
    }

    [TestMethod]
    public void Junction_at_extension_id_folder_is_refused()
    {
        string outside = Path.Combine(_tempDir, "outside_smart");
        Directory.CreateDirectory(outside);

        string extRoot = Path.Combine(_tempDir, "extensions");
        Directory.CreateDirectory(extRoot);

        string smartJunction = Path.Combine(extRoot, "smart");
        if (!TestJunctionHelper.TryCreateJunction(smartJunction, outside))
        {
            Assert.Inconclusive("Junction creation is not supported in this environment");
        }

        var files = new List<PayloadFile>
        {
            new PayloadFile("smart", "extension.json", "content"),
        };

        Assert.ThrowsException<InvalidOperationException>(() =>
            Installer.WriteExtensions(_tempDir, files));

        // outside directory must not have had anything written to it
        Assert.AreEqual(0, Directory.GetFileSystemEntries(outside).Length);
    }

    [TestMethod]
    public void Uninstall_helper_removes_owned_files_and_empty_folders_while_preserving_third_party_extensions()
    {
        var smartFiles = new List<PayloadFile>
        {
            new PayloadFile("smart", "extension.json", "{\"id\":\"smart\"}"),
            new PayloadFile("smart", "SysMonitor.Ext.Smart.dll", "smart"),
        };

        Installer.WriteExtensions(_tempDir, smartFiles);

        // Put a third-party extension in extensions\sample
        string sampleDir = Path.Combine(_tempDir, "extensions", "sample");
        Directory.CreateDirectory(sampleDir);
        File.WriteAllText(Path.Combine(sampleDir, "extension.json"), "{\"id\":\"sample\"}");
        File.WriteAllText(Path.Combine(sampleDir, "Sample.dll"), "sample");

        // Run uninstall helper for the owned smart extension
        Installer.UninstallExtensions(_tempDir, smartFiles);

        // smart folder and its files are gone
        Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "extensions", "smart")));

        // third-party sample folder and its files survive
        Assert.IsTrue(Directory.Exists(sampleDir));
        Assert.IsTrue(File.Exists(Path.Combine(sampleDir, "extension.json")));
        Assert.IsTrue(File.Exists(Path.Combine(sampleDir, "Sample.dll")));

        // extensions root and installDir survive because sample is still present
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "extensions")));
        Assert.IsTrue(Directory.Exists(_tempDir));

        // Now remove the sample folder as if the user uninstalled it, and run UninstallExtensions again
        Directory.Delete(sampleDir, recursive: true);
        Installer.UninstallExtensions(_tempDir, smartFiles);

        // Empty extensions folder and empty installDir are now cleaned up
        Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "extensions")));
        Assert.IsFalse(Directory.Exists(_tempDir));
    }

    [TestMethod]
    public void Locked_file_does_not_abort_writing_other_files_and_is_reported()
    {
        string smartDir = Path.Combine(_tempDir, "extensions", "smart");
        Directory.CreateDirectory(smartDir);
        string lockedPath = Path.Combine(smartDir, "DiskInfoCore.dll");
        File.WriteAllText(lockedPath, "initial");

        // Lock DiskInfoCore.dll exclusively
        using var lockStream = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var files = new List<PayloadFile>
        {
            new PayloadFile("smart", "DiskInfoCore.dll", "new core"),
            new PayloadFile("smart", "SysMonitor.Ext.Smart.dll", "smart dll"),
            new PayloadFile("smart", "extension.json", "{\"id\":\"smart\"}"),
        };

        var failed = Installer.WriteExtensions(_tempDir, files);

        string manifestPath = Path.Combine(smartDir, "extension.json");
        Assert.IsTrue(failed.Contains(lockedPath));
        // The manifest is withheld: a new manifest next to an old DLL would fail every load with
        // HashMismatch, so it is reported as not written instead of being committed.
        Assert.IsTrue(failed.Contains(manifestPath));
        Assert.AreEqual(2, failed.Count);
        Assert.IsFalse(File.Exists(manifestPath));

        // Other files were still written despite the locked file
        Assert.IsTrue(File.Exists(Path.Combine(smartDir, "SysMonitor.Ext.Smart.dll")));
    }

    [TestMethod]
    public void Stale_file_that_cannot_be_deleted_keeps_the_manifest_from_being_written()
    {
        string smartDir = Path.Combine(_tempDir, "extensions", "smart");
        Directory.CreateDirectory(smartDir);
        string stalePath = Path.Combine(smartDir, "old.dll");
        File.WriteAllText(stalePath, "old");
        using var lockStream = new FileStream(stalePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var files = new List<PayloadFile>
        {
            new PayloadFile("smart", "SysMonitor.Ext.Smart.dll", "smart dll"),
            new PayloadFile("smart", "extension.json", "{}"),
        };

        var failed = Installer.WriteExtensions(_tempDir, files);

        // An unlisted file makes the loader reject the package, so it must not look complete.
        Assert.IsTrue(failed.Contains(stalePath));
        Assert.IsFalse(File.Exists(Path.Combine(smartDir, "extension.json")));
    }

    [TestMethod]
    public void A_file_link_in_the_package_folder_is_not_written_through()
    {
        string smartDir = Path.Combine(_tempDir, "extensions", "smart");
        Directory.CreateDirectory(smartDir);
        string outside = Path.Combine(_tempDir, "outside.txt");
        File.WriteAllText(outside, "untouched");
        string link = Path.Combine(smartDir, "SysMonitor.Ext.Smart.dll");
        if (!TestJunctionHelper.TryCreateFileSymlink(link, outside))
        {
            Assert.Inconclusive("File symbolic links cannot be created here.");
            return;
        }

        var files = new List<PayloadFile>
        {
            new PayloadFile("smart", "SysMonitor.Ext.Smart.dll", "payload bytes"),
            new PayloadFile("smart", "extension.json", "{}"),
        };

        var failed = Installer.WriteExtensions(_tempDir, files);

        Assert.AreEqual("untouched", File.ReadAllText(outside), "the link target must never be overwritten");
        Assert.IsTrue(failed.Contains(link));
        Assert.IsFalse(File.Exists(Path.Combine(smartDir, "extension.json")));
    }

    [TestMethod]
    public void Uninstall_helper_validates_its_input_like_the_write_side()
    {
        string victimDir = Path.Combine(_tempDir, "victim");
        Directory.CreateDirectory(victimDir);
        string victim = Path.Combine(victimDir, "victim.txt");
        File.WriteAllText(victim, "keep");

        string[] hostileIds = { victimDir, "..", "../victim", "Smart", "" };
        foreach (string id in hostileIds)
        {
            var files = new List<PayloadFile> { new PayloadFile(id, "victim.txt", "") };
            Assert.ThrowsException<InvalidOperationException>(
                () => Installer.UninstallExtensions(Path.Combine(_tempDir, "install"), files),
                $"id '{id}' must be rejected");
            Assert.ThrowsException<InvalidOperationException>(
                () => Installer.ExtensionOwnedPaths(Path.Combine(_tempDir, "install"), files).ToArray(),
                $"id '{id}' must be rejected");
        }

        Assert.IsTrue(File.Exists(victim));
    }

    [TestMethod]
    public void Names_that_windows_rewrites_are_rejected()
    {
        foreach (string name in new[] { "extension.json.", "x.dll ", "ab:c", " lead.dll", "a*b.dll", "a?b.dll" })
        {
            var files = new List<PayloadFile> { new PayloadFile("smart", name, "x") };
            Assert.ThrowsException<InvalidOperationException>(
                () => Installer.ValidatePayload(files), $"name '{name}' must be rejected");
        }
    }

    [TestMethod]
    public void Extension_owned_paths_produces_fully_qualified_paths_under_extensions_folder()
    {
        var files = new List<PayloadFile>
        {
            new PayloadFile("smart", "extension.json", "{}"),
            new PayloadFile("smart", "SysMonitor.Ext.Smart.dll", "dll"),
        };

        string[] paths = Installer.ExtensionOwnedPaths(_tempDir, files).ToArray();
        Assert.AreEqual(2, paths.Length);
        Assert.AreEqual(Path.Combine(_tempDir, "extensions", "smart", "extension.json"), paths[0]);
        Assert.AreEqual(Path.Combine(_tempDir, "extensions", "smart", "SysMonitor.Ext.Smart.dll"), paths[1]);
    }
}
