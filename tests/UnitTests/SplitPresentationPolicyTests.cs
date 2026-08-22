using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Headless regression coverage for the pure split-presentation state machine.
/// The policy decides logical authority only; deterministic tests can drive it
/// without creating windows or sending input (it operates on stable string
/// identities). These guard the survivor-promotion and dormant-pair contracts
/// that the real-input ValidationDriver scenarios exercise on a live desktop.
/// </summary>
public class SplitPresentationPolicyTests
{
    // --------------------------------------------------------------------
    // Basics: DefinePair / NoPair
    // --------------------------------------------------------------------

    [Fact]
    public void DefinePair_PresentsWithFocusDefaultingToLeft()
    {
        var s = SplitPresentationPolicy.DefinePair("A", "B");
        Assert.True(s.RelationshipDefined);
        Assert.True(s.PairPresented);
        Assert.Equal("A", s.Left);
        Assert.Equal("B", s.Right);
        Assert.Equal("A", s.ActiveGuest); // focus defaults to left
        Assert.Equal(SplitPresentationMode.Pair, s.Mode);
        Assert.Equal(1, s.Generation); // starts at generation 0, +1
    }

    [Fact]
    public void DefinePair_KeepsExplicitFocusOnRight()
    {
        var s = SplitPresentationPolicy.DefinePair("A", "B", focusedMember: "B");
        Assert.Equal("B", s.ActiveGuest);
    }

    [Theory]
    [InlineData(null, "B")]
    [InlineData("A", null)]
    [InlineData("A", "A")]
    [InlineData("", "B")]
    public void DefinePair_RejectsInvalidMembers(string? left, string? right)
    {
        Assert.Throws<ArgumentException>(() => SplitPresentationPolicy.DefinePair(left!, right!));
    }

    [Fact]
    public void SelectNonMember_SuspendsPairAndActivatesGuest()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var s = SplitPresentationPolicy.SelectNonMember(pair, "C");
        Assert.False(s.PairPresented);
        Assert.Equal("C", s.ActiveGuest);
        Assert.Equal(SplitPresentationMode.SingleGuest, s.Mode);
        Assert.Equal(pair.Generation + 1, s.Generation);
        // The relationship itself is retained (dormant), not destroyed.
        Assert.True(s.RelationshipDefined);
    }

    [Fact]
    public void SelectNonMember_IsNoOpForMemberOrNoRelationship()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        Assert.Equal(pair, SplitPresentationPolicy.SelectNonMember(pair, "A")); // member -> no-op
        Assert.Equal(pair, SplitPresentationPolicy.SelectNonMember(pair, "B"));
        var none = SplitPresentationPolicy.NoPair("Z");
        Assert.Equal(none, SplitPresentationPolicy.SelectNonMember(none, "C")); // no relationship -> no-op
    }

    [Fact]
    public void SelectMember_ResumesPairAndActivatesMember()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        var s = SplitPresentationPolicy.SelectMember(dormant, "B");
        Assert.True(s.PairPresented);
        Assert.Equal("B", s.ActiveGuest);
        Assert.Equal(SplitPresentationMode.Pair, s.Mode);
    }

    [Fact]
    public void ExplicitExit_RemovesRelationshipButKeepsSurvivor()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var s = SplitPresentationPolicy.ExplicitExit(pair);
        Assert.False(s.RelationshipDefined);
        Assert.Equal("A", s.ActiveGuest); // active survivor retained
        Assert.Equal(pair.Generation + 1, s.Generation);
    }

    [Fact]
    public void RemoveMember_WhenActiveRemoved_PromotesOtherMember()
    {
        // Active guest A is removed while presented -> the other member B is the
        // deterministic survivor (regression guard for the 3-tab survivor case).
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var s = SplitPresentationPolicy.RemoveMember(pair, "A");
        Assert.False(s.RelationshipDefined);
        Assert.Equal("B", s.ActiveGuest);
    }

    [Fact]
    public void RemoveMember_WhenNonActiveRemoved_KeepsActive()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var s = SplitPresentationPolicy.RemoveMember(pair, "B");
        Assert.False(s.RelationshipDefined);
        Assert.Equal("A", s.ActiveGuest);
    }

    [Fact]
    public void ResolveNativeTransition_SucceededTakesDesired()
    {
        var auth = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var desired = SplitPresentationPolicy.DefinePair("A", "B", "B");
        var s = SplitPresentationPolicy.ResolveNativeTransition(
            auth, desired, SplitNativeTransitionOutcome.Succeeded);
        Assert.Equal("B", s.ActiveGuest);
    }

    [Fact]
    public void ResolveNativeTransition_NonSucceededKeepsAuthoritative()
    {
        var auth = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var desired = SplitPresentationPolicy.DefinePair("A", "B", "B");
        foreach (var outcome in new[]
        {
            SplitNativeTransitionOutcome.RecoveryPending,
            SplitNativeTransitionOutcome.IdentityMismatch,
            SplitNativeTransitionOutcome.ShowFailed,
        })
        {
            var s = SplitPresentationPolicy.ResolveNativeTransition(auth, desired, outcome);
            Assert.Equal("A", s.ActiveGuest);
        }
    }

    [Fact]
    public void IsCurrentSettle_OnlyForPresentedMatchingGeneration()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        Assert.True(SplitPresentationPolicy.IsCurrentSettle(pair, pair.Generation));
        Assert.False(SplitPresentationPolicy.IsCurrentSettle(pair, pair.Generation + 1)); // stale callback

        // A dormant (non-presented) relationship must never be resurrected.
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        Assert.False(SplitPresentationPolicy.IsCurrentSettle(dormant, dormant.Generation));
    }

    // --------------------------------------------------------------------
    // Exhaustive review-checklist cases
    // --------------------------------------------------------------------

    [Fact]
    public void ThreeTabs_SplitAB_PlusC_SuspendAndResumePreservesPair()
    {
        // 3 tabs: A/B split + C. Selecting C suspends, selecting A/B resumes.
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        Assert.True(dormant.RelationshipDefined);
        Assert.False(dormant.PairPresented);
        Assert.Equal("C", dormant.ActiveGuest);

        var resumeA = SplitPresentationPolicy.SelectMember(dormant, "A");
        Assert.True(resumeA.PairPresented);
        Assert.Equal("A", resumeA.ActiveGuest);
        Assert.Equal("A", resumeA.Left);
        Assert.Equal("B", resumeA.Right);

        // Also verify resume via B.
        var resumeB = SplitPresentationPolicy.SelectMember(dormant, "B");
        Assert.True(resumeB.PairPresented);
        Assert.Equal("B", resumeB.ActiveGuest);
    }

    [Fact]
    public void FourTabs_SplitAB_PlusCAndD_GuestSwitching()
    {
        // 4 tabs: A/B split + C/D. C->D guest switching stays dormant.
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var s1 = SplitPresentationPolicy.SelectNonMember(pair, "C");
        var s2 = SplitPresentationPolicy.SelectNonMember(s1, "D");
        Assert.True(s2.RelationshipDefined);
        Assert.False(s2.PairPresented);
        Assert.Equal("D", s2.ActiveGuest);
        Assert.Equal("A", s2.Left);
        Assert.Equal("B", s2.Right);

        // Resume still restores exact pair identity.
        var resumed = SplitPresentationPolicy.SelectMember(s2, "A");
        Assert.True(resumed.PairPresented);
        Assert.Equal("A", resumed.Left);
        Assert.Equal("B", resumed.Right);
    }

    [Fact]
    public void Repeated_CD_Switching_20Cycles_DormantSurvives()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var cur = SplitPresentationPolicy.SelectNonMember(pair, "C");
        for (int i = 0; i < 20; i++)
        {
            cur = SplitPresentationPolicy.SelectNonMember(cur, "D");
            Assert.True(cur.RelationshipDefined, $"cycle {i} D: relationship lost");
            Assert.False(cur.PairPresented);
            Assert.Equal("D", cur.ActiveGuest);

            cur = SplitPresentationPolicy.SelectNonMember(cur, "C");
            Assert.True(cur.RelationshipDefined, $"cycle {i} C: relationship lost");
            Assert.False(cur.PairPresented);
            Assert.Equal("C", cur.ActiveGuest);
        }
        // Pair identity survived all cycles.
        Assert.Equal("A", cur.Left);
        Assert.Equal("B", cur.Right);
    }

    [Fact]
    public void Alternating_C_And_D_GuestSwitches_DoNotDestroyPair()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var s = pair;
        string[] guests = { "C", "D", "C", "D", "C" };
        foreach (var g in guests)
        {
            s = SplitPresentationPolicy.SelectNonMember(s, g);
            Assert.True(s.RelationshipDefined);
            Assert.False(s.PairPresented);
            Assert.Equal(g, s.ActiveGuest);
        }
        Assert.Equal("A", s.Left);
        Assert.Equal("B", s.Right);
    }

    [Fact]
    public void SplitMemberFocus_A_To_B_WithoutSuspension()
    {
        // While pair is presented, SelectMember A<->B must not suspend.
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var toB = SplitPresentationPolicy.SelectMember(pair, "B");
        Assert.True(toB.PairPresented);
        Assert.True(toB.RelationshipDefined);
        Assert.Equal("B", toB.ActiveGuest);
        Assert.Equal(SplitPresentationMode.Pair, toB.Mode);

        var backToA = SplitPresentationPolicy.SelectMember(toB, "A");
        Assert.True(backToA.PairPresented);
        Assert.Equal("A", backToA.ActiveGuest);
    }

    [Fact]
    public void SelectMember_WhilePresented_DoesNotSuspend()
    {
        // Selecting the already-active member while presented is idempotent focus.
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var s = SplitPresentationPolicy.SelectMember(pair, "A");
        Assert.True(s.PairPresented);
        Assert.Equal("A", s.ActiveGuest);
    }

    [Fact]
    public void DormantPairResume_SelectMemberAfterSelectNonMember()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        Assert.False(dormant.PairPresented);

        var resumeA = SplitPresentationPolicy.SelectMember(dormant, "A");
        Assert.True(resumeA.PairPresented);
        Assert.Equal("A", resumeA.ActiveGuest);
        Assert.Equal("A", resumeA.Left);
        Assert.Equal("B", resumeA.Right);

        // B resume too.
        var dormant2 = SplitPresentationPolicy.SelectNonMember(pair, "D");
        var resumeB = SplitPresentationPolicy.SelectMember(dormant2, "B");
        Assert.True(resumeB.PairPresented);
        Assert.Equal("B", resumeB.ActiveGuest);
    }

    [Fact]
    public void DormantResume_RestoresExactPairIdentity()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        // Go through several guest switches then resume — identity must be exact.
        dormant = SplitPresentationPolicy.SelectNonMember(dormant, "D");
        dormant = SplitPresentationPolicy.SelectNonMember(dormant, "C");
        var resumed = SplitPresentationPolicy.SelectMember(dormant, "B");
        Assert.Equal("A", resumed.Left);
        Assert.Equal("B", resumed.Right);
        Assert.True(resumed.PairPresented);
        Assert.Equal("B", resumed.ActiveGuest);
    }

    [Fact]
    public void ExplicitExit_FromPresented_RemovesRelationship()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var exited = SplitPresentationPolicy.ExplicitExit(pair);
        Assert.False(exited.RelationshipDefined);
        Assert.Equal(SplitPresentationMode.None, exited.Mode);
        Assert.Equal("A", exited.ActiveGuest);
    }

    [Fact]
    public void ExplicitExit_FromDormant_RemovesRelationshipKeepsGuest()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        var exited = SplitPresentationPolicy.ExplicitExit(dormant);
        Assert.False(exited.RelationshipDefined);
        Assert.Equal("C", exited.ActiveGuest);
        Assert.Equal(SplitPresentationMode.None, exited.Mode);
    }

    [Fact]
    public void RemoveMember_WhileDormant_KeepsGuestSurvivor()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        // Removing either member while dormant keeps C as survivor.
        var rA = SplitPresentationPolicy.RemoveMember(dormant, "A");
        Assert.False(rA.RelationshipDefined);
        Assert.Equal("C", rA.ActiveGuest);

        var rB = SplitPresentationPolicy.RemoveMember(dormant, "B");
        Assert.False(rB.RelationshipDefined);
        Assert.Equal("C", rB.ActiveGuest);
    }

    [Fact]
    public void RemoveMember_WhilePresented_ActiveSurvivorPromotesOther()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A"); // A active
        var s = SplitPresentationPolicy.RemoveMember(pair, "A");
        Assert.False(s.RelationshipDefined);
        Assert.Equal("B", s.ActiveGuest);
    }

    [Fact]
    public void RemoveMember_WhilePresented_NonActiveSurvivorKeepsActive()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A"); // A active, remove B
        var s = SplitPresentationPolicy.RemoveMember(pair, "B");
        Assert.False(s.RelationshipDefined);
        Assert.Equal("A", s.ActiveGuest);
    }

    [Fact]
    public void RemoveMember_WhilePresented_BActive_NonActiveSurvivor()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "B"); // B active
        var s = SplitPresentationPolicy.RemoveMember(pair, "A");
        Assert.False(s.RelationshipDefined);
        Assert.Equal("B", s.ActiveGuest);
    }

    [Fact]
    public void RemoveMember_NonMember_IsNoOp()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        var s = SplitPresentationPolicy.RemoveMember(dormant, "C");
        Assert.Equal(dormant, s);
        var s2 = SplitPresentationPolicy.RemoveMember(pair, "Z");
        Assert.Equal(pair, s2);
    }

    [Fact]
    public void StaleTarget_IdentityMismatch_KeepsAuthoritative()
    {
        var auth = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var desired = SplitPresentationPolicy.SelectNonMember(auth, "C");
        var result = SplitPresentationPolicy.ResolveNativeTransition(auth, desired, SplitNativeTransitionOutcome.IdentityMismatch);
        Assert.Equal(auth, result);
        Assert.True(result.PairPresented);
        Assert.Equal("A", result.ActiveGuest);
    }

    [Fact]
    public void RecoveryPending_KeepsAuthoritative_PairAndDormant()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var desiredSuspend = SplitPresentationPolicy.SelectNonMember(pair, "C");
        var r1 = SplitPresentationPolicy.ResolveNativeTransition(pair, desiredSuspend, SplitNativeTransitionOutcome.RecoveryPending);
        Assert.Equal(pair, r1);
        Assert.Equal(SplitPresentationMode.Pair, r1.Mode);

        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        var desiredResume = SplitPresentationPolicy.SelectMember(dormant, "A");
        var r2 = SplitPresentationPolicy.ResolveNativeTransition(dormant, desiredResume, SplitNativeTransitionOutcome.RecoveryPending);
        Assert.Equal(dormant, r2);
        Assert.Equal(SplitPresentationMode.SingleGuest, r2.Mode);
    }

    [Fact]
    public void ShowFailed_KeepsAuthoritative()
    {
        var auth = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var desired = SplitPresentationPolicy.SelectNonMember(auth, "C");
        var result = SplitPresentationPolicy.ResolveNativeTransition(auth, desired, SplitNativeTransitionOutcome.ShowFailed);
        Assert.Equal(auth, result);
    }

    [Fact]
    public void DormantRelationship_SurvivesNonMemberSelection()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        Assert.True(dormant.RelationshipDefined);
        // Switching guest C->D must keep relationship defined (not destroy).
        var dormant2 = SplitPresentationPolicy.SelectNonMember(dormant, "D");
        Assert.True(dormant2.RelationshipDefined);
        Assert.Equal("A", dormant2.Left);
        Assert.Equal("B", dormant2.Right);
        Assert.False(dormant2.PairPresented);
    }

    [Fact]
    public void NormalTabsRemainSwitchable_AfterManyCycles()
    {
        // Loop 20: SelectNonMember C, resume A, SelectNonMember D, resume B
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var cur = pair;
        for (int i = 0; i < 20; i++)
        {
            cur = SplitPresentationPolicy.SelectNonMember(cur, "C");
            Assert.False(cur.PairPresented);
            Assert.Equal("C", cur.ActiveGuest);
            Assert.True(cur.RelationshipDefined);

            cur = SplitPresentationPolicy.SelectMember(cur, "A");
            Assert.True(cur.PairPresented);
            Assert.Equal("A", cur.ActiveGuest);

            cur = SplitPresentationPolicy.SelectNonMember(cur, "D");
            Assert.False(cur.PairPresented);
            Assert.Equal("D", cur.ActiveGuest);

            cur = SplitPresentationPolicy.SelectMember(cur, "B");
            Assert.True(cur.PairPresented);
            Assert.Equal("B", cur.ActiveGuest);
        }
        // Exact pair identity survived.
        Assert.Equal("A", cur.Left);
        Assert.Equal("B", cur.Right);
    }

    [Fact]
    public void Generation_IsMonotonic_EveryTransitionIncrements()
    {
        var s = SplitPresentationPolicy.DefinePair("A", "B", "A");
        long g = s.Generation;
        s = SplitPresentationPolicy.SelectNonMember(s, "C");
        Assert.Equal(g + 1, s.Generation); g = s.Generation;
        s = SplitPresentationPolicy.SelectMember(s, "A");
        Assert.Equal(g + 1, s.Generation); g = s.Generation;
        s = SplitPresentationPolicy.SelectNonMember(s, "D");
        Assert.Equal(g + 1, s.Generation); g = s.Generation;
        s = SplitPresentationPolicy.ExplicitExit(s);
        Assert.Equal(g + 1, s.Generation);
    }

    [Fact]
    public void IsCurrentSettle_Staleness_DormantNeverValid()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        // Dormant must never be considered current even with exact generation.
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        Assert.False(SplitPresentationPolicy.IsCurrentSettle(dormant, dormant.Generation));
        Assert.False(SplitPresentationPolicy.IsCurrentSettle(dormant, pair.Generation));

        // Presented pair: only exact generation is current.
        Assert.True(SplitPresentationPolicy.IsCurrentSettle(pair, pair.Generation));
        Assert.False(SplitPresentationPolicy.IsCurrentSettle(pair, pair.Generation - 1));
        Assert.False(SplitPresentationPolicy.IsCurrentSettle(pair, pair.Generation + 1));

        // No relationship never qualifies.
        var none = SplitPresentationPolicy.NoPair("Z");
        Assert.False(SplitPresentationPolicy.IsCurrentSettle(none, none.Generation));
        Assert.False(SplitPresentationPolicy.IsCurrentSettle(none, 0));
    }

    [Fact]
    public void RelationshipDefined_StaysTrue_AfterSuspend()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        Assert.True(dormant.RelationshipDefined);
        Assert.Equal(SplitPresentationMode.SingleGuest, dormant.Mode);
    }

    [Fact]
    public void Reconfigure_ReplacesRelationshipOnlyAfterSuccess()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        var reconfigured = SplitPresentationPolicy.Reconfigure(dormant, "C", "D");
        Assert.Equal("C", reconfigured.Left);
        Assert.Equal("D", reconfigured.Right);
        Assert.True(reconfigured.PairPresented);
        Assert.Equal("C", reconfigured.ActiveGuest);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void SelectMember_InvalidMember_IsNoOp(string invalidMember)
    {
        // While dormant, selecting a non-member does not resume.
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
        // "C" is not a member — SelectMember with "C" is a no-op.
        var s = SplitPresentationPolicy.SelectMember(dormant, "C");
        Assert.Equal(dormant, s);

        // No relationship: SelectMember is no-op regardless.
        var none = SplitPresentationPolicy.NoPair("Z");
        Assert.Equal(none, SplitPresentationPolicy.SelectMember(none, invalidMember));
    }

    // --------------------------------------------------------------------
    // Wave 3 additions: preferred-survivor explicit exit + focus switch
    // --------------------------------------------------------------------

    [Fact]
    public void ExplicitExit_PreferredSurvivor_WinsWhenItIsAMember()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");

        var exited = SplitPresentationPolicy.ExplicitExit(pair, preferredSurvivor: "B");

        Assert.False(exited.RelationshipDefined);
        Assert.Equal("B", exited.ActiveGuest);
        Assert.Equal(pair.Generation + 1, exited.Generation);
    }

    [Fact]
    public void ExplicitExit_PreferredSurvivor_NonMemberIgnored_KeepsActiveGuest()
    {
        var dormant = SplitPresentationPolicy.SelectNonMember(
            SplitPresentationPolicy.DefinePair("A", "B", "A"), "C");

        var exited = SplitPresentationPolicy.ExplicitExit(dormant, preferredSurvivor: "Z");

        // "Z" is not one of the two members: the preference cannot take effect,
        // so the dormant active non-member guest stays.
        Assert.Equal("C", exited.ActiveGuest);
        Assert.Equal(dormant.Generation + 1, exited.Generation);
    }

    [Fact]
    public void ExplicitExit_NoPreferred_KeepsDormantActiveGuest()
    {
        var dormant = SplitPresentationPolicy.SelectNonMember(
            SplitPresentationPolicy.DefinePair("A", "B", "A"), "C");

        var exited = SplitPresentationPolicy.ExplicitExit(dormant);

        Assert.Equal("C", exited.ActiveGuest);
        Assert.Equal(dormant.Generation + 1, exited.Generation);
    }

    [Fact]
    public void FocusMember_SwitchesActiveGuestWithinPair_GenerationUnchanged()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");

        var focused = SplitPresentationPolicy.FocusMember(pair, "B");

        Assert.Equal("B", focused.ActiveGuest);
        Assert.Equal(pair.Generation, focused.Generation);
        Assert.True(focused.PairPresented);
        Assert.Equal(pair.Left, focused.Left);
        Assert.Equal(pair.Right, focused.Right);
    }

    [Theory]
    [InlineData("C")] // non-member
    [InlineData("A")] // already active: no change
    public void FocusMember_NonMemberOrUnchanged_IsNoOp(string target)
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");

        Assert.Equal(pair, SplitPresentationPolicy.FocusMember(pair, target));
    }

    // --------------------------------------------------------------------
    // SplitInteractionPolicy integration smoke (button / hover bypass)
    // --------------------------------------------------------------------

    [Fact]
    public void SplitInteraction_ContextMenu_ButtonHit_ReturnsIgnoreButton()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var action = SplitInteractionPolicy.Classify(pair, isTargetSplitMember: false, isButtonHit: true, isStaleIdentity: false, isRightClickOrHover: false);
        Assert.Equal(SplitInteractionAction.IgnoreButton, action);
    }

    [Fact]
    public void SplitInteraction_RightClickOrHover_ReturnsNone_DoesNotSuspend()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var action = SplitInteractionPolicy.Classify(pair, isTargetSplitMember: false, isButtonHit: false, isStaleIdentity: false, isRightClickOrHover: true);
        Assert.Equal(SplitInteractionAction.None, action);
    }

    [Fact]
    public void SplitInteraction_StaleTarget_ReturnsRejectStale()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var action = SplitInteractionPolicy.Classify(pair, isTargetSplitMember: false, isButtonHit: false, isStaleIdentity: true, isRightClickOrHover: false);
        Assert.Equal(SplitInteractionAction.RejectStale, action);
    }

    [Fact]
    public void SplitInteraction_RecoveryPending_ReturnsFailClosed()
    {
        var pair = SplitPresentationPolicy.DefinePair("A", "B", "A");
        var action = SplitInteractionPolicy.Classify(pair, true, false, false, false, SplitNativeTransitionOutcome.RecoveryPending, false);
        Assert.Equal(SplitInteractionAction.FailClosedRecoveryPending, action);
    }
}
