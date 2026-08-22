using System;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Wave 3A ownership invariant: the REAL <see cref="SplitPresentationController"/>
/// must commit exactly what <see cref="SplitPresentationPolicy"/> computes —
/// <c>controller.ToState()</c> equals the policy result of the same transition
/// applied to the prior authoritative state. Individual field assertions would
/// let the two state machines drift silently; these cross-checks cannot.
///
/// Proven per transition: committed logical state, generation exactness
/// (+1 per commit, +0 on rejection/pending), fail-closed atomicity
/// (RecoveryPending / identity rejection leave S0 untouched), and stale-settle
/// suppression (a queued settle for an older generation can never resurrect a
/// dormant pair).
/// </summary>
public class SplitControllerPolicyAuthorityTests
{
    private sealed class FakePresentationOps : IPresentationOperations
    {
        private readonly Queue<WindowHideOutcome> _hideOutcomes = new();
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
        public bool IsCurrentCapturedWindow(CapturedWindow window) => true;
    }

    private static CapturedWindow W(string name)
    {
        long hwnd = name[^1] - 'A' + 1; // A=>1, B=>2, ...
        return new CapturedWindow
        {
            Hwnd = new IntPtr(hwnd),
            ProcessId = 10,
            WindowThreadId = 20,
            WindowIdentityToken = 1000 + hwnd,
            ExePath = $"{name}.exe",
            OriginalClassName = "Pig",
        };
    }

    private static (SplitPresentationController Controller, FakePresentationOps Ops) Create(
        Func<CapturedWindow, bool>? isCurrent = null)
    {
        FakePresentationOps ops = new();
        var controller = new SplitPresentationController(ops, isCurrent);
        return (controller, ops);
    }

    private static void AssertState(SplitPresentationController controller, SplitPresentationState expected)
    {
        Assert.Equal(expected, controller.ToState());
    }

    // ---- committed transitions match the policy result ----------------------

    [Fact]
    public void DefinePair_Fresh_CommitsExactlyPolicyDefinePairResult()
    {
        var (controller, _) = Create();

        Assert.True(controller.DefinePair(W("A"), W("B"), W("A")).Committed);

        AssertState(controller, SplitPresentationPolicy.DefinePair("1", "2", "1", generation: 0));
        Assert.Equal(1, controller.Generation);
    }

    [Fact]
    public void DefinePair_Reconfiguration_CommitsExactlyReconfigureResult()
    {
        var (controller, _) = Create();
        controller.DefinePair(W("A"), W("B"), W("A"));
        SplitPresentationState before = controller.ToState();

        Assert.True(controller.DefinePair(W("C"), W("D"), W("C")).Committed);

        AssertState(controller, SplitPresentationPolicy.Reconfigure(before, "3", "4"));
        Assert.Equal(2, controller.Generation);
    }

    [Fact]
    public void SuspendForGuest_CommitsExactlySelectNonMemberResult()
    {
        var (controller, _) = Create();
        controller.DefinePair(W("A"), W("B"), W("A"));
        SplitPresentationState before = controller.ToState();

        Assert.True(controller.SuspendForGuest(W("C")));

        AssertState(controller, SplitPresentationPolicy.SelectNonMember(before, "3"));
        Assert.Equal(2, controller.Generation);
    }

    [Fact]
    public void ResumeMember_CommitsExactlySelectMemberResult()
    {
        CapturedWindow a = W("A"), b = W("B"), c = W("C");
        var (controller, _) = Create();
        controller.DefinePair(a, b, a);
        controller.SuspendForGuest(c);
        SplitPresentationState before = controller.ToState();

        Assert.True(controller.ResumeMember(b, c));

        AssertState(controller, SplitPresentationPolicy.SelectMember(before, "2"));
        Assert.Equal(3, controller.Generation);
    }

    [Fact]
    public void CommitExplicitExit_ActiveSurvivor_CommitsExactlyExplicitExitResult()
    {
        var (controller, _) = Create();
        controller.DefinePair(W("A"), W("B"), W("A"));
        SplitPresentationState before = controller.ToState();

        controller.CommitExplicitExit(W("A")); // keepActive resolves to the focused member

        AssertState(controller, SplitPresentationPolicy.ExplicitExit(before));
        Assert.Equal(2, controller.Generation);
    }

    [Fact]
    public void CommitExplicitExit_PreferredPartnerSurvivor_CommitsExactlyPreferredResult()
    {
        var (controller, _) = Create();
        controller.DefinePair(W("A"), W("B"), W("A"));
        SplitPresentationState before = controller.ToState();

        // Exiting while explicitly keeping the NON-focused member active: the
        // policy's preferred-survivor overload is the authority (this used to
        // be decided by hand in the controller with the policy result discarded).
        controller.CommitExplicitExit(W("B"));

        AssertState(controller, SplitPresentationPolicy.ExplicitExit(before, "2"));
        Assert.Equal(2, controller.Generation);
    }

    [Theory]
    [InlineData("A")] // active LEFT member removed -> RIGHT survives
    [InlineData("B")] // inactive RIGHT member removed -> active LEFT stays
    public void HandleMemberRemoved_Presented_CommitsExactlyRemoveMemberResult(string removedName)
    {
        CapturedWindow a = W("A"), b = W("B");
        var (controller, _) = Create();
        controller.DefinePair(a, b, a);
        SplitPresentationState before = controller.ToState();

        controller.HandleMemberRemoved(removedName == "A" ? a : b);

        AssertState(controller, SplitPresentationPolicy.RemoveMember(before, removedName == "A" ? "1" : "2"));
    }

    [Fact]
    public void HandleMemberRemoved_DormantNonMemberActive_CommitsExactlyRemoveMemberResult()
    {
        CapturedWindow a = W("A"), b = W("B"), c = W("C");
        var (controller, _) = Create();
        controller.DefinePair(a, b, a);
        controller.SuspendForGuest(c);
        SplitPresentationState before = controller.ToState();

        controller.HandleMemberRemoved(a);

        // The dormant non-member stays active; the surviving member does NOT
        // get promoted (policy authority, previously re-derived by hand here).
        AssertState(controller, SplitPresentationPolicy.RemoveMember(before, "1"));
        Assert.Equal(3, controller.Generation);
    }

    [Fact]
    public void FocusMember_CommitsExactlyPolicyFocusMember_GenerationUnchanged()
    {
        CapturedWindow a = W("A"), b = W("B");
        var (controller, _) = Create();
        controller.DefinePair(a, b, a);
        SplitPresentationState before = controller.ToState();

        controller.FocusMember(b);

        AssertState(controller, SplitPresentationPolicy.FocusMember(before, "2"));
        Assert.Equal(1, controller.Generation); // focus switch is not a world transition
    }

    // ---- fail-closed atomicity: pending/rejected keeps exactly S0 ------------

    [Fact]
    public void SuspendForGuest_RecoveryPending_StateStaysAtPriorPolicyState()
    {
        var (controller, ops) = Create();
        controller.DefinePair(W("A"), W("B"), W("A"));
        SplitPresentationState before = controller.ToState();
        ops.QueueHideOutcome(WindowHideOutcome.Hidden);
        ops.QueueHideOutcome(WindowHideOutcome.RecoveryPending);

        Assert.False(controller.SuspendForGuest(W("C")));

        AssertState(controller, before);
    }

    [Fact]
    public void ResumeMember_RecoveryPendingOnSingleGuestHide_StateStaysAtPriorPolicyState()
    {
        var (controller, ops) = Create();
        controller.DefinePair(W("A"), W("B"), W("A"));
        controller.SuspendForGuest(W("C"));
        SplitPresentationState before = controller.ToState();
        ops.QueueHideOutcome(WindowHideOutcome.RecoveryPending);

        Assert.False(controller.ResumeMember(W("A"), W("C")));

        AssertState(controller, before);
    }

    [Fact]
    public void DefinePair_RecoveryPendingOnDepartingHide_StateStaysAtPriorPolicyState()
    {
        var (controller, ops) = Create();
        controller.DefinePair(W("A"), W("B"), W("A"));
        SplitPresentationState before = controller.ToState();
        ops.QueueHideOutcome(WindowHideOutcome.RecoveryPending);

        Assert.False(controller.DefinePair(W("C"), W("D"), W("C")).Committed);

        AssertState(controller, before);
    }

    [Fact]
    public void SuspendForGuest_IdentityRejected_NoCommitNoNativeWorkNoGenerationBump()
    {
        bool guestIsCurrent = true;
        var (controller, ops) = Create(isCurrent: _ => guestIsCurrent);
        controller.DefinePair(W("A"), W("B"), W("A"));
        SplitPresentationState before = controller.ToState();
        guestIsCurrent = false;

        Assert.False(controller.SuspendForGuest(W("C")));

        AssertState(controller, before);
        Assert.Empty(ops.HideAttempts);
    }

    // ---- no-op transitions never mutate or bump ------------------------------

    [Fact]
    public void InvalidTransitions_AreRejectedWithoutStateOrGenerationChange()
    {
        CapturedWindow a = W("A"), b = W("B"), c = W("C");
        var (controller, _) = Create();
        controller.DefinePair(a, b, a);
        SplitPresentationState before = controller.ToState();

        Assert.False(controller.SuspendForGuest(a));       // member cannot suspend the pair
        Assert.False(controller.ResumeMember(c));          // not a member
        Assert.Null(controller.HandleMemberRemoved(c));    // not a member
        Assert.Equal(1, controller.Generation);

        var emptyController = new SplitPresentationController();
        emptyController.CommitExplicitExit(W("A"));             // no relationship defined
        Assert.Equal(0, emptyController.Generation);
        emptyController.FocusMember(W("A"));                    // nothing to focus
        Assert.Equal(0, emptyController.Generation);
        AssertState(controller, before);
    }

    // ---- generation exactness across a whole lifecycle -----------------------

    [Fact]
    public void Generation_IncrementsExactlyOncePerCommittedTransition_NeverOtherwise()
    {
        CapturedWindow a = W("A"), b = W("B"), c = W("C"), d = W("D"), e = W("E");
        var (controller, _) = Create();

        Assert.Equal(0, controller.Generation);
        Assert.True(controller.DefinePair(a, b, a).Committed);
        Assert.Equal(1, controller.Generation);
        Assert.True(controller.SuspendForGuest(c));
        Assert.Equal(2, controller.Generation);
        Assert.True(controller.ResumeMember(b, c));
        Assert.Equal(3, controller.Generation);
        controller.FocusMember(a);                 // focus: NO bump
        Assert.Equal(3, controller.Generation);
        controller.CommitExplicitExit(a);
        Assert.Equal(4, controller.Generation);
        Assert.True(controller.DefinePair(d, e, d).Committed);
        Assert.Equal(5, controller.Generation);

        // Cross-check every stage against the pure policy chain as a whole.
        AssertState(controller,
            SplitPresentationPolicy.DefinePair("4", "5", "4",
                SplitPresentationPolicy.ExplicitExit(
                    SplitPresentationPolicy.SelectMember(
                        SplitPresentationPolicy.SelectNonMember(
                            SplitPresentationPolicy.DefinePair("1", "2", "1", 0), "3"), "2"),
                    "1").Generation));
    }

    // ---- stale-settle suppression -------------------------------------------

    [Fact]
    public void StaleSettle_CanNeverResurrectDormantPair()
    {
        CapturedWindow a = W("A"), b = W("B"), c = W("C");
        var (controller, ops) = Create();
        controller.DefinePair(a, b, a);
        long armedGeneration = controller.SettleGeneration;

        // Pair goes dormant under a newer generation: any callback queued for
        // the OLD generation — or even the NEW one — must be rejected while
        // dormant, so a stale render pass cannot re-present the pair.
        Assert.True(controller.IsCurrentSettle(armedGeneration));
        Assert.True(controller.SuspendForGuest(c));

        Assert.False(controller.IsCurrentSettle(armedGeneration)); // old generation stale...
        Assert.False(controller.IsCurrentSettle(controller.SettleGeneration));
        Assert.False(controller.SettlePending);
        Assert.False(controller.IsPresented);                      // ...and dormancy holds

        // Re-presenting bumps the presentation epoch; the view scheduler
        // re-arms the settle for EXACTLY the newest generation.
        Assert.True(controller.ResumeMember(a, c));
        controller.ArmSettle();
        Assert.True(controller.IsCurrentSettle(controller.SettleGeneration));
        Assert.False(controller.IsCurrentSettle(armedGeneration));
    }
}
