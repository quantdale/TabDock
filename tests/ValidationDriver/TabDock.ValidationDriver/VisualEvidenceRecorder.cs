using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace TabDock.ValidationDriver;

internal interface IVisualCaptureProvider
{
    bool TryCapture(VisualCaptureScope scope, out VisualFrame? frame, out string reason);
}

internal sealed record VisualCheckpointResult(
    string CheckpointId,
    bool Captured,
    bool RequiredFailure,
    IReadOnlyList<VisualArtifactRecord> Artifacts,
    IReadOnlyList<VisualUnavailableRecord> Unavailable);

/// <summary>
/// Event-driven visual evidence boundary. Scenario code submits semantic
/// checkpoints; this class owns policy gating, capture, encoding, hashing,
/// artifact naming, and explicit unavailable results.
/// </summary>
internal sealed class VisualEvidenceRecorder
{
    private readonly VisualEvidencePolicy _policy;
    private readonly VisualArtifactStore _store;
    private readonly IVisualCaptureProvider _capture;
    private readonly string _scenario;
    private readonly int _attempt;
    private readonly VisualRingBuffer? _ring;
    private readonly List<VisualArtifactRecord> _artifacts = new();
    private readonly List<VisualUnavailableRecord> _unavailable = new();
    private readonly List<VisualDerivedArtifactFailure> _derivedFailures = new();
    private readonly VisualEvidenceCounters _counters = new();
    private int _ordinal;

    public VisualEvidenceRecorder(
        VisualEvidencePolicy policy,
        string artifactRoot,
        string scenario,
        int attempt,
        IVisualCaptureProvider capture)
    {
        policy.Validate();
        if (string.IsNullOrWhiteSpace(artifactRoot))
            throw new ArgumentException("visual artifact root is required.", nameof(artifactRoot));
        if (string.IsNullOrWhiteSpace(scenario))
            throw new ArgumentException("scenario is required.", nameof(scenario));
        if (!IsSafePathSegment(scenario))
            throw new ArgumentException("scenario must be a portable artifact path segment.", nameof(scenario));
        if (attempt < 1)
            throw new ArgumentOutOfRangeException(nameof(attempt));
        _policy = policy;
        _store = new VisualArtifactStore(artifactRoot);
        _scenario = scenario;
        _attempt = attempt;
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _ring = policy.Level == VisualEvidenceLevel.FLIGHT_RECORDER
            ? new VisualRingBuffer(policy)
            : null;
    }

    public bool FlightRecorderRunning => _ring?.Snapshot().Running == true;
    public VisualEvidencePolicy Policy => _policy;
    public IReadOnlyList<VisualArtifactRecord> Artifacts => _artifacts;
    public IReadOnlyList<VisualUnavailableRecord> Unavailable => _unavailable;
    public IReadOnlyList<VisualDerivedArtifactFailure> DerivedFailures => _derivedFailures;
    public VisualEvidenceCounterSnapshot Counters => _counters.Snapshot();
    public string ArtifactRoot => _store.Root;

    public VisualEvidenceManifest CreateManifest(
        string candidateSha,
        string runId,
        DateTimeOffset startedUtc,
        DateTimeOffset endedUtc,
        string? reviewPacketPath = null,
        string? reviewPacketSha256 = null)
    {
        var manifest = new VisualEvidenceManifest(
            VisualEvidenceSchema.Manifest,
            VisualEvidenceSchema.CurrentVersion,
            candidateSha,
            runId,
            _scenario,
            _attempt,
            startedUtc,
            endedUtc,
            _policy,
            _artifacts.ToArray(),
            _unavailable.ToArray(),
            _counters.Snapshot(),
            reviewPacketPath,
            reviewPacketSha256,
            _derivedFailures.ToArray());
        manifest.Validate();
        return manifest;
    }

    public VisualStoredArtifact WriteManifest(VisualEvidenceManifest manifest, string relativePath)
    {
        manifest.Validate();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, VisualJson.Options);
        return _store.WriteImmutable(
            $"{_scenario}-attempt-{_attempt:D3}-visual-manifest",
            relativePath,
            bytes,
            256L * 1024 * 1024);
    }
    public (VisualStoredArtifact Packet, VisualStoredArtifact Instructions) WriteReviewPacket(
        VisualReviewPacketBuildResult packet)
    {
        if (packet == null)
            throw new ArgumentNullException(nameof(packet));
        packet.Packet.Validate();
        return (
            _store.WriteImmutable(
                $"{_scenario}-attempt-{_attempt:D3}-review-packet",
                packet.PacketRelativePath,
                packet.PacketBytes,
                256L * 1024 * 1024),
            _store.WriteImmutable(
                $"{_scenario}-attempt-{_attempt:D3}-review-instructions",
                packet.InstructionsRelativePath,
                packet.InstructionsBytes,
                256L * 1024 * 1024));
    }

    public void RecordReviewUnavailable(string reason)
    {
        _counters.Skipped();
        var item = new VisualUnavailableRecord(
            "review-packet",
            VisualCheckpointPhase.SUSPICIOUS,
            VisualCaptureScopeKind.HOST_CLIENT,
            VisualCaptureRequiredness.BEST_EFFORT,
            string.IsNullOrWhiteSpace(reason) ? "visual review packet unavailable" : reason,
            DateTimeOffset.UtcNow);
        item.Validate();
        _unavailable.Add(item);
    }

    public bool TryBuildContactSheet(out VisualArtifactRecord? artifact, out string reason)
    {
        artifact = null;
        reason = string.Empty;
        if (_artifacts.Count >= _policy.MaxArtifacts)
        {
            reason = "visual artifact count budget exhausted before contact-sheet generation";
            RecordDerivedFailure(reason);
            return false;
        }
        long remaining = _policy.MaxBytes - _counters.BytesRetained;
        if (remaining <= 0)
        {
            reason = "visual byte budget exhausted before contact-sheet generation";
            RecordDerivedFailure(reason);
            return false;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        bool built = VisualContactSheetBuilder.TryBuild(
            _store.Root,
            _artifacts,
            _policy.MaxWidth,
            _policy.MaxHeight,
            remaining,
            out VisualContactSheetBuildResult? sheet,
            out reason);
        stopwatch.Stop();
        _counters.AddEncodeMilliseconds(stopwatch.ElapsedMilliseconds);
        if (!built || sheet == null)
        {
            RecordDerivedFailure(reason);
            return false;
        }

        try
        {
            string artifactId = $"{_scenario}-attempt-{_attempt:D3}-contact-sheet";
            VisualStoredArtifact stored = _store.WriteImmutable(
                artifactId,
                $"visual/{_scenario}/attempt-{_attempt:D3}/review/contact-sheet.png",
                sheet.Png,
                remaining);
            (int width, int height, _) = VisualPngEncoder.Decode(sheet.Png);
            VisualArtifactRecord first = sheet.IncludedArtifacts[0];
            VisualPrivacyClass privacy = sheet.IncludedArtifacts
                .Select(item => item.Privacy)
                .Max();
            var rectangle = new VisualRect(0, 0, width, height);
            var candidate = new VisualArtifactRecord(
                artifactId,
                "contact-sheet",
                first.Phase,
                stored.RelativePath,
                VisualEvidenceSchema.PngMimeType,
                stored.Sha256,
                stored.SizeBytes,
                width,
                height,
                DateTimeOffset.UtcNow,
                sheet.IncludedArtifacts.Max(item => item.Sequence) + 1,
                sheet.IncludedArtifacts.Max(item => item.RelativeMilliseconds),
                VisualCaptureMethod.SYNTHETIC,
                VisualCaptureScopeKind.HOST_CLIENT,
                privacy,
                rectangle,
                rectangle,
                96,
                "derived-contact-sheet",
                null,
                "Derived chronological overview; raw visual artifacts remain authoritative.",
                true,
                first.ArtifactId,
                stopwatch.ElapsedMilliseconds);
            candidate.Validate();
            _artifacts.Add(candidate);
            _counters.RetainBytes(candidate.SizeBytes);
            artifact = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or OverflowException)
        {
            reason = $"contact-sheet artifact finalization failed: {ex.GetType().Name}";
            RecordDerivedFailure(reason);
            return false;
        }
    }


    public VisualCheckpointResult Checkpoint(VisualCheckpointRequest request)
    {
        request.Validate();
        if (request.FlushRing)
            return FlushFlightRecorder(request);

        var captured = new List<VisualArtifactRecord>();
        var unavailable = new List<VisualUnavailableRecord>();
        bool requiredFailure = false;

        if (!IsPhaseEnabled(request.Phase))
        {
            string disabledReason = $"visual checkpoint phase '{request.Phase}' is disabled by policy";
            foreach (VisualCaptureScope scope in request.Scopes)
            {
                VisualUnavailableRecord item = RecordUnavailable(request, scope, disabledReason);
                unavailable.Add(item);
                requiredFailure |= request.Requiredness == VisualCaptureRequiredness.REQUIRED;
            }
            return new VisualCheckpointResult(request.Id, false, requiredFailure, captured, unavailable);
        }

        foreach (VisualCaptureScope scope in request.Scopes)
        {
            _counters.Requested();
            if (scope.Kind == VisualCaptureScopeKind.VIRTUAL_DESKTOP && !_policy.AllowVirtualDesktop)
            {
                VisualUnavailableRecord item = RecordUnavailable(
                    request,
                    scope,
                    "virtual-desktop capture is not authorized by the effective visual policy");
                unavailable.Add(item);
                requiredFailure |= request.Requiredness == VisualCaptureRequiredness.REQUIRED;
                continue;
            }

            if (_artifacts.Count >= _policy.MaxArtifacts)
            {
                VisualUnavailableRecord item = RecordUnavailable(request, scope, "visual artifact count budget exhausted");
                unavailable.Add(item);
                requiredFailure |= request.Requiredness == VisualCaptureRequiredness.REQUIRED;
                continue;
            }

            if (!_capture.TryCapture(scope, out VisualFrame? frame, out string captureReason) || frame == null)
            {
                _counters.Failed();
                VisualUnavailableRecord item = RecordUnavailable(
                    request,
                    scope,
                    string.IsNullOrWhiteSpace(captureReason) ? "visual capture failed" : captureReason);
                unavailable.Add(item);
                requiredFailure |= request.Requiredness == VisualCaptureRequiredness.REQUIRED;
                continue;
            }

            if (TryRetainFrame(request, frame, "checkpoints", out VisualArtifactRecord? artifact, out string retainReason))
            {
                _artifacts.Add(artifact!);
                captured.Add(artifact!);
                _counters.Succeeded();
                _counters.RetainBytes(artifact!.SizeBytes);
            }
            else
            {
                _counters.Failed();
                VisualUnavailableRecord item = RecordUnavailable(request, scope, retainReason);
                unavailable.Add(item);
                requiredFailure |= request.Requiredness == VisualCaptureRequiredness.REQUIRED;
            }
        }

        return new VisualCheckpointResult(
            request.Id,
            captured.Count > 0 && !requiredFailure,
            requiredFailure,
            captured,
            unavailable);
    }
    /// <summary>
    /// Captures a bounded assertion-failure image without changing the
    /// scenario's native result. The caller still declares every approved scope.
    /// </summary>
    public VisualCheckpointResult CaptureFailure(
        string failureReason,
        IReadOnlyList<VisualCaptureScope> scopes,
        VisualCaptureRequiredness requiredness = VisualCaptureRequiredness.BEST_EFFORT)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("failure reason is required.", nameof(failureReason));
        if (scopes == null || scopes.Count == 0)
            throw new ArgumentException("failure evidence requires at least one approved scope.", nameof(scopes));
        string expectation = $"Failure evidence: {failureReason}";
        if (expectation.Length > 500)
            expectation = expectation[..500];
        return Checkpoint(new VisualCheckpointRequest(
            "failure",
            VisualCheckpointPhase.ASSERTION_FAILURE,
            expectation,
            scopes,
            requiredness,
            FlushRing: true,
            IncludeInReview: true));
    }
    public void StartFlightRecorder() => _ring?.Start();

    public void StopFlightRecorder() => _ring?.Stop();
    public VisualRingBufferSnapshot SnapshotFlightRecorder()
        => _ring?.Snapshot() ?? new VisualRingBufferSnapshot(false, 0, 0, 0, 0);

    public bool TryRecordFlightFrame(VisualCaptureScope scope, out string reason)
    {
        reason = string.Empty;
        if (_ring == null)
        {
            reason = "flight recorder is not enabled by policy";
            return false;
        }
        if (!_ring.Snapshot().Running)
        {
            reason = "flight recorder is not running";
            return false;
        }
        try
        {
            scope.Validate();
        }
        catch (ArgumentException ex)
        {
            reason = ex.Message;
            return false;
        }
        _counters.Requested();
        if (scope.Kind == VisualCaptureScopeKind.VIRTUAL_DESKTOP && !_policy.AllowVirtualDesktop)
        {
            _counters.Skipped();
            reason = "virtual-desktop capture is not authorized by the effective visual policy";
            return false;
        }
        if (!_capture.TryCapture(scope, out VisualFrame? frame, out reason) || frame == null)
        {
            _counters.Failed();
            if (string.IsNullOrWhiteSpace(reason))
                reason = "visual flight-frame capture failed";
            return false;
        }

        VisualRingBufferSnapshot before = _ring.Snapshot();
        if (!_ring.TryAdd(frame))
        {
            _counters.Skipped();
            reason = "flight-frame was rejected by rate, duration, or memory bounds";
            return false;
        }
        VisualRingBufferSnapshot after = _ring.Snapshot();
        for (int i = before.FramesEvicted; i < after.FramesEvicted; i++)
            _counters.Evicted();
        _counters.Succeeded();
        return true;
    }

    public VisualCheckpointResult FlushFlightRecorder(VisualCheckpointRequest request)
    {
        request.Validate();
        if (_ring == null || !_ring.Snapshot().Running)
            return Checkpoint(request with { FlushRing = false });

        var captured = new List<VisualArtifactRecord>();
        var unavailable = new List<VisualUnavailableRecord>();
        bool requiredFailure = false;
        VisualFrame? triggerFrame = null;
        try
        {
            VisualCaptureScope triggerScope = request.Scopes[0];
            Stopwatch triggerClock = Stopwatch.StartNew();
            bool triggerCaptured = _capture.TryCapture(
                triggerScope,
                out VisualFrame? trigger,
                out string triggerReason);
            triggerClock.Stop();
            _counters.AddTriggerFrameMilliseconds(triggerClock.ElapsedMilliseconds);
            if (!triggerCaptured || trigger == null)
            {
                _counters.Failed();
                VisualUnavailableRecord item = RecordUnavailable(
                    request,
                    triggerScope,
                    string.IsNullOrWhiteSpace(triggerReason)
                        ? "trigger frame capture failed"
                        : triggerReason);
                unavailable.Add(item);
                requiredFailure = request.Requiredness == VisualCaptureRequiredness.REQUIRED;
            }
            else if (_ring.TryPrepareTriggerFrame(
                trigger,
                out VisualFrame? preparedTrigger,
                out string triggerBoundReason))
            {
                triggerFrame = preparedTrigger!;
                _counters.Succeeded();
            }
            else
            {
                _counters.Skipped();
                VisualUnavailableRecord item = RecordUnavailable(
                    request,
                    triggerScope,
                    string.IsNullOrWhiteSpace(triggerBoundReason)
                        ? "trigger frame exceeded the flight recorder bounds"
                        : triggerBoundReason);
                unavailable.Add(item);
                requiredFailure = request.Requiredness == VisualCaptureRequiredness.REQUIRED;
            }

            IReadOnlyList<VisualFrame> frames = _ring.FlushForFailure();
            for (int i = 0; i < frames.Count; i++)
            {
                _counters.Flushed();
                if (TryRetainFrame(request, frames[i], "ring", out VisualArtifactRecord? artifact, out string retainReason))
                {
                    _artifacts.Add(artifact!);
                    captured.Add(artifact!);
                    _counters.RetainBytes(artifact!.SizeBytes);
                }
                else
                {
                    _counters.Failed();
                    VisualCaptureScope? scope = request.Scopes.FirstOrDefault(
                        candidate => candidate.Kind == frames[i].ScopeKind) ?? request.Scopes[0];
                    VisualUnavailableRecord item = RecordUnavailable(request, scope, retainReason);
                    unavailable.Add(item);
                    requiredFailure = request.Requiredness == VisualCaptureRequiredness.REQUIRED;
                }
            }

            if (triggerFrame is { } prepared)
            {
                if (TryRetainFrame(request, prepared, "ring", out VisualArtifactRecord? artifact, out string retainReason))
                {
                    _artifacts.Add(artifact!);
                    captured.Add(artifact!);
                    _counters.RetainBytes(artifact!.SizeBytes);
                }
                else
                {
                    _counters.Failed();
                    VisualUnavailableRecord item = RecordUnavailable(request, triggerScope, retainReason);
                    unavailable.Add(item);
                    requiredFailure = request.Requiredness == VisualCaptureRequiredness.REQUIRED;
                }
            }

            if (frames.Count == 0 && triggerFrame == null && unavailable.Count == 0)
            {
                VisualUnavailableRecord item = RecordUnavailable(
                    request,
                    triggerScope,
                    "flight recorder had no frame to flush");
                unavailable.Add(item);
                requiredFailure = request.Requiredness == VisualCaptureRequiredness.REQUIRED;
            }
        }
        finally
        {
            _ring.Stop();
        }

        return new VisualCheckpointResult(
            request.Id,
            captured.Count > 0 && !requiredFailure,
            requiredFailure,
            captured,
            unavailable);
    }

    public void Dispose() => StopFlightRecorder();

    private bool TryRetainFrame(
        VisualCheckpointRequest request,
        VisualFrame frame,
        string category,
        out VisualArtifactRecord? artifact,
        out string reason)
    {
        artifact = null;
        reason = string.Empty;
        if (_artifacts.Count >= _policy.MaxArtifacts)
        {
            reason = "visual artifact count budget exhausted";
            return false;
        }

        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            byte[] png = VisualPngEncoder.Encode(frame.Width, frame.Height, frame.Pixels.Span);
            stopwatch.Stop();
            _counters.AddEncodeMilliseconds(stopwatch.ElapsedMilliseconds);
            long remaining = _policy.MaxBytes - _counters.BytesRetained;
            if (png.LongLength > remaining)
            {
                reason = "visual byte budget exhausted before artifact finalization";
                return false;
            }

            int ordinal = ++_ordinal;
            string artifactId = $"{_scenario}-attempt-{_attempt:D3}-{request.Id}-{ordinal:D4}";
            string relativePath =
                $"visual/{_scenario}/attempt-{_attempt:D3}/{category}/{request.Id}/{ordinal:D4}-{frame.ScopeKind}.png";
            VisualStoredArtifact stored = _store.WriteImmutable(
                artifactId,
                relativePath,
                png,
                remaining);
            var candidate = new VisualArtifactRecord(
                artifactId,
                request.Id,
                request.Phase,
                stored.RelativePath,
                VisualEvidenceSchema.PngMimeType,
                stored.Sha256,
                stored.SizeBytes,
                frame.Width,
                frame.Height,
                frame.CapturedUtc,
                frame.Sequence,
                frame.RelativeMilliseconds,
                frame.Method,
                frame.ScopeKind,
                frame.Privacy,
                frame.RequestedRect,
                frame.ActualRect,
                frame.Dpi,
                frame.MonitorId,
                frame.Target,
                request.Expectation,
                Derived: false,
                SourceArtifactId: null,
                frame.CaptureDurationMilliseconds,
                request.Requiredness,
                request.IncludeInReview);
            candidate.Validate();
            artifact = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or OverflowException)
        {
            reason = $"visual artifact finalization failed: {ex.GetType().Name}";
            return false;
        }
    }

    private void RecordDerivedFailure(string reason)
    {
        const string artifactKind = "contact-sheet";
        const string artifactId = "contact-sheet";
        string failureId = $"{_scenario}-attempt-{_attempt:D3}-contact-sheet-failure";
        if (_derivedFailures.Any(item => string.Equals(item.FailureId, failureId, StringComparison.Ordinal)))
            return;

        _counters.Failed();
        VisualArtifactRecord[] sources = _artifacts
            .Where(item => !item.Derived && item.IncludeInReview)
            .OrderBy(item => item.RelativeMilliseconds)
            .ThenBy(item => item.Sequence)
            .ThenBy(item => item.ArtifactId, StringComparer.Ordinal)
            .Take(32)
            .ToArray();
        string boundedReason = string.IsNullOrWhiteSpace(reason)
            ? "contact-sheet generation failed"
            : reason.Length > 500 ? reason[..500] : reason;
        var item = new VisualDerivedArtifactFailure(
            failureId,
            artifactKind,
            artifactId,
            "contact-sheet",
            _scenario,
            _attempt,
            boundedReason,
            VisualCaptureRequiredness.BEST_EFFORT,
            sources.Length > 0,
            sources.Select(source => source.ArtifactId).ToArray(),
            DateTimeOffset.UtcNow);
        item.Validate(_scenario, _attempt);
        _derivedFailures.Add(item);
    }

    private VisualUnavailableRecord RecordUnavailable(
        VisualCheckpointRequest request,
        VisualCaptureScope scope,
        string reason)
    {
        _counters.Skipped();
        var item = new VisualUnavailableRecord(
            request.Id,
            request.Phase,
            scope.Kind,
            request.Requiredness,
            reason,
            DateTimeOffset.UtcNow);
        item.Validate();
        _unavailable.Add(item);
        return item;
    }

    private bool IsPhaseEnabled(VisualCheckpointPhase phase)
        => _policy.Level switch
        {
            VisualEvidenceLevel.NONE => false,
            VisualEvidenceLevel.FAILURE_ONLY => phase is
                VisualCheckpointPhase.BASELINE
                or VisualCheckpointPhase.SUSPICIOUS
                or VisualCheckpointPhase.ASSERTION_FAILURE
                or VisualCheckpointPhase.BEFORE_CLEANUP,
            VisualEvidenceLevel.CHECKPOINTS => true,
            VisualEvidenceLevel.FLIGHT_RECORDER => true,
            _ => false,
        };

    private static bool IsSafePathSegment(string value)
    {
        foreach (char ch in value)
        {
            if (!(ch is >= 'a' and <= 'z')
                && !(ch is >= 'A' and <= 'Z')
                && !(ch is >= '0' and <= '9')
                && ch is not ('.' or '-' or '_'))
            {
                return false;
            }
        }
        return true;
    }
}
