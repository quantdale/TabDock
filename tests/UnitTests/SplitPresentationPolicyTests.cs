using TabDock.Models;
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
}
