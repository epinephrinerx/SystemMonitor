namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The four phase-3 integrations. Phase 2a shows their controls but wires them
/// to no-op implementations behind these interfaces, so phase 3 can replace
/// them without touching the views.
/// </summary>
public interface IAutostart
{
    bool IsEnabled { get; }
    void Set(bool enabled);
}

public interface ITray : IDisposable
{
    /// <summary>The tray icon exists only while the window is hidden.</summary>
    bool Available { get; }

    /// <summary>
    /// Create the icon if needed and set its tooltip; false when the desktop
    /// has no tray to put it in. A caller that gets false must not hide the
    /// window, or there would be no way back.
    /// </summary>
    bool Show(string tooltip);

    void Hide();
}

public interface IRestarter
{
    /// <summary>True when the new copy started, so this one can shut down.</summary>
    bool Restart();
}

/// <summary>
/// One check: whether it succeeded, the release version when it did, and the
/// reason when it did not (a missing Linux asset is a failure, not "current").
/// </summary>
public sealed record UpdateCheck(bool Succeeded, string? Version, string? Reason = null);

public interface IUpdater
{
    string CurrentVersion { get; }

    /// <summary>Check for a newer release; a failure is a result, not an exception.</summary>
    Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Download and start the installer. False means the download failed.</summary>
    Task<bool> DownloadAndInstallAsync(
        string version, IProgress<int>? progress, CancellationToken cancellationToken);
}

public sealed class NoopAutostart : IAutostart
{
    public bool IsEnabled => false;
    public void Set(bool enabled) { }
}

public sealed class NoopTray : ITray
{
    public bool Available => false;
    public bool Show(string tooltip) => false;
    public void Hide() { }
    public void Dispose() { }
}

public sealed class NoopRestarter : IRestarter
{
    public bool Restart() => false;
}

/// <summary>Phase 2a: reports the current version and finds nothing newer.</summary>
public sealed class NoopUpdater : IUpdater
{
    public NoopUpdater(string currentVersion)
    {
        CurrentVersion = currentVersion;
    }

    public string CurrentVersion { get; }

    public Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new UpdateCheck(true, CurrentVersion));

    public Task<bool> DownloadAndInstallAsync(
        string version, IProgress<int>? progress, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}

/// <summary>
/// The sidebar update panel's state machine (spec section 8): idle, checking,
/// up to date, found, downloading, installing, failed. The actual network work
/// belongs to <see cref="IUpdater"/>; this keeps the labels correct.
/// </summary>
public sealed class UpdatePanelState
{
    public enum Phase
    {
        Idle,
        Checking,
        UpToDate,
        Found,
        Downloading,
        Installing,
        DownloadFailed,
        CheckFailed,
    }

    public UpdatePanelState(string currentVersion)
    {
        CurrentVersion = currentVersion;
    }

    public string CurrentVersion { get; }
    public Phase State { get; private set; } = Phase.Idle;
    public string? PendingVersion { get; private set; }
    public int Progress { get; private set; }

    public bool Busy => State is Phase.Checking or Phase.Downloading or Phase.Installing;

    public void StartCheck()
    {
        State = Phase.Checking;
        Progress = 0;
    }

    public void Checked(string currentVersion, string? newestVersion)
    {
        if (newestVersion is not null && IsNewer(newestVersion, currentVersion))
        {
            PendingVersion = newestVersion;
            State = Phase.Found;
        }
        else
        {
            PendingVersion = null;
            State = Phase.UpToDate;
        }
    }

    public void CheckFailed()
    {
        PendingVersion = null;
        State = Phase.CheckFailed;
    }

    public void StartDownload()
    {
        State = Phase.Downloading;
        Progress = 0;
    }

    public void DownloadProgress(int percent)
    {
        Progress = Math.Clamp(percent, 0, 100);
    }

    public void Installing()
    {
        State = Phase.Installing;
    }

    public void DownloadFailed()
    {
        PendingVersion = null;
        State = Phase.DownloadFailed;
    }

    public string ButtonLabel(Lang lang) =>
        State == Phase.Found ? lang["download_install"] : lang["check_updates"];

    /// <summary>Null means "hide the status line": nothing has been done yet.</summary>
    public string? StatusText(Lang lang) => State switch
    {
        Phase.Checking => lang["checking"],
        Phase.UpToDate => lang["up_to_date"],
        Phase.Found => $"{lang["update_found"]} {PendingVersion}",
        Phase.Downloading => $"{lang["downloading"]} {Progress}%",
        Phase.Installing => lang["installing"],
        Phase.DownloadFailed => lang["download_failed"],
        Phase.CheckFailed => lang["check_failed"],
        _ => null,
    };

    /// <summary>
    /// "3.1.0" newer than "3.0.9": three numeric parts, the way the updater
    /// compares tags once the leading v is gone.
    /// </summary>
    public static bool IsNewer(string candidate, string current)
    {
        static int[] Parts(string value) =>
            value.TrimStart('v', 'V')
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => int.TryParse(part, out int number) ? number : 0)
                .Concat(new[] { 0, 0, 0 })
                .Take(3)
                .ToArray();

        int[] left = Parts(candidate);
        int[] right = Parts(current);
        for (int i = 0; i < 3; i++)
        {
            if (left[i] != right[i])
            {
                return left[i] > right[i];
            }
        }
        return false;
    }
}
