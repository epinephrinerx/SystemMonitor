using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Xml.Linq;

namespace SysMonitor;

/// <summary>
/// Launch-at-logon for a machine-wide install (R-022). That install starts
/// from a Scheduled Task made by the setup; the per-user Run key of
/// <see cref="Startup"/> would start the program un-elevated, ask for UAC at
/// every logon and run it twice. So the sidebar's checkbox turns the task on
/// and off instead, which needs administrator rights, and is disabled without.
/// </summary>
internal static class MachineWideStartup
{
    /// <summary>The name the setup registers the task under (Installer.Key).</summary>
    public const string TaskName = "SystemMonitor";

    public static IReadOnlyList<string> QueryArguments() =>
        new[] { "/Query", "/TN", TaskName, "/XML" };

    public static IReadOnlyList<string> ChangeArguments(bool enabled) =>
        new[] { "/Change", "/TN", TaskName, enabled ? "/ENABLE" : "/DISABLE" };

    /// <summary>
    /// Settings/Enabled of a task definition. A definition that does not say is
    /// enabled (the schema default); one that cannot be read at all is null.
    /// </summary>
    public static bool? ParseEnabled(string xml)
    {
        try
        {
            XElement root = XDocument.Parse(xml).Root
                ?? throw new FormatException("no root element");
            XElement? enabled = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Settings")
                ?.Elements().FirstOrDefault(e => e.Name.LocalName == "Enabled");
            return enabled is null || !bool.TryParse(enabled.Value, out bool value) || value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>schtasks.exe by its full path, never one found on PATH.</summary>
    private static string SchtasksPath => Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    private static (int Exit, string Output) Run(IReadOnlyList<string> arguments)
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
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            process.Kill();
            throw new TimeoutException("schtasks.exe did not finish.");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult());
    }

    /// <summary>Whether the task is enabled; null when it cannot be read.</summary>
    public static bool? IsEnabled()
    {
        try
        {
            (int exit, string output) = Run(QueryArguments());
            return exit == 0 ? ParseEnabled(output) : null;
        }
        catch (Exception error)
        {
            Diag.ReportException("Startup task read", error);
            return null;
        }
    }

    /// <summary>Turns the task on or off. False when Windows refused (usually: no administrator rights).</summary>
    public static bool Set(bool enabled)
    {
        try
        {
            return Run(ChangeArguments(enabled)).Exit == 0;
        }
        catch (Exception error)
        {
            Diag.ReportException("Startup task write", error);
            return false;
        }
    }
}

/// <summary>
/// A directory only administrators can enter or change, for an elevated
/// process that has to write a file and then run it. Staging that in %TEMP%
/// lets any process of the same user swap the file in between.
/// </summary>
internal static class AdminStaging
{
    /// <summary>Administrators and SYSTEM: full control. Nobody else, and nothing inherited.</summary>
    public static DirectorySecurity AdminOnlySecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (WellKnownSidType sid in new[]
                 { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
        return security;
    }

    /// <summary>A new, uniquely named directory with the admin-only ACL applied as it is created.</summary>
    public static string Create(string parent, string prefix)
    {
        string path = Path.Combine(parent, prefix + Guid.NewGuid().ToString("N"));
        new DirectoryInfo(path).Create(AdminOnlySecurity());
        return path;
    }

    /// <summary>Owned by Administrators or SYSTEM, which is what a directory made by an elevated copy is.</summary>
    internal static bool OwnedByAdministrators(DirectoryInfo directory)
    {
        try
        {
            IdentityReference? owner = directory.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
            return owner is not null
                && (owner.Equals(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null))
                    || owner.Equals(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Removes earlier staging directories of the same prefix directly under
    /// <paramref name="parent"/>. Best effort, never follows a link, and only
    /// those <paramref name="isOurs"/> accepts (by default: owned by
    /// Administrators or SYSTEM). Standard users can create folders in
    /// ProgramData, so a matching name alone does not make a folder ours.
    /// </summary>
    public static void RemoveOld(string parent, string prefix, Func<DirectoryInfo, bool>? isOurs = null)
    {
        isOurs ??= OwnedByAdministrators;
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(parent, prefix + "*"))
            {
                var info = new DirectoryInfo(directory);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null
                    || !isOurs(info))
                {
                    continue;
                }
                try
                {
                    info.Delete(recursive: true);
                }
                catch (Exception)
                {
                    // In use (an installer still running from it): leave it.
                }
            }
        }
        catch (Exception)
        {
            // Nothing to clean, or no right to: not worth failing an update over.
        }
    }
}
