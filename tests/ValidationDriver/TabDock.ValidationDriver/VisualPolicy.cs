using System;

namespace TabDock.ValidationDriver;

internal static class VisualPolicyResolver
{
    public static bool TryParseLevel(string value, out VisualEvidenceLevel level)
    {
        level = value.Trim().ToLowerInvariant() switch
        {
            "none" => VisualEvidenceLevel.NONE,
            "failure" => VisualEvidenceLevel.FAILURE_ONLY,
            "checkpoints" => VisualEvidenceLevel.CHECKPOINTS,
            "flight" => VisualEvidenceLevel.FLIGHT_RECORDER,
            _ => (VisualEvidenceLevel)(-1),
        };
        return level != (VisualEvidenceLevel)(-1);
    }

    public static VisualEvidencePolicy Resolve(Options options)
    {
        if (options.VisualEvidence == VisualEvidenceLevel.NONE)
        {
            if (options.VisualReviewPacket || options.VisualMaxBytes.HasValue)
                throw new ArgumentException("--visual-review-packet and --visual-max-bytes require --visual-evidence failure|checkpoints|flight.");
            return VisualEvidencePolicy.Disabled;
        }

        VisualEvidencePolicy policy = VisualEvidencePolicy.SafeDefaults(
            options.VisualEvidence,
            options.VisualReviewPacket);
        if (options.VisualMaxBytes is long maxBytes)
            policy = policy.WithBudgets(maxBytes, policy.MaxArtifacts);
        policy.Validate();
        return policy;
    }

    public static string Describe(VisualEvidencePolicy policy)
        => policy.Level switch
        {
            VisualEvidenceLevel.NONE => "none",
            VisualEvidenceLevel.FAILURE_ONLY => $"failure maxBytes={policy.MaxBytes} maxArtifacts={policy.MaxArtifacts}",
            VisualEvidenceLevel.CHECKPOINTS => $"checkpoints maxBytes={policy.MaxBytes} maxArtifacts={policy.MaxArtifacts}",
            VisualEvidenceLevel.FLIGHT_RECORDER => $"flight maxBytes={policy.MaxBytes} maxArtifacts={policy.MaxArtifacts} ringFrames={policy.RingMaxFrames} ringFps={policy.RingMaxFramesPerSecond:0.###} ringDurationMs={policy.RingDurationMilliseconds}",
            _ => "invalid",
        };
    public static string ToCliValue(VisualEvidenceLevel level)
        => level switch
        {
            VisualEvidenceLevel.NONE => "none",
            VisualEvidenceLevel.FAILURE_ONLY => "failure",
            VisualEvidenceLevel.CHECKPOINTS => "checkpoints",
            VisualEvidenceLevel.FLIGHT_RECORDER => "flight",
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };
}
