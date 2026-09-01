using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TabDock.ValidationDriver;
using Xunit;

namespace TabDock.UnitTests;

public sealed class VisualEvidenceRecorderTests
{
    private static readonly VisualTargetIdentity Target = new(
        "0x10", 10, 20, "TabDock.Guest", 100, "GuestWindow", "OwnedWindow");

    [Fact]
    public void Checkpoint_CapturesDeclaredScopesAndBindsMetadata()
    {
        string root = CreateTempRoot();
        try
        {
            var provider = new FakeProvider();
            VisualEvidencePolicy policy = VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.CHECKPOINTS);
            var recorder = new VisualEvidenceRecorder(policy, root, "rename", 1, provider);
            var request = new VisualCheckpointRequest(
                "rename-settled",
                VisualCheckpointPhase.AFTER_ACTION_SETTLED,
                "The renamed guest remains visible in the container.",
                new[]
                {
                    VisualCaptureScope.ForWindow(
                        VisualCaptureScopeKind.CONTAINER_WINDOW,
                        Target,
                        VisualPrivacyClass.PRODUCT_OWNED),
                    VisualCaptureScope.ForWindow(
                        VisualCaptureScopeKind.GUEST_WINDOW,
                        Target,
                        VisualPrivacyClass.TEST_OWNED),
                },
                VisualCaptureRequiredness.REQUIRED);

            VisualCheckpointResult result = recorder.Checkpoint(request);

            Assert.True(result.Captured);
            Assert.False(result.RequiredFailure);
            Assert.Equal(2, result.Artifacts.Count);
            Assert.Empty(result.Unavailable);
            Assert.Equal(2, recorder.Counters.CapturesRequested);
            Assert.Equal(2, recorder.Counters.CapturesSucceeded);
            Assert.True(recorder.Counters.BytesRetained > 0);
            foreach (VisualArtifactRecord artifact in result.Artifacts)
            {
                artifact.Validate();
                Assert.Equal(VisualEvidenceSchema.PngMimeType, artifact.MimeType);
                Assert.Equal(request.Id, artifact.CheckpointId);
                Assert.True(File.Exists(Path.Combine(root, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void RequiredCaptureFailure_IsExplicitAndBestEffortFailureDoesNotPromote()
    {
        string root = CreateTempRoot();
        try
        {
            var provider = new FakeProvider { Fail = true };
            var recorder = new VisualEvidenceRecorder(
                VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.CHECKPOINTS),
                root,
                "scenario",
                1,
                provider);
            VisualCaptureScope scope = VisualCaptureScope.ForWindow(
                VisualCaptureScopeKind.GUEST_WINDOW,
                Target,
                VisualPrivacyClass.TEST_OWNED);

            VisualCheckpointResult required = recorder.Checkpoint(new VisualCheckpointRequest(
                "required-frame",
                VisualCheckpointPhase.BEFORE_ASSERTION,
                "Guest is visible.",
                new[] { scope },
                VisualCaptureRequiredness.REQUIRED));
            VisualCheckpointResult optional = recorder.Checkpoint(new VisualCheckpointRequest(
                "optional-frame",
                VisualCheckpointPhase.AFTER_ACTION_SETTLED,
                "Guest is still visible.",
                new[] { scope },
                VisualCaptureRequiredness.BEST_EFFORT));

            Assert.False(required.Captured);
            Assert.True(required.RequiredFailure);
            Assert.Single(required.Unavailable);
            Assert.False(optional.Captured);
            Assert.False(optional.RequiredFailure);
            Assert.Equal(2, recorder.Unavailable.Count);
            Assert.Equal(2, recorder.Counters.CapturesFailed);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void FailureOnlyPolicy_CapturesBaselineAndRejectsOrdinaryCheckpoint()
    {
        string root = CreateTempRoot();
        try
        {
            var recorder = new VisualEvidenceRecorder(
                VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.FAILURE_ONLY),
                root,
                "scenario",
                1,
                new FakeProvider());
            VisualCaptureScope scope = VisualCaptureScope.ForWindow(
                VisualCaptureScopeKind.HOST_CLIENT,
                Target,
                VisualPrivacyClass.PRODUCT_OWNED);

            VisualCheckpointResult baseline = recorder.Checkpoint(new VisualCheckpointRequest(
                "baseline",
                VisualCheckpointPhase.BASELINE,
                "Initial presentation is stable.",
                new[] { scope }));
            VisualCheckpointResult ordinary = recorder.Checkpoint(new VisualCheckpointRequest(
                "settled",
                VisualCheckpointPhase.AFTER_ACTION_SETTLED,
                "Presentation is stable after action.",
                new[] { scope }));

            Assert.True(baseline.Captured);
            Assert.False(ordinary.Captured);
            Assert.Single(ordinary.Unavailable);
            Assert.Contains("disabled by policy", ordinary.Unavailable[0].Reason, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void DisabledPolicy_MakesRequiredCheckpointFailExplicitly()
    {
        string root = CreateTempRoot();
        try
        {
            var recorder = new VisualEvidenceRecorder(
                VisualEvidencePolicy.Disabled,
                root,
                "scenario",
                1,
                new FakeProvider());
            VisualCaptureScope scope = VisualCaptureScope.ForWindow(
                VisualCaptureScopeKind.GUEST_WINDOW,
                Target,
                VisualPrivacyClass.TEST_OWNED);

            VisualCheckpointResult result = recorder.Checkpoint(new VisualCheckpointRequest(
                "required-frame",
                VisualCheckpointPhase.BASELINE,
                "Guest is visible.",
                new[] { scope },
                VisualCaptureRequiredness.REQUIRED));

            Assert.False(result.Captured);
            Assert.True(result.RequiredFailure);
            Assert.Equal(0, recorder.Counters.CapturesRequested);
            Assert.Equal(1, recorder.Counters.CapturesSkipped);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void FailureCapture_PreservesFirstAttemptAlongsideRerun()
    {
        string root = CreateTempRoot();
        try
        {
            VisualCaptureScope scope = VisualCaptureScope.ForWindow(
                VisualCaptureScopeKind.GUEST_WINDOW,
                Target,
                VisualPrivacyClass.TEST_OWNED);
            VisualEvidencePolicy policy = VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.FAILURE_ONLY);
            var first = new VisualEvidenceRecorder(policy, root, "scenario", 1, new FakeProvider());
            var rerun = new VisualEvidenceRecorder(policy, root, "scenario", 2, new FakeProvider());

            VisualCheckpointResult firstResult = first.CaptureFailure("first failure", new[] { scope });
            VisualCheckpointResult rerunResult = rerun.CaptureFailure("rerun failure", new[] { scope });

            Assert.True(firstResult.Captured);
            Assert.True(rerunResult.Captured);
            Assert.NotEqual(firstResult.Artifacts[0].RelativePath, rerunResult.Artifacts[0].RelativePath);
            Assert.Contains("attempt-001", firstResult.Artifacts[0].RelativePath, StringComparison.Ordinal);
            Assert.Contains("attempt-002", rerunResult.Artifacts[0].RelativePath, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(root, firstResult.Artifacts[0].RelativePath.Replace('/', Path.DirectorySeparatorChar))));
            Assert.True(File.Exists(Path.Combine(root, rerunResult.Artifacts[0].RelativePath.Replace('/', Path.DirectorySeparatorChar))));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void FlightRecorder_FlushesBoundedHistoryAndStops()
    {
        string root = CreateTempRoot();
        try
        {
            var recorder = new VisualEvidenceRecorder(
                VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.FLIGHT_RECORDER),
                root,
                "scenario",
                1,
                new FakeProvider());
            VisualCaptureScope scope = VisualCaptureScope.ForWindow(
                VisualCaptureScopeKind.GUEST_WINDOW,
                Target,
                VisualPrivacyClass.TEST_OWNED);
            recorder.StartFlightRecorder();
            Assert.True(recorder.TryRecordFlightFrame(scope, out string recordReason), recordReason);

            VisualCheckpointResult result = recorder.CaptureFailure(
                "transient presentation defect",
                new[] { scope },
                VisualCaptureRequiredness.REQUIRED);

            Assert.True(result.Captured);
            Assert.False(result.RequiredFailure);
            Assert.NotEmpty(result.Artifacts);
            Assert.All(result.Artifacts, artifact => Assert.Contains("/ring/", artifact.RelativePath, StringComparison.Ordinal));
            Assert.True(recorder.Counters.FramesFlushed >= 1);
            Assert.False(recorder.FlightRecorderRunning);
            Assert.All(result.Artifacts, artifact => Assert.True(File.Exists(
                Path.Combine(root, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar)))));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void FlightRecorder_FailureFlushPreservesHistoryBeforeLargeTrigger()
    {
        string root = CreateTempRoot();
        try
        {
            VisualEvidencePolicy policy = VisualEvidencePolicy
                .SafeDefaults(VisualEvidenceLevel.FLIGHT_RECORDER)
                with
                {
                    RingMaxFrames = 3,
                    RingMaxBytes = 8L * 1024 * 1024,
                    RingDurationMilliseconds = 2000,
                    RingMaxFramesPerSecond = 2,
                };
            var recorder = new VisualEvidenceRecorder(
                policy,
                root,
                "scenario",
                1,
                new LargeProvider());
            VisualCaptureScope scope = VisualCaptureScope.ForWindow(
                VisualCaptureScopeKind.GUEST_WINDOW,
                Target,
                VisualPrivacyClass.TEST_OWNED);

            recorder.StartFlightRecorder();
            Assert.True(recorder.TryRecordFlightFrame(scope, out string recordReason), recordReason);
            VisualCheckpointResult result = recorder.CaptureFailure(
                "large trigger preserves pre-trigger history",
                new[] { scope },
                VisualCaptureRequiredness.REQUIRED);

            Assert.True(result.Captured);
            Assert.False(result.RequiredFailure);
            Assert.True(result.Artifacts.Count >= 2);
            Assert.Equal(1, recorder.Counters.FramesFlushed);
            Assert.False(recorder.FlightRecorderRunning);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Manifest_BindsPolicyArtifactsAndHashToImmutableJson()
    {
        string root = CreateTempRoot();
        try
        {
            var recorder = new VisualEvidenceRecorder(
                VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.CHECKPOINTS),
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
                "The guest is initially visible.",
                new[] { scope }));

            VisualEvidenceManifest manifest = recorder.CreateManifest(
                "candidate-sha",
                "run-id",
                DateTimeOffset.Parse("2026-09-01T00:00:00+00:00"),
                DateTimeOffset.Parse("2026-09-01T00:00:01+00:00"));
            VisualStoredArtifact stored = recorder.WriteManifest(
                manifest,
                "visual/scenario/attempt-001/manifest.json");

            Assert.Equal(manifest.Artifacts[0].Sha256, recorder.Artifacts[0].Sha256);
            Assert.True(stored.SizeBytes > 0);
            Assert.Equal(stored.Sha256, Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(root, stored.RelativePath.Replace('/', Path.DirectorySeparatorChar))))).ToLowerInvariant());
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(
                Path.Combine(root, stored.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Equal(VisualEvidenceSchema.Manifest, document.RootElement.GetProperty("schema").GetString());
            Assert.Equal("candidate-sha", document.RootElement.GetProperty("candidateSha").GetString());
            Assert.Equal("run-id", document.RootElement.GetProperty("runId").GetString());
            Assert.Single(document.RootElement.GetProperty("artifacts").EnumerateArray());
        }
        finally
        {
            TryDelete(root);
        }
    }
    [Fact]
    public void FailureCapture_RespectsHardArtifactBudget()
    {
        string root = CreateTempRoot();
        try
        {
            var recorder = new VisualEvidenceRecorder(
                VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.FAILURE_ONLY).WithBudgets(1, 1),
                root,
                "scenario",
                1,
                new FakeProvider());
            VisualCaptureScope scope = VisualCaptureScope.ForWindow(
                VisualCaptureScopeKind.GUEST_WINDOW,
                Target,
                VisualPrivacyClass.TEST_OWNED);

            VisualCheckpointResult result = recorder.CaptureFailure(
                "bounded failure",
                new[] { scope });

            Assert.False(result.Captured);
            Assert.False(result.RequiredFailure);
            Assert.Single(result.Unavailable);
            Assert.Empty(recorder.Artifacts);
            Assert.Equal(0, recorder.Counters.BytesRetained);
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void FlightCaptureFailure_DisposeStopsRecorderAndCleansTemporaryFiles()
    {
        string root = CreateTempRoot();
        try
        {
            var recorder = new VisualEvidenceRecorder(
                VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.FLIGHT_RECORDER),
                root,
                "scenario",
                1,
                new FakeProvider { Fail = true });
            VisualCaptureScope scope = VisualCaptureScope.ForWindow(
                VisualCaptureScopeKind.GUEST_WINDOW,
                Target,
                VisualPrivacyClass.TEST_OWNED);
            recorder.StartFlightRecorder();

            VisualCheckpointResult result = recorder.CaptureFailure(
                "capture failed",
                new[] { scope },
                VisualCaptureRequiredness.REQUIRED);

            Assert.False(result.Captured);
            Assert.True(result.RequiredFailure);
            Assert.False(recorder.FlightRecorderRunning);
            recorder.Dispose();
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            TryDelete(root);
        }
    }


    [Fact]
    public void ContactSheet_IsDerivedAndManifestRetainsRawSource()
    {
        string root = CreateTempRoot();
        try
        {
            var recorder = new VisualEvidenceRecorder(
                VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.CHECKPOINTS) with
                {
                    BuildReviewPacket = true,
                },
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
                "The guest is visible before the action.",
                new[] { scope }));
            string rawPath = Path.Combine(
                root,
                recorder.Artifacts[0].RelativePath.Replace('/', Path.DirectorySeparatorChar));
            string rawHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(rawPath))).ToLowerInvariant();

            Assert.True(recorder.TryBuildContactSheet(out VisualArtifactRecord? sheet, out string reason), reason);
            Assert.NotNull(sheet);
            Assert.True(sheet!.Derived);
            Assert.Equal(recorder.Artifacts[0].ArtifactId, sheet.SourceArtifactId);
            Assert.Contains("/review/contact-sheet.png", sheet.RelativePath, StringComparison.Ordinal);
            Assert.Equal(rawHash, recorder.Artifacts[0].Sha256);
            Assert.Equal(rawHash, Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(rawPath))).ToLowerInvariant());
            VisualEvidenceManifest manifest = recorder.CreateManifest(
                "candidate",
                "run",
                DateTimeOffset.UtcNow.AddSeconds(-1),
                DateTimeOffset.UtcNow);
            Assert.Equal(2, manifest.Artifacts.Length);
            Assert.Contains(manifest.Artifacts, artifact => artifact.Derived);
        }
        finally
        {
            TryDelete(root);
        }
    }


    [Fact]
    public void ContactSheetFailure_RetainsRawArtifactsAndRecordsDerivedFailure()
    {
        string root = CreateTempRoot();
        try
        {
            VisualEvidencePolicy policy = VisualEvidencePolicy
                .SafeDefaults(VisualEvidenceLevel.CHECKPOINTS, buildReviewPacket: true)
                .WithBudgets(1_000, 64);
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
                "The guest is visible before derived generation.",
                new[] { scope },
                VisualCaptureRequiredness.REQUIRED));
            string rawPath = Path.Combine(
                root,
                recorder.Artifacts[0].RelativePath.Replace('/', Path.DirectorySeparatorChar));
            string rawHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(rawPath))).ToLowerInvariant();

            Assert.False(recorder.TryBuildContactSheet(out VisualArtifactRecord? sheet, out string reason));
            Assert.Null(sheet);
            Assert.Contains("budget", reason, StringComparison.OrdinalIgnoreCase);
            VisualDerivedArtifactFailure failure = Assert.Single(recorder.DerivedFailures);
            Assert.Equal("contact-sheet", failure.ArtifactKind);
            Assert.True(failure.RawArtifactsPreserved);
            Assert.Equal(recorder.Artifacts[0].ArtifactId, Assert.Single(failure.SourceArtifactIds));
            VisualEvidenceManifest manifest = recorder.CreateManifest(
                "candidate",
                "run",
                DateTimeOffset.UtcNow.AddSeconds(-1),
                DateTimeOffset.UtcNow);
            Assert.Equal(rawHash, manifest.Artifacts[0].Sha256);
            Assert.Equal(failure, Assert.Single(manifest.DerivedArtifactFailures));
            Assert.True(File.Exists(rawPath));
        }
        finally
        {
            TryDelete(root);
        }
    }
    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "tabdock-visual-recorder-" + Guid.NewGuid().ToString("N"));
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
        public bool Fail { get; init; }

        public bool TryCapture(VisualCaptureScope scope, out VisualFrame? frame, out string reason)
        {
            if (Fail)
            {
                frame = null;
                reason = "seeded capture failure";
                return false;
            }

            frame = new VisualFrame(
                2,
                1,
                new[] { unchecked((int)0x00FF0000), 0x0000FF00 },
                DateTimeOffset.UtcNow,
                new VisualRect(10, 20, 12, 21),
                new VisualRect(10, 20, 12, 21),
                VisualCaptureMethod.SYNTHETIC,
                scope.Kind,
                Target,
                scope.Privacy,
                96,
                "synthetic-monitor");
            reason = string.Empty;
            return true;
        }
    }
    private sealed class LargeProvider : IVisualCaptureProvider
    {
        public bool TryCapture(VisualCaptureScope scope, out VisualFrame? frame, out string reason)
        {
            const int width = 1500;
            const int height = 800;
            int[] pixels = new int[width * height];
            Array.Fill(pixels, unchecked((int)0x00FF0000));
            frame = new VisualFrame(
                width,
                height,
                pixels,
                DateTimeOffset.UtcNow,
                new VisualRect(10, 20, 10 + width, 20 + height),
                new VisualRect(10, 20, 10 + width, 20 + height),
                VisualCaptureMethod.SYNTHETIC,
                scope.Kind,
                Target,
                scope.Privacy,
                96,
                "synthetic-monitor");
            reason = string.Empty;
            return true;
        }
    }

}
