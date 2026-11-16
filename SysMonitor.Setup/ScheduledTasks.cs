using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace SysMonitor.Setup;

/// <summary>
/// The Scheduled Task that starts the machine-wide install at logon with the
/// highest privileges (R-022). A task is how a program starts elevated at
/// logon without a UAC prompt: the prompt was answered once, when the task was
/// created by the elevated installer.
///
/// Building the task definition and the command lines is separate from running
/// them, so what would be registered can be checked without registering it.
/// </summary>
public static class ScheduledTasks
{
    private static readonly XNamespace Ns =
        "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>
    /// The task definition. Only <paramref name="userId"/> triggers it, and
    /// it runs as that user with the highest privileges the user has, not as
    /// SYSTEM and not for anyone else who signs in.
    /// </summary>
    public static string BuildXml(string exePath, string workingDirectory, string userId)
    {
        RequireAbsolute(exePath, nameof(exePath));
        RequireAbsolute(workingDirectory, nameof(workingDirectory));
        if (string.IsNullOrWhiteSpace(userId) || userId.Any(char.IsControl))
        {
            throw new ArgumentException("A user to run the task for is required.", nameof(userId));
        }

        var task = new XElement(Ns + "Task",
            new XAttribute("version", "1.2"),
            new XElement(Ns + "RegistrationInfo",
                new XElement(Ns + "Author", Installer.Publisher),
                new XElement(Ns + "Description",
                    "Starts " + Installer.DisplayName + " at logon with administrator rights."),
                new XElement(Ns + "URI", "\\" + Installer.Key)),
            new XElement(Ns + "Triggers",
                new XElement(Ns + "LogonTrigger",
                    new XElement(Ns + "Enabled", "true"),
                    new XElement(Ns + "UserId", userId))),
            new XElement(Ns + "Principals",
                new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                    new XElement(Ns + "UserId", userId),
                    new XElement(Ns + "LogonType", "InteractiveToken"),
                    new XElement(Ns + "RunLevel", "HighestAvailable"))),
            new XElement(Ns + "Settings",
                new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                new XElement(Ns + "AllowHardTerminate", "true"),
                new XElement(Ns + "StartWhenAvailable", "false"),
                new XElement(Ns + "RunOnlyIfNetworkAvailable", "false"),
                new XElement(Ns + "AllowStartOnDemand", "true"),
                new XElement(Ns + "Enabled", "true"),
                new XElement(Ns + "Hidden", "false"),
                new XElement(Ns + "RunOnlyIfIdle", "false"),
                new XElement(Ns + "WakeToRun", "false"),
                // A monitor runs for as long as the user is signed in.
                new XElement(Ns + "ExecutionTimeLimit", "PT0S"),
                new XElement(Ns + "Priority", "7")),
            new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                new XElement(Ns + "Exec",
                    new XElement(Ns + "Command", exePath),
                    new XElement(Ns + "WorkingDirectory", workingDirectory))));

        // schtasks reads the file as UTF-16, which the declaration says.
        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" + task.ToString();
    }

    /// <summary>The definition for an admin layout, for the given user.</summary>
    public static string BuildXml(InstallLayout layout, string userId)
    {
        ArgumentNullException.ThrowIfNull(layout);
        RequireAdmin(layout);
        return BuildXml(layout.ExePath, layout.InstallDir, userId);
    }

    // Each command is an argument list, never one string: no quoting to get
    // wrong and no shell to reinterpret a path.
    public static IReadOnlyList<string> CreateArguments(string taskName, string xmlPath) =>
        new[] { "/Create", "/TN", RequireName(taskName), "/XML", xmlPath, "/F" };

    public static IReadOnlyList<string> DeleteArguments(string taskName) =>
        new[] { "/Delete", "/TN", RequireName(taskName), "/F" };

    public static IReadOnlyList<string> QueryArguments(string taskName) =>
        new[] { "/Query", "/TN", RequireName(taskName) };

    // ------------------------------------------------------------ running
    /// <summary>schtasks.exe by its full path, so a copy earlier on PATH is never run.</summary>
    private static string SchtasksPath =>
        Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    /// <summary>Runs schtasks with the given arguments and returns its exit code.</summary>
    private static int Run(IReadOnlyList<string> arguments) => RunCapture(arguments).Exit;

    private static (int Exit, string Output) RunCapture(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(SchtasksPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("schtasks.exe did not start.");
        // Drain both pipes so a full buffer cannot stall the child.
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill();
            throw new TimeoutException("schtasks.exe did not finish.");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult());
    }

    public static bool Exists(string taskName) => Run(QueryArguments(taskName)) == 0;

    /// <summary>
    /// Whether the task exists and is switched on. The app can turn the task off
    /// from its sidebar; a setup that took "exists" for "on" would turn it back
    /// on at the next update.
    /// </summary>
    public static bool IsEnabled(string taskName)
    {
        (int exit, string output) = RunCapture(new[] { "/Query", "/TN", RequireName(taskName), "/XML" });
        return exit == 0 && ParseEnabled(output) == true;
    }

    /// <summary>
    /// Settings/Enabled of a task definition. A definition that does not say is
    /// enabled (the schema default); one that cannot be read at all is null.
    /// </summary>
    public static bool? ParseEnabled(string xml)
    {
        try
        {
            XElement root = XDocument.Parse(xml).Root ?? throw new FormatException("no root element");
            XElement? enabled = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Settings")
                ?.Elements().FirstOrDefault(e => e.Name.LocalName == "Enabled");
            return enabled is null || !bool.TryParse(enabled.Value, out bool value) || value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Creates or replaces the task for an admin layout. Needs an elevated process.</summary>
    public static void Create(InstallLayout layout, string userId)
    {
        RequireAdmin(layout);
        string xml = BuildXml(layout, userId);

        // The definition is staged in the install folder, not in %TEMP%: this
        // process is elevated and schtasks reads the file by name, so a folder
        // the user can write to would let a user-level process swap in its own
        // task (any program, any principal) between the write and the read.
        // Program Files is writable by administrators only.
        string file = Path.Combine(layout.InstallDir, "task-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Encoding.Unicode))
            {
                writer.Write(xml);
            }
            int exit = Run(CreateArguments(layout.ScheduledTaskName, file));
            if (exit != 0)
            {
                throw new Win32Exception(exit, "schtasks.exe could not create the startup task.");
            }
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception)
            {
                // A leftover file in an administrators-only folder is not worth failing over.
            }
        }
    }

    /// <summary>
    /// Removes the task. A task that is not there is already the wanted state;
    /// a failed delete only counts when the task is still there afterwards,
    /// because schtasks gives the same exit code for "not found" as for most
    /// other failures.
    /// </summary>
    public static void Delete(string taskName)
    {
        int exit = Run(DeleteArguments(taskName));
        if (exit != 0 && Exists(taskName))
        {
            throw new Win32Exception(exit, "schtasks.exe could not remove the startup task.");
        }
    }

    // ------------------------------------------------------------- checks
    private static void RequireAdmin(InstallLayout layout)
    {
        if (layout.Autostart != AutostartMechanism.ScheduledTask)
        {
            throw new InvalidOperationException(
                "Only the machine-wide install starts from a Scheduled Task.");
        }
    }

    private static string RequireName(string taskName)
    {
        if (string.IsNullOrWhiteSpace(taskName) || taskName.Any(c => char.IsControl(c) || c is '\\' or '/'))
        {
            throw new ArgumentException("A plain task name is required.", nameof(taskName));
        }
        return taskName;
    }

    private static void RequireAbsolute(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Any(char.IsControl))
        {
            throw new ArgumentException("An absolute path is required.", name);
        }
    }
}
