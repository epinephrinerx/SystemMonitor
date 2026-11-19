using System.Text.Json;
using SysMonitor.Linux.Presentation;
using SysMonitor.Model;

namespace SysMonitor.Linux.Sensors;

/// <summary>
/// systemd units, read through <c>systemctl</c> with an argument list. The
/// system scope is one call and the user scope is another; the per-unit state
/// (MainPID, unit-file state) comes from one batched <c>systemctl show</c> per
/// scope, never one call per unit.
///
/// Nothing here changes a unit. The reader only ever runs <c>list-units</c> and
/// <c>show</c>.
/// </summary>
public sealed class ServiceReader
{
    /// <summary>The properties asked of <c>systemctl show</c>, in one call.</summary>
    public const string ShowProperties =
        "Id,MainPID,UnitFileState,ActiveState,SubState,Description";

    private readonly ICommandRunner _commands;
    private readonly int _timeoutMs;

    public ServiceReader(ICommandRunner commands, int timeoutMs = 4000)
    {
        _commands = commands;
        _timeoutMs = timeoutMs;
    }

    /// <summary>Both scopes, user units first.</summary>
    public IReadOnlyList<ServiceEntry> Read()
    {
        var services = new List<ServiceEntry>();
        services.AddRange(ReadScope(user: true));
        services.AddRange(ReadScope(user: false));
        return services;
    }

    /// <summary>One scope: the unit list plus the batched detailed state.</summary>
    public IReadOnlyList<ServiceEntry> ReadScope(bool user)
    {
        string? json = _commands.Run("systemctl", ListArguments(user), _timeoutMs);
        if (json is null)
        {
            return Array.Empty<ServiceEntry>();
        }

        var listed = ParseList(json, user);
        if (listed.Count == 0)
        {
            return listed;
        }

        Dictionary<string, ShowState> states = ReadStates(listed.Select(s => s.Name), user);
        var result = new List<ServiceEntry>(listed.Count);
        foreach (ServiceEntry unit in listed)
        {
            states.TryGetValue(unit.Name, out ShowState? state);
            result.Add(new ServiceEntry
            {
                Name = unit.Name,
                Display = state?.Description is { Length: > 0 } description
                    ? description
                    : unit.Display,
                Running = state?.ActiveState is { Length: > 0 } active
                    ? string.Equals(active, "active", StringComparison.Ordinal)
                    : unit.Running,
                State = state?.ActiveState is { Length: > 0 } shown ? shown : unit.State,
                Pid = state?.MainPid ?? 0,
                Start = StartOf(state?.UnitFileState),
                UserUnit = user,
            });
        }
        return result;
    }

    /// <summary>The exact argument list for the unit listing.</summary>
    public static IReadOnlyList<string> ListArguments(bool user)
    {
        var args = new List<string>
        {
            "list-units",
            "--type=service",
            "--all",
            "--output=json",
            "--no-pager",
        };
        if (user)
        {
            args.Add("--user");
        }
        return args;
    }

    /// <summary>
    /// The exact argument list of the one batched show call:
    /// <c>[--user] show -p &lt;properties&gt; --no-pager -- &lt;units…&gt;</c>.
    /// <c>--</c> terminates options, so a unit name can never be read as one.
    /// A name that is empty or holds '/' or a NUL is left out; systemd never
    /// produces one, and it is not something to hand back to systemctl.
    /// </summary>
    public static IReadOnlyList<string> ShowArguments(IEnumerable<string> unitNames, bool user)
    {
        var args = new List<string>();
        if (user)
        {
            args.Add("--user");
        }
        args.Add("show");
        args.Add("-p");
        args.Add(ShowProperties);
        args.Add("--no-pager");
        args.Add("--");
        foreach (string name in unitNames)
        {
            if (ServiceControl.IsValidUnitName(name))
            {
                args.Add(ServiceControl.UnitFile(name));
            }
        }
        return args;
    }

    /// <summary>The state word systemd printed, mapped to the three menus.</summary>
    public static ServiceStart StartOf(string? unitFileState) => unitFileState switch
    {
        "enabled" or "enabled-runtime" => ServiceStart.Automatic,
        "masked" or "masked-runtime" => ServiceStart.Disabled,
        "disabled" => ServiceStart.Manual,
        // static, indirect, generated, transient, alias: startable but not a
        // thing the user enables. The menu shows Manual and the call may still
        // be refused by systemd, which is the truthful outcome.
        _ => ServiceStart.Manual,
    };

    /// <summary>
    /// Parse <c>list-units --output=json</c>. A malformed document is an empty
    /// list, not an exception: the service card then simply has nothing to show.
    /// </summary>
    public static IReadOnlyList<ServiceEntry> ParseList(string json, bool user)
    {
        var result = new List<ServiceEntry>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                string unit = Text(item, "unit");
                if (unit.Length == 0)
                {
                    continue;
                }
                string name = UnitName(unit);
                if (!ServiceControl.IsValidUnitName(name))
                {
                    continue;
                }
                string active = Text(item, "active");
                string description = Text(item, "description");
                result.Add(new ServiceEntry
                {
                    Name = name,
                    Display = description,
                    Running = string.Equals(active, "active", StringComparison.Ordinal),
                    State = active,
                    UserUnit = user,
                });
            }
        }
        catch (JsonException)
        {
            return result;
        }
        return result;
    }

    /// <summary>
    /// Parse the blank-line-separated blocks of <c>systemctl show</c>.
    /// </summary>
    public static Dictionary<string, ShowState> ParseShow(string text)
    {
        var result = new Dictionary<string, ShowState>(StringComparer.Ordinal);
        ShowState? current = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                Finish(current);
                current = null;
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }
            string key = line[..equals];
            string value = line[(equals + 1)..];
            current ??= new ShowState();

            switch (key)
            {
                case "Id":
                    current.Id = value;
                    break;
                case "MainPID":
                    current.MainPid = int.TryParse(value, out int pid) && pid > 0 ? pid : 0;
                    break;
                case "UnitFileState":
                    current.UnitFileState = value;
                    break;
                case "ActiveState":
                    current.ActiveState = value;
                    break;
                case "SubState":
                    current.SubState = value;
                    break;
                case "Description":
                    current.Description = value;
                    break;
            }
        }
        Finish(current);
        return result;

        void Finish(ShowState? state)
        {
            if (state?.Id is { Length: > 0 } id)
            {
                result[UnitName(id)] = state;
            }
        }
    }

    private Dictionary<string, ShowState> ReadStates(IEnumerable<string> names, bool user)
    {
        // Never run `show` without a unit: with no operand it reports manager
        // properties, which is not what the service list asked for.
        List<string> valid = names.Where(ServiceControl.IsValidUnitName).ToList();
        if (valid.Count == 0)
        {
            return new Dictionary<string, ShowState>(StringComparer.Ordinal);
        }

        IReadOnlyList<string> arguments = ShowArguments(valid, user);
        string? text = _commands.Run("systemctl", arguments, _timeoutMs);
        return text is null
            ? new Dictionary<string, ShowState>(StringComparer.Ordinal)
            : ParseShow(text);
    }

    /// <summary>"cups.service" becomes "cups"; the UI shows the short name.</summary>
    public static string UnitName(string unit) =>
        unit.EndsWith(".service", StringComparison.Ordinal)
            ? unit[..^".service".Length]
            : unit;

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>One unit's detailed state from <c>systemctl show</c>.</summary>
    public sealed class ShowState
    {
        public string Id { get; set; } = string.Empty;
        public int MainPid { get; set; }
        public string UnitFileState { get; set; } = string.Empty;
        public string ActiveState { get; set; } = string.Empty;
        public string SubState { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
