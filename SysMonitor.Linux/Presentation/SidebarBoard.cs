using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>
/// One module in the Main window's sidebar: the title, the value on the right,
/// the full device name in small type and the two sparkline series behind it.
/// </summary>
public sealed class SidebarItem
{
    /// <summary>The page this item opens, from <see cref="PageKeys"/>.</summary>
    public required string Key { get; init; }

    /// <summary>"CPU", "GPU · MX250", "Disk · nvme0n1".</summary>
    public required string Title { get; init; }

    /// <summary>"14%", "4.0/7.4 GB", "R 3 KB/s · W 5 KB/s".</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>The full device name, in small type under the title.</summary>
    public string Device { get; init; } = string.Empty;

    public Colour Accent { get; init; } = Accents.Cpu;

    /// <summary>The sparkline: usage, or the module's primary rate.</summary>
    public required History Primary { get; init; }

    /// <summary>An optional second line: the swap under RAM, upload under download.</summary>
    public History? Secondary { get; init; }

    /// <summary>
    /// Whether the item draws a sparkline. False for a grouped drive page: it
    /// has no series of its own, so the item is text-only (Amendment 1).
    /// </summary>
    public bool ShowSparkline { get; init; } = true;
}

/// <summary>
/// Builds the sidebar's module items from the module pages (R-030 §2/§5).
///
/// The pages already know their device name, their current value and their
/// page key, so the sidebar is a projection of them rather than a second copy of
/// the device rules: one item per page, in the pages' order — CPU, Memory, GPUs,
/// physical disks, the grouped virtual and network drive pages, networks,
/// batteries.
/// </summary>
public static class SidebarBoard
{
    public static IReadOnlyList<SidebarItem> Build(
        FullModel modules,
        Snapshot? snapshot,
        HistoryBoard history,
        Lang lang)
    {
        // Without a snapshot there are no pages, and so no items either.
        if (snapshot is null)
        {
            return Array.Empty<SidebarItem>();
        }

        var items = new List<SidebarItem>(modules.Tabs.Count);
        foreach (FullTab tab in modules.Tabs)
        {
            SidebarItem? item = Item(tab, history, lang);
            if (item is not null)
            {
                items.Add(item);
            }
        }
        return items;
    }

    private static SidebarItem? Item(FullTab tab, HistoryBoard history, Lang lang)
    {
        if (string.Equals(tab.Key, PageKeys.Cpu, StringComparison.Ordinal))
        {
            return new SidebarItem
            {
                Key = tab.Key,
                Title = lang["cpu"],
                Value = tab.Summary,
                Device = tab.Hardware,
                Accent = Accents.Cpu,
                Primary = history.Cpu,
            };
        }

        if (string.Equals(tab.Key, PageKeys.Ram, StringComparison.Ordinal))
        {
            return new SidebarItem
            {
                Key = tab.Key,
                Title = lang["module_memory"],
                Value = tab.Summary,
                Device = tab.Hardware,
                Accent = Accents.Ram,
                Primary = history.Ram,
                Secondary = history.Swap,
            };
        }

        if (PageKeys.IsGpuKey(tab.Key))
        {
            return new SidebarItem
            {
                Key = tab.Key,
                Title = $"{lang["gpu"]} · {ShortName(tab.Hardware, tab.Label)}",
                Value = tab.Summary,
                Device = tab.Hardware,
                Accent = Accents.Gpu,
                Primary = history.GpuUsage(tab.Key),
            };
        }

        if (tab.Key.StartsWith("disk:", StringComparison.Ordinal))
        {
            string device = tab.Key["disk:".Length..];
            return new SidebarItem
            {
                Key = tab.Key,
                Title = $"{lang["module_disk"]} · {device}",
                Value = tab.Summary,
                Device = tab.Hardware,
                Accent = Accents.Disk,
                Primary = history.DiskActivity(tab.Key),
            };
        }

        if (tab.Key.StartsWith("net", StringComparison.Ordinal))
        {
            string id = tab.Key[3..];
            return new SidebarItem
            {
                Key = tab.Key,
                Title = $"{tab.Label} · {id}",
                Value = tab.Summary,
                Device = tab.Hardware,
                Accent = Accents.Net,
                Primary = history.AdapterDown(id),
                Secondary = history.AdapterUp(id),
            };
        }

        if (tab.Key.StartsWith("battery:", StringComparison.Ordinal))
        {
            string id = tab.Key["battery:".Length..];
            return new SidebarItem
            {
                Key = tab.Key,
                Title = $"{lang["battery"]} · {id}",
                Value = tab.Summary,
                Device = tab.Hardware,
                Accent = Accents.Battery,
                Primary = history.BatteryCharge(id),
            };
        }

        if (string.Equals(tab.Key, PageKeys.DiskVirtual, StringComparison.Ordinal)
            || string.Equals(tab.Key, PageKeys.DiskNetwork, StringComparison.Ordinal))
        {
            // A grouped drive page has no device of its own. It closes the disk
            // section with the rail title the old Full tab used, and keeps a
            // sparkline only when a series for the page key was pushed.
            History? series = history.DiskActivityIfAny(tab.Key);
            return new SidebarItem
            {
                Key = tab.Key,
                Title = $"{lang["module_disk"]} · {tab.Title}",
                Value = tab.Summary,
                Device = tab.Detail,
                Accent = Accents.Disk,
                Primary = series ?? NoSeries,
                ShowSparkline = series is not null,
            };
        }

        return null;
    }

    /// <summary>A stand-in series for an item that draws no sparkline.</summary>
    private static readonly History NoSeries = new(HistoryBoard.Capacity);

    /// <summary>
    /// "Intel Iris Plus Graphics G1 (Ice Lake)" → "Iris Plus G1"; "NVIDIA
    /// GeForce MX250" → "MX250". The vendor and marketing words are dropped so
    /// the sidebar line stays short; a name with nothing to shorten is returned
    /// as it is, and an empty one falls back to the page label.
    /// </summary>
    internal static string ShortName(string name, string label)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return label;
        }

        int parenthesis = name.IndexOf('(');
        string text = (parenthesis >= 0 ? name[..parenthesis] : name).Trim();

        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = words
            .Where(word => !Noise.Contains(word.Trim(',', '.', '(', ')').ToLowerInvariant()))
            .ToList();
        if (kept.Count == 0)
        {
            kept.AddRange(words);
        }

        string shortName = string.Join(" ", kept);
        if (shortName.Length > 24)
        {
            shortName = shortName[..24].TrimEnd();
        }
        return shortName.Length > 0 ? shortName : label;
    }

    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "intel",
        "nvidia",
        "amd",
        "ati",
        "geforce",
        "radeon",
        "graphics",
        "corporation",
        "corp",
        "inc",
        "ltd",
    };
}
