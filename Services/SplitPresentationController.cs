using System;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Owns split pair identity, presented vs dormant, foreground member, generation
/// and settle generation. Wraps <see cref="SplitPresentationPolicy"/> +
/// <see cref="SplitInteractionPolicy"/> so <see cref="Views.ContainerWindow"/>
/// only owns WPF wiring. Keeps cross-handle identity by <see cref="CapturedWindow"/>
/// reference (never positional index) and preserves LEFT/RIGHT orientation.
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
    private readonly IPresentationBudgetSink? _budget;
    private readonly Func<CapturedWindow, bool>? _isCurrent;

    public SplitPresentationController(
        IPresentationOperations? ops = null,
        IPresentationBudgetSink? budget = null,
        Func<CapturedWindow, bool>? isCurrent = null)
    {
        _ops = ops;
        _budget = budget;
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

    public void DefinePair(CapturedWindow left, CapturedWindow right, CapturedWindow? focusedMember = null)
    {
        if (left == null || right == null || ReferenceEquals(left, right))
            return;

        // Hide departing members if replacing an existing pair.
        // Hide counting is owned by the presentation ops / shepherd budget sink,
        // not duplicated here.
        if (IsRelationshipDefined)
        {
            foreach (var m in new[] { _left!, _right! })
            {
                if (m != left && m != right && _ops != null)
                {
                    WindowHideOutcome o = _ops.Hide(m);
                    if (o == WindowHideOutcome.RecoveryPending)
                        return;
                }
            }
        }

        _generation++;
        _left = left;
        _right = right;
        _presented = true;
        _foreground = focusedMember != null && IsMember(focusedMember) ? focusedMember : left;
        DisarmSettle();
        _settlePending = true;
        _settleGeneration = _generation;
    }

    public bool SuspendForGuest(CapturedWindow guest)
    {
        if (!IsPresented || IsMember(guest))
            return false;
        if (_isCurrent != null && !_isCurrent(guest))
            return false;

        CapturedWindow left = _left!;
        CapturedWindow right = _right!;

        foreach (CapturedWindow member in new[] { left, right })
        {
            WindowHideOutcome outcome = _ops != null ? _ops.Hide(member) : WindowHideOutcome.Hidden;
            if (outcome == WindowHideOutcome.RecoveryPending)
                return false;
        }

        if (_isCurrent != null && !_isCurrent(guest))
            return false;

        _presented = false;
        _generation++;
        DisarmSettle();
        return true;
    }

    public bool ResumeMember(CapturedWindow member, CapturedWindow? currentSingleGuest = null)
    {
        if (!IsRelationshipDefined || !IsMember(member))
            return false;

        if (currentSingleGuest != null && !IsMember(currentSingleGuest) && _ops != null)
        {
            WindowHideOutcome o = _ops.Hide(currentSingleGuest);
            if (o == WindowHideOutcome.RecoveryPending)
                return false;
        }

        _presented = true;
        _generation++;
        DisarmSettle();
        _foreground = member;
        return true;
    }

    public bool ExplicitExit(CapturedWindow? keepActive = null)
    {
        if (!IsRelationshipDefined)
            return false;

        if (!IsPresented)
        {
            // Dormant: hide any visible former members without promoting.
            if (_ops != null)
            {
                foreach (var m in new[] { _left, _right })
                {
                    if (m == null) continue;
                    WindowHideOutcome o = _ops.Hide(m);
                    if (o == WindowHideOutcome.RecoveryPending)
                        return false;
                }
            }
            _left = null; _right = null; _foreground = null; _presented = false;
            _generation++; DisarmSettle();
            return true;
        }

        CapturedWindow? survivor = keepActive != null && IsMember(keepActive) ? keepActive : _left;
        if (_ops != null)
        {
            foreach (var m in new[] { _left, _right })
            {
                if (m != null && !ReferenceEquals(m, survivor))
                {
                    WindowHideOutcome o = _ops.Hide(m);
                    if (o == WindowHideOutcome.RecoveryPending)
                        return false;
                }
            }
        }
        _left = null; _right = null; _foreground = null; _presented = false;
        _generation++; DisarmSettle();
        return true;
    }

    public CapturedWindow? HandleMemberRemoved(CapturedWindow removed)
    {
        if (!IsRelationshipDefined || !IsMember(removed))
            return null;
        CapturedWindow? survivor = ReferenceEquals(removed, _left) ? _right : _left;
        _left = null; _right = null; _foreground = null; _presented = false;
        _generation++; DisarmSettle();
        return survivor;
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

    public SplitInteractionAction ClassifyInteraction(
        bool isSplitPresented,
        bool isTargetSplitMember,
        bool isButtonHit,
        bool isStaleIdentity,
        SplitNativeTransitionOutcome nativeOutcome,
        bool isRightClickOrHover)
        => SplitInteractionPolicy.Classify(ToState(), isSplitPresented, isTargetSplitMember, isButtonHit, isStaleIdentity, nativeOutcome, isRightClickOrHover);

    public void FocusMember(CapturedWindow member)
    {
        if (!IsMember(member)) return;
        _foreground = member;
    }

    // Test seam: seed state without side effects.
    public void SeedState(CapturedWindow? left, CapturedWindow? right, bool presented, CapturedWindow? foreground, long generation)
    {
        _left = left; _right = right; _presented = presented; _foreground = foreground; _generation = generation;
    }
}
