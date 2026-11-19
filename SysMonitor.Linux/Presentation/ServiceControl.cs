namespace SysMonitor.Linux.Presentation;

/// <summary>The outcome of one service operation.</summary>
public readonly record struct ServiceResult(bool Ok, string? Error)
{
    public static ServiceResult Success => new(true, null);
    public static ServiceResult Failed(string error) => new(false, error);
}

/// <summary>
/// The four user-unit actions and the startup change, built as argument lists
/// for <c>systemctl --user</c>. System units never reach here: the app does not
/// ask for administrator rights, so they stay read-only.
/// </summary>
public sealed class ServiceControl
{
    public const string Start = "start";
    public const string Stop = "stop";
    public const string Restart = "restart";
    public const string Enable = "enable";
    public const string Disable = "disable";

    private readonly Func<IReadOnlyList<string>, string?> _run;

    /// <param name="run">
    /// Runs systemctl with an argument list; null means success, otherwise the
    /// error text the status line shows.
    /// </param>
    public ServiceControl(Func<IReadOnlyList<string>, string?> run)
    {
        _run = run;
    }

    /// <summary>Apply one action to a user unit.</summary>
    public ServiceResult Apply(string action, string unitName)
    {
        string? error = _run(Arguments(action, unitName));
        return error is null ? ServiceResult.Success : ServiceResult.Failed(error);
    }

    /// <summary>Set enabled or disabled from the startup menu.</summary>
    public ServiceResult SetStartup(bool automatic, string unitName) =>
        Apply(automatic ? Enable : Disable, unitName);

    /// <summary>
    /// The exact argv: <c>--user</c>, the verb, <c>--</c>, then the unit file
    /// name. The terminator keeps a unit name from ever being read as an option.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string action, string unitName)
    {
        if (action is not (Start or Stop or Restart or Enable or Disable))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown service action.");
        }
        return new[] { "--user", action, "--", UnitFile(unitName) };
    }

    /// <summary>
    /// "cups" becomes "cups.service"; systemd wants the unit file name. A name
    /// that is empty or holds '/' or a NUL is refused, so nothing that looks
    /// like a path reaches systemctl.
    /// </summary>
    public static string UnitFile(string unitName)
    {
        if (!IsValidUnitName(unitName))
        {
            throw new ArgumentException(
                "A unit name must be non-empty and contain neither '/' nor a NUL.",
                nameof(unitName));
        }
        return unitName.EndsWith(".service", StringComparison.Ordinal)
            ? unitName
            : unitName + ".service";
    }

    /// <summary>systemd's unit names never contain these; reject the rest.</summary>
    public static bool IsValidUnitName(string? unitName) =>
        !string.IsNullOrEmpty(unitName)
        && unitName.IndexOf('/') < 0
        && unitName.IndexOf('\0') < 0;
}
