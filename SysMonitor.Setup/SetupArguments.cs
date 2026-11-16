namespace SysMonitor.Setup;

/// <summary>
/// The command line of the setup. Parsed in one place so the elevated copy
/// that a UAC prompt starts receives exactly what the first copy was given,
/// and so it can be tested without starting anything.
///
///   /S                    silent: no window, exit code 1 on failure
///   /admin, --admin       machine-wide install (Program Files, UAC once)
///   --desktop / --no-desktop
///   --start-menu / --no-start-menu
///   --autostart / --no-autostart
///   --launch              start the program when the install is done
///   --run                 start the wizard and run it without waiting for a click
///   --settings            uninstall: remove settings and logs too
///   --finish &lt;pid&gt;       (internal) the temporary uninstaller finishing the job
///   --elevated            (internal) started by the setup's own UAC relaunch;
///                         a copy that still is not elevated must not ask again
/// </summary>
public sealed record SetupArguments(
    bool Silent,
    bool Admin,
    bool? Desktop,
    bool? StartMenu,
    bool? Autostart,
    bool Launch,
    bool Run,
    bool RemoveSettings,
    int? FinishPid,
    bool Elevated = false)
{
    public static SetupArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        bool Has(params string[] names) =>
            args.Any(a => names.Any(n => string.Equals(a, n, StringComparison.OrdinalIgnoreCase)));

        // A pair of switches: the later one wins, neither means "not given".
        bool? Pair(string on, string off)
        {
            bool? value = null;
            foreach (string a in args)
            {
                if (string.Equals(a, on, StringComparison.OrdinalIgnoreCase))
                {
                    value = true;
                }
                else if (string.Equals(a, off, StringComparison.OrdinalIgnoreCase))
                {
                    value = false;
                }
            }
            return value;
        }

        int? finish = null;
        for (int i = 0; i + 1 < args.Count; i++)
        {
            if (string.Equals(args[i], "--finish", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out int pid))
            {
                finish = pid;
            }
        }

        return new SetupArguments(
            Silent: Has("/S"),
            Admin: Has("/admin", "--admin"),
            Desktop: Pair("--desktop", "--no-desktop"),
            StartMenu: Pair("--start-menu", "--no-start-menu"),
            Autostart: Pair("--autostart", "--no-autostart"),
            Launch: Has("--launch"),
            Run: Has("--run"),
            RemoveSettings: Has("--settings"),
            FinishPid: finish,
            Elevated: Has("--elevated"));
    }

    /// <summary>The arguments to hand to the elevated copy.</summary>
    public IReadOnlyList<string> ToArguments()
    {
        var list = new List<string>();
        if (Silent)
        {
            list.Add("/S");
        }
        if (Admin)
        {
            list.Add("--admin");
        }
        AddPair(list, Desktop, "--desktop", "--no-desktop");
        AddPair(list, StartMenu, "--start-menu", "--no-start-menu");
        AddPair(list, Autostart, "--autostart", "--no-autostart");
        if (Launch)
        {
            list.Add("--launch");
        }
        if (Run)
        {
            list.Add("--run");
        }
        if (RemoveSettings)
        {
            list.Add("--settings");
        }
        if (Elevated)
        {
            list.Add("--elevated");
        }
        if (FinishPid is int pid)
        {
            list.Add("--finish");
            list.Add(pid.ToString());
        }
        return list;
    }

    private static void AddPair(List<string> list, bool? value, string on, string off)
    {
        if (value is bool chosen)
        {
            list.Add(chosen ? on : off);
        }
    }
}
