using System.Reflection;

namespace SysMonitor.Tests;

/// <summary>
/// The version the installer reports.
///
/// It used to be a `const string` written by hand, and it went stale: the two
/// project files were bumped to 3.2.0 and this stayed at 3.1.0, so the wizard
/// offered to "replace version 3.2.0 with version 3.1.0" and Add/Remove
/// Programs recorded the older number against the newer build. Nobody notices
/// a wrong version until they are trying to work out which one they have.
/// </summary>
[TestClass]
public class InstallerVersionTests
{
    private static Assembly Setup =>
        typeof(SysMonitor.Setup.Installer).Assembly;

    [TestMethod]
    public void It_matches_the_assembly_it_ships_in()
    {
        Version? assembly = Setup.GetName().Version;
        Assert.IsNotNull(assembly);

        Assert.AreEqual($"{assembly!.Major}.{assembly.Minor}.{assembly.Build}",
                        SysMonitor.Setup.Installer.Version);
    }

    [TestMethod]
    public void It_is_three_parts_with_no_revision()
    {
        // The release tags and the file names are three-part, so a fourth
        // component here would not line up with either.
        Assert.AreEqual(3, SysMonitor.Setup.Installer.Version.Split('.').Length);
    }

    [TestMethod]
    public void The_app_and_the_installer_are_released_together()
    {
        // They are bumped as a pair and shipped as a pair; a mismatch means
        // one of the two project files was missed.
        Assert.AreEqual(Updater.Current.ToString(), SysMonitor.Setup.Installer.Version);
    }
}
