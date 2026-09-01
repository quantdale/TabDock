using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace TabDock.ValidationDriver;

internal sealed record VisualReviewVerificationResult(
    bool Valid,
    IReadOnlyList<string> Failures);

/// <summary>Offline verifier for packet/result identity and referenced image bytes.</summary>

internal static class VisualReviewVerifier
{
    public static VisualReviewVerificationResult VerifyFiles(
        string artifactRoot,
        string packetRelativePath,
        string resultRelativePath,
        bool requireAllCheckpoints = true,
        bool requirePhysicalTopology = false,
        VisualTopologyBinding? expectedTopology = null)
    {
        var failures = new List<string>();
        try
        {
            var paths = new VisualPathPolicy(artifactRoot);
            string normalizedPacketPath = paths.NormalizeRelative(packetRelativePath);
            string packetPath = paths.Resolve(normalizedPacketPath);
            string resultPath = paths.Resolve(paths.NormalizeRelative(resultRelativePath));
            if (!File.Exists(packetPath))
            {
                failures.Add($"review packet is missing: {packetRelativePath}");
                return new VisualReviewVerificationResult(false, failures);
            }
            if (!File.Exists(resultPath))
            {
                failures.Add($"review result is missing: {resultRelativePath}");
                return new VisualReviewVerificationResult(false, failures);
            }

            byte[] packetBytes = File.ReadAllBytes(packetPath);
            VisualReviewPacket packet = VisualJson.Deserialize<VisualReviewPacket>(packetBytes);
            VisualReviewResult review = VisualJson.Deserialize<VisualReviewResult>(
                File.ReadAllBytes(resultPath));
            return VerifyLoaded(
                artifactRoot,
                normalizedPacketPath,
                packetBytes,
                packet,
                review,
                requireAllCheckpoints,
                requirePhysicalTopology,
                expectedTopology);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or JsonException)
        {
            failures.Add($"review files could not be verified: {ex.GetType().Name}: {ex.Message}");
            return new VisualReviewVerificationResult(false, failures);
        }
    }

    public static VisualReviewVerificationResult Verify(
        string artifactRoot,
        string packetRelativePath,
        VisualReviewResult review,
        bool requireAllCheckpoints = true,
        bool requirePhysicalTopology = false,
        VisualTopologyBinding? expectedTopology = null)
    {
        var failures = new List<string>();
        try
        {
            var paths = new VisualPathPolicy(artifactRoot);
            string normalizedPacketPath = paths.NormalizeRelative(packetRelativePath);
            string packetPath = paths.Resolve(normalizedPacketPath);
            if (!File.Exists(packetPath))
            {
                failures.Add($"review packet is missing: {packetRelativePath}");
                return new VisualReviewVerificationResult(false, failures);
            }
            byte[] packetBytes = File.ReadAllBytes(packetPath);
            VisualReviewPacket packet = VisualJson.Deserialize<VisualReviewPacket>(packetBytes);
            return VerifyLoaded(
                artifactRoot,
                normalizedPacketPath,
                packetBytes,
                packet,
                review,
                requireAllCheckpoints,
                requirePhysicalTopology,
                expectedTopology);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or JsonException)
        {
            failures.Add($"review packet could not be verified: {ex.GetType().Name}: {ex.Message}");
            return new VisualReviewVerificationResult(false, failures);
        }
    }

    private static VisualReviewVerificationResult VerifyLoaded(
        string artifactRoot,
        string normalizedPacketPath,
        byte[] packetBytes,
        VisualReviewPacket packet,
        VisualReviewResult? review,
        bool requireAllCheckpoints,
        bool requirePhysicalTopology,
        VisualTopologyBinding? expectedTopology)
    {
        var failures = new List<string>();
        string packetSha256 = Convert.ToHexString(SHA256.HashData(packetBytes)).ToLowerInvariant();
        if (!IsSha256(packetSha256))
            failures.Add("review packet hash is malformed");
        if (!string.Equals(normalizedPacketPath, ExpectedPacketPath(packet), StringComparison.Ordinal))
            failures.Add("review packet path disagrees with packet identity");

        bool packetValid = true;
        try
        {
            packet.Validate();
        }
        catch (ArgumentException ex)
        {
            packetValid = false;
            failures.Add(ex.Message);
        }
        if (!packetValid)
            return new VisualReviewVerificationResult(false, failures);

        if (review is null)
        {
            failures.Add("review result is empty");
            return new VisualReviewVerificationResult(false, failures);
        }
        if (!IsSha256(review.PacketSha256))
            failures.Add("review result packet hash is empty or malformed");

        bool reviewValid = true;
        try
        {
            review.Validate();
        }
        catch (ArgumentException ex)
        {
            reviewValid = false;
            failures.Add(ex.Message);
        }
        if (!reviewValid)
            return new VisualReviewVerificationResult(false, failures);

        if (!string.Equals(review.PacketSha256, packetSha256, StringComparison.OrdinalIgnoreCase))
            failures.Add("review result packet hash disagrees with packet bytes");
        if (!string.Equals(review.CandidateSha, packet.CandidateSha, StringComparison.Ordinal)
            || !string.Equals(review.RunId, packet.RunId, StringComparison.Ordinal)
            || !string.Equals(review.Scenario, packet.Scenario, StringComparison.Ordinal)
            || review.Attempt != packet.Attempt)
        {
            failures.Add("review result identity disagrees with packet identity");
        }

        VisualPathPolicy? paths = null;
        VisualEvidenceManifest? manifest = null;
        try
        {
            paths = new VisualPathPolicy(artifactRoot);
            VerifyManifestAndPacketImages(
                paths,
                packet,
                packetSha256,
                failures,
                out manifest);
        }
        catch (ArgumentException ex)
        {
            failures.Add(ex.Message);
        }
        VerifyTopologyBindings(
            manifest,
            packet,
            review,
            requirePhysicalTopology || expectedTopology is not null,
            expectedTopology,
            failures);
        if (paths != null)
            VerifyReviewedImages(paths, packet, review, failures);
        VerifyFindings(packet, review, failures);
        VerifyDerivedFailureBindings(manifest, packet, review, failures);

        if (review.Verdict == VisualReviewVerdict.REVIEW_UNAVAILABLE)
        {
            if (review.ReviewedImages.Length != 0 || review.Findings.Length != 0)
                failures.Add("REVIEW_UNAVAILABLE result cannot contain reviewed images or findings");
            if (string.IsNullOrWhiteSpace(review.Notes))
                failures.Add("REVIEW_UNAVAILABLE result requires a capability note");
        }
        else if (requireAllCheckpoints)
        {
            var reviewed = review.ReviewedImages
                .Select(image => image.CheckpointId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (VisualReviewCheckpoint checkpoint in packet.Checkpoints)
            {
                if (!reviewed.Contains(checkpoint.CheckpointId))
                    failures.Add($"review omits required checkpoint '{checkpoint.CheckpointId}'");
            }
        }
        if (review.Verdict is VisualReviewVerdict.VISUAL_SUSPECT or VisualReviewVerdict.VISUAL_DEFECT
            && review.Findings.Length == 0)
        {
            failures.Add("non-OK visual verdict requires at least one finding");
        }
        if (review.Verdict == VisualReviewVerdict.VISUAL_OK && review.Findings.Length > 0)
            failures.Add("VISUAL_OK review cannot contain defect findings");
        return new VisualReviewVerificationResult(failures.Count == 0, failures);
    }

    private static void VerifyManifestAndPacketImages(
        VisualPathPolicy paths,
        VisualReviewPacket packet,
        string packetSha256,
        List<string> failures,
        out VisualEvidenceManifest? manifest)
    {
        manifest = null;
        string manifestPath;
        try
        {
            manifestPath = paths.Resolve(paths.NormalizeRelative(packet.VisualManifestPath));
        }
        catch (ArgumentException ex)
        {
            failures.Add(ex.Message);
            return;
        }
        if (!File.Exists(manifestPath))
        {
            failures.Add($"visual manifest is missing: {packet.VisualManifestPath}");
        }
        else
        {
            try
            {
                manifest = VisualJson.Deserialize<VisualEvidenceManifest>(
                    File.ReadAllBytes(manifestPath));
                manifest.Validate();
                if (!string.Equals(manifest.CandidateSha, packet.CandidateSha, StringComparison.Ordinal)
                    || !string.Equals(manifest.RunId, packet.RunId, StringComparison.Ordinal)
                    || !string.Equals(manifest.Scenario, packet.Scenario, StringComparison.Ordinal)
                    || manifest.Attempt != packet.Attempt)
                {
                    failures.Add("visual manifest identity disagrees with review packet");
                }
                if (!string.Equals(manifest.ReviewPacketPath, ExpectedPacketPath(packet), StringComparison.Ordinal)
                    || !string.Equals(manifest.ReviewPacketSha256, packetSha256, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add("visual manifest review-packet binding is inconsistent");
                }
                var manifestArtifacts = manifest.Artifacts.ToDictionary(
                    item => item.ArtifactId,
                    StringComparer.Ordinal);
                foreach (VisualArtifactRecord artifact in manifest.Artifacts)
                {
                    VerifyFile(paths, artifact.RelativePath, artifact.Sha256, artifact.ArtifactId, failures);
                }
                foreach (VisualReviewImageReference image in packet.Images)
                {
                    if (!manifestArtifacts.TryGetValue(image.ArtifactId, out VisualArtifactRecord? artifact)
                        || !string.Equals(artifact.RelativePath, image.RelativePath, StringComparison.Ordinal)
                        || !string.Equals(artifact.Sha256, image.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        failures.Add($"visual manifest image binding disagrees for '{image.ArtifactId}'");
                    }
                }
                if (packet.ContactSheetArtifactId != null
                    && (!manifestArtifacts.TryGetValue(packet.ContactSheetArtifactId, out VisualArtifactRecord? contact)
                        || !string.Equals(contact.RelativePath, packet.ContactSheetPath, StringComparison.Ordinal)
                        || !string.Equals(contact.Sha256, packet.ContactSheetSha256, StringComparison.OrdinalIgnoreCase)
                        || !contact.Derived))
                {
                    failures.Add("visual manifest contact-sheet binding is inconsistent");
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or JsonException)
            {
                manifest = null;
                failures.Add($"visual manifest could not be verified: {ex.GetType().Name}");
            }
        }

        var imageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (VisualReviewImageReference image in packet.Images)
        {
            if (!imageIds.Add(image.ArtifactId))
                failures.Add($"review packet duplicates artifact '{image.ArtifactId}'");
            VerifyFile(paths, image.RelativePath, image.Sha256, image.ArtifactId, failures);
        }
        if (packet.ContactSheetPath != null && packet.ContactSheetSha256 != null)
            VerifyFile(paths, packet.ContactSheetPath, packet.ContactSheetSha256, "contact-sheet", failures);
    }

    private static void VerifyReviewedImages(
        VisualPathPolicy paths,
        VisualReviewPacket packet,
        VisualReviewResult review,
        List<string> failures)
    {
        var packetImages = packet.Images.ToDictionary(image => image.ArtifactId, StringComparer.Ordinal);
        foreach (VisualReviewReviewedImage image in review.ReviewedImages)
        {
            if (!packetImages.TryGetValue(image.ArtifactId, out VisualReviewImageReference? expected))
            {
                failures.Add($"review references unknown artifact '{image.ArtifactId}'");
                continue;
            }
            if (!string.Equals(image.CheckpointId, expected.CheckpointId, StringComparison.Ordinal)
                || !string.Equals(image.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"review image binding disagrees for '{image.ArtifactId}'");
            }
            VerifyFile(paths, expected.RelativePath, image.Sha256, image.ArtifactId, failures);
        }
    }

    private static void VerifyFindings(
        VisualReviewPacket packet,
        VisualReviewResult review,
        List<string> failures)
    {
        var images = packet.Images.ToDictionary(image => image.ArtifactId, StringComparer.Ordinal);
        foreach (VisualReviewFinding finding in review.Findings)
        {
            if (!images.TryGetValue(finding.ArtifactId, out VisualReviewImageReference? image))
            {
                failures.Add($"finding references unknown artifact '{finding.ArtifactId}'");
                continue;
            }
            if (!string.Equals(finding.CheckpointId, image.CheckpointId, StringComparison.Ordinal)
                || !string.Equals(finding.ImageSha256, image.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"finding image binding disagrees for '{finding.FindingId}'");
            }
            if (!review.ReviewedImages.Any(item => string.Equals(item.ArtifactId, finding.ArtifactId, StringComparison.Ordinal)))
                failures.Add($"finding '{finding.FindingId}' references an image that was not reviewed");
        }
    }

    private static void VerifyDerivedFailureBindings(
        VisualEvidenceManifest? manifest,
        VisualReviewPacket packet,
        VisualReviewResult review,
        List<string> failures)
    {
        if (manifest is null)
            return;

        var manifestFailures = manifest.DerivedArtifactFailures.ToDictionary(
            failure => failure.FailureId,
            StringComparer.Ordinal);
        var packetFailures = packet.DerivedArtifactFailures.ToDictionary(
            failure => failure.FailureId,
            StringComparer.Ordinal);
        if (manifestFailures.Count != packetFailures.Count)
            failures.Add("derived artifact failure count disagrees between manifest and packet");

        foreach (KeyValuePair<string, VisualDerivedArtifactFailure> pair in manifestFailures)
        {
            if (!packetFailures.TryGetValue(pair.Key, out VisualDerivedArtifactFailure? packetFailure)
                || !Equivalent(pair.Value, packetFailure))
            {
                failures.Add($"derived artifact failure binding disagrees for '{pair.Key}'");
            }
            var sourceIds = manifest.Artifacts
                .Select(artifact => artifact.ArtifactId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (string sourceArtifactId in pair.Value.SourceArtifactIds)
            {
                if (!sourceIds.Contains(sourceArtifactId))
                    failures.Add($"derived artifact failure '{pair.Key}' references unknown source '{sourceArtifactId}'");
            }
            bool acknowledged = review.AcknowledgedDerivedFailureIds.Contains(
                pair.Key,
                StringComparer.Ordinal);
            if (review.Verdict != VisualReviewVerdict.REVIEW_UNAVAILABLE && !acknowledged)
                failures.Add($"derived artifact failure '{pair.Key}' is not acknowledged");
            if (review.Verdict == VisualReviewVerdict.VISUAL_OK
                && (pair.Value.Requiredness == VisualCaptureRequiredness.REQUIRED
                    || !pair.Value.RawArtifactsPreserved))
            {
                failures.Add($"VISUAL_OK cannot accept derived artifact failure '{pair.Key}'");
            }
        }
        foreach (string failureId in packetFailures.Keys)
        {
            if (!manifestFailures.ContainsKey(failureId))
                failures.Add($"packet contains unknown derived artifact failure '{failureId}'");
        }
        foreach (string failureId in review.AcknowledgedDerivedFailureIds)
        {
            if (!manifestFailures.ContainsKey(failureId))
                failures.Add($"review acknowledges unknown derived artifact failure '{failureId}'");
        }

        static bool Equivalent(VisualDerivedArtifactFailure left, VisualDerivedArtifactFailure right)
            => string.Equals(left.FailureId, right.FailureId, StringComparison.Ordinal)
                && string.Equals(left.ArtifactKind, right.ArtifactKind, StringComparison.Ordinal)
                && string.Equals(left.ArtifactId, right.ArtifactId, StringComparison.Ordinal)
                && string.Equals(left.CheckpointId, right.CheckpointId, StringComparison.Ordinal)
                && string.Equals(left.Scenario, right.Scenario, StringComparison.Ordinal)
                && left.Attempt == right.Attempt
                && string.Equals(left.Reason, right.Reason, StringComparison.Ordinal)
                && left.Requiredness == right.Requiredness
                && left.RawArtifactsPreserved == right.RawArtifactsPreserved
                && left.SourceArtifactIds.SequenceEqual(right.SourceArtifactIds, StringComparer.Ordinal)
                && left.RecordedUtc == right.RecordedUtc;
    }
    private static void VerifyTopologyBindings(
        VisualEvidenceManifest? manifest,
        VisualReviewPacket packet,
        VisualReviewResult review,
        bool requirePhysicalTopology,
        VisualTopologyBinding? expectedTopology,
        List<string> failures)
    {
        VisualTopologyBinding? packetBinding = packet.TopologyBinding;
        bool physicalRequired = requirePhysicalTopology || expectedTopology is not null;
        if (physicalRequired)
        {
            if (packetBinding is null)
                failures.Add("physical visual verification requires a topology binding");
            else if (!packetBinding.IsPhysicalEligible)
                failures.Add("synthetic or non-physical topology cannot satisfy physical visual verification");
        }

        if (packetBinding is not null)
        {
            try
            {
                packetBinding.Validate();
            }
            catch (ArgumentException ex)
            {
                failures.Add(ex.Message);
            }
            if (!string.Equals(packetBinding.CandidateSha, packet.CandidateSha, StringComparison.Ordinal)
                || !string.Equals(packetBinding.RunId, packet.RunId, StringComparison.Ordinal)
                || !string.Equals(packetBinding.Scenario, packet.Scenario, StringComparison.Ordinal)
                || packetBinding.Attempt != packet.Attempt)
            {
                failures.Add("visual packet topology binding disagrees with packet identity");
            }
        }

        if (manifest is not null)
        {
            if ((manifest.TopologyBinding is null) != (packetBinding is null))
            {
                failures.Add("visual manifest and packet topology-binding presence disagrees");
            }
            else if (manifest.TopologyBinding is not null
                && !manifest.TopologyBinding.MatchesAttempt(packetBinding!))
            {
                failures.Add("visual manifest and packet topology bindings disagree");
            }

            foreach (VisualArtifactRecord artifact in manifest.Artifacts)
            {
                if (packetBinding is null)
                {
                    if (artifact.TopologyBinding is not null)
                        failures.Add($"visual artifact '{artifact.ArtifactId}' has an unexpected topology binding");
                    continue;
                }
                if (artifact.TopologyBinding is null)
                {
                    failures.Add($"visual artifact '{artifact.ArtifactId}' is missing its topology binding");
                    continue;
                }
                try
                {
                    artifact.TopologyBinding.Validate(requireMonitor: !artifact.Derived);
                }
                catch (ArgumentException ex)
                {
                    failures.Add(ex.Message);
                }
                if (!artifact.TopologyBinding.MatchesAttempt(packetBinding))
                    failures.Add($"visual artifact '{artifact.ArtifactId}' topology identity disagrees");
            }
        }

        if (expectedTopology is not null)
        {
            try
            {
                expectedTopology.Validate();
                if (!expectedTopology.IsPhysicalEligible)
                    failures.Add("expected visual topology binding is synthetic or non-physical");
            }
            catch (ArgumentException ex)
            {
                failures.Add(ex.Message);
            }
            if (packetBinding is null || !packetBinding.MatchesAttempt(expectedTopology))
                failures.Add("visual packet topology binding disagrees with expected topology");
        }

        if (review.TopologyBinding is null)
        {
            if (packetBinding is not null)
                failures.Add("visual review result is missing its topology binding");
        }
        else
        {
            try
            {
                review.TopologyBinding.Validate();
            }
            catch (ArgumentException ex)
            {
                failures.Add(ex.Message);
            }
            if (packetBinding is null
                || !review.TopologyBinding.MatchesAttempt(packetBinding))
            {
                failures.Add("visual review result topology binding disagrees with packet");
            }
        }
    }

    private static void VerifyFile(
        VisualPathPolicy paths,
        string relativePath,
        string expectedSha256,
        string label,
        List<string> failures)
    {
        try
        {
            string normalized = paths.NormalizeRelative(relativePath);
            string fullPath = paths.Resolve(normalized);
            if (!File.Exists(fullPath))
            {
                failures.Add($"visual evidence file is missing for '{label}': {normalized}");
                return;
            }
            string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))).ToLowerInvariant();
            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                failures.Add($"visual evidence hash mismatch for '{label}'");
        }
        catch (ArgumentException ex)
        {
            failures.Add(ex.Message);
        }
        catch (IOException ex)
        {
            failures.Add($"visual evidence file could not be read for '{label}': {ex.GetType().Name}");
        }
    }

    private static string ExpectedPacketPath(VisualReviewPacket packet)
        => $"visual/{packet.Scenario}/attempt-{packet.Attempt:D3}/review/visual-review-manifest.json";

    private static bool IsSha256(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length == 64
            && value.All(Uri.IsHexDigit);
}
