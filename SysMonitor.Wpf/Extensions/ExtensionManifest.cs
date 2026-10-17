using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SysMonitor.Extensions;

/// <summary>
/// Manifest parsed from extension.json describing an extension package.
/// </summary>
public sealed class ExtensionManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("contract")]
    public string Contract { get; set; } = string.Empty;

    [JsonPropertyName("min_host")]
    public string? MinHost { get; set; }

    [JsonPropertyName("entry")]
    public ExtensionEntry Entry { get; set; } = new();

    [JsonPropertyName("files")]
    public List<ExtensionFile> Files { get; set; } = new();

    private static readonly Regex IdRegex = new(@"^[a-z0-9][a-z0-9._-]{0,63}\z", RegexOptions.Compiled);
    private static readonly Regex Sha256Regex = new(@"^[a-fA-F0-9]{64}$", RegexOptions.Compiled);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static bool TryParse(
        byte[] manifestBytes,
        out ExtensionManifest? manifest,
        out string? error)
    {
        manifest = null;
        error = null;

        try
        {
            manifest = JsonSerializer.Deserialize<ExtensionManifest>(manifestBytes, JsonOptions);
        }
        catch (Exception ex)
        {
            error = $"JSON parsing failed: {ex.Message}";
            return false;
        }

        if (manifest == null)
        {
            error = "Manifest JSON parsed to null";
            return false;
        }

        return Validate(manifest, out error);
    }

    public static bool Validate(ExtensionManifest manifest, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(manifest.Id) || !IdRegex.IsMatch(manifest.Id))
        {
            error = $"Invalid or missing 'id' ('{manifest.Id}'). Must match ^[a-z0-9][a-z0-9._-]{{0,63}}$";
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            error = "Manifest 'name' is missing or empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            error = "Manifest 'version' is missing or empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.Contract) || !System.Version.TryParse(manifest.Contract, out _))
        {
            error = $"Manifest 'contract' ('{manifest.Contract}') is missing or invalid";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(manifest.MinHost) && !System.Version.TryParse(manifest.MinHost, out _))
        {
            error = $"Manifest 'min_host' ('{manifest.MinHost}') is invalid";
            return false;
        }

        if (manifest.Entry == null)
        {
            error = "Manifest 'entry' section is missing";
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.Entry.Assembly))
        {
            error = "Manifest 'entry.assembly' is missing or empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.Entry.Type))
        {
            error = "Manifest 'entry.type' is missing or empty";
            return false;
        }

        if (manifest.Files == null || manifest.Files.Count == 0)
        {
            error = "Manifest 'files' list is missing or empty";
            return false;
        }

        if (manifest.Files.Any(f => f == null))
        {
            error = "Manifest 'files' list contains null entry";
            return false;
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenLeaves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (!string.IsNullOrWhiteSpace(file.Path))
            {
                string normalized = file.Path.Replace('\\', '/');
                string leaf = normalized[(normalized.LastIndexOf('/') + 1)..];
                if (!seenPaths.Add(normalized) || !seenLeaves.Add(leaf))
                {
                    error = $"File entry '{file.Path}' duplicates another entry (same path or same file name)";
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(file.Path))
            {
                error = "File entry has missing or empty 'path'";
                return false;
            }

            if (string.IsNullOrWhiteSpace(file.Sha256) || !Sha256Regex.IsMatch(file.Sha256))
            {
                error = $"File entry '{file.Path}' has invalid sha256 ('{file.Sha256}'). Must be 64 hex characters";
                return false;
            }
        }

        return true;
    }
}

public sealed class ExtensionEntry
{
    [JsonPropertyName("assembly")]
    public string Assembly { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

public sealed class ExtensionFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("native")]
    public bool Native { get; set; } = false;
}
