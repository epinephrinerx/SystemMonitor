using System.IO;
using System.Reflection;
using System.Text.Json;

namespace SysMonitor.Extensions;

/// <summary>
/// Allowlist of first-party extension manifest SHA-256 hashes.
/// </summary>
public sealed class FirstPartyAllowlist
{
    public static readonly FirstPartyAllowlist Empty = new(Array.Empty<string>());

    private readonly HashSet<string> _hashes = new(StringComparer.OrdinalIgnoreCase);

    public FirstPartyAllowlist(IEnumerable<string>? hashes)
    {
        if (hashes != null)
        {
            foreach (string h in hashes)
            {
                if (!string.IsNullOrWhiteSpace(h))
                {
                    _hashes.Add(h.Trim());
                }
            }
        }
    }

    public bool IsFirstParty(string? manifestSha256)
    {
        if (string.IsNullOrWhiteSpace(manifestSha256))
        {
            return false;
        }

        return _hashes.Contains(manifestSha256.Trim());
    }

    public static FirstPartyAllowlist FromAssembly(Assembly assembly, Action<string>? log = null)
    {
        const string ResourceName = "first-party-extensions.json";
        string? name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.Equals(ResourceName, StringComparison.OrdinalIgnoreCase)
                              || n.EndsWith("." + ResourceName, StringComparison.OrdinalIgnoreCase));

        if (name == null)
        {
            return Empty;
        }

        try
        {
            using Stream? stream = assembly.GetManifestResourceStream(name);
            if (stream == null)
            {
                return Empty;
            }

            using var reader = new StreamReader(stream);
            string json = reader.ReadToEnd();
            var hashes = JsonSerializer.Deserialize<List<string>>(json);
            return new FirstPartyAllowlist(hashes ?? Enumerable.Empty<string>());
        }
        catch (Exception ex)
        {
            log.SafeLog($"Failed to parse embedded {ResourceName}: {ex.Describe()}");
            return Empty;
        }
    }
}
