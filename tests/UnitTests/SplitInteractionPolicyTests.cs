using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Deterministic tests for <see cref="SplitInteractionPolicy"/> — the tiny
/// pure policy that classifies a tab-strip hit into an action, isolating
/// "WPF hit → policy decision" from actual native work.
/// </summary>
public class SplitInteractionPolicyTests
{
    private static SplitPresentationState Pair(string focus = "A")
        => SplitPresentationPolicy.DefinePair("A", "B", focus);

    // --------------------------------------------------------------------
    // Handled preview event still yields SuspendPairForGuest for non-member
    // --------------------------------------------------------------------

    [Fact]
    public void HandledPreviewEvent_StillYieldsSuspendPairForGuest_ForNonMember()
    {
        // The policy is driven by boolean flags, not by RoutedEventArgs.Handled.
        // A handled preview event that already hit-tested a non-member must
        // still suspend. This is the regression guard for "handled event does
        // not suppress non-member activation".
        var pair = Pair("A");
        var action = SplitInteractionPolicy.Classify(
            pair,
            isSplitPresented: true,
            isTargetSplitMember: false,
            isButtonHit: false,
            isStaleIdentity: false,
            nativeOutcome: SplitNativeTransitionOutcome.Succeeded,
            isRightClickOrHover: false);
        Assert.Equal(SplitInteractionAction.SuspendPairForGuest, action);
    }

    [Fact]
    public void HandledPreviewEvent_NonMember_WithViewFlagMismatch_StillSuspends()
    {
        // Even if authoritative state is presented but view flag is stale,
        // the member check keeps it correct. PairPresented in state is the
        // fallback; either being true triggers suspend for non-members.
        var pair = Pair("A");
        var action = SplitInteractionPolicy.Classify(
            pair,
            isSplitPresented: false, // view thinks not presented, but state is
            isTargetSplitMember: false,
            isButtonHit: false,
            isStaleIdentity: false,
            nativeOutcome: SplitNativeTransitionOutcome.Succeeded,
            isRightClickOrHover: false);
        // State PairPresented is true, so classify still suspends.
        Assert.Equal(SplitInteractionAction.SuspendPairForGuest, action);
    }

    // --------------------------------------------------------------------
    // Split member clicks do not suspend
    // --------------------------------------------------------------------

    [Fact]
    public void SplitMemberClick_WhenPresented_YieldsIgnoreMember_NotSuspend()
    {
        var pair = Pair("A");
        var action = SplitInteractionPolicy.Classify(
            pair,
            isSplitPresented: true,
            isTargetSplitMember: true,
            isButtonHit: false,
            isStaleIdentity: false,
            nativeOutcome: SplitNativeTransitionOutcome.Succeeded,
            isRightClickOrHover: false);
        Assert.Equal(SplitInteractionAction.IgnoreMember, action);
    }

    [Fact]
    public void SplitMemberClick_WhenDormant_YieldsResumeMember()
    {
        var pair = Pair("A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        var action = SplitInteractionPolicy.Classify(
            dormant,
            isSplitPresented: false,
            isTargetSplitMember: true,
            isButtonHit: false,
            isStaleIdentity: false,
            nativeOutcome: SplitNativeTransitionOutcome.Succeeded,
            isRightClickOrHover: false);
        Assert.Equal(SplitInteractionAction.ResumeMember, action);
    }

    [Fact]
    public void SplitMemberClick_A_To_B_WhilePresented_IsIgnoreMember()
    {
        // A<->B member focus while presented must not suspend — the
        // presentation policy handles it as SelectMember with PairPresented.
        var pair = Pair("A");
        var actionA = SplitInteractionPolicy.Classify(pair, true, true, false, false, SplitNativeTransitionOutcome.Succeeded, false);
        Assert.Equal(SplitInteractionAction.IgnoreMember, actionA);

        var pairB = Pair("B");
        var actionB = SplitInteractionPolicy.Classify(pairB, true, true, false, false, SplitNativeTransitionOutcome.Succeeded, false);
        Assert.Equal(SplitInteractionAction.IgnoreMember, actionB);
    }

    // --------------------------------------------------------------------
    // Button / context-menu bypass
    // --------------------------------------------------------------------

    [Fact]
    public void ButtonHit_YieldsIgnoreButton_EvenForNonMember()
    {
        var pair = Pair("A");
        var action = SplitInteractionPolicy.Classify(
            pair,
            isSplitPresented: true,
            isTargetSplitMember: false,
            isButtonHit: true,
            isStaleIdentity: false,
            nativeOutcome: SplitNativeTransitionOutcome.Succeeded,
            isRightClickOrHover: false);
        Assert.Equal(SplitInteractionAction.IgnoreButton, action);
    }

    [Fact]
    public void ButtonHit_YieldsIgnoreButton_EvenForMember()
    {
        var pair = Pair("A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        var action = SplitInteractionPolicy.Classify(
            dormant,
            isSplitPresented: false,
            isTargetSplitMember: true,
            isButtonHit: true,
            isStaleIdentity: false,
            nativeOutcome: SplitNativeTransitionOutcome.Succeeded,
            isRightClickOrHover: false);
        Assert.Equal(SplitInteractionAction.IgnoreButton, action);
    }

    [Fact]
    public void ButtonHit_TakesPrecedence_OverStaleAndRecoveryPending()
    {
        var pair = Pair("A");
        // Even with stale + recovery-pending, button wins.
        var action = SplitInteractionPolicy.Classify(
            pair,
            isSplitPresented: true,
            isTargetSplitMember: false,
            isButtonHit: true,
            isStaleIdentity: true,
            nativeOutcome: SplitNativeTransitionOutcome.RecoveryPending,
            isRightClickOrHover: false);
        Assert.Equal(SplitInteractionAction.IgnoreButton, action);
    }

    // --------------------------------------------------------------------
    // Right-click / hover does not suspend
    // --------------------------------------------------------------------

    [Fact]
    public void RightClickOrHover_YieldsNone_DoesNotSuspend()
    {
        var pair = Pair("A");
        var action = SplitInteractionPolicy.Classify(
            pair,
            isSplitPresented: true,
            isTargetSplitMember: false,
            isButtonHit: false,
            isStaleIdentity: false,
            nativeOutcome: SplitNativeTransitionOutcome.Succeeded,
            isRightClickOrHover: true);
        Assert.Equal(SplitInteractionAction.None, action);
    }

    [Fact]
    public void RightClickOrHover_OnMember_AlsoYieldsNone()
    {
        var pair = Pair("A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        var action = SplitInteractionPolicy.Classify(
            dormant,
            isSplitPresented: false,
            isTargetSplitMember: true,
            isButtonHit: false,
            isStaleIdentity: false,
            nativeOutcome: SplitNativeTransitionOutcome.Succeeded,
            isRightClickOrHover: true);
        Assert.Equal(SplitInteractionAction.None, action);
    }

    [Fact]
    public void RightClickOrHover_TakesPrecedence_OverMemberSuspend()
    {
        var pair = Pair("A");
        // Right-click on non-member with presented pair would otherwise suspend.
        var action = SplitInteractionPolicy.Classify(
            pair,
            isSplitPresented: true,
            isTargetSplitMember: false,
            isButtonHit: false,
            isStaleIdentity: false,
            nativeOutcome: SplitNativeTransitionOutcome.Succeeded,
            isRightClickOrHover: true);
        Assert.Equal(SplitInteractionAction.None, action);
    }

    // --------------------------------------------------------------------
    // Stale / recycled identity rejection
    // --------------------------------------------------------------------

    [Fact]
    public void StaleIdentity_YieldsRejectStale_FailClosed()
    {
        var pair = Pair("A");
        var action = SplitInteractionPolicy.Classify(
            pair,
            isSplitPresented: true,
            isTargetSplitMember: false,
            isButtonHit: false,
            isStaleIdentity: true,
            nativeOutcome: SplitNativeTransitionOutcome.Succeeded,
            isRightClickOrHover: false);
        Assert.Equal(SplitInteractionAction.RejectStale, action);
    }

    [Fact]
    public void StaleIdentity_OnMember_AlsoRejects()
    {
        var pair = Pair("A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        var action = SplitInteractionPolicy.Classify(
            dormant,
            isSplitPresented: false,
            isTargetSplitMember: true,
            isButtonHit: false,
            isStaleIdentity: true,
            nativeOutcome: SplitNativeTransitionOutcome.Succeeded,
            isRightClickOrHover: false);
        Assert.Equal(SplitInteractionAction.RejectStale, action);
    }

    // --------------------------------------------------------------------
    // Recovery-pending / ShowFailed keeps pair authoritative
    // --------------------------------------------------------------------

    [Theory]
    [InlineData(SplitNativeTransitionOutcome.RecoveryPending)]
    [InlineData(SplitNativeTransitionOutcome.ShowFailed)]
    [InlineData(SplitNativeTransitionOutcome.IdentityMismatch)]
    public void NonSucceededOutcome_YieldsFailClosedRecoveryPending(SplitNativeTransitionOutcome outcome)
    {
        var pair = Pair("A");
        var action = SplitInteractionPolicy.Classify(
            pair,
            isSplitPresented: true,
            isTargetSplitMember: false,
            isButtonHit: false,
            isStaleIdentity: false,
            nativeOutcome: outcome,
            isRightClickOrHover: false);
        Assert.Equal(SplitInteractionAction.FailClosedRecoveryPending, action);
    }

    [Fact]
    public void SucceededOutcome_DoesNotFailClosed()
    {
        var pair = Pair("A");
        var action = SplitInteractionPolicy.Classify(
            pair,
            isSplitPresented: true,
            isTargetSplitMember: false,
            isButtonHit: false,
            isStaleIdentity: false,
            nativeOutcome: SplitNativeTransitionOutcome.Succeeded,
            isRightClickOrHover: false);
        Assert.Equal(SplitInteractionAction.SuspendPairForGuest, action);
    }

    // --------------------------------------------------------------------
    // Dormant / no-relationship edge cases
    // --------------------------------------------------------------------

    [Fact]
    public void Dormant_NoSuspend_ForNonMember_WhenAlreadyDormant()
    {
        var pair = Pair("A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        // Clicking another non-member (D) while already dormant: the
        // classifier returns None (caller routes via SelectNonMember if
        // desired). The key contract is it does NOT return SuspendPairForGuest.
        var action = SplitInteractionPolicy.Classify(
            dormant,
            isSplitPresented: false,
            isTargetSplitMember: false,
            isButtonHit: false,
            isStaleIdentity: false,
            nativeOutcome: SplitNativeTransitionOutcome.Succeeded,
            isRightClickOrHover: false);
        Assert.Equal(SplitInteractionAction.None, action);
    }

    [Fact]
    public void NoRelationship_AnyHit_ReturnsNoneOrIgnoreMember()
    {
        var none = SplitPresentationPolicy.NoPair("Z");
        var a1 = SplitInteractionPolicy.Classify(none, false, false, false, false, SplitNativeTransitionOutcome.Succeeded, false);
        Assert.Equal(SplitInteractionAction.None, a1);

        var a2 = SplitInteractionPolicy.Classify(none, false, true, false, false, SplitNativeTransitionOutcome.Succeeded, false);
        Assert.Equal(SplitInteractionAction.IgnoreMember, a2);
    }

    // --------------------------------------------------------------------
    // Convenience overload
    // --------------------------------------------------------------------

    [Fact]
    public void ConvenienceOverload_MirrorsFullClassify_ForPresentedPair()
    {
        var pair = Pair("A");
        var viaConvenience = SplitInteractionPolicy.Classify(pair, isTargetSplitMember: false, isButtonHit: false, isStaleIdentity: false, isRightClickOrHover: false);
        var viaFull = SplitInteractionPolicy.Classify(pair, pair.PairPresented, false, false, false, SplitNativeTransitionOutcome.Succeeded, false);
        Assert.Equal(viaFull, viaConvenience);
    }
}
