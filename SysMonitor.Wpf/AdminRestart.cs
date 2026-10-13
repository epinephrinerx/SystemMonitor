using System.Diagnostics;

namespace SysMonitor;

/// <summary>
/// Starting a second copy of the app with administrator rights, on the user's
/// say-so. Nothing in the app calls this on its own: a normal run stays
/// un-elevated, and an elevated one lasts only until it is closed.
/// </summary>
public static class AdminRestart
{
    /// <summary>Win32 error for "the user declined the UAC prompt".</summary>
    public const int Cancelled = 1223;

    /// <summary>Did the start fail only because the user declined the UAC prompt?</summary>
    public static bool IsDeclined(System.ComponentModel.Win32Exception error) =>
        error.NativeErrorCode == Cancelled;

    public static ProcessStartInfo Start(string exe) => new(exe)
    {
        UseShellExecute = true,
        Verb = "runas",
        WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? string.Empty,
    };
}
