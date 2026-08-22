using System;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Pure decision authority for the pane-containment refusal cache (Wave 0B
/// seam). The REFUSAL STORAGE remains with the presentation layer (currently
/// <c>ContainerWindow._refusedPaneByHwnd</c>); this policy owns the DECISION
/// that storage feeds:
///
/// <code>
/// visible guest + same refused rect     => suppression allowed
/// hidden  guest + same refused rect     => suppression MUST NOT apply
/// </code>
///
/// The hidden-guest half is the minimize/restore invariant fixed in 3591ee3:
/// a guest hidden by container minimize whose native minimum previously
/// refused the rect it is being restored to must always receive a fresh
/// PositionAndShow / PositionGuestsDeferred, otherwise it is re-pinned in
/// z-order but never re-shown (permanently blank container until an unrelated
/// resize).
/// </summary>
public static class PaneContainmentPolicy
{
    /// <summary>The glue epsilon used for every requested-vs-recorded pane comparison.</summary>
    internal const int RectEpsilon = 1;

    /// <summary>
    /// True when the recorded refusal matches the requested pane rect within
    /// the glue epsilon — "the guest already refused THIS rect".
    /// </summary>
    public static bool MatchesWithinEpsilon(NativeMethods.RECT refused, NativeMethods.RECT requested)
    {
        return Math.Abs(refused.left - requested.left) <= RectEpsilon
            && Math.Abs(refused.top - requested.top) <= RectEpsilon
            && Math.Abs(refused.right - requested.right) <= RectEpsilon
            && Math.Abs(refused.bottom - requested.bottom) <= RectEpsilon;
    }

    /// <summary>
    /// Exact rect equality, used to dedupe repeated refusal records so one
    /// persistent non-compliance produces one diagnostic, not a stream.
    /// </summary>
    public static bool IsExactSameRect(NativeMethods.RECT prior, NativeMethods.RECT rect)
        => prior.left == rect.left && prior.top == rect.top
            && prior.right == rect.right && prior.bottom == rect.bottom;

    /// <summary>
    /// The bounded non-compliance decision: skip the geometry write only when
    /// the guest is CURRENTLY VISIBLE and its recorded refusal covers the
    /// exact requested rect. A hidden guest always gets the fresh position
    /// attempt even against a stale identical refusal.
    /// </summary>
    public static bool ShouldSuppressRepositioning(
        bool guestCurrentlyVisible,
        NativeMethods.RECT refusedRect,
        NativeMethods.RECT requestedRect)
        => guestCurrentlyVisible && MatchesWithinEpsilon(refusedRect, requestedRect);
}
