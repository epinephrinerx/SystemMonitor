using SysMonitor.Setup;

namespace SysMonitor.Tests;

/// <summary>
/// Finding and removing the retired Python build.
///
/// The two install side by side on purpose -- different directories, different
/// Run values, different uninstall keys -- which is right, and also means a
/// machine with both starts two widgets every morning with the older window on
/// top. It took a process list to establish that a screen "reverted to version
/// 2.0" was version 2.0, still installed and still starting itself.
/// </summary>
[TestClass]
public class LegacyBuildTests
{
    [TestMethod]
    public void A_quoted_command_splits_into_program_and_arguments()
    {
        (string exe, string args) = Legacy.Split(
            "\"C:\\Users\\x\\AppData\\Local\\Programs\\SysMonitor\\uninstall.exe\" --uninstall /S");

        Assert.AreEqual(@"C:\Users\x\AppData\Local\Programs\SysMonitor\uninstall.exe", exe);
        Assert.AreEqual("--uninstall /S", args);
    }

    [TestMethod]
    public void A_quoted_command_with_no_arguments_still_splits()
    {
        (string exe, string args) = Legacy.Split("\"C:\\Program Files\\App\\uninstall.exe\"");

        Assert.AreEqual(@"C:\Program Files\App\uninstall.exe", exe);
        Assert.AreEqual(string.Empty, args);
    }

    [TestMethod]
    public void An_unquoted_command_splits_at_the_first_space()
    {
        (string exe, string args) = Legacy.Split("uninstall.exe --uninstall");

        Assert.AreEqual("uninstall.exe", exe);
        Assert.AreEqual("--uninstall", args);
    }

    [TestMethod]
    public void A_bare_program_name_has_no_arguments()
    {
        (string exe, string args) = Legacy.Split("uninstall.exe");

        Assert.AreEqual("uninstall.exe", exe);
        Assert.AreEqual(string.Empty, args);
    }

    [TestMethod]
    public void Whitespace_around_the_command_is_not_part_of_the_path()
    {
        (string exe, _) = Legacy.Split("   \"C:\\x\\uninstall.exe\" --uninstall  ");
        Assert.AreEqual(@"C:\x\uninstall.exe", exe);
    }

    [TestMethod]
    public void Looking_for_it_never_throws_whatever_the_machine_holds()
    {
        // On this machine it may or may not be installed; either answer is
        // correct and neither may be an exception, because this runs inside
        // the installer before anything else has happened.
        Legacy.Install? found = Legacy.Find();
        if (found is not null)
        {
            Assert.AreNotEqual(string.Empty, found.UninstallCommand);
        }
    }

    [TestMethod]
    public void The_two_builds_do_not_share_a_single_registry_name()
    {
        // Sharing any of these would mean one uninstaller removing the other's
        // working installation.
        Assert.AreEqual("SysMonitor.NET", Installer.Key);
        Assert.AreNotEqual("SysMonitor", Installer.Key);
    }
}
