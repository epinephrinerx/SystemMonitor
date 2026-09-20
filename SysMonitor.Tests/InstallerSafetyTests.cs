using System.IO;
using SysMonitor.Setup;

namespace SysMonitor.Tests;

/// <summary>
/// What the uninstaller is allowed to delete.
///
/// These assert on path selection only. Nothing here installs, deletes, or
/// touches the registry.
/// </summary>
[TestClass]
public class InstallerSafetyTests
{
    private static string Parent => Installer.InstallDir;

    [TestMethod]
    public void A_file_inside_the_install_directory_is_deletable()
    {
        Assert.IsTrue(Installer.IsSafeTarget(
            Path.Combine(Parent, "SysMonitor.exe"), Parent));
    }

    [TestMethod]
    public void The_install_directory_itself_is_not_a_delete_target()
    {
        Assert.IsFalse(Installer.IsSafeTarget(Parent, Parent));
    }

    [TestMethod]
    public void A_drive_root_is_never_a_delete_target()
    {
        Assert.IsFalse(Installer.IsSafeTarget(@"C:\", @"C:\"));
        Assert.IsFalse(Installer.IsSafeTarget(@"C:\", Parent));
    }

    [TestMethod]
    public void A_relative_path_is_rejected()
    {
        Assert.IsFalse(Installer.IsSafeTarget("SysMonitor.exe", Parent));
        Assert.IsFalse(Installer.IsSafeTarget(@"..\SysMonitor.exe", Parent));
    }

    [TestMethod]
    public void A_path_that_escapes_the_parent_is_rejected()
    {
        // Resolves above the install directory, so it is not ours to remove.
        string escape = Path.Combine(Parent, "..", "..", "something.exe");
        Assert.IsFalse(Installer.IsSafeTarget(escape, Parent));
    }

    [TestMethod]
    public void A_file_in_a_subdirectory_is_rejected()
    {
        // Only the files we put there directly, never a tree walk.
        Assert.IsFalse(Installer.IsSafeTarget(
            Path.Combine(Parent, "plugins", "thing.dll"), Parent));
    }

    [TestMethod]
    public void A_file_in_an_unrelated_directory_is_rejected()
    {
        Assert.IsFalse(Installer.IsSafeTarget(@"C:\Windows\System32\kernel32.dll", Parent));
    }

    [TestMethod]
    public void Owned_paths_are_files_and_never_a_directory()
    {
        string[] owned = Installer.OwnedPaths(includeSettings: true).ToArray();

        CollectionAssert.DoesNotContain(owned, Installer.InstallDir);
        CollectionAssert.DoesNotContain(owned, Installer.SettingsDir);
        foreach (string path in owned)
        {
            Assert.IsTrue(Path.IsPathFullyQualified(path), path);
            Assert.IsFalse(string.IsNullOrEmpty(Path.GetFileName(path)), path);
        }
    }

    [TestMethod]
    public void Settings_are_left_alone_unless_asked_for()
    {
        string[] kept = Installer.OwnedPaths(includeSettings: false).ToArray();
        Assert.IsFalse(kept.Any(p => p.StartsWith(Installer.SettingsDir,
                                                  StringComparison.OrdinalIgnoreCase)));

        string[] removed = Installer.OwnedPaths(includeSettings: true).ToArray();
        Assert.IsTrue(removed.Any(p => p.EndsWith("config.wpf.json",
                                                  StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void This_build_installs_beside_the_python_one_not_over_it()
    {
        // Both are called SysMonitor and both install per-user. If they shared
        // a directory, installing this one would overwrite a working
        // application; if they shared a Run value or an uninstall key, either
        // uninstaller would claim the other's.
        string python = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "SysMonitor");

        Assert.AreNotEqual(python.ToLowerInvariant(),
                           Installer.InstallDir.ToLowerInvariant());
        Assert.AreNotEqual("SysMonitor", Installer.Key);
        Assert.IsFalse(Installer.DesktopShortcut.EndsWith(@"\SysMonitor.lnk",
                                                          StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(Installer.StartMenuShortcut.EndsWith(@"\SysMonitor.lnk",
                                                            StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void The_installer_stays_out_of_the_python_build_files()
    {
        // Both builds keep their settings in the same directory. Uninstalling
        // the C# one must not take config.json or sysmonitor.log with it.
        string[] owned = Installer.OwnedPaths(includeSettings: true)
                                  .Select(Path.GetFileName).ToArray()!;
        CollectionAssert.DoesNotContain(owned, "config.json");
        CollectionAssert.DoesNotContain(owned, "sysmonitor.log");
    }
}
