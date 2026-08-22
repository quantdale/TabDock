using System;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>Explicit outcome of a guarded split transition.</summary>
public readonly record struct SplitTransitionResult(
    bool Committed,
    SplitNativeTransitionOutcome Native)
{
    public static SplitTransitionResult Succeeded() => new(true, SplitNativeTransitionOutcome.Succeeded);
    public static SplitTransitionResult Pending() => new(false, SplitNativeTransitionOutcome.RecoveryPending);
    public static SplitTransitionResult Rejected() => new(false, SplitNativeTransitionOutcome.IdentityMismatch);
}

/// <summary>
/// Owns split pair identity, presented vs dormant, foreground member, generation
/// and settle generation. Wraps <see cref="SplitPresentationPolicy"/> +
/// <see cref="SplitInteractionPolicy"/> so <see cref="Views.ContainerWindow"/>
/// only owns WPF wiring. Keeps cross-handle identity by <see cref="CapturedWindow"/>
/// reference (never positional index) and preserves LEFT/RIGHT orientation.
///
/// Every transition follows ONE pattern (Wave 3 canonical commit): the pure
/// policy computes the desired logical state from the authoritative state,
/// guarded native operations execute the required diff, and the desired state
/// is committed ONLY when ALL of that native work succeeded — through the
/// single <see cref="CommitDesired"/> helper. There are no other writers of
/// _left/_right/_presented/_foreground/_generation: no transition re-derives a
/// policy decision by hand and no code path increments the generation outside
/// the committed policy result. On a pending/failed native operation nothing
/// commits, so the authoritative state remains exactly S0 and the caller can
/// re-present it. The controller never restates transition semantics the
/// policy already owns.
/// </summary>
public sealed class SplitPresentationController
{
    private CapturedWindow? _left;
    private CapturedWindow? _right;
    private bool _presented;
    private CapturedWindow? _foreground;
    private long _generation;
    private bool _settlePending;
    private long _settleGeneration;
    private readonly IPresentationOperations? _ops;
    private readonly Func<CapturedWindow, bool>? _isCurrent;

    public SplitPresentationController(
        IPresentationOperations? ops = null,
        Func<CapturedWindow, bool>? isCurrent = null)
    {
        _ops = ops;
        _isCurrent = isCurrent;
    }

    public CapturedWindow? Left => _left;
    public CapturedWindow? Right => _right;
    public bool IsRelationshipDefined => _left != null && _right != null;
    public bool IsPresented => IsRelationshipDefined && _presented;
    public CapturedWindow? Foreground => _foreground;
    public long Generation => _generation;
    public bool SettlePending => _settlePending;
    public long SettleGeneration => _settleGeneration;

    public bool IsMember(CapturedWindow? window)
        => window != null && (ReferenceEquals(window, _left) || ReferenceEquals(window, _right));

    public bool IsInSplit(CapturedWindow window) => IsPresented && IsMember(window);

    public SplitPresentationState ToState()
        => new(
            _left?.Hwnd.ToString("X"),
            _right?.Hwnd.ToString("X"),
            IsPresented,
            _foreground?.Hwnd.ToString("X") ?? _left?.Hwnd.ToString("X"),
            _generation);

    /// <summary>
    /// THE one canonical commit path. Applies a policy-computed desired state
    /// to the runtime fields. The policy speaks in stable string identities;
    /// this adapter resolves them back to live references using ONLY the
    /// candidate arguments this very transition already holds — never a global
    /// HWND lookup that would undermine captured-object identity.
    /// Callers must have completed ALL guarded native work successfully before
    /// invoking this; on RecoveryPending/identity rejection they return without
    /// committing, so the authoritative state stays at S0.
    /// </summary>
    private void CommitDesired(
        SplitPresentationState desired,
        CapturedWindow? resolvedLeft,
        CapturedWindow? resolvedRight,
        CapturedWindow? resolvedForeground)
    {
        _left = resolvedLeft;
        _right = resolvedRight;
        _presented = desired.PairPresented;
        _foreground = resolvedForeground;
        _generation = desired.Generation;
    }

    /// <summary>
    /// Resolves a policy string identity back to one of the supplied live
    /// references from this transition's own arguments.
    /// </summary>
    private static CapturedWindow? Resolve(string? identity, params CapturedWindow?[] candidates)
    {
        if (identity == null)
            return null;
        foreach (CapturedWindow? candidate in candidates)
        {
            if (candidate != null && string.Equals(Id(candidate), identity, StringComparison.Ordinal))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Defines (or reconfigures) the pair. Departing members are hidden through
    /// the guarded presentation seam while the old relationship is still
    /// authoritative; a RecoveryPending hide commits nothing so the caller can
    /// re-present the retained state instead of stranding a half-hidden pair.
    /// The committed state is exactly the policy result
    /// (<see cref="SplitPresentationPolicy.DefinePair"/> /
    /// <see cref="SplitPresentationPolicy.Reconfigure"/>) including which
    /// member holds focus.
    /// </summary>
    public SplitTransitionResult DefinePair(CapturedWindow left, CapturedWindow right, CapturedWindow? focusedMember = null)
    {
        if (left == null || right == null || ReferenceEquals(left, right))
            return SplitTransitionResult.Rejected();

        // Pure authority first: compute the desired logical state.
        SplitPresentationState desired = IsRelationshipDefined
            ? SplitPresentationPolicy.Reconfigure(ToState(), Id(left), Id(right))
            : SplitPresentationPolicy.DefinePair(Id(left), Id(right), Id(focusedMember ?? left), _generation);

        // Guarded native work: hide members departing from the current pair.
        if (IsRelationshipDefined)
        {
            foreach (CapturedWindow m in new[] { _left!, _right! })
            {
                if (ReferenceEquals(m, left) || ReferenceEquals(m, right))
                    continue;
                WindowHideOutcome o = _ops != null ? _ops.Hide(m) : WindowHideOutcome.Hidden;
                if (o == WindowHideOutcome.RecoveryPending)
                    return SplitTransitionResult.Pending();
            }
        }

        // All native work succeeded: commit the policy's desired state.
        CommitDesired(desired, left, right, Resolve(desired.ActiveGuest, focusedMember, left, right));
        ArmSettle();
        return SplitTransitionResult.Succeeded();
    }

    public bool SuspendForGuest(CapturedWindow guest)
    {
        if (!IsPresented || IsMember(guest))
            return false;
        if (_isCurrent != null && !_isCurrent(guest))
            return false;

        // Pure authority: pair -> single with the non-member active.
        SplitPresentationState desired = SplitPresentationPolicy.SelectNonMember(ToState(), Id(guest));

        CapturedWindow left = _left!;
        CapturedWindow right = _right!;
        foreach (CapturedWindow member in new[] { left, right })
        {
            WindowHideOutcome outcome = _ops != null ? _ops.Hide(member) : WindowHideOutcome.Hidden;
            if (outcome == WindowHideOutcome.RecoveryPending || outcome == WindowHideOutcome.TargetGoneOrRecycled)
                return false; // Authoritative pair retained; caller re-presents it.
                               // TargetGoneOrRecycled is a member that died before its
                               // destroy event dispatched: committing a dormant pair
                               // referencing it would strand the survivor behind the
                               // all-or-nothing positioning identity gate on resume.
        }

        if (_isCurrent != null && !_isCurrent(guest))
            return false;

        CommitDesired(desired, left, right, Resolve(desired.ActiveGuest, guest));
        DisarmSettle();
        return true;
    }

    public bool ResumeMember(CapturedWindow member, CapturedWindow? currentSingleGuest = null)
    {
        if (!IsRelationshipDefined || !IsMember(member))
            return false;

        // Liveness gate: a presented pair must reference only live members. A
        // member that died before its destroy event dispatched would otherwise
        // be committed into PairPresented and then defeat the all-or-nothing
        // deferred positioning (blank panes, foreground handed to a hidden
        // window) until membership heals. Fail closed instead; the caller
        // retains the dormant single-guest presentation.
        if (_isCurrent != null
            && (!_isCurrent(member)
                || (_left != null && !_isCurrent(_left))
                || (_right != null && !_isCurrent(_right))))
            return false;

        // Pure authority: single -> pair with the member focused.
        SplitPresentationState desired = SplitPresentationPolicy.SelectMember(ToState(), Id(member));

        if (currentSingleGuest != null && !IsMember(currentSingleGuest) && _ops != null)
        {
            WindowHideOutcome o = _ops.Hide(currentSingleGuest);
            if (o == WindowHideOutcome.RecoveryPending)
                return false; // Single-guest presentation retained.
        }

        CommitDesired(desired, _left, _right, Resolve(desired.ActiveGuest, member));
        DisarmSettle();
        return true;
    }

    /// <summary>
    /// Structural invalidation (a member's window is gone or was released by
    /// another path). Applies <see cref="SplitPresentationPolicy.RemoveMember"/>
    /// exactly: when the removed member was the active guest the surviving
    /// member is promoted; otherwise the current active guest (which may be a
    /// dormant non-member) is preserved. No native work happens here — the
    /// departing member was already released/hidden by the removal path.
    /// </summary>
    public CapturedWindow? HandleMemberRemoved(CapturedWindow removed)
    {
        if (!IsRelationshipDefined || !IsMember(removed))
            return null;

        SplitPresentationState desired = SplitPresentationPolicy.RemoveMember(ToState(), Id(removed));
        CapturedWindow? other = ReferenceEquals(removed, _left) ? _right : _left;
        CapturedWindow? survivor = Resolve(desired.ActiveGuest, _foreground, other);
        CommitDesired(desired, null, null, survivor);
        DisarmSettle();
        return survivor;
    }

    /// <summary>
    /// Commits an explicit split exit after the caller has executed the
    /// journal-safe hides for every departing member. Applies
    /// <see cref="SplitPresentationPolicy.ExplicitExit"/>: only the
    /// relationship is removed and the preferred survivor (an explicit
    /// "keep THIS member active" request) or the current active guest becomes
    /// the ordinary active guest.
    /// </summary>
    public void CommitExplicitExit(CapturedWindow? preferredSurvivor)
    {
        if (!IsRelationshipDefined)
            return;
        SplitPresentationState desired = SplitPresentationPolicy.ExplicitExit(
            ToState(),
            preferredSurvivor == null ? null : Id(preferredSurvivor));
        CapturedWindow? resolved = Resolve(desired.ActiveGuest, preferredSurvivor, _foreground);
        CommitDesired(desired, null, null, resolved);
        DisarmSettle();
    }

    public bool IsCurrentSettle(long queuedGeneration)
        => SplitPresentationPolicy.IsCurrentSettle(ToState(), queuedGeneration);

    public void ArmSettle()
    {
        if (!IsPresented) return;
        _settlePending = true;
        _settleGeneration = _generation;
    }

    public void DisarmSettle()
    {
        _settlePending = false;
    }

    /// <summary>
    /// Switches the focused member of the defined pair (z-top + logical active).
    /// Commits the pure <see cref="SplitPresentationPolicy.FocusMember"/> result:
    /// no generation bump, no mode change, non-members ignored.
    /// </summary>
    public void FocusMember(CapturedWindow member)
    {
        if (!IsMember(member)) return;
        CommitDesired(
            SplitPresentationPolicy.FocusMember(ToState(), Id(member)),
            _left,
            _right,
            member);
    }

    /// <summary>
    /// Commits <paramref name="guest"/> as the presented single active guest
    /// (standalone or dormant mode) — the ordinary tab-switch transition, and
    /// the ONE replacement for what used to be a bare view-side write of a
    /// parallel active-guest field. The caller performs its guarded native
    /// work around this commit exactly as before (hide-old before, show-new/
    /// layout after); a null guest models teardown/empty-group. Presented
    /// pairs never pass through here — suspension is
    /// <see cref="SuspendForGuest"/> — so a presented state is left untouched
    /// (fail-closed).
    /// </summary>
    public void SelectGuest(CapturedWindow? guest)
    {
        if (IsPresented)
            return;
        CommitDesired(
            SplitPresentationPolicy.SelectGuest(ToState(), guest == null ? null : Id(guest)),
            _left,
            _right,
            guest);
        DisarmSettle();
    }

    /// <summary>
    /// Clears ALL presentation authority: relationship, presented flag, active
    /// guest, settle. Used by container teardown / session ending, replacing
    /// the former combination of a view-field null-out plus a conditional
    /// HandleMemberRemoved (which could leave a dormant non-member as a ghost
    /// Foreground on a dead container). Bumps the generation so any callback
    /// that captured pre-teardown state is invalidated by construction.
    /// </summary>
    public void Clear()
    {
        // Deliberately NOT routed through SelectGuest: its policy form no-ops
        // on a PRESENTED pair, and teardown must clear even that. NoPair is the
        // policy's canonical empty state; the explicit epoch bump invalidates
        // every callback captured before teardown.
        CommitDesired(
            SplitPresentationPolicy.NoPair(generation: _generation + 1),
            null,
            null,
            null);
        DisarmSettle();
    }

    private static string Id(CapturedWindow window) => window.Hwnd.ToString("X");
}
