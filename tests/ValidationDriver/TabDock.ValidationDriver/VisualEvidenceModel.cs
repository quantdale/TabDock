using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TabDock.ValidationDriver;

internal static class VisualEvidenceSchema
{
    public const int CurrentVersion = 1;
    public const string Manifest = "tabdock-visual-manifest-v1";
    public const string ReviewPacket = "tabdock-visual-review-packet-v1";
    public const string ReviewResult = "tabdock-visual-review-result-v1";
    public const string PngMimeType = "image/png";
}

internal enum VisualCaptureMethod
{
    SCREEN_COMPOSITION,
    PRINT_WINDOW,
    SYNTHETIC,
}

internal enum VisualCaptureScopeKind
{
    HOST_CLIENT,
    CONTAINER_WINDOW,
    GUEST_WINDOW,
    OWNED_POPUP,
    TARGET_WITH_CONTEXT,
    VIRTUAL_DESKTOP,
}

internal enum VisualPrivacyClass
{
    TEST_OWNED,
    PRODUCT_OWNED,
    REAL_APP_RESTRICTED,
    DESKTOP_RESTRICTED,
}

internal enum VisualEvidenceLevel
{
    NONE,
    FAILURE_ONLY,
    CHECKPOINTS,
    FLIGHT_RECORDER,
}

internal enum VisualCheckpointPhase
{
    BASELINE,
    BEFORE_ACTION,
    AFTER_ACTION_IMMEDIATE,
    AFTER_ACTION_SETTLED,
    BEFORE_ASSERTION,
    SUSPICIOUS,
    ASSERTION_FAILURE,
    FINAL_HEALTHY,
    BEFORE_CLEANUP,
}

internal enum VisualCaptureRequiredness
{
    BEST_EFFORT,
    REQUIRED,
}

internal enum VisualReviewVerdict
{
    VISUAL_OK,
    VISUAL_SUSPECT,
    VISUAL_DEFECT,
    REVIEW_UNAVAILABLE,
}

internal enum VisualFindingCategory
{
    OCCLUSION,
    BLANK_BLACK_REGION,
    WRONG_GUEST,
    CLIPPING,
    MISALIGNMENT,
    POPUP_PLACEMENT,
    Z_ORDER_COMPOSITION,
    TRANSIENT_FLICKER_FLASH,
    STALE_FRAME,
    VISIBLE_GEOMETRY_DRIFT,
    DPI_ANOMALY,
    UNEXPECTED_CHROME,
    CAPTURE_ARTIFACT,
    OTHER,
}

internal readonly record struct VisualRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsPositive => Right > Left && Bottom > Top;

    public void Validate(string name)
    {
        if (!IsPositive)
            throw new ArgumentException($"{name} must have positive dimensions.", name);
    }

    public VisualRect Inflate(int margin)
    {
        if (margin < 0)
            throw new ArgumentOutOfRangeException(nameof(margin));
        return new VisualRect(Left - margin, Top - margin, Right + margin, Bottom + margin);
    }

    public VisualRect Intersect(VisualRect other)
        => new(
            Math.Max(Left, other.Left),
            Math.Max(Top, other.Top),
            Math.Min(Right, other.Right),
            Math.Min(Bottom, other.Bottom));
}

internal sealed record VisualTargetIdentity(
    string Hwnd,
    uint ProcessId,
    uint WindowThreadId,
    string ClassName,
    long ProcessStartTimeUtcTicks,
    string Role,
    string Ownership)
{

    public void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(Hwnd) || !Hwnd.StartsWith("0x", StringComparison.Ordinal)
            || ProcessId == 0 || WindowThreadId == 0 || ProcessStartTimeUtcTicks == 0
            || string.IsNullOrWhiteSpace(ClassName) || string.IsNullOrWhiteSpace(Role)
            || string.IsNullOrWhiteSpace(Ownership))
        {
            throw new ArgumentException($"{name} contains an incomplete target identity.", name);
        }
    }

}

internal sealed record VisualCaptureScope(
    VisualCaptureScopeKind Kind,
    VisualTargetIdentity? Target,
    VisualRect? RequestedRect,
    int ContextMargin,
    VisualPrivacyClass Privacy,
    VisualCaptureMethod Method,
    bool VirtualDesktopAuthorization)
{
    public void Validate(string name = "scope")
    {
        if (Kind == VisualCaptureScopeKind.VIRTUAL_DESKTOP && !VirtualDesktopAuthorization)
            throw new ArgumentException("virtual-desktop capture requires explicit authorization.", name);
        if (Kind != VisualCaptureScopeKind.VIRTUAL_DESKTOP && Target == null)
            throw new ArgumentException("target identity is required for a window scope.", name);
        if (RequestedRect is VisualRect requested)
            requested.Validate($"{name}.requestedRect");
        if (ContextMargin < 0 || ContextMargin > 128)
            throw new ArgumentOutOfRangeException(nameof(ContextMargin), "context margin must be between 0 and 128 pixels.");
        if (Privacy == VisualPrivacyClass.DESKTOP_RESTRICTED
            && Kind != VisualCaptureScopeKind.VIRTUAL_DESKTOP)
        {
            throw new ArgumentException("desktop-restricted privacy is reserved for virtual-desktop diagnostics.", name);
        }
        Target?.Validate($"{name}.target");
    }

    public static VisualCaptureScope ForWindow(
        VisualCaptureScopeKind kind,
        VisualTargetIdentity target,
        VisualPrivacyClass privacy,
        VisualCaptureMethod method = VisualCaptureMethod.SCREEN_COMPOSITION,
        VisualRect? requestedRect = null,
        int contextMargin = 0)
    {
        var scope = new VisualCaptureScope(kind, target, requestedRect, contextMargin, privacy, method, false);
        scope.Validate();
        return scope;
    }

    public static VisualCaptureScope ForVirtualDesktop(bool authorized)
    {
        var scope = new VisualCaptureScope(
            VisualCaptureScopeKind.VIRTUAL_DESKTOP,
            null,
            null,
            0,
            VisualPrivacyClass.DESKTOP_RESTRICTED,
            VisualCaptureMethod.SCREEN_COMPOSITION,
            authorized);
        scope.Validate();
        return scope;
    }
}

internal sealed class VisualFrame
{
    private readonly int[] _pixels;

    public VisualFrame(
        int width,
        int height,
        ReadOnlySpan<int> pixels,
        DateTimeOffset capturedUtc,
        VisualRect requestedRect,
        VisualRect actualRect,
        VisualCaptureMethod method,
        VisualCaptureScopeKind scopeKind,
        VisualTargetIdentity? target,
        VisualPrivacyClass privacy,
        int dpi,
        string monitorId,
        long sequence = 0,
        long relativeMilliseconds = 0,
        long captureDurationMilliseconds = 0)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "frame dimensions must be positive.");
        if ((long)width * height != pixels.Length)
            throw new ArgumentException("pixel count does not match frame dimensions.", nameof(pixels));
        requestedRect.Validate(nameof(requestedRect));
        actualRect.Validate(nameof(actualRect));
        if (actualRect.Width != width || actualRect.Height != height)
            throw new ArgumentException("actual rectangle dimensions must match the pixel buffer.", nameof(actualRect));
        if (dpi <= 0 || captureDurationMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(dpi));
        if (string.IsNullOrWhiteSpace(monitorId))
            throw new ArgumentException("monitor identity is required.", nameof(monitorId));
        if (scopeKind != VisualCaptureScopeKind.VIRTUAL_DESKTOP && target == null)
            throw new ArgumentException("window frames require a target identity.", nameof(target));
        target?.Validate(nameof(target));
        _pixels = pixels.ToArray();
        Width = width;
        Height = height;
        CapturedUtc = capturedUtc;
        RequestedRect = requestedRect;
        ActualRect = actualRect;
        Method = method;
        ScopeKind = scopeKind;
        Target = target;
        Privacy = privacy;
        Dpi = dpi;
        MonitorId = monitorId;
        Sequence = sequence;
        RelativeMilliseconds = relativeMilliseconds;
        CaptureDurationMilliseconds = captureDurationMilliseconds;
    }

    public int Width { get; }
    public int Height { get; }
    public DateTimeOffset CapturedUtc { get; }
    public VisualRect RequestedRect { get; }
    public VisualRect ActualRect { get; }
    public VisualCaptureMethod Method { get; }
    public VisualCaptureScopeKind ScopeKind { get; }
    public VisualTargetIdentity? Target { get; }
    public VisualPrivacyClass Privacy { get; }
    public int Dpi { get; }
    public string MonitorId { get; }
    public long Sequence { get; }
    public long RelativeMilliseconds { get; }
    public long CaptureDurationMilliseconds { get; }
    public ReadOnlyMemory<int> Pixels => _pixels;

    public int[] CopyPixels() => (int[])_pixels.Clone();
}

internal sealed record VisualCheckpointRequest(
    string Id,
    VisualCheckpointPhase Phase,
    string Expectation,
    IReadOnlyList<VisualCaptureScope> Scopes,
    VisualCaptureRequiredness Requiredness = VisualCaptureRequiredness.BEST_EFFORT,
    bool FlushRing = false,
    bool IncludeInReview = true)
{
    private static readonly Regex IdPattern = new("^[a-z][a-z0-9._-]{1,80}$", RegexOptions.CultureInvariant);

    public void Validate(string name = "checkpoint")
    {
        if (string.IsNullOrWhiteSpace(Id) || !IdPattern.IsMatch(Id))
            throw new ArgumentException($"{name}.id is not a stable checkpoint identifier.", name);
        if (string.IsNullOrWhiteSpace(Expectation) || Expectation.Length > 500)
            throw new ArgumentException($"{name}.expectation is empty or too long.", name);
        if (Scopes == null || Scopes.Count == 0)
            throw new ArgumentException($"{name} must declare at least one capture scope.", name);
        for (int i = 0; i < Scopes.Count; i++)
            Scopes[i].Validate($"{name}.scopes[{i}]");
    }
}

internal sealed record VisualEvidencePolicy(
    VisualEvidenceLevel Level,
    bool BuildReviewPacket,
    long MaxBytes,
    int MaxArtifacts,
    int MaxWidth,
    int MaxHeight,
    int RingMaxFrames,
    long RingMaxBytes,
    int RingDurationMilliseconds,
    double RingMaxFramesPerSecond,
    bool AllowVirtualDesktop)
{
    public static VisualEvidencePolicy Disabled { get; } = new(
        VisualEvidenceLevel.NONE,
        false,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        false);

    public static VisualEvidencePolicy SafeDefaults(VisualEvidenceLevel level = VisualEvidenceLevel.NONE, bool buildReviewPacket = false)
        => new(
            level,
            buildReviewPacket,
            16L * 1024 * 1024,
            64,
            4096,
            4096,
            12,
            8L * 1024 * 1024,
            6000,
            2.0,
            false);

    public bool Enabled => Level != VisualEvidenceLevel.NONE;

    public void Validate()
    {
        if (Level == VisualEvidenceLevel.NONE)
        {
            if (BuildReviewPacket || MaxBytes != 0 || MaxArtifacts != 0 || MaxWidth != 0 || MaxHeight != 0
                || RingMaxFrames != 0 || RingMaxBytes != 0 || RingDurationMilliseconds != 0
                || RingMaxFramesPerSecond != 0 || AllowVirtualDesktop)
            {
                throw new ArgumentException("disabled visual policy cannot carry capture budgets or authorization.");
            }
            return;
        }

        if (MaxBytes <= 0 || MaxBytes > 256L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaxBytes));
        if (MaxArtifacts <= 0 || MaxArtifacts > 1024)
            throw new ArgumentOutOfRangeException(nameof(MaxArtifacts));
        if (MaxWidth <= 0 || MaxWidth > 16384 || MaxHeight <= 0 || MaxHeight > 16384)
            throw new ArgumentOutOfRangeException(nameof(MaxWidth));
        if (RingMaxFrames <= 0 || RingMaxFrames > 256 || RingMaxBytes <= 0 || RingMaxBytes > MaxBytes)
            throw new ArgumentOutOfRangeException(nameof(RingMaxFrames));
        if (RingDurationMilliseconds <= 0 || RingDurationMilliseconds > 60_000)
            throw new ArgumentOutOfRangeException(nameof(RingDurationMilliseconds));
        if (double.IsNaN(RingMaxFramesPerSecond) || double.IsInfinity(RingMaxFramesPerSecond)
            || RingMaxFramesPerSecond <= 0 || RingMaxFramesPerSecond > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(RingMaxFramesPerSecond));
        }
    }

    public VisualEvidencePolicy WithBudgets(long maxBytes, int maxArtifacts)
    {
        var updated = this with
        {
            MaxBytes = maxBytes,
            MaxArtifacts = maxArtifacts,
            RingMaxBytes = Math.Min(RingMaxBytes, maxBytes),
        };
        updated.Validate();
        return updated;
    }
}

internal sealed record VisualArtifactRecord(
    string ArtifactId,
    string CheckpointId,
    VisualCheckpointPhase Phase,
    string RelativePath,
    string MimeType,
    string Sha256,
    long SizeBytes,
    int Width,
    int Height,
    DateTimeOffset CapturedUtc,
    long Sequence,
    long RelativeMilliseconds,
    VisualCaptureMethod CaptureMethod,
    VisualCaptureScopeKind ScopeKind,
    VisualPrivacyClass Privacy,
    VisualRect RequestedRect,
    VisualRect ActualRect,
    int Dpi,
    string MonitorId,
    VisualTargetIdentity? Target,
    string Expectation,
    bool Derived,
    string? SourceArtifactId,
    long CaptureDurationMilliseconds,
    VisualCaptureRequiredness Requiredness = VisualCaptureRequiredness.BEST_EFFORT,
    bool IncludeInReview = true)
{
    public void Validate(string name = "artifact")
    {
        if (string.IsNullOrWhiteSpace(ArtifactId) || string.IsNullOrWhiteSpace(CheckpointId)
            || string.IsNullOrWhiteSpace(RelativePath) || string.IsNullOrWhiteSpace(MimeType)
            || string.IsNullOrWhiteSpace(Sha256) || SizeBytes <= 0 || Width <= 0 || Height <= 0
            || string.IsNullOrWhiteSpace(MonitorId) || string.IsNullOrWhiteSpace(Expectation))
        {
            throw new ArgumentException($"{name} is incomplete.", name);
        }
        RequestedRect.Validate($"{name}.requestedRect");
        ActualRect.Validate($"{name}.actualRect");
        if (ActualRect.Width != Width || ActualRect.Height != Height)
            throw new ArgumentException($"{name}.actualRect dimensions disagree with image dimensions.", name);
        if (Dpi <= 0 || CaptureDurationMilliseconds < 0)
            throw new ArgumentException($"{name} contains invalid capture metadata.", name);
        Target?.Validate($"{name}.target");
        if (Derived && string.IsNullOrWhiteSpace(SourceArtifactId))
            throw new ArgumentException($"{name} derived artifacts require a source artifact.", name);
    }
}

internal sealed record VisualUnavailableRecord(
    string CheckpointId,
    VisualCheckpointPhase Phase,
    VisualCaptureScopeKind ScopeKind,
    VisualCaptureRequiredness Requiredness,
    string Reason,
    DateTimeOffset RecordedUtc)
{
    public void Validate(string name = "unavailable")
    {
        if (string.IsNullOrWhiteSpace(CheckpointId) || string.IsNullOrWhiteSpace(Reason))
            throw new ArgumentException($"{name} is incomplete.", name);
    }
}

internal sealed record VisualDerivedArtifactFailure(
    string FailureId,
    string ArtifactKind,
    string ArtifactId,
    string CheckpointId,
    string Scenario,
    int Attempt,
    string Reason,
    VisualCaptureRequiredness Requiredness,
    bool RawArtifactsPreserved,
    string[] SourceArtifactIds,
    DateTimeOffset RecordedUtc)
{
    public void Validate(string? expectedScenario = null, int? expectedAttempt = null, string name = "derivedArtifactFailure")
    {
        if (!IsStableId(FailureId)
            || !IsStableId(ArtifactKind)
            || !IsStableId(ArtifactId)
            || !IsStableId(CheckpointId)
            || string.IsNullOrWhiteSpace(Scenario)
            || Attempt < 1
            || string.IsNullOrWhiteSpace(Reason)
            || Reason.Length > 500
            || !Enum.IsDefined(Requiredness)
            || SourceArtifactIds is null
            || SourceArtifactIds.Any(source => !IsStableId(source))
            || SourceArtifactIds.Distinct(StringComparer.Ordinal).Count() != SourceArtifactIds.Length)
        {
            throw new ArgumentException($"{name} is incomplete or invalid.", name);
        }
        if (expectedScenario is not null
            && !string.Equals(Scenario, expectedScenario, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{name} scenario binding disagrees.", name);
        }
        if (expectedAttempt is int attempt && Attempt != attempt)
            throw new ArgumentException($"{name} attempt binding disagrees.", name);
    }

    private static bool IsStableId(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= 128
            && value.All(character =>
                (character is >= 'a' and <= 'z')
                || (character is >= 'A' and <= 'Z')
                || (character is >= '0' and <= '9')
                || character is '.' or '-' or '_');
}


internal sealed record VisualEvidenceCounterSnapshot(
    int CapturesRequested,
    int CapturesSucceeded,
    int CapturesFailed,
    int CapturesSkipped,
    int FramesEvicted,
    int FramesFlushed,
    long BytesRetained,
    long EncodeMilliseconds,
    long TriggerFrameMilliseconds = 0)
{
    public void Validate()
    {
        if (CapturesRequested < 0 || CapturesSucceeded < 0 || CapturesFailed < 0 || CapturesSkipped < 0
            || FramesEvicted < 0 || FramesFlushed < 0 || BytesRetained < 0
            || EncodeMilliseconds < 0 || TriggerFrameMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(CapturesRequested));
    }
}

internal sealed class VisualEvidenceCounters
{
    public int CapturesRequested { get; private set; }
    public int CapturesSucceeded { get; private set; }
    public int CapturesFailed { get; private set; }
    public int CapturesSkipped { get; private set; }
    public int FramesEvicted { get; private set; }
    public int FramesFlushed { get; private set; }
    public long BytesRetained { get; private set; }
    public long EncodeMilliseconds { get; private set; }
    public long TriggerFrameMilliseconds { get; private set; }

    public void Requested() => CapturesRequested++;
    public void Succeeded() => CapturesSucceeded++;
    public void Failed() => CapturesFailed++;
    public void Skipped() => CapturesSkipped++;
    public void Evicted() => FramesEvicted++;
    public void Flushed() => FramesFlushed++;
    public void RetainBytes(long bytes) => BytesRetained += bytes;
    public void AddEncodeMilliseconds(long milliseconds) => EncodeMilliseconds += Math.Max(0, milliseconds);
    public void AddTriggerFrameMilliseconds(long milliseconds)
        => TriggerFrameMilliseconds += Math.Max(0, milliseconds);

    public VisualEvidenceCounterSnapshot Snapshot()
        => new(CapturesRequested, CapturesSucceeded, CapturesFailed, CapturesSkipped,
            FramesEvicted, FramesFlushed, BytesRetained, EncodeMilliseconds, TriggerFrameMilliseconds);
}

internal readonly record struct VisualNormalizedRect(double Left, double Top, double Right, double Bottom)
{
    public void Validate(string name)
    {
        if (double.IsNaN(Left) || double.IsNaN(Top) || double.IsNaN(Right) || double.IsNaN(Bottom)
            || double.IsInfinity(Left) || double.IsInfinity(Top)
            || double.IsInfinity(Right) || double.IsInfinity(Bottom)
            || Left < 0 || Top < 0 || Right > 1 || Bottom > 1 || Right <= Left || Bottom <= Top)
        {
            throw new ArgumentException($"{name} must be a positive normalized rectangle.", name);
        }
}
}

internal sealed record VisualReviewImageReference(
    string ArtifactId,
    string CheckpointId,
    string RelativePath,
    string Sha256,
    long RelativeMilliseconds,
    bool Derived)
{
    public void Validate(string name = "reviewImage")
    {
        if (string.IsNullOrWhiteSpace(ArtifactId)
            || string.IsNullOrWhiteSpace(CheckpointId)
            || string.IsNullOrWhiteSpace(RelativePath)
            || !Regex.IsMatch(Sha256 ?? string.Empty, "^[0-9a-fA-F]{64}$")
            || RelativeMilliseconds < 0)
        {
            throw new ArgumentException($"{name} is incomplete.", name);
        }
    }
}

internal sealed record VisualReviewCheckpoint(
    string CheckpointId,
    VisualCheckpointPhase Phase,
    string Expectation,
    VisualCaptureRequiredness Requiredness,
    string[] ArtifactIds)
{
    public void Validate(string name = "reviewCheckpoint")
    {
        if (string.IsNullOrWhiteSpace(CheckpointId) || string.IsNullOrWhiteSpace(Expectation)
            || Expectation.Length > 500 || ArtifactIds == null || ArtifactIds.Length == 0)
        {
            throw new ArgumentException($"{name} is incomplete.", name);
        }
    }
}

internal sealed record VisualReviewPacket(
    string Schema,
    int SchemaVersion,
    string CandidateSha,
    string RunId,
    string Scenario,
    int Attempt,
    DateTimeOffset CreatedUtc,
    string VisualManifestPath,
    VisualReviewCheckpoint[] Checkpoints,
    VisualReviewImageReference[] Images,
    string? ContactSheetArtifactId,
    string? ContactSheetPath,
    string? ContactSheetSha256,
    Dictionary<string, string>? CorrelatedFacts,
    string[] EnvironmentNotes,
    string[] ProhibitedInferenceReminders,
    string InstructionsPath,
    string RequiredResultPath,
    string RequiredResultSchema,
    VisualDerivedArtifactFailure[] DerivedArtifactFailures)
{
    public void Validate()
    {
        if (!string.Equals(Schema, VisualEvidenceSchema.ReviewPacket, StringComparison.Ordinal)
            || SchemaVersion != VisualEvidenceSchema.CurrentVersion
            || string.IsNullOrWhiteSpace(CandidateSha)
            || string.IsNullOrWhiteSpace(RunId)
            || string.IsNullOrWhiteSpace(Scenario)
            || Attempt < 1
            || !IsPortableRelativePath(VisualManifestPath)
            || !IsPortableRelativePath(InstructionsPath)
            || !IsPortableRelativePath(RequiredResultPath)
            || !string.Equals(RequiredResultSchema, VisualEvidenceSchema.ReviewResult, StringComparison.Ordinal)
            || Checkpoints is null || Checkpoints.Length == 0
            || Images is null || Images.Length == 0
            || EnvironmentNotes is null
            || ProhibitedInferenceReminders is null || ProhibitedInferenceReminders.Length == 0
            || DerivedArtifactFailures is null)
        {
            throw new ArgumentException("visual review packet identity or contents are invalid.");
        }
        var imageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (VisualReviewImageReference image in Images)
        {
            image.Validate();
            if (!IsPortableRelativePath(image.RelativePath) || !imageIds.Add(image.ArtifactId))
                throw new ArgumentException("visual review packet contains an invalid or duplicate image reference.");
        }
        var checkpointIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (VisualReviewCheckpoint checkpoint in Checkpoints)
        {
            checkpoint.Validate();
            if (!checkpointIds.Add(checkpoint.CheckpointId))
                throw new ArgumentException("visual review packet contains duplicate checkpoint IDs.");
            foreach (string artifactId in checkpoint.ArtifactIds)
            {
                if (!imageIds.Contains(artifactId))
                    throw new ArgumentException($"visual review checkpoint references unknown artifact '{artifactId}'.");
            }
        }
        var failureIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (VisualDerivedArtifactFailure failure in DerivedArtifactFailures)
        {
            failure.Validate(Scenario, Attempt);
            if (!failureIds.Add(failure.FailureId))
                throw new ArgumentException($"visual review packet contains duplicate derived failure '{failure.FailureId}'.");
            foreach (string sourceArtifactId in failure.SourceArtifactIds)
            {
                if (!imageIds.Contains(sourceArtifactId))
                    throw new ArgumentException($"visual review packet derived failure references unknown artifact '{sourceArtifactId}'.");
            }
        }
        if (ContactSheetArtifactId != null
            || ContactSheetPath != null
            || ContactSheetSha256 != null)
        {
            if (string.IsNullOrWhiteSpace(ContactSheetArtifactId)
                || !IsPortableRelativePath(ContactSheetPath)
                || !Regex.IsMatch(ContactSheetSha256 ?? string.Empty, "^[0-9a-fA-F]{64}$"))
            {
                throw new ArgumentException("contact-sheet reference is incomplete.");
            }
        }
    }

    private static bool IsPortableRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        string normalized = value.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }
        string[] segments = normalized.Split('/');
        return segments.Length > 0
            && segments.All(segment => segment.Length > 0 && segment is not "." and not "..");
    }
}

internal sealed record VisualReviewReviewedImage(
    string ArtifactId,
    string CheckpointId,
    string Sha256)
{
    public void Validate(string name = "reviewedImage")
    {
        if (string.IsNullOrWhiteSpace(ArtifactId)
            || string.IsNullOrWhiteSpace(CheckpointId)
            || !Regex.IsMatch(Sha256 ?? string.Empty, "^[0-9a-fA-F]{64}$"))
        {
            throw new ArgumentException($"{name} is incomplete.", name);
        }
    }
}

internal sealed record VisualReviewFinding(
    string FindingId,
    string ArtifactId,
    string CheckpointId,
    string ImageSha256,
    VisualFindingCategory Category,
    string Severity,
    string Description,
    string Expected,
    string Observed,
    string Uncertainty,
    VisualNormalizedRect? Region)
{
    public void Validate(string name = "finding")
    {
        if (string.IsNullOrWhiteSpace(FindingId)
            || string.IsNullOrWhiteSpace(ArtifactId)
            || string.IsNullOrWhiteSpace(CheckpointId)
            || !Regex.IsMatch(ImageSha256 ?? string.Empty, "^[0-9a-fA-F]{64}$")
            || string.IsNullOrWhiteSpace(Severity)
            || string.IsNullOrWhiteSpace(Description)
            || string.IsNullOrWhiteSpace(Expected)
            || string.IsNullOrWhiteSpace(Observed)
            || string.IsNullOrWhiteSpace(Uncertainty))
        {
            throw new ArgumentException($"{name} is incomplete.", name);
        }
        if (Region is VisualNormalizedRect region)
            region.Validate($"{name}.region");
    }
}

internal sealed record VisualReviewResult(
    string Schema,
    int SchemaVersion,
    string PacketSha256,
    string CandidateSha,
    string RunId,
    string Scenario,
    int Attempt,
    VisualReviewVerdict Verdict,
    string ReviewerKind,
    string ReviewerId,
    DateTimeOffset ReviewedUtc,
    VisualReviewReviewedImage[] ReviewedImages,
    VisualReviewFinding[] Findings,
    string Notes,
    string[] AcknowledgedDerivedFailureIds)
{
    public void Validate()
    {
        if (!string.Equals(Schema, VisualEvidenceSchema.ReviewResult, StringComparison.Ordinal)
            || SchemaVersion != VisualEvidenceSchema.CurrentVersion
            || !Enum.IsDefined(Verdict)
            || !Regex.IsMatch(PacketSha256 ?? string.Empty, "^[0-9a-fA-F]{64}$")
            || string.IsNullOrWhiteSpace(CandidateSha)
            || string.IsNullOrWhiteSpace(RunId)
            || string.IsNullOrWhiteSpace(Scenario)
            || Attempt < 1
            || string.IsNullOrWhiteSpace(ReviewerKind)
            || string.IsNullOrWhiteSpace(ReviewerId)
            || ReviewedImages is null
            || Findings is null
            || Notes is null
            || AcknowledgedDerivedFailureIds is null)
        {
            throw new ArgumentException("visual review result identity or contents are invalid.");
        }
        var imageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (VisualReviewReviewedImage image in ReviewedImages)
        {
            image.Validate();
            if (!imageIds.Add(image.ArtifactId))
                throw new ArgumentException("visual review result contains duplicate reviewed artifact IDs.");
        }
        var findingIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (VisualReviewFinding finding in Findings)
        {
            finding.Validate();
            if (!findingIds.Add(finding.FindingId))
                throw new ArgumentException("visual review result contains duplicate finding IDs.");
        }
        var acknowledged = new HashSet<string>(StringComparer.Ordinal);
        foreach (string failureId in AcknowledgedDerivedFailureIds)
        {
            if (string.IsNullOrWhiteSpace(failureId) || !acknowledged.Add(failureId))
                throw new ArgumentException("visual review result contains duplicate or empty derived-failure acknowledgements.");
        }
    }
}

internal sealed record VisualEvidenceManifest(
    string Schema,
    int SchemaVersion,
    string CandidateSha,
    string RunId,
    string Scenario,
    int Attempt,
    DateTimeOffset StartedUtc,
    DateTimeOffset EndedUtc,
    VisualEvidencePolicy Policy,
    VisualArtifactRecord[] Artifacts,
    VisualUnavailableRecord[] Unavailable,
    VisualEvidenceCounterSnapshot Counters,
    string? ReviewPacketPath,
    string? ReviewPacketSha256,
    VisualDerivedArtifactFailure[] DerivedArtifactFailures)
{
    public void Validate()
    {
        if (!string.Equals(Schema, VisualEvidenceSchema.Manifest, StringComparison.Ordinal)
            || SchemaVersion != VisualEvidenceSchema.CurrentVersion
            || string.IsNullOrWhiteSpace(CandidateSha)
            || string.IsNullOrWhiteSpace(RunId)
            || string.IsNullOrWhiteSpace(Scenario)
            || Attempt < 1
            || EndedUtc < StartedUtc
            || Policy is null
            || Counters is null
            || Artifacts is null
            || Unavailable is null
            || DerivedArtifactFailures is null)
        {
            throw new ArgumentException("visual evidence manifest identity, timestamps, or collections are invalid.");
        }
        Policy.Validate();
        Counters.Validate();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (VisualArtifactRecord artifact in Artifacts)
        {
            artifact.Validate();
            if (!ids.Add(artifact.ArtifactId))
                throw new ArgumentException($"duplicate visual artifact ID '{artifact.ArtifactId}'.");
            if (!paths.Add(artifact.RelativePath))
                throw new ArgumentException($"duplicate visual artifact path '{artifact.RelativePath}'.");
        }
        foreach (VisualUnavailableRecord unavailable in Unavailable)
            unavailable.Validate();
        var failureIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (VisualDerivedArtifactFailure failure in DerivedArtifactFailures)
        {
            failure.Validate(Scenario, Attempt);
            if (!failureIds.Add(failure.FailureId))
                throw new ArgumentException($"duplicate derived artifact failure ID '{failure.FailureId}'.");
        }
        if (ReviewPacketPath != null
            && (!IsSha256(ReviewPacketSha256) || string.IsNullOrWhiteSpace(ReviewPacketPath)))
        {
            throw new ArgumentException("review packet path requires a packet hash.");
        }
        if (ReviewPacketPath == null && ReviewPacketSha256 != null)
            throw new ArgumentException("review packet hash requires a packet path.");
    }

    private static bool IsSha256(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length == 64
            && value.All(Uri.IsHexDigit);
}

internal static class VisualJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static T Deserialize<T>(byte[] utf8Json)
        where T : class
    {
        if (utf8Json == null)
            throw new ArgumentNullException(nameof(utf8Json));
        T? value = JsonSerializer.Deserialize<T>(utf8Json, Options);
        return value ?? throw new JsonException($"JSON value for {typeof(T).Name} is null.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new StrictCollectionConverter<VisualReviewPacket>(
            "checkpoints",
            "images",
            "environmentNotes",
            "prohibitedInferenceReminders",
            "derivedArtifactFailures"));
        options.Converters.Add(new StrictCollectionConverter<VisualReviewResult>(
            "reviewedImages",
            "findings",
            "acknowledgedDerivedFailureIds"));
        options.Converters.Add(new StrictCollectionConverter<VisualEvidenceManifest>(
            "artifacts",
            "unavailable",
            "derivedArtifactFailures"));
        return options;
    }

    private sealed class StrictCollectionConverter<T> : JsonConverter<T>
        where T : class
    {
        private readonly string[] _requiredArrayProperties;

        public StrictCollectionConverter(params string[] requiredArrayProperties)
            => _requiredArrayProperties = requiredArrayProperties;

        public override T Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException($"{typeToConvert.Name} must be a JSON object.");
            foreach (string propertyName in _requiredArrayProperties)
            {
                if (!document.RootElement.TryGetProperty(propertyName, out JsonElement property)
                    || property.ValueKind != JsonValueKind.Array)
                {
                    throw new JsonException(
                        $"{typeToConvert.Name} requires non-null array property '{propertyName}'.");
                }
            }

            var nestedOptions = new JsonSerializerOptions(options);
            nestedOptions.Converters.Remove(this);
            T? value = JsonSerializer.Deserialize<T>(
                document.RootElement.GetRawText(),
                nestedOptions);
            return value ?? throw new JsonException($"{typeToConvert.Name} deserialized to null.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            T value,
            JsonSerializerOptions options)
        {
            var nestedOptions = new JsonSerializerOptions(options);
            nestedOptions.Converters.Remove(this);
            JsonSerializer.Serialize(writer, value, nestedOptions);
        }
    }
}
