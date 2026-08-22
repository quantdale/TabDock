using System;

namespace TabDock.Services;

/// <summary>
/// Owns coalesced relayout scheduling. Centralizes native mutation batching so
/// one frame cannot issue several identical batches. No WPF types — caller
/// supplies scheduling via delegate.
/// </summary>
/// <remarks>
/// Ownership decisions:
/// - Coalescing (<c>_relayoutPending</c>) and the ensureFinalPass latch
///   (<c>_relayoutAfterPending</c>, Q9) are LIVE production machinery used by
///   <c>ContainerWindow</c>.
/// - Wave 3D (Model B): the former layout-generation stale-callback machinery
///   (<c>InvalidateLayout</c>, pending-frame token, discard branch) was REMOVED.
///   Its sole mutator had zero production callers, so the generation stayed
///   pinned at 0 and the discard branch was unreachable. Executing a queued
///   frame is always safe instead: the execute closure re-reads CURRENT
///   presentation authority at callback time (idempotent under redundant-glue
///   guards), the split settle carries its own live generation guard in
///   <see cref="SplitPresentationController"/>, and container teardown disarms
///   settle callbacks and zeroes the container HWND before dropping state. If a
///   future world-transition ever needs frame cancellation, reintroduce
///   invalidation AT that semantic boundary — deliberately not speculative.
/// - Refusal tracking lives in <see cref="PaneContainmentCoordinator"/> (Wave 3C);
///   the former test-only shims, unread counters, and budget-sink plumbing were
///   deleted as proven dead in Wave 1.
/// </remarks>
public sealed class PresentationLayoutCoordinator
{
    private bool _relayoutPending;
    private bool _relayoutAfterPending;

    public PresentationLayoutCoordinator()
    {
    }

    /// <summary>
    /// Schedules one <paramref name="execute"/> per frame. If already pending,
    /// coalesces (only one callback). <paramref name="scheduleRender"/> is the
    /// WPF-specific BeginInvoke(Render, ...) seam; tests pass a synchronous delegate.
    /// Queued frames are never discarded: they always execute exactly once,
    /// against whatever the presentation state is at callback time.
    /// </summary>
    public void RequestRelayout(Action<Action> scheduleRender, Action execute, bool ensureFinalPass = false)
    {
        // ensureFinalPass must latch even when already pending: WM_EXITSIZEMOVE's
        // final z-order reconciliation must survive a Render already queued from
        // the final WM_WINDOWPOSCHANGED (Q9).
        // A requested "final pass" only needs a SECOND pass when another
        // render callback is already pending. When idle, the pass we are about
        // to schedule is itself the final pass; latching here would always
        // execute two frames for one request.
        if (_relayoutPending)
        {
            if (ensureFinalPass)
                _relayoutAfterPending = true;
            return;
        }
        _relayoutPending = true;
        scheduleRender(() =>
        {
            // Clear BEFORE execute so a re-entrant RequestRelayout inside
            // execute (Q1/Q2) correctly re-queues for the next frame.
            _relayoutPending = false;
            execute();
            if (_relayoutAfterPending)
            {
                _relayoutAfterPending = false;
                RequestRelayout(scheduleRender, execute);
            }
        });
    }
}
