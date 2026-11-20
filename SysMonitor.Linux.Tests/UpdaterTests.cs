using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SysMonitor.Linux;
using SysMonitor.Linux.Presentation;

namespace SysMonitor.Linux.Tests;

[TestClass]
public sealed class UpdaterTests
{
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class FakeCommandRunner : ICommandRunner
    {
        private readonly Func<string, IReadOnlyList<string>, string?>? _handler;

        public FakeCommandRunner(Func<string, IReadOnlyList<string>, string?>? handler = null)
        {
            _handler = handler;
        }

        public List<(string File, IReadOnlyList<string> Args)> Invocations { get; } = new();

        public string? Run(string file, string args, int timeoutMs) =>
            Run(file, args.Split(' ', StringSplitOptions.RemoveEmptyEntries), timeoutMs);

        public string? Run(string file, IReadOnlyList<string> args, int timeoutMs)
        {
            Invocations.Add((file, args));
            return _handler?.Invoke(file, args);
        }
    }

    private static byte[] CreateTarGz(Action<TarWriter> populate)
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            populate(writer);
        }
        return memory.ToArray();
    }

    // ------------------------------------------------------------- UpdateAssets

    [TestMethod]
    public void UpdateAssets_ArchitectureSuffix_MapsKnownArchitectures()
    {
        Assert.AreEqual("x64", UpdateAssets.ArchitectureSuffix(Architecture.X64));
        Assert.AreEqual("arm64", UpdateAssets.ArchitectureSuffix(Architecture.Arm64));
        Assert.AreEqual("arm", UpdateAssets.ArchitectureSuffix(Architecture.Arm));
        Assert.AreEqual("x86", UpdateAssets.ArchitectureSuffix(Architecture.X86));
    }

    [TestMethod]
    public void UpdateAssets_ArchiveAndChecksumNames_FormatCorrectly()
    {
        string archive = UpdateAssets.ArchiveName("4.4.0", "x64");
        string checksum = UpdateAssets.ChecksumName(archive);

        Assert.AreEqual("SystemMonitor-4.4.0-linux-x64.tar.gz", archive);
        Assert.AreEqual("SystemMonitor-4.4.0-linux-x64.tar.gz.sha256", checksum);
    }

    [TestMethod]
    public void UpdateAssets_DebNames_FormatCorrectly_And_MapsArchitecture()
    {
        Assert.AreEqual("amd64", UpdateAssets.DebArchitectureSuffix(Architecture.X64));
        Assert.AreEqual("arm64", UpdateAssets.DebArchitectureSuffix(Architecture.Arm64));
        Assert.AreEqual("armhf", UpdateAssets.DebArchitectureSuffix(Architecture.Arm));
        Assert.AreEqual("i386", UpdateAssets.DebArchitectureSuffix(Architecture.X86));

        string debX64 = UpdateAssets.DebName("4.4.0", "amd64");
        string debArm64 = UpdateAssets.DebName("4.4.0", "arm64");
        Assert.AreEqual("systemmonitor_4.4.0_amd64.deb", debX64);
        Assert.AreEqual("systemmonitor_4.4.0_arm64.deb", debArm64);
        Assert.AreEqual("systemmonitor_4.4.0_amd64.deb.sha256", UpdateAssets.ChecksumName(debX64));

        var x64Set = UpdateAssetSet.Deb("amd64");
        Assert.AreEqual("deb package (amd64)", x64Set.Describe());
        (string file64, string chk64) = x64Set.Names("4.4.0");
        Assert.AreEqual("systemmonitor_4.4.0_amd64.deb", file64);
        Assert.AreEqual("systemmonitor_4.4.0_amd64.deb.sha256", chk64);

        var arm64Set = UpdateAssetSet.Deb("arm64");
        Assert.AreEqual("deb package (arm64)", arm64Set.Describe());
        (string fileArm, string chkArm) = arm64Set.Names("4.4.0");
        Assert.AreEqual("systemmonitor_4.4.0_arm64.deb", fileArm);
        Assert.AreEqual("systemmonitor_4.4.0_arm64.deb.sha256", chkArm);
    }

    // ------------------------------------------------------------- UpdateReleaseParser

    [TestMethod]
    public void UpdateReleaseParser_TryVersion_TrimsVPrefixAndValidatesParts()
    {
        Assert.IsTrue(UpdateReleaseParser.TryVersion("v4.4.0", out string v1));
        Assert.AreEqual("4.4.0", v1);

        Assert.IsTrue(UpdateReleaseParser.TryVersion("V1.2.3", out string v2));
        Assert.AreEqual("1.2.3", v2);

        Assert.IsTrue(UpdateReleaseParser.TryVersion("5.0.12", out string v3));
        Assert.AreEqual("5.0.12", v3);

        Assert.IsFalse(UpdateReleaseParser.TryVersion(null, out _));
        Assert.IsFalse(UpdateReleaseParser.TryVersion("", out _));
        Assert.IsFalse(UpdateReleaseParser.TryVersion("v4.4", out _));
        Assert.IsFalse(UpdateReleaseParser.TryVersion("4.4.0.1", out _));
        Assert.IsFalse(UpdateReleaseParser.TryVersion("v4.4.beta", out _));
    }

    [TestMethod]
    public void UpdateReleaseParser_TryAllowedUrl_AcceptsAllowListedHttpsHostsOnly()
    {
        Assert.IsTrue(UpdateReleaseParser.TryAllowedUrl("https://github.com/releases/download/v1/a.tar.gz", out Uri? u1));
        Assert.IsNotNull(u1);

        Assert.IsTrue(UpdateReleaseParser.TryAllowedUrl("https://objects.githubusercontent.com/file", out Uri? u2));
        Assert.IsNotNull(u2);

        Assert.IsTrue(UpdateReleaseParser.TryAllowedUrl("https://release-assets.githubusercontent.com/file", out Uri? u3));
        Assert.IsNotNull(u3);

        Assert.IsFalse(UpdateReleaseParser.TryAllowedUrl("http://github.com/releases/download/v1/a.tar.gz", out _));
        Assert.IsFalse(UpdateReleaseParser.TryAllowedUrl("https://malicious.com/download/a.tar.gz", out _));
        Assert.IsFalse(UpdateReleaseParser.TryAllowedUrl("https://evilgithub.com/download/a.tar.gz", out _));
        Assert.IsFalse(UpdateReleaseParser.TryAllowedUrl("not-a-url", out _));
    }

    [TestMethod]
    public void UpdateReleaseParser_Select_ValidJson_ReturnsRelease()
    {
        string json = """
        [
          {
            "tag_name": "SystemMonitorLinux-4.4.0",
            "assets": [
              {
                "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
              },
              {
                "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
              }
            ]
          }
        ]
        """;

        (UpdateRelease? release, string? error, string note) =
            UpdateReleaseParser.Select(json, UpdateAssetSet.TarGz("x64"));

        Assert.IsNull(error);
        Assert.IsNotNull(release);
        Assert.AreEqual("4.4.0", release.Version);
        Assert.AreEqual("SystemMonitorLinux-4.4.0", release.Tag);
        Assert.AreEqual("SystemMonitor-4.4.0-linux-x64.tar.gz", release.ArchiveName);
        Assert.AreEqual("SystemMonitor-4.4.0-linux-x64.tar.gz.sha256", release.ChecksumName);
        Assert.IsTrue(note.Contains("picked SystemMonitorLinux-4.4.0"));
    }

    [TestMethod]
    public void UpdateReleaseParser_Select_MissingAssetOrChecksum_ReturnsError()
    {
        string missingChecksum = """
        [
          {
            "tag_name": "SystemMonitorLinux-4.4.0",
            "assets": [
              {
                "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
              }
            ]
          }
        ]
        """;

        (UpdateRelease? release, string? error, _) =
            UpdateReleaseParser.Select(missingChecksum, UpdateAssetSet.TarGz("x64"));
        Assert.IsNull(release);
        Assert.IsTrue(error!.Contains("no checksum asset"));

        string missingLinuxAsset = """
        [
          {
            "tag_name": "SystemMonitorLinux-4.4.0",
            "assets": [
              {
                "name": "SystemMonitor-4.4.0-win-x64.zip",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-win-x64.zip"
              }
            ]
          }
        ]
        """;

        (release, error, _) =
            UpdateReleaseParser.Select(missingLinuxAsset, UpdateAssetSet.TarGz("x64"));
        Assert.IsNull(release);
        Assert.IsTrue(error!.Contains("no linux asset"));
    }

    [TestMethod]
    public void UpdateReleaseParser_Select_DisallowedHost_ReturnsError()
    {
        string json = """
        [
          {
            "tag_name": "SystemMonitorLinux-4.4.0",
            "assets": [
              {
                "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                "browser_download_url": "https://unauthorized-domain.com/SystemMonitor-4.4.0-linux-x64.tar.gz"
              },
              {
                "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
              }
            ]
          }
        ]
        """;

        (UpdateRelease? release, string? error, _) =
            UpdateReleaseParser.Select(json, UpdateAssetSet.TarGz("x64"));
        Assert.IsNull(release);
        Assert.IsTrue(error!.Contains("not on an allowed https host"));
    }

    [TestMethod]
    public void UpdateReleaseParser_Select_IgnoresWindowsMacDraftAndPrereleaseReleases()
    {
        string json = """
        [
          {
            "tag_name": "v4.5.0",
            "draft": false,
            "prerelease": false,
            "assets": [
              {
                "name": "SystemMonitor-4.5.0-win-x64.zip",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.5.0/SystemMonitor-4.5.0-win-x64.zip"
              }
            ]
          },
          {
            "tag_name": "SystemMonitorMac-4.6.0",
            "draft": false,
            "prerelease": false,
            "assets": []
          },
          {
            "tag_name": "SystemMonitorLinux-4.7.0",
            "draft": true,
            "prerelease": false,
            "assets": []
          },
          {
            "tag_name": "SystemMonitorLinux-4.8.0",
            "draft": false,
            "prerelease": true,
            "assets": []
          },
          {
            "tag_name": "SystemMonitorLinux-4.4.0",
            "draft": false,
            "prerelease": false,
            "assets": [
              {
                "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
              },
              {
                "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
              }
            ]
          }
        ]
        """;

        (UpdateRelease? release, string? error, string note) =
            UpdateReleaseParser.Select(json, UpdateAssetSet.TarGz("x64"));

        Assert.IsNull(error);
        Assert.IsNotNull(release);
        Assert.AreEqual("4.4.0", release.Version);
        Assert.AreEqual("SystemMonitorLinux-4.4.0", release.Tag);
        Assert.IsTrue(note.Contains("picked SystemMonitorLinux-4.4.0"));
        Assert.IsTrue(note.Contains("1 prerelease, 1 draft, 2 other tag(s)"));
    }

    [TestMethod]
    public void UpdateReleaseParser_TryLinuxVersion_ValidatesFormatStrictly()
    {
        Assert.IsFalse(UpdateReleaseParser.TryLinuxVersion("v4.4.0", out _));
        Assert.IsFalse(UpdateReleaseParser.TryLinuxVersion("4.4.0-rc1", out _));
        Assert.IsFalse(UpdateReleaseParser.TryLinuxVersion("4.4.0.1", out _));
        Assert.IsFalse(UpdateReleaseParser.TryLinuxVersion("4.4", out _));
        Assert.IsFalse(UpdateReleaseParser.TryLinuxVersion("", out _));
        Assert.IsFalse(UpdateReleaseParser.TryLinuxVersion(null, out _));

        Assert.IsTrue(UpdateReleaseParser.TryLinuxVersion("4.3.1", out string v1));
        Assert.AreEqual("4.3.1", v1);
        Assert.IsTrue(UpdateReleaseParser.TryLinuxVersion("4.10.0", out string v2));
        Assert.AreEqual("4.10.0", v2);
    }

    [TestMethod]
    public void UpdateReleaseParser_Select_IgnoresV440AndRc1_AndChoosesValid431()
    {
        string json = """
        [
          {
            "tag_name": "SystemMonitorLinux-v4.4.0",
            "draft": false,
            "prerelease": false,
            "assets": [
              {
                "name": "SystemMonitor-v4.4.0-linux-x64.tar.gz",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-v4.4.0/SystemMonitor-v4.4.0-linux-x64.tar.gz"
              },
              {
                "name": "SystemMonitor-v4.4.0-linux-x64.tar.gz.sha256",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-v4.4.0/SystemMonitor-v4.4.0-linux-x64.tar.gz.sha256"
              }
            ]
          },
          {
            "tag_name": "SystemMonitorLinux-4.4.0-rc1",
            "draft": false,
            "prerelease": false,
            "assets": [
              {
                "name": "SystemMonitor-4.4.0-rc1-linux-x64.tar.gz",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0-rc1/SystemMonitor-4.4.0-rc1-linux-x64.tar.gz"
              },
              {
                "name": "SystemMonitor-4.4.0-rc1-linux-x64.tar.gz.sha256",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0-rc1/SystemMonitor-4.4.0-rc1-linux-x64.tar.gz.sha256"
              }
            ]
          },
          {
            "tag_name": "SystemMonitorLinux-4.3.1",
            "draft": false,
            "prerelease": false,
            "assets": [
              {
                "name": "SystemMonitor-4.3.1-linux-x64.tar.gz",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.3.1/SystemMonitor-4.3.1-linux-x64.tar.gz"
              },
              {
                "name": "SystemMonitor-4.3.1-linux-x64.tar.gz.sha256",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.3.1/SystemMonitor-4.3.1-linux-x64.tar.gz.sha256"
              }
            ]
          }
        ]
        """;

        (UpdateRelease? release, string? error, string note) =
            UpdateReleaseParser.Select(json, UpdateAssetSet.TarGz("x64"));

        Assert.IsNull(error);
        Assert.IsNotNull(release);
        Assert.AreEqual("4.3.1", release.Version);
        Assert.AreEqual("SystemMonitorLinux-4.3.1", release.Tag);
        Assert.AreEqual("SystemMonitor-4.3.1-linux-x64.tar.gz", release.ArchiveName);
        Assert.IsTrue(note.Contains("picked SystemMonitorLinux-4.3.1 (4.3.1) of 1 Linux release(s) seen in 3"));
        Assert.IsTrue(note.Contains("2 other tag(s)"));
    }

    [TestMethod]
    public void UpdateReleaseParser_Select_HighestVersionWins_ComparedNumerically()
    {
        Assert.IsTrue(UpdateReleaseParser.CompareVersions("4.10.0", "4.9.0") > 0);
        Assert.IsTrue(UpdateReleaseParser.CompareVersions("4.9.0", "4.10.0") < 0);
        Assert.AreEqual(0, UpdateReleaseParser.CompareVersions("4.10.0", "4.10.0"));
        Assert.IsTrue(UpdateReleaseParser.CompareVersions("5.0.0", "4.10.0") > 0);

        string json = """
        [
          {
            "tag_name": "SystemMonitorLinux-4.9.0",
            "draft": false,
            "prerelease": false,
            "assets": [
              {
                "name": "SystemMonitor-4.9.0-linux-x64.tar.gz",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.9.0/SystemMonitor-4.9.0-linux-x64.tar.gz"
              },
              {
                "name": "SystemMonitor-4.9.0-linux-x64.tar.gz.sha256",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.9.0/SystemMonitor-4.9.0-linux-x64.tar.gz.sha256"
              }
            ]
          },
          {
            "tag_name": "SystemMonitorLinux-4.10.0",
            "draft": false,
            "prerelease": false,
            "assets": [
              {
                "name": "SystemMonitor-4.10.0-linux-x64.tar.gz",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.10.0/SystemMonitor-4.10.0-linux-x64.tar.gz"
              },
              {
                "name": "SystemMonitor-4.10.0-linux-x64.tar.gz.sha256",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.10.0/SystemMonitor-4.10.0-linux-x64.tar.gz.sha256"
              }
            ]
          },
          {
            "tag_name": "SystemMonitorLinux-4.2.0",
            "draft": false,
            "prerelease": false,
            "assets": [
              {
                "name": "SystemMonitor-4.2.0-linux-x64.tar.gz",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.2.0/SystemMonitor-4.2.0-linux-x64.tar.gz"
              },
              {
                "name": "SystemMonitor-4.2.0-linux-x64.tar.gz.sha256",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.2.0/SystemMonitor-4.2.0-linux-x64.tar.gz.sha256"
              }
            ]
          }
        ]
        """;

        (UpdateRelease? release, string? error, string note) =
            UpdateReleaseParser.Select(json, UpdateAssetSet.TarGz("x64"));

        Assert.IsNull(error);
        Assert.IsNotNull(release);
        Assert.AreEqual("4.10.0", release.Version);
        Assert.AreEqual("SystemMonitorLinux-4.10.0", release.Tag);
        Assert.IsTrue(note.Contains("picked SystemMonitorLinux-4.10.0"));
    }

    [TestMethod]
    public void UpdateReleaseParser_Select_NoLinuxRelease_ReturnsNullReleaseAndNullError()
    {
        string emptyList = "[]";
        (UpdateRelease? r1, string? err1, string note1) =
            UpdateReleaseParser.Select(emptyList, UpdateAssetSet.TarGz("x64"));

        Assert.IsNull(r1);
        Assert.IsNull(err1);
        Assert.IsTrue(note1.Contains("no SystemMonitorLinux-* release among 0"));

        string windowsOnly = """
        [
          {
            "tag_name": "v4.4.0",
            "assets": []
          }
        ]
        """;
        (UpdateRelease? r2, string? err2, string note2) =
            UpdateReleaseParser.Select(windowsOnly, UpdateAssetSet.TarGz("x64"));

        Assert.IsNull(r2);
        Assert.IsNull(err2);
        Assert.IsTrue(note2.Contains("no SystemMonitorLinux-* release among 1"));
    }

    // ------------------------------------------------------------- UpdatePanelState

    [TestMethod]
    public void UpdatePanelState_IsNewer_ComparesVersionsCorrectly()
    {
        Assert.IsTrue(UpdatePanelState.IsNewer("4.4.0", "4.3.0"));
        Assert.IsTrue(UpdatePanelState.IsNewer("4.3.1", "4.3.0"));
        Assert.IsTrue(UpdatePanelState.IsNewer("v5.0.0", "4.9.9"));
        Assert.IsTrue(UpdatePanelState.IsNewer("4.10.0", "4.9.0"));

        Assert.IsFalse(UpdatePanelState.IsNewer("4.3.0", "4.3.0"));
        Assert.IsFalse(UpdatePanelState.IsNewer("4.2.9", "4.3.0"));
        Assert.IsFalse(UpdatePanelState.IsNewer("3.9.0", "4.0.0"));
    }

    [TestMethod]
    public void UpdatePanelState_Checked_TransitionsToFoundOrUpToDate()
    {
        var state = new UpdatePanelState("4.3.0");

        state.Checked("4.3.0", "4.4.0");
        Assert.AreEqual(UpdatePanelState.Phase.Found, state.State);
        Assert.AreEqual("4.4.0", state.PendingVersion);

        state.Checked("4.3.0", "4.3.0");
        Assert.AreEqual(UpdatePanelState.Phase.UpToDate, state.State);
        Assert.IsNull(state.PendingVersion);

        state.Checked("4.3.0", "4.2.0");
        Assert.AreEqual(UpdatePanelState.Phase.UpToDate, state.State);
        Assert.IsNull(state.PendingVersion);
    }

    [TestMethod]
    public void UpdatePanelState_CheckFailed_And_DownloadFailed_ClearPendingVersion()
    {
        var state = new UpdatePanelState("4.3.0");
        state.Checked("4.3.0", "4.4.0");
        Assert.AreEqual(UpdatePanelState.Phase.Found, state.State);

        state.CheckFailed();
        Assert.AreEqual(UpdatePanelState.Phase.CheckFailed, state.State);
        Assert.IsNull(state.PendingVersion);

        state.Checked("4.3.0", "4.4.0");
        state.DownloadFailed();
        Assert.AreEqual(UpdatePanelState.Phase.DownloadFailed, state.State);
        Assert.IsNull(state.PendingVersion);
    }

    [TestMethod]
    public void UpdatePanelState_ButtonLabel_And_StatusText_ReflectState()
    {
        var state = new UpdatePanelState("4.3.0");
        var lang = new Lang("en");

        Assert.AreEqual("Check for updates", state.ButtonLabel(lang));
        Assert.IsNull(state.StatusText(lang));

        state.StartCheck();
        Assert.AreEqual("Checking...", state.StatusText(lang));

        state.Checked("4.3.0", "4.4.0");
        Assert.AreEqual("Download and install", state.ButtonLabel(lang));
        Assert.AreEqual("Version 4.4.0", state.StatusText(lang));

        state.StartDownload();
        state.DownloadProgress(45);
        Assert.AreEqual("Downloading 45%", state.StatusText(lang));

        state.Installing();
        Assert.AreEqual("Starting the installer...", state.StatusText(lang));

        state.PackageOpened();
        Assert.AreEqual(UpdatePanelState.Phase.PackageOpened, state.State);
        Assert.AreEqual("Opened the package installer; confirm there", state.StatusText(lang));
        Assert.AreEqual("Check for updates", state.ButtonLabel(lang));

        state.DownloadFailed();
        Assert.AreEqual("Download failed", state.StatusText(lang));
        Assert.AreEqual("Check for updates", state.ButtonLabel(lang));
    }

    // ------------------------------------------------------------- GitHubUpdater

    [TestMethod]
    public async Task GitHubUpdater_CheckAsync_SetsHeaders_And_ParsesRelease()
    {
        string releaseJson = """
        [
          {
            "tag_name": "SystemMonitorLinux-4.4.0",
            "assets": [
              {
                "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
              },
              {
                "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
              }
            ]
          }
        ]
        """;

        var handler = new FakeHttpMessageHandler(req =>
        {
            Assert.AreEqual(UpdateReleaseParser.Endpoint, req.RequestUri!.ToString());
            Assert.AreEqual("SystemMonitor/4.3.0", req.Headers.UserAgent.ToString());
            Assert.IsTrue(req.Headers.Accept.ToString().Contains("application/vnd.github+json"));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
            };
        });

        var updater = new GitHubUpdater("4.3.0", architecture: "x64", handler: handler);
        UpdateCheck result = await updater.CheckAsync(CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("4.4.0", result.Version);
        Assert.AreEqual("SystemMonitorLinux-4.4.0", result.Tag);
        Assert.IsNotNull(updater.Release);
        Assert.AreEqual("SystemMonitor-4.4.0-linux-x64.tar.gz", updater.Release.ArchiveName);
    }

    [TestMethod]
    public async Task GitHubUpdater_CheckAsync_NoLinuxRelease_TransitionsToUpToDate()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            Assert.AreEqual(UpdateReleaseParser.Endpoint, req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        });

        var updater = new GitHubUpdater("4.3.0", architecture: "x64", handler: handler);
        UpdateCheck result = await updater.CheckAsync(CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNull(result.Version);
        Assert.IsNull(result.Reason);
        Assert.IsNull(updater.Release);
        Assert.IsTrue(result.Detail!.Contains("no SystemMonitorLinux-*"));

        var state = new UpdatePanelState("4.3.0");
        state.Checked("4.3.0", result.Version);
        Assert.AreEqual(UpdatePanelState.Phase.UpToDate, state.State);
        Assert.IsNull(state.PendingVersion);
        Assert.AreEqual("You have the newest version", state.StatusText(new Lang("en")));
    }

    [TestMethod]
    public async Task GitHubUpdater_CheckAsync_HttpError_ReturnsCheckFailed()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound));

        var updater = new GitHubUpdater("4.3.0", architecture: "x64", handler: handler);
        UpdateCheck result = await updater.CheckAsync(CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Version);
        Assert.IsTrue(result.Reason!.Contains("404"));
    }

    [TestMethod]
    public async Task GitHubUpdater_CheckAsync_Non200Response_ReturnsCheckFailed()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var updater = new GitHubUpdater("4.3.0", architecture: "x64", handler: handler);
        UpdateCheck result = await updater.CheckAsync(CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Version);
        Assert.IsTrue(result.Reason!.Contains("500"));

        var state = new UpdatePanelState("4.3.0");
        state.CheckFailed();
        Assert.AreEqual(UpdatePanelState.Phase.CheckFailed, state.State);
        Assert.AreEqual("Could not check. Try again.", state.StatusText(new Lang("en")));
    }

    [TestMethod]
    public async Task GitHubUpdater_CheckAsync_MalformedJson_ReturnsCheckFailed()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-valid-json", Encoding.UTF8, "application/json")
            });

        var updater = new GitHubUpdater("4.3.0", architecture: "x64", handler: handler);
        UpdateCheck result = await updater.CheckAsync(CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Reason!.Contains("not JSON"));

        // Also test payload that is a JSON object instead of an array
        var objectHandler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });

        var objectUpdater = new GitHubUpdater("4.3.0", architecture: "x64", handler: objectHandler);
        UpdateCheck objectResult = await objectUpdater.CheckAsync(CancellationToken.None);

        Assert.IsFalse(objectResult.Succeeded);
        Assert.IsTrue(objectResult.Reason!.Contains("not a list"));
    }

    [TestMethod]
    public async Task GitHubUpdater_CheckAsync_NewestLinuxReleaseMissingArchitectureAsset_ReturnsCheckFailed()
    {
        string json = """
        [
          {
            "tag_name": "SystemMonitorLinux-4.4.0",
            "assets": [
              {
                "name": "SystemMonitor-4.4.0-linux-arm64.tar.gz",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-arm64.tar.gz"
              },
              {
                "name": "SystemMonitor-4.4.0-linux-arm64.tar.gz.sha256",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-arm64.tar.gz.sha256"
              }
            ]
          }
        ]
        """;

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        var updater = new GitHubUpdater("4.3.0", architecture: "x64", handler: handler);
        UpdateCheck result = await updater.CheckAsync(CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Version);
        Assert.IsTrue(result.Reason!.Contains("no linux asset \"SystemMonitor-4.4.0-linux-x64.tar.gz\""));
    }

    [TestMethod]
    public void GitHubUpdater_ReadChecksum_ParsesDigestAndRejectsMalformed()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "checksum-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string hash = "a" + new string('0', 63);
            string standardFile = Path.Combine(tempDir, "file1.sha256");
            File.WriteAllText(standardFile, $"  \n{hash}  SystemMonitor-4.4.0-linux-x64.tar.gz\n");
            Assert.AreEqual(hash, GitHubUpdater.ReadChecksum(standardFile));

            string bareFile = Path.Combine(tempDir, "file2.sha256");
            File.WriteAllText(bareFile, hash + "\n");
            Assert.AreEqual(hash, GitHubUpdater.ReadChecksum(bareFile));

            string invalidFile = Path.Combine(tempDir, "file3.sha256");
            File.WriteAllText(invalidFile, "not-a-valid-sha256-digest\n");
            Assert.IsNull(GitHubUpdater.ReadChecksum(invalidFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadAndInstallAsync_HappyPath_ExtractsAndRunsInstaller()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "updater-happy-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] tarGz = CreateTarGz(writer =>
            {
                var scriptEntry = new PaxTarEntry(TarEntryType.RegularFile, "install.sh")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("#!/bin/sh\nexit 0\n"))
                };
                writer.WriteEntry(scriptEntry);
            });

            string sha256 = Convert.ToHexString(SHA256.HashData(tarGz)).ToLowerInvariant();
            string checksumPayload = $"{sha256}  SystemMonitor-4.4.0-linux-x64.tar.gz\n";

            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                  },
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                if (url.EndsWith(".tar.gz"))
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(tarGz)
                    };
                    response.Content.Headers.ContentLength = tarGz.Length;
                    return response;
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            string? startedProgram = null;
            IReadOnlyList<string>? startedArgs = null;
            var progressReports = new List<int>();
            var progress = new Progress<int>(progressReports.Add);

            var updater = new GitHubUpdater(
                currentVersion: "4.3.0",
                cacheDirectory: cacheDir,
                handler: handler,
                architecture: "x64",
                start: (prog, a) =>
                {
                    startedProgram = prog;
                    startedArgs = a;
                    return true;
                },
                ownPid: 12345);

            await updater.CheckAsync(CancellationToken.None);

            bool success = await updater.DownloadAndInstallAsync("4.4.0", progress, CancellationToken.None);

            Assert.IsTrue(success);
            Assert.AreEqual("/bin/sh", startedProgram);
            Assert.IsNotNull(startedArgs);
            Assert.AreEqual(4, startedArgs.Count);
            Assert.IsTrue(startedArgs[0].EndsWith("install.sh"));
            Assert.AreEqual("--update", startedArgs[1]);
            Assert.AreEqual("--wait-pid", startedArgs[2]);
            Assert.AreEqual("12345", startedArgs[3]);
        }
        finally
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadAndInstallAsync_ChecksumMismatch_DeletesFilesAndReturnsFalse()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "updater-mismatch-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] tarGz = CreateTarGz(writer =>
            {
                var scriptEntry = new PaxTarEntry(TarEntryType.RegularFile, "install.sh")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("#!/bin/sh\nexit 0\n"))
                };
                writer.WriteEntry(scriptEntry);
            });

            string wrongSha256 = new string('f', 64);
            string checksumPayload = $"{wrongSha256}  SystemMonitor-4.4.0-linux-x64.tar.gz\n";

            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                  },
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                if (url.EndsWith(".tar.gz"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(tarGz)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var updater = new GitHubUpdater(
                currentVersion: "4.3.0",
                cacheDirectory: cacheDir,
                handler: handler,
                architecture: "x64");

            await updater.CheckAsync(CancellationToken.None);
            bool result = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);

            Assert.IsFalse(result);
            string archivePath = Path.Combine(cacheDir, "SystemMonitor-4.4.0-linux-x64.tar.gz");
            string checksumPath = Path.Combine(cacheDir, "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256");
            Assert.IsFalse(File.Exists(archivePath), "Archive file must be deleted on checksum mismatch.");
            Assert.IsFalse(File.Exists(checksumPath), "Checksum file must be deleted on failure.");
        }
        finally
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadAndInstallAsync_MissingInstallSh_FailsAndCleansUp()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "updater-no-install-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Archive has files but lacks install.sh at its root
            byte[] tarGz = CreateTarGz(writer =>
            {
                var dummyEntry = new PaxTarEntry(TarEntryType.RegularFile, "readme.txt")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("no installer here\n"))
                };
                writer.WriteEntry(dummyEntry);
            });

            string sha256 = Convert.ToHexString(SHA256.HashData(tarGz)).ToLowerInvariant();
            string checksumPayload = $"{sha256}  SystemMonitor-4.4.0-linux-x64.tar.gz\n";

            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                  },
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                if (url.EndsWith(".tar.gz"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(tarGz)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var updater = new GitHubUpdater(
                currentVersion: "4.3.0",
                cacheDirectory: cacheDir,
                handler: handler,
                architecture: "x64");

            await updater.CheckAsync(CancellationToken.None);
            bool result = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);

            Assert.IsFalse(result);
            string targetDir = Path.Combine(cacheDir, "4.4.0");
            Assert.IsFalse(Directory.Exists(targetDir), "Target extraction directory must be deleted when install.sh is missing.");
        }
        finally
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, recursive: true);
            }
        }
    }

    // ------------------------------------------------------------- DebPackage and Deb Install

    [TestMethod]
    public void DebPackage_IsInstalled_RequiresUnderOptRoot_And_DpkgQueryInstalled()
    {
        var validRunner = new FakeCommandRunner((file, args) =>
        {
            if (file == "dpkg-query" && args.Contains("-f=${Status}") && args.Contains("systemmonitor"))
            {
                return "install ok installed\n";
            }
            return null;
        });

        // Path under /opt/systemmonitor/ and registered in dpkg -> true
        Assert.IsTrue(DebPackage.IsInstalled("/opt/systemmonitor/sysmonitor", validRunner));
        Assert.IsTrue(DebPackage.IsInstalled("/opt/systemmonitor/bin/sysmonitor", validRunner));

        // Path under /opt/systemmonitor/ but dpkg-query says not installed -> false
        var notInstalledRunner = new FakeCommandRunner((_, _) => "unknown ok not-installed\n");
        Assert.IsFalse(DebPackage.IsInstalled("/opt/systemmonitor/sysmonitor", notInstalledRunner));

        // Path under /opt/systemmonitor/ but dpkg-query fails (returns null) -> false
        var failingRunner = new FakeCommandRunner((_, _) => null);
        Assert.IsFalse(DebPackage.IsInstalled("/opt/systemmonitor/sysmonitor", failingRunner));

        // Not under /opt/systemmonitor/, even if dpkg-query says installed -> false
        Assert.IsFalse(DebPackage.IsInstalled("/home/user/apps/sysmonitor", validRunner));
        Assert.IsFalse(DebPackage.IsInstalled("/usr/local/bin/sysmonitor", validRunner));
        Assert.IsFalse(DebPackage.IsInstalled("/opt/otherapp/sysmonitor", validRunner));
        Assert.IsFalse(DebPackage.IsInstalled(null, validRunner));
        Assert.IsFalse(DebPackage.IsInstalled("", validRunner));

        // Verify with GitHubUpdater
        var updaterDeb = new GitHubUpdater(
            "4.3.0",
            processPath: "/opt/systemmonitor/sysmonitor",
            commands: validRunner);
        Assert.IsTrue(updaterDeb.DebInstall);
        Assert.IsTrue(updaterDeb.Assets.Package);
        Assert.AreEqual("deb package (amd64)", updaterDeb.Assets.Describe());

        var updaterTar = new GitHubUpdater(
            "4.3.0",
            processPath: "/home/user/sysmonitor",
            commands: validRunner,
            architecture: "x64");
        Assert.IsFalse(updaterTar.DebInstall);
        Assert.IsFalse(updaterTar.Assets.Package);
        Assert.AreEqual("tar.gz archive (x64)", updaterTar.Assets.Describe());
    }

    [TestMethod]
    public void DebPackage_ResolveDownloadDirectory_UsesXdgDownloadDir_WhenSet()
    {
        string? prev = Environment.GetEnvironmentVariable("XDG_DOWNLOAD_DIR");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DOWNLOAD_DIR", "/custom/xdg/downloads");
            var runner = new FakeCommandRunner();
            string dir = DebPackage.ResolveDownloadDirectory(runner);
            Assert.AreEqual("/custom/xdg/downloads", dir);
            Assert.AreEqual(0, runner.Invocations.Count, "xdg-user-dir must not be invoked when XDG_DOWNLOAD_DIR is set.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DOWNLOAD_DIR", prev);
        }
    }

    [TestMethod]
    public void DebPackage_ResolveDownloadDirectory_FallsBackToXdgUserDir_OrDownloadsFolder()
    {
        string? prevXdg = Environment.GetEnvironmentVariable("XDG_DOWNLOAD_DIR");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DOWNLOAD_DIR", null);

            // 1. xdg-user-dir DOWNLOAD returns a custom path
            var runnerCustom = new FakeCommandRunner((file, args) =>
                file == "xdg-user-dir" && args.Contains("DOWNLOAD") ? "/custom/user/downloads\n" : null);
            string dir1 = DebPackage.ResolveDownloadDirectory(runnerCustom);
            Assert.AreEqual("/custom/user/downloads", dir1);

            // 2. xdg-user-dir DOWNLOAD returns the user's home directory -> fallback to ~/Downloads
            string home = Environment.GetEnvironmentVariable("HOME")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var runnerHome = new FakeCommandRunner((file, args) =>
                file == "xdg-user-dir" && args.Contains("DOWNLOAD") ? home : null);
            string dir2 = DebPackage.ResolveDownloadDirectory(runnerHome);
            Assert.AreEqual(Path.Combine(home, "Downloads"), dir2);

            // 3. xdg-user-dir DOWNLOAD fails (null) -> fallback to ~/Downloads
            var runnerNull = new FakeCommandRunner((_, _) => null);
            string dir3 = DebPackage.ResolveDownloadDirectory(runnerNull);
            Assert.AreEqual(Path.Combine(home, "Downloads"), dir3);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DOWNLOAD_DIR", prevXdg);
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadAndInstallAsync_DebPath_HappyPath_DownloadsVerifiesAndOpensWithDesktop()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "updater-deb-happy-" + Guid.NewGuid().ToString("N"));
        string downloadDir = Path.Combine(Path.GetTempPath(), "updater-deb-down-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] debBytes = Encoding.UTF8.GetBytes("fake deb package binary content");
            string sha256 = Convert.ToHexString(SHA256.HashData(debBytes)).ToLowerInvariant();
            string checksumPayload = $"{sha256}  systemmonitor_4.4.0_amd64.deb\n";

            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb"
                  },
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".deb.sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                if (url.EndsWith(".deb"))
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(debBytes)
                    };
                    response.Content.Headers.ContentLength = debBytes.Length;
                    return response;
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var fakeCommands = new FakeCommandRunner((file, args) =>
            {
                if (file == "dpkg-query" && args.Contains("-f=${Status}"))
                {
                    return "install ok installed\n";
                }
                return null;
            });

            string? openedPath = null;
            bool startCalled = false;
            var progressReports = new List<int>();
            var progress = new Progress<int>(progressReports.Add);

            var updater = new GitHubUpdater(
                currentVersion: "4.3.0",
                cacheDirectory: cacheDir,
                downloadDirectory: downloadDir,
                handler: handler,
                debInstall: true,
                commands: fakeCommands,
                start: (_, _) =>
                {
                    startCalled = true;
                    return true;
                },
                open: path =>
                {
                    openedPath = path;
                    return true;
                });

            await updater.CheckAsync(CancellationToken.None);
            bool success = await updater.DownloadAndInstallAsync("4.4.0", progress, CancellationToken.None);

            Assert.IsTrue(success);
            Assert.IsTrue(updater.OpenedPackageInstaller);
            Assert.IsFalse(startCalled, "install.sh must not be started for a .deb install.");

            string expectedDestination = Path.Combine(downloadDir, "systemmonitor_4.4.0_amd64.deb");
            Assert.AreEqual(expectedDestination, openedPath);
            Assert.IsTrue(File.Exists(expectedDestination), "Downloaded deb must exist in Downloads directory.");
            CollectionAssert.AreEqual(debBytes, File.ReadAllBytes(expectedDestination));

            // Cached copies must have been deleted
            Assert.IsFalse(File.Exists(Path.Combine(cacheDir, "systemmonitor_4.4.0_amd64.deb")));
            Assert.IsFalse(File.Exists(Path.Combine(cacheDir, "systemmonitor_4.4.0_amd64.deb.sha256")));

            // Opener was called, and no privileged commands like sudo, pkexec, apt or dpkg -i were run
            foreach (var (cmd, args) in fakeCommands.Invocations)
            {
                Assert.AreNotEqual("sudo", cmd);
                Assert.AreNotEqual("pkexec", cmd);
                Assert.AreNotEqual("apt", cmd);
                Assert.AreNotEqual("apt-get", cmd);
                if (cmd == "dpkg")
                {
                    Assert.IsFalse(args.Contains("-i") || args.Contains("--install"));
                }
            }
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
            if (Directory.Exists(downloadDir)) Directory.Delete(downloadDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadAndInstallAsync_DebPath_ShaMismatch_RemovesFileAndFails()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "updater-deb-mismatch-" + Guid.NewGuid().ToString("N"));
        string downloadDir = Path.Combine(Path.GetTempPath(), "updater-deb-down-mismatch-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] debBytes = Encoding.UTF8.GetBytes("fake deb package binary content");
            string wrongSha = new string('0', 64);
            string checksumPayload = $"{wrongSha}  systemmonitor_4.4.0_amd64.deb\n";

            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb"
                  },
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".deb.sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                if (url.EndsWith(".deb"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(debBytes)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            bool openerCalled = false;
            var updater = new GitHubUpdater(
                currentVersion: "4.3.0",
                cacheDirectory: cacheDir,
                downloadDirectory: downloadDir,
                handler: handler,
                debInstall: true,
                open: _ =>
                {
                    openerCalled = true;
                    return true;
                });

            await updater.CheckAsync(CancellationToken.None);
            bool success = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);

            Assert.IsFalse(success);
            Assert.IsFalse(updater.OpenedPackageInstaller);
            Assert.IsFalse(openerCalled);

            string destination = Path.Combine(downloadDir, "systemmonitor_4.4.0_amd64.deb");
            Assert.IsFalse(File.Exists(destination));
            Assert.IsFalse(File.Exists(Path.Combine(cacheDir, "systemmonitor_4.4.0_amd64.deb")));
            Assert.IsFalse(File.Exists(Path.Combine(cacheDir, "systemmonitor_4.4.0_amd64.deb.sha256")));
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
            if (Directory.Exists(downloadDir)) Directory.Delete(downloadDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadAndInstallAsync_DebPath_ExistingMatchingShaFile_IsReused()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "updater-deb-reuse-" + Guid.NewGuid().ToString("N"));
        string downloadDir = Path.Combine(Path.GetTempPath(), "updater-deb-down-reuse-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(downloadDir);

            byte[] debBytes = Encoding.UTF8.GetBytes("fake deb package binary content for reuse");
            string sha256 = Convert.ToHexString(SHA256.HashData(debBytes)).ToLowerInvariant();
            string checksumPayload = $"{sha256}  systemmonitor_4.4.0_amd64.deb\n";

            string existingFile = Path.Combine(downloadDir, "systemmonitor_4.4.0_amd64.deb");
            File.WriteAllBytes(existingFile, debBytes);
            DateTime originalTimestamp = DateTime.UtcNow.AddHours(-2);
            File.SetLastWriteTimeUtc(existingFile, originalTimestamp);

            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb"
                  },
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".deb.sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                if (url.EndsWith(".deb"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(debBytes)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            string? openedPath = null;
            var logs = new List<string>();

            var updater = new GitHubUpdater(
                currentVersion: "4.3.0",
                cacheDirectory: cacheDir,
                downloadDirectory: downloadDir,
                handler: handler,
                debInstall: true,
                log: logs.Add,
                open: path =>
                {
                    openedPath = path;
                    return true;
                });

            await updater.CheckAsync(CancellationToken.None);
            bool success = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);

            Assert.IsTrue(success);
            Assert.AreEqual(existingFile, openedPath);
            Assert.IsTrue(File.Exists(existingFile));
            CollectionAssert.AreEqual(debBytes, File.ReadAllBytes(existingFile));
            Assert.AreEqual(originalTimestamp, File.GetLastWriteTimeUtc(existingFile), "Reused file must not be modified or overwritten.");
            Assert.IsTrue(logs.Any(l => l.Contains("reusing the package already in " + downloadDir)));

            string numberedCandidate = Path.Combine(downloadDir, "systemmonitor_4.4.0_amd64 (1).deb");
            Assert.IsFalse(File.Exists(numberedCandidate), "Numbered deb file must not be created when existing matches.");
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
            if (Directory.Exists(downloadDir)) Directory.Delete(downloadDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadAndInstallAsync_DebPath_ExistingDifferentShaFile_SavesAsNumberedDeb_AndLeavesOriginalUntouched()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "updater-deb-numbered-" + Guid.NewGuid().ToString("N"));
        string downloadDir = Path.Combine(Path.GetTempPath(), "updater-deb-down-num-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(downloadDir);

            byte[] originalBytes = Encoding.UTF8.GetBytes("pre-existing different content");
            string existingFile = Path.Combine(downloadDir, "systemmonitor_4.4.0_amd64.deb");
            File.WriteAllBytes(existingFile, originalBytes);
            DateTime originalTimestamp = DateTime.UtcNow.AddHours(-1);
            File.SetLastWriteTimeUtc(existingFile, originalTimestamp);

            byte[] newDebBytes = Encoding.UTF8.GetBytes("new deb content from remote release");
            string sha256 = Convert.ToHexString(SHA256.HashData(newDebBytes)).ToLowerInvariant();
            string checksumPayload = $"{sha256}  systemmonitor_4.4.0_amd64.deb\n";

            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb"
                  },
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".deb.sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                if (url.EndsWith(".deb"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(newDebBytes)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            string? openedPath = null;
            var updater = new GitHubUpdater(
                currentVersion: "4.3.0",
                cacheDirectory: cacheDir,
                downloadDirectory: downloadDir,
                handler: handler,
                debInstall: true,
                open: path =>
                {
                    openedPath = path;
                    return true;
                });

            await updater.CheckAsync(CancellationToken.None);
            bool success = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);

            Assert.IsTrue(success);
            string expectedNumberedFile = Path.Combine(downloadDir, "systemmonitor_4.4.0_amd64 (1).deb");
            Assert.AreEqual(expectedNumberedFile, openedPath);
            Assert.IsTrue(File.Exists(expectedNumberedFile), "New deb must be saved under numbered name … (1).deb");
            CollectionAssert.AreEqual(newDebBytes, File.ReadAllBytes(expectedNumberedFile));

            // Original must be untouched
            Assert.IsTrue(File.Exists(existingFile), "Original file must still exist.");
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(existingFile));
            Assert.AreEqual(originalTimestamp, File.GetLastWriteTimeUtc(existingFile), "Original file timestamp must be untouched.");
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
            if (Directory.Exists(downloadDir)) Directory.Delete(downloadDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadAndInstallAsync_DebPath_XdgOpenExitCode0_PackageOpened_StatusShowsOpened()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "updater-deb-code0-" + Guid.NewGuid().ToString("N"));
        string downloadDir = Path.Combine(Path.GetTempPath(), "updater-deb-down0-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] debBytes = Encoding.UTF8.GetBytes("fake deb package binary content");
            string sha256 = Convert.ToHexString(SHA256.HashData(debBytes)).ToLowerInvariant();
            string checksumPayload = $"{sha256}  systemmonitor_4.4.0_amd64.deb\n";

            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb"
                  },
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".deb.sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                if (url.EndsWith(".deb"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(debBytes)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            string? openedPath = null;
            var updater = new GitHubUpdater(
                currentVersion: "4.3.0",
                cacheDirectory: cacheDir,
                downloadDirectory: downloadDir,
                handler: handler,
                debInstall: true,
                open: path =>
                {
                    openedPath = path;
                    return true; // Simulating xdg-open exit code 0
                });

            await updater.CheckAsync(CancellationToken.None);
            bool success = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);

            Assert.IsTrue(success);
            Assert.IsTrue(updater.OpenedPackageInstaller);
            Assert.IsNull(updater.SavedPackagePath);
            Assert.AreEqual(Path.Combine(downloadDir, "systemmonitor_4.4.0_amd64.deb"), openedPath);

            var state = new UpdatePanelState("4.3.0");
            state.PackageOpened();
            Assert.AreEqual(UpdatePanelState.Phase.PackageOpened, state.State);

            var langEn = new Lang("en");
            Assert.AreEqual("Opened the package installer; confirm there", state.StatusText(langEn));
            Assert.AreEqual("Check for updates", state.ButtonLabel(langEn));

            var langTh = new Lang("th");
            Assert.AreEqual("เปิดตัวติดตั้งแพ็กเกจแล้ว ยืนยันในตัวจัดการแพ็กเกจ", state.StatusText(langTh));
            Assert.AreEqual("ตรวจหาเวอร์ชันใหม่", state.ButtonLabel(langTh));
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
            if (Directory.Exists(downloadDir)) Directory.Delete(downloadDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadAndInstallAsync_DebPath_XdgOpenExitCode3_Fails_FileKept_StatusShowsPath()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "updater-deb-openfail-" + Guid.NewGuid().ToString("N"));
        string downloadDir = Path.Combine(Path.GetTempPath(), "updater-deb-down-fail-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] debBytes = Encoding.UTF8.GetBytes("fake deb package binary content");
            string sha256 = Convert.ToHexString(SHA256.HashData(debBytes)).ToLowerInvariant();
            string checksumPayload = $"{sha256}  systemmonitor_4.4.0_amd64.deb\n";

            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb"
                  },
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".deb.sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                if (url.EndsWith(".deb"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(debBytes)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var updater = new GitHubUpdater(
                currentVersion: "4.3.0",
                cacheDirectory: cacheDir,
                downloadDirectory: downloadDir,
                handler: handler,
                debInstall: true,
                open: _ => false); // Simulating xdg-open exit code 3 (non-zero)

            await updater.CheckAsync(CancellationToken.None);
            bool success = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);

            Assert.IsFalse(success);
            Assert.IsFalse(updater.OpenedPackageInstaller);
            string destination = Path.Combine(downloadDir, "systemmonitor_4.4.0_amd64.deb");
            Assert.IsTrue(File.Exists(destination), "Destination file must be kept when xdg-open fails.");
            Assert.AreEqual(destination, updater.SavedPackagePath);

            var state = new UpdatePanelState("4.3.0");
            state.DownloadFailed(updater.SavedPackagePath);
            Assert.AreEqual(UpdatePanelState.Phase.DownloadFailed, state.State);

            var langEn = new Lang("en");
            Assert.AreEqual($"Saved the package to {destination}; open it to install", state.StatusText(langEn));
            Assert.AreEqual("Check for updates", state.ButtonLabel(langEn));

            var langTh = new Lang("th");
            Assert.AreEqual($"บันทึกแพ็กเกจไว้ที่ {destination} เปิดไฟล์นี้เพื่อติดตั้งเอง", state.StatusText(langTh));
            Assert.AreEqual("ตรวจหาเวอร์ชันใหม่", state.ButtonLabel(langTh));
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
            if (Directory.Exists(downloadDir)) Directory.Delete(downloadDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadAndInstallAsync_FailedDownload_DeletesOnlyFilesItCreated()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "updater-failed-created-" + Guid.NewGuid().ToString("N"));
        string downloadDir = Path.Combine(Path.GetTempPath(), "updater-down-failed-created-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(cacheDir);
            Directory.CreateDirectory(downloadDir);

            // 1. Pre-existing file in downloads directory
            string preExistingDownload = Path.Combine(downloadDir, "systemmonitor_4.4.0_amd64.deb");
            byte[] preExistingDownloadBytes = Encoding.UTF8.GetBytes("unrelated pre-existing download file");
            File.WriteAllBytes(preExistingDownload, preExistingDownloadBytes);

            // 2. Pre-existing unrelated file in cache directory
            string preExistingCache = Path.Combine(cacheDir, "pre-existing-cache.txt");
            byte[] preExistingCacheBytes = Encoding.UTF8.GetBytes("pre-existing cache content");
            File.WriteAllBytes(preExistingCache, preExistingCacheBytes);

            byte[] debBytes = Encoding.UTF8.GetBytes("deb content");
            string sha256 = Convert.ToHexString(SHA256.HashData(debBytes)).ToLowerInvariant();
            string checksumPayload = $"{sha256}  systemmonitor_4.4.0_amd64.deb\n";

            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb"
                  },
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".deb.sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                if (url.EndsWith(".deb"))
                {
                    // Fail the package download
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var updater = new GitHubUpdater(
                currentVersion: "4.3.0",
                cacheDirectory: cacheDir,
                downloadDirectory: downloadDir,
                handler: handler,
                debInstall: true);

            await updater.CheckAsync(CancellationToken.None);
            bool success = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);

            Assert.IsFalse(success);

            // Pre-existing files must be untouched
            Assert.IsTrue(File.Exists(preExistingDownload), "Pre-existing file in download directory must not be deleted.");
            CollectionAssert.AreEqual(preExistingDownloadBytes, File.ReadAllBytes(preExistingDownload));

            Assert.IsTrue(File.Exists(preExistingCache), "Pre-existing file in cache directory must not be deleted.");
            CollectionAssert.AreEqual(preExistingCacheBytes, File.ReadAllBytes(preExistingCache));

            // Any temporary files created by this run in cache must be deleted
            string tempChecksum = Path.Combine(cacheDir, "systemmonitor_4.4.0_amd64.deb.sha256");
            Assert.IsFalse(File.Exists(tempChecksum), "Downloaded checksum file created by this run must be deleted.");

            string tempPackage = Path.Combine(cacheDir, "systemmonitor_4.4.0_amd64.deb");
            Assert.IsFalse(File.Exists(tempPackage), "Package file created by this run must be deleted.");
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
            if (Directory.Exists(downloadDir)) Directory.Delete(downloadDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadAndInstallAsync_CancelledAfterWritingDestination_DeletesCreatedFile_AndLeavesOriginalUntouched()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "updater-cancel-dest-" + Guid.NewGuid().ToString("N"));
        string downloadDir = Path.Combine(Path.GetTempPath(), "updater-down-cancel-dest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(downloadDir);

            // Pre-existing file
            string originalFile = Path.Combine(downloadDir, "systemmonitor_4.4.0_amd64.deb");
            byte[] originalBytes = Encoding.UTF8.GetBytes("original deb content");
            File.WriteAllBytes(originalFile, originalBytes);

            byte[] newDebBytes = Encoding.UTF8.GetBytes("new deb content");
            string sha256 = Convert.ToHexString(SHA256.HashData(newDebBytes)).ToLowerInvariant();
            string checksumPayload = $"{sha256}  systemmonitor_4.4.0_amd64.deb\n";

            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb"
                  },
                  {
                    "name": "systemmonitor_4.4.0_amd64.deb.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/systemmonitor_4.4.0_amd64.deb.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".deb.sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                if (url.EndsWith(".deb"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(newDebBytes)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            // Opener throws OperationCanceledException after file was saved to destination
            var updater = new GitHubUpdater(
                currentVersion: "4.3.0",
                cacheDirectory: cacheDir,
                downloadDirectory: downloadDir,
                handler: handler,
                debInstall: true,
                open: _ => throw new OperationCanceledException());

            await updater.CheckAsync(CancellationToken.None);
            bool success = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);

            Assert.IsFalse(success);

            // The created (1).deb file must have been deleted by catch handler
            string numberedFile = Path.Combine(downloadDir, "systemmonitor_4.4.0_amd64 (1).deb");
            Assert.IsFalse(File.Exists(numberedFile), "Candidate file created in this run must be deleted on cancellation.");

            // Original file must be untouched
            Assert.IsTrue(File.Exists(originalFile), "Original pre-existing file must remain untouched.");
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(originalFile));
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
            if (Directory.Exists(downloadDir)) Directory.Delete(downloadDir, recursive: true);
        }
    }

    // ------------------------------------------------------------- TarExtractor

    [TestMethod]
    public void TarExtractor_Extract_ValidArchive_ExtractsSuccessfully()
    {
        string destDir = Path.Combine(Path.GetTempPath(), "tar-extract-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(Path.GetTempPath(), "test-" + Guid.NewGuid().ToString("N") + ".tar.gz");
        try
        {
            byte[] tarGz = CreateTarGz(writer =>
            {
                var dir = new PaxTarEntry(TarEntryType.Directory, "sub/");
                writer.WriteEntry(dir);

                var file = new PaxTarEntry(TarEntryType.RegularFile, "sub/file.txt")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("hello from tar"))
                };
                writer.WriteEntry(file);
            });

            File.WriteAllBytes(archivePath, tarGz);

            bool ok = TarExtractor.Extract(archivePath, destDir, out string? error);

            Assert.IsTrue(ok, error);
            Assert.IsNull(error);
            Assert.IsTrue(File.Exists(Path.Combine(destDir, "sub", "file.txt")));
            Assert.AreEqual("hello from tar", File.ReadAllText(Path.Combine(destDir, "sub", "file.txt")));
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [TestMethod]
    public void TarExtractor_Extract_RejectsAbsolutePath()
    {
        string destDir = Path.Combine(Path.GetTempPath(), "tar-abs-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(Path.GetTempPath(), "test-abs-" + Guid.NewGuid().ToString("N") + ".tar.gz");
        try
        {
            byte[] tarGz = CreateTarGz(writer =>
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, "/etc/malicious.txt")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("evil"))
                };
                writer.WriteEntry(entry);
            });

            File.WriteAllBytes(archivePath, tarGz);

            bool ok = TarExtractor.Extract(archivePath, destDir, out string? error);

            Assert.IsFalse(ok);
            Assert.IsTrue(error!.Contains("absolute"));
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [TestMethod]
    public void TarExtractor_Extract_RejectsDotDotEntry()
    {
        string destDir = Path.Combine(Path.GetTempPath(), "tar-dotdot-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(Path.GetTempPath(), "test-dotdot-" + Guid.NewGuid().ToString("N") + ".tar.gz");
        try
        {
            byte[] tarGz = CreateTarGz(writer =>
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, "folder/../../escaped.txt")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("evil"))
                };
                writer.WriteEntry(entry);
            });

            File.WriteAllBytes(archivePath, tarGz);

            bool ok = TarExtractor.Extract(archivePath, destDir, out string? error);

            Assert.IsFalse(ok);
            Assert.IsTrue(error!.Contains(".."));
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [TestMethod]
    public void TarExtractor_Extract_RejectsSymbolicLinkEntry()
    {
        string destDir = Path.Combine(Path.GetTempPath(), "tar-symlink-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(Path.GetTempPath(), "test-symlink-" + Guid.NewGuid().ToString("N") + ".tar.gz");
        try
        {
            byte[] tarGz = CreateTarGz(writer =>
            {
                var file = new PaxTarEntry(TarEntryType.RegularFile, "target.txt")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("target"))
                };
                writer.WriteEntry(file);
                var link = new PaxTarEntry(TarEntryType.SymbolicLink, "symlink.txt")
                {
                    LinkName = "target.txt"
                };
                writer.WriteEntry(link);
            });

            File.WriteAllBytes(archivePath, tarGz);

            bool ok = TarExtractor.Extract(archivePath, destDir, out string? error);

            Assert.IsFalse(ok);
            Assert.IsTrue(error!.Contains("links are not allowed") || error.Contains("SymbolicLink"));
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [TestMethod]
    public void TarExtractor_Extract_RejectsHardLinkEntry()
    {
        string destDir = Path.Combine(Path.GetTempPath(), "tar-hardlink-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(Path.GetTempPath(), "test-hardlink-" + Guid.NewGuid().ToString("N") + ".tar.gz");
        try
        {
            byte[] tarGz = CreateTarGz(writer =>
            {
                var file = new PaxTarEntry(TarEntryType.RegularFile, "target.txt")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("target"))
                };
                writer.WriteEntry(file);
                var link = new PaxTarEntry(TarEntryType.HardLink, "hardlink.txt")
                {
                    LinkName = "target.txt"
                };
                writer.WriteEntry(link);
            });

            File.WriteAllBytes(archivePath, tarGz);

            bool ok = TarExtractor.Extract(archivePath, destDir, out string? error);

            Assert.IsFalse(ok);
            Assert.IsTrue(error!.Contains("links are not allowed") || error.Contains("HardLink"));
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [TestMethod]
    public void TarExtractor_Extract_RejectsCharacterDeviceEntry()
    {
        string destDir = Path.Combine(Path.GetTempPath(), "tar-chardev-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(Path.GetTempPath(), "test-chardev-" + Guid.NewGuid().ToString("N") + ".tar.gz");
        try
        {
            byte[] tarGz = CreateTarGz(writer =>
            {
                var chardev = new PaxTarEntry(TarEntryType.CharacterDevice, "dev_char");
                writer.WriteEntry(chardev);
            });

            File.WriteAllBytes(archivePath, tarGz);

            bool ok = TarExtractor.Extract(archivePath, destDir, out string? error);

            Assert.IsFalse(ok);
            Assert.IsTrue(error!.Contains("unsupported entry") || error.Contains("CharacterDevice"));
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [TestMethod]
    public void TarExtractor_Extract_RejectsBlockDeviceEntry()
    {
        string destDir = Path.Combine(Path.GetTempPath(), "tar-blockdev-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(Path.GetTempPath(), "test-blockdev-" + Guid.NewGuid().ToString("N") + ".tar.gz");
        try
        {
            byte[] tarGz = CreateTarGz(writer =>
            {
                var blockdev = new PaxTarEntry(TarEntryType.BlockDevice, "dev_block");
                writer.WriteEntry(blockdev);
            });

            File.WriteAllBytes(archivePath, tarGz);

            bool ok = TarExtractor.Extract(archivePath, destDir, out string? error);

            Assert.IsFalse(ok);
            Assert.IsTrue(error!.Contains("unsupported entry") || error.Contains("BlockDevice"));
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [TestMethod]
    public void TarExtractor_Extract_RejectsFifoEntry()
    {
        string destDir = Path.Combine(Path.GetTempPath(), "tar-fifo-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(Path.GetTempPath(), "test-fifo-" + Guid.NewGuid().ToString("N") + ".tar.gz");
        try
        {
            byte[] tarGz = CreateTarGz(writer =>
            {
                var fifo = new PaxTarEntry(TarEntryType.Fifo, "pipe_fifo");
                writer.WriteEntry(fifo);
            });

            File.WriteAllBytes(archivePath, tarGz);

            bool ok = TarExtractor.Extract(archivePath, destDir, out string? error);

            Assert.IsFalse(ok);
            Assert.IsTrue(error!.Contains("unsupported entry") || error.Contains("Fifo"));
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [TestMethod]
    public void TarExtractor_Extract_RejectsSymlinkChain_EscapingTarget()
    {
        string destDir = Path.Combine(Path.GetTempPath(), "tar-symchain-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(Path.GetTempPath(), "test-symchain-" + Guid.NewGuid().ToString("N") + ".tar.gz");
        try
        {
            byte[] tarGz = CreateTarGz(writer =>
            {
                var linkA = new PaxTarEntry(TarEntryType.SymbolicLink, "a")
                {
                    LinkName = "."
                };
                writer.WriteEntry(linkA);

                var linkInstall = new PaxTarEntry(TarEntryType.SymbolicLink, "a/install.sh")
                {
                    LinkName = "../outside.sh"
                };
                writer.WriteEntry(linkInstall);
            });

            File.WriteAllBytes(archivePath, tarGz);

            bool ok = TarExtractor.Extract(archivePath, destDir, out string? error);

            Assert.IsFalse(ok);
            Assert.IsNotNull(error);
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [TestMethod]
    public void TarExtractor_Extract_ClearsPreviousExtractionDirectoryFirst()
    {
        string destDir = Path.Combine(Path.GetTempPath(), "tar-clear-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(Path.GetTempPath(), "test-clear-" + Guid.NewGuid().ToString("N") + ".tar.gz");
        try
        {
            Directory.CreateDirectory(destDir);
            string staleFile = Path.Combine(destDir, "stale-old-file.txt");
            File.WriteAllText(staleFile, "stale content");

            byte[] tarGz = CreateTarGz(writer =>
            {
                var file = new PaxTarEntry(TarEntryType.RegularFile, "install.sh")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("#!/bin/sh\nexit 0\n"))
                };
                writer.WriteEntry(file);
            });

            File.WriteAllBytes(archivePath, tarGz);

            bool ok = TarExtractor.Extract(archivePath, destDir, out string? error);

            Assert.IsTrue(ok, error);
            Assert.IsFalse(File.Exists(staleFile), "Previous extraction directory must be wiped before unpacking.");
            Assert.IsTrue(File.Exists(Path.Combine(destDir, "install.sh")));
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [TestMethod]
    public void GitHubUpdater_IsInstallerFile_ValidatesFileAndTarget()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "installer-val-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string regularInstaller = Path.Combine(tempDir, "install.sh");
            File.WriteAllText(regularInstaller, "#!/bin/sh\nexit 0\n");

            Assert.IsTrue(GitHubUpdater.IsInstallerFile(tempDir, regularInstaller, out string? err1));
            Assert.IsNull(err1);

            string missingInstaller = Path.Combine(tempDir, "nonexistent.sh");
            Assert.IsFalse(GitHubUpdater.IsInstallerFile(tempDir, missingInstaller, out string? err2));
            Assert.IsTrue(err2!.Contains("no install.sh"));

            string outsideInstaller = Path.Combine(Path.GetTempPath(), "outside.sh");
            Assert.IsFalse(GitHubUpdater.IsInstallerFile(tempDir, outsideInstaller, out string? err3));
            Assert.IsTrue(err3!.Contains("not inside the extraction directory"));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void TarExtractor_Extract_RejectsSymlinkPointingOutside()
    {
        string destDir = Path.Combine(Path.GetTempPath(), "tar-symlink-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(Path.GetTempPath(), "test-symlink-" + Guid.NewGuid().ToString("N") + ".tar.gz");
        try
        {
            byte[] tarGz = CreateTarGz(writer =>
            {
                var link = new PaxTarEntry(TarEntryType.SymbolicLink, "outside_link")
                {
                    LinkName = "../../target"
                };
                writer.WriteEntry(link);
            });

            File.WriteAllBytes(archivePath, tarGz);

            bool ok = TarExtractor.Extract(archivePath, destDir, out string? error);

            Assert.IsFalse(ok);
            Assert.IsTrue(error!.Contains("points outside") || error.Contains("links are not allowed"));
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
        }
    }

    [TestMethod]
    public void TarExtractor_TryEntryPath_ValidatesPaths()
    {
        Assert.IsTrue(TarExtractor.TryEntryPath("a/b/c.txt", out string n1, out _));
        Assert.AreEqual("a/b/c.txt", n1);

        Assert.IsFalse(TarExtractor.TryEntryPath("/root/file", out _, out string? e1));
        Assert.IsTrue(e1!.Contains("absolute"));

        Assert.IsFalse(TarExtractor.TryEntryPath("a/../b", out _, out string? e2));
        Assert.IsTrue(e2!.Contains(".."));

        Assert.IsFalse(TarExtractor.TryEntryPath("", out _, out string? e3));
        Assert.IsTrue(e3!.Contains("empty"));
    }

    // ------------------------------------------------------------- Redirects

    [TestMethod]
    public async Task GitHubUpdater_CheckAsync_RedirectToDisallowedHost_Fails()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.Found);
            res.Headers.Location = new Uri("https://malicious.example.com/releases?per_page=30");
            return res;
        });

        var updater = new GitHubUpdater("4.3.0", handler: handler);
        UpdateCheck result = await updater.CheckAsync(CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Reason!.Contains("not on an allowed https host"));
    }

    [TestMethod]
    public async Task GitHubUpdater_CheckAsync_RedirectToHttp_Fails()
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.Found);
            res.Headers.Location = new Uri("http://github.com/releases?per_page=30");
            return res;
        });

        var updater = new GitHubUpdater("4.3.0", handler: handler);
        UpdateCheck result = await updater.CheckAsync(CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Reason!.Contains("not on an allowed https host"));
    }

    [TestMethod]
    public async Task GitHubUpdater_CheckAsync_SixRedirectHops_Fails()
    {
        int hopCount = 0;
        var handler = new FakeHttpMessageHandler(req =>
        {
            hopCount++;
            var res = new HttpResponseMessage(HttpStatusCode.Found);
            res.Headers.Location = new Uri($"https://github.com/hop{hopCount}");
            return res;
        });

        var updater = new GitHubUpdater("4.3.0", handler: handler);
        UpdateCheck result = await updater.CheckAsync(CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Reason!.Contains("more than 5 redirects"));
        Assert.AreEqual(6, hopCount, "Should fail on the 6th hop after MaxRedirects (5) is exceeded.");
    }

    [TestMethod]
    public async Task GitHubUpdater_CheckAsync_AllowedToAllowedRedirect_Succeeds()
    {
        string releaseJson = """
        [
          {
            "tag_name": "SystemMonitorLinux-4.4.0",
            "assets": [
              {
                "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
              },
              {
                "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
              }
            ]
          }
        ]
        """;

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.Host == "api.github.com")
            {
                var res = new HttpResponseMessage(HttpStatusCode.Found);
                res.Headers.Location = new Uri("https://github.com/epinephrinerx/SystemMonitor/releases?per_page=30");
                return res;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
            };
        });

        var updater = new GitHubUpdater("4.3.0", handler: handler);
        UpdateCheck result = await updater.CheckAsync(CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("4.4.0", result.Version);
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadChecksum_RedirectToDisallowedHost_Fails()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "check-redir-disallowed-" + Guid.NewGuid().ToString("N"));
        try
        {
            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                  },
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".sha256"))
                {
                    var res = new HttpResponseMessage(HttpStatusCode.Found);
                    res.Headers.Location = new Uri("https://malicious.example.com/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256");
                    return res;
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var updater = new GitHubUpdater("4.3.0", cacheDirectory: cacheDir, handler: handler);
            await updater.CheckAsync(CancellationToken.None);

            bool success = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);
            Assert.IsFalse(success);
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadChecksum_RedirectToHttp_Fails()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "check-redir-http-" + Guid.NewGuid().ToString("N"));
        try
        {
            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                  },
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".sha256"))
                {
                    var res = new HttpResponseMessage(HttpStatusCode.Found);
                    res.Headers.Location = new Uri("http://github.com/file.sha256");
                    return res;
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var updater = new GitHubUpdater("4.3.0", cacheDirectory: cacheDir, handler: handler);
            await updater.CheckAsync(CancellationToken.None);

            bool success = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);
            Assert.IsFalse(success);
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadChecksum_SixRedirectHops_Fails()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "check-redir-6hops-" + Guid.NewGuid().ToString("N"));
        try
        {
            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                  },
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                  }
                ]
              }
            ]
            """;

            int checksumHops = 0;
            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                checksumHops++;
                var res = new HttpResponseMessage(HttpStatusCode.Found);
                res.Headers.Location = new Uri($"https://objects.githubusercontent.com/hop{checksumHops}");
                return res;
            });

            var updater = new GitHubUpdater("4.3.0", cacheDirectory: cacheDir, handler: handler);
            await updater.CheckAsync(CancellationToken.None);

            bool success = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);
            Assert.IsFalse(success);
            Assert.AreEqual(6, checksumHops);
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadChecksum_AllowedToAllowedRedirect_Succeeds()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "check-redir-ok-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] tarGz = CreateTarGz(writer =>
            {
                var script = new PaxTarEntry(TarEntryType.RegularFile, "install.sh")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("#!/bin/sh\nexit 0\n"))
                };
                writer.WriteEntry(script);
            });

            string sha256 = Convert.ToHexString(SHA256.HashData(tarGz)).ToLowerInvariant();
            string checksumPayload = $"{sha256}  SystemMonitor-4.4.0-linux-x64.tar.gz\n";

            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                  },
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".sha256") && !url.EndsWith("download/file.sha256"))
                {
                    // Redirect from github.com to objects.githubusercontent.com
                    var res = new HttpResponseMessage(HttpStatusCode.Found);
                    res.Headers.Location = new Uri("https://objects.githubusercontent.com/download/file.sha256");
                    return res;
                }
                if (url.EndsWith("download/file.sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                if (url.EndsWith(".tar.gz"))
                {
                    var res = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(tarGz)
                    };
                    res.Content.Headers.ContentLength = tarGz.Length;
                    return res;
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var updater = new GitHubUpdater(
                "4.3.0",
                cacheDirectory: cacheDir,
                handler: handler,
                start: (_, _) => true);

            await updater.CheckAsync(CancellationToken.None);
            bool success = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);

            Assert.IsTrue(success);
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadArchive_RedirectToDisallowedHost_Fails()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "arch-redir-disallowed-" + Guid.NewGuid().ToString("N"));
        try
        {
            string checksumPayload = $"{new string('a', 64)}  SystemMonitor-4.4.0-linux-x64.tar.gz\n";
            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                  },
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                if (url.EndsWith(".tar.gz"))
                {
                    var res = new HttpResponseMessage(HttpStatusCode.Found);
                    res.Headers.Location = new Uri("https://malicious.example.com/archive.tar.gz");
                    return res;
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var updater = new GitHubUpdater("4.3.0", cacheDirectory: cacheDir, handler: handler);
            await updater.CheckAsync(CancellationToken.None);

            bool success = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);
            Assert.IsFalse(success);
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadArchive_RedirectToHttp_Fails()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "arch-redir-http-" + Guid.NewGuid().ToString("N"));
        try
        {
            string checksumPayload = $"{new string('a', 64)}  SystemMonitor-4.4.0-linux-x64.tar.gz\n";
            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                  },
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                if (url.EndsWith(".tar.gz"))
                {
                    var res = new HttpResponseMessage(HttpStatusCode.Found);
                    res.Headers.Location = new Uri("http://github.com/archive.tar.gz");
                    return res;
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var updater = new GitHubUpdater("4.3.0", cacheDirectory: cacheDir, handler: handler);
            await updater.CheckAsync(CancellationToken.None);

            bool success = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);
            Assert.IsFalse(success);
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadArchive_SixRedirectHops_Fails()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "arch-redir-6hops-" + Guid.NewGuid().ToString("N"));
        try
        {
            string checksumPayload = $"{new string('a', 64)}  SystemMonitor-4.4.0-linux-x64.tar.gz\n";
            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                  },
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                  }
                ]
              }
            ]
            """;

            int archiveHops = 0;
            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                archiveHops++;
                var res = new HttpResponseMessage(HttpStatusCode.Found);
                res.Headers.Location = new Uri($"https://objects.githubusercontent.com/archhop{archiveHops}");
                return res;
            });

            var updater = new GitHubUpdater("4.3.0", cacheDirectory: cacheDir, handler: handler);
            await updater.CheckAsync(CancellationToken.None);

            bool success = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);
            Assert.IsFalse(success);
            Assert.AreEqual(6, archiveHops);
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task GitHubUpdater_DownloadArchive_AllowedToAllowedRedirect_Succeeds()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), "arch-redir-ok-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] tarGz = CreateTarGz(writer =>
            {
                var script = new PaxTarEntry(TarEntryType.RegularFile, "install.sh")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("#!/bin/sh\nexit 0\n"))
                };
                writer.WriteEntry(script);
            });

            string sha256 = Convert.ToHexString(SHA256.HashData(tarGz)).ToLowerInvariant();
            string checksumPayload = $"{sha256}  SystemMonitor-4.4.0-linux-x64.tar.gz\n";

            string releaseJson = """
            [
              {
                "tag_name": "SystemMonitorLinux-4.4.0",
                "assets": [
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                  },
                  {
                    "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                    "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/SystemMonitorLinux-4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                  }
                ]
              }
            ]
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.Contains("/releases?per_page=30"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
                    };
                }
                if (url.EndsWith(".sha256"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(checksumPayload, Encoding.UTF8, "text/plain")
                    };
                }
                if (url.EndsWith(".tar.gz") && req.RequestUri!.Host == "github.com")
                {
                    // Redirect from github.com to objects.githubusercontent.com
                    var res = new HttpResponseMessage(HttpStatusCode.Found);
                    res.Headers.Location = new Uri("https://objects.githubusercontent.com/real-archive.tar.gz");
                    return res;
                }
                if (url.EndsWith("real-archive.tar.gz"))
                {
                    var res = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(tarGz)
                    };
                    res.Content.Headers.ContentLength = tarGz.Length;
                    return res;
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var updater = new GitHubUpdater(
                "4.3.0",
                cacheDirectory: cacheDir,
                handler: handler,
                start: (_, _) => true);

            await updater.CheckAsync(CancellationToken.None);
            bool success = await updater.DownloadAndInstallAsync("4.4.0", null, CancellationToken.None);

            Assert.IsTrue(success);
        }
        finally
        {
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
        }
    }

    // ------------------------------------------------------------- UpdateAutoCheck retry

    [TestMethod]
    public async Task UpdateAutoCheck_RunAsync_RetriesOnceAfter60Seconds_WhenSettingRemainsOn()
    {
        int checkCount = 0;
        var fakeUpdater = new FakeUpdater(() =>
        {
            checkCount++;
            return new UpdateCheck(false, null, "fake network failure");
        });

        var delays = new List<TimeSpan>();
        Task FakeDelay(TimeSpan span, CancellationToken token)
        {
            delays.Add(span);
            return Task.CompletedTask;
        }

        var autoCheck = new UpdateAutoCheck(
            updater: fakeUpdater,
            settingOn: () => true,
            delay: FakeDelay);

        UpdateCheck? result = await autoCheck.RunAsync(CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(2, checkCount, "Check should be attempted twice: initial check + 1 retry.");
        Assert.AreEqual(1, delays.Count);
        Assert.AreEqual(TimeSpan.FromSeconds(60), delays[0]);
    }

    [TestMethod]
    public async Task UpdateAutoCheck_RunAsync_DoesNotRetry_WhenSettingTurnedOffDuringDelay()
    {
        int checkCount = 0;
        var fakeUpdater = new FakeUpdater(() =>
        {
            checkCount++;
            return new UpdateCheck(false, null, "fake network failure");
        });

        bool setting = true;
        var delays = new List<TimeSpan>();
        Task FakeDelay(TimeSpan span, CancellationToken token)
        {
            delays.Add(span);
            setting = false; // User turned setting off while waiting
            return Task.CompletedTask;
        }

        var autoCheck = new UpdateAutoCheck(
            updater: fakeUpdater,
            settingOn: () => setting,
            delay: FakeDelay);

        UpdateCheck? result = await autoCheck.RunAsync(CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(1, checkCount, "Retry must be skipped if setting was turned off.");
        Assert.AreEqual(1, delays.Count);
    }

    [TestMethod]
    public async Task UpdateAutoCheck_RunAsync_SettingOffInitially_ReturnsNullWithoutChecking()
    {
        int checkCount = 0;
        var fakeUpdater = new FakeUpdater(() =>
        {
            checkCount++;
            return new UpdateCheck(true, "4.4.0");
        });

        var autoCheck = new UpdateAutoCheck(
            updater: fakeUpdater,
            settingOn: () => false);

        UpdateCheck? result = await autoCheck.RunAsync(CancellationToken.None);

        Assert.IsNull(result);
        Assert.AreEqual(0, checkCount);
    }

    [TestMethod]
    public async Task UpdateAutoCheck_RunAsync_InitialCheckSucceeds_DoesNotDelayOrRetry()
    {
        int checkCount = 0;
        var fakeUpdater = new FakeUpdater(() =>
        {
            checkCount++;
            return new UpdateCheck(true, "4.4.0");
        });

        bool delayCalled = false;
        var autoCheck = new UpdateAutoCheck(
            updater: fakeUpdater,
            settingOn: () => true,
            delay: (_, _) =>
            {
                delayCalled = true;
                return Task.CompletedTask;
            });

        UpdateCheck? result = await autoCheck.RunAsync(CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, checkCount);
        Assert.IsFalse(delayCalled);
    }

    [TestMethod]
    public async Task UpdateAutoCheck_RunAsync_SettingTurnedOffImmediatelyAfterFirstCheck_DoesNotDelayOrRetry()
    {
        int checkCount = 0;
        bool setting = true;
        var fakeUpdater = new FakeUpdater(() =>
        {
            checkCount++;
            setting = false;
            return new UpdateCheck(false, null, "failure");
        });

        bool delayCalled = false;
        var autoCheck = new UpdateAutoCheck(
            updater: fakeUpdater,
            settingOn: () => setting,
            delay: (_, _) =>
            {
                delayCalled = true;
                return Task.CompletedTask;
            });

        UpdateCheck? result = await autoCheck.RunAsync(CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(1, checkCount);
        Assert.IsFalse(delayCalled);
    }

    private sealed class FakeUpdater : IUpdater
    {
        private readonly Func<UpdateCheck> _check;

        public FakeUpdater(Func<UpdateCheck> check)
        {
            _check = check;
        }

        public string CurrentVersion => "4.3.0";

        public Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_check());

        public Task<bool> DownloadAndInstallAsync(
            string version, IProgress<int>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
