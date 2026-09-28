using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace SysMonitor;

/// <summary>
/// Checks GitHub for a newer release and fetches its installer.
///
/// Through <see cref="HttpClient"/> and <see cref="JsonSerializer"/>, both in
/// the class library, so the rule about shipping no packages holds. The whole
/// exchange is two requests: one for the release metadata, one for the file.
///
/// Nothing happens without the user asking. An updater that reaches out on its
/// own is a network call the person did not make and a surprise dialog while
/// they are working; this one runs when the button is pressed and is silent
/// otherwise.
/// </summary>
internal static class Updater
{
    private const string Api =
        "https://api.github.com/repos/epinephrinerx/SystemMonitor/releases/latest";

    /// <summary>
    /// Hosts a download may come from. GitHub serves release assets by
    /// redirecting to its object store, so both are needed -- and nothing
    /// else is, which is the point of checking: the release metadata is
    /// fetched over the network, so the URL inside it is not ours to trust
    /// blindly, and this is an executable we are about to run.
    /// </summary>
    private static readonly string[] AllowedHosts =
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    };

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // The GitHub API rejects a request with no user agent outright.
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("SysMonitor", Current.ToString()));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>This build's version, as the release tags write it.</summary>
    public static Version Current =>
        Assembly.GetExecutingAssembly().GetName().Version is Version version
            ? new Version(version.Major, version.Minor, version.Build)
            : new Version(0, 0, 0);

    /// <summary>What a check found. Null <see cref="Version"/> means nothing newer.</summary>
    public sealed record Release(Version Version, string Name, string DownloadUrl, long Bytes);

    /// <summary>
    /// Ask GitHub what the newest release is.
    ///
    /// Returns null when the check could not be made at all -- no network, a
    /// rate limit, a repository that has no releases yet -- which is different
    /// from "you are up to date" and is reported differently.
    /// </summary>
    public static async Task<Release?> CheckAsync(CancellationToken token = default)
    {
        try
        {
            string json = await Http.GetStringAsync(Api, token).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("tag_name", out JsonElement tag)
                || tag.GetString() is not string name
                || ParseTag(name) is not Version version)
            {
                return null;
            }
            if (!root.TryGetProperty("assets", out JsonElement assets)
                || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string? file = asset.TryGetProperty("name", out JsonElement n)
                    ? n.GetString() : null;
                string? url = asset.TryGetProperty("browser_download_url", out JsonElement u)
                    ? u.GetString() : null;

                // The installer, not the bare executable: replacing a running
                // program's own file is what the installer knows how to do.
                if (file is null || url is null
                    || !file.StartsWith("SysMonitor-Setup-", StringComparison.OrdinalIgnoreCase)
                    || !file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || !IsAllowed(url))
                {
                    continue;
                }
                long bytes = asset.TryGetProperty("size", out JsonElement s)
                             && s.TryGetInt64(out long value) ? value : 0;
                return new Release(version, file, url, bytes);
            }
            return null;
        }
        catch (Exception error)
        {
            Diag.ReportException("Update check", error);
            return null;
        }
    }

    /// <summary>
    /// "v3.2.1" and "3.2.1" both mean the same release.
    ///
    /// Whitespace comes off first: stripping the 'v' from a string that starts
    /// with a space strips nothing, and what is left still starts with a 'v'
    /// once the space goes.
    /// </summary>
    internal static Version? ParseTag(string tag)
    {
        string text = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out Version? version)
            ? new Version(version.Major, version.Minor, Math.Max(version.Build, 0))
            : null;
    }

    internal static bool IsAllowed(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fetch the installer into the temp directory and hand back its path.
    ///
    /// Written under a name of our own making rather than one taken from the
    /// response: a filename that arrived over the network has no business
    /// deciding where a file lands.
    /// </summary>
    public static async Task<string?> DownloadAsync(Release release,
                                                    IProgress<double>? progress = null,
                                                    CancellationToken token = default)
    {
        if (!IsAllowed(release.DownloadUrl))
        {
            return null;
        }

        string path = Path.Combine(Path.GetTempPath(),
            $"SysMonitor-Setup-{release.Version}.exe");
        try
        {
            using HttpResponseMessage response = await Http
                .GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? release.Bytes;
            await using (Stream source = await response.Content
                             .ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write,
                                                   FileShare.None))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    done += read;
                    if (total > 0)
                    {
                        progress?.Report((double)done / total);
                    }
                }
            }
            return path;
        }
        catch (Exception error)
        {
            Diag.ReportException("Update download", error);
            try
            {
                File.Delete(path);      // a half-written installer must not be run
            }
            catch (Exception)
            {
                // Nothing to do about it; it is in the temp directory.
            }
            return null;
        }
    }
}
