using System;

namespace TabDock.ValidationDriver;

/// <summary>
/// Separates the visual-review dimension from native qualification. A visual
/// result may make a required gate non-pass, but it can never promote native
/// failure or lease failure to PASS.
/// </summary>
internal sealed record VisualQualificationDecision(
    ScenarioOutcome NativeOutcome,
    VisualReviewVerdict Verdict,
    bool ReviewRequired,
    bool VisualPass,
    ScenarioOutcome EffectiveOutcome,
    string Reason)
{
    public bool ReleasePass => EffectiveOutcome.IsReleasePass;
}

internal static class VisualQualificationGate
{
    public static VisualQualificationDecision Evaluate(
        ScenarioOutcome nativeOutcome,
        VisualReviewVerdict verdict,
        bool reviewRequired)
    {
        bool visualPass = verdict == VisualReviewVerdict.VISUAL_OK;
        if (!nativeOutcome.IsReleasePass)
        {
            return new VisualQualificationDecision(
                nativeOutcome,
                verdict,
                reviewRequired,
                visualPass,
                nativeOutcome,
                $"native outcome {nativeOutcome.Code} remains authoritative");
        }

        if (verdict == VisualReviewVerdict.VISUAL_OK)
        {
            return new VisualQualificationDecision(
                nativeOutcome,
                verdict,
                reviewRequired,
                true,
                nativeOutcome,
                "visual review accepted");
        }

        if (!reviewRequired)
        {
            return new VisualQualificationDecision(
                nativeOutcome,
                verdict,
                reviewRequired,
                false,
                nativeOutcome,
                $"optional visual review is {verdict}");
        }

        ScenarioOutcome effective = verdict switch
        {
            VisualReviewVerdict.REVIEW_UNAVAILABLE => new ScenarioOutcome(
                ScenarioOutcomeKind.BlockedCapability,
                "required capable visual review is unavailable"),
            VisualReviewVerdict.VISUAL_SUSPECT or VisualReviewVerdict.VISUAL_DEFECT => new ScenarioOutcome(
                ScenarioOutcomeKind.FailProduct,
                $"required visual review returned {verdict}"),
            _ => new ScenarioOutcome(
                ScenarioOutcomeKind.FailHarness,
                "visual review returned an unsupported verdict"),
        };
        return new VisualQualificationDecision(
            nativeOutcome,
            verdict,
            reviewRequired,
            false,
            effective,
            effective.Reason ?? effective.Code);
    }
}
