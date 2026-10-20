using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SysMonitor.Extensibility;
using SysMonitor.Extensions;

namespace SysMonitor.PackTool;

/// <summary>
/// Options passed to <see cref="ExtensionPacker.Pack"/>.
/// </summary>
public sealed record PackOptions(
    string TemplatePath,
    string OutDir,
    string Version,
    IReadOnlyDictionary<string, string> FileSources,
    string? AllowlistPath = null
);

/// <summary>
/// Result of a successful pack operation.
/// </summary>
public sealed record PackResult(
    string Id,
    string ManifestSha256,
    IReadOnlyList<string> WrittenFiles,
    ExtensionManifest Manifest
);

/// <summary>
/// Thrown when packing fails due to invalid template, missing files, or IO errors.
/// </summary>
public sealed class PackException : Exception
{
    public PackException(string message) : base(message) { }
    public PackException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// In-memory representation of an extension.template.json file.
/// </summary>
public sealed class ExtensionTemplate
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

    [JsonPropertyName("provides")]
    public List<string>? Provides { get; set; }

    [JsonPropertyName("place_after")]
    public string? PlaceAfter { get; set; }

    [JsonPropertyName("files")]
    public List<TemplateFile> Files { get; set; } = new();
}

/// <summary>
/// A file entry in an extension.template.json file.
/// </summary>
public sealed class TemplateFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("native")]
    public bool Native { get; set; } = false;

    [JsonPropertyName("optional")]
    public bool Optional { get; set; } = false;
}

/// <summary>
/// Logic for validating templates, copying extension files, computing hashes,
/// generating extension.json, and updating the first-party allowlist.
/// </summary>
public static class ExtensionPacker
{
    public static PackResult Pack(
        string templatePath,
        string outDir,
        string version,
        IReadOnlyDictionary<string, string>? fileSources = null,
        string? allowlistPath = null)
    {
        return Pack(new PackOptions(
            templatePath,
            outDir,
            version,
            fileSources ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            allowlistPath));
    }

    public static PackResult Pack(PackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.TemplatePath))
        {
            throw new PackException("Template path must not be empty.");
        }
        if (string.IsNullOrWhiteSpace(options.OutDir))
        {
            throw new PackException("Output directory must not be empty.");
        }
        if (string.IsNullOrWhiteSpace(options.Version))
        {
            throw new PackException("Version must not be empty.");
        }

        if (!File.Exists(options.TemplatePath))
        {
            throw new PackException($"Template file does not exist: '{options.TemplatePath}'");
        }

        string templateJson;
        try
        {
            templateJson = File.ReadAllText(options.TemplatePath);
        }
        catch (Exception ex)
        {
            throw new PackException($"Failed to read template file '{options.TemplatePath}': {ex.Message}", ex);
        }

        ExtensionTemplate? template;
        try
        {
            template = JsonSerializer.Deserialize<ExtensionTemplate>(templateJson, ExtensionManifest.JsonOptions);
        }
        catch (Exception ex)
        {
            throw new PackException($"Failed to parse template JSON in '{options.TemplatePath}': {ex.Message}", ex);
        }

        if (template == null)
        {
            throw new PackException($"Template JSON in '{options.TemplatePath}' deserialized to null.");
        }

        // Replace literal "$VERSION" with --version
        if (string.Equals(template.Version, "$VERSION", StringComparison.Ordinal))
        {
            template.Version = options.Version.Trim();
        }
        else if (template.Version != null && template.Version.Contains("$VERSION"))
        {
            template.Version = template.Version.Replace("$VERSION", options.Version.Trim());
        }

        // Validate contract compatibility with ContractInfo.Current
        if (string.IsNullOrWhiteSpace(template.Contract) || !Version.TryParse(template.Contract, out Version? templateContract))
        {
            throw new PackException($"Invalid or missing contract version '{template.Contract}' in template '{options.TemplatePath}'.");
        }

        Version currentContract = ContractInfo.Current;
        if (templateContract.Major != currentContract.Major || templateContract.Minor > currentContract.Minor)
        {
            throw new PackException(
                $"Template contract '{template.Contract}' is incompatible with host contract '{currentContract}'. " +
                $"Major must equal {currentContract.Major} and minor must be <= {currentContract.Minor}.");
        }

        // Validate template files collection
        if (template.Files == null || template.Files.Count == 0)
        {
            throw new PackException($"Template '{options.TemplatePath}' must declare at least one file in 'files'.");
        }

        var templateFilesByPath = new Dictionary<string, TemplateFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var tf in template.Files)
        {
            if (string.IsNullOrWhiteSpace(tf.Path))
            {
                throw new PackException("Template contains a file entry with empty 'path'.");
            }
            string normPath = tf.Path.Replace('\\', '/');
            if (Path.IsPathRooted(normPath) || normPath.Split('/').Any(part => part is "" or "." or ".."))
            {
                throw new PackException($"Template file path '{tf.Path}' must be a plain relative path inside the package.");
            }
            // Windows rewrites a trailing dot or space and ':' opens a data stream; the loader refuses them too.
            if (normPath.Contains(':') || normPath.Split('/').Any(part => part.EndsWith('.') || part.EndsWith(' ')))
            {
                throw new PackException($"Template file path '{tf.Path}' contains characters Windows rewrites or the loader forbids.");
            }
            // The tool writes extension.json itself, after hashing the other files.
            if (string.Equals(normPath, "extension.json", StringComparison.OrdinalIgnoreCase))
            {
                throw new PackException("Template must not list 'extension.json': the tool generates it.");
            }
            if (templateFilesByPath.ContainsKey(normPath))
            {
                throw new PackException($"Template contains duplicate file entry: '{tf.Path}'.");
            }
            templateFilesByPath[normPath] = tf;
        }

        // Same rule as the loader (EntryNotFound): the entry assembly must be a listed, managed file.
        string entryAssembly = (template.Entry?.Assembly ?? string.Empty).Replace('\\', '/');
        if (!templateFilesByPath.TryGetValue(entryAssembly, out TemplateFile? entryFile) || entryFile.Native)
        {
            throw new PackException(
                $"Template entry assembly '{template.Entry?.Assembly}' must be listed in 'files' and must not be native.");
        }
        if (entryFile.Optional)
        {
            throw new PackException($"Template entry assembly '{template.Entry?.Assembly}' cannot be optional.");
        }

        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options.FileSources != null)
        {
            foreach (var (k, v) in options.FileSources)
            {
                string normKey = k.Replace('\\', '/');
                sources[normKey] = v;
            }
        }

        // 1. Any supplied source for a name not in the template -> error naming the file
        foreach (var sourceKey in sources.Keys)
        {
            if (!templateFilesByPath.ContainsKey(sourceKey))
            {
                throw new PackException($"File source provided for unknown template file: '{sourceKey}'.");
            }
        }

        // 2. Check each template file:
        //    - Non-optional without source -> error naming the file
        //    - Source path does not exist -> error naming the file
        //    - Optional without source -> left out of manifest and not copied
        var filesToCopy = new List<(TemplateFile TemplateFile, string SourcePath)>();
        foreach (var tf in template.Files)
        {
            string normPath = tf.Path.Replace('\\', '/');
            if (sources.TryGetValue(normPath, out string? sourcePath))
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    throw new PackException($"Source file does not exist for template file '{tf.Path}': '{sourcePath}'.");
                }
                filesToCopy.Add((tf, sourcePath));
            }
            else
            {
                if (!tf.Optional)
                {
                    throw new PackException($"Missing required file source for non-optional template file: '{tf.Path}'.");
                }
                // Optional and not supplied: omit from manifest and do not copy
            }
        }

        // 3. Output directory handling:
        //    Must create it; if it already contains anything, exit 1 so stale files can never survive
        string fullOutDir = Path.GetFullPath(options.OutDir);
        // The allowlist is written after the files are hashed; inside the package it would change one.
        if (!string.IsNullOrWhiteSpace(options.AllowlistPath))
        {
            string fullAllowlist = Path.GetFullPath(options.AllowlistPath);
            string outPrefix = fullOutDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullAllowlist.StartsWith(outPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new PackException("The allowlist file must not be inside the package output directory.");
            }
        }
        if (Directory.Exists(fullOutDir))
        {
            if (Directory.EnumerateFileSystemEntries(fullOutDir).Any())
            {
                throw new PackException($"Output directory '{fullOutDir}' already exists and is not empty.");
            }
        }
        else
        {
            try
            {
                Directory.CreateDirectory(fullOutDir);
            }
            catch (Exception ex)
            {
                throw new PackException($"Failed to create output directory '{fullOutDir}': {ex.Message}", ex);
            }
        }

        // 4. Copy supplied files and compute SHA-256
        var writtenFiles = new List<string>();
        var manifestFiles = new List<ExtensionFile>();

        foreach (var (tf, src) in filesToCopy)
        {
            string normRelPath = tf.Path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string destPath = Path.Combine(fullOutDir, normRelPath);

            string? destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            try
            {
                File.Copy(src, destPath, overwrite: false);
            }
            catch (Exception ex)
            {
                throw new PackException($"Failed to copy '{src}' to '{destPath}': {ex.Message}", ex);
            }

            byte[] copiedBytes;
            try
            {
                copiedBytes = File.ReadAllBytes(destPath);
            }
            catch (Exception ex)
            {
                throw new PackException($"Failed to read copied file '{destPath}': {ex.Message}", ex);
            }

            string sha256 = ComputeSha256Hex(copiedBytes);

            manifestFiles.Add(new ExtensionFile
            {
                Path = tf.Path.Replace('\\', '/'),
                Sha256 = sha256,
                Native = tf.Native
            });

            writtenFiles.Add(tf.Path.Replace('\\', '/'));
        }

        // 5. Build ExtensionManifest (in template order)
        var manifest = new ExtensionManifest
        {
            Id = template.Id,
            Name = template.Name,
            Version = template.Version ?? string.Empty,
            Contract = template.Contract,
            MinHost = template.MinHost,
            Entry = new ExtensionEntry
            {
                Assembly = template.Entry.Assembly,
                Type = template.Entry.Type
            },
            Provides = template.Provides ?? new List<string>(),
            PlaceAfter = template.PlaceAfter,
            Files = manifestFiles
        };

        if (!ExtensionManifest.Validate(manifest, out string? valError))
        {
            throw new PackException($"Manifest validation failed for extension '{template.Id}': {valError}");
        }

        // 6. Serialize with ExtensionManifest.JsonOptions indented, UTF-8 without BOM, \n line ends
        var writeOptions = new JsonSerializerOptions(ExtensionManifest.JsonOptions)
        {
            WriteIndented = true
        };
        string json = JsonSerializer.Serialize(manifest, writeOptions);
        json = json.Replace("\r\n", "\n");
        if (!json.EndsWith('\n'))
        {
            json += "\n";
        }
        byte[] manifestBytes = new UTF8Encoding(false).GetBytes(json);

        // Re-read with ExtensionManifest.TryParse and fail if it does not validate
        if (!ExtensionManifest.TryParse(manifestBytes, out var reparsed, out string? parseError) || reparsed == null)
        {
            throw new PackException($"Failed to validate serialized extension.json on re-read: {parseError}");
        }

        string manifestFilePath = Path.Combine(fullOutDir, "extension.json");
        try
        {
            File.WriteAllBytes(manifestFilePath, manifestBytes);
        }
        catch (Exception ex)
        {
            throw new PackException($"Failed to write '{manifestFilePath}': {ex.Message}", ex);
        }
        writtenFiles.Add("extension.json");

        string manifestSha256 = ComputeSha256Hex(manifestBytes);

        // 7. Update allowlist if requested
        if (!string.IsNullOrWhiteSpace(options.AllowlistPath))
        {
            UpdateAllowlist(options.AllowlistPath, manifestSha256);
        }

        return new PackResult(manifest.Id, manifestSha256, writtenFiles, manifest);
    }

    /// <summary>
    /// Reads the allowlist JSON array (missing file = empty array; malformed = PackException),
    /// adds the manifest SHA-256 if not present, and writes the array back sorted ordinal,
    /// indented, UTF-8 no BOM.
    /// </summary>
    public static void UpdateAllowlist(string allowlistPath, string manifestSha256)
    {
        if (string.IsNullOrWhiteSpace(allowlistPath))
        {
            throw new ArgumentException("Allowlist path must not be empty.", nameof(allowlistPath));
        }
        if (string.IsNullOrWhiteSpace(manifestSha256))
        {
            throw new ArgumentException("Manifest SHA-256 must not be empty.", nameof(manifestSha256));
        }

        string normHash = manifestSha256.Trim().ToLowerInvariant();
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(allowlistPath))
        {
            string content;
            try
            {
                content = File.ReadAllText(allowlistPath);
            }
            catch (Exception ex)
            {
                throw new PackException($"Failed to read allowlist file '{allowlistPath}': {ex.Message}", ex);
            }

            List<string>? list;
            try
            {
                list = JsonSerializer.Deserialize<List<string>>(content, ExtensionManifest.JsonOptions);
            }
            catch (Exception ex)
            {
                throw new PackException($"Malformed allowlist JSON in '{allowlistPath}': {ex.Message}", ex);
            }

            if (list == null)
            {
                throw new PackException($"Allowlist JSON in '{allowlistPath}' deserialized to null.");
            }

            foreach (string item in list)
            {
                if (item == null)
                {
                    throw new PackException($"Allowlist JSON in '{allowlistPath}' contains null entry.");
                }
                string h = item.Trim().ToLowerInvariant();
                if (h.Length != 64 || !h.All(Uri.IsHexDigit))
                {
                    throw new PackException($"Allowlist entry '{item}' in '{allowlistPath}' is not a valid 64-character hex hash.");
                }
                hashes.Add(h);
            }
        }

        hashes.Add(normHash);

        var sorted = hashes.OrderBy(h => h, StringComparer.Ordinal).ToList();
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(sorted, options);
        json = json.Replace("\r\n", "\n");
        if (!json.EndsWith('\n'))
        {
            json += "\n";
        }

        string? dir = Path.GetDirectoryName(Path.GetFullPath(allowlistPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(allowlistPath, new UTF8Encoding(false).GetBytes(json));
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
