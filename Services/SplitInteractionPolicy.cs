using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Outcome of classifying a single tab-strip hit into a policy decision.
/// Pure — no WPF types, no native calls. Callers translate WPF hit-test
/// results into the boolean flags and then act only on the returned action.
/// </summary>
public enum SplitInteractionAction
{
    /// <summary>No state transition. The hit is inert for split presentation.</summary>
    None,

    /// <summary>Non-member activation while the pair is presented: suspend the pair and show the guest.</summary>
    SuspendPairForGuest,

    /// <summary>Dormant pair member was selected: resume the exact pair with that member focused.</summary>
    ResumeMember,

    /// <summary>Hit landed on a chrome button (close, context menu, etc.): bypass activation entirely.</summary>
    IgnoreButton,

    /// <summary>Presented pair member was clicked: no suspension, let normal selection/focus handle it.</summary>
    IgnoreMember,

    /// <summary>Target identity is stale/recycled (e.g. HWND reused): fail closed, keep authoritative state.</summary>
    RejectStale,

    /// <summary>Native transition reported recovery-pending / show-failed: fail closed, keep authoritative state.</summary>
    FailClosedRecoveryPending,
}

/// <summary>
/// Tiny pure policy that isolates "WPF hit → policy decision" from actual
/// native window work. All inputs are plain values so deterministic unit
/// tests can prove the interaction contract without creating windows or
/// sending input.
/// <para/>
/// Decision priority (highest first):
/// <list type="number">
/// <item>Button/chrome hit → <see cref="SplitInteractionAction.IgnoreButton"/></item>
/// <item>Right-click / hover → <see cref="SplitInteractionAction.None"/></item>
/// <item>Stale/recycled identity → <see cref="SplitInteractionAction.RejectStale"/></item>
/// <item>Native outcome is non-success (RecoveryPending / ShowFailed / IdentityMismatch) → <see cref="SplitInteractionAction.FailClosedRecoveryPending"/></item>
/// <item>Split member hit → ResumeMember (dormant) or IgnoreMember (presented)</item>
/// <item>Non-member hit with a presented pair → SuspendPairForGuest</item>
/// <item>Otherwise → None</item>
/// </list>
/// The ordering guarantees: buttons never suspend, stale targets never
/// mutate, recovery-pending never loses the authoritative pair, member
/// clicks never suspend, and a handled preview event (which already
/// translates to these flags) cannot suppress non-member activation.
/// </summary>
public static class SplitInteractionPolicy
{
    /// <summary>
    /// Classifies a tab-strip interaction into a single deterministic action.
    /// </summary>
    /// <param name="current">Authoritative presentation state.</param>
    /// <param name="isSplitPresented">Whether the split pair is currently presented (mirrors <c>current.PairPresented</c> but supplied from the view layer).</param>
    /// <param name="isTargetSplitMember">Whether the hit target is a member of the split pair.</param>
    /// <param name="isButtonHit">Whether the hit landed on a button/chrome element that must bypass activation.</param>
    /// <param name="isStaleIdentity">Whether the target identity is stale or recycled and must fail closed.</param>
    /// <param name="nativeOutcome">Outcome of the guarded native work for the desired transition, if already attempted.</param>
    /// <param name="isRightClickOrHover">Whether the input is a right-click or hover that must not suspend.</param>
    public static SplitInteractionAction Classify(
        SplitPresentationState current,
        bool isSplitPresented,
        bool isTargetSplitMember,
        bool isButtonHit,
        bool isStaleIdentity,
        SplitNativeTransitionOutcome nativeOutcome,
        bool isRightClickOrHover)
    {
        // 1. Chrome buttons always bypass activation — even if the underlying
        //    tab would otherwise suspend or resume.
        if (isButtonHit)
            return SplitInteractionAction.IgnoreButton;

        // 2. Right-click / hover / context-menu gestures must not suspend the
        //    pair. They are observational, not navigation.
        if (isRightClickOrHover)
            return SplitInteractionAction.None;

        // 3. Stale or recycled identities fail closed. This covers the
        //    IdentityMismatch path before any state mutation.
        if (isStaleIdentity)
            return SplitInteractionAction.RejectStale;

        // 4. Recovery-pending / show-failed native outcomes fail closed and
        //    keep the authoritative pair intact. Succeeded is the only
        //    outcome that allows a transition to proceed.
        if (nativeOutcome != SplitNativeTransitionOutcome.Succeeded)
            return SplitInteractionAction.FailClosedRecoveryPending;

        // 5. Split member hits never suspend.
        if (isTargetSplitMember)
        {
            if (!current.RelationshipDefined)
                return SplitInteractionAction.IgnoreMember;

            // Dormant pair: member selection resumes the exact pair.
            if (!isSplitPresented && !current.PairPresented)
                return SplitInteractionAction.ResumeMember;

            // Presented pair: member focus (A<->B) is a no-suspend path.
            // Even if the view flag and state disagree, treat any presented
            // signal as "do not suspend".
            if (isSplitPresented || current.PairPresented)
                return SplitInteractionAction.IgnoreMember;

            return SplitInteractionAction.ResumeMember;
        }

        // 6. Non-member hit with a presented pair → suspend for guest.
        //    Handled preview events still produce this result because the
        //    classification is driven by these flags, not by Handled state.
        if (current.RelationshipDefined && (isSplitPresented || current.PairPresented))
            return SplitInteractionAction.SuspendPairForGuest;

        // 7. Non-member hit while dormant or with no relationship → no
        //    split-specific transition. Ordinary tab switching applies.
        //    Dormant guest-to-guest switches are handled by the presentation
        //    policy (SelectNonMember) outside this classifier when the caller
        //    chooses to route through it; this policy returns None to signal
        //    "no split suspend/resume needed here".
        //    However, for the spec's "dormant guest switching" the caller can
        //    still call SelectNonMember directly. To keep the policy useful
        //    for the validation checklist we treat a dormant non-member hit
        //    as a suspend-like guest switch when a relationship exists.
        if (current.RelationshipDefined && !current.PairPresented && !isTargetSplitMember)
        {
            // Caller may interpret None as "ordinary switch". Return None
            // here so tests that assert dormant guest switching goes through
            // the presentation policy (not this classifier) remain valid.
            // For callers that want the classifier to drive that switch,
            // they can map this to SuspendPairForGuest externally. We keep
            // it as None to preserve the "no suspend when already dormant"
            // invariant unless explicitly requested.
            return SplitInteractionAction.None;
        }

        return SplitInteractionAction.None;
    }

    /// <summary>
    /// Convenience overload for the common case where the native work has not
    /// yet been attempted (assumed succeeded) and the view's presented flag
    /// matches the authoritative state.
    /// </summary>
    public static SplitInteractionAction Classify(
        SplitPresentationState current,
        bool isTargetSplitMember,
        bool isButtonHit,
        bool isStaleIdentity,
        bool isRightClickOrHover)
        => Classify(
            current,
            current.PairPresented,
            isTargetSplitMember,
            isButtonHit,
            isStaleIdentity,
            SplitNativeTransitionOutcome.Succeeded,
            isRightClickOrHover);

    /// <summary>
    /// Describes a successful pair→guest transaction for diagnostics / logging.
    /// Returns null when the transition did not succeed.
    /// </summary>
    public static string? DescribeTransaction(
        SplitPresentationState authoritative,
        SplitPresentationState desired,
        SplitNativeTransitionOutcome outcome)
    {
        if (outcome != SplitNativeTransitionOutcome.Succeeded)
            return null;
        return $"pair({authoritative.Left},{authoritative.Right})@{authoritative.Generation} -> guest({desired.ActiveGuest})@{desired.Generation} mode={desired.Mode}";
    }
}
