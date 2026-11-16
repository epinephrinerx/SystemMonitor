using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SysMonitor.Tests;

/// <summary>
/// R-022 phase 3: how the app itself behaves when it was installed machine-wide.
/// Everything here runs against temporary folders; no prompt is raised, no task
/// is touched and nothing is written to Program Files or ProgramData.
/// </summary>
[TestClass]
public class MachineWideAppTests
{
    private string _root = "";
    private string _programFiles = "";
    private string _installDir = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "sysmon-r022app-" + Guid.NewGuid().ToString("N"));
        _programFiles = Path.Combine(_root, "Program Files");
        _installDir = Path.Combine(_programFiles, "SystemMonitor");
        Directory.CreateDirectory(_installDir);
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

    private void Marker(string content) =>
        File.WriteAllText(Path.Combine(_installDir, InstallMode.MarkerName), content);

    // --------------------------------------------------------- where it runs
    [TestMethod]
    public void Only_Program_Files_SystemMonitor_itself_counts_as_the_machine_wide_place()
    {
        Assert.IsTrue(InstallMode.IsProgramFilesInstall(_installDir, _programFiles));
        Assert.IsTrue(InstallMode.IsProgramFilesInstall(_installDir + @"\", _programFiles + @"\"));
        Assert.IsTrue(InstallMode.IsProgramFilesInstall(_installDir.ToUpperInvariant(), _programFiles));

        Assert.IsFalse(InstallMode.IsProgramFilesInstall(_programFiles, _programFiles));
        Assert.IsFalse(InstallMode.IsProgramFilesInstall(Path.Combine(_installDir, "extensions"), _programFiles));
        Assert.IsFalse(InstallMode.IsProgramFilesInstall(_installDir + "2", _programFiles));
        Assert.IsFalse(InstallMode.IsProgramFilesInstall(Path.Combine(_root, "Local", "Programs", "SystemMonitor"), _programFiles));
        Assert.IsFalse(InstallMode.IsProgramFilesInstall(@"SystemMonitor", _programFiles));
        Assert.IsFalse(InstallMode.IsProgramFilesInstall("", _programFiles));
        Assert.IsFalse(InstallMode.IsProgramFilesInstall(_installDir, ""));
    }

    [TestMethod]
    public void The_marker_is_honoured_in_program_files_and_only_there()
    {
        Marker("{\"scope\": \"admin\"}\n");
        Assert.IsTrue(InstallMode.IsMachineWide(_installDir, _programFiles));

        string perUser = Path.Combine(_root, "Local", "Programs", "SystemMonitor");
        Directory.CreateDirectory(perUser);
        File.WriteAllText(Path.Combine(perUser, InstallMode.MarkerName), "{\"scope\": \"admin\"}");
        Assert.IsFalse(InstallMode.IsMachineWide(perUser, _programFiles),
            "a marker a standard user could have written proves nothing");
    }

    [TestMethod]
    public void A_missing_odd_or_oversized_marker_means_not_machine_wide()
    {
        Assert.IsFalse(InstallMode.IsMachineWide(_installDir, _programFiles), "no marker");

        foreach (string content in new[] { "", "{}", "not json", "[\"admin\"]", "{\"scope\": \"user\"}",
                                           "{\"scope\": 1}", "{\"scope\": \"ADMIN\"}" })
        {
            Marker(content);
            Assert.IsFalse(InstallMode.IsMachineWide(_installDir, _programFiles), $"'{content}'");
        }

        Marker("{\"scope\": \"admin\"}" + new string(' ', 2000));
        Assert.IsFalse(InstallMode.IsMachineWide(_installDir, _programFiles), "a real marker is a few bytes");
    }

    [TestMethod]
    public void The_marker_the_setup_writes_is_the_marker_the_app_accepts()
    {
        var folders = new SysMonitor.Setup.KnownFolders(
            Path.Combine(_root, "L"), Path.Combine(_root, "R"), _programFiles,
            Path.Combine(_root, "D"), Path.Combine(_root, "P"),
            Path.Combine(_root, "CD"), Path.Combine(_root, "CP"));
        foreach (string dir in new[] { folders.LocalAppData, folders.AppData, folders.Desktop,
                                       folders.Programs, folders.CommonDesktop, folders.CommonPrograms })
        {
            Directory.CreateDirectory(dir);
        }
        string setup = Path.Combine(_root, "setup.exe");
        File.WriteAllText(setup, "SETUP");
        var sources = new SysMonitor.Setup.InstallSources(
            () => new MemoryStream(new byte[] { 1 }), setup, Array.Empty<SysMonitor.Setup.PayloadFile>());
        var host = new RecordingHost();

        var admin = SysMonitor.Setup.InstallLayout.Create(SysMonitor.Setup.InstallScope.Admin, folders);
        SysMonitor.Setup.Installer.Install(admin, new SysMonitor.Setup.InstallOptions(false, false, false), host, sources);
        Assert.IsTrue(InstallMode.IsMachineWide(admin.InstallDir, _programFiles));

        var perUser = SysMonitor.Setup.InstallLayout.Create(SysMonitor.Setup.InstallScope.PerUser, folders);
        SysMonitor.Setup.Installer.Install(perUser, new SysMonitor.Setup.InstallOptions(false, false, false), host, sources);
        Assert.IsFalse(InstallMode.IsMachineWide(perUser.InstallDir, _programFiles));
    }

    private sealed class RecordingHost : SysMonitor.Setup.IInstallHost
    {
        public bool IsElevated => true;
        public void StopRunningApp(string exePath) { }
        public void WriteShortcut(bool wanted, string linkPath, string target, string workingDirectory) { }
        public void RegisterUninstall(SysMonitor.Setup.InstallLayout layout, int estimatedSizeKb) { }
        public void UnregisterUninstall(SysMonitor.Setup.InstallLayout layout) { }
        public void SetAutostart(SysMonitor.Setup.InstallLayout layout, bool enabled) { }
        public void RemoveAutostart(SysMonitor.Setup.InstallLayout layout) { }
        public bool IsAutostartEnabled(SysMonitor.Setup.InstallLayout layout) => false;
    }

    // --------------------------------------------------------- the relaunch
    [TestMethod]
    public void A_machine_wide_copy_without_rights_asks_for_them_and_nothing_else_does()
    {
        Assert.IsTrue(InstallMode.ShouldRelaunchElevated(machineWide: true, isElevated: false, Array.Empty<string>()));

        Assert.IsFalse(InstallMode.ShouldRelaunchElevated(true, true, Array.Empty<string>()), "already elevated: no loop");
        Assert.IsFalse(InstallMode.ShouldRelaunchElevated(false, false, Array.Empty<string>()),
            "a per-user install never asks for UAC");
        Assert.IsFalse(InstallMode.ShouldRelaunchElevated(false, true, Array.Empty<string>()));
    }

    [TestMethod]
    public void The_no_elevate_switch_keeps_the_copy_as_it_is_whatever_its_case()
    {
        Assert.IsFalse(InstallMode.ShouldRelaunchElevated(true, false, new[] { "--no-elevate" }));
        Assert.IsFalse(InstallMode.ShouldRelaunchElevated(true, false, new[] { "--full", "--NO-ELEVATE" }));
        Assert.IsTrue(InstallMode.ShouldRelaunchElevated(true, false, new[] { "--full" }));
    }

    [TestMethod]
    public void A_running_copy_is_seen_without_taking_its_lock()
    {
        string name = @"Local\SystemMonitor.Test." + Guid.NewGuid().ToString("N");

        Assert.IsFalse(SingleInstance.IsRunning(name));
        using SingleInstance? first = SingleInstance.TryAcquire(name, TimeSpan.Zero);
        Assert.IsNotNull(first);

        Assert.IsTrue(SingleInstance.IsRunning(name));

        // Looking neither took the lock nor released it: another thread still
        // cannot get it while the first copy holds it, and can once it lets go.
        SingleInstance? other = null;
        var thread = new Thread(() => other = SingleInstance.TryAcquire(name, TimeSpan.Zero));
        thread.Start();
        thread.Join();
        Assert.IsNull(other);
        first!.Dispose();
        Assert.IsFalse(SingleInstance.IsRunning(name));
    }

    [TestMethod]
    public void A_restart_waits_for_the_old_copy_to_close_before_deciding()
    {
        string name = @"Local\SystemMonitor.Test." + Guid.NewGuid().ToString("N");
        Assert.IsTrue(SingleInstance.WaitUntilGone(name, TimeSpan.Zero), "nothing running: no wait");

        SingleInstance? old = SingleInstance.TryAcquire(name, TimeSpan.Zero);
        Assert.IsNotNull(old);
        Assert.IsFalse(SingleInstance.WaitUntilGone(name, TimeSpan.FromMilliseconds(250)), "still running");

        var closing = new Thread(() =>
        {
            Thread.Sleep(300);
            old!.Dispose();
        });
        closing.Start();
        Assert.IsTrue(SingleInstance.WaitUntilGone(name, TimeSpan.FromSeconds(10)));
        closing.Join();
    }

    [TestMethod]
    public void A_copy_started_by_the_elevated_relaunch_does_not_ask_again()
    {
        Assert.IsFalse(InstallMode.ShouldRelaunchElevated(true, false,
            new[] { "--x", InstallMode.ElevationAttemptedSwitch.ToUpperInvariant() }));
    }

    // ---------------------------------------------------- start-up by task
    private const string TaskXml = @"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><Enabled>{0}</Enabled></Settings>
</Task>";

    [TestMethod]
    public void The_task_definition_says_whether_start_up_is_on()
    {
        Assert.AreEqual(true, MachineWideStartup.ParseEnabled(string.Format(TaskXml, "true")));
        Assert.AreEqual(false, MachineWideStartup.ParseEnabled(string.Format(TaskXml, "false")));
        Assert.AreEqual(true, MachineWideStartup.ParseEnabled(
            @"<Task xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task""><Settings/></Task>"),
            "the schema default is enabled");
        Assert.IsNull(MachineWideStartup.ParseEnabled("ERROR: The system cannot find the file specified."));
        Assert.IsNull(MachineWideStartup.ParseEnabled(""));
    }

    [TestMethod]
    public void The_start_up_task_commands_are_argument_lists_for_the_one_task_name()
    {
        CollectionAssert.AreEqual(new[] { "/Query", "/TN", "SystemMonitor", "/XML" },
            MachineWideStartup.QueryArguments().ToArray());
        CollectionAssert.AreEqual(new[] { "/Change", "/TN", "SystemMonitor", "/ENABLE" },
            MachineWideStartup.ChangeArguments(true).ToArray());
        CollectionAssert.AreEqual(new[] { "/Change", "/TN", "SystemMonitor", "/DISABLE" },
            MachineWideStartup.ChangeArguments(false).ToArray());
    }

    [TestMethod]
    public void The_task_name_is_the_one_the_setup_registers()
    {
        Assert.AreEqual(SysMonitor.Setup.Installer.Key, MachineWideStartup.TaskName);
    }

    // ------------------------------------------------- elevated update stage
    [TestMethod]
    public void An_ordinary_copy_still_downloads_into_the_temp_folder()
    {
        string temp = Path.Combine(_root, "Temp");

        Assert.AreEqual(temp, Updater.StagingDirectory(elevated: false, temp, Path.Combine(_root, "ProgramData")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "ProgramData")), "nothing is created for it");
    }

    [TestMethod]
    public void The_elevated_staging_directory_is_open_to_administrators_and_system_only()
    {
        DirectorySecurity security = AdminStaging.AdminOnlySecurity();

        Assert.IsTrue(security.AreAccessRulesProtected);
        SecurityIdentifier[] named = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().Select(r => (SecurityIdentifier)r.IdentityReference).ToArray();
        CollectionAssert.AreEquivalent(
            new[]
            {
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            },
            named);
    }

    [TestMethod]
    public void A_folder_with_a_matching_name_that_is_not_ours_is_left_alone()
    {
        // Standard users can create folders in ProgramData: the name alone proves nothing.
        string parent = Path.Combine(_root, "ProgramData");
        string planted = Path.Combine(parent, Updater.StagingPrefix + "planted");
        Directory.CreateDirectory(planted);

        AdminStaging.RemoveOld(parent, Updater.StagingPrefix, isOurs: _ => false);

        Assert.IsTrue(Directory.Exists(planted));
    }

    [TestMethod]
    public void Old_staging_directories_are_cleared_and_other_folders_are_left_alone()
    {
        string parent = Path.Combine(_root, "ProgramData");
        string old1 = Path.Combine(parent, Updater.StagingPrefix + "aaa");
        string old2 = Path.Combine(parent, Updater.StagingPrefix + "bbb");
        string other = Path.Combine(parent, "SystemMonitor");
        foreach (string dir in new[] { old1, old2, other })
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(Path.Combine(old1, "SystemMonitor-Setup-4.0.0.exe"), "old");
        File.WriteAllText(Path.Combine(other, "approved-extensions.json"), "{}");

        AdminStaging.RemoveOld(parent, Updater.StagingPrefix, isOurs: _ => true);

        Assert.IsFalse(Directory.Exists(old1));
        Assert.IsFalse(Directory.Exists(old2));
        Assert.IsTrue(File.Exists(Path.Combine(other, "approved-extensions.json")),
            "the approvals store is not a staging directory");
    }
}
