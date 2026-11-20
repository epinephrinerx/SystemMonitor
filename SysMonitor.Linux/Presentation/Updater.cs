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
/// A release's Linux update files, resolved and validated: the archive (or the
/// .deb), its checksum, the https URLs they are fetched from, and the tag they
/// came from.
/// </summary>
public sealed record UpdateRelease(
    string Version,
    string ArchiveName,
    string ChecksumName,
    Uri ArchiveUrl,
    Uri ChecksumUrl,
    string? Tag = null);

/// <summary>
/// The Linux asset names (spec section 8, Linux decision): one tar.gz per
/// release and architecture plus a <c>.sha256</c> next to it, or the .deb a
/// packaged install updates with.
/// </summary>
public static class UpdateAssets
{
    public const string Prefix = "SystemMonitor-";
    public const string Suffix = ".tar.gz";
    public const string ChecksumSuffix = ".sha256";

    /// <summary>
    /// The Linux releases' tag prefix. The Windows releases share the same
    /// GitHub releases page and use <c>v*</c>, so the prefix is the only thing
    /// that tells the two apart.
    /// </summary>
    public const string TagPrefix = "SystemMonitorLinux-";

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

    /// <summary>amd64 or arm64, the way a .deb names its architecture.</summary>
    public static string DebArchitectureSuffix() =>
        DebArchitectureSuffix(RuntimeInformation.OSArchitecture);

    public static string DebArchitectureSuffix(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "amd64",
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "armhf",
        Architecture.X86 => "i386",
        _ => architecture.ToString().ToLowerInvariant(),
    };

    public static string ArchiveName(string version, string architecture) =>
        $"{Prefix}{version}-linux-{architecture}{Suffix}";

    public static string DebName(string version, string debArchitecture) =>
        $"systemmonitor_{version}_{debArchitecture}.deb";

    public static string ChecksumName(string archiveName) => archiveName + ChecksumSuffix;
}

/// <summary>
/// The files this install needs in a release. A per-user install updates from
/// the tar.gz; a .deb install updates from the package, and the two have
/// different names, so the parser is told which to require before it looks at
/// a release's assets.
/// </summary>
public sealed record UpdateAssetSet(bool Package, string Architecture)
{
    public static UpdateAssetSet TarGz(string architecture) => new(false, architecture);

    public static UpdateAssetSet Deb(string? architecture = null) =>
        new(true, architecture ?? UpdateAssets.DebArchitectureSuffix());

    public (string File, string Checksum) Names(string version)
    {
        string file = Package
            ? UpdateAssets.DebName(version, Architecture)
            : UpdateAssets.ArchiveName(version, Architecture);
        return (file, UpdateAssets.ChecksumName(file));
    }

    /// <summary>What the probes and the update log call this install.</summary>
    public string Describe() =>
        Package ? $"deb package ({Architecture})" : $"tar.gz archive ({Architecture})";
}

/// <summary>
/// Reads <c>GET /repos/epinephrinerx/SystemMonitor/releases?per_page=30</c> and
/// picks the newest Linux release in it.
///
/// The Windows releases share this page and <c>v4.3.0</c> is "Latest", so
/// <c>/releases/latest</c> is never the Linux answer. Only a tag that starts
/// with <see cref="UpdateAssets.TagPrefix"/> and is neither a draft nor a
/// prerelease counts; the Windows <c>v*</c> tags and the Mac tags are ignored.
///
/// Every failure is a reason, never an exception. No Linux release at all is
/// not a failure: it means the installed copy is current (no Linux release
/// exists yet). A 404, malformed body, unreachable host or a newest Linux
/// release without this install's assets is <c>check_failed</c>.
/// </summary>
public static class UpdateReleaseParser
{
    public const string Endpoint =
        "https://api.github.com/repos/epinephrinerx/SystemMonitor/releases?per_page=30";

    /// <summary>Spec section 8: https, and only these hosts.</summary>
    public static IReadOnlyList<string> AllowedHosts { get; } = new[]
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    };

    /// <summary>
    /// The permissive form the legacy single-release helper uses: "v4.4.0" or
    /// "V4.4.0" to "4.4.0"; anything but three numeric parts fails. The release
    /// list does not use it (Amendment 1, item 4): a Linux tag's suffix has to
    /// be exactly Major.Minor.Build, so <see cref="TryLinuxVersion"/> is used
    /// there instead.
    /// </summary>
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

    /// <summary>
    /// Amendment 1, item 4: the text after <c>SystemMonitorLinux-</c> must be
    /// exactly Major.Minor.Build -- digits only, no leading "v", nothing before
    /// or after. "v4.4.0", "4.4.0-rc1" and "4.4.0.1" are all ignored.
    /// </summary>
    public static bool TryLinuxVersion(string? suffix, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrEmpty(suffix))
        {
            return false;
        }

        string[] parts = suffix.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        foreach (string part in parts)
        {
            if (part.Length == 0)
            {
                return false;
            }
            foreach (char character in part)
            {
                if (!char.IsAsciiDigit(character))
                {
                    return false;
                }
            }
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }
        }

        version = suffix;
        return true;
    }

    /// <summary>
    /// The newest Linux release in the list, with this install's files, or the
    /// reason there is none. A null release with a null error means "no Linux
    /// release exists", which the updater reports as up to date;
    /// <paramref name="note"/> says what was seen either way, for the probe and
    /// the log.
    /// </summary>
    public static (UpdateRelease? Release, string? Error, string Note) Select(
        string json, UpdateAssetSet assets)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return (null, "release payload is not JSON: " + exception.Message, string.Empty);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return (null, "release payload is not a list", string.Empty);
            }

            int total = 0;
            int otherTags = 0;
            int drafts = 0;
            int prereleases = 0;
            var candidates = new List<Candidate>();
            foreach (JsonElement release in root.EnumerateArray())
            {
                total++;
                string? tag = Text(release, "tag_name");
                if (tag is null || !tag.StartsWith(UpdateAssets.TagPrefix, StringComparison.Ordinal))
                {
                    otherTags++;
                    continue;
                }
                if (Flag(release, "draft"))
                {
                    drafts++;
                    continue;
                }
                if (Flag(release, "prerelease"))
                {
                    prereleases++;
                    continue;
                }
                if (!TryLinuxVersion(tag[UpdateAssets.TagPrefix.Length..], out string version))
                {
                    otherTags++;
                    continue;
                }
                candidates.Add(new Candidate(release, tag, version));
            }

            string ignored =
                $"{prereleases} prerelease, {drafts} draft, {otherTags} other tag(s)";
            if (candidates.Count == 0)
            {
                return (
                    null,
                    null,
                    $"no {UpdateAssets.TagPrefix}* release among {total} ({ignored})");
            }

            Candidate newest = candidates[0];
            foreach (Candidate candidate in candidates)
            {
                if (CompareVersions(candidate.Version, newest.Version) > 0)
                {
                    newest = candidate;
                }
            }

            string note =
                $"picked {newest.Tag} ({newest.Version}) of {candidates.Count} Linux " +
                $"release(s) seen in {total}; ignored {ignored}";

            (UpdateRelease? found, string? missing) =
                RequireAssets(newest.Release, newest.Version, newest.Tag, assets);
            return (found, missing, note);
        }
    }

    /// <summary>
    /// One release object with no tag filtering: the shape
    /// <c>/releases/tags/{tag}</c> answers with. The Linux updater reads the
    /// list through <see cref="Select"/>; this stays because the existing
    /// updater tests read a single release and must keep compiling until they
    /// move to <see cref="Select"/>.
    /// </summary>
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

            return RequireAssets(root, version, tag, UpdateAssetSet.TarGz(architecture));
        }
    }

    /// <summary>The release's files for this install, or the reason one is missing.</summary>
    private static (UpdateRelease? Release, string? Error) RequireAssets(
        JsonElement release, string version, string? tag, UpdateAssetSet assets)
    {
        (string file, string checksum) = assets.Names(version);
        string? fileUrl = null;
        string? checksumUrl = null;
        foreach (JsonElement asset in Assets(release))
        {
            string? name = Text(asset, "name");
            if (string.Equals(name, file, StringComparison.Ordinal))
            {
                fileUrl = Text(asset, "browser_download_url");
            }
            else if (string.Equals(name, checksum, StringComparison.Ordinal))
            {
                checksumUrl = Text(asset, "browser_download_url");
            }
        }

        if (fileUrl is null)
        {
            return (null, $"no linux asset \"{file}\" in release {version}");
        }
        if (!TryAllowedUrl(fileUrl, out Uri? fileUri))
        {
            return (null, $"update asset \"{file}\" is not on an allowed https host");
        }
        if (checksumUrl is null)
        {
            return (null, $"no checksum asset \"{checksum}\" in release {version}");
        }
        if (!TryAllowedUrl(checksumUrl, out Uri? checksumUri))
        {
            return (null, $"checksum asset \"{checksum}\" is not on an allowed https host");
        }

        return (
            new UpdateRelease(version, file, checksum, fileUri!, checksumUri!, tag),
            null);
    }

    /// <summary>Three numeric parts compared in order, so 4.10.0 beats 4.9.0.</summary>
    public static int CompareVersions(string left, string right)
    {
        int[] a = Parts(left);
        int[] b = Parts(right);
        for (int index = 0; index < 3; index++)
        {
            if (a[index] != b[index])
            {
                return a[index].CompareTo(b[index]);
            }
        }
        return 0;
    }

    /// <summary>True for a JSON flag that is literally true (absent means false).</summary>
    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;

    private static int[] Parts(string version) =>
        version.Split('.')
            .Select(part => int.TryParse(
                part, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                ? number
                : 0)
            .Concat(new[] { 0, 0, 0 })
            .Take(3)
            .ToArray();

    private readonly record struct Candidate(JsonElement Release, string Tag, string Version);

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
/// Check: one GET of the release list, 30 s, GitHub's headers, then the newest
/// Linux release (<see cref="UpdateReleaseParser"/>). A per-user install
/// downloads the tar.gz plus its checksum into the cache directory, verifies
/// the SHA-256, extracts it with the framework's tar reader, then starts
/// <c>/bin/sh install.sh --update --wait-pid N</c> without a shell. A .deb
/// install downloads the package instead, verifies it the same way, saves it
/// in the Downloads directory and hands it to the desktop with <c>xdg-open</c>:
/// the app never runs <c>sudo</c>, <c>pkexec</c>, <c>apt</c> or <c>dpkg -i</c>.
///
/// Every failure deletes what it wrote and returns false, so the panel can say
/// <c>download_failed</c> and clear the pending update.
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

    /// <summary>Amendment 1, item 3: how long xdg-open may take before it is left running.</summary>
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(10);

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
    private readonly Func<string, bool> _open;
    private readonly ICommandRunner _commands;
    private readonly string? _processPath;
    private readonly string? _downloadDirectory;
    private readonly int _ownPid;

    private UpdateRelease? _release;
    private bool? _debInstall;
    private bool _openedPackageInstaller;
    private string? _savedPackagePath;


    public GitHubUpdater(
        string currentVersion,
        string? cacheDirectory = null,
        HttpMessageHandler? handler = null,
        Action<string>? log = null,
        string? architecture = null,
        Func<string, IReadOnlyList<string>, bool>? start = null,
        int ownPid = 0,
        bool? debInstall = null,
        ICommandRunner? commands = null,
        string? processPath = null,
        Func<string, bool>? open = null,
        string? downloadDirectory = null)
    {
        CurrentVersion = currentVersion;
        _log = log;
        _architecture = architecture ?? UpdateAssets.ArchitectureSuffix();
        _cacheDirectory = cacheDirectory ?? DefaultCacheDirectory;
        _start = start ?? StartDetached;
        _open = open ?? OpenWithDesktop;
        _commands = commands ?? new ProcessCommandRunner();
        _processPath = processPath;
        _downloadDirectory = downloadDirectory;
        _debInstall = debInstall;
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

    /// <summary>
    /// True for a package install: the running program is under
    /// <c>/opt/systemmonitor/</c> and <c>dpkg-query</c> reports the package as
    /// installed. Otherwise the update is the per-user tar.gz and
    /// <c>install.sh --update</c>.
    /// </summary>
    public bool DebInstall => _debInstall ??= DebPackage.IsInstalled(
        _processPath ?? Environment.ProcessPath, _commands);

    /// <summary>
    /// True after <see cref="DownloadAndInstallAsync"/> saved a .deb and handed
    /// it to the desktop, so the panel can say the installer was opened rather
    /// than that install.sh was started.
    /// </summary>
    public bool OpenedPackageInstaller => _openedPackageInstaller;

    /// <summary>
    /// Amendment 1, item 3: when the package was saved and verified but the
    /// desktop could not open it, this is the file's path. The panel shows it
    /// with <c>update_deb_saved</c> so the user can open it by hand; the file
    /// is kept. Null when the installer opened or nothing was saved.
    /// </summary>
    public string? SavedPackagePath => _savedPackagePath;

    /// <summary>The files this install looks for in a release.</summary>
    public UpdateAssetSet Assets =>
        DebInstall ? UpdateAssetSet.Deb() : UpdateAssetSet.TarGz(_architecture);

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
                (UpdateRelease? release, string? message, string note) =
                    UpdateReleaseParser.Select(body, Assets);
                if (release is null)
                {
                    // No Linux release at all: up to date, with the note saying
                    // what the list held. Anything else is a failed check.
                    _release = null;
                    return message is null
                        ? new UpdateCheck(true, null, null, null, note)
                        : Fail(message, note);
                }

                _release = release;
                _log?.Invoke(note);
                return new UpdateCheck(true, release.Version, null, release.Tag, note);
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
        _savedPackagePath = null;
        UpdateRelease? release = _release is { } cached
            && string.Equals(cached.Version, version, StringComparison.Ordinal)
            ? cached
            : await ResolveAsync(version, cancellationToken).ConfigureAwait(false);
        if (release is null)
        {
            return false;
        }

        _openedPackageInstaller = false;
        return DebInstall
            ? await DownloadPackageAsync(release, progress, cancellationToken)
                .ConfigureAwait(false)
            : await DownloadArchiveAsync(release, progress, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// A .deb install: fetch the package and its checksum, verify the SHA-256,
    /// save it in the Downloads directory and let the desktop's package
    /// installer open it. Opening is all this does; the authentication prompt
    /// and the install belong to the desktop, never to this program.
    /// </summary>
    private async Task<bool> DownloadPackageAsync(
        UpdateRelease release, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        string package = Path.Combine(_cacheDirectory, release.ArchiveName);
        string checksum = Path.Combine(_cacheDirectory, release.ChecksumName);
        string? destination = null;
        bool wroteDestination = false;

        try
        {
            Directory.CreateDirectory(_cacheDirectory);

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

            if (!await DownloadAsync(release.ArchiveUrl, package, progress, cancellationToken)
                    .ConfigureAwait(false))
            {
                return DownloadFailed(release, checksum, package);
            }

            if (!string.Equals(HashFile(package), expected, StringComparison.OrdinalIgnoreCase))
            {
                _log?.Invoke("update checksum mismatch for " + release.ArchiveName);
                return DownloadFailed(release, checksum, package);
            }

            string directory = _downloadDirectory
                ?? DebPackage.ResolveDownloadDirectory(_commands);
            Directory.CreateDirectory(directory);

            string preferred = Path.Combine(directory, release.ArchiveName);
            if (File.Exists(preferred) && TryHashMatches(preferred, expected))
            {
                // Amendment 1, item 2: a verified package already in Downloads
                // is reused as it is; it is not overwritten or recopied.
                destination = preferred;
                _log?.Invoke("reusing the package already in " + directory);
            }
            else
            {
                (destination, wroteDestination) = SaveNewPackage(package, preferred);
            }

            TryDelete(package);
            TryDelete(checksum);

            if (!_open(destination))
            {
                // Amendment 1, item 3: the file is verified, so a failed
                // opener leaves it in place for the user to open by hand.
                _log?.Invoke("the package installer could not be opened for " + destination);
                _savedPackagePath = destination;
                return false;
            }

            _openedPackageInstaller = true;
            progress?.Report(100);
            _log?.Invoke("opened the package installer for " + release.Version);
            return true;
        }
        catch (OperationCanceledException)
        {
            _log?.Invoke("update download was cancelled");
            if (destination is not null && wroteDestination)
            {
                TryDelete(destination);
            }
            return DownloadFailed(release, checksum, package);
        }
        catch (Exception exception)
        {
            _log?.Invoke("update download failed: " + exception.Message);
            if (destination is not null && wroteDestination)
            {
                TryDelete(destination);
            }
            return DownloadFailed(release, checksum, package);
        }
    }

    /// <summary>
    /// A per-user install: fetch the archive and its checksum, verify the
    /// SHA-256, unpack it and start its own <c>install.sh --update</c>.
    /// </summary>
    private async Task<bool> DownloadArchiveAsync(
        UpdateRelease release, IProgress<int>? progress, CancellationToken cancellationToken)
    {
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

    private UpdateCheck Fail(string reason, string? detail = null)
    {
        _log?.Invoke("update check failed: " + reason);
        return new UpdateCheck(false, null, reason, null, detail);
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>True when the file's SHA-256 matches; false when it cannot be read.</summary>
    private static bool TryHashMatches(string path, string expected)
    {
        try
        {
            return string.Equals(HashFile(path), expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Amendment 1, item 2: never overwrite a file the app did not create.
    /// The preferred name is used when it is free; otherwise the first free
    /// <c>name (n).ext</c> is. <see cref="FileMode.CreateNew"/> also settles
    /// the race between two runs.
    /// </summary>
    private static (string Path, bool Written) SaveNewPackage(string source, string preferred)
    {
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            string candidate = attempt == 0 ? preferred : NumberedName(preferred, attempt);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                continue;
            }

            FileStream output;
            try
            {
                output = new FileStream(
                    candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate))
            {
                continue;
            }

            try
            {
                using (output)
                using (FileStream input = File.OpenRead(source))
                {
                    input.CopyTo(output);
                }
                return (candidate, true);
            }
            catch (Exception)
            {
                // Only the file this run created is removed.
                TryDelete(candidate);
                throw;
            }
        }

        throw new IOException("no free file name for " + preferred);
    }

    /// <summary>"x.deb" with 2 becomes "x (2).deb".</summary>
    private static string NumberedName(string path, int number)
    {
        string? directory = Path.GetDirectoryName(path);
        string name =
            Path.GetFileNameWithoutExtension(path) +
            " (" + number.ToString(CultureInfo.InvariantCulture) + ")" +
            Path.GetExtension(path);
        return directory is null ? name : Path.Combine(directory, name);
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

    /// <summary>
    /// <c>xdg-open &lt;file&gt;</c> with the path as one argv entry, never through
    /// a shell. The desktop decides what opens it; on a desktop with Discover
    /// or GNOME Software that is the package installer, which asks for
    /// authentication itself.
    ///
    /// Amendment 1, item 3: wait up to ten seconds for the handler. Exit code
    /// 0 means it opened; a non-zero exit or a missing <c>xdg-open</c> means it
    /// did not. A handler that is still running after the timeout counts as
    /// opened, because some block until the package manager closes.
    /// </summary>
    private static bool OpenWithDesktop(string path)
    {
        try
        {
            var info = new ProcessStartInfo("xdg-open")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add(path);

            using Process? process = Process.Start(info);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit((int)OpenTimeout.TotalMilliseconds))
            {
                // Still running: a blocking handler, not a failure.
                return true;
            }
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            // xdg-open is missing or could not be started.
            return false;
        }
    }
}

/// <summary>
/// What makes this run a .deb install, and where a downloaded package goes.
///
/// Both checks are needed: the path alone would be true for an unpacked
/// <c>/opt/systemmonitor</c> that was copied by hand, and the package alone
/// would be true while a per-user copy runs beside the packaged one.
/// </summary>
public static class DebPackage
{
    /// <summary>Where the .deb puts the program; <c>/usr/bin/sysmonitor</c> links here.</summary>
    public const string Root = "/opt/systemmonitor/";

    public const string Name = "systemmonitor";

    /// <summary>True when <paramref name="processPath"/> is the packaged binary.</summary>
    public static bool IsUnderRoot(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }
        try
        {
            return Path.GetFullPath(processPath).StartsWith(Root, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary><c>dpkg-query</c> reports the package as installed for this user's database.</summary>
    public static bool IsRegistered(ICommandRunner commands) =>
        string.Equals(
            commands.Run(
                "dpkg-query",
                new[] { "-W", "-f=${Status}", Name },
                4000)?.Trim(),
            "install ok installed",
            StringComparison.Ordinal);

    public static bool IsInstalled(string? processPath, ICommandRunner commands) =>
        IsUnderRoot(processPath) && IsRegistered(commands);

    /// <summary>
    /// <c>$XDG_DOWNLOAD_DIR</c>, else <c>xdg-user-dir DOWNLOAD</c>, else
    /// <c>~/Downloads</c>. <c>xdg-user-dir</c> answers with the home directory
    /// when no download directory is configured, which is not a place to leave
    /// a package: that answer falls back too.
    /// </summary>
    public static string ResolveDownloadDirectory(ICommandRunner commands)
    {
        string? xdg = Environment.GetEnvironmentVariable("XDG_DOWNLOAD_DIR");
        if (!string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg))
        {
            return xdg;
        }

        string home = HomeDirectory();
        string? answer = commands.Run("xdg-user-dir", new[] { "DOWNLOAD" }, 4000)?.Trim();
        if (!string.IsNullOrEmpty(answer)
            && Path.IsPathRooted(answer)
            && !string.Equals(Path.TrimEndingDirectorySeparator(answer),
                Path.TrimEndingDirectorySeparator(home), StringComparison.Ordinal))
        {
            return answer;
        }
        return Path.Combine(home, "Downloads");
    }

    private static string HomeDirectory()
    {
        string? home = Environment.GetEnvironmentVariable("HOME");
        return string.IsNullOrWhiteSpace(home)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : home;
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
