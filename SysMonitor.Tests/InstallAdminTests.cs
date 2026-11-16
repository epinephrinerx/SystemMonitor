using System.IO;
using SysMonitor.Setup;

namespace SysMonitor.Tests;

/// <summary>
/// R-022 phase 2: the machine-wide install and its removal, run against a
/// temporary folder with a recording host. Nothing here touches Program
/// Files, the registry, the Task Scheduler or the user's real settings.
/// </summary>
[TestClass]
public class InstallAdminTests
{
    private string _root = "";
    private KnownFolders _folders = null!;
    private InstallLayout _admin = null!;
    private InstallLayout _perUser = null!;
    private string _uninstallerSource = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "sysmon-r022-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _folders = new KnownFolders(
            LocalAppData: Path.Combine(_root, "Local"),
            AppData: Path.Combine(_root, "Roaming"),
            ProgramFiles: Path.Combine(_root, "Program Files"),
            Desktop: Path.Combine(_root, "Desktop"),
            Programs: Path.Combine(_root, "StartMenu"),
            CommonDesktop: Path.Combine(_root, "PublicDesktop"),
            CommonPrograms: Path.Combine(_root, "CommonStartMenu"));
        foreach (string dir in new[] { _folders.LocalAppData, _folders.AppData, _folders.ProgramFiles,
                                       _folders.Desktop, _folders.Programs,
                                       _folders.CommonDesktop, _folders.CommonPrograms })
        {
            Directory.CreateDirectory(dir);
        }
        _admin = InstallLayout.Create(InstallScope.Admin, _folders);
        _perUser = InstallLayout.Create(InstallScope.PerUser, _folders);
        _uninstallerSource = Path.Combine(_root, "setup.exe");
        File.WriteAllText(_uninstallerSource, "SETUP");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception)
        {
        }
    }

    private InstallSources Sources(string app = "APP") => new(
        () => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(app)),
        _uninstallerSource,
        new[]
        {
            new PayloadFile("smart", "extension.json", "{}"),
            new PayloadFile("smart", "smart.dll", "DLL"),
        });

    private static readonly InstallOptions All = new(DesktopShortcut: true, StartMenu: true, Autostart: true);

    private sealed class FakeHost : IInstallHost
    {
        public bool IsElevated { get; set; } = true;
        public bool FailAutostartRemoval { get; set; }
        public bool FailAutostartSet { get; set; }
        public List<string> Calls { get; } = new();

        public void StopRunningApp(string exePath) => Calls.Add("stop " + exePath);

        public void WriteShortcut(bool wanted, string linkPath, string target, string workingDirectory)
        {
            Calls.Add((wanted ? "shortcut " : "no-shortcut ") + linkPath);
            if (wanted)
            {
                File.WriteAllText(linkPath, target);
            }
        }

        public void RegisterUninstall(InstallLayout layout, int estimatedSizeKb) =>
            Calls.Add("register " + layout.Scope);

        public void UnregisterUninstall(InstallLayout layout) => Calls.Add("unregister " + layout.Scope);

        public void SetAutostart(InstallLayout layout, bool enabled)
        {
            if ((!enabled && FailAutostartRemoval) || (enabled && FailAutostartSet))
            {
                throw new InvalidOperationException("task service unavailable");
            }
            Calls.Add($"autostart {layout.Scope} {enabled}");
        }

        public bool IsAutostartEnabled(InstallLayout layout) => false;
    }

    // ------------------------------------------------------------- install
    [TestMethod]
    public void Admin_install_puts_the_files_under_program_files_and_the_shortcuts_in_the_common_folders()
    {
        var host = new FakeHost();

        Installer.Install(_admin, All, host, Sources());

        Assert.AreEqual("APP", File.ReadAllText(_admin.ExePath));
        Assert.AreEqual("SETUP", File.ReadAllText(_admin.UninstallPath));
        Assert.IsTrue(File.Exists(Path.Combine(_admin.ExtensionsDir, "smart", "extension.json")));
        Assert.IsTrue(File.Exists(Path.Combine(_admin.ExtensionsDir, "smart", "smart.dll")));
        Assert.IsTrue(File.Exists(_admin.DesktopShortcut));
        Assert.IsTrue(File.Exists(_admin.StartMenuShortcut));
        StringAssert.StartsWith(_admin.InstallDir, _folders.ProgramFiles);
        Assert.IsFalse(Directory.Exists(_perUser.InstallDir), "a machine-wide install leaves the per-user folder alone");
    }

    [TestMethod]
    public void Admin_install_records_the_mode_marker_and_registers_for_the_machine_with_a_task()
    {
        var host = new FakeHost();

        Installer.Install(_admin, All, host, Sources());

        StringAssert.Contains(File.ReadAllText(_admin.ModeMarkerPath), "admin");
        CollectionAssert.Contains(host.Calls, "register Admin");
        CollectionAssert.Contains(host.Calls, "autostart Admin True");
        CollectionAssert.DoesNotContain(host.Calls, "register PerUser");
    }

    [TestMethod]
    public void Admin_install_without_autostart_removes_the_task()
    {
        var host = new FakeHost();

        Installer.Install(_admin, All with { Autostart = false }, host, Sources());

        CollectionAssert.Contains(host.Calls, "autostart Admin False");
    }

    [TestMethod]
    public void Admin_install_without_administrator_rights_refuses_and_writes_nothing()
    {
        var host = new FakeHost { IsElevated = false };

        Assert.ThrowsException<InvalidOperationException>(
            () => Installer.Install(_admin, All, host, Sources()));

        Assert.IsFalse(Directory.Exists(_admin.InstallDir));
        Assert.AreEqual(0, host.Calls.Count, "no registry, task or shortcut change without rights");
    }

    [TestMethod]
    public void Per_user_install_still_needs_no_rights_and_writes_no_marker()
    {
        var host = new FakeHost { IsElevated = false };

        Installer.Install(_perUser, All, host, Sources());

        Assert.IsTrue(File.Exists(_perUser.ExePath));
        Assert.IsFalse(File.Exists(_perUser.ModeMarkerPath), "only the machine-wide install writes the marker");
        CollectionAssert.Contains(host.Calls, "register PerUser");
        CollectionAssert.Contains(host.Calls, "autostart PerUser True");
    }

    // ------------------------------------------------------------- upgrade
    private void InstallPerUserWithSettingsAndStrangers(FakeHost host)
    {
        Installer.Install(_perUser, All, host, Sources("OLD"));
        Directory.CreateDirectory(_perUser.SettingsDir);
        File.WriteAllText(Path.Combine(_perUser.SettingsDir, "config.wpf.json"), "{\"pos_x\": 5}");
        File.WriteAllText(Path.Combine(_perUser.InstallDir, "notes.txt"), "the user's own file");
        host.Calls.Clear();
    }

    [TestMethod]
    public void An_unelevated_run_retires_the_per_user_install_without_touching_settings()
    {
        var host = new FakeHost();
        InstallPerUserWithSettingsAndStrangers(host);
        Installer.Install(_admin, All, host, Sources("NEW"));
        host.IsElevated = false;                     // the setup run that follows the elevated one
        host.Calls.Clear();

        bool removed = Installer.MigrateFromPerUser(_perUser, host, Sources().Extensions);

        Assert.IsTrue(removed);
        Assert.AreEqual("NEW", File.ReadAllText(_admin.ExePath), "the machine-wide install is untouched");
        Assert.IsFalse(File.Exists(_perUser.ExePath), "the old program is gone");
        Assert.IsFalse(File.Exists(_perUser.UninstallPath));
        Assert.IsFalse(File.Exists(_perUser.DesktopShortcut));
        Assert.IsFalse(File.Exists(_perUser.StartMenuShortcut));
        Assert.IsTrue(File.Exists(Path.Combine(_perUser.SettingsDir, "config.wpf.json")),
            "the user's config is not the installer's to remove");
        Assert.IsTrue(File.Exists(Path.Combine(_perUser.InstallDir, "notes.txt")),
            "a file the installer did not create stays, and so does its folder");
        CollectionAssert.Contains(host.Calls, "autostart PerUser False");
        CollectionAssert.Contains(host.Calls, "unregister PerUser");
        CollectionAssert.Contains(host.Calls, "stop " + _perUser.ExePath);
    }

    [TestMethod]
    public void An_elevated_run_never_deletes_in_the_per_user_folder_it_only_stops_its_start_up()
    {
        // The per-user folder is writable by the user, and the safety check on
        // a delete target is not atomic with the delete: with administrator
        // rights the deletion would go wherever the folder had been turned
        // into in between.
        var host = new FakeHost();
        InstallPerUserWithSettingsAndStrangers(host);
        host.IsElevated = true;

        bool removed = Installer.MigrateFromPerUser(_perUser, host, Sources().Extensions);

        Assert.IsFalse(removed);
        Assert.IsTrue(File.Exists(_perUser.ExePath));
        Assert.IsTrue(File.Exists(_perUser.UninstallPath));
        Assert.IsTrue(File.Exists(_perUser.DesktopShortcut));
        CollectionAssert.Contains(host.Calls, "autostart PerUser False");
        CollectionAssert.DoesNotContain(host.Calls, "unregister PerUser");
        CollectionAssert.DoesNotContain(host.Calls, "stop " + _perUser.ExePath);
    }

    [TestMethod]
    public void Migration_with_no_per_user_install_does_nothing()
    {
        var host = new FakeHost { IsElevated = false };

        Assert.IsFalse(Installer.MigrateFromPerUser(_perUser, host, Sources().Extensions));
        Assert.AreEqual(0, host.Calls.Count);
    }

    [TestMethod]
    public void Only_a_per_user_install_can_be_migrated()
    {
        Assert.ThrowsException<ArgumentException>(
            () => Installer.MigrateFromPerUser(_admin, new FakeHost(), Sources().Extensions));
    }

    [TestMethod]
    public void A_failed_start_up_step_is_reported_but_the_install_can_still_be_uninstalled()
    {
        var host = new FakeHost();
        InstallPerUserWithSettingsAndStrangers(host);
        host.FailAutostartSet = true;                // only the machine-wide install's step fails

        var error = Assert.ThrowsException<IOException>(
            () => Installer.Install(_admin, All, host, Sources()));

        StringAssert.Contains(error.Message, "start-up entry");
        Assert.IsTrue(File.Exists(_admin.ExePath), "the files are in place");
        CollectionAssert.Contains(host.Calls, "register Admin",
            "it is in Apps & features, so it can be removed");
        Assert.IsTrue(File.Exists(_perUser.ExePath), "the working per-user install is not touched");
    }

    [TestMethod]
    public void A_failed_admin_install_leaves_the_per_user_install_working()
    {
        var host = new FakeHost();
        InstallPerUserWithSettingsAndStrangers(host);
        var broken = Sources() with { OpenApp = () => throw new IOException("disk full") };

        Assert.ThrowsException<IOException>(() => Installer.Install(_admin, All, host, broken));

        Assert.IsTrue(File.Exists(_perUser.ExePath));
        CollectionAssert.DoesNotContain(host.Calls, "unregister PerUser");
    }

    // ----------------------------------------------------------- uninstall
    [TestMethod]
    public void Admin_uninstall_removes_only_what_the_installer_owns()
    {
        var host = new FakeHost();
        Installer.Install(_admin, All, host, Sources());
        Directory.CreateDirectory(_admin.SettingsDir);
        File.WriteAllText(Path.Combine(_admin.SettingsDir, "config.wpf.json"), "{}");
        File.WriteAllText(Path.Combine(_admin.InstallDir, "notes.txt"), "not ours");
        host.Calls.Clear();

        Installer.Uninstall(_admin, removeSettings: false, host, Sources().Extensions);

        Assert.IsFalse(File.Exists(_admin.ExePath));
        Assert.IsFalse(File.Exists(_admin.UninstallPath));
        Assert.IsFalse(File.Exists(_admin.ModeMarkerPath));
        Assert.IsFalse(File.Exists(Path.Combine(_admin.ExtensionsDir, "smart", "smart.dll")));
        Assert.IsFalse(File.Exists(_admin.DesktopShortcut));
        Assert.IsFalse(File.Exists(_admin.StartMenuShortcut));
        Assert.IsTrue(File.Exists(Path.Combine(_admin.InstallDir, "notes.txt")), "a stranger's file stays");
        Assert.IsTrue(File.Exists(Path.Combine(_admin.SettingsDir, "config.wpf.json")), "settings stay unless asked");
        CollectionAssert.Contains(host.Calls, "autostart Admin False");
        CollectionAssert.Contains(host.Calls, "unregister Admin");
    }

    [TestMethod]
    public void Admin_uninstall_never_deletes_in_the_users_settings_folder_even_when_asked()
    {
        // The settings folder is user-writable and the delete-target check is
        // not atomic with the delete, so the elevated uninstall leaves it.
        var host = new FakeHost();
        Installer.Install(_admin, All, host, Sources());
        Directory.CreateDirectory(_admin.SettingsDir);
        File.WriteAllText(Path.Combine(_admin.SettingsDir, "config.wpf.json"), "{}");

        Installer.Uninstall(_admin, removeSettings: true, host, Sources().Extensions);

        Assert.IsTrue(File.Exists(Path.Combine(_admin.SettingsDir, "config.wpf.json")));
        Assert.IsFalse(Directory.Exists(_admin.InstallDir), "an empty install folder goes");
    }

    [TestMethod]
    public void Per_user_uninstall_still_removes_settings_when_asked()
    {
        var host = new FakeHost { IsElevated = false };
        Installer.Install(_perUser, All, host, Sources());
        Directory.CreateDirectory(_perUser.SettingsDir);
        File.WriteAllText(Path.Combine(_perUser.SettingsDir, "config.wpf.json"), "{}");

        Installer.Uninstall(_perUser, removeSettings: true, host, Sources().Extensions);

        Assert.IsFalse(File.Exists(Path.Combine(_perUser.SettingsDir, "config.wpf.json")));
    }

    [TestMethod]
    public void A_start_up_entry_that_cannot_be_removed_does_not_keep_the_files()
    {
        var host = new FakeHost();
        Installer.Install(_admin, All, host, Sources());
        host.FailAutostartRemoval = true;

        Installer.Uninstall(_admin, removeSettings: false, host, Sources().Extensions);

        Assert.IsFalse(File.Exists(_admin.ExePath));
        CollectionAssert.Contains(host.Calls, "unregister Admin");
    }

    [TestMethod]
    public void Admin_uninstall_without_administrator_rights_refuses_and_removes_nothing()
    {
        var host = new FakeHost();
        Installer.Install(_admin, All, host, Sources());
        host.IsElevated = false;
        host.Calls.Clear();

        Assert.ThrowsException<InvalidOperationException>(
            () => Installer.Uninstall(_admin, removeSettings: true, host, Sources().Extensions));

        Assert.IsTrue(File.Exists(_admin.ExePath));
        Assert.AreEqual(0, host.Calls.Count);
    }

    [TestMethod]
    public void The_running_uninstaller_is_skipped_so_it_can_be_removed_after_it_exits()
    {
        var host = new FakeHost();
        Installer.Install(_admin, All, host, Sources());

        Installer.Uninstall(_admin, removeSettings: false, host, Sources().Extensions, skip: _admin.UninstallPath);

        Assert.IsTrue(File.Exists(_admin.UninstallPath));
        Assert.IsFalse(File.Exists(_admin.ExePath));
    }

    // ------------------------------------------------------------ ownership
    [TestMethod]
    public void Only_the_machine_wide_install_owns_a_mode_marker()
    {
        string[] admin = Installer.OwnedPaths(_admin, includeSettings: false, Sources().Extensions).ToArray();
        string[] perUser = Installer.OwnedPaths(_perUser, includeSettings: false, Sources().Extensions).ToArray();

        CollectionAssert.Contains(admin, _admin.ModeMarkerPath);
        CollectionAssert.DoesNotContain(perUser, _perUser.ModeMarkerPath);
    }

    [TestMethod]
    public void Every_owned_path_of_the_admin_layout_is_inside_its_own_folders()
    {
        string[] allowed =
        {
            _admin.InstallDir, _admin.SettingsDir, _folders.CommonDesktop, _folders.CommonPrograms,
            Path.Combine(_admin.ExtensionsDir, "smart"),
        };

        foreach (string path in Installer.OwnedPaths(_admin, includeSettings: true, Sources().Extensions))
        {
            Assert.IsTrue(Installer.IsSafeTarget(path, allowed), $"{path} is outside what the installer owns");
        }
    }

    // ------------------------------------------------------------ detection
    [TestMethod]
    public void The_installed_scope_is_found_from_where_the_program_is()
    {
        Assert.IsNull(Installer.DetectInstalledScope(_folders));

        Installer.Install(_perUser, All, new FakeHost(), Sources());
        Assert.AreEqual(InstallScope.PerUser, Installer.DetectInstalledScope(_folders));

        Installer.Install(_admin, All, new FakeHost(), Sources());
        Assert.AreEqual(InstallScope.Admin, Installer.DetectInstalledScope(_folders),
            "if both exist the machine-wide one is the one that runs");
    }
}
