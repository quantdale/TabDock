using System;
using System.Collections.Generic;

namespace TabDock.Services;

/// <summary>
/// Owns coalesced relayout scheduling with generation-token stale callback
/// suppression. Centralizes native mutation batching so one frame cannot issue
/// several identical batches. No WPF types — caller supplies scheduling via delegate.
/// </summary>
/// <remarks>
/// Ownership decisions (Wave-1 cleanup):
/// - Relayout coalescing/stale suppression is LIVE production machinery used by
///   <c>ContainerWindow</c>; <see cref="InvalidateLayout"/> is its real
///   invalidation transition (Wave 3C decides whether more production paths
///   begin invalidating or the model is simplified further).
/// - Refusal tracking was removed here: <c>ContainerWindow._refusedPaneByHwnd</c>
///   is the single existing owner (Wave 3B re-examines ownership placement).
/// - The former test-only shims (<c>CoalesceAndExecute</c>,
///   <c>NeedsPanePositionForTest</c>), unread layout counters, and budget-sink
///   plumbing were deleted as proven dead.
/// </remarks>
public sealed class PresentationLayoutCoordinator
{
    private long _layoutGeneration;
    private long _pendingLayoutGeneration;
    private bool _relayoutPending;
    private bool _relayoutAfterPending;

    public PresentationLayoutCoordinator()
    {
    }

    public void InvalidateLayout() => _layoutGeneration++;

    /// <summary>
    /// Schedules one <paramref name="execute"/> per frame. If already pending,
    /// coalesces (only one callback). <paramref name="scheduleRender"/> is the
    /// WPF-specific BeginInvoke(Render, ...) seam; tests pass a synchronous delegate.
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
        // Coalescing token: each queued frame gets a monotonic id so a stale
        // Render callback that was queued before InvalidateLayout/exit can be
        // discarded. Previously gen was allocated but discarded (_ = gen), so
        // stale frames still executed against a changed layout world. Now capture
        // the layout generation at schedule time and gate at callback time.
        long gen = ++_pendingLayoutGeneration;
        long scheduledLayoutGen = _layoutGeneration;
        scheduleRender(() =>
        {
            // Clear BEFORE execute so a re-entrant RequestRelayout inside
            // execute (Q1/Q2) correctly re-queues for the next frame.
            _relayoutPending = false;
            if (scheduledLayoutGen != _layoutGeneration)
            {
                // Stale frame: layout was invalidated between schedule and
                // callback (member removed, mode changed, container closed).
                // Discard this execute, but still honor a requested final pass:
                // a pending frame's follow-up (_relayoutAfterPending) OR an idle
                // ensureFinalPass whose frame went stale before executing must
                // re-schedule a fresh pass so a WM_EXITSIZEMOVE final z-order
                // reconciliation is not lost.
                if (_relayoutAfterPending || ensureFinalPass)
                {
                    _relayoutAfterPending = false;
                    RequestRelayout(scheduleRender, execute, ensureFinalPass);
                }
                return;
            }
            execute();
            if (_relayoutAfterPending)
            {
                _relayoutAfterPending = false;
                RequestRelayout(scheduleRender, execute);
            }
            _ = gen;
        });
    }
}
