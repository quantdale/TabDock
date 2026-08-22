using System;
using System.Windows.Threading;

namespace TabDock.Services;

/// <summary>
/// Deterministic single-ownership slot for REPLACEABLE deferred UI work
/// (Wave 2E). This is the correctness core behind the handwritten
/// <c>_field?.Stop(); var t = new DispatcherTimer ...; if
/// (!ReferenceEquals(_field, t)) { t.Stop(); return; }</c> idiom that previously
/// guarded every coalesced container timer (AUDIT25-05/Q5/Q8): when a slot's
/// owner is replaced or cancelled, any already-dispatched tick of the OLD owner
/// must find itself stale and run nothing.
///
/// The class is deliberately free of WPF timing so the stale-suppression
/// contract is unit-testable without a dispatcher pump or sleeps: tokens are
/// opaque objects and "firing a tick" is just calling
/// <see cref="ConsumeIfCurrent"/> / <see cref="IsCurrent"/>.
/// </summary>
internal sealed class ReplaceableWorkSlot
{
    private object? _current;

    /// <summary>Makes <paramref name="token"/> the sole owner, implicitly revoking any previous owner.</summary>
    public void Claim(object token) => _current = token;

    /// <summary>True only while <paramref name="token"/> is the current owner.</summary>
    public bool IsCurrent(object token) => ReferenceEquals(_current, token);

    /// <summary>
    /// One-shot consumption: returns true exactly once for the current owner,
    /// clearing ownership so a second tick of the same timer finds itself stale.
    /// Returns false for any non-current token (replaced or cancelled) without
    /// touching the slot.
    /// </summary>
    public bool ConsumeIfCurrent(object token)
    {
        if (!ReferenceEquals(_current, token))
            return false;
        _current = null;
        return true;
    }

    /// <summary>Revokes ownership outright (logical cancellation).</summary>
    public void Clear() => _current = null;
}

/// <summary>
/// WPF adapter owning ONE replaceable <see cref="DispatcherTimer"/> slot.
/// Scheduling replaces any prior pending work (stopping its timer and revoking
/// its ownership); cancellation stops and disarms; and the stale-guard that
/// keeps a replaced timer's in-flight tick from executing user code is built
/// into the Tick subscription, making the safe AUDIT25-05/Q5/Q8 pattern the
/// only way to use this type.
///
/// Snapshot discipline: <see cref="Schedule"/> receives the user action as a
/// delegate, so anything the caller closes over (active guest, generation,
/// HWND) is captured AT SCHEDULE TIME exactly as the handwritten idiom did —
/// this type never re-reads live owner state at tick time.
///
/// Default priority is <see cref="DispatcherPriority.Background"/>, matching
/// the parameterless <c>new DispatcherTimer</c> construction all migrated call
/// sites used; passing another priority is always an explicit choice.
/// </summary>
internal sealed class ReplaceableDispatcherTimer
{
    private readonly ReplaceableWorkSlot _slot = new();
    private readonly DispatcherPriority _priority;
    private DispatcherTimer? _timer;

    public ReplaceableDispatcherTimer(DispatcherPriority priority = DispatcherPriority.Background)
        => _priority = priority;

    /// <summary>True while a timer is armed in this slot (regardless of staleness races).</summary>
    public bool HasPendingWork => _timer != null;

    /// <summary>
    /// Arms <paramref name="onTick"/> in this slot, replacing any prior pending
    /// timer. When <paramref name="repeatEveryInterval"/> is false the timer
    /// fires once: ownership is consumed and the timer stopped BEFORE the user
    /// action runs, so the action cannot observe (or resurrect) its own timer.
    /// When true, the timer keeps firing until replaced or cancelled — the
    /// bounded periodic shape used by the constraint-refresh probe batch.
    /// </summary>
    public void Schedule(TimeSpan interval, Action onTick, bool repeatEveryInterval = false)
    {
        ArgumentNullException.ThrowIfNull(onTick);
        Cancel();

        var timer = new DispatcherTimer(_priority) { Interval = interval };
        timer.Tick += (_, _) =>
        {
            // Stale-callback suppression is unavoidable by construction: if this
            // timer was replaced or cancelled between arming and dispatch, it
            // owns nothing and must stop silently.
            if (!_slot.IsCurrent(timer))
            {
                timer.Stop();
                return;
            }
            if (!repeatEveryInterval)
            {
                _slot.ConsumeIfCurrent(timer);
                timer.Stop();
            }
            onTick();
        };
        _slot.Claim(timer);
        _timer = timer;
        timer.Start();
    }

    /// <summary>Stops the pending timer (if any) and revokes its ownership; queued-but-undelivered ticks become silent no-ops.</summary>
    public void Cancel()
    {
        _slot.Clear();
        _timer?.Stop();
        _timer = null;
    }
}
