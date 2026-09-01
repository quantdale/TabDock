using TabDock.ValidationDriver;
using Xunit;

namespace TabDock.UnitTests;

public sealed class VisualOutcomeAggregationTests
{
    [Fact]
    public void VisualOk_CannotOverrideNativeFailure()
    {
        foreach (ScenarioOutcomeKind nativeKind in new[]
        {
            ScenarioOutcomeKind.FailProduct,
            ScenarioOutcomeKind.FailHarness,
            ScenarioOutcomeKind.BlockedEnvironment,
            ScenarioOutcomeKind.BlockedSupervised,
        })
        {
            var native = new ScenarioOutcome(nativeKind, "native prerequisite failed");

            VisualQualificationDecision decision = VisualQualificationGate.Evaluate(
                native,
                VisualReviewVerdict.VISUAL_OK,
                reviewRequired: true);

            Assert.Equal(native, decision.NativeOutcome);
            Assert.Equal(native, decision.EffectiveOutcome);
            Assert.False(decision.ReleasePass);
        }
    }

    [Fact]
    public void RequiredVisualDefect_BlocksOtherwisePassingNativeOutcome()
    {
        VisualQualificationDecision decision = VisualQualificationGate.Evaluate(
            ScenarioOutcome.Pass,
            VisualReviewVerdict.VISUAL_DEFECT,
            reviewRequired: true);

        Assert.Equal(ScenarioOutcomeKind.Pass, decision.NativeOutcome.Kind);
        Assert.Equal(ScenarioOutcomeKind.FailProduct, decision.EffectiveOutcome.Kind);
        Assert.False(decision.ReleasePass);
        Assert.False(decision.VisualPass);
    }

    [Fact]
    public void OptionalVisualDefect_DoesNotRewriteNativeOutcome()
    {
        VisualQualificationDecision decision = VisualQualificationGate.Evaluate(
            ScenarioOutcome.Pass,
            VisualReviewVerdict.VISUAL_DEFECT,
            reviewRequired: false);

        Assert.Equal(ScenarioOutcomeKind.Pass, decision.NativeOutcome.Kind);
        Assert.Equal(ScenarioOutcomeKind.Pass, decision.EffectiveOutcome.Kind);
        Assert.True(decision.ReleasePass);
        Assert.False(decision.VisualPass);
    }

    [Fact]
    public void RequiredReviewUnavailable_BlocksCapabilityWithoutChangingNativeRecord()
    {
        VisualQualificationDecision decision = VisualQualificationGate.Evaluate(
            ScenarioOutcome.Pass,
            VisualReviewVerdict.REVIEW_UNAVAILABLE,
            reviewRequired: true);

        Assert.Equal(ScenarioOutcomeKind.Pass, decision.NativeOutcome.Kind);
        Assert.Equal(ScenarioOutcomeKind.BlockedCapability, decision.EffectiveOutcome.Kind);
        Assert.False(decision.ReleasePass);
        Assert.False(decision.VisualPass);
    }

    [Fact]
    public void OptionalReviewUnavailable_LeavesNativeQualificationIndependent()
    {
        VisualQualificationDecision decision = VisualQualificationGate.Evaluate(
            ScenarioOutcome.Pass,
            VisualReviewVerdict.REVIEW_UNAVAILABLE,
            reviewRequired: false);

        Assert.Equal(ScenarioOutcomeKind.Pass, decision.NativeOutcome.Kind);
        Assert.Equal(ScenarioOutcomeKind.Pass, decision.EffectiveOutcome.Kind);
        Assert.True(decision.ReleasePass);
        Assert.False(decision.VisualPass);
    }

    [Fact]
    public void ValidVisualReview_PreservesNativePass()
    {
        VisualQualificationDecision decision = VisualQualificationGate.Evaluate(
            ScenarioOutcome.Pass,
            VisualReviewVerdict.VISUAL_OK,
            reviewRequired: true);

        Assert.Equal(ScenarioOutcomeKind.Pass, decision.NativeOutcome.Kind);
        Assert.Equal(ScenarioOutcomeKind.Pass, decision.EffectiveOutcome.Kind);
        Assert.True(decision.ReleasePass);
        Assert.True(decision.VisualPass);
    }

    [Fact]
    public void FirstAttemptDefect_RemainsAuthoritativeAcrossRerun()
    {
        var aggregate = new ScenarioAggregate(
            "visual-fixture",
            new[]
            {
                new ScenarioAttempt(
                    "visual-fixture",
                    1,
                    new ScenarioOutcome(ScenarioOutcomeKind.FailProduct, "visual defect")),
                new ScenarioAttempt("visual-fixture", 2, ScenarioOutcome.Pass),
            });

        Assert.Equal(ScenarioOutcomeKind.FlakeUnclassified, aggregate.FinalOutcome.Kind);
        Assert.Contains("first=FAIL_PRODUCT", aggregate.FinalOutcome.Reason, StringComparison.Ordinal);
    }
}
