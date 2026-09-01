using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TabDock.ValidationDriver;
using Xunit;

namespace TabDock.UnitTests;

public sealed class VisualReviewVerifierTests
{
    private static readonly VisualTargetIdentity Target = new(
        "0x10", 10, 20, "TabDock.Guest", 100, "GuestWindow", "OwnedWindow");

    [Fact]
    public void ValidReview_BindsPacketImagesAndManifestFiles()
    {
        string root = CreateTempRoot();
        try
        {
            (VisualEvidenceRecorder recorder, VisualReviewPacket packet, VisualStoredArtifact storedPacket) = Prepare(root);
            VisualReviewImageReference image = packet.Images[0];
            var review = new VisualReviewResult(
                VisualEvidenceSchema.ReviewResult,
                VisualEvidenceSchema.CurrentVersion,
                storedPacket.Sha256,
                packet.CandidateSha,
                packet.RunId,
                packet.Scenario,
                packet.Attempt,
                VisualReviewVerdict.VISUAL_OK,
                "AI_AGENT",
                "fixture-reviewer",
                DateTimeOffset.UtcNow,
                new[] { new VisualReviewReviewedImage(image.ArtifactId, image.CheckpointId, image.Sha256) },
                Array.Empty<VisualReviewFinding>(),
                "All reviewed checkpoints match their declared expectations.",
                Array.Empty<string>());

            VisualReviewVerificationResult verification = VisualReviewVerifier.Verify(
                root,
                $"visual/{packet.Scenario}/attempt-{packet.Attempt:D3}/review/visual-review-manifest.json",
                review);

            Assert.True(verification.Valid, string.Join("; ", verification.Failures));
            Assert.Empty(verification.Failures);
            string resultPath = Path.Combine(root, packet.RequiredResultPath.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(resultPath, JsonSerializer.SerializeToUtf8Bytes(review, VisualJson.Options));
            VisualReviewVerificationResult fileVerification = VisualReviewVerifier.VerifyFiles(
                root,
                $"visual/{packet.Scenario}/attempt-{packet.Attempt:D3}/review/visual-review-manifest.json",
                packet.RequiredResultPath);
            Assert.True(fileVerification.Valid, string.Join("; ", fileVerification.Failures));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ReviewUnavailable_AllowsEmptyReviewCollectionsButRemainsExplicit()
    {
        string root = CreateTempRoot();
        try
        {
            (VisualEvidenceRecorder recorder, VisualReviewPacket packet, VisualStoredArtifact storedPacket) = Prepare(root);
            VisualReviewResult unavailable = new(
                VisualEvidenceSchema.ReviewResult,
                VisualEvidenceSchema.CurrentVersion,
                storedPacket.Sha256,
                packet.CandidateSha,
                packet.RunId,
                packet.Scenario,
                packet.Attempt,
                VisualReviewVerdict.REVIEW_UNAVAILABLE,
                "non-vision-test",
                "capability-fixture",
                DateTimeOffset.UtcNow,
                Array.Empty<VisualReviewReviewedImage>(),
                Array.Empty<VisualReviewFinding>(),
                "No capable image reviewer was available.",
                Array.Empty<string>());

            VisualReviewVerificationResult verification = VisualReviewVerifier.Verify(
                root,
                $"visual/{packet.Scenario}/attempt-{packet.Attempt:D3}/review/visual-review-manifest.json",
                unavailable);

            Assert.True(verification.Valid, string.Join("; ", verification.Failures));

            unavailable = unavailable with
            {
                ReviewedImages = new[]
                {
                    new VisualReviewReviewedImage(
                        packet.Images[0].ArtifactId,
                        packet.Images[0].CheckpointId,
                        packet.Images[0].Sha256),
                },
            };
            VisualReviewVerificationResult nonEmpty = VisualReviewVerifier.Verify(
                root,
                $"visual/{packet.Scenario}/attempt-{packet.Attempt:D3}/review/visual-review-manifest.json",
                unavailable);

            Assert.False(nonEmpty.Valid);
            Assert.Contains(nonEmpty.Failures, failure => failure.Contains("REVIEW_UNAVAILABLE", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void TamperedImage_IsRejectedEvenWhenReviewHashIsUnchanged()
    {
        string root = CreateTempRoot();
        try
        {
            (VisualEvidenceRecorder recorder, VisualReviewPacket packet, VisualStoredArtifact storedPacket) = Prepare(root);
            string imagePath = Path.Combine(root, packet.Images[0].RelativePath.Replace('/', Path.DirectorySeparatorChar));
            byte[] bytes = File.ReadAllBytes(imagePath);
            bytes[^1] ^= 0x01;
            File.WriteAllBytes(imagePath, bytes);
            VisualReviewImageReference image = packet.Images[0];
            var review = CreateReview(packet, storedPacket.Sha256, image);

            VisualReviewVerificationResult verification = VisualReviewVerifier.Verify(
                root,
                $"visual/{packet.Scenario}/attempt-{packet.Attempt:D3}/review/visual-review-manifest.json",
                review);

            Assert.False(verification.Valid);
            Assert.Contains(verification.Failures, failure => failure.Contains("hash mismatch", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void StaleOrIncompleteReview_IsRejected()
    {
        string root = CreateTempRoot();
        try
        {
            (VisualEvidenceRecorder recorder, VisualReviewPacket packet, VisualStoredArtifact storedPacket) = Prepare(root);
            var stale = new VisualReviewResult(
                VisualEvidenceSchema.ReviewResult,
                VisualEvidenceSchema.CurrentVersion,
                storedPacket.Sha256,
                "different-candidate",
                packet.RunId,
                packet.Scenario,
                packet.Attempt,
                VisualReviewVerdict.VISUAL_OK,
                "HUMAN",
                "reviewer",
                DateTimeOffset.UtcNow,
                Array.Empty<VisualReviewReviewedImage>(),
                Array.Empty<VisualReviewFinding>(),
                "No images were reviewed.",
                Array.Empty<string>());

            VisualReviewVerificationResult verification = VisualReviewVerifier.Verify(
                root,
                $"visual/{packet.Scenario}/attempt-{packet.Attempt:D3}/review/visual-review-manifest.json",
                stale);

            Assert.False(verification.Valid);
            Assert.Contains(verification.Failures, failure => failure.Contains("identity", StringComparison.Ordinal));
            Assert.Contains(verification.Failures, failure => failure.Contains("omits required checkpoint", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }


    [Fact]
    public void CurrentVisualCollections_MissingOrNullFailDuringDeserialization()
    {
        string root = CreateTempRoot();
        try
        {
            (VisualEvidenceRecorder recorder, VisualReviewPacket packet, VisualStoredArtifact storedPacket) = Prepare(root);
            string packetPath = Path.Combine(root, $"visual/{packet.Scenario}/attempt-{packet.Attempt:D3}/review/visual-review-manifest.json")
                .Replace('/', Path.DirectorySeparatorChar);
            byte[] packetBytes = File.ReadAllBytes(packetPath);

            JsonObject packetWithoutImages = JsonNode.Parse(packetBytes)!.AsObject();
            packetWithoutImages.Remove("images");
            Assert.Throws<JsonException>(() => VisualJson.Deserialize<VisualReviewPacket>(
                JsonSerializer.SerializeToUtf8Bytes(packetWithoutImages, VisualJson.Options)));

            JsonObject packetWithNullFailures = JsonNode.Parse(packetBytes)!.AsObject();
            packetWithNullFailures["derivedArtifactFailures"] = null;
            Assert.Throws<JsonException>(() => VisualJson.Deserialize<VisualReviewPacket>(
                JsonSerializer.SerializeToUtf8Bytes(packetWithNullFailures, VisualJson.Options)));

            string manifestPath = Path.Combine(root, packet.VisualManifestPath.Replace('/', Path.DirectorySeparatorChar));
            JsonObject manifestWithNullFailures = JsonNode.Parse(File.ReadAllBytes(manifestPath))!.AsObject();
            manifestWithNullFailures["derivedArtifactFailures"] = null;
            Assert.Throws<JsonException>(() => VisualJson.Deserialize<VisualEvidenceManifest>(
                JsonSerializer.SerializeToUtf8Bytes(manifestWithNullFailures, VisualJson.Options)));

            VisualReviewImageReference image = packet.Images[0];
            VisualReviewResult review = CreateReview(packet, storedPacket.Sha256, image);
            JsonObject resultWithoutAcknowledgements = JsonNode.Parse(
                JsonSerializer.SerializeToUtf8Bytes(review, VisualJson.Options))!.AsObject();
            resultWithoutAcknowledgements.Remove("acknowledgedDerivedFailureIds");
            Assert.Throws<JsonException>(() => VisualJson.Deserialize<VisualReviewResult>(
                JsonSerializer.SerializeToUtf8Bytes(resultWithoutAcknowledgements, VisualJson.Options)));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PacketHash_IsComputedFromOnDiskBytes()
    {
        string root = CreateTempRoot();
        try
        {
            (VisualEvidenceRecorder recorder, VisualReviewPacket packet, VisualStoredArtifact storedPacket) = Prepare(root);
            VisualReviewResult review = CreateReview(packet, storedPacket.Sha256, packet.Images[0]);
            string packetRelativePath = $"visual/{packet.Scenario}/attempt-{packet.Attempt:D3}/review/visual-review-manifest.json";
            string packetPath = Path.Combine(root, packetRelativePath.Replace('/', Path.DirectorySeparatorChar));
            JsonObject packetObject = JsonNode.Parse(File.ReadAllBytes(packetPath))!.AsObject();
            var changedNotes = new JsonArray();
            changedNotes.Add("changed after review");
            packetObject["environmentNotes"] = changedNotes;
            File.WriteAllBytes(packetPath, JsonSerializer.SerializeToUtf8Bytes(packetObject, VisualJson.Options));

            VisualReviewVerificationResult verification = VisualReviewVerifier.Verify(
                root,
                packetRelativePath,
                review);

            Assert.False(verification.Valid);
            Assert.Contains(verification.Failures, failure => failure.Contains("packet hash", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void EmptyPacketHash_IsRejectedAsPacketBindingFailure()
    {
        string root = CreateTempRoot();
        try
        {
            (VisualEvidenceRecorder recorder, VisualReviewPacket packet, VisualStoredArtifact storedPacket) = Prepare(root);
            VisualReviewResult review = CreateReview(packet, string.Empty, packet.Images[0]);

            VisualReviewVerificationResult verification = VisualReviewVerifier.Verify(
                root,
                $"visual/{packet.Scenario}/attempt-{packet.Attempt:D3}/review/visual-review-manifest.json",
                review);

            Assert.False(verification.Valid);
            Assert.Contains(verification.Failures, failure => failure.Contains("packet hash", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void DerivedFailure_MustBeAcknowledgedAndMustMatchManifest()
    {
        string root = CreateTempRoot();
        try
        {
            (VisualEvidenceRecorder recorder, VisualReviewPacket packet, VisualStoredArtifact storedPacket) = Prepare(root, derivedFailure: true);
            VisualDerivedArtifactFailure failure = Assert.Single(packet.DerivedArtifactFailures);
            VisualReviewResult review = CreateReview(packet, storedPacket.Sha256, packet.Images[0]);
            string packetRelativePath = $"visual/{packet.Scenario}/attempt-{packet.Attempt:D3}/review/visual-review-manifest.json";

            VisualReviewVerificationResult unacknowledged = VisualReviewVerifier.Verify(
                root,
                packetRelativePath,
                review);
            Assert.False(unacknowledged.Valid);
            Assert.Contains(unacknowledged.Failures, item => item.Contains("not acknowledged", StringComparison.Ordinal));

            review = review with
            {
                AcknowledgedDerivedFailureIds = new[] { failure.FailureId },
            };
            VisualReviewVerificationResult acknowledged = VisualReviewVerifier.Verify(
                root,
                packetRelativePath,
                review);
            Assert.True(acknowledged.Valid, string.Join("; ", acknowledged.Failures));

            string manifestPath = Path.Combine(root, packet.VisualManifestPath.Replace('/', Path.DirectorySeparatorChar));
            JsonObject manifestObject = JsonNode.Parse(File.ReadAllBytes(manifestPath))!.AsObject();
            JsonArray failures = manifestObject["derivedArtifactFailures"]!.AsArray();
            failures[0]!["reason"] = "tampered";
            File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifestObject, VisualJson.Options));
            VisualReviewVerificationResult tampered = VisualReviewVerifier.Verify(
                root,
                packetRelativePath,
                review);
            Assert.False(tampered.Valid);
            Assert.Contains(tampered.Failures, item => item.Contains("derived artifact failure binding", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }
    private static (VisualEvidenceRecorder Recorder, VisualReviewPacket Packet, VisualStoredArtifact PacketArtifact) Prepare(
        string root,
        bool derivedFailure = false)
    {
        VisualEvidencePolicy policy = VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.CHECKPOINTS);
        if (derivedFailure)
        {
            policy = policy with { MaxHeight = 100 };
            policy.Validate();
        }
        var recorder = new VisualEvidenceRecorder(
            policy,
            root,
            "scenario",
            1,
            new FakeProvider());
        VisualCaptureScope scope = VisualCaptureScope.ForWindow(
            VisualCaptureScopeKind.GUEST_WINDOW,
            Target,
            VisualPrivacyClass.TEST_OWNED);
        recorder.Checkpoint(new VisualCheckpointRequest(
            "baseline",
            VisualCheckpointPhase.BASELINE,
            "The guest is visible.",
            new[] { scope },
            VisualCaptureRequiredness.REQUIRED));
        if (derivedFailure)
        {
            Assert.False(recorder.TryBuildContactSheet(out VisualArtifactRecord? contactSheet, out string reason));
            Assert.Null(contactSheet);
            Assert.Contains("height", reason, StringComparison.OrdinalIgnoreCase);
        }
        VisualEvidenceManifest manifest = recorder.CreateManifest(
            "candidate",
            "run",
            DateTimeOffset.UtcNow.AddSeconds(-1),
            DateTimeOffset.UtcNow);
        Assert.True(VisualReviewPacketBuilder.TryBuild(manifest, null, out VisualReviewPacketBuildResult? build, out string buildReason), buildReason);
        Assert.NotNull(build);
        (VisualStoredArtifact packetArtifact, _) = recorder.WriteReviewPacket(build!);
        VisualEvidenceManifest finalManifest = recorder.CreateManifest(
            "candidate",
            "run",
            DateTimeOffset.UtcNow.AddSeconds(-1),
            DateTimeOffset.UtcNow,
            packetArtifact.RelativePath,
            packetArtifact.Sha256);
        recorder.WriteManifest(finalManifest, build.Packet.VisualManifestPath);
        return (recorder, build.Packet, packetArtifact);
    }

    private static VisualReviewResult CreateReview(
        VisualReviewPacket packet,
        string packetSha,
        VisualReviewImageReference image)
        => new(
            VisualEvidenceSchema.ReviewResult,
            VisualEvidenceSchema.CurrentVersion,
            packetSha,
            packet.CandidateSha,
            packet.RunId,
            packet.Scenario,
            packet.Attempt,
            VisualReviewVerdict.VISUAL_OK,
            "AI_AGENT",
            "fixture-reviewer",
            DateTimeOffset.UtcNow,
            new[] { new VisualReviewReviewedImage(image.ArtifactId, image.CheckpointId, image.Sha256) },
            Array.Empty<VisualReviewFinding>(),
            "The image was reviewed.",
            Array.Empty<string>());

    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "tabdock-visual-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class FakeProvider : IVisualCaptureProvider
    {
        public bool TryCapture(VisualCaptureScope scope, out VisualFrame? frame, out string reason)
        {
            frame = new VisualFrame(
                2,
                1,
                new[] { unchecked((int)0x00FF0000), 0x0000FF00 },
                DateTimeOffset.Parse("2026-09-01T00:00:00+00:00"),
                new VisualRect(10, 20, 12, 21),
                new VisualRect(10, 20, 12, 21),
                VisualCaptureMethod.SYNTHETIC,
                scope.Kind,
                Target,
                scope.Privacy,
                96,
                "synthetic",
                sequence: 1,
                relativeMilliseconds: 0,
                captureDurationMilliseconds: 1);
            reason = string.Empty;
            return true;
        }
    }
}
