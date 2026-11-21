using System.Globalization;
using System.Text;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// The Apps page's source: the desktop session's <c>app.slice</c> cgroups, each
/// unit resolved to a <c>.desktop</c> file, with the processes read separately by
/// <see cref="ProcessStats"/>.
///
/// A top-level <c>app-*.scope</c> or <c>app-*.service</c> is one instance. Two
/// unit spellings that resolve to the same desktop file (<c>dolphin@…</c> and
/// <c>org.kde.dolphin@…</c>) become one app; an unresolved id is its own app.
/// </summary>
public sealed class AppResolver
{
    private static readonly string[] Launchers = { "gnome", "kde", "flatpak" };

    private readonly string _root;
    private readonly int _uid;
    private readonly IReadOnlyList<string> _searchDirectories;
    private readonly string _language;

    /// <param name="root">The root of the tree; "/" on a real machine.</param>
    /// <param name="uid">The user whose <c>app.slice</c> is read; this process's real UID by default.</param>
    /// <param name="searchDirectories">
    /// The <c>applications</c> directories to search, in XDG order. Null uses
    /// <c>$XDG_DATA_HOME</c>, <c>$XDG_DATA_DIRS</c> and the Flatpak exports.
    /// </param>
    /// <param name="language">The app language; <c>th</c> prefers <c>Name[th]</c>.</param>
    public AppResolver(
        string root = "/",
        int? uid = null,
        IReadOnlyList<string>? searchDirectories = null,
        string language = "en")
    {
        _root = string.IsNullOrEmpty(root) ? "/" : root;
        _uid = uid ?? Posix.Uid;
        _searchDirectories = searchDirectories ?? DefaultSearchDirectories();
        _language = string.Equals(language, "th", StringComparison.OrdinalIgnoreCase)
            ? "th"
            : "en";
    }

    /// <summary>Every app with at least one live process, sorted by name.</summary>
    public IReadOnlyList<AppEntry> Read(IReadOnlyList<ProcessRow> processes)
    {
        var byPid = new Dictionary<int, ProcessRow>();
        foreach (ProcessRow process in processes)
        {
            byPid[process.Pid] = process;
        }

        string appSlice = Path.Combine(
            _root,
            "sys",
            "fs",
            "cgroup",
            "user.slice",
            $"user-{_uid}.slice",
            $"user@{_uid}.service",
            "app.slice");

        var resolved = new Dictionary<string, ResolvedDesktop?>(StringComparer.Ordinal);
        var units = new List<UnitScan>();

        foreach (string directory in SysFs.Directories(appSlice))
        {
            string unit = Path.GetFileName(directory);
            if (ParseUnit(unit) is not UnitName parsed)
            {
                continue;
            }

            ResolvedDesktop? desktop = parsed.Unresolved ? null : Resolve(parsed.Id, resolved);
            if (!parsed.Unresolved
                && desktop is null
                && StripLauncher(parsed.Id, resolved) is { } stripped)
            {
                parsed = parsed with { Id = stripped.Id };
                desktop = stripped.Desktop;
            }

            List<ProcessRow> rows = PidsOf(directory)
                .Distinct()
                .Select(pid => byPid.GetValueOrDefault(pid))
                .Where(row => row is not null)
                .Select(row => row!)
                .OrderBy(row => row.Pid)
                .ToList();

            if (rows.Count == 0)
            {
                // An instance with no live process is not an app.
                continue;
            }

            units.Add(new UnitScan(unit, parsed.Id, parsed.Instance, desktop, rows));
        }

        var groups = units
            .GroupBy(GroupKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        var apps = new List<AppEntry>();
        foreach (IGrouping<string, UnitScan> group in groups)
        {
            UnitScan first = group.First();
            var instances = group
                .Select(unit => Instance(unit.Unit, unit.Rows))
                .OrderBy(instance => instance.Unit, StringComparer.Ordinal)
                .ToList();

            List<ProcessRow> members = group.SelectMany(unit => unit.Rows).ToList();
            bool autostart = group.Any(unit =>
                string.Equals(unit.Instance, "autostart", StringComparison.Ordinal));

            string id = first.Desktop is { } desktop
                ? Path.GetFileNameWithoutExtension(desktop.Path)
                : first.Id;
            string name = first.Desktop?.Name is { Length: > 0 } resolvedName
                ? resolvedName
                : id;

            apps.Add(new AppEntry
            {
                Id = id,
                Name = name,
                Icon = first.Desktop?.Icon,
                DesktopFile = first.Desktop?.Path,
                Exec = first.Desktop?.Exec,
                Autostart = autostart,
                Instances = instances,
                CpuPercent = SumCpu(members),
                MemoryBytes = members.Sum(row => row.MemoryBytes),
                GpuPercent = SumGpu(members),
                GpuMemoryBytes = SumGpuMemory(members),
            });
        }

        return apps
            .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(app => app.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Parse one top-level unit name. The instance is the trailing
    /// <c>-&lt;digits&gt;</c> of a scope or the <c>@&lt;x&gt;</c> of a service;
    /// systemd's <c>\xNN</c> escaping is decoded afterwards, so an escaped dash
    /// is never mistaken for the instance separator. A name whose escapes are
    /// not valid UTF-8 keeps the raw text, is marked <see cref="UnitName.Unresolved"/>,
    /// and is never looked up (Amendment 3 item 2).
    /// </summary>
    public static UnitName? ParseUnit(string unit)
    {
        if (!unit.StartsWith("app-", StringComparison.Ordinal))
        {
            return null;
        }

        string stem;
        bool service;
        if (unit.EndsWith(".scope", StringComparison.Ordinal))
        {
            stem = unit["app-".Length..^".scope".Length];
            service = false;
        }
        else if (unit.EndsWith(".service", StringComparison.Ordinal))
        {
            stem = unit["app-".Length..^".service".Length];
            service = true;
        }
        else
        {
            return null;
        }

        string idText = stem;
        string instance = string.Empty;

        if (service)
        {
            int at = stem.LastIndexOf('@');
            if (at > 0)
            {
                idText = stem[..at];
                instance = stem[(at + 1)..];
            }
        }
        else
        {
            int dash = stem.LastIndexOf('-');
            if (dash > 0
                && dash + 1 < stem.Length
                && stem[(dash + 1)..].All(c => c is >= '0' and <= '9'))
            {
                idText = stem[..dash];
                instance = stem[(dash + 1)..];
            }
        }

        string rawId = idText;
        string? id = UnescapeUnit(idText);
        string? decodedInstance = UnescapeUnit(instance);
        if (id is null || decodedInstance is null)
        {
            // Amendment 2 item 5 / Amendment 3 item 2: the escapes are bytes of
            // a UTF-8 name. One bad run anywhere — in the id or in the instance —
            // makes the whole unit unresolved, so it keeps the raw, still-escaped
            // text, is never looked up, and is never signalled as another app.
            return new UnitName(rawId, instance, service, Unresolved: true);
        }

        if (id.Length == 0
            || id.Contains('/')
            || id.Contains('\\')
            || id.Contains('\0'))
        {
            return null;
        }

        return new UnitName(id, decodedInstance, service);
    }

    /// <summary>
    /// Decode systemd's <c>\xNN</c> escapes as UTF-8 bytes, so
    /// <c>app-caf\xc3\xa9-123.scope</c> becomes <c>café</c>. Consecutive escapes
    /// are one byte run; valid text between runs is copied as it is. Null when a
    /// run is not valid UTF-8, so the caller can leave the unit unresolved
    /// instead of inventing a name.
    /// </summary>
    public static string? UnescapeUnit(string text)
    {
        if (text.IndexOf("\\x", StringComparison.Ordinal) < 0)
        {
            return text;
        }

        var result = new StringBuilder(text.Length);
        var bytes = new List<byte>();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\\'
                && i + 3 < text.Length
                && text[i + 1] == 'x'
                && IsHex(text[i + 2])
                && IsHex(text[i + 3]))
            {
                bytes.Add(byte.Parse(
                    text.AsSpan(i + 2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture));
                i += 4;
                continue;
            }

            if (bytes.Count > 0 && !AppendUtf8(result, bytes))
            {
                return null;
            }

            result.Append(text[i]);
            i++;
        }

        return bytes.Count > 0 && !AppendUtf8(result, bytes)
            ? null
            : result.ToString();
    }

    /// <summary>Decode one run of escaped bytes, or report invalid UTF-8.</summary>
    private static bool AppendUtf8(StringBuilder result, List<byte> bytes)
    {
        byte[] data = bytes.ToArray();
        if (!System.Text.Unicode.Utf8.IsValid(data))
        {
            return false;
        }

        result.Append(System.Text.Encoding.UTF8.GetString(data));
        bytes.Clear();
        return true;
    }

    private static bool IsHex(char c) =>
        c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    /// <summary>
    /// One systemd location may write <c>app-&lt;launcher&gt;-&lt;id&gt;-&lt;n&gt;.scope</c>.
    /// The launcher is stripped only when the remainder really resolves to a
    /// desktop file; otherwise it is part of the id.
    /// </summary>
    private (string Id, ResolvedDesktop Desktop)? StripLauncher(
        string id, Dictionary<string, ResolvedDesktop?> cache)
    {
        foreach (string launcher in Launchers)
        {
            if (!id.StartsWith(launcher + "-", StringComparison.Ordinal))
            {
                continue;
            }

            string candidate = id[(launcher.Length + 1)..];
            if (candidate.Length == 0)
            {
                continue;
            }

            if (Resolve(candidate, cache) is { } desktop)
            {
                return (candidate, desktop);
            }
        }
        return null;
    }

    private ResolvedDesktop? Resolve(string id, Dictionary<string, ResolvedDesktop?> cache)
    {
        if (cache.TryGetValue(id, out ResolvedDesktop? cached))
        {
            return cached;
        }

        ResolvedDesktop? result = FindDesktop(id);
        cache[id] = result;
        return result;
    }

    private ResolvedDesktop? FindDesktop(string id)
    {
        if (id.Length == 0
            || id.Contains('/')
            || id.Contains('\\')
            || id.Contains('\0'))
        {
            return null;
        }

        foreach (string directory in _searchDirectories)
        {
            string exact = Path.Combine(directory, id + ".desktop");
            if (File.Exists(exact))
            {
                return ParseDesktop(exact, id);
            }
        }

        string suffix = "." + id + ".desktop";
        var matches = new List<string>();
        foreach (string directory in _searchDirectories)
        {
            foreach (string file in SysFs.Files(directory))
            {
                if (Path.GetFileName(file).EndsWith(suffix, StringComparison.Ordinal)
                    && !matches.Contains(file, StringComparer.Ordinal))
                {
                    matches.Add(file);
                }
            }
        }

        return matches.Count == 1 ? ParseDesktop(matches[0], id) : null;
    }

    private ResolvedDesktop? ParseDesktop(string path, string id)
    {
        string? text = SysFs.ReadText(path);
        if (text is null)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool inEntry = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                inEntry = string.Equals(line, "[Desktop Entry]", StringComparison.Ordinal);
                continue;
            }

            if (!inEntry)
            {
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            string key = line[..equals].Trim();
            // The first occurrence wins; the spec says duplicate keys are invalid.
            values.TryAdd(key, DesktopEntry.UnescapeValue(line[(equals + 1)..].Trim()));
        }

        string name = _language == "th"
            ? values.GetValueOrDefault("Name[th]") ?? string.Empty
            : string.Empty;
        if (name.Length == 0)
        {
            name = values.GetValueOrDefault("Name") ?? string.Empty;
        }
        if (name.Length == 0)
        {
            name = id;
        }

        string icon = values.GetValueOrDefault("Icon") ?? string.Empty;
        string exec = values.GetValueOrDefault("Exec") ?? string.Empty;

        return new ResolvedDesktop(
            path,
            name,
            icon.Length > 0 ? icon : null,
            exec.Length > 0 ? exec : null);
    }

    /// <summary>
    /// The key an app's instances are grouped by. A resolved desktop entry is
    /// identified by its path, as the handoff says. Two files that carry the
    /// same name and command (Chrome ships both <c>com.google.Chrome.desktop</c>
    /// and <c>google-chrome.desktop</c>) are the same app to the person using it,
    /// so they share a key too; an entry with no Exec keeps its own path.
    /// Unresolved ids group by id.
    /// </summary>
    private static string GroupKey(UnitScan unit)
    {
        if (unit.Desktop is not { } desktop)
        {
            return "id:" + unit.Id;
        }

        return desktop.Exec is { Length: > 0 } exec
            ? "app:" + desktop.Name + "\n" + exec
            : "desktop:" + desktop.Path;
    }

    private static AppInstance Instance(string unit, IReadOnlyList<ProcessRow> rows) =>
        new()
        {
            Unit = unit,
            Processes = rows,
            CpuPercent = SumCpu(rows),
            MemoryBytes = rows.Sum(row => row.MemoryBytes),
            GpuPercent = SumGpu(rows),
            GpuMemoryBytes = SumGpuMemory(rows),
        };

    /// <summary>Null only when every member is unknown, otherwise the sum of the known ones.</summary>
    private static double? SumCpu(IReadOnlyList<ProcessRow> rows)
    {
        double sum = 0;
        bool any = false;
        foreach (ProcessRow row in rows)
        {
            if (row.CpuPercent is double value)
            {
                sum += value;
                any = true;
            }
        }
        return any ? Math.Round(sum, 1, MidpointRounding.AwayFromZero) : null;
    }

    private static double? SumGpu(IReadOnlyList<ProcessRow> rows)
    {
        double sum = 0;
        bool any = false;
        foreach (ProcessRow row in rows)
        {
            if (row.GpuPercent is double value)
            {
                sum += value;
                any = true;
            }
        }
        return any ? Math.Round(sum, 1, MidpointRounding.AwayFromZero) : null;
    }

    private static long? SumGpuMemory(IReadOnlyList<ProcessRow> rows)
    {
        long sum = 0;
        bool any = false;
        foreach (ProcessRow row in rows)
        {
            if (row.GpuMemoryBytes is long value)
            {
                sum += value;
                any = true;
            }
        }
        return any ? sum : null;
    }

    private static IEnumerable<int> PidsOf(string unitDirectory)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(unitDirectory);

        while (stack.Count > 0)
        {
            string directory = stack.Pop();
            if (!seen.Add(directory))
            {
                continue;
            }

            string? procs = SysFs.ReadText(Path.Combine(directory, "cgroup.procs"));
            if (procs is not null)
            {
                foreach (string raw in procs.Split('\n'))
                {
                    string line = raw.Trim();
                    if (int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
                        && pid > 0)
                    {
                        yield return pid;
                    }
                }
            }

            foreach (string child in SysFs.Directories(directory))
            {
                stack.Push(child);
            }
        }
    }

    /// <summary>
    /// The XDG and Flatpak <c>applications</c> directories in search order:
    /// <c>$XDG_DATA_HOME</c>, <c>$XDG_DATA_DIRS</c>, then the Flatpak exports.
    /// </summary>
    public static IReadOnlyList<string> DefaultSearchDirectories()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetEnvironmentVariable("HOME") ?? ".";
        }

        string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dataHome))
        {
            dataHome = Path.Combine(home, ".local", "share");
        }

        string dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dataDirs))
        {
            dataDirs = "/usr/local/share:/usr/share";
        }

        var result = new List<string>();
        Add(Path.Combine(dataHome, "applications"));

        foreach (string directory in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            Add(Path.Combine(directory, "applications"));
        }

        Add("/var/lib/flatpak/exports/share/applications");
        Add(Path.Combine(home, ".local", "share", "flatpak", "exports", "share", "applications"));
        return result;

        void Add(string directory)
        {
            if (!result.Contains(directory, StringComparer.Ordinal))
            {
                result.Add(directory);
            }
        }
    }

    /// <summary>
    /// The parsed parts of a unit name. <paramref name="Unresolved"/> is true when
    /// an escape anywhere in the name was not valid UTF-8: the id is the raw
    /// text, no desktop file is searched, and the instance has no Exec.
    /// </summary>
    public readonly record struct UnitName(
        string Id, string Instance, bool Service, bool Unresolved = false);

    private sealed record ResolvedDesktop(
        string Path, string Name, string? Icon, string? Exec);

    private sealed record UnitScan(
        string Unit,
        string Id,
        string Instance,
        ResolvedDesktop? Desktop,
        IReadOnlyList<ProcessRow> Rows);
}
