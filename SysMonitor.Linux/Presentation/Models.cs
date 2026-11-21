namespace SysMonitor.Linux.Presentation;

/// <summary>
/// One meter row, as the view needs it: a title, a percentage, the colour the
/// bar should be, and the optional pieces the spec hides in compact mode.
/// </summary>
public sealed class MeterRow
{
    public required string Title { get; init; }
    public int Percent { get; init; }
    public Colour BarColour { get; init; }

    /// <summary>Widget rows show only the label and the bar.</summary>
    public bool Compact { get; init; }

    public bool ShowValue { get; init; } = true;

    /// <summary>
    /// Replaces the percentage readout when set, e.g. "n/a" for a total with
    /// nothing to divide by. Null keeps "NN%".
    /// </summary>
    public string? ValueText { get; init; }

    /// <summary>Null hides the badge; a badge whose text is "n/a" is still a badge.</summary>
    public TempBadge? Badge { get; init; }

    public string Detail { get; init; } = string.Empty;

    /// <summary>Set on drive rows: the mount point, used for the drive menu and double-click.</summary>
    public string? DriveKey { get; init; }

    public bool IsDrive => DriveKey is not null;

    /// <summary>Set on drive rows in Overall when the drive is running low.</summary>
    public DiskAlert? Alert { get; init; }
}

public enum WidgetPageKind
{
    CpuTotal,
    CpuGroup,
    Ram,
    DiskTotal,
    DiskOne,
    NetworkTotal,
    NetworkOne,
    Battery,
    Empty,
}

/// <summary>One page of the rotating widget.</summary>
public sealed class WidgetPage
{
    public required string Key { get; init; }
    public required WidgetPageKind Kind { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public TempBadge? Badge { get; init; }
    public int Columns { get; init; } = 1;
    public IReadOnlyList<MeterRow> Rows { get; init; } = Array.Empty<MeterRow>();

    /// <summary>Shown instead of the content: "connecting" or "no_data".</summary>
    public string Message { get; init; } = string.Empty;

    public DiskAlert? Alert { get; init; }
    public string? DriveKey { get; init; }
}

/// <summary>One card in the Overall content column.</summary>
public sealed class SectionModel
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string Note { get; init; } = string.Empty;
    public int Columns { get; init; } = 1;
    public IReadOnlyList<MeterRow> Rows { get; init; } = Array.Empty<MeterRow>();
}

/// <summary>The Overall view: its cards and the legend line under them.</summary>
public sealed class OverallModel
{
    public IReadOnlyList<SectionModel> Sections { get; init; } = Array.Empty<SectionModel>();
    public string Legend { get; init; } = string.Empty;

    /// <summary>Shown instead of the cards when there is nothing to draw:
    /// "connecting" before the first snapshot, "no_data" when every module is off.</summary>
    public string Message { get; init; } = string.Empty;
}
