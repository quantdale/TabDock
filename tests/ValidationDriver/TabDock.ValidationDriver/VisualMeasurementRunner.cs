using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace TabDock.ValidationDriver;

/// <summary>
/// Produces bounded, screen-capture-free paired measurements for the CI path.
/// The synthetic provider exercises the real PNG, artifact, contact-sheet,
/// packet, and ring implementations without touching production windows.
/// </summary>
internal static class VisualMeasurementRunner
{
    private static readonly string[] RepresentativeScenarios =
    {
        "rename",
        "split",
        "inline-capture",
        "maximize-fullscreen",
        "title-centering",
        "topmost-transition",
    };

    public static VisualMeasurementReport RunSynthetic(
        string candidateSha,
        string runId,
        string driverSha,
        string configuration,
        int sampleCount,
        int seed,
        string workRoot)
    {
        if (string.IsNullOrWhiteSpace(candidateSha))
            throw new ArgumentException("candidate identity is required.", nameof(candidateSha));
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("run identity is required.", nameof(runId));
        if (string.IsNullOrWhiteSpace(driverSha))
            throw new ArgumentException("driver identity is required.", nameof(driverSha));
        if (string.IsNullOrWhiteSpace(configuration))
            throw new ArgumentException("configuration is required.", nameof(configuration));
        if (sampleCount < 1 || sampleCount > 100)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        if (string.IsNullOrWhiteSpace(workRoot))
            throw new ArgumentException("measurement work root is required.", nameof(workRoot));

        Directory.CreateDirectory(workRoot);
        var samples = new List<VisualMeasurementSample>(
            RepresentativeScenarios.Length * sampleCount * 5);
        int sequence = 0;
        foreach (string scenario in RepresentativeScenarios)
        {
            for (int sampleNumber = 1; sampleNumber <= sampleCount; sampleNumber++)
            {
                samples.Add(CreateDisabledSample(
                    candidateSha,
                    runId,
                    configuration,
                    scenario,
                    sampleNumber,
                    seed + sequence++));

                samples.Add(CreateRecorderSample(
                    candidateSha,
                    runId,
                    configuration,
                    scenario,
                    sampleNumber,
                    seed + sequence++,
                    VisualMeasurementMode.CHECKPOINTS,
                    workRoot));
                samples.Add(CreateRecorderSample(
                    candidateSha,
                    runId,
                    configuration,
                    scenario,
                    sampleNumber,
                    seed + sequence++,
                    VisualMeasurementMode.CHECKPOINTS_PLUS_PACKET,
                    workRoot));
                samples.Add(CreateRecorderSample(
                    candidateSha,
                    runId,
                    configuration,
                    scenario,
                    sampleNumber,
                    seed + sequence++,
                    VisualMeasurementMode.FLIGHT_HEALTHY_DISCARD,
                    workRoot));
                samples.Add(CreateRecorderSample(
                    candidateSha,
                    runId,
                    configuration,
                    scenario,
                    sampleNumber,
                    seed + sequence++,
                    VisualMeasurementMode.FLIGHT_FAILURE_FLUSH,
                    workRoot));
            }
        }

        VisualMeasurementReport measured = VisualMeasurementReportBuilder.Build(
            samples,
            driverSha,
            limitation: "Synthetic/in-memory provider; this report cannot satisfy a physical visual gate.");
        IReadOnlyList<VisualMeasurementBudget> budgets = VisualMeasurementBudgetSelector.Derive(measured);
        VisualMeasurementReport report = measured with
        {
            Budgets = budgets,
            Outcome = measured.Outcome == "BLOCKED"
                ? "BLOCKED"
                : measured.Cells.Any(cell => cell.SampleCount < 20) ? "PROVISIONAL" : "PASS",
        };
        report.Validate();
        return report;
    }

    private static VisualMeasurementSample CreateDisabledSample(
        string candidateSha,
        string runId,
        string configuration,
        string scenario,
        int sampleNumber,
        int seed)
    {
        VisualMeasurementMode mode = VisualMeasurementMode.DISABLED;
        VisualResourceObservation before = VisualResourceObservationFactory.Synthetic(mode);
        VisualResourceObservation after = VisualResourceObservationFactory.Synthetic(mode);
        return new VisualMeasurementSample(
            DateTimeOffset.UtcNow,
            candidateSha,
            runId,
            scenario,
            configuration,
            Attempt: 1,
            sampleNumber,
            mode,
            VisualMeasurementClassification.SYNTHETIC,
            SyntheticTopology(seed),
            VisualEvidencePolicy.Disabled,
            Width: null,
            Height: null,
            CaptureMethod: null,
            ScopeKind: null,
            new VisualMeasurementTiming(
                ControlOverheadMilliseconds: 1,
                CaptureMilliseconds: 0,
                PngEncodeMilliseconds: 0,
                WriteMilliseconds: 0,
                HashMilliseconds: 0,
                ManifestMilliseconds: 0,
                ContactSheetMilliseconds: 0,
                PacketMilliseconds: 0,
                InstructionsMilliseconds: 0,
                FlushMilliseconds: 0,
                DiscardMilliseconds: 0),
            new VisualMeasurementWork(
                CaptureRequests: 0,
                CapturesSucceeded: 0,
                CapturesFailed: 0,
                PngEncodes: 0,
                ContactSheetBuilds: 0,
                PacketBuilds: 0,
                InstructionBuilds: 0,
                WorkerActivations: 0,
                TimerActivations: 0,
                RetainedFrames: 0,
                RetainedBytes: 0,
                ArtifactCount: 0,
                ArtifactBytes: 0,
                RingEvictions: 0,
                RingFlushes: 0),
            ManagedAllocationDeltaBytes: 0,
            CpuMilliseconds: null,
            before,
            after,
            RingOccupancy: 0,
            PeakRingBytes: 0,
            HealthyFlightDiscarded: false,
            CleanupCompleted: true,
            CancellationOutcome: null,
            OutlierNote: null,
            PeakPrivateBytes: Math.Max(before.PrivateBytes ?? 0, after.PrivateBytes ?? 0),
            PeakWorkingSet: Math.Max(before.WorkingSet ?? 0, after.WorkingSet ?? 0));
    }

    private static VisualMeasurementSample CreateRecorderSample(
        string candidateSha,
        string runId,
        string configuration,
        string scenario,
        int sampleNumber,
        int seed,
        VisualMeasurementMode mode,
        string workRoot)
    {
        VisualEvidenceLevel level = mode is VisualMeasurementMode.FLIGHT_HEALTHY_DISCARD
            or VisualMeasurementMode.FLIGHT_FAILURE_FLUSH
            ? VisualEvidenceLevel.FLIGHT_RECORDER
            : VisualEvidenceLevel.CHECKPOINTS;
        bool buildPacket = mode == VisualMeasurementMode.CHECKPOINTS_PLUS_PACKET;
        VisualEvidencePolicy policy = VisualEvidencePolicy.SafeDefaults(level, buildPacket);
        string sampleRoot = Path.Combine(
            workRoot,
            scenario,
            mode.ToString().ToLowerInvariant(),
            sampleNumber.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(sampleRoot);

        var target = new VisualTargetIdentity(
            "0x1",
            1,
            1,
            "SyntheticWindow",
            1,
            "SyntheticTarget",
            "OwnedProcess");
        VisualCaptureScope scope = VisualCaptureScope.ForWindow(
            VisualCaptureScopeKind.GUEST_WINDOW,
            target,
            VisualPrivacyClass.TEST_OWNED);
        var provider = new SyntheticVisualCaptureProvider(seed, scenario, target);
        var recorder = new VisualEvidenceRecorder(policy, sampleRoot, scenario, 1, provider);
        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
        TimeSpan cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
        var timing = new VisualMeasurementTiming(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        VisualRingBufferSnapshot? peakRing = null;
        VisualMeasurementSample? result = null;
        try
        {
            switch (mode)
            {
                case VisualMeasurementMode.CHECKPOINTS:
                case VisualMeasurementMode.CHECKPOINTS_PLUS_PACKET:
                {
                    Stopwatch captureClock = Stopwatch.StartNew();
                    VisualCheckpointResult checkpoint = recorder.Checkpoint(new VisualCheckpointRequest(
                        $"sample-{sampleNumber:D3}",
                        VisualCheckpointPhase.AFTER_ACTION_SETTLED,
                        $"Synthetic {scenario} presentation remains bounded and aligned.",
                        new[] { scope },
                        VisualCaptureRequiredness.REQUIRED));
                    captureClock.Stop();
                    if (!checkpoint.Captured || checkpoint.RequiredFailure)
                        throw new InvalidOperationException("synthetic checkpoint did not capture a required frame");
                    VisualEvidenceCounterSnapshot counters = recorder.Counters;
                    timing = timing with
                    {
                        CaptureMilliseconds = captureClock.ElapsedMilliseconds,
                        PngEncodeMilliseconds = counters.EncodeMilliseconds,
                        WriteMilliseconds = Math.Max(0, captureClock.ElapsedMilliseconds - counters.EncodeMilliseconds),
                    };
                    timing = mode == VisualMeasurementMode.CHECKPOINTS_PLUS_PACKET
                        ? AddPacketWork(recorder, candidateSha, runId, scenario, timing)
                        : WriteManifest(recorder, candidateSha, runId, scenario, timing);
                    break;
                }
                case VisualMeasurementMode.FLIGHT_HEALTHY_DISCARD:
                {
                    recorder.StartFlightRecorder();
                    Stopwatch flightClock = Stopwatch.StartNew();
                    for (int i = 0; i < 4; i++)
                    {
                        if (!recorder.TryRecordFlightFrame(scope, out string flightReason))
                            throw new InvalidOperationException($"synthetic healthy flight frame failed: {flightReason}");
                        peakRing = MaxRing(peakRing, recorder.SnapshotFlightRecorder());
                    }
                    flightClock.Stop();
                    Stopwatch discardClock = Stopwatch.StartNew();
                    recorder.StopFlightRecorder();
                    discardClock.Stop();
                    timing = timing with
                    {
                        CaptureMilliseconds = flightClock.ElapsedMilliseconds,
                        DiscardMilliseconds = discardClock.ElapsedMilliseconds,
                    };
                    timing = WriteManifest(recorder, candidateSha, runId, scenario, timing);
                    break;
                }
                case VisualMeasurementMode.FLIGHT_FAILURE_FLUSH:
                {
                    recorder.StartFlightRecorder();
                    for (int i = 0; i < 3; i++)
                    {
                        if (!recorder.TryRecordFlightFrame(scope, out string flightReason))
                            throw new InvalidOperationException($"synthetic failure flight frame failed: {flightReason}");
                        peakRing = MaxRing(peakRing, recorder.SnapshotFlightRecorder());
                    }
                    Stopwatch flushClock = Stopwatch.StartNew();
                    VisualCheckpointResult flushed = recorder.FlushFlightRecorder(new VisualCheckpointRequest(
                        "failure-flush",
                        VisualCheckpointPhase.ASSERTION_FAILURE,
                        "Synthetic bounded failure history is flushed in chronological order.",
                        new[] { scope },
                        VisualCaptureRequiredness.BEST_EFFORT,
                        FlushRing: true));
                    flushClock.Stop();
                    if (!flushed.Captured)
                        throw new InvalidOperationException("synthetic flight failure did not flush a frame");
                    timing = timing with
                    {
                        FlushMilliseconds = flushClock.ElapsedMilliseconds,
                        PngEncodeMilliseconds = recorder.Counters.EncodeMilliseconds,
                    };
                    timing = WriteManifest(recorder, candidateSha, runId, scenario, timing);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }

            timing = timing with
            {
                TriggerFrameMilliseconds = recorder.Counters.TriggerFrameMilliseconds,
            };
            VisualEvidenceCounterSnapshot finalCounters = recorder.Counters;
            VisualRingBufferSnapshot finalRing = recorder.SnapshotFlightRecorder();
            long allocationDelta = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocationStart);
            double cpuMilliseconds = Math.Max(
                0,
                (Process.GetCurrentProcess().TotalProcessorTime - cpuStart).TotalMilliseconds);
            int contactSheetBuilds = mode == VisualMeasurementMode.CHECKPOINTS_PLUS_PACKET ? 1 : 0;
            int packetBuilds = contactSheetBuilds;
            var work = new VisualMeasurementWork(
                finalCounters.CapturesRequested,
                finalCounters.CapturesSucceeded,
                finalCounters.CapturesFailed,
                finalCounters.CapturesSucceeded + finalCounters.FramesFlushed + contactSheetBuilds,
                contactSheetBuilds,
                packetBuilds,
                packetBuilds,
                0,
                0,
                recorder.Artifacts.Count,
                finalCounters.BytesRetained,
                recorder.Artifacts.Count,
                finalCounters.BytesRetained,
                finalCounters.FramesEvicted,
                finalCounters.FramesFlushed);
            bool healthyDiscarded = mode == VisualMeasurementMode.FLIGHT_HEALTHY_DISCARD
                && finalRing.Count == 0
                && recorder.Artifacts.Count == 0;
            VisualResourceObservation beforeResources = VisualResourceObservationFactory.Synthetic(mode);
            VisualResourceObservation afterResources = VisualResourceObservationFactory.Synthetic(
                mode,
                ringBytes: finalRing.Bytes);
            result = new VisualMeasurementSample(
                DateTimeOffset.UtcNow,
                candidateSha,
                runId,
                scenario,
                configuration,
                Attempt: 1,
                sampleNumber,
                mode,
                VisualMeasurementClassification.SYNTHETIC,
                SyntheticTopology(seed),
                policy,
                provider.Width,
                provider.Height,
                VisualCaptureMethod.SYNTHETIC,
                VisualCaptureScopeKind.GUEST_WINDOW,
                timing,
                work,
                allocationDelta,
                cpuMilliseconds,
                beforeResources,
                afterResources,
                finalRing.Count,
                peakRing?.Bytes ?? 0,
                healthyDiscarded,
                CleanupCompleted: false,
                CancellationOutcome: null,
                OutlierNote: null,
                PeakPrivateBytes: Math.Max(
                    beforeResources.PrivateBytes ?? 0,
                    afterResources.PrivateBytes ?? 0),
                PeakWorkingSet: Math.Max(
                    beforeResources.WorkingSet ?? 0,
                    afterResources.WorkingSet ?? 0));
        }
        finally
        {
            recorder.Dispose();
            bool cleanupCompleted = TryDelete(sampleRoot);
            if (result is not null)
                result = result with { CleanupCompleted = cleanupCompleted };
        }

        return result ?? throw new InvalidOperationException("synthetic visual measurement produced no sample");
    }

    private static VisualMeasurementTiming AddPacketWork(
        VisualEvidenceRecorder recorder,
        string candidateSha,
        string runId,
        string scenario,
        VisualMeasurementTiming timing)
    {
        Stopwatch contactClock = Stopwatch.StartNew();
        if (!recorder.TryBuildContactSheet(out _, out string contactReason))
            throw new InvalidOperationException($"synthetic contact-sheet build failed: {contactReason}");
        contactClock.Stop();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        VisualEvidenceManifest preliminary = recorder.CreateManifest(candidateSha, runId, now, now);
        Stopwatch packetClock = Stopwatch.StartNew();
        if (!VisualReviewPacketBuilder.TryBuild(
                preliminary,
                new Dictionary<string, string> { ["measurement"] = "synthetic" },
                out VisualReviewPacketBuildResult? built,
                out string reason)
            || built == null)
        {
            throw new InvalidOperationException($"synthetic packet build failed: {reason}");
        }
        packetClock.Stop();

        Stopwatch writeClock = Stopwatch.StartNew();
        (VisualStoredArtifact packetArtifact, _) = recorder.WriteReviewPacket(built);
        writeClock.Stop();
        Stopwatch hashClock = Stopwatch.StartNew();
        _ = SHA256.HashData(built.PacketBytes);
        hashClock.Stop();
        Stopwatch manifestClock = Stopwatch.StartNew();
        VisualEvidenceManifest finalManifest = recorder.CreateManifest(
            candidateSha,
            runId,
            now,
            DateTimeOffset.UtcNow,
            packetArtifact.RelativePath,
            packetArtifact.Sha256);
        recorder.WriteManifest(finalManifest, $"visual/{scenario}/attempt-001/manifest.json");
        manifestClock.Stop();
        return timing with
        {
            WriteMilliseconds = timing.WriteMilliseconds + writeClock.ElapsedMilliseconds,
            HashMilliseconds = hashClock.ElapsedMilliseconds,
            ManifestMilliseconds = manifestClock.ElapsedMilliseconds,
            ContactSheetMilliseconds = contactClock.ElapsedMilliseconds,
            PacketMilliseconds = packetClock.ElapsedMilliseconds,
            InstructionsMilliseconds = built.InstructionsSerializationMilliseconds,
        };
    }

    private static VisualMeasurementTiming WriteManifest(
        VisualEvidenceRecorder recorder,
        string candidateSha,
        string runId,
        string scenario,
        VisualMeasurementTiming timing)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Stopwatch manifestClock = Stopwatch.StartNew();
        VisualEvidenceManifest manifest = recorder.CreateManifest(candidateSha, runId, now, now);
        recorder.WriteManifest(manifest, $"visual/{scenario}/attempt-001/manifest.json");
        manifestClock.Stop();
        return timing with { ManifestMilliseconds = manifestClock.ElapsedMilliseconds };
    }


    private static VisualRingBufferSnapshot MaxRing(
        VisualRingBufferSnapshot? previous,
        VisualRingBufferSnapshot current)
        => previous is VisualRingBufferSnapshot prior
            ? new VisualRingBufferSnapshot(
                current.Running,
                Math.Max(prior.Count, current.Count),
                Math.Max(prior.Bytes, current.Bytes),
                Math.Max(prior.FramesEvicted, current.FramesEvicted),
                Math.Max(prior.FramesFlushed, current.FramesFlushed))
            : current;

    private static VisualMachineTopology SyntheticTopology(int seed)
        => new(
            "ci-synthetic",
            "single-monitor",
            1,
            96,
            "primary");

    private static bool TryDelete(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return true;
            Directory.Delete(path, recursive: true);
            return !Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private sealed class SyntheticVisualCaptureProvider : IVisualCaptureProvider
    {
        private readonly int _seed;
        private readonly string _scenario;
        private readonly VisualTargetIdentity _target;
        private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;
        private int _sequence;

        public SyntheticVisualCaptureProvider(int seed, string scenario, VisualTargetIdentity target)
        {
            _seed = seed;
            _scenario = scenario;
            _target = target;
            Width = 96 + Math.Abs(seed % 3) * 16;
            Height = 64 + Math.Abs(seed % 2) * 16;
        }

        public int Width { get; }
        public int Height { get; }

        public bool TryCapture(VisualCaptureScope scope, out VisualFrame? frame, out string reason)
        {
            frame = null;
            reason = string.Empty;
            try
            {
                scope.Validate();
                if (scope.Target != _target)
                {
                    reason = "synthetic target identity mismatch";
                    return false;
                }
                int[] pixels = new int[checked(Width * Height)];
                int scenarioOffset = _scenario.Length * 17;
                int color = (_seed * 31 + scenarioOffset + _sequence * 7) & 0xFF;
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        int red = (color + x) & 0xFF;
                        int green = (color + y * 2) & 0xFF;
                        int blue = (color + x + y) & 0xFF;
                        pixels[y * Width + x] = (red << 16) | (green << 8) | blue;
                    }
                }
                _sequence++;
                DateTimeOffset captured = _start.AddMilliseconds(_sequence * 600L);
                var rectangle = new VisualRect(0, 0, Width, Height);
                frame = new VisualFrame(
                    Width,
                    Height,
                    pixels,
                    captured,
                    rectangle,
                    rectangle,
                    VisualCaptureMethod.SYNTHETIC,
                    scope.Kind,
                    _target,
                    scope.Privacy,
                    96,
                    "synthetic",
                    captureDurationMilliseconds: 1);
                return true;
            }
            catch (ArgumentException ex)
            {
                reason = ex.Message;
                return false;
            }
        }
    }
}
