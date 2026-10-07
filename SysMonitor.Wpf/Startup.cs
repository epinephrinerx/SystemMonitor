using Microsoft.Win32;

namespace SysMonitor;

/// <summary>
/// Launch-at-logon, through the per-user Run key.
///
/// HKCU, never HKLM: the whole install is per-user and needs no elevation.
/// The value is rewritten rather than trusted, so moving the executable and
/// ticking the box again fixes a stale path.
/// </summary>
public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    /// <summary>
    /// The same value the installer writes (Installer.Key). Not "SysMonitor":
    /// the Python build claims that one, and both install per-user.
    /// </summary>
    public const string ValueName = "SystemMonitor";

    /// <summary>
    /// What the pre-4.0 C# build wrote. A second Run entry makes Windows start
    /// the app twice at logon, so it is cleaned up whenever the box is set.
    /// </summary>
    private const string LegacyValueName = "SysMonitor.NET";

    /// <summary>The command the Run key should hold for this installation.</summary>
    public static string Command(string exePath) => "\"" + exePath + "\"";

    public static bool IsEnabled(string exePath)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) as string == Command(exePath);
        }
        catch (Exception error)
        {
            Diag.ReportException("Startup read", error);
            return false;
        }
    }

    public static void Set(bool enabled, string exePath)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            if (enabled)
            {
                key.SetValue(ValueName, Command(exePath));
            }
            else
            {
                // Missing is the desired state, not an error worth reporting.
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception error)
        {
            Diag.ReportException("Startup write", error);
        }
    }
}
