namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The start-up update check (spec section 8, Amendment 2 item 6): check once;
/// if that failed and the setting is still on, wait 60 s and check once more.
/// If the setting was turned off during the wait, the retry is skipped. Nothing
/// is downloaded and nothing is shown: the panel changes only through the
/// check that is passed in.
///
/// The delay is injectable so a test runs in no time, and the check itself is
/// a seam: the shell passes its panel-aware one, so this class owns the
/// sequence and the window owns no copy of it.
/// </summary>
public sealed class UpdateAutoCheck
{
    public const int RetrySeconds = 60;

    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(RetrySeconds);

    private readonly IUpdater _updater;
    private readonly Func<bool> _settingOn;
    private readonly Func<CancellationToken, Task<UpdateCheck>> _check;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="updater">The updater whose result the check means.</param>
    /// <param name="settingOn">Reads <c>auto_check_updates</c> at the moment it is asked.</param>
    /// <param name="check">One check through the panel; the updater's own when omitted.</param>
    /// <param name="delay">The 60 s wait; a real delay when omitted.</param>
    public UpdateAutoCheck(
        IUpdater updater,
        Func<bool> settingOn,
        Func<CancellationToken, Task<UpdateCheck>>? check = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _updater = updater;
        _settingOn = settingOn;
        _check = check ?? updater.CheckAsync;
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
    }

    public IUpdater Updater => _updater;

    /// <summary>Whether the setting is on right now; the retry asks again.</summary>
    public bool Enabled => _settingOn();

    /// <summary>
    /// The start-up check and its one retry. Null means the setting was off and
    /// nothing was asked.
    /// </summary>
    public async Task<UpdateCheck?> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_settingOn())
        {
            return null;
        }

        UpdateCheck result = await _check(cancellationToken).ConfigureAwait(false);
        if (result.Succeeded || !_settingOn())
        {
            return result;
        }

        await _delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        if (!_settingOn())
        {
            return result;
        }
        return await _check(cancellationToken).ConfigureAwait(false);
    }
}
