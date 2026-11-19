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
    public void UpdateReleaseParser_Parse_ValidJson_ReturnsRelease()
    {
        string json = """
        {
          "tag_name": "v4.4.0",
          "assets": [
            {
              "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
              "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
            },
            {
              "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
              "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
            }
          ]
        }
        """;

        (UpdateRelease? release, string? error) = UpdateReleaseParser.Parse(json, "x64");

        Assert.IsNull(error);
        Assert.IsNotNull(release);
        Assert.AreEqual("4.4.0", release.Version);
        Assert.AreEqual("SystemMonitor-4.4.0-linux-x64.tar.gz", release.ArchiveName);
        Assert.AreEqual("SystemMonitor-4.4.0-linux-x64.tar.gz.sha256", release.ChecksumName);
    }

    [TestMethod]
    public void UpdateReleaseParser_Parse_MissingAssetOrChecksum_ReturnsError()
    {
        string missingChecksum = """
        {
          "tag_name": "v4.4.0",
          "assets": [
            {
              "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
              "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
            }
          ]
        }
        """;

        (UpdateRelease? release, string? error) = UpdateReleaseParser.Parse(missingChecksum, "x64");
        Assert.IsNull(release);
        Assert.IsTrue(error!.Contains("no checksum asset"));

        string missingLinuxAsset = """
        {
          "tag_name": "v4.4.0",
          "assets": [
            {
              "name": "SystemMonitor-4.4.0-win-x64.zip",
              "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-win-x64.zip"
            }
          ]
        }
        """;

        (release, error) = UpdateReleaseParser.Parse(missingLinuxAsset, "x64");
        Assert.IsNull(release);
        Assert.IsTrue(error!.Contains("no linux asset"));
    }

    [TestMethod]
    public void UpdateReleaseParser_Parse_DisallowedHost_ReturnsError()
    {
        string json = """
        {
          "tag_name": "v4.4.0",
          "assets": [
            {
              "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
              "browser_download_url": "https://unauthorized-domain.com/SystemMonitor-4.4.0-linux-x64.tar.gz"
            },
            {
              "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
              "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
            }
          ]
        }
        """;

        (UpdateRelease? release, string? error) = UpdateReleaseParser.Parse(json, "x64");
        Assert.IsNull(release);
        Assert.IsTrue(error!.Contains("not on an allowed https host"));
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

        state.DownloadFailed();
        Assert.AreEqual("Download failed", state.StatusText(lang));
        Assert.AreEqual("Check for updates", state.ButtonLabel(lang));
    }

    // ------------------------------------------------------------- GitHubUpdater

    [TestMethod]
    public async Task GitHubUpdater_CheckAsync_SetsHeaders_And_ParsesRelease()
    {
        string releaseJson = """
        {
          "tag_name": "v4.4.0",
          "assets": [
            {
              "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
              "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
            },
            {
              "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
              "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
            }
          ]
        }
        """;

        var handler = new FakeHttpMessageHandler(req =>
        {
            Assert.AreEqual("https://api.github.com/repos/epinephrinerx/SystemMonitor/releases/latest", req.RequestUri!.ToString());
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
        Assert.IsNotNull(updater.Release);
        Assert.AreEqual("SystemMonitor-4.4.0-linux-x64.tar.gz", updater.Release.ArchiveName);
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
            {
              "tag_name": "v4.4.0",
              "assets": [
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                },
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                }
              ]
            }
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.EndsWith("/latest"))
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
            {
              "tag_name": "v4.4.0",
              "assets": [
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                },
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                }
              ]
            }
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.EndsWith("/latest"))
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
            {
              "tag_name": "v4.4.0",
              "assets": [
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                },
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                }
              ]
            }
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.EndsWith("/latest"))
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
        // Include the `a -> .` + `a/install.sh -> ../outside.sh` case
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
        // A previous extraction directory is cleared first (Amendment 2, item 2)
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
            res.Headers.Location = new Uri("https://malicious.example.com/releases/latest");
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
            res.Headers.Location = new Uri("http://github.com/releases/latest");
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
        {
          "tag_name": "v4.4.0",
          "assets": [
            {
              "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
              "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
            },
            {
              "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
              "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
            }
          ]
        }
        """;

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.RequestUri!.Host == "api.github.com")
            {
                var res = new HttpResponseMessage(HttpStatusCode.Found);
                res.Headers.Location = new Uri("https://github.com/epinephrinerx/SystemMonitor/releases/latest");
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
            {
              "tag_name": "v4.4.0",
              "assets": [
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                },
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                }
              ]
            }
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.EndsWith("/latest"))
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
            {
              "tag_name": "v4.4.0",
              "assets": [
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                },
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                }
              ]
            }
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.EndsWith("/latest"))
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
            {
              "tag_name": "v4.4.0",
              "assets": [
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                },
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                }
              ]
            }
            """;

            int checksumHops = 0;
            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.EndsWith("/latest"))
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
            {
              "tag_name": "v4.4.0",
              "assets": [
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                },
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                }
              ]
            }
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.EndsWith("/latest"))
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
            {
              "tag_name": "v4.4.0",
              "assets": [
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                },
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                }
              ]
            }
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.EndsWith("/latest"))
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
            {
              "tag_name": "v4.4.0",
              "assets": [
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                },
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                }
              ]
            }
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.EndsWith("/latest"))
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
            {
              "tag_name": "v4.4.0",
              "assets": [
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                },
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                }
              ]
            }
            """;

            int archiveHops = 0;
            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.EndsWith("/latest"))
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
            {
              "tag_name": "v4.4.0",
              "assets": [
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz"
                },
                {
                  "name": "SystemMonitor-4.4.0-linux-x64.tar.gz.sha256",
                  "browser_download_url": "https://github.com/epinephrinerx/SystemMonitor/releases/download/v4.4.0/SystemMonitor-4.4.0-linux-x64.tar.gz.sha256"
                }
              ]
            }
            """;

            var handler = new FakeHttpMessageHandler(req =>
            {
                string url = req.RequestUri!.ToString();
                if (url.EndsWith("/latest"))
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
