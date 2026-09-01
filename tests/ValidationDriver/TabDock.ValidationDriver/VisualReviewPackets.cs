using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TabDock.ValidationDriver;

internal sealed record VisualReviewPacketBuildResult(
    VisualReviewPacket Packet,
    byte[] PacketBytes,
    byte[] InstructionsBytes,
    string PacketRelativePath,
    string InstructionsRelativePath,
    string ResultRelativePath,
    long PacketSerializationMilliseconds = 0,
    long InstructionsSerializationMilliseconds = 0);

/// <summary>Builds a provider-neutral, bounded packet from one attempt manifest.</summary>
internal static class VisualReviewPacketBuilder
{
    private const int MaximumImages = 32;
    private const int MaximumCheckpoints = 32;
    private const int MaximumFacts = 32;
    private const int MaximumNotes = 16;

    public static bool TryBuild(
        VisualEvidenceManifest manifest,
        IReadOnlyDictionary<string, string>? correlatedFacts,
        out VisualReviewPacketBuildResult? result,
        out string reason)
    {
        result = null;
        reason = string.Empty;
        try
        {
            manifest.Validate();
        }
        catch (ArgumentException ex)
        {
            reason = ex.Message;
            return false;
        }

        VisualArtifactRecord[] images = manifest.Artifacts
            .Where(artifact => !artifact.Derived && artifact.IncludeInReview)
            .OrderBy(artifact => artifact.RelativeMilliseconds)
            .ThenBy(artifact => artifact.Sequence)
            .ThenBy(artifact => artifact.ArtifactId, StringComparer.Ordinal)
            .Take(MaximumImages)
            .ToArray();
        if (images.Length == 0)
        {
            reason = "review packet requires at least one selected raw visual artifact";
            return false;
        }

        var checkpoints = new List<VisualReviewCheckpoint>();
        foreach (IGrouping<string, VisualArtifactRecord> group in images.GroupBy(item => item.CheckpointId, StringComparer.Ordinal)
                     .OrderBy(group => group.Min(item => item.RelativeMilliseconds))
                     .ThenBy(group => group.Key, StringComparer.Ordinal)
                     .Take(MaximumCheckpoints))
        {
            VisualArtifactRecord first = group
                .OrderBy(item => item.RelativeMilliseconds)
                .ThenBy(item => item.Sequence)
                .ThenBy(item => item.ArtifactId, StringComparer.Ordinal)
                .First();
            checkpoints.Add(new VisualReviewCheckpoint(
                group.Key,
                first.Phase,
                first.Expectation,
                group.Any(item => item.Requiredness == VisualCaptureRequiredness.REQUIRED)
                    ? VisualCaptureRequiredness.REQUIRED
                    : VisualCaptureRequiredness.BEST_EFFORT,
                group.Select(item => item.ArtifactId).ToArray()));
        }

        VisualArtifactRecord? contactSheet = manifest.Artifacts
            .Where(artifact => artifact.Derived && string.Equals(artifact.CheckpointId, "contact-sheet", StringComparison.Ordinal))
            .OrderBy(artifact => artifact.ArtifactId, StringComparer.Ordinal)
            .FirstOrDefault();
        string packetRoot = $"visual/{manifest.Scenario}/attempt-{manifest.Attempt:D3}/review";
        string packetPath = $"{packetRoot}/visual-review-manifest.json";
        string instructionsPath = $"{packetRoot}/visual-review-instructions.md";
        string resultPath = $"{packetRoot}/visual-review-result.json";
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["nativeOutcomeIsAuthoritative"] = "true",
            ["visualEvidenceLevel"] = manifest.Policy.Level.ToString().ToLowerInvariant(),
            ["rawImageCount"] = images.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["declaredArtifactCount"] = manifest.Artifacts.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (correlatedFacts != null)
        {
            foreach (KeyValuePair<string, string> fact in correlatedFacts.OrderBy(item => item.Key, StringComparer.Ordinal).Take(MaximumFacts))
            {
                if (!string.IsNullOrWhiteSpace(fact.Key) && !string.IsNullOrWhiteSpace(fact.Value))
                    facts[fact.Key] = Trim(fact.Value, 300);
            }
        }

        var environmentNotes = new[]
        {
            "The packet contains bounded evidence for one scenario attempt.",
            "Native identity, lease, foreground, geometry, cleanup, and candidate checks remain authoritative outside the images.",
        };
        var prohibited = new[]
        {
            "Images cannot prove process identity, HWND ownership, lease validity, cleanup correctness, or root cause.",
            "Do not infer unrelated desktop activity from a bounded target image.",
            "Describe visible symptoms before hypothesizing causes.",
            "Do not authorize production changes until findings are correlated with native, UIA, pixel, and timeline evidence.",
        };
        var packet = new VisualReviewPacket(
            VisualEvidenceSchema.ReviewPacket,
            VisualEvidenceSchema.CurrentVersion,
            manifest.CandidateSha,
            manifest.RunId,
            manifest.Scenario,
            manifest.Attempt,
            DateTimeOffset.UtcNow,
            $"visual/{manifest.Scenario}/attempt-{manifest.Attempt:D3}/manifest.json",
            checkpoints.ToArray(),
            images.Select(image => new VisualReviewImageReference(
                image.ArtifactId,
                image.CheckpointId,
                image.RelativePath,
                image.Sha256,
                image.RelativeMilliseconds,
                image.Derived)).ToArray(),
            contactSheet?.ArtifactId,
            contactSheet?.RelativePath,
            contactSheet?.Sha256,
            new Dictionary<string,string>(facts, StringComparer.Ordinal),
            environmentNotes.Take(MaximumNotes).ToArray(),
            prohibited,
            instructionsPath,
            resultPath,
            VisualEvidenceSchema.ReviewResult,
            manifest.DerivedArtifactFailures,
            manifest.TopologyBinding);
        packet.Validate();
        Stopwatch packetSerialization = Stopwatch.StartNew();
        byte[] packetBytes = JsonSerializer.SerializeToUtf8Bytes(packet, VisualJson.Options);
        packetSerialization.Stop();
        Stopwatch instructionsSerialization = Stopwatch.StartNew();
        byte[] instructionsBytes = Encoding.UTF8.GetBytes(BuildInstructions(packet));
        instructionsSerialization.Stop();
        long totalBytes = checked((long)packetBytes.Length + instructionsBytes.Length);
        if (totalBytes > manifest.Policy.MaxBytes)
        {
            reason = "review packet exceeds the configured byte budget";
            return false;
        }

        result = new VisualReviewPacketBuildResult(
            packet,
            packetBytes,
            instructionsBytes,
            packetPath,
            instructionsPath,
            resultPath,
            packetSerialization.ElapsedMilliseconds,
            instructionsSerialization.ElapsedMilliseconds);
        return true;
    }

    private static string BuildInstructions(VisualReviewPacket packet)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# TabDock visual review");
        builder.AppendLine();
        builder.AppendLine("Verify the packet and every referenced SHA-256 before inspecting pixels.");
        builder.AppendLine($"Scenario: `{packet.Scenario}` attempt `{packet.Attempt}`.");
        builder.AppendLine($"Packet schema: `{packet.Schema}`.");
        builder.AppendLine();
        builder.AppendLine("Review sequence:");
        builder.AppendLine("1. Open the contact sheet when present, then inspect suspicious raw PNGs at full resolution.");
        builder.AppendLine("2. Review checkpoints in chronological order against each declared expectation.");
        builder.AppendLine("3. Describe visible symptoms before proposing causes; distinguish capture artifacts from presentation defects.");
        builder.AppendLine("4. Correlate findings with native/UIA/pixel/timeline evidence. Images never establish process identity or cleanup.");
        builder.AppendLine("5. Write the required result JSON at the packet's `requiredResultPath`, binding every reviewed image ID and hash.");
        builder.AppendLine();
        builder.AppendLine("Required verdict: `VISUAL_OK`, `VISUAL_SUSPECT`, `VISUAL_DEFECT`, or `REVIEW_UNAVAILABLE`.");
        builder.AppendLine($"Required result schema: `{packet.RequiredResultSchema}`.");
        builder.AppendLine();
        builder.AppendLine("Checkpoint expectations:");
        foreach (VisualReviewCheckpoint checkpoint in packet.Checkpoints)
        {
            builder.Append("- `").Append(checkpoint.CheckpointId).Append("` ")
                .Append(checkpoint.Phase).Append(" — ").AppendLine(checkpoint.Expectation);
        }
        return builder.ToString();
    }

    private static string Trim(string value, int maximumLength)
        => value.Length <= maximumLength ? value : value[..maximumLength];
}
