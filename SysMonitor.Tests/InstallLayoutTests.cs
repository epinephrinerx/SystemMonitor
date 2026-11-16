using System.IO;
using Microsoft.Win32;
using SysMonitor.Setup;

namespace SysMonitor.Tests;

/// <summary>
/// R-022 phase 1: where each kind of install puts things. The layout is pure
/// data, so these run on made-up folders and touch neither the disk nor the
/// registry.
/// </summary>
[TestClass]
public class InstallLayoutTests
{
    private static readonly KnownFolders Fake = new(
        LocalAppData: @"C:\Users\tester\AppData\Local",
        AppData: @"C:\Users\tester\AppData\Roaming",
        ProgramFiles: @"C:\Program Files",
        Desktop: @"C:\Users\tester\Desktop",
        Programs: @"C:\Users\tester\AppData\Roaming\Microsoft\Windows\Start Menu\Programs",
        CommonDesktop: @"C:\Users\Public\Desktop",
        CommonPrograms: @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs");

    private static InstallLayout PerUser => InstallLayout.Create(InstallScope.PerUser, Fake);
    private static InstallLayout Admin => InstallLayout.Create(InstallScope.Admin, Fake);

    // ------------------------------------------------------------ per user
    [TestMethod]
    public void Per_user_install_lives_under_local_app_data()
    {
        Assert.AreEqual(@"C:\Users\tester\AppData\Local\Programs\SystemMonitor", PerUser.InstallDir);
        Assert.AreEqual(@"C:\Users\tester\AppData\Local\Programs\SystemMonitor\SystemMonitor.exe", PerUser.ExePath);
        Assert.AreEqual(@"C:\Users\tester\AppData\Local\Programs\SystemMonitor\uninstall.exe", PerUser.UninstallPath);
        Assert.AreEqual(@"C:\Users\tester\AppData\Local\Programs\SystemMonitor\extensions", PerUser.ExtensionsDir);
    }

    [TestMethod]
    public void Per_user_install_uses_the_users_shortcuts_registry_and_run_key_without_elevation()
    {
        Assert.AreEqual(@"C:\Users\tester\Desktop\System Monitor.lnk", PerUser.DesktopShortcut);
        Assert.AreEqual(@"C:\Users\tester\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\System Monitor.lnk",
            PerUser.StartMenuShortcut);
        Assert.AreEqual(RegistryHive.CurrentUser, PerUser.UninstallHive);
        Assert.AreEqual(AutostartMechanism.RunKey, PerUser.Autostart);
        Assert.IsFalse(PerUser.RequiresElevation, "a per-user install never asks for UAC");
    }

    [TestMethod]
    public void Per_user_layout_is_exactly_what_the_installer_uses_today()
    {
        InstallLayout real = InstallLayout.Create(InstallScope.PerUser);

        Assert.AreEqual(Installer.InstallDir, real.InstallDir);
        Assert.AreEqual(Installer.SettingsDir, real.SettingsDir);
        Assert.AreEqual(Installer.ExePath, real.ExePath);
        Assert.AreEqual(Installer.UninstallPath, real.UninstallPath);
        Assert.AreEqual(Installer.CorePath, real.CorePath);
        Assert.AreEqual(Installer.DesktopShortcut, real.DesktopShortcut);
        Assert.AreEqual(Installer.StartMenuShortcut, real.StartMenuShortcut);
    }

    [TestMethod]
    public void Per_user_layout_is_still_where_every_earlier_version_put_things()
    {
        // Written out from the folders Windows reports, not from the layout
        // itself, so it fails if the per-user place ever moves.
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        InstallLayout real = InstallLayout.Create(InstallScope.PerUser);

        Assert.AreEqual(Path.Combine(local, "Programs", "SystemMonitor"), real.InstallDir);
        Assert.AreEqual(Path.Combine(roaming, "SystemMonitor"), real.SettingsDir);
        Assert.AreEqual(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "System Monitor.lnk"),
            real.DesktopShortcut);
        Assert.AreEqual(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "System Monitor.lnk"),
            real.StartMenuShortcut);
    }

    [TestMethod]
    public void TryCreate_answers_null_instead_of_failing_when_a_folder_is_missing()
    {
        Assert.IsNull(InstallLayout.TryCreate(InstallScope.Admin, Fake with { CommonDesktop = "" }));
        Assert.IsNull(InstallLayout.TryCreate(InstallScope.PerUser, Fake with { LocalAppData = "" }));
        Assert.IsNotNull(InstallLayout.TryCreate(InstallScope.PerUser, Fake with { CommonDesktop = "" }),
            "a per-user run does not need the machine-wide folders");
        Assert.IsNotNull(InstallLayout.TryCreate(InstallScope.Admin, Fake));
    }

    // --------------------------------------------------------------- admin
    [TestMethod]
    public void Admin_install_lives_under_program_files()
    {
        Assert.AreEqual(@"C:\Program Files\SystemMonitor", Admin.InstallDir);
        Assert.AreEqual(@"C:\Program Files\SystemMonitor\SystemMonitor.exe", Admin.ExePath);
        Assert.AreEqual(@"C:\Program Files\SystemMonitor\uninstall.exe", Admin.UninstallPath);
        Assert.AreEqual(@"C:\Program Files\SystemMonitor\extensions", Admin.ExtensionsDir);
    }

    [TestMethod]
    public void Admin_install_uses_machine_shortcuts_registry_and_a_scheduled_task()
    {
        Assert.AreEqual(@"C:\Users\Public\Desktop\System Monitor.lnk", Admin.DesktopShortcut);
        Assert.AreEqual(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\System Monitor.lnk",
            Admin.StartMenuShortcut);
        Assert.AreEqual(RegistryHive.LocalMachine, Admin.UninstallHive);
        Assert.AreEqual(AutostartMechanism.ScheduledTask, Admin.Autostart,
            "an elevated program is not started from a Run value");
        Assert.AreEqual("SystemMonitor", Admin.ScheduledTaskName);
        Assert.IsTrue(Admin.RequiresElevation);
    }

    [TestMethod]
    public void Both_scopes_keep_the_users_config_in_app_data()
    {
        Assert.AreEqual(@"C:\Users\tester\AppData\Roaming\SystemMonitor", PerUser.SettingsDir);
        Assert.AreEqual(PerUser.SettingsDir, Admin.SettingsDir,
            "R-022: the config does not move when the install does");
    }

    [TestMethod]
    public void The_two_scopes_never_share_an_install_folder()
    {
        Assert.AreNotEqual(PerUser.InstallDir, Admin.InstallDir);
        Assert.IsFalse(
            Admin.InstallDir.StartsWith(Fake.LocalAppData, StringComparison.OrdinalIgnoreCase),
            "the admin install is outside any folder a standard user can write to");
        Assert.IsFalse(
            PerUser.InstallDir.StartsWith(Fake.ProgramFiles, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------- bad input
    [TestMethod]
    public void A_missing_or_relative_folder_is_refused_rather_than_guessed()
    {
        Assert.ThrowsException<ArgumentException>(
            () => InstallLayout.Create(InstallScope.Admin, Fake with { ProgramFiles = "" }));
        Assert.ThrowsException<ArgumentException>(
            () => InstallLayout.Create(InstallScope.Admin, Fake with { CommonDesktop = @"Public\Desktop" }));
        Assert.ThrowsException<ArgumentException>(
            () => InstallLayout.Create(InstallScope.PerUser, Fake with { LocalAppData = " " }));
        Assert.ThrowsException<ArgumentException>(
            () => InstallLayout.Create(InstallScope.PerUser, Fake with { AppData = @"..\Roaming" }));
    }

    [TestMethod]
    public void Each_scope_ignores_the_folders_only_the_other_one_needs()
    {
        // A per-user install on a machine where the machine-wide folders are
        // unreadable must still work, and the other way round.
        KnownFolders noMachine = Fake with { ProgramFiles = "", CommonDesktop = "", CommonPrograms = "" };
        KnownFolders noUser = Fake with { LocalAppData = "", Desktop = "", Programs = "" };

        Assert.AreEqual(PerUser.InstallDir, InstallLayout.Create(InstallScope.PerUser, noMachine).InstallDir);
        Assert.AreEqual(Admin.InstallDir, InstallLayout.Create(InstallScope.Admin, noUser).InstallDir);
    }
}
