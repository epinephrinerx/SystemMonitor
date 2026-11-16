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
}
