using System;
using Xunit;
using TabDock.Models;
using TabDock.Services;

namespace TabDock.UnitTests;

/// <summary>
/// Regression guards for hardening-audit fixes: relayout scheduling,
/// split suspension isolation, and policy/membership invariants.
/// All tests are headless (no HWND / WPF / input).
/// </summary>
public class HardeningRegressionTests
{
    // --------------------------------------------------------------------
    // PresentationLayoutCoordinator (Wave 3D Model B): queued frames are never
    // discarded — there is no invalidation transition. A callback invoked
    // arbitrarily late still runs exactly once against CURRENT presentation
    // state; the execute closure must stay self-refreshing/idempotent.
    // RequestRelayout's schedule delegate is Action&lt;Action&gt; — the framework
    // calls it as schedule(() => { execute... }).
    // --------------------------------------------------------------------

    [Fact]
    public void Coordinator_DeferredQueuedFrame_AlwaysExecutesOnce()
    {
        var c = new PresentationLayoutCoordinator();
        int executes = 0;
        Action? captured = null;
        void Schedule(Action act) => captured = act;
        c.RequestRelayout(Schedule, () => executes++);
        Assert.NotNull(captured);
        captured!.Invoke();
        Assert.Equal(1, executes);
        // After the frame consumed itself, a new request schedules fresh work.
        captured = null;
        c.RequestRelayout(Schedule, () => executes++);
        Assert.NotNull(captured);
        captured!.Invoke();
        Assert.Equal(2, executes);
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
