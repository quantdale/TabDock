using System;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Pure presentation-drift decision: does a captured guest's observed native
/// geometry/state violate its assigned presentation contract and require one
/// bounded reconciliation via the existing Shepherd authority?
///
/// Centralizes the decision so WinEvent location, foreground, and direct layout
/// checks share one classifier instead of each inventing a parallel "needs glue"
/// heuristic. The callers still own the coalescing, identity, and mutation-gate
/// concerns — this only answers "does this observed state need work?".
///
/// No reparenting, no style mutation, no topmost fight, no polling. A true
/// result means "re-glue once via PositionAndShow / PositionGuestsDeferred and
/// PairZOrderBehind" — the existing bounded path with its refusal guard.
/// </summary>
internal static class GuestPresentationDriftPolicy
{
    public enum DriftKind
    {
        None = 0,
        Zoomed = 1,
        GeometryMismatch = 2,
        NotVisibleButShouldBe = 3,
    }

    public readonly record struct DriftEvaluation(DriftKind Kind, bool NeedsReconciliation);

    /// <summary>
    /// Evaluates a single guest against its assigned pane rect. The assigned rect
    /// is the current content-host client rect (single) or the pane's half (split).
    /// Observed is the guest's current GetWindowRect. Zoomed/iconic/visible are
    /// live IsZoomed/IsIconic/IsWindowVisible probes.
    ///
    /// Bounded fail-closed: a guest that is not visible but should be (hidden
    /// by container minimize) is NOT considered drifting — the container's own
    /// minimize/restore path owns that. Iconic is handled by the minimize timer,
    /// so iconic also does not request a drift reconciliation here.
    /// </summary>
    public static DriftEvaluation EvaluateSingle(
        NativeMethods.RECT assignedRect,
        NativeMethods.RECT observedRect,
        bool isZoomed,
        bool isIconic,
        bool isVisible,
        bool shouldBeVisible)
    {
        if (isIconic)
            return new DriftEvaluation(DriftKind.None, false);
        if (!shouldBeVisible)
            return new DriftEvaluation(DriftKind.None, false);
        if (!isVisible)
            return new DriftEvaluation(DriftKind.NotVisibleButShouldBe, true);
        if (isZoomed)
            return new DriftEvaluation(DriftKind.Zoomed, true);
        if (assignedRect.Width <= 0 || assignedRect.Height <= 0)
            return new DriftEvaluation(DriftKind.None, false);
        bool matches = PaneContainmentPolicy.MatchesWithinEpsilon(observedRect, assignedRect);
        return matches
            ? new DriftEvaluation(DriftKind.None, false)
            : new DriftEvaluation(DriftKind.GeometryMismatch, true);
    }

    /// <summary>
    /// Split pair variant: evaluates both members. Either member drifting
    /// requires a pair reconciliation (the atomic PositionGuestsDeferred path
    /// re-establishes [top, bottom, container] together).
    /// </summary>
    public static bool NeedsPairReconciliation(
        NativeMethods.RECT leftAssigned,
        NativeMethods.RECT rightAssigned,
        NativeMethods.RECT leftObserved,
        NativeMethods.RECT rightObserved,
        bool leftZoomed,
        bool rightZoomed,
        bool leftVisible,
        bool rightVisible,
        bool leftIconic,
        bool rightIconic,
        bool shouldBeVisible)
    {
        var leftEval = EvaluateSingle(leftAssigned, leftObserved, leftZoomed, leftIconic, leftVisible, shouldBeVisible);
        var rightEval = EvaluateSingle(rightAssigned, rightObserved, rightZoomed, rightIconic, rightVisible, shouldBeVisible);
        return leftEval.NeedsReconciliation || rightEval.NeedsReconciliation;
    }
}
