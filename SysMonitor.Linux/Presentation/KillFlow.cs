namespace SysMonitor.Linux.Presentation;

/// <summary>
/// The identity of one process, captured from its row: the PID, the start time
/// and the name. A PID alone is not enough, because the kernel reuses it; all
/// three have to still match when the signal is about to be sent.
///
/// The start time is nullable on purpose (Amendment 3): "unknown" cannot equal
/// a real value, so a row whose stat file gave no start time is never signalled.
/// </summary>
public readonly record struct ProcessTarget(int Pid, ulong? StartTime, string Name);

/// <summary>
/// One fresh read of /proc/&lt;pid&gt;: what the kill rules check at action
/// time. Protection is decided from this, never from the row that was clicked,
/// because a process can change owner or become a kernel thread meanwhile.
/// </summary>
public sealed class ProcessFacts
{
    public required int Pid { get; init; }
    public required string Name { get; init; }

    /// <summary>
    /// Field 22 of <c>/proc/&lt;pid&gt;/stat</c>, clock ticks since boot, or
    /// null when the stat file could not be read or held no start time.
    /// </summary>
    public ulong? StartTime { get; init; }

    /// <summary>The real UID, or null when the status file did not say.</summary>
    public int? Uid { get; init; }

    public int ParentPid { get; init; }
    public bool KernelThread { get; init; }
    public bool Readable { get; init; }
}

/// <summary>How a pin attempt ended.</summary>
public enum PinState
{
    /// <summary>The process is pinned and the handle may check and signal it.</summary>
    Pinned,

    /// <summary><c>pidfd_open</c> is not implemented by this kernel (ENOSYS).</summary>
    Unsupported,

    /// <summary><c>pidfd_open</c> failed for another reason; the text says why.</summary>
    Failed,
}

/// <summary>The result of <see cref="IProcessSignals.Pin"/>.</summary>
public readonly record struct PinnedResult(
    PinState State, IPinnedProcess? Process, string? Error)
{
    public static PinnedResult Pinned(IPinnedProcess process) =>
        new(PinState.Pinned, process, null);

    public static PinnedResult Unsupported => new(PinState.Unsupported, null, null);

    public static PinnedResult Failed(string error) => new(PinState.Failed, null, error);
}

/// <summary>
/// One process held open by a pidfd. As long as the descriptor lives the kernel
/// cannot hand the PID to another process, so the check read and the signal
/// reach the same process. Dispose closes the descriptor.
/// </summary>
public interface IPinnedProcess : IDisposable
{
    /// <summary>The process as it is now, or null when it has gone.</summary>
    ProcessFacts? Read();

    /// <summary>SIGTERM or SIGKILL. Null on success, else the system's text.</summary>
    string? Signal(bool force);
}

/// <summary>
/// Everything the kill flow needs from the system, so its rules run against a
/// fake in tests. The real implementation is
/// <c>SysMonitor.Linux.Sensors.ProcessSignals</c>: a pidfd plus /proc.
/// </summary>
public interface IProcessSignals
{
    /// <summary>A fresh read of the process, or null when it no longer exists.</summary>
    ProcessFacts? Read(int pid);

    /// <summary>
    /// Pin the process with <c>pidfd_open(pid, 0)</c>. <see cref="PinState.Failed"/>
    /// carries the system's error text; ENOSYS is reported as
    /// <see cref="PinState.Unsupported"/> so the caller can fall back to kill(2).
    /// </summary>
    PinnedResult Pin(int pid);

    /// <summary>
    /// <c>kill(2)</c> without a pidfd. Only the ENOSYS fallback uses it; the
    /// normal path signals through <see cref="IPinnedProcess.Signal"/>.
    /// </summary>
    string? Signal(int pid, bool force);
}

/// <summary>
/// The wait between SIGTERM and the force question. It polls <c>read</c> until
/// the process is gone or the deadline (on the flow's clock) passes, and
/// returns true when the process is gone.
///
/// The wait is a separate injectable delegate so a test never sleeps: it can
/// hold the wait open on a <c>ManualResetEventSlim</c>, start a second action
/// (another Terminate, <see cref="KillFlow.CancelAll"/>) and then release it,
/// which makes each interleaving deterministic (handoff round 5). The
/// <c>onCancelAllRequested</c> hook is the other half of that: it fires inside
/// <see cref="KillFlow.CancelAll"/> before the gate is taken, so a test can
/// show that the close was requested while Terminate or Force held the gate.
/// The default implementation is the 100 ms poll with the injected clock and
/// sleep.
/// </summary>
public delegate bool ExitWait(Func<ProcessFacts?> read, long deadline);

/// <summary>
/// The one thing that may send SIGKILL after SIGTERM: a token that owns the
/// pinned handle and the target it was opened for (Amendment 4).
///
/// It is created by <see cref="KillFlow.Terminate"/> when the process survived
/// SIGTERM, and it is the only copy of the handle the flow keeps. Disposing it
/// closes the handle exactly once and takes it out of the flow's live table; a
/// token that has been disposed can never signal again, because
/// <see cref="KillFlow.Force"/> refuses it before it looks at the handle.
///
/// Dispose is idempotent and may run on any thread: the UI may close the view
/// while a worker is still asking about a kill.
/// </summary>
public sealed class PendingKill : IDisposable
{
    private readonly Action<PendingKill>? _untrack;

    /// <summary>The handle, or null on the ENOSYS path and once taken or closed.</summary>
    private IPinnedProcess? _handle;

    private int _disposed;

    internal PendingKill(
        ProcessTarget target,
        IPinnedProcess? handle,
        bool usesFallback,
        Action<PendingKill>? untrack)
    {
        Target = target;
        _handle = handle;
        UsesFallback = usesFallback;
        _untrack = untrack;
    }

    /// <summary>The exact process this token was opened for.</summary>
    public ProcessTarget Target { get; }

    /// <summary>True once the handle is closed and the token is out of the flow.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// True on a kernel without pidfds: there is no handle, so the force step
    /// re-checks /proc and uses kill(2). It still never pins.
    /// </summary>
    internal bool UsesFallback { get; }

    /// <summary>
    /// Hand the pinned process out for the one Force that owns the flow's lock.
    /// Null after a dispose, which means "do not signal".
    /// </summary>
    internal IPinnedProcess? TakeHandle() => Interlocked.Exchange(ref _handle, null);

    /// <summary>
    /// Close the pinned handle exactly once and report the token as gone. Safe
    /// to call twice and from several threads.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _handle, null)?.Dispose();
        _untrack?.Invoke(this);
    }
}

/// <summary>The outcome of one kill attempt.</summary>
public enum KillState
{
    /// <summary>The process is gone.</summary>
    Gone,

    /// <summary>It survived SIGTERM: the caller may offer to force it.</summary>
    NeedsForce,

    /// <summary>
    /// The signal was refused, the rules said no, or the PID now belongs to
    /// another process.
    /// </summary>
    Failed,

    /// <summary>
    /// There was nothing left to force: the token was already disposed
    /// (declined, replaced, exited, or the view closed) or already used. No
    /// signal was sent.
    /// </summary>
    Cancelled,
}

/// <summary>
/// The result of one <see cref="KillFlow"/> step. A
/// <see cref="KillState.NeedsForce"/> result carries the <see cref="PendingKill"/>
/// token that owns the pinned handle; the caller keeps that token while it asks
/// the person and hands it back to <see cref="KillFlow.Force"/>.
/// </summary>
public readonly record struct KillResult(
    KillState State, string? Error, PendingKill? Pending = null)
{
    public static KillResult Gone => new(KillState.Gone, null);

    public static KillResult NeedsForce(PendingKill pending) =>
        new(KillState.NeedsForce, null, pending);

    public static KillResult Failed(string error) => new(KillState.Failed, error);

    public static KillResult Cancelled => new(KillState.Cancelled, null);
}

/// <summary>
/// The kill sequence from spec section 5c, with the Amendment 2, 3, 4, 5 and 6
/// safety rules: a pidfd pins the process, the start time and the name are
/// re-checked from a fresh read, and the protection rules are evaluated again
/// before anything is sent. A target without a start time is never signalled,
/// because its identity cannot be proved.
///
/// Concurrency (Amendment 5, tightened by Amendment 6): every change to the
/// live-token table happens inside one <c>_gate</c> lock. Terminate's
/// sequence — check <c>_closed</c>, pin, re-check, SIGTERM, publish — runs
/// inside that lock, and so does Force's SIGKILL. Each of those steps is a
/// fast, non-blocking syscall, so the lock is held only briefly; keeping them
/// in one section is what makes "after CancelAll returns, no signal of any
/// kind is sent" true. Only the ≤3 s exit wait runs outside the lock, on a
/// token that is already published, so <see cref="CancelAll"/> can reach it
/// while the wait is still running.
///
/// The exit wait is injected (see <see cref="ExitWait"/>) so a test never
/// sleeps; <see cref="IProcessSignals.Pin"/> is a caller-supplied fake, so a
/// test can also hold the pin open.
/// </summary>
public sealed class KillFlow
{
    public const int WaitMs = 3000;
    public const int StepMs = 100;

    /// <summary>
    /// Shown when pidfd_open failed without an errno text. A technical detail,
    /// not a sentence: the status line wraps it in mem_failed.
    /// </summary>
    public const string PinFailed = "pidfd_open failed";

    private readonly IProcessSignals _signals;
    private readonly Func<long> _clock;
    private readonly Action<int> _sleep;
    private readonly ExitWait _waitForExit;
    private readonly Lang _lang;
    private readonly int _ownPid;
    private readonly int _ownUid;

    /// <summary>
    /// Test hook (Amendment 6): invoked by <see cref="CancelAll"/> before it
    /// takes the gate. It exists so a test can prove the close was requested
    /// while Terminate or Force still held the gate. Null in production.
    /// </summary>
    private readonly Action? _onCancelAllRequested;

    /// <summary>
    /// Test hook (Amendment 6c): invoked by <see cref="Terminate"/> with the
    /// target's PID, before it takes the gate and after the identity pre-check.
    /// It fires while an earlier kill still holds the gate, so a test can show
    /// that a second Terminate was requested and is blocked rather than late.
    /// Null in production.
    /// </summary>
    private readonly Action<int>? _onTerminateRequested;

    /// <summary>
    /// The one lock that guards the state below. A kill runs on a worker thread
    /// while the window can close on the UI thread, so everything that adds,
    /// replaces, consumes or drops a token is done here.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// The one live token per PID. A token is in here from the moment
    /// <see cref="Terminate"/> publishes it (before the exit wait) until it is
    /// disposed by <see cref="Force"/>, <see cref="Decline"/>,
    /// <see cref="CancelAll"/>, a newer kill for the same PID, or the exit.
    /// Disposing a token takes it out of the table; the flow keeps the handle
    /// in no other place.
    /// </summary>
    private readonly Dictionary<int, PendingKill> _pending = new();

    /// <summary>
    /// Set by <see cref="CancelAll"/> (the view closed) and cleared by
    /// <see cref="Reopen"/>. While it is set nothing is pinned, published or
    /// signalled, and a close cannot interleave with a signal that is already
    /// under way (Amendment 6).
    /// </summary>
    private bool _closed;

    public KillFlow(
        IProcessSignals signals,
        Func<long>? clock = null,
        Action<int>? sleep = null,
        Lang? lang = null,
        int ownPid = 0,
        int ownUid = -1,
        ExitWait? waitForExit = null,
        Action? onCancelAllRequested = null,
        Action<int>? onTerminateRequested = null)
    {
        _signals = signals;
        _clock = clock ?? (() => Environment.TickCount64);
        _sleep = sleep ?? Thread.Sleep;
        _waitForExit = waitForExit ?? DefaultWait;
        _lang = lang ?? new Lang("en");
        _ownPid = ownPid > 0 ? ownPid : Environment.ProcessId;
        _ownUid = ownUid;
        _onCancelAllRequested = onCancelAllRequested;
        _onTerminateRequested = onTerminateRequested;
    }

    /// <summary>
    /// SIGTERM after pinning and re-checking the identity and the protection
    /// rules, then wait up to <see cref="WaitMs"/> for the process to go. When it
    /// does not go, the pinned handle is already owned by the token published
    /// before the wait, and that token is returned inside
    /// <see cref="KillState.NeedsForce"/>.
    ///
    /// Step 1 runs under the gate (Amendment 6): check <c>_closed</c>, pin,
    /// re-check, SIGTERM and publish are one lock section, since each is fast
    /// and non-blocking. Only the exit wait is outside it. The
    /// <c>onTerminateRequested</c> test hook fires with the PID before the gate
    /// is taken (Amendment 6c).
    /// </summary>
    public KillResult Terminate(ProcessTarget target)
    {
        if (!HasIdentity(target))
        {
            return KillResult.Failed(Changed(target, target.Name));
        }

        PendingKill? token = null;
        Func<ProcessFacts?>? read = null;

        // Before the gate, on purpose (Amendment 6c): a test can see that a
        // second Terminate was requested while an earlier kill still holds the
        // gate, instead of racing a timer. The identity pre-check above has
        // already passed, so nothing fires for a row that cannot be signalled.
        _onTerminateRequested?.Invoke(target.Pid);

        // Step 1, under the gate: a closed flow never signals again, and no
        // close can land between the check, the pin, the signal and the
        // publish. A kill started after CancelAll therefore sends nothing.
        lock (_gate)
        {
            if (_closed)
            {
                return KillResult.Cancelled;
            }

            PinnedResult pin = _signals.Pin(target.Pid);
            if (pin.State == PinState.Failed)
            {
                return KillResult.Failed(pin.Error ?? PinFailed);
            }

            if (pin.State == PinState.Unsupported)
            {
                // pidfd_open returned ENOSYS: the kernel has no pidfd. The same
                // rule applies here — the _closed check has already happened
                // above, then re-check through /proc and use kill(2), all under
                // the gate. There is still a small window between the check and
                // the call that a pidfd would have closed; nothing better exists
                // on such a kernel, so it is the documented fallback, not the
                // normal path.
                KillResult? fallbackCheck = Check(_signals.Read(target.Pid), target);
                if (fallbackCheck is { } fallbackFailure)
                {
                    return fallbackFailure;
                }

                string? fallbackError = _signals.Signal(target.Pid, force: false);
                if (fallbackError is not null)
                {
                    return KillResult.Failed(fallbackError);
                }

                // No handle exists to own; the token carries the fallback flag
                // and the force step re-checks before kill(2), as Amendment 3
                // says.
                token = Publish(target, null, usesFallback: true);
                read = () => _signals.Read(target.Pid);
            }
            else
            {
                if (pin.Process is not { } pinned)
                {
                    return KillResult.Failed(PinFailed);
                }

                // The handle belongs to the token once Publish stores it; until
                // then this method closes it on every exit path.
                bool handled = false;
                try
                {
                    KillResult? check = Check(pinned.Read(), target);
                    if (check is { } failed)
                    {
                        return failed;
                    }

                    string? error = pinned.Signal(force: false);
                    if (error is not null)
                    {
                        return KillResult.Failed(error);
                    }

                    token = Publish(target, pinned, usesFallback: false);
                    handled = true;
                    read = pinned.Read;
                }
                finally
                {
                    if (!handled)
                    {
                        pinned.Dispose();
                    }
                }
            }
        }

        // Step 2: the exit wait is the only part outside the lock. The token is
        // already published, so a close or a newer kill can reach it while the
        // wait is still running (it then comes back as Cancelled).
        return token is null
            ? KillResult.Cancelled
            : AwaitExit(token, read!);
    }

    /// <summary>
    /// SIGKILL through the handle the token owns. Never waits for a
    /// confirmation and never pins.
    ///
    /// Everything happens under the lock, as Amendment 5 orders: a token that
    /// was already disposed (declined, replaced, gone, or the view closed), or
    /// that is not the current token for its PID, does nothing and returns
    /// <see cref="KillState.Cancelled"/>. So an old confirm dialog can only act
    /// on the token it opened, and can never reach a newer process.
    /// </summary>
    public KillResult Force(PendingKill? pending)
    {
        if (pending is null)
        {
            return KillResult.Cancelled;
        }

        lock (_gate)
        {
            if (_closed
                || pending.IsDisposed
                || !_pending.TryGetValue(pending.Target.Pid, out PendingKill? current)
                || !ReferenceEquals(current, pending))
            {
                return KillResult.Cancelled;
            }

            // A fast syscall, allowed under the lock (Amendment 5): it is what
            // keeps CancelAll from returning before the signal is done, so
            // nothing can be signalled after the view has closed.
            KillResult result;
            if (pending.UsesFallback)
            {
                result = ForceFallback(pending.Target);
            }
            else if (pending.TakeHandle() is { } handle)
            {
                try
                {
                    KillResult? check = Check(handle.Read(), pending.Target);
                    if (check is { } failed)
                    {
                        result = failed;
                    }
                    else
                    {
                        string? error = handle.Signal(force: true);
                        result = error is null
                            ? KillResult.Gone
                            : KillResult.Failed(error);
                    }
                }
                finally
                {
                    handle.Dispose();
                }
            }
            else
            {
                result = KillResult.Cancelled;
            }

            _pending.Remove(pending.Target.Pid);
            pending.Dispose();
            return result;
        }
    }

    /// <summary>
    /// The person said no. Under the lock, drop the token when it is still the
    /// current one and close the handle it owns. Nothing else is sent. A token
    /// from an older dialog simply has nothing left to close.
    /// </summary>
    public void Decline(PendingKill? pending)
    {
        if (pending is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_pending.TryGetValue(pending.Target.Pid, out PendingKill? current)
                && ReferenceEquals(current, pending))
            {
                _pending.Remove(pending.Target.Pid);
            }
            pending.Dispose();
        }
    }

    /// <summary>Kept for callers written before Amendment 5 named it Decline.</summary>
    public void Cancel(PendingKill? pending) => Decline(pending);

    /// <summary>
    /// The view is closing. First the <c>onCancelAllRequested</c> test hook
    /// fires, then, under the lock: mark the flow closed, dispose every live
    /// token and clear the table, so no descriptor outlives the window and no
    /// open dialog can still signal. Because Terminate and Force signal under
    /// the same gate (Amendment 6), a close that is requested while one of them
    /// holds it waits, and once this method returns no signal of any kind is
    /// sent. A kill that is inside its exit wait finds its token disposed and
    /// returns <see cref="KillState.Cancelled"/>.
    /// </summary>
    public void CancelAll()
    {
        // Before the gate, on purpose: a test that is inside a signal callback
        // sees this fire while the gate is still held by Terminate or Force,
        // which is what proves the two overlap.
        _onCancelAllRequested?.Invoke();

        lock (_gate)
        {
            _closed = true;

            // Copy before disposing: a token's dispose takes the same
            // (reentrant) lock and would otherwise change the table while it
            // is being enumerated.
            PendingKill[] live = _pending.Values.ToArray();
            _pending.Clear();
            foreach (PendingKill pending in live)
            {
                pending.Dispose();
            }
        }
    }

    /// <summary>
    /// The view opened again after a <see cref="CancelAll"/>. The flow is
    /// created once for the model (Amendment 5), so this is how a new view
    /// session becomes usable without a second instance.
    /// </summary>
    public void Reopen()
    {
        lock (_gate)
        {
            _closed = false;
        }
    }

    /// <summary>
    /// Ends any number of processes with SIGTERM. Returns the tokens that need
    /// the force question, plus the first error, if any. Each token is owned by
    /// the caller until it is forced or declined.
    /// </summary>
    public (IReadOnlyList<PendingKill> Stubborn, string? Error) TerminateAll(
        IEnumerable<ProcessTarget> targets)
    {
        var stubborn = new List<PendingKill>();
        string? error = null;
        foreach (ProcessTarget target in targets)
        {
            KillResult result = Terminate(target);
            if (result.State == KillState.NeedsForce && result.Pending is { } pending)
            {
                stubborn.Add(pending);
            }
            else if (result.State == KillState.Failed && error is null)
            {
                error = result.Error;
            }
        }
        return (stubborn, error);
    }

    /// <summary>The default wait: poll every <see cref="StepMs"/> until the deadline.</summary>
    private bool DefaultWait(Func<ProcessFacts?> read, long deadline)
    {
        while (read() is not null && _clock() < deadline)
        {
            _sleep(StepMs);
        }
        return read() is null;
    }

    /// <summary>
    /// The ENOSYS path of <see cref="Force"/>: re-check, then kill(2), under
    /// the gate the caller already holds.
    /// </summary>
    private KillResult ForceFallback(ProcessTarget target)
    {
        KillResult? check = Check(_signals.Read(target.Pid), target);
        if (check is { } failed)
        {
            return failed;
        }

        string? error = _signals.Signal(target.Pid, force: true);
        return error is null ? KillResult.Gone : KillResult.Failed(error);
    }

    /// <summary>
    /// Store the token for the target. Called by <see cref="Terminate"/> with
    /// the gate already held, in the same section as its <c>_closed</c> check,
    /// the pin and the signal (Amendment 6), so a close cannot land between the
    /// check and the store. The guard below is therefore defensive only; when it
    /// does fire the handle is closed and no token is published. Replacing an
    /// earlier token for the PID disposes it before the new token is stored, so
    /// its untrack cannot drop the new one.
    /// </summary>
    private PendingKill? Publish(
        ProcessTarget target, IPinnedProcess? handle, bool usesFallback)
    {
        if (_closed)
        {
            handle?.Dispose();
            return null;
        }

        var token = new PendingKill(target, handle, usesFallback, Untrack);
        if (_pending.Remove(target.Pid, out PendingKill? earlier))
        {
            earlier.Dispose();
        }
        _pending[target.Pid] = token;
        return token;
    }

    /// <summary>
    /// The exit wait of <see cref="Terminate"/>: it runs outside the lock, then
    /// the token is settled under it. A token disposed meanwhile was declined,
    /// replaced or cleared by the closing view, so the result is
    /// <see cref="KillState.Cancelled"/> and no handle is touched again. A
    /// process that exited has its token removed and disposed once.
    /// </summary>
    private KillResult AwaitExit(PendingKill token, Func<ProcessFacts?> read)
    {
        long deadline = _clock() + WaitMs;
        bool gone = _waitForExit(read, deadline);

        lock (_gate)
        {
            if (token.IsDisposed)
            {
                return KillResult.Cancelled;
            }

            if (gone || read() is null)
            {
                RemoveCurrent(token);
                return KillResult.Gone;
            }

            return KillResult.NeedsForce(token);
        }
    }

    /// <summary>
    /// Null when the fresh reading still describes the chosen process and the
    /// rules allow ending it. The start time catches a reused PID; the name is
    /// the second half of the triple the row captured. A start time that is
    /// missing on either side is not an identity, so it aborts.
    /// </summary>
    private KillResult? Check(ProcessFacts? facts, ProcessTarget target)
    {
        if (facts is null)
        {
            return KillResult.Gone;
        }

        if (target.StartTime is not > 0
            || facts.StartTime is not > 0
            || facts.StartTime != target.StartTime
            || !string.Equals(facts.Name, target.Name, StringComparison.Ordinal))
        {
            return KillResult.Failed(Changed(target, facts.Name));
        }

        if (ProcessProtection.IsProtected(facts, _ownPid, _ownUid))
        {
            return KillResult.Failed(_lang["mem_protected"]);
        }

        return null;
    }

    /// <summary>No identity, no signal: the row must carry a real start time.</summary>
    private static bool HasIdentity(ProcessTarget target) => target.StartTime is > 0;

    private string Changed(ProcessTarget target, string? name) =>
        _lang.Get("mem_pid_changed", target.Pid, name ?? target.Name);

    /// <summary>
    /// Remove a token that is still current and dispose it. Called under the
    /// gate from <see cref="AwaitExit"/>; dispose's untrack takes the same
    /// reentrant lock and finds the table already clean.
    /// </summary>
    private void RemoveCurrent(PendingKill token)
    {
        if (_pending.TryGetValue(token.Target.Pid, out PendingKill? current)
            && ReferenceEquals(current, token))
        {
            _pending.Remove(token.Target.Pid);
        }
        token.Dispose();
    }

    /// <summary>
    /// A token disposed itself: forget it, unless a newer token already owns the
    /// PID. Called by <see cref="PendingKill.Dispose"/>; idempotent, and it
    /// takes the gate because a token may be disposed from another thread.
    /// </summary>
    private void Untrack(PendingKill pending)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(pending.Target.Pid, out PendingKill? current)
                && ReferenceEquals(current, pending))
            {
                _pending.Remove(pending.Target.Pid);
            }
        }
    }
}
