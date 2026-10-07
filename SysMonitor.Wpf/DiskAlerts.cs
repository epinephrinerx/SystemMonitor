namespace SysMonitor;

internal enum DiskLevel
{
    Ok,
    Warm,
    Hot,
}

/// <summary>
/// Decides when a drive that is filling up deserves a warning.
///
/// The thresholds are the ones the bars already use -- amber from 70% used,
/// red from 90% -- so the warning and the colour never disagree. A warning is
/// raised when the program first sees the drive that full (every start-up) and
/// whenever the level goes up; it is not repeated every reading, and it is
/// forgotten once the drive drops back below the amber line.
///
/// Only local drives are tracked. Disk Cleanup does nothing for a mapped share
/// or a removable stick, so warning about one would offer no way out.
/// </summary>
internal sealed class DiskAlertTracker
{
    /// <summary>
    /// Points of slack when falling back, so a drive hovering at 70% does not
    /// raise the same warning every time a reading dips to 69.
    /// </summary>
    internal const int Hysteresis = 2;

    private readonly Dictionary<string, DiskLevel> _level = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DiskLevel> _pending = new(StringComparer.OrdinalIgnoreCase);

    public static DiskLevel Classify(int usage, DiskLevel previous)
    {
        if (usage >= Palette.LoadHotAt
            || (previous == DiskLevel.Hot && usage >= Palette.LoadHotAt - Hysteresis))
        {
            return DiskLevel.Hot;
        }
        if (usage >= Palette.LoadWarmAt
            || (previous >= DiskLevel.Warm && usage >= Palette.LoadWarmAt - Hysteresis))
        {
            return DiskLevel.Warm;
        }
        return DiskLevel.Ok;
    }

    /// <summary>Feed one reading of every drive; non-local drives are ignored.</summary>
    public void Update(IEnumerable<(string Letter, int Usage, bool Local)> drives)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string letter, int usage, bool local) in drives)
        {
            if (!local)
            {
                continue;
            }
            seen.Add(letter);

            bool known = _level.TryGetValue(letter, out DiskLevel before);
            DiskLevel level = Classify(usage, known ? before : DiskLevel.Ok);

            if (level == DiskLevel.Ok)
            {
                _pending.Remove(letter);
            }
            else if (!known || level > before)
            {
                _pending[letter] = level;
            }
            else if (_pending.ContainsKey(letter))
            {
                _pending[letter] = level;       // still waiting; say how bad it is now
            }
            _level[letter] = level;
        }

        foreach (string gone in _level.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _level.Remove(gone);
            _pending.Remove(gone);
        }
    }

    /// <summary>The level to warn about, or null if there is nothing to say.</summary>
    public DiskLevel? Pending(string letter) =>
        _pending.TryGetValue(letter, out DiskLevel level) ? level : null;

    /// <summary>The person has seen it; stay quiet until it gets worse.</summary>
    public void Dismiss(string letter) => _pending.Remove(letter);
}
