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
/// Every transition follows one pattern: the pure policy computes the desired
/// logical state from the authoritative state, guarded native operations
/// execute the required diff, and the desired state is committed only when ALL
/// of that native work succeeded. On a pending/failed native operation the
/// authoritative state is retained untouched (the container re-presents it),
/// so logical state and visible state can never disagree about whether the
/// pair is presented. The controller never restates transition semantics the
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
    /// Defines (or reconfigures) the pair. Departing members are hidden through
    /// the guarded presentation seam while the old relationship is still
    /// authoritative; a RecoveryPending hide commits nothing so the caller can
    /// re-present the retained state instead of stranding a half-hidden pair.
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
        _left = left;
        _right = right;
        _presented = true;
        _foreground = focusedMember != null && IsMember(focusedMember) ? focusedMember : left;
        _generation = desired.Generation;
        DisarmSettle();
        _settlePending = true;
        _settleGeneration = _generation;
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
            if (outcome == WindowHideOutcome.RecoveryPending)
                return false; // Authoritative pair retained; caller re-presents it.
        }

        if (_isCurrent != null && !_isCurrent(guest))
            return false;

        _presented = false;
        _foreground = guest;
        _generation = desired.Generation;
        DisarmSettle();
        return true;
    }

    public bool ResumeMember(CapturedWindow member, CapturedWindow? currentSingleGuest = null)
    {
        if (!IsRelationshipDefined || !IsMember(member))
            return false;

        // Pure authority: single -> pair with the member focused.
        SplitPresentationState desired = SplitPresentationPolicy.SelectMember(ToState(), Id(member));

        if (currentSingleGuest != null && !IsMember(currentSingleGuest) && _ops != null)
        {
            WindowHideOutcome o = _ops.Hide(currentSingleGuest);
            if (o == WindowHideOutcome.RecoveryPending)
                return false; // Single-guest presentation retained.
        }

        _presented = true;
        _foreground = member;
        _generation = desired.Generation;
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
        CapturedWindow? survivor;
        if (_foreground == null || ReferenceEquals(_foreground, removed))
        {
            survivor = ReferenceEquals(removed, _left) ? _right : _left;
        }
        else
        {
            survivor = _foreground;
        }

        _left = null;
        _right = null;
        _presented = false;
        _foreground = desired.ActiveGuest != null ? survivor : null;
        _generation++;
        DisarmSettle();
        return survivor;
    }

    /// <summary>
    /// Commits an explicit split exit after the caller has executed the
    /// journal-safe hides for every departing member. Applies
    /// <see cref="SplitPresentationPolicy.ExplicitExit"/>: only the
    /// relationship is removed and <paramref name="survivor"/> becomes the
    /// ordinary active guest.
    /// </summary>
    public void CommitExplicitExit(CapturedWindow? survivor)
    {
        if (!IsRelationshipDefined)
            return;
        SplitPresentationPolicy.ExplicitExit(ToState());
        _left = null;
        _right = null;
        _presented = false;
        _foreground = survivor;
        _generation++;
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

    public void FocusMember(CapturedWindow member)
    {
        if (!IsMember(member)) return;
        _foreground = member;
    }

    private static string Id(CapturedWindow window) => window.Hwnd.ToString("X");
}
