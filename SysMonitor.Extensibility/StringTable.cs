namespace SysMonitor.Extensibility;

/// <summary>
/// Helper for extensions to manage localized strings across multiple languages (e.g. "th" and "en").
/// </summary>
public sealed class StringTable
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _data;

    /// <summary>
    /// Initializes a new instance of <see cref="StringTable"/> with localized string dictionaries.
    /// </summary>
    /// <param name="data">Mapping of language code -> (key -> translated string).</param>
    public StringTable(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>
    /// Read-only access to the underlying language tables.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Data => _data;

    /// <summary>
    /// Gets the localized string for the specified language and key.
    /// If the language is not recognized, falls back to "en".
    /// If the key is not found in either the requested language or "en", returns the key itself.
    /// </summary>
    /// <param name="language">Language code ("th" or "en").</param>
    /// <param name="key">String key.</param>
    /// <returns>Translated string, or key if not found.</returns>
    public string Get(string language, string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        // Language codes are lower case ("th", "en"); callers may pass "TH".
        if (!string.IsNullOrEmpty(language)
            && _data.TryGetValue(language.ToLowerInvariant(), out var langDict)
            && langDict.TryGetValue(key, out var val))
        {
            return val;
        }

        if (_data.TryGetValue("en", out var enDict)
            && enDict.TryGetValue(key, out var enVal))
        {
            return enVal;
        }

        return key;
    }

    /// <summary>
    /// Returns all keys that exist in at least one language but are missing in another language.
    /// Used for parity testing in extension test suites.
    /// </summary>
    /// <param name="table">The string table to check.</param>
    /// <returns>A list of keys missing in one or more languages.</returns>
    public static IReadOnlyList<string> MissingKeys(StringTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var data = table._data;
        if (data.Count < 2)
        {
            return Array.Empty<string>();
        }

        var allKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dict in data.Values)
        {
            foreach (var key in dict.Keys)
            {
                allKeys.Add(key);
            }
        }

        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in allKeys)
        {
            foreach (var dict in data.Values)
            {
                if (!dict.ContainsKey(key))
                {
                    missing.Add(key);
                    break;
                }
            }
        }

        return missing.ToList();
    }
}
