namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The Linux command line (spec section 0, Windows-only switches dropped).
/// Unknown switches are ignored rather than fatal: the app is a widget, and a
/// typo in a desktop entry must not stop it from opening.
/// </summary>
public sealed class StartupOptions
{
    public bool Expanded { get; init; }
    public bool Full { get; init; }
    public bool AllowMultiple { get; init; }

    /// <summary>Heartbeat interval in seconds. The spec default is ten minutes.</summary>
    public int HeartbeatSeconds { get; init; } = 600;

    /// <summary>Print the built view models and exit without opening a window.</summary>
    public bool ProbeUi { get; init; }

    public static StartupOptions Parse(IEnumerable<string>? args)
    {
        bool expanded = false, full = false, allowMultiple = false, probeUi = false;
        int heartbeat = 600;

        string[] list = args?.ToArray() ?? Array.Empty<string>();
        for (int i = 0; i < list.Length; i++)
        {
            string arg = list[i];
            if (Equals(arg, "--expanded"))
            {
                expanded = true;
            }
            else if (Equals(arg, "--full"))
            {
                full = true;
            }
            else if (Equals(arg, "--allow-multiple"))
            {
                allowMultiple = true;
            }
            else if (Equals(arg, "--probe-ui"))
            {
                probeUi = true;
            }
            else if (Equals(arg, "--heartbeat") && i + 1 < list.Length
                     && int.TryParse(list[i + 1], out int seconds) && seconds > 0)
            {
                heartbeat = seconds;
                i++;
            }
        }

        return new StartupOptions
        {
            Expanded = expanded,
            Full = full,
            AllowMultiple = allowMultiple,
            HeartbeatSeconds = heartbeat,
            ProbeUi = probeUi,
        };
    }

    /// <summary>
    /// <c>--full</c> wins over <c>--expanded</c>; otherwise the remembered view
    /// decides. An unknown stored value means Widget.
    /// </summary>
    public ViewMode StartView(AppConfig config)
    {
        if (Full)
        {
            return ViewMode.Full;
        }
        if (Expanded)
        {
            return ViewMode.Overall;
        }
        return ViewModeExtensions.Parse(config.LastView, ViewMode.Widget);
    }

    private static bool Equals(string? value, string flag) =>
        string.Equals(value, flag, StringComparison.OrdinalIgnoreCase);
}
