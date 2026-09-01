using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TabDock.ValidationDriver;
using Xunit;

namespace TabDock.UnitTests;

public sealed class VisualReviewPacketTests
{
    private static readonly VisualTargetIdentity Target = new(
        "0x10", 10, 20, "TabDock.Guest", 100, "GuestWindow", "OwnedWindow");

    [Fact]
    public void Packet_BindsOrderedImagesContactSheetAndOutputContract()
    {
        string root = CreateTempRoot();
        try
        {
            var recorder = CreateRecorder(root);
            VisualCaptureScope scope = VisualCaptureScope.ForWindow(
                VisualCaptureScopeKind.GUEST_WINDOW,
                Target,
                VisualPrivacyClass.TEST_OWNED);
            recorder.Checkpoint(new VisualCheckpointRequest(
                "baseline",
                VisualCheckpointPhase.BASELINE,
                "The guest is visible before the action.",
                new[] { scope },
                VisualCaptureRequiredness.REQUIRED));
            Assert.True(recorder.TryBuildContactSheet(out _, out string sheetReason), sheetReason);
            VisualEvidenceManifest manifest = recorder.CreateManifest(
                "candidate-sha",
                "run-id",
                DateTimeOffset.Parse("2026-09-01T00:00:00+00:00"),
                DateTimeOffset.Parse("2026-09-01T00:00:01+00:00"));

            bool built = VisualReviewPacketBuilder.TryBuild(
                manifest,
                new Dictionary<string, string> { ["nativeOutcome"] = "PASS" },
                out VisualReviewPacketBuildResult? result,
                out string reason);

            Assert.True(built, reason);
            Assert.NotNull(result);
            result!.Packet.Validate();
            Assert.Equal("candidate-sha", result.Packet.CandidateSha);
            Assert.Equal("run-id", result.Packet.RunId);
            Assert.Contains("review-instructions.md", result.Packet.InstructionsPath, StringComparison.Ordinal);
            Assert.Single(result.Packet.Checkpoints);
            Assert.Single(result.Packet.Images);
            Assert.NotNull(result.Packet.ContactSheetPath);
            Assert.Contains("contact-sheet.png", result.Packet.ContactSheetPath, StringComparison.Ordinal);
            Assert.Contains("requiredResultPath", Encoding(result.InstructionsBytes), StringComparison.Ordinal);
            Assert.DoesNotContain(root, Encoding(result.PacketBytes), StringComparison.OrdinalIgnoreCase);

            (VisualStoredArtifact packet, VisualStoredArtifact instructions) = recorder.WriteReviewPacket(result);
            Assert.True(File.Exists(Path.Combine(root, packet.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
            Assert.True(File.Exists(Path.Combine(root, instructions.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(
                Path.Combine(root, packet.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Equal(VisualEvidenceSchema.ReviewPacket, document.RootElement.GetProperty("schema").GetString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Packet_RejectsWhenNoSelectedRawImagesRemain()
    {
        string root = CreateTempRoot();
        try
        {
            VisualEvidencePolicy policy = VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.CHECKPOINTS);
            var manifest = new VisualEvidenceManifest(
                VisualEvidenceSchema.Manifest,
                VisualEvidenceSchema.CurrentVersion,
                "candidate",
                "run",
                "scenario",
                1,
                DateTimeOffset.UtcNow.AddSeconds(-1),
                DateTimeOffset.UtcNow,
                policy,
                Array.Empty<VisualArtifactRecord>(),
                Array.Empty<VisualUnavailableRecord>(),
                new VisualEvidenceCounterSnapshot(0, 0, 0, 0, 0, 0, 0, 0),
                null,
                null,
                Array.Empty<VisualDerivedArtifactFailure>());

            bool built = VisualReviewPacketBuilder.TryBuild(
                manifest,
                null,
                out VisualReviewPacketBuildResult? result,
                out string reason);

            Assert.False(built);
            Assert.Null(result);
            Assert.Contains("at least one", reason, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static VisualEvidenceRecorder CreateRecorder(string root)
        => new(
            VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.CHECKPOINTS) with
            {
                BuildReviewPacket = true,
            },
            root,
            "scenario",
            1,
            new FakeProvider(Target));

    private static string Encoding(byte[] bytes) => System.Text.Encoding.UTF8.GetString(bytes);

    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "tabdock-visual-packet-" + Guid.NewGuid().ToString("N"));
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
        private readonly VisualTargetIdentity _target;

        public FakeProvider(VisualTargetIdentity target) => _target = target;

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
                _target,
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
