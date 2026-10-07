using System.Diagnostics;

namespace SysMonitor;

/// <summary>
/// The three Windows tools a drive links to: File Explorer, Disk Cleanup and
/// Disk Management.
///
/// Each is built as a <see cref="ProcessStartInfo"/> first and launched second,
/// so what would be run can be checked without running it. The drive letter is
/// the only thing that varies, and it is accepted only as a single letter --
/// it comes from the sampler, but it ends up on a command line.
/// </summary>
internal static class DiskTools
{
    public static bool IsLetter(string? letter) =>
        letter is { Length: 1 } && char.IsAsciiLetter(letter[0]);

    /// <summary>Open the drive's root in File Explorer.</summary>
    public static ProcessStartInfo? Explorer(string letter) =>
        IsLetter(letter)
            ? new ProcessStartInfo($"{char.ToUpperInvariant(letter[0])}:\\") { UseShellExecute = true }
            : null;

    /// <summary>Disk Cleanup, already pointed at the drive.</summary>
    public static ProcessStartInfo? Cleanup(string letter) =>
        IsLetter(letter)
            ? new ProcessStartInfo("cleanmgr.exe", $"/d {char.ToUpperInvariant(letter[0])}")
            {
                UseShellExecute = true,
            }
            : null;

    public static ProcessStartInfo Management() =>
        new("diskmgmt.msc") { UseShellExecute = true };

    public static void OpenExplorer(string letter) => Launch(Explorer(letter), "Explorer");

    public static void OpenCleanup(string letter) => Launch(Cleanup(letter), "Disk Cleanup");

    public static void OpenManagement() => Launch(Management(), "Disk Management");

    private static void Launch(ProcessStartInfo? start, string what)
    {
        if (start is null)
        {
            return;
        }
        try
        {
            Process.Start(start)?.Dispose();
        }
        catch (Exception error)
        {
            // A tool that is missing or was cancelled at the elevation prompt
            // is not worth a dialog; the widget carries on.
            Diag.ReportException(what, error);
        }
    }
}
