using System.IO;
using SysMonitor.Setup;

namespace SysMonitor.Tests;

/// <summary>
/// Deletion through a link.
///
/// The other installer tests check which paths get *selected*, which is not
/// the same claim as "nothing outside the two directories it owns can be
/// deleted". A junction makes a perfectly well-formed path inside a directory
/// we appear to own resolve somewhere else entirely, and the lexical check saw
/// nothing wrong with it. These build a real junction and ask.
///
/// A junction, not a symbolic link: symlinks need SeCreateSymbolicLinkPrivilege
/// and junctions need nothing at all, which is exactly why a junction is the
/// shape an unprivileged attacker would reach for.
/// </summary>
[TestClass]
public class InstallerJunctionTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateSandbox()
    {
        _root = Path.Combine(Path.GetTempPath(), "sysmon-junction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveSandbox()
    {
        try
        {
            // Delete the link before the tree, or the tree walk follows it.
            foreach (string directory in Directory.GetDirectories(_root))
            {
                // The link goes first, or deleting the tree walks through it.
                if (new DirectoryInfo(directory).LinkTarget is not null)
                {
                    Directory.Delete(directory);
                }
            }
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A sandbox left in the temp directory is not worth failing over.
        }
    }

    /// <summary>
    /// Make a directory junction. mklink is the only route that needs no
    /// privilege; the paths are ours and freshly created under a GUID, and
    /// they are quoted, so there is nothing here for cmd to reinterpret.
    /// </summary>
    private static void Junction(string link, string target)
    {
        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
        Assert.IsNotNull(process);
        process.WaitForExit(10_000);

        if (new DirectoryInfo(link).LinkTarget is null)
        {
            Assert.Inconclusive("this environment would not create a junction");
        }
    }

    [TestMethod]
    public void A_file_in_a_real_directory_we_own_is_deletable()
    {
        string owned = Path.Combine(_root, "owned");
        Directory.CreateDirectory(owned);
        string file = Path.Combine(owned, "SysMonitor.exe");
        File.WriteAllText(file, "x");

        Assert.IsTrue(Installer.IsSafeTarget(file, owned));
    }

    [TestMethod]
    public void A_file_reached_through_a_junction_is_not_deletable()
    {
        // outside\secret.txt is somebody else's file. link -> outside makes
        // link\secret.txt look like ours.
        string outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "not ours");

        string link = Path.Combine(_root, "link");
        Junction(link, outside);

        string throughLink = Path.Combine(link, "secret.txt");
        Assert.IsTrue(File.Exists(throughLink), "the junction should resolve");
        Assert.IsFalse(Installer.IsSafeTarget(throughLink, link),
            "a path whose parent redirects elsewhere must not be a delete target");
    }

    [TestMethod]
    public void A_junction_further_up_the_chain_is_caught_too()
    {
        // The link is the grandparent, not the immediate parent.
        string outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(Path.Combine(outside, "inner"));
        File.WriteAllText(Path.Combine(outside, "inner", "secret.txt"), "not ours");

        string link = Path.Combine(_root, "link");
        Junction(link, outside);

        string parent = Path.Combine(link, "inner");
        Assert.IsFalse(Installer.IsSafeTarget(Path.Combine(parent, "secret.txt"), parent),
            "a link anywhere in the chain redirects the whole path");
    }

    [TestMethod]
    public void The_real_uninstall_targets_are_still_accepted()
    {
        // The fix must not make the installer unable to remove its own files.
        string[] owned = Installer.OwnedPaths(includeSettings: true).ToArray();
        string[] parents =
        {
            Installer.InstallDir,
            Installer.SettingsDir,
            Path.GetDirectoryName(Installer.DesktopShortcut)!,
            Path.GetDirectoryName(Installer.StartMenuShortcut)!,
        };

        foreach (string path in owned)
        {
            Assert.IsTrue(Installer.IsSafeTarget(path, parents),
                $"{path} is one of ours and should still be removable");
        }
    }
}
