using System.IO;
using SysMonitor.Setup;

namespace SysMonitor.Tests;

/// <summary>R-022 round-2 review: the setup's own file, and the relaunch marker.</summary>
[TestClass]
public class SetupHardeningTests
{
    [TestMethod]
    public void A_held_setup_file_cannot_be_renamed_or_replaced_but_can_be_copied()
    {
        string dir = Path.Combine(Path.GetTempPath(), "SysMon-self-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "setup.exe");
            File.WriteAllText(file, "GENUINE");

            using FileStream? held = SelfImage.Hold(file);

            Assert.IsNotNull(held);
            Assert.ThrowsException<IOException>(() => File.Move(file, file + ".old"));
            Assert.ThrowsException<IOException>(() => File.WriteAllText(file, "FAKE"));
            Assert.ThrowsException<IOException>(() => File.Delete(file));
            File.Copy(file, Path.Combine(dir, "copy.exe"));
            Assert.AreEqual("GENUINE", File.ReadAllText(Path.Combine(dir, "copy.exe")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void Holding_a_missing_file_gives_null_not_an_exception()
    {
        Assert.IsNull(SelfImage.Hold(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [TestMethod]
    public void The_elevated_marker_survives_the_round_trip_so_the_copy_that_comes_back_can_tell()
    {
        SetupArguments parsed = SetupArguments.Parse(new[] { "--admin", "--elevated", "/S" });

        Assert.IsTrue(parsed.Elevated);
        Assert.IsFalse(SetupArguments.Parse(new[] { "--admin" }).Elevated);
        CollectionAssert.Contains(parsed.ToArguments().ToArray(), "--elevated");
    }

    [TestMethod]
    public void A_machine_wide_install_is_refused_by_a_setup_that_did_not_lock_its_own_file()
    {
        // Nothing in the test process holds an image: HoldOwn is only called by the real setup.
        Assert.IsFalse(SelfImage.IsHeld);
        Assert.ThrowsException<IOException>(SelfImage.Require);
    }

    [TestMethod]
    public void Closing_the_elevated_wizard_without_installing_has_an_exit_code_of_its_own()
    {
        Assert.AreNotEqual(0, SetupArguments.NotInstalledExitCode, "it must not look like success");
        Assert.AreNotEqual(1, SetupArguments.NotInstalledExitCode, "nor like a failure");
    }
}
