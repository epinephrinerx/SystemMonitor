using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// A release's Linux update files, resolved and validated: the archive, its
/// checksum, and the https URLs they are fetched from.
/// </summary>
public sealed record UpdateRelease(
    string Version,
    string ArchiveName,
    string ChecksumName,
    Uri ArchiveUrl,
    Uri ChecksumUrl);

/// <summary>
/// The Linux asset names (spec section 8, Linux decision): one tar.gz per
/// release and architecture plus a <c>.sha256</c> next to it.
/// </summary>
public static class UpdateAssets
{
    public const string Prefix = "SystemMonitor-";
    public const string Suffix = ".tar.gz";
    public const string ChecksumSuffix = ".sha256";

    /// <summary>
    /// What <see cref="RuntimeInformation.OSArchitecture"/> calls x64 and
    /// arm64, spelled the way the release assets are.
    /// </summary>
    public static string ArchitectureSuffix() => ArchitectureSuffix(RuntimeInformation.OSArchitecture);

    public static string ArchitectureSuffix(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "arm",
        Architecture.X86 => "x86",
        _ => architecture.ToString().ToLowerInvariant(),
    };

    public static string ArchiveName(string version, string architecture) =>
        $"{Prefix}{version}-linux-{architecture}{Suffix}";

    public static string ChecksumName(string archiveName) => archiveName + ChecksumSuffix;
}

/// <summary>
/// Reads <c>GET /repos/epinephrinerx/SystemMonitor/releases/latest</c>.
///
/// Every failure is a reason, never an exception: a release with no Linux
/// asset must come back as <c>check_failed</c>, not as "up to date", because
/// nothing about it says the installed copy is current.
/// </summary>
public static class UpdateReleaseParser
{
    public const string Endpoint =
        "https://api.github.com/repos/epinephrinerx/SystemMonitor/releases/latest";

    /// <summary>Spec section 8: https, and only these hosts.</summary>
    public static IReadOnlyList<string> AllowedHosts { get; } = new[]
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    };

    /// <summary>"v4.4.0" or "V4.4.0" to "4.4.0"; anything but three numeric parts fails.</summary>
    public static bool TryVersion(string? tag, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        string text = tag.Trim().TrimStart('v', 'V');
        string[] parts = text.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        foreach (string part in parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }
        }

        version = text;
        return true;
    }

    /// <summary>The release's Linux archive and checksum, or the reason there is none.</summary>
    public static (UpdateRelease? Release, string? Error) Parse(string json, string architecture)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return (null, "release payload is not JSON: " + exception.Message);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, "release payload is not an object");
            }

            string? tag = Text(root, "tag_name");
            if (!TryVersion(tag, out string version))
            {
                return (null, $"release tag \"{tag}\" is not Major.Minor.Build");
            }

            string expected = UpdateAssets.ArchiveName(version, architecture);
            string? archiveName = null;
            string? archiveUrl = null;
            foreach (JsonElement asset in Assets(root))
            {
                if (!string.Equals(Text(asset, "name"), expected, StringComparison.Ordinal))
                {
                    continue;
                }
                archiveName = expected;
                archiveUrl = Text(asset, "browser_download_url");
                break;
            }

            if (archiveName is null || archiveUrl is null)
            {
                return (null, $"no linux asset \"{expected}\" in release {version}");
            }
            if (!TryAllowedUrl(archiveUrl, out Uri? archiveUri))
            {
                return (null, $"update asset \"{archiveName}\" is not on an allowed https host");
            }

            string checksumName = UpdateAssets.ChecksumName(archiveName);
            string? checksumUrl = null;
            foreach (JsonElement asset in Assets(root))
            {
                if (string.Equals(Text(asset, "name"), checksumName, StringComparison.Ordinal))
                {
                    checksumUrl = Text(asset, "browser_download_url");
                    break;
                }
            }
            if (checksumUrl is null)
            {
                return (null, $"no checksum asset \"{checksumName}\" in release {version}");
            }
            if (!TryAllowedUrl(checksumUrl, out Uri? checksumUri))
            {
                return (null, $"checksum asset \"{checksumName}\" is not on an allowed https host");
            }

            return (
                new UpdateRelease(version, archiveName, checksumName, archiveUri!, checksumUri!),
                null);
        }
    }

    /// <summary>True for an absolute https URL on the spec's allow-list.</summary>
    public static bool TryAllowedUrl(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        foreach (string host in AllowedHosts)
        {
            if (string.Equals(parsed.Host, host, StringComparison.OrdinalIgnoreCase))
            {
                uri = parsed;
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<JsonElement> Assets(JsonElement root) =>
        root.TryGetProperty("assets", out JsonElement assets)
        && assets.ValueKind == JsonValueKind.Array
            ? assets.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// The real <see cref="IUpdater"/> (spec section 8, Linux decision).
///
/// Check: one GET, 30 s, GitHub's headers. Download: archive plus checksum into
/// the cache directory, SHA-256 verified, extracted with the framework's tar
/// reader, then <c>/bin/sh install.sh --update --wait-pid N</c> started
/// without a shell. Every failure deletes what it wrote and returns false, so
/// the panel can say <c>download_failed</c> and clear the pending update.
///
/// Redirects are never followed by the client (Amendment 2, item 1): they are
/// followed by hand, at most <see cref="MaxRedirects"/> hops, and every hop has
/// to stay on an https host from the spec's allow-list.
/// </summary>
public sealed class GitHubUpdater : IUpdater
{
    /// <summary>Amendment 2, item 1: five hand-followed redirects at most.</summary>
    public const int MaxRedirects = 5;

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    private static readonly HttpStatusCode[] RedirectStatuses =
    {
        HttpStatusCode.MovedPermanently,
        HttpStatusCode.Found,
        HttpStatusCode.SeeOther,
        HttpStatusCode.TemporaryRedirect,
        HttpStatusCode.PermanentRedirect,
    };

    private readonly HttpClient _client;
    private readonly Action<string>? _log;
    private readonly string _architecture;
    private readonly string _cacheDirectory;
    private readonly Func<string, IReadOnlyList<string>, bool> _start;
    private readonly int _ownPid;

    private UpdateRelease? _release;

    public GitHubUpdater(
        string currentVersion,
        string? cacheDirectory = null,
        HttpMessageHandler? handler = null,
        Action<string>? log = null,
        string? architecture = null,
        Func<string, IReadOnlyList<string>, bool>? start = null,
        int ownPid = 0)
    {
        CurrentVersion = currentVersion;
        _log = log;
        _architecture = architecture ?? UpdateAssets.ArchitectureSuffix();
        _cacheDirectory = cacheDirectory ?? DefaultCacheDirectory;
        _start = start ?? StartDetached;
        _ownPid = ownPid > 0 ? ownPid : Environment.ProcessId;

        // The check carries its own 30 s token; a download must not be cut off
        // at the same limit, so the client itself has no timeout. Redirects are
        // ours to follow and validate, so the platform handler must not do it.
        if (handler is HttpClientHandler redirecting)
        {
            redirecting.AllowAutoRedirect = false;
        }
        _client = new HttpClient(
            handler ?? new HttpClientHandler { AllowAutoRedirect = false },
            disposeHandler: handler is null)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public string CurrentVersion { get; }

    /// <summary><c>~/.cache/SystemMonitor/update</c> by default.</summary>
    public static string DefaultCacheDirectory
    {
        get
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrWhiteSpace(xdg))
            {
                return Path.Combine(xdg, "SystemMonitor", "update");
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                home = Environment.GetEnvironmentVariable("HOME") ?? ".";
            }
            return Path.Combine(home, ".cache", "SystemMonitor", "update");
        }
    }

    /// <summary>The release the last successful check resolved, for the download.</summary>
    public UpdateRelease? Release => _release;

    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);
        try
        {
            (HttpResponseMessage? response, string? error) = await GetAsync(
                new Uri(UpdateReleaseParser.Endpoint),
                request =>
                {
                    request.Headers.UserAgent.ParseAdd("SystemMonitor/" + CurrentVersion);
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                },
                timeout.Token).ConfigureAwait(false);
            if (response is null)
            {
                return Fail(error ?? "the request failed");
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    return Fail($"GitHub returned {(int)response.StatusCode}");
                }

                string body = await response.Content.ReadAsStringAsync(timeout.Token)
                    .ConfigureAwait(false);
                (UpdateRelease? release, string? message) =
                    UpdateReleaseParser.Parse(body, _architecture);
                if (release is null)
                {
                    return Fail(message ?? "the release could not be read");
                }

                _release = release;
                return new UpdateCheck(true, release.Version);
            }
        }
        catch (OperationCanceledException)
        {
            return Fail("the request timed out");
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }
    }

    public async Task<bool> DownloadAndInstallAsync(
        string version, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        UpdateRelease? release = _release is { } cached
            && string.Equals(cached.Version, version, StringComparison.Ordinal)
            ? cached
            : await ResolveAsync(version, cancellationToken).ConfigureAwait(false);
        if (release is null)
        {
            return false;
        }

        string archive = Path.Combine(_cacheDirectory, release.ArchiveName);
        string checksum = Path.Combine(_cacheDirectory, release.ChecksumName);
        string target = Path.Combine(_cacheDirectory, release.Version);

        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            // A stale extract from an earlier attempt must not be mistaken for
            // this one, and a failure leaves nothing behind.
            Delete(target);

            if (!await DownloadAsync(release.ChecksumUrl, checksum, null, cancellationToken)
                    .ConfigureAwait(false))
            {
                return DownloadFailed(release, checksum);
            }

            string? expected = ReadChecksum(checksum);
            if (expected is null)
            {
                _log?.Invoke("update checksum file is empty or malformed");
                return DownloadFailed(release, checksum);
            }

            if (!await DownloadAsync(release.ArchiveUrl, archive, progress, cancellationToken)
                    .ConfigureAwait(false))
            {
                return DownloadFailed(release, checksum, archive);
            }

            if (!string.Equals(HashFile(archive), expected, StringComparison.OrdinalIgnoreCase))
            {
                _log?.Invoke("update checksum mismatch for " + release.ArchiveName);
                return DownloadFailed(release, checksum, archive);
            }

            Delete(target);
            if (!TarExtractor.Extract(archive, target, out string? error))
            {
                _log?.Invoke("update archive rejected: " + error);
                Delete(target);
                return DownloadFailed(release, checksum, archive);
            }

            string installer = Path.Combine(target, "install.sh");
            if (!IsInstallerFile(target, installer, out string? installerError))
            {
                _log?.Invoke(installerError!);
                Delete(target);
                return DownloadFailed(release, checksum, archive);
            }

            bool started = _start("/bin/sh", new[]
            {
                installer,
                "--update",
                "--wait-pid",
                _ownPid.ToString(CultureInfo.InvariantCulture),
            });
            if (!started)
            {
                _log?.Invoke("update installer could not be started");
                return DownloadFailed(release, checksum, archive);
            }

            progress?.Report(100);
            _log?.Invoke("update installer started for " + release.Version);
            return true;
        }
        catch (OperationCanceledException)
        {
            _log?.Invoke("update download was cancelled");
            return DownloadFailed(release, checksum, archive);
        }
        catch (Exception exception)
        {
            _log?.Invoke("update download failed: " + exception.Message);
            return DownloadFailed(release, checksum, archive);
        }
    }

    /// <summary>The checksum file holds the hex digest, optionally then a file name.</summary>
    public static string? ReadChecksum(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            string text = line.Trim();
            if (text.Length == 0)
            {
                continue;
            }
            int space = text.IndexOfAny(new[] { ' ', '\t' });
            string token = space < 0 ? text : text[..space];
            return IsHexDigest(token) ? token : null;
        }
        return null;
    }

    /// <summary>
    /// Amendment 2, item 2: the installer has to be a regular file below the
    /// extraction directory after <see cref="Path.GetFullPath"/>, and it must
    /// not be a link. A link is never extracted in the first place, so this is
    /// the second lock on the same door.
    /// </summary>
    public static bool IsInstallerFile(string directory, string path, out string? error)
    {
        error = null;
        string prefix = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
        {
            error = "install.sh is not inside the extraction directory";
            return false;
        }

        var file = new FileInfo(full);
        if (!file.Exists)
        {
            error = "the update archive has no install.sh at its root";
            return false;
        }
        if (file.LinkTarget is not null)
        {
            error = "install.sh is a link, not a regular file";
            return false;
        }
        return true;
    }

    private async Task<UpdateRelease?> ResolveAsync(string version, CancellationToken cancellationToken)
    {
        UpdateCheck check = await CheckAsync(cancellationToken).ConfigureAwait(false);
        if (!check.Succeeded)
        {
            return null;
        }
        if (_release is { } release && string.Equals(release.Version, version, StringComparison.Ordinal))
        {
            return release;
        }
        _log?.Invoke($"update {version} is not the release the checker resolved");
        return null;
    }

    /// <summary>
    /// GET with the redirects followed by hand (Amendment 2, item 1). The
    /// client never follows one itself, so a 302 cannot leave the https
    /// allow-list or move the request where the parser never looked. The
    /// returned response belongs to the caller; a redirect problem comes back
    /// as a reason and no response.
    /// </summary>
    private async Task<(HttpResponseMessage? Response, string? Error)> GetAsync(
        Uri url,
        Action<HttpRequestMessage>? configure,
        CancellationToken cancellationToken)
    {
        Uri current = url;
        for (int hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            configure?.Invoke(request);

            HttpResponseMessage response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!RedirectStatuses.Contains(response.StatusCode))
            {
                return (response, null);
            }

            int status = (int)response.StatusCode;
            Uri? location = response.Headers.Location;
            response.Dispose();
            if (location is null)
            {
                return (null, $"GitHub returned {status} without a location");
            }
            if (hop == MaxRedirects)
            {
                return (null, $"more than {MaxRedirects} redirects from {url.Host}");
            }

            Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (!UpdateReleaseParser.TryAllowedUrl(next.AbsoluteUri, out Uri? allowed))
            {
                return (null, $"a redirect to {next.Host} is not on an allowed https host");
            }
            _log?.Invoke($"following a redirect from {current.Host} to {allowed!.Host}");
            current = allowed!;
        }

        return (null, $"more than {MaxRedirects} redirects from {url.Host}");
    }

    private async Task<bool> DownloadAsync(
        Uri url, string path, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        try
        {
            (HttpResponseMessage? response, string? error) =
                await GetAsync(url, null, cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                _log?.Invoke("update download failed: " + (error ?? "the request failed"));
                return false;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    _log?.Invoke($"update download returned {(int)response.StatusCode} from {url.Host}");
                    return false;
                }

                long? total = response.Content.Headers.ContentLength;
                await using Stream input = await response.Content
                    .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(
                    path, FileMode.Create, FileAccess.Write, FileShare.None);

                byte[] buffer = new byte[81920];
                long written = 0;
                int lastPercent = -1;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    written += read;
                    if (progress is not null && total is > 0)
                    {
                        int percent = (int)Math.Clamp(written * 100 / total.Value, 0, 100);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            progress.Report(percent);
                        }
                    }
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _log?.Invoke("update download failed: " + exception.Message);
            TryDelete(path);
            return false;
        }
    }

    private bool DownloadFailed(UpdateRelease release, params string[] files)
    {
        _log?.Invoke("update to " + release.Version + " failed; the pending update is cleared");
        foreach (string file in files)
        {
            TryDelete(file);
        }
        return false;
    }

    private UpdateCheck Fail(string reason)
    {
        _log?.Invoke("update check failed: " + reason);
        return new UpdateCheck(false, null, reason);
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsHexDigest(string token)
    {
        if (token.Length != 64)
        {
            return false;
        }
        foreach (char character in token)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }
        return true;
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // A leftover directory is cleaned on the next attempt.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // A partial file that cannot be removed is still a failure.
        }
    }

    /// <summary>The installer may outlive us, so its handle is not kept.</summary>
    private static bool StartDetached(string file, IReadOnlyList<string> arguments)
    {
        try
        {
            var info = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(info);
            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// Unpacks an update archive with <see cref="TarReader"/>, refusing anything
/// that could write outside its own directory or hide what is being run:
/// absolute names, any <c>..</c> segment, and every entry that is not a plain
/// file or directory (link, device, FIFO or anything else).
///
/// Amendment 2, item 2: the archive is rejected whole, and it is always
/// unpacked into a freshly created, empty directory.
/// </summary>
public static class TarExtractor
{
    public static bool Extract(string archivePath, string destination, out string? error)
    {
        try
        {
            // Start from nothing: an earlier attempt at the same version must
            // not leave files the installer would then run.
            string root = Path.GetFullPath(destination);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            Directory.CreateDirectory(root);

            using FileStream file = File.OpenRead(archivePath);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new TarReader(gzip);

            while (reader.GetNextEntry(copyData: false) is { } entry)
            {
                if (!TryEntryPath(entry.Name, out string name, out error))
                {
                    return false;
                }

                string path = Path.Combine(root, name);
                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(path);
                        break;

                    case TarEntryType.RegularFile:
                    case TarEntryType.V7RegularFile:
                    case TarEntryType.ContiguousFile:
                        CreateParent(path);
                        using (var output = new FileStream(path, FileMode.Create, FileAccess.Write))
                        {
                            entry.DataStream?.CopyTo(output);
                        }
                        break;

                    case TarEntryType.SymbolicLink:
                    case TarEntryType.HardLink:
                        // A link that escapes is worth naming; either way the
                        // archive is refused, because phase 4 ships none.
                        if (!TryLinkTarget(root, name, entry.LinkName, out string? linkError))
                        {
                            error = linkError;
                            return false;
                        }
                        error = $"unsupported entry \"{entry.Name}\" ({entry.EntryType}): links are not allowed";
                        return false;

                    default:
                        error = $"unsupported entry \"{entry.Name}\" ({entry.EntryType})";
                        return false;
                }
            }

            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>Relative, <c>..</c>-free, and not an absolute or drive path.</summary>
    public static bool TryEntryPath(string rawName, out string name, out string? error)
    {
        name = string.Empty;
        error = null;

        string normalized = (rawName ?? string.Empty).Replace('\\', '/');
        if (normalized.Length == 0)
        {
            error = "an entry has an empty name";
            return false;
        }
        if (normalized[0] == '/')
        {
            error = $"entry \"{rawName}\" is absolute";
            return false;
        }
        if (normalized.Length >= 2 && normalized[1] == ':')
        {
            error = $"entry \"{rawName}\" is absolute";
            return false;
        }

        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = "an entry has an empty name";
            return false;
        }
        foreach (string part in parts)
        {
            if (part == "..")
            {
                error = $"entry \"{rawName}\" contains \"..\"";
                return false;
            }
        }

        name = string.Join('/', parts);
        return true;
    }

    /// <summary>A link may point anywhere inside the archive, nowhere outside it.</summary>
    private static bool TryLinkTarget(
        string destination, string entryName, string? linkName, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(linkName))
        {
            error = $"link \"{entryName}\" has no target";
            return false;
        }

        string target = linkName.Replace('\\', '/');
        if (target[0] == '/')
        {
            error = $"link \"{entryName}\" points outside the archive";
            return false;
        }

        string baseDirectory = Path.GetDirectoryName(entryName) ?? string.Empty;
        string resolved = Path.GetFullPath(Path.Combine(destination, baseDirectory, target));
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(root, StringComparison.Ordinal))
        {
            error = $"link \"{entryName}\" points outside the archive";
            return false;
        }
        return true;
    }

    private static void CreateParent(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }
}
