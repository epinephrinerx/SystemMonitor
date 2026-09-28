namespace SysMonitor.Tests;

/// <summary>
/// The update check's judgement calls, tested without the network: what counts
/// as a version, and what counts as a URL we are willing to download an
/// executable from.
///
/// That second one is the part worth guarding. The release metadata arrives
/// over the wire, so the download URL inside it is not ours by definition, and
/// what we do with it is run it.
/// </summary>
[TestClass]
public class UpdaterTests
{
    [TestMethod]
    public void A_tag_is_read_with_or_without_its_v()
    {
        Assert.AreEqual(new Version(3, 2, 1), Updater.ParseTag("v3.2.1"));
        Assert.AreEqual(new Version(3, 2, 1), Updater.ParseTag("3.2.1"));
        Assert.AreEqual(new Version(3, 2, 0), Updater.ParseTag("V3.2"));
        Assert.AreEqual(new Version(3, 2, 1), Updater.ParseTag("  v3.2.1 "));
    }

    [TestMethod]
    public void Something_that_is_not_a_version_is_not_guessed_at()
    {
        Assert.IsNull(Updater.ParseTag("latest"));
        Assert.IsNull(Updater.ParseTag(""));
        Assert.IsNull(Updater.ParseTag("v"));
        Assert.IsNull(Updater.ParseTag("release-2024"));
    }

    [TestMethod]
    public void Versions_compare_the_way_a_release_order_needs()
    {
        Assert.IsTrue(Updater.ParseTag("v3.2.1") > Updater.ParseTag("v3.2.0"));
        Assert.IsTrue(Updater.ParseTag("v3.10.0") > Updater.ParseTag("v3.9.0"),
            "ten comes after nine, which string comparison would get wrong");
        Assert.IsTrue(Updater.ParseTag("v4.0.0") > Updater.ParseTag("v3.99.99"));
    }

    [TestMethod]
    public void Only_github_over_https_may_hand_us_an_executable()
    {
        Assert.IsTrue(Updater.IsAllowed(
            "https://github.com/epinephrinerx/SystemMonitor/releases/download/v3.2.1/SysMonitor-Setup-3.2.1.exe"));
        Assert.IsTrue(Updater.IsAllowed(
            "https://objects.githubusercontent.com/github-production-release-asset/1/2"));
    }

    [TestMethod]
    public void Anywhere_else_is_refused()
    {
        Assert.IsFalse(Updater.IsAllowed("http://github.com/x/y/z.exe"), "plain http");
        Assert.IsFalse(Updater.IsAllowed("https://example.com/SysMonitor-Setup-9.9.9.exe"));
        Assert.IsFalse(Updater.IsAllowed("https://github.com.evil.test/x.exe"),
            "a host that merely begins with ours");
        Assert.IsFalse(Updater.IsAllowed("https://evil.test/?x=github.com"));
        Assert.IsFalse(Updater.IsAllowed("file:///C:/Windows/System32/cmd.exe"));
        Assert.IsFalse(Updater.IsAllowed("not a url"));
        Assert.IsFalse(Updater.IsAllowed(string.Empty));
    }

    [TestMethod]
    public void This_build_knows_its_own_version()
    {
        Assert.IsTrue(Updater.Current > new Version(0, 0, 0));
        Assert.AreEqual(3, Updater.Current.ToString().Split('.').Length,
            "release tags carry three parts, so the assembly's fourth must not creep in");
    }
}
