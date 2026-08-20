using System;
using Xunit;
using TabDock.Models;
using TabDock.Services;

namespace TabDock.UnitTests;

/// <summary>
/// Regression guards for hardening-audit fixes: stale generation tokens,
/// split suspension isolation, and policy/membership invariants.
/// All tests are headless (no HWND / WPF / input).
/// </summary>
public class HardeningRegressionTests
{
    // --------------------------------------------------------------------
    // PresentationLayoutCoordinator: stale Render callbacks must not execute
    // after InvalidateLayout; ensureFinalPass still fires the final z-order.
    // RequestRelayout's schedule delegate is Action&lt;Action&gt; — the framework
    // calls it as schedule(() => { execute... }).
    // --------------------------------------------------------------------

    [Fact]
    public void Coordinator_StaleRenderCallback_IsSuppressedAfterInvalidate()
    {
        var c = new PresentationLayoutCoordinator();
        int executes = 0;
        Action? captured = null;
        void Schedule(Action act) => captured = act;
        c.RequestRelayout(Schedule, () => executes++);
        Assert.NotNull(captured);
        c.InvalidateLayout();
        captured!.Invoke();
        Assert.Equal(0, executes);
    }

    [Fact]
    public void Coordinator_StaleRenderCallback_EnsureFinalPass_StillRuns()
    {
        var c = new PresentationLayoutCoordinator();
        int executes = 0;
        Action? pendingOutside = null;
        void Schedule(Action a) => pendingOutside = a;

        c.RequestRelayout(Schedule, () => executes++, ensureFinalPass: true);
        Assert.NotNull(pendingOutside);
        c.InvalidateLayout();
        Action? first = pendingOutside;
        pendingOutside = null;
        first!.Invoke();
        Assert.Equal(0, executes);
        Assert.NotNull(pendingOutside);
        pendingOutside!.Invoke();
        Assert.Equal(1, executes);
    }

    [Fact]
    public void Coordinator_FreshCallback_Executes()
    {
        var c = new PresentationLayoutCoordinator();
        int executes = 0;
        Action? cap = null;
        void Schedule(Action a) => cap = a;
        c.RequestRelayout(Schedule, () => executes++);
        Assert.NotNull(cap);
        cap!.Invoke();
        Assert.Equal(1, executes);
    }

    [Fact]
    public void SplitPresentationPolicy_RemoveMember_WhileDormant_ChoosesCorrectSurvivor()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A", 0);
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        Assert.Equal("C", dormant.ActiveGuest);
        // RemoveMember requires removed be a pair member — removing C (the
        // dormant active, but not a member) leaves the state unchanged.
        var afterC = SplitPresentationPolicy.RemoveMember(dormant, "C");
        Assert.Equal("C", afterC.ActiveGuest);
        Assert.Equal("A", afterC.Left);
        // Removing A while C is active keeps C as survivor.
        var afterA = SplitPresentationPolicy.RemoveMember(dormant, "A");
        Assert.Equal("C", afterA.ActiveGuest);
        Assert.Null(afterA.Left); // NoPair after structural invalidation
    }

    [Fact]
    public void SplitInteractionPolicy_Priority_StaleBeforeRecoveryPending()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A", 0);
        var action = SplitInteractionPolicy.Classify(
            pair, true, false, false, isStaleIdentity: true,
            nativeOutcome: SplitNativeTransitionOutcome.RecoveryPending, isRightClickOrHover: false);
        Assert.Equal(SplitInteractionAction.RejectStale, action);
    }

    [Fact]
    public void SplitPresentationPolicy_IsCurrentSettle_DormantNeverResurrects()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A", 5);
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        Assert.False(SplitPresentationPolicy.IsCurrentSettle(dormant, dormant.Generation));
        Assert.False(SplitPresentationPolicy.IsCurrentSettle(dormant, pair.Generation));
    }
}
