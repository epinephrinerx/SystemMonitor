using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SysMonitor;

/// <summary>
/// Whether this copy was installed machine-wide (R-022): in Program Files,
/// by the setup's administrator option, which writes a marker next to the
/// program. A machine-wide install is meant to run elevated every time.
///
/// The marker is trusted only where a standard user cannot have put it: the
/// program must be running from Program Files\SystemMonitor itself. A marker
/// beside a copy anywhere else, such as a per-user install, means nothing.
/// </summary>
public static class InstallMode
{
    public const string MarkerName = "install-mode.json";
    public const string FolderName = "SystemMonitor";

    /// <summary>Starts this copy as it is, without the elevated relaunch. For checking a build.</summary>
    public const string NoElevateSwitch = "--no-elevate";

    /// <summary>Added to the elevated relaunch so a copy that comes back unelevated does not ask again, for ever.</summary>
    public const string ElevationAttemptedSwitch = "--elevation-attempted";

    private static readonly Lazy<bool> Real = new(() =>
        IsMachineWide(AppContext.BaseDirectory,
                      Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));

    /// <summary>For this running copy.</summary>
    public static bool IsMachineWideInstall => Real.Value;

    /// <summary>Is <paramref name="baseDirectory"/> exactly Program Files\SystemMonitor?</summary>
    public static bool IsProgramFilesInstall(string baseDirectory, string programFiles)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory) || string.IsNullOrWhiteSpace(programFiles)
            || !Path.IsPathFullyQualified(baseDirectory) || !Path.IsPathFullyQualified(programFiles))
        {
            return false;
        }
        string expected = Path.Combine(Path.GetFullPath(programFiles), FolderName);
        string actual = Path.GetFullPath(baseDirectory);
        return string.Equals(
            expected.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            actual.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMachineWide(string baseDirectory, string programFiles)
    {
        if (!IsProgramFilesInstall(baseDirectory, programFiles))
        {
            return false;
        }
        try
        {
            var marker = new FileInfo(Path.Combine(baseDirectory, MarkerName));
            // A link could point at a file elsewhere; a real marker is a few bytes.
            if (!marker.Exists || marker.Attributes.HasFlag(FileAttributes.ReparsePoint) || marker.Length > 1024)
            {
                return false;
            }
            return MarkerSaysAdmin(File.ReadAllText(marker.FullName));
        }
        catch (Exception)
        {
            // Unreadable is "not machine-wide": the program still runs, just not elevated.
            return false;
        }
    }

    internal static bool MarkerSaysAdmin(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("scope", out JsonElement scope)
                && scope.ValueKind == JsonValueKind.String
                && scope.GetString() == "admin";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Machine-wide, not elevated yet, and not told to stay as it is.</summary>
    public static bool ShouldRelaunchElevated(bool machineWide, bool isElevated, IEnumerable<string> args) =>
        machineWide && !isElevated
        && !args.Contains(NoElevateSwitch, StringComparer.OrdinalIgnoreCase)
        && !args.Contains(ElevationAttemptedSwitch, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The one elevated relaunch of a machine-wide install, taken at the very start
/// of the process.
/// </summary>
internal static class ElevatedLaunch
{
    private static readonly TimeSpan HandOverWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// True when this process should simply end: an elevated copy was started
    /// in its place, or one is already running. False means carry on as this
    /// copy, which includes the user declining the UAC prompt: the program then
    /// runs without administrator rights (no SMART) and the sidebar's restart
    /// button still offers the prompt again.
    ///
    /// This runs before the single-instance lock is taken. Holding the lock
    /// here would make the elevated copy find the app "already running" and
    /// quit.
    /// </summary>
    public static bool TryRelaunch(string[] args)
    {
        if (!InstallMode.ShouldRelaunchElevated(
                InstallMode.IsMachineWideInstall, Native.Win32.IsElevated(), args))
        {
            return false;
        }

        // A copy is already up (and holds a lock this token may not even be able
        // to open): do not ask for UAC just to have the new copy quit. But a
        // restart starts this copy while the old one is still closing, so give
        // that a moment first; otherwise the restart would end with no copy.
        if (!args.Contains("--allow-multiple")
            && !SingleInstance.WaitUntilGone(SingleInstance.DefaultName, HandOverWait))
        {
            return true;
        }

        if (Environment.ProcessPath is not string exe)
        {
            return false;
        }
        var start = AdminRestart.Start(exe);
        foreach (string argument in args)
        {
            start.ArgumentList.Add(argument);
        }
        start.ArgumentList.Add(InstallMode.ElevationAttemptedSwitch);
        try
        {
            using Process? process = Process.Start(start);
            return process is not null;
        }
        catch (Win32Exception error) when (AdminRestart.IsDeclined(error))
        {
            Diag.Write("elevation declined; running without administrator rights");
            return false;
        }
        catch (Exception error)
        {
            Diag.ReportException("Elevated relaunch", error);
            return false;
        }
    }
}
