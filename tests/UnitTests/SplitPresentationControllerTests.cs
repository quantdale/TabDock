using System;
using System.Collections.Generic;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Drives the REAL SplitPresentationController (not just the pure policy) so
/// production transition behavior — including guarded native boundaries and
/// commit-only-on-success atomicity — is proven against the tested authority.
/// </summary>
public class SplitPresentationControllerTests
{
    private sealed class FakePresentationOps : IPresentationOperations
    {
        // Pending outcomes consumed in order by Hide; empty queue = success.
        private readonly Queue<WindowHideOutcome> _hideOutcomes = new();
        public List<CapturedWindow> HiddenWindows { get; } = new();
        public Func<CapturedWindow, bool>? IsCurrentOverride { get; set; }

        public void QueueHideOutcome(WindowHideOutcome outcome) => _hideOutcomes.Enqueue(outcome);

        public WindowHideOutcome Hide(CapturedWindow window)
        {
            WindowHideOutcome outcome = _hideOutcomes.Count > 0 ? _hideOutcomes.Dequeue() : WindowHideOutcome.Hidden;
            if (outcome == WindowHideOutcome.Hidden)
                HiddenWindows.Add(window);
            return outcome;
        }

        public void PositionAndShow(CapturedWindow window, IntPtr containerHwnd, NativeMethods.RECT screenRect) { }
        public void PositionGuestsDeferred(CapturedWindow top, NativeMethods.RECT topRect, CapturedWindow bottom, NativeMethods.RECT bottomRect, IntPtr containerHwnd) { }
        public void SetForeground(CapturedWindow window) { }
        public void PairZOrderBehind(IntPtr containerHwnd, CapturedWindow guest) { }
        public bool IsCurrentCapturedWindow(CapturedWindow window)
            => IsCurrentOverride?.Invoke(window) ?? true;
    }

    private static CapturedWindow W(string name)
    {
        int hwnd = name[name.Length - 1] - 'A' + 1;
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

    private static (SplitPresentationController Controller, FakePresentationOps Ops) Create()
    {
        FakePresentationOps ops = new();
        var controller = new SplitPresentationController(ops);
        return (controller, ops);
    }

    [Fact]
    public void DefinePair_CommitsPresentedPairWithFocusedLeft()
    {
        var (controller, _) = Create();
        CapturedWindow a = W("A"), b = W("B");

        SplitTransitionResult result = controller.DefinePair(a, b, a);

        Assert.True(result.Committed);
        Assert.Equal(SplitNativeTransitionOutcome.Succeeded, result.Native);
        Assert.True(controller.IsPresented);
        Assert.Same(a, controller.Foreground);
        Assert.Equal(1, controller.Generation);
    }

    [Fact]
    public void DefinePair_ReplacementHidesOnlyDepartingMembers()
    {
        var (controller, ops) = Create();
        CapturedWindow a = W("A"), b = W("B"), c = W("C"), d = W("D");
        controller.DefinePair(a, b, a);

        controller.DefinePair(c, d, c);

        // A and B depart; nothing else is hidden by the define itself.
        Assert.Contains(ops.HiddenWindows, w => ReferenceEquals(w, a));
        Assert.Contains(ops.HiddenWindows, w => ReferenceEquals(w, b));
        Assert.Equal(2, ops.HiddenWindows.Count);
        Assert.Same(c, controller.Left);
        Assert.Same(d, controller.Right);
    }

    [Fact]
    public void DefinePair_RecoveryPendingOnDepartingHide_RetainsPriorPairUncommitted()
    {
        var (controller, ops) = Create();
        CapturedWindow a = W("A"), b = W("B"), c = W("C"), d = W("D");
        controller.DefinePair(a, b, a);
        long generationBefore = controller.Generation;

        // First departing hide succeeds, second returns RecoveryPending: the
        // logical state must NOT become "new pair presented" while the visible
        // state still contains the old pair.
        ops.QueueHideOutcome(WindowHideOutcome.Hidden);
        ops.QueueHideOutcome(WindowHideOutcome.RecoveryPending);

        SplitTransitionResult result = controller.DefinePair(c, d, c);

        Assert.False(result.Committed);
        Assert.Equal(SplitNativeTransitionOutcome.RecoveryPending, result.Native);
        Assert.Same(a, controller.Left);
        Assert.Same(b, controller.Right);
        Assert.True(controller.IsPresented);
        Assert.Equal(generationBefore, controller.Generation);
    }

    [Fact]
    public void SuspendForGuest_DormantsPairAndFocusesTheNonMember()
    {
        var (controller, _) = Create();
        CapturedWindow a = W("A"), b = W("B"), c = W("C");
        controller.DefinePair(a, b, a);

        bool committed = controller.SuspendForGuest(c);

        Assert.True(committed);
        Assert.False(controller.IsPresented);
        Assert.True(controller.IsRelationshipDefined);
        Assert.Same(c, controller.Foreground);
    }

    [Fact]
    public void SuspendForGuest_PendingAfterFirstHide_RetainsPresentedPair()
    {
        var (controller, ops) = Create();
        CapturedWindow a = W("A"), b = W("B"), c = W("C");
        controller.DefinePair(a, b, a);

        ops.QueueHideOutcome(WindowHideOutcome.Hidden);          // left hides
        ops.QueueHideOutcome(WindowHideOutcome.RecoveryPending); // right pends

        bool committed = controller.SuspendForGuest(c);

        Assert.False(committed);
        Assert.True(controller.IsPresented);
        Assert.Same(a, controller.Foreground);
    }

    [Fact]
    public void SuspendForGuest_IdentityMismatch_IsRejectedWithoutNativeWork()
    {
        var (_, ops) = Create();
        CapturedWindow a = W("A"), b = W("B"), c = W("C");
        // The controller's identity gate is its isCurrent seam (wired to the
        // shepherd in production), not the presentation-operations fake.
        bool guestIsCurrent = true;
        var gated = new SplitPresentationController(ops, isCurrent: _ => guestIsCurrent);
        gated.DefinePair(a, b, a);
        guestIsCurrent = false;

        bool committed = gated.SuspendForGuest(c);

        Assert.False(committed);
        Assert.True(gated.IsPresented);
        Assert.Empty(ops.HiddenWindows);
    }

    [Fact]
    public void ResumeMember_HidesDormantSingleGuestAndCommitsPair()
    {
        var (controller, ops) = Create();
        CapturedWindow a = W("A"), b = W("B"), c = W("C");
        controller.DefinePair(a, b, a);
        controller.SuspendForGuest(c);

        bool committed = controller.ResumeMember(a, c);

        Assert.True(committed);
        Assert.True(controller.IsPresented);
        Assert.Same(a, controller.Foreground);
        Assert.Contains(ops.HiddenWindows, w => ReferenceEquals(w, c));
    }

    [Fact]
    public void ResumeMember_PendingHideOfSingleGuest_RetainsDormantState()
    {
        var (controller, ops) = Create();
        CapturedWindow a = W("A"), b = W("B"), c = W("C");
        controller.DefinePair(a, b, a);
        controller.SuspendForGuest(c);

        ops.QueueHideOutcome(WindowHideOutcome.RecoveryPending);

        bool committed = controller.ResumeMember(a, c);

        Assert.False(committed);
        Assert.False(controller.IsPresented);
        Assert.Same(c, controller.Foreground);
    }

    [Fact]
    public void HandleMemberRemoved_PresentedActiveRemoved_PromotesSurvivor()
    {
        var (controller, _) = Create();
        CapturedWindow a = W("A"), b = W("B");
        controller.DefinePair(a, b, a);

        CapturedWindow? survivor = controller.HandleMemberRemoved(a);

        Assert.Same(b, survivor);
        Assert.False(controller.IsRelationshipDefined);
        Assert.Same(b, controller.Foreground);
    }

    [Fact]
    public void HandleMemberRemoved_PresentedInactiveRemoved_KeepsActiveMember()
    {
        var (controller, _) = Create();
        CapturedWindow a = W("A"), b = W("B");
        controller.DefinePair(a, b, a);

        CapturedWindow? survivor = controller.HandleMemberRemoved(b);

        Assert.Same(a, survivor);
        Assert.Same(a, controller.Foreground);
    }

    [Fact]
    public void HandleMemberRemoved_DormantNonMemberActive_PreservesTheNonMemberNotThePairSurvivor()
    {
        // The pure policy preserves the dormant active non-member after a pair
        // member is removed; the controller must agree (this used to diverge
        // and promote the surviving pair member instead).
        var (controller, _) = Create();
        CapturedWindow a = W("A"), b = W("B"), c = W("C");
        controller.DefinePair(a, b, a);
        controller.SuspendForGuest(c);

        CapturedWindow? survivor = controller.HandleMemberRemoved(a);

        Assert.Same(c, survivor);
        Assert.Same(c, controller.Foreground);
        Assert.False(controller.IsRelationshipDefined);
    }

    [Fact]
    public void CommitExplicitExit_ClearsRelationshipAndKeepsSurvivor()
    {
        var (controller, _) = Create();
        CapturedWindow a = W("A"), b = W("B");
        controller.DefinePair(a, b, a);

        controller.CommitExplicitExit(b);

        Assert.False(controller.IsRelationshipDefined);
        Assert.Same(b, controller.Foreground);
    }

    [Fact]
    public void RepeatedThirdTabSwitching_StaysConsistentAcrossManyCycles()
    {
        var (controller, _) = Create();
        CapturedWindow a = W("A"), b = W("B"), c = W("C");
        controller.DefinePair(a, b, a);

        for (int i = 0; i < 20; i++)
        {
            Assert.True(controller.SuspendForGuest(c));
            Assert.False(controller.IsPresented);
            Assert.True(controller.ResumeMember(i % 2 == 0 ? a : b, c));
            Assert.True(controller.IsPresented);
        }

        Assert.True(controller.IsPresented);
        Assert.Same(b, controller.Foreground);
    }
}
