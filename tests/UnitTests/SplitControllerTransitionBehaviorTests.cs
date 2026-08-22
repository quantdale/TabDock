using System;
using System.Collections.Generic;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Behavioral coverage for the REAL <see cref="SplitPresentationController"/>
/// transition semantics that other controller tests do not assert explicitly:
/// exactly-once native hide counts, focus-without-teardown, the settle
/// generation lifecycle, and fail-closed atomicity after the FIRST failed
/// hide (no second native attempt). Arranged exclusively through public
/// transitions — no state seeding.
///
/// (Wave-1 cleanup: this replaces the former PresentationOperationBudgetTests,
/// whose counting infrastructure was a test double compiled into production.
/// The behavioral assertions were preserved; the budget sink was deleted.)
/// </summary>
public sealed class SplitControllerTransitionBehaviorTests
{
    /// <summary>Counting fake: records every native attempt the controller makes.</summary>
    private sealed class CountingOps : IPresentationOperations
    {
        private readonly Queue<WindowHideOutcome> _hideOutcomes = new();
        public List<CapturedWindow> HideAttempts { get; } = new();
        public List<CapturedWindow> HiddenWindows { get; } = new();

        public void QueueHideOutcome(WindowHideOutcome outcome) => _hideOutcomes.Enqueue(outcome);

        public WindowHideOutcome Hide(CapturedWindow window)
        {
            HideAttempts.Add(window);
            WindowHideOutcome outcome = _hideOutcomes.Count > 0 ? _hideOutcomes.Dequeue() : WindowHideOutcome.Hidden;
            if (outcome == WindowHideOutcome.Hidden)
                HiddenWindows.Add(window);
            return outcome;
        }

        public void PositionAndShow(CapturedWindow window, IntPtr containerHwnd, NativeMethods.RECT screenRect) { }
        public void PositionGuestsDeferred(CapturedWindow top, NativeMethods.RECT topRect, CapturedWindow bottom, NativeMethods.RECT bottomRect, IntPtr containerHwnd) { }
        public void SetForeground(CapturedWindow window) { }
        public void PairZOrderBehind(IntPtr containerHwnd, CapturedWindow guest) { }
        public bool IsCurrentCapturedWindow(CapturedWindow window) => true;
    }

    private static CapturedWindow W(long hwnd) => new()
    {
        Hwnd = new IntPtr(hwnd),
        ProcessId = 10,
        WindowThreadId = 20,
        WindowIdentityToken = 1000 + hwnd,
        ExePath = "C:\\app.exe",
        OriginalClassName = "Pig",
    };

    // A: FocusMember switches foreground within the presented pair without any
    //    native hide and without tearing the pair down.
    [Fact]
    public void FocusMember_SwitchesForeground_NoHides_PairStaysPresented()
    {
        var ops = new CountingOps();
        var ctrl = new SplitPresentationController(ops);
        CapturedWindow a = W(0x1001), b = W(0x1002);
        Assert.True(ctrl.DefinePair(a, b, a).Committed);

        ctrl.FocusMember(b);

        Assert.True(ctrl.IsPresented);
        Assert.Same(b, ctrl.Foreground);
        Assert.Empty(ops.HideAttempts); // member focus is a no-hide path
    }

    [Fact]
    public void FocusMember_NonMember_IsIgnored()
    {
        var ops = new CountingOps();
        var ctrl = new SplitPresentationController(ops);
        CapturedWindow a = W(0x1001), b = W(0x1002), c = W(0x1003);
        Assert.True(ctrl.DefinePair(a, b, a).Committed);

        ctrl.FocusMember(c);

        Assert.True(ctrl.IsPresented);
        Assert.Same(a, ctrl.Foreground); // unchanged
    }

    // B: Settle generation lifecycle — DefinePair commits with its settle
    //    already armed for the committed generation; only that generation
    //    counts as current; disarm clears pending.
    [Fact]
    public void SettleGeneration_ArmIsCurrentDisarm_Lifecycle()
    {
        var ctrl = new SplitPresentationController();
        CapturedWindow a = W(0x1001), b = W(0x1002);
        Assert.True(ctrl.DefinePair(a, b, a).Committed);

        long gen = ctrl.Generation;
        Assert.True(ctrl.IsPresented);
        Assert.True(ctrl.SettlePending);
        Assert.Equal(gen, ctrl.SettleGeneration);
        Assert.True(ctrl.IsCurrentSettle(gen));
        Assert.False(ctrl.IsCurrentSettle(gen + 1));

        ctrl.DisarmSettle();
        Assert.False(ctrl.SettlePending);
    }

    // C: SuspendForGuest hides each departing member EXACTLY once and never
    //    touches the incoming guest.
    [Fact]
    public void SuspendForGuest_HidesEachMemberExactlyOnce_NeverTouchesIncomingGuest()
    {
        var ops = new CountingOps();
        var ctrl = new SplitPresentationController(ops);
        CapturedWindow a = W(0x1001), b = W(0x1002), c = W(0x1003);
        Assert.True(ctrl.DefinePair(a, b, a).Committed);

        Assert.True(ctrl.SuspendForGuest(c));

        Assert.Equal(2, ops.HideAttempts.Count);
        Assert.Contains(ops.HideAttempts, w => ReferenceEquals(w, a));
        Assert.Contains(ops.HideAttempts, w => ReferenceEquals(w, b));
        Assert.DoesNotContain(ops.HideAttempts, w => ReferenceEquals(w, c));
        Assert.All(ops.HiddenWindows, w => Assert.True(ReferenceEquals(w, a) || ReferenceEquals(w, b)));
    }

    // D: ResumeMember hides the dormant single guest exactly once.
    [Fact]
    public void ResumeMember_HidesDormantSingleGuestExactlyOnce()
    {
        var ops = new CountingOps();
        var ctrl = new SplitPresentationController(ops);
        CapturedWindow a = W(0x1001), b = W(0x1002), c = W(0x1003);
        Assert.True(ctrl.DefinePair(a, b, a).Committed);
        Assert.True(ctrl.SuspendForGuest(c));
        ops.HideAttempts.Clear();

        Assert.True(ctrl.ResumeMember(a, c));

        Assert.Same(c, Assert.Single(ops.HideAttempts));
    }

    // E: A RecoveryPending on the FIRST member hide fails closed atomically:
    //    the pair stays presented and NO second native hide is attempted.
    [Fact]
    public void SuspendForGuest_FirstHideRecoveryPending_FailsClosedWithSingleNativeAttempt()
    {
        var ops = new CountingOps();
        ops.QueueHideOutcome(WindowHideOutcome.RecoveryPending); // first attempt pends; queue then empty
        var ctrl = new SplitPresentationController(ops);
        CapturedWindow a = W(0x1001), b = W(0x1002), c = W(0x1003);
        Assert.True(ctrl.DefinePair(a, b, a).Committed);

        bool committed = ctrl.SuspendForGuest(c);

        Assert.False(committed);                       // recovery-pending keeps pair presented
        Assert.True(ctrl.IsPresented);
        Assert.Single(ops.HideAttempts);               // exactly one native attempt, no storm
    }

    /// <summary>
    /// SG-2 fixture: hide outcomes plus a controllable liveness oracle wired
    /// in as the controller's <c>isCurrent</c> seam (production wires the
    /// shepherd identity gate there).
    /// </summary>
    private sealed class LivenessOps : IPresentationOperations
    {
        private readonly Queue<WindowHideOutcome> _hideOutcomes = new();
        public HashSet<CapturedWindow> Dead { get; } = new();
        public List<CapturedWindow> HideAttempts { get; } = new();

        public void QueueHideOutcome(WindowHideOutcome outcome) => _hideOutcomes.Enqueue(outcome);

        public WindowHideOutcome Hide(CapturedWindow window)
        {
            HideAttempts.Add(window);
            return _hideOutcomes.Count > 0 ? _hideOutcomes.Dequeue() : WindowHideOutcome.Hidden;
        }

        public void PositionAndShow(CapturedWindow window, IntPtr containerHwnd, NativeMethods.RECT screenRect) { }
        public void PositionGuestsDeferred(CapturedWindow top, NativeMethods.RECT topRect, CapturedWindow bottom, NativeMethods.RECT bottomRect, IntPtr containerHwnd) { }
        public void SetForeground(CapturedWindow window) { }
        public void PairZOrderBehind(IntPtr containerHwnd, CapturedWindow guest) { }
        public bool IsCurrentCapturedWindow(CapturedWindow window) => !Dead.Contains(window);
    }

    // F (SG-2): resuming with a DEAD pair member must fail closed without
    // committing a presented pair that references an unreachable HWND — the
    // all-or-nothing deferred positioning would otherwise leave the surviving
    // member hidden over blank content.
    [Fact]
    public void ResumeMember_DeadPairMember_FailsClosedWithoutCommit()
    {
        var ops = new LivenessOps();
        var ctrl = new SplitPresentationController(ops, w => !ops.Dead.Contains(w));
        CapturedWindow a = W(0x1001), b = W(0x1002), c = W(0x1003);
        Assert.True(ctrl.DefinePair(a, b, a).Committed);
        Assert.True(ctrl.SuspendForGuest(c));
        ops.HideAttempts.Clear();

        ops.Dead.Add(a); // partner died; EVENT_OBJECT_DESTROY not yet dispatched
        bool committed = ctrl.ResumeMember(b);

        Assert.False(committed);
        Assert.False(ctrl.IsPresented);                 // dormant state retained
        Assert.Same(c, ctrl.Foreground);                // single-guest authority untouched
        Assert.Empty(ops.HideAttempts);                 // C was never journal-safely hidden for a doomed resume
    }

    // G (SG-2): the resuming member itself being dead also fails closed.
    [Fact]
    public void ResumeMember_DeadResumingMember_FailsClosedWithoutCommit()
    {
        var ops = new LivenessOps();
        var ctrl = new SplitPresentationController(ops, w => !ops.Dead.Contains(w));
        CapturedWindow a = W(0x1001), b = W(0x1002), c = W(0x1003);
        Assert.True(ctrl.DefinePair(a, b, a).Committed);
        Assert.True(ctrl.SuspendForGuest(c));
        ops.HideAttempts.Clear();

        ops.Dead.Add(b);
        bool committed = ctrl.ResumeMember(b);

        Assert.False(committed);
        Assert.False(ctrl.IsPresented);
        Assert.Same(c, ctrl.Foreground);
        Assert.Empty(ops.HideAttempts);
    }

    // H (SG-2): a member hide answering TargetGoneOrRecycled during suspension
    // is a dead member — treating it as success would commit a dormant pair
    // referencing an unreachable HWND. It must fail closed like RecoveryPending.
    [Fact]
    public void SuspendForGuest_MemberHideTargetGone_FailsClosedLikePending()
    {
        var ops = new LivenessOps();
        ops.QueueHideOutcome(WindowHideOutcome.TargetGoneOrRecycled); // first member already gone
        var ctrl = new SplitPresentationController(ops, w => !ops.Dead.Contains(w));
        CapturedWindow a = W(0x1001), b = W(0x1002), c = W(0x1003);
        Assert.True(ctrl.DefinePair(a, b, a).Committed);

        bool committed = ctrl.SuspendForGuest(c);

        Assert.False(committed);
        Assert.True(ctrl.IsPresented);                  // authoritative pair retained
        Assert.Single(ops.HideAttempts);                // no second native attempt after stale evidence
    }

    // I (SG-2): same fail-closed atomicity when the SECOND member's hide
    // reports the dead window — nothing may commit with a dead member inside.
    [Fact]
    public void SuspendForGuest_SecondHideTargetGone_FailsClosedLikePending()
    {
        var ops = new LivenessOps();
        ops.QueueHideOutcome(WindowHideOutcome.Hidden);
        ops.QueueHideOutcome(WindowHideOutcome.TargetGoneOrRecycled);
        var ctrl = new SplitPresentationController(ops, w => !ops.Dead.Contains(w));
        CapturedWindow a = W(0x1001), b = W(0x1002), c = W(0x1003);
        Assert.True(ctrl.DefinePair(a, b, a).Committed);

        bool committed = ctrl.SuspendForGuest(c);

        Assert.False(committed);
        Assert.True(ctrl.IsPresented);
        Assert.Equal(2, ops.HideAttempts.Count);
    }
}
