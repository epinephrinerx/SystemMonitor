using System.Globalization;
using SysMonitor.Model;

namespace SysMonitor.Linux.Presentation;

/// <summary>The columns the Apps and Processes grids can sort by.</summary>
public enum GridColumn
{
    Name,
    Pid,
    User,
    Cpu,
    Memory,
    Gpu,
    GpuMemory,
    Instances,
}

/// <summary>
/// Sorting, filtering and the config spelling of the grid columns. Pure: no
/// Avalonia and no live data, so the rules can be checked without a window.
///
/// A null CPU or GPU value is always last, whichever direction is chosen: it
/// means "unknown", and putting it at the top when sorting descending would
/// claim it was the highest reading.
/// </summary>
public static class ProcessGrid
{
    public static IReadOnlyList<ProcessRow> Sort(
        IEnumerable<ProcessRow> rows, GridColumn column, bool descending)
    {
        List<ProcessRow> list = rows.ToList();
        list.Sort((a, b) =>
        {
            int order = column switch
            {
                GridColumn.Pid => a.Pid.CompareTo(b.Pid),
                GridColumn.User => string.Compare(a.User, b.User, StringComparison.OrdinalIgnoreCase),
                GridColumn.Cpu => CompareNullable(a.CpuPercent, b.CpuPercent, descending),
                GridColumn.Memory => a.MemoryBytes.CompareTo(b.MemoryBytes),
                GridColumn.Gpu => CompareNullable(a.GpuPercent, b.GpuPercent, descending),
                GridColumn.GpuMemory => CompareNullable(a.GpuMemoryBytes, b.GpuMemoryBytes, descending),
                GridColumn.Instances => 0,
                _ => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            };

            if (descending && !IsOptional(column))
            {
                order = -order;
            }

            if (order != 0)
            {
                return order;
            }

            int name = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return name != 0 ? name : a.Pid.CompareTo(b.Pid);
        });
        return list;
    }

    public static IReadOnlyList<AppEntry> Sort(
        IEnumerable<AppEntry> apps, GridColumn column, bool descending)
    {
        List<AppEntry> list = apps.ToList();
        list.Sort((a, b) =>
        {
            int order = column switch
            {
                GridColumn.Pid => 0,
                GridColumn.User => 0,
                GridColumn.Cpu => CompareNullable(a.CpuPercent, b.CpuPercent, descending),
                GridColumn.Memory => a.MemoryBytes.CompareTo(b.MemoryBytes),
                GridColumn.Gpu => CompareNullable(a.GpuPercent, b.GpuPercent, descending),
                GridColumn.GpuMemory => CompareNullable(a.GpuMemoryBytes, b.GpuMemoryBytes, descending),
                GridColumn.Instances => a.Instances.Count.CompareTo(b.Instances.Count),
                _ => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            };

            if (descending && !IsOptional(column))
            {
                order = -order;
            }

            if (order != 0)
            {
                return order;
            }

            int name = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return name != 0 ? name : string.Compare(a.Id, b.Id, StringComparison.Ordinal);
        });
        return list;
    }

    /// <summary>Case-insensitive substring of name, command, user or PID text.</summary>
    public static IReadOnlyList<ProcessRow> Filter(
        IEnumerable<ProcessRow> rows, string? query)
    {
        string text = query?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return rows.ToList();
        }

        return rows.Where(row =>
                Contains(row.Name, text)
                || Contains(row.Command, text)
                || Contains(row.User, text)
                || Contains(row.Pid.ToString(CultureInfo.InvariantCulture), text))
            .ToList();
    }

    /// <summary>The Apps grid filters by app name, id and its instance units.</summary>
    public static IReadOnlyList<AppEntry> Filter(
        IEnumerable<AppEntry> apps, string? query)
    {
        string text = query?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return apps.ToList();
        }

        return apps.Where(app =>
                Contains(app.Name, text)
                || Contains(app.Id, text)
                || app.Instances.Any(instance => Contains(instance.Unit, text)))
            .ToList();
    }

    /// <summary>The config spelling of a column, for the four sort keys.</summary>
    public static string Key(GridColumn column) => column switch
    {
        GridColumn.Pid => "pid",
        GridColumn.User => "user",
        GridColumn.Cpu => "cpu",
        GridColumn.Memory => "memory",
        GridColumn.Gpu => "gpu",
        GridColumn.GpuMemory => "gpu_memory",
        GridColumn.Instances => "instances",
        _ => "name",
    };

    /// <summary>An unknown config value falls back to <paramref name="fallback"/>.</summary>
    public static GridColumn ParseColumn(string? value, GridColumn fallback = GridColumn.Memory) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "name" => GridColumn.Name,
            "pid" => GridColumn.Pid,
            "user" => GridColumn.User,
            "cpu" => GridColumn.Cpu,
            "memory" => GridColumn.Memory,
            "gpu" => GridColumn.Gpu,
            "gpu_memory" => GridColumn.GpuMemory,
            "instances" => GridColumn.Instances,
            _ => fallback,
        };

    private static int CompareNullable(double? a, double? b, bool descending)
    {
        if (a is null && b is null)
        {
            return 0;
        }
        if (a is null)
        {
            // Unknown is last in both directions.
            return 1;
        }
        if (b is null)
        {
            return -1;
        }
        return descending ? b.Value.CompareTo(a.Value) : a.Value.CompareTo(b.Value);
    }

    private static int CompareNullable(long? a, long? b, bool descending)
    {
        if (a is null && b is null)
        {
            return 0;
        }
        if (a is null)
        {
            return 1;
        }
        if (b is null)
        {
            return -1;
        }
        return descending ? b.Value.CompareTo(a.Value) : a.Value.CompareTo(b.Value);
    }

    private static bool IsOptional(GridColumn column) =>
        column is GridColumn.Cpu or GridColumn.Gpu or GridColumn.GpuMemory;

    private static bool Contains(string? value, string query) =>
        value is { Length: > 0 }
        && value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
