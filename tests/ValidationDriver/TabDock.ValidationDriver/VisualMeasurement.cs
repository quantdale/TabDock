using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TabDock.ValidationDriver;

internal enum VisualMeasurementMode
{
    DISABLED,
    CHECKPOINTS,
    CHECKPOINTS_PLUS_PACKET,
    FLIGHT_HEALTHY_DISCARD,
    FLIGHT_FAILURE_FLUSH,
}

internal enum VisualMeasurementClassification
{
    SYNTHETIC,
    PHYSICAL,
}

/// <summary>Privacy-safe machine/topology labels for a measurement cell.</summary>
internal sealed record VisualMachineTopology(
    string MachineClass,
    string TopologyClass,
    int MonitorCount,
    int Dpi,
    string DisplayClass)
{
    public void Validate(string name = "machineTopology")
    {
        if (!IsStableToken(MachineClass)
            || !IsStableToken(TopologyClass)
            || MonitorCount < 1
            || Dpi < 1
            || !IsStableToken(DisplayClass))
        {
            throw new ArgumentException($"{name} is incomplete or contains a non-portable label.", name);
        }
    }

    private static bool IsStableToken(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= 80
            && value.All(character =>
                character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '.' or '-' or '_');
}

/// <summary>Independent timing phases; control overhead is not visual work.</summary>
internal sealed record VisualMeasurementTiming(
    long ControlOverheadMilliseconds,
    long CaptureMilliseconds,
    long PngEncodeMilliseconds,
    long WriteMilliseconds,
    long HashMilliseconds,
    long ManifestMilliseconds,
    long ContactSheetMilliseconds,
    long PacketMilliseconds,
    long InstructionsMilliseconds,
    long FlushMilliseconds,
    long DiscardMilliseconds,
    long TriggerFrameMilliseconds = 0)
{
    public bool HasVisualWork
        => CaptureMilliseconds > 0
            || PngEncodeMilliseconds > 0
            || WriteMilliseconds > 0
            || HashMilliseconds > 0
            || ManifestMilliseconds > 0
            || ContactSheetMilliseconds > 0
            || PacketMilliseconds > 0
            || InstructionsMilliseconds > 0
            || FlushMilliseconds > 0
            || DiscardMilliseconds > 0
            || TriggerFrameMilliseconds > 0;

    public void Validate(string name = "timing")
    {
        if (ControlOverheadMilliseconds < 0
            || CaptureMilliseconds < 0
            || PngEncodeMilliseconds < 0
            || WriteMilliseconds < 0
            || HashMilliseconds < 0
            || ManifestMilliseconds < 0
            || ContactSheetMilliseconds < 0
            || PacketMilliseconds < 0
            || InstructionsMilliseconds < 0
            || FlushMilliseconds < 0
            || DiscardMilliseconds < 0
            || TriggerFrameMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(name, "measurement timings cannot be negative.");
        }
    }
}

/// <summary>Counts visual operations without retaining pixels in the report.</summary>
internal sealed record VisualMeasurementWork(
    int CaptureRequests,
    int CapturesSucceeded,
    int CapturesFailed,
    int PngEncodes,
    int ContactSheetBuilds,
    int PacketBuilds,
    int InstructionBuilds,
    int WorkerActivations,
    int TimerActivations,
    int RetainedFrames,
    long RetainedBytes,
    int ArtifactCount,
    long ArtifactBytes,
    int RingEvictions,
    int RingFlushes)
{
    public bool IsZero
        => CaptureRequests == 0
            && CapturesSucceeded == 0
            && CapturesFailed == 0
            && PngEncodes == 0
            && ContactSheetBuilds == 0
            && PacketBuilds == 0
            && InstructionBuilds == 0
            && WorkerActivations == 0
            && TimerActivations == 0
            && RetainedFrames == 0
            && RetainedBytes == 0
            && ArtifactCount == 0
            && ArtifactBytes == 0
            && RingEvictions == 0
            && RingFlushes == 0;

    public void Validate(string name = "work")
    {
        if (CaptureRequests < 0 || CapturesSucceeded < 0 || CapturesFailed < 0
            || PngEncodes < 0 || ContactSheetBuilds < 0 || PacketBuilds < 0
            || InstructionBuilds < 0 || WorkerActivations < 0 || TimerActivations < 0
            || RetainedFrames < 0 || RetainedBytes < 0 || ArtifactCount < 0
            || ArtifactBytes < 0 || RingEvictions < 0 || RingFlushes < 0)
        {
            throw new ArgumentOutOfRangeException(name, "visual work counters cannot be negative.");
        }
    }
}

/// <summary>
/// Resource-only observation attached to a visual sample. Null optional counters
/// remain unavailable; this type never converts missing native data to zero.
/// </summary>
internal sealed record VisualResourceObservation(
    VisualMeasurementMode Mode,
    ResourceProcessIdentity ProcessIdentity,
    long? ProcessHandleCount,
    long? UserObjectCount,
    long? GdiObjectCount,
    long? HBitmapCount,
    long? HdcCount,
    long? FileHandleCount,
    long? PrivateBytes,
    long? WorkingSet,
    long? ThreadCount,
    long? WorkerThreadCount,
    long? TimerCount,
    long? TabDockOwnedWindowCount,
    long? RingBytes,
    long? ArtifactCount,
    long? ArtifactBytes,
    string? MeasurementError = null)
{
    public static VisualResourceObservation FromSnapshot(
        ResourceSnapshot snapshot,
        VisualMeasurementMode mode,
        long? hBitmapCount = null,
        long? hdcCount = null,
        long? fileHandleCount = null,
        long? workerThreadCount = null,
        long? timerCount = null,
        long? tabDockOwnedWindowCount = null,
        long? ringBytes = null,
        long? artifactCount = null,
        long? artifactBytes = null)
        => new(
            mode,
            snapshot.ProcessIdentity,
            snapshot.HandleCount,
            snapshot.UserObjectCount,
            snapshot.GdiObjectCount,
            hBitmapCount,
            hdcCount,
            fileHandleCount,
            snapshot.PrivateBytes,
            snapshot.WorkingSet,
            snapshot.ThreadCount,
            workerThreadCount,
            timerCount,
            tabDockOwnedWindowCount ?? snapshot.TopLevelWindowCount,
            ringBytes,
            artifactCount,
            artifactBytes,
            snapshot.MeasurementError);

    public long? Value(string metric) => metric switch
    {
        "processHandleCount" => ProcessHandleCount,
        "userObjectCount" => UserObjectCount,
        "gdiObjectCount" => GdiObjectCount,
        "hBitmapCount" => HBitmapCount,
        "hdcCount" => HdcCount,
        "fileHandleCount" => FileHandleCount,
        "privateBytes" => PrivateBytes,
        "workingSet" => WorkingSet,
        "threadCount" => ThreadCount,
        "workerThreadCount" => WorkerThreadCount,
        "timerCount" => TimerCount,
        "tabDockOwnedWindowCount" => TabDockOwnedWindowCount,
        "ringBytes" => RingBytes,
        "artifactCount" => ArtifactCount,
        "artifactBytes" => ArtifactBytes,
        _ => null,
    };

    public void Validate(string name = "resource")
    {
        if (!Enum.IsDefined(Mode)
            || !ProcessIdentity.IsValid
            || MeasurementError?.Length > 500)
        {
            throw new ArgumentException($"{name} identity or error state is invalid.", name);
        }

        foreach (long? value in new long?[]
        {
            ProcessHandleCount,
            UserObjectCount,
            GdiObjectCount,
            HBitmapCount,
            HdcCount,
            FileHandleCount,
            PrivateBytes,
            WorkingSet,
            ThreadCount,
            WorkerThreadCount,
            TimerCount,
            TabDockOwnedWindowCount,
            RingBytes,
            ArtifactCount,
            ArtifactBytes,
        })
        {
            if (value is < 0)
                throw new ArgumentOutOfRangeException(name, "resource counters cannot be negative.");
        }
    }
}

internal sealed record VisualMeasurementSample(
    DateTimeOffset RecordedUtc,
    string CandidateSha,
    string RunId,
    string Scenario,
    string Configuration,
    int Attempt,
    int SampleNumber,
    VisualMeasurementMode Mode,
    VisualMeasurementClassification Classification,
    VisualMachineTopology MachineTopology,
    VisualEvidencePolicy Policy,
    int? Width,
    int? Height,
    VisualCaptureMethod? CaptureMethod,
    VisualCaptureScopeKind? ScopeKind,
    VisualMeasurementTiming Timing,
    VisualMeasurementWork Work,
    long? ManagedAllocationDeltaBytes,
    double? CpuMilliseconds,
    VisualResourceObservation BeforeResources,
    VisualResourceObservation AfterResources,
    int RingOccupancy,
    long PeakRingBytes,
    bool HealthyFlightDiscarded,
    bool CleanupCompleted,
    string? CancellationOutcome = null,
    string? OutlierNote = null,
    long? PeakPrivateBytes = null,
    long? PeakWorkingSet = null)
{
    public void Validate(string name = "sample")
    {
        if (RecordedUtc == default
            || string.IsNullOrWhiteSpace(CandidateSha)
            || string.IsNullOrWhiteSpace(RunId)
            || !IsStableToken(Scenario)
            || !IsStableToken(Configuration)
            || Attempt < 1
            || SampleNumber < 1
            || !Enum.IsDefined(Mode)
            || !Enum.IsDefined(Classification)
            || Policy is null
            || BeforeResources is null
            || AfterResources is null
            || RingOccupancy < 0
            || PeakRingBytes < 0
            || ManagedAllocationDeltaBytes is < 0
            || CpuMilliseconds is < 0
            || PeakPrivateBytes is < 0
            || PeakWorkingSet is < 0
            || double.IsNaN(CpuMilliseconds ?? 0)
            || double.IsInfinity(CpuMilliseconds ?? 0))
        {
            throw new ArgumentException($"{name} identity or scalar values are invalid.", name);
        }

        MachineTopology.Validate($"{name}.machineTopology");
        Policy.Validate();
        Timing.Validate($"{name}.timing");
        Work.Validate($"{name}.work");
        BeforeResources.Validate($"{name}.beforeResources");
        AfterResources.Validate($"{name}.afterResources");
        if (BeforeResources.Mode != Mode || AfterResources.Mode != Mode)
            throw new ArgumentException($"{name} resource observations must use the sample mode.", name);

        bool dimensionsProvided = Width.HasValue || Height.HasValue;
        if (dimensionsProvided && (!Width.HasValue || !Height.HasValue || Width <= 0 || Height <= 0))
            throw new ArgumentException($"{name} dimensions must be both positive or both absent.", name);
        if (CaptureMethod.HasValue != dimensionsProvided || ScopeKind.HasValue != dimensionsProvided)
            throw new ArgumentException($"{name} capture metadata must match dimensions.", name);
        if (Mode == VisualMeasurementMode.DISABLED
            && (Policy.Level != VisualEvidenceLevel.NONE
                || dimensionsProvided
                || !Work.IsZero
                || Timing.HasVisualWork
                || RingOccupancy != 0
                || PeakRingBytes != 0))
        {
            throw new ArgumentException($"{name} disabled mode carries visual work.", name);
        }
    }

    private static bool IsStableToken(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= 128
            && !value.Contains('\\', StringComparison.Ordinal)
            && !value.Contains('/', StringComparison.Ordinal)
            && !value.Contains(':', StringComparison.Ordinal);
}

internal sealed record VisualMeasurementStatistic(
    string Metric,
    string Units,
    bool Available,
    int SampleCount,
    double? Median,
    double? P95,
    double? Maximum,
    string? Limitation,
    string? OutlierNote)
{
    public void Validate(string name = "statistic")
    {
        if (string.IsNullOrWhiteSpace(Metric)
            || string.IsNullOrWhiteSpace(Units)
            || SampleCount < 0
            || (!Available && (Median.HasValue || P95.HasValue || Maximum.HasValue))
            || (Available && (SampleCount < 1 || !Median.HasValue || !Maximum.HasValue))
            || (P95.HasValue && SampleCount < 20)
            || (Available && (double.IsNaN(Median!.Value)
                || double.IsInfinity(Median.Value)
                || double.IsNaN(Maximum!.Value)
                || double.IsInfinity(Maximum.Value)))
            || (P95.HasValue && (double.IsNaN(P95.Value) || double.IsInfinity(P95.Value)))
            || Limitation?.Length > 500
            || OutlierNote?.Length > 500)
        {
            throw new ArgumentException($"{name} is incomplete or statistically invalid.", name);
        }
    }
}

internal sealed record VisualMeasurementCell(
    string CellKey,
    string Scenario,
    VisualMeasurementMode Mode,
    VisualMeasurementClassification Classification,
    string CandidateSha,
    string Configuration,
    VisualMachineTopology MachineTopology,
    int SampleCount,
    IReadOnlyList<VisualMeasurementStatistic> Statistics,
    string? Limitation)
{
    public void Validate(string name = "cell")
    {
        if (string.IsNullOrWhiteSpace(CellKey)
            || string.IsNullOrWhiteSpace(Scenario)
            || !Enum.IsDefined(Mode)
            || !Enum.IsDefined(Classification)
            || string.IsNullOrWhiteSpace(CandidateSha)
            || string.IsNullOrWhiteSpace(Configuration)
            || SampleCount < 1
            || Statistics is null
            || Statistics.Count != VisualMeasurementReportBuilder.Metrics.Length)
        {
            throw new ArgumentException($"{name} identity or collections are invalid.", name);
        }
        MachineTopology.Validate($"{name}.machineTopology");
        var metrics = new HashSet<string>(StringComparer.Ordinal);
        foreach (VisualMeasurementStatistic statistic in Statistics)
        {
            statistic.Validate();
            if (!metrics.Add(statistic.Metric))
                throw new ArgumentException($"{name} contains duplicate metric '{statistic.Metric}'.", name);
        }
        if (VisualMeasurementReportBuilder.Metrics.Any(definition => !metrics.Contains(definition.Metric)))
            throw new ArgumentException($"{name} does not contain the complete metric set.", name);
    }
}

internal sealed record VisualMeasurementBudget(
    string CellKey,
    string Metric,
    double Limit,
    string Units,
    string Statistic,
    int SourceSampleCount,
    double SafetyMarginFraction,
    string SourceCandidateSha,
    bool HardCeiling,
    bool DiagnosticOnly,
    string Rationale,
    string? OutlierNote = null)
{
    public void Validate(string name = "budget")
    {
        if (string.IsNullOrWhiteSpace(CellKey)
            || string.IsNullOrWhiteSpace(Metric)
            || double.IsNaN(Limit)
            || double.IsInfinity(Limit)
            || Limit < 0
            || string.IsNullOrWhiteSpace(Units)
            || Statistic is not ("p95" or "maximum")
            || SourceSampleCount < 1
            || double.IsNaN(SafetyMarginFraction)
            || double.IsInfinity(SafetyMarginFraction)
            || SafetyMarginFraction < 0
            || SafetyMarginFraction > 10
            || string.IsNullOrWhiteSpace(SourceCandidateSha)
            || string.IsNullOrWhiteSpace(Rationale)
            || Rationale.Length > 500
            || OutlierNote?.Length > 500
            || DiagnosticOnly == HardCeiling)
        {
            throw new ArgumentException($"{name} is incomplete or contradictory.", name);
        }
    }
}
internal sealed record VisualResourcePairComparison(
    string Scenario,
    int SampleNumber,
    VisualMeasurementMode EnabledMode,
    VisualMeasurementClassification Classification,
    string CandidateSha,
    string RunId,
    string Configuration,
    VisualMachineTopology MachineTopology,
    IReadOnlyDictionary<string, long?> ResourceDeltas,
    IReadOnlyDictionary<string, long> AllowedPositiveDeltas,
    string Outcome,
    string? Reason)
{
    public void Validate(string name = "pairComparison")
    {
        if (string.IsNullOrWhiteSpace(Scenario)
            || SampleNumber < 1
            || !Enum.IsDefined(EnabledMode)
            || EnabledMode == VisualMeasurementMode.DISABLED
            || !Enum.IsDefined(Classification)
            || string.IsNullOrWhiteSpace(CandidateSha)
            || string.IsNullOrWhiteSpace(RunId)
            || string.IsNullOrWhiteSpace(Configuration)
            || MachineTopology is null
            || ResourceDeltas is null
            || AllowedPositiveDeltas is null
            || !IsOutcome(Outcome)
            || Reason?.Length > 500
            || (Outcome == "PASS" && !string.IsNullOrWhiteSpace(Reason)))
        {
            throw new ArgumentException($"{name} identity or collections are invalid.", name);
        }

        MachineTopology.Validate($"{name}.machineTopology");
        bool unavailable = false;
        bool overBudget = false;
        foreach (string metric in VisualMeasurementGates.RequiredResourceMetrics)
        {
            if (!ResourceDeltas.TryGetValue(metric, out long? delta)
                || !AllowedPositiveDeltas.TryGetValue(metric, out long allowed)
                || allowed < 0)
            {
                throw new ArgumentException($"{name} is missing resource metric '{metric}'.", name);
            }
            unavailable |= delta is null;
            overBudget |= delta > allowed;
        }

        if (ResourceDeltas.Keys.Any(metric => !VisualMeasurementGates.RequiredResourceMetrics.Contains(metric, StringComparer.Ordinal))
            || AllowedPositiveDeltas.Keys.Any(metric => !VisualMeasurementGates.RequiredResourceMetrics.Contains(metric, StringComparer.Ordinal)))
        {
            throw new ArgumentException($"{name} contains an unknown resource metric.", name);
        }
        if (Outcome == "PASS" && (unavailable || overBudget))
            throw new ArgumentException($"{name} passes despite unavailable or over-budget resources.", name);
        if (Outcome != "PASS" && string.IsNullOrWhiteSpace(Reason))
            throw new ArgumentException($"{name} requires a reason for a non-pass outcome.", name);
    }

    private static bool IsOutcome(string? value)
        => value is "PASS" or "FAIL" or "BLOCKED";
}


internal sealed record VisualMeasurementReport(
    int SchemaVersion,
    string ArtifactKind,
    string CandidateSha,
    string RunId,
    string DriverSha,
    string Configuration,
    VisualMeasurementClassification Classification,
    DateTimeOffset StartedUtc,
    DateTimeOffset EndedUtc,
    IReadOnlyList<VisualMeasurementSample> Samples,
    IReadOnlyList<VisualMeasurementCell> Cells,
    IReadOnlyList<VisualMeasurementBudget> Budgets,
    string Outcome,
    string? Limitation,
    IReadOnlyList<VisualResourcePairComparison>? PairComparisons = null)
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentArtifactKind = "visual-performance-measurements";

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion
            || !string.Equals(ArtifactKind, CurrentArtifactKind, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(CandidateSha)
            || string.IsNullOrWhiteSpace(RunId)
            || string.IsNullOrWhiteSpace(DriverSha)
            || string.IsNullOrWhiteSpace(Configuration)
            || !Enum.IsDefined(Classification)
            || EndedUtc < StartedUtc
            || Samples is null
            || Samples.Count == 0
            || Cells is null
            || Cells.Count == 0
            || Budgets is null
            || !IsOutcome(Outcome))
        {
            throw new ArgumentException("visual measurement report identity or collections are invalid.");
        }

        var cellKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (VisualMeasurementCell cell in Cells)
        {
            cell.Validate();
            if (!cellKeys.Add(cell.CellKey))
                throw new ArgumentException($"duplicate visual measurement cell '{cell.CellKey}'.");
        }

        var sampleCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var sampleKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (VisualMeasurementSample sample in Samples)
        {
            sample.Validate();
            if (!string.Equals(sample.CandidateSha, CandidateSha, StringComparison.Ordinal)
                || !string.Equals(sample.RunId, RunId, StringComparison.Ordinal)
                || !string.Equals(sample.Configuration, Configuration, StringComparison.Ordinal)
                || sample.Classification != Classification)
            {
                throw new ArgumentException("visual measurement sample identity disagrees with its report.");
            }
            string key = CellKeyFor(sample);
            if (!cellKeys.Contains(key))
                throw new ArgumentException($"sample is not represented by cell '{key}'.");
            if (!sampleCounts.TryAdd(key, 1))
                sampleCounts[key]++;
            string sampleKey = key + "#" + sample.SampleNumber.ToString(CultureInfo.InvariantCulture);
            if (!sampleKeys.Add(sampleKey))
                throw new ArgumentException($"duplicate sample number in cell '{key}'.");
        }
        foreach (VisualMeasurementCell cell in Cells)
        {
            if (!sampleCounts.TryGetValue(cell.CellKey, out int actualCount)
                || actualCount != cell.SampleCount)
            {
                throw new ArgumentException($"cell '{cell.CellKey}' sample count disagrees with raw samples.");
            }
        }
        if (PairComparisons is null)
            throw new ArgumentException("visual measurement report must include pair comparisons.");
        IReadOnlyList<VisualResourcePairComparison> pairComparisons = PairComparisons;
        int enabledSampleCount = Samples.Count(sample => sample.Mode != VisualMeasurementMode.DISABLED);
        if (pairComparisons.Count != enabledSampleCount)
            throw new ArgumentException("visual measurement report must pair every enabled sample with a disabled baseline.");
        var pairKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (VisualMeasurementBudget budget in Budgets)
        {
            budget.Validate();
            if (!cellKeys.Contains(budget.CellKey))
                throw new ArgumentException($"budget references unknown cell '{budget.CellKey}'.");
        }
        foreach (VisualResourcePairComparison comparison in pairComparisons)
        {
            comparison.Validate();
            if (!string.Equals(comparison.CandidateSha, CandidateSha, StringComparison.Ordinal)
                || !string.Equals(comparison.RunId, RunId, StringComparison.Ordinal)
                || !string.Equals(comparison.Configuration, Configuration, StringComparison.Ordinal)
                || comparison.Classification != Classification)
            {
                throw new ArgumentException("visual resource pair identity disagrees with its report.");
            }

            string key = string.Join(
                "|",
                comparison.Scenario,
                comparison.EnabledMode,
                comparison.Classification,
                comparison.Configuration,
                comparison.MachineTopology.MachineClass,
                comparison.MachineTopology.TopologyClass,
                comparison.MachineTopology.DisplayClass,
                comparison.MachineTopology.Dpi.ToString(CultureInfo.InvariantCulture));
            if (!cellKeys.Contains(key))
                throw new ArgumentException($"pair comparison references unknown cell '{key}'.");
            if (!Samples.Any(sample =>
                    sample.Scenario == comparison.Scenario
                    && sample.SampleNumber == comparison.SampleNumber
                    && sample.Mode == comparison.EnabledMode
                    && sample.MachineTopology == comparison.MachineTopology))
            {
                throw new ArgumentException("pair comparison references an unknown enabled sample.");
            }
            if (!pairKeys.Add(key + "#" + comparison.SampleNumber.ToString(CultureInfo.InvariantCulture)))
                throw new ArgumentException("duplicate visual resource pair comparison.");
        }
        foreach (VisualMeasurementSample sample in Samples.Where(sample => sample.Mode != VisualMeasurementMode.DISABLED))
        {
            string key = CellKeyFor(sample) + "#" + sample.SampleNumber.ToString(CultureInfo.InvariantCulture);
            if (!pairKeys.Contains(key))
                throw new ArgumentException($"enabled sample '{key}' has no paired disabled baseline.");
        }
        bool underSampled = Cells.Any(cell => cell.SampleCount < 20);
        if (Outcome == "PASS" && underSampled)
            throw new ArgumentException("a PASS visual measurement report requires at least 20 samples per cell.");
        if (Outcome == "PROVISIONAL" && !underSampled)
            throw new ArgumentException("a PROVISIONAL visual measurement report must identify an under-sampled cell.");
        if ((Outcome == "PASS" || Outcome == "PROVISIONAL")
            && pairComparisons.Any(pair => pair.Outcome != "PASS"))
        {
            throw new ArgumentException("a non-failing visual measurement report cannot contain a failed or blocked pair.");
        }
    }

    internal static string CellKeyFor(VisualMeasurementSample sample)
        => string.Join(
            "|",
            sample.Scenario,
            sample.Mode,
            sample.Classification,
            sample.Configuration,
            sample.MachineTopology.MachineClass,
            sample.MachineTopology.TopologyClass,
            sample.MachineTopology.DisplayClass,
            sample.MachineTopology.Dpi.ToString(CultureInfo.InvariantCulture));

    private static bool IsOutcome(string? value)
        => value is "PASS" or "FAIL" or "BLOCKED" or "PROVISIONAL";

}

internal static class VisualMeasurementReportBuilder
{
    internal static readonly (string Metric, string Units, Func<VisualMeasurementSample, double?> Value)[] Metrics =
    {
        ("timing.controlOverheadMs", "ms", sample => sample.Timing.ControlOverheadMilliseconds),
        ("timing.captureMs", "ms", sample => sample.Timing.CaptureMilliseconds),
        ("timing.pngEncodeMs", "ms", sample => sample.Timing.PngEncodeMilliseconds),
        ("timing.writeMs", "ms", sample => sample.Timing.WriteMilliseconds),
        ("timing.hashMs", "ms", sample => sample.Timing.HashMilliseconds),
        ("timing.manifestMs", "ms", sample => sample.Timing.ManifestMilliseconds),
        ("timing.contactSheetMs", "ms", sample => sample.Timing.ContactSheetMilliseconds),
        ("timing.packetMs", "ms", sample => sample.Timing.PacketMilliseconds),
        ("timing.instructionsMs", "ms", sample => sample.Timing.InstructionsMilliseconds),
        ("timing.flushMs", "ms", sample => sample.Timing.FlushMilliseconds),
        ("timing.discardMs", "ms", sample => sample.Timing.DiscardMilliseconds),
        ("work.captureRequests", "count", sample => sample.Work.CaptureRequests),
        ("work.capturesSucceeded", "count", sample => sample.Work.CapturesSucceeded),
        ("work.capturesFailed", "count", sample => sample.Work.CapturesFailed),
        ("work.pngEncodes", "count", sample => sample.Work.PngEncodes),
        ("timing.triggerFrameMs", "ms", sample => sample.Timing.TriggerFrameMilliseconds),
        ("work.contactSheetBuilds", "count", sample => sample.Work.ContactSheetBuilds),
        ("work.packetBuilds", "count", sample => sample.Work.PacketBuilds),
        ("work.instructionBuilds", "count", sample => sample.Work.InstructionBuilds),
        ("work.retainedFrames", "count", sample => sample.Work.RetainedFrames),
        ("work.retainedBytes", "bytes", sample => sample.Work.RetainedBytes),
        ("work.artifactCount", "count", sample => sample.Work.ArtifactCount),
        ("work.artifactBytes", "bytes", sample => sample.Work.ArtifactBytes),
        ("ring.evictions", "count", sample => sample.Work.RingEvictions),
        ("ring.flushes", "count", sample => sample.Work.RingFlushes),
        ("ring.occupancy", "count", sample => sample.RingOccupancy),
        ("ring.peakBytes", "bytes", sample => sample.PeakRingBytes),
        ("memory.peakPrivateBytes", "bytes", sample => sample.PeakPrivateBytes),
        ("memory.peakWorkingSet", "bytes", sample => sample.PeakWorkingSet),
        ("allocation.managedDeltaBytes", "bytes", sample => sample.ManagedAllocationDeltaBytes),
        ("cpu.processMs", "ms", sample => sample.CpuMilliseconds),
        ("resource.processHandleDelta", "count", sample => Delta(sample, "processHandleCount")),
        ("resource.userObjectDelta", "count", sample => Delta(sample, "userObjectCount")),
        ("resource.gdiObjectDelta", "count", sample => Delta(sample, "gdiObjectCount")),
        ("resource.hBitmapDelta", "count", sample => Delta(sample, "hBitmapCount")),
        ("resource.hdcDelta", "count", sample => Delta(sample, "hdcCount")),
        ("resource.fileHandleDelta", "count", sample => Delta(sample, "fileHandleCount")),
        ("resource.privateBytesDelta", "bytes", sample => Delta(sample, "privateBytes")),
        ("resource.workingSetDelta", "bytes", sample => Delta(sample, "workingSet")),
        ("resource.threadDelta", "count", sample => Delta(sample, "threadCount")),
        ("resource.workerThreadDelta", "count", sample => Delta(sample, "workerThreadCount")),
        ("resource.timerDelta", "count", sample => Delta(sample, "timerCount")),
        ("resource.tabDockWindowDelta", "count", sample => Delta(sample, "tabDockOwnedWindowCount")),
        ("resource.ringBytesDelta", "bytes", sample => Delta(sample, "ringBytes")),
        ("resource.artifactCountDelta", "count", sample => Delta(sample, "artifactCount")),
        ("resource.artifactBytesDelta", "bytes", sample => Delta(sample, "artifactBytes")),
    };
    internal static bool TryGetMetricValue(
        VisualMeasurementSample sample,
        string metric,
        out double? value)
    {
        foreach ((string name, _, Func<VisualMeasurementSample, double?> getter) in Metrics)
        {
            if (string.Equals(name, metric, StringComparison.Ordinal))
            {
                value = getter(sample);
                return true;
            }
        }

        value = null;
        return false;
    }

    public static VisualMeasurementReport Build(
        IReadOnlyList<VisualMeasurementSample> samples,
        string driverSha,
        string? outcome = null,
        IReadOnlyList<VisualMeasurementBudget>? budgets = null,
        string? limitation = null)
    {
        if (samples is null || samples.Count == 0)
            throw new ArgumentException("at least one visual measurement sample is required.", nameof(samples));
        if (string.IsNullOrWhiteSpace(driverSha))
            throw new ArgumentException("driver identity is required.", nameof(driverSha));

        foreach (VisualMeasurementSample sample in samples)
            sample.Validate();
        VisualMeasurementSample first = samples[0];
        var cells = new List<VisualMeasurementCell>();
        foreach (IGrouping<string, VisualMeasurementSample> group in samples
                     .GroupBy(VisualMeasurementReport.CellKeyFor, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            VisualMeasurementSample sample = group.First();
            var statistics = new List<VisualMeasurementStatistic>(Metrics.Length);
            foreach ((string metric, string units, Func<VisualMeasurementSample, double?> value) in Metrics)
                statistics.Add(Statistics(metric, units, group.ToArray(), value));
            string? cellLimitation = string.Join(
                "; ",
                statistics
                    .Where(statistic => !statistic.Available || statistic.P95 is null)
                    .Select(statistic => statistic.Available
                        ? $"{statistic.Metric}: p95 requires at least 20 available samples"
                        : $"{statistic.Metric}: {statistic.Limitation ?? "unavailable"}"));
            cells.Add(new VisualMeasurementCell(
                group.Key,
                sample.Scenario,
                sample.Mode,
                sample.Classification,
                sample.CandidateSha,
                sample.Configuration,
                sample.MachineTopology,
                group.Count(),
                statistics,
                string.IsNullOrWhiteSpace(cellLimitation) ? null : cellLimitation));
        }

        string? underSampled = cells.Any(cell => cell.SampleCount < 20)
            ? "p95 is intentionally unavailable for cells with fewer than 20 samples; budgets from those cells are diagnostic-only."
            : null;
        VisualResourcePairComparison[] pairComparisons = BuildPairComparisons(samples);
        string? pairLimitation = pairComparisons.Any(pair => pair.Outcome == "BLOCKED")
            ? "one or more visual-enabled samples could not be compared with the paired disabled baseline"
            : null;
        string reportOutcome = outcome
            ?? (pairComparisons.Any(pair => pair.Outcome == "BLOCKED")
                ? "BLOCKED"
                : pairComparisons.Any(pair => pair.Outcome == "FAIL")
                    ? "FAIL"
                    : samples.All(sample => sample.Mode == VisualMeasurementMode.DISABLED
                        ? VisualMeasurementGates.ValidateDisabled(sample, out _)
                        : VisualMeasurementGates.ValidateEnabled(sample, out _))
                        ? (underSampled == null ? "PASS" : "PROVISIONAL")
                        : "BLOCKED");
        var report = new VisualMeasurementReport(
            VisualMeasurementReport.CurrentSchemaVersion,
            VisualMeasurementReport.CurrentArtifactKind,
            first.CandidateSha,
            first.RunId,
            driverSha,
            first.Configuration,
            first.Classification,
            samples.Min(sample => sample.RecordedUtc),
            samples.Max(sample => sample.RecordedUtc),
            samples.OrderBy(sample => VisualMeasurementReport.CellKeyFor(sample), StringComparer.Ordinal)
                .ThenBy(sample => sample.SampleNumber)
                .ToArray(),
            cells,
            budgets ?? Array.Empty<VisualMeasurementBudget>(),
            reportOutcome,
            limitation ?? pairLimitation ?? underSampled,
            pairComparisons);
        report.Validate();
        return report;
    }

    private static VisualResourcePairComparison[] BuildPairComparisons(
        IReadOnlyList<VisualMeasurementSample> samples)
    {
        var comparisons = new List<VisualResourcePairComparison>();
        foreach (VisualMeasurementSample enabled in samples
                     .Where(sample => sample.Mode != VisualMeasurementMode.DISABLED)
                     .OrderBy(sample => sample.Scenario, StringComparer.Ordinal)
                     .ThenBy(sample => sample.SampleNumber)
                     .ThenBy(sample => sample.Mode))
        {
            VisualMeasurementSample? disabled = samples.FirstOrDefault(sample =>
                sample.Mode == VisualMeasurementMode.DISABLED
                && sample.Scenario == enabled.Scenario
                && sample.SampleNumber == enabled.SampleNumber
                && sample.CandidateSha == enabled.CandidateSha
                && sample.RunId == enabled.RunId
                && sample.Configuration == enabled.Configuration
                && sample.Classification == enabled.Classification
                && sample.MachineTopology == enabled.MachineTopology);

            Dictionary<string, long> allowed = VisualMeasurementGates.RequiredResourceMetrics
                .ToDictionary(metric => metric, _ => 0L, StringComparer.Ordinal);
            Dictionary<string, long?> deltas = VisualMeasurementGates.RequiredResourceMetrics
                .ToDictionary(
                    metric => metric,
                    metric => disabled is null
                        ? null
                        : DeltaAfter(disabled, enabled, metric),
                    StringComparer.Ordinal);
            string outcome;
            string? reason;
            if (disabled is null)
            {
                outcome = "BLOCKED";
                reason = "paired visual-disabled baseline is missing";
            }
            else
            {
                allowed = VisualMeasurementGates.RequiredResourceMetrics
                    .ToDictionary(
                        metric => metric,
                        metric => PositiveBaselineAllowance(disabled, metric),
                        StringComparer.Ordinal);
                bool passed = VisualMeasurementGates.ComparePairedResources(
                    disabled,
                    enabled,
                    allowed,
                    out string comparisonReason);
                outcome = passed
                    ? "PASS"
                    : IsBlockingReason(comparisonReason) ? "BLOCKED" : "FAIL";
                reason = passed ? null : comparisonReason;
            }

            comparisons.Add(new VisualResourcePairComparison(
                enabled.Scenario,
                enabled.SampleNumber,
                enabled.Mode,
                enabled.Classification,
                enabled.CandidateSha,
                enabled.RunId,
                enabled.Configuration,
                enabled.MachineTopology,
                deltas,
                allowed,
                outcome,
                reason));
        }

        return comparisons.ToArray();
    }

    private static long? DeltaAfter(
        VisualMeasurementSample disabled,
        VisualMeasurementSample enabled,
        string metric)
    {
        long? disabledValue = disabled.AfterResources.Value(metric);
        long? enabledValue = enabled.AfterResources.Value(metric);
        return disabledValue.HasValue && enabledValue.HasValue
            ? enabledValue.Value - disabledValue.Value
            : null;
    }

    private static long PositiveBaselineAllowance(
        VisualMeasurementSample disabled,
        string metric)
    {
        long? before = disabled.BeforeResources.Value(metric);
        long? after = disabled.AfterResources.Value(metric);
        if (!before.HasValue || !after.HasValue)
            return 0;
        long positiveDelta = Math.Max(0, after.Value - before.Value);
        return positiveDelta == 0
            ? 0
            : checked(positiveDelta + (long)Math.Ceiling(positiveDelta * 0.25));
    }

    private static bool IsBlockingReason(string reason)
        => reason.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("identity", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("comparable", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("cleanup", StringComparison.OrdinalIgnoreCase);

    private static VisualMeasurementStatistic Statistics(
        string metric,
        string units,
        IReadOnlyList<VisualMeasurementSample> samples,
        Func<VisualMeasurementSample, double?> value)
    {
        double[] values = samples
            .Select(value)
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .OrderBy(item => item)
            .ToArray();
        if (values.Length != samples.Count)
        {
            return new VisualMeasurementStatistic(
                metric,
                units,
                false,
                samples.Count,
                null,
                null,
                null,
                $"{samples.Count - values.Length} of {samples.Count} observations unavailable",
                null);
        }

        double median = Median(values);
        double? p95 = values.Length >= 20 ? values[Math.Max(0, (int)Math.Ceiling(values.Length * 0.95) - 1)] : null;
        double maximum = values[^1];
        string? outlier = maximum > 0 && (median == 0 || maximum > median * 2)
            ? $"maximum {maximum:0.###} exceeds twice the median {median:0.###}"
            : null;
        return new VisualMeasurementStatistic(
            metric,
            units,
            true,
            values.Length,
            median,
            p95,
            maximum,
            p95 is null ? "p95 requires at least 20 available samples" : null,
            outlier);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        int middle = values.Count / 2;
        return values.Count % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2.0
            : values[middle];
    }

    private static double? Delta(VisualMeasurementSample sample, string metric)
    {
        long? before = sample.BeforeResources.Value(metric);
        long? after = sample.AfterResources.Value(metric);
        return before.HasValue && after.HasValue ? after.Value - before.Value : null;
    }
}

internal static class VisualMeasurementBudgetSelector
{
    public static IReadOnlyList<VisualMeasurementBudget> Derive(
        VisualMeasurementReport report,
        double safetyMarginFraction = 0.25)
    {
        report.Validate();
        if (double.IsNaN(safetyMarginFraction)
            || double.IsInfinity(safetyMarginFraction)
            || safetyMarginFraction < 0
            || safetyMarginFraction > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(safetyMarginFraction));
        }

        var budgets = new List<VisualMeasurementBudget>();
        foreach (VisualMeasurementCell cell in report.Cells)
        {
            foreach (VisualMeasurementStatistic statistic in cell.Statistics)
            {
                if (!statistic.Available || statistic.Maximum is null)
                    continue;
                bool diagnosticOnly = statistic.P95 is null;
                string sourceStatistic = diagnosticOnly ? "maximum" : "p95";
                double observed = statistic.P95 ?? statistic.Maximum.Value;
                double limit = Math.Ceiling(observed * (1 + safetyMarginFraction));
                budgets.Add(new VisualMeasurementBudget(
                    cell.CellKey,
                    statistic.Metric,
                    limit,
                    statistic.Units,
                    sourceStatistic,
                    statistic.SampleCount,
                    safetyMarginFraction,
                    report.CandidateSha,
                    !diagnosticOnly,
                    diagnosticOnly,
                    $"Measured {sourceStatistic}={observed:0.###} {statistic.Units} from candidate {report.CandidateSha}; "
                    + $"applied explicit {safetyMarginFraction:P0} safety margin."
                    + (statistic.OutlierNote is null ? string.Empty : $" Outlier note: {statistic.OutlierNote}"),
                    statistic.OutlierNote));
            }
        }
        return budgets;
    }
}

internal static class VisualMeasurementGates
{
    internal static readonly string[] RequiredResourceMetrics =
    {
        "processHandleCount",
        "userObjectCount",
        "gdiObjectCount",
        "hBitmapCount",
        "hdcCount",
        "fileHandleCount",
        "privateBytes",
        "workingSet",
        "threadCount",
        "workerThreadCount",
        "timerCount",
        "tabDockOwnedWindowCount",
        "ringBytes",
        "artifactCount",
        "artifactBytes",
    };
    public static bool ValidateDisabled(VisualMeasurementSample sample, out string reason)
    {
        reason = string.Empty;
        try
        {
            sample.Validate();
        }
        catch (ArgumentException ex)
        {
            reason = ex.Message;
            return false;
        }
        if (sample.Mode != VisualMeasurementMode.DISABLED)
        {
            reason = "sample is not visual-disabled";
            return false;
        }
        if (!TryValidateResourceObservation(sample, out reason))
            return false;
        if (!sample.Work.IsZero || sample.Timing.HasVisualWork)
        {
            reason = "disabled sample performed visual work";
            return false;
        }
        if (sample.Width.HasValue || sample.Height.HasValue || sample.CaptureMethod.HasValue || sample.ScopeKind.HasValue)
        {
            reason = "disabled sample carries capture metadata";
            return false;
        }
        if (sample.RingOccupancy != 0 || sample.PeakRingBytes != 0 || !sample.CleanupCompleted)
        {
            reason = "disabled sample did not return to a clean zero-work state";
            return false;
        }
        return true;
    }

    public static bool ValidateEnabled(VisualMeasurementSample sample, out string reason)
    {
        reason = string.Empty;
        try
        {
            sample.Validate();
        }
        catch (ArgumentException ex)
        {
            reason = ex.Message;
            return false;
        }
        if (sample.Mode == VisualMeasurementMode.DISABLED || !sample.Policy.Enabled)
        {
            reason = "enabled sample does not carry an enabled policy";
            return false;
        }
        if (!TryValidateResourceObservation(sample, out reason))
            return false;

        if (sample.Width is not int width
            || sample.Height is not int height
            || width > sample.Policy.MaxWidth
            || height > sample.Policy.MaxHeight)
        {
            reason = "capture dimensions exceed the selected policy";
            return false;
        }
        if (sample.Work.RetainedBytes > sample.Policy.MaxBytes
            || sample.Work.ArtifactBytes > sample.Policy.MaxBytes
            || sample.Work.ArtifactCount > sample.Policy.MaxArtifacts
            || sample.PeakRingBytes > sample.Policy.RingMaxBytes
            || sample.RingOccupancy > sample.Policy.RingMaxFrames)
        {
            reason = "visual work exceeded a hard policy bound";
            return false;
        }
        if (!sample.CleanupCompleted)
        {
            reason = "visual sample did not complete cleanup";
            return false;
        }
        if (sample.Mode == VisualMeasurementMode.FLIGHT_HEALTHY_DISCARD
            && (!sample.HealthyFlightDiscarded || sample.RingOccupancy != 0 || sample.Work.RetainedFrames != 0))
        {
            reason = "healthy flight history was not discarded";
            return false;
        }
        if (sample.Mode == VisualMeasurementMode.FLIGHT_FAILURE_FLUSH
            && sample.Work.RingFlushes < 1)
        {
            reason = "flight failure sample did not flush bounded history";
            return false;
        }
        return true;
    }
    public static bool ValidateMeasuredBudgets(
        VisualMeasurementSample sample,
        IReadOnlyList<VisualMeasurementBudget> budgets,
        out string reason)
    {
        reason = string.Empty;
        if (budgets is null)
        {
            reason = "measured budgets are unavailable";
            return false;
        }

        bool valid = sample.Mode == VisualMeasurementMode.DISABLED
            ? ValidateDisabled(sample, out reason)
            : ValidateEnabled(sample, out reason);
        if (!valid)
            return false;

        string cellKey = VisualMeasurementReport.CellKeyFor(sample);
        VisualMeasurementBudget[] cellBudgets = budgets
            .Where(budget => string.Equals(budget.CellKey, cellKey, StringComparison.Ordinal))
            .ToArray();
        if (cellBudgets.Length == 0)
        {
            reason = $"no measured budgets exist for cell '{cellKey}'";
            return false;
        }

        foreach (VisualMeasurementBudget budget in cellBudgets)
        {
            budget.Validate();
            if (budget.DiagnosticOnly || !budget.HardCeiling)
            {
                reason = $"budget '{budget.Metric}' is diagnostic-only and cannot gate";
                return false;
            }
            if (!VisualMeasurementReportBuilder.TryGetMetricValue(sample, budget.Metric, out double? value)
                || !value.HasValue)
            {
                reason = $"measured metric '{budget.Metric}' is unavailable";
                return false;
            }
            if (value.Value > budget.Limit)
            {
                reason = $"measured metric '{budget.Metric}'={value.Value:0.###} exceeds limit {budget.Limit:0.###}";
                return false;
            }
        }

        return true;
    }


    public static bool ComparePairedResources(
        VisualMeasurementSample disabled,
        VisualMeasurementSample enabled,
        IReadOnlyDictionary<string, long> maximumPositiveDeltas,
        out string reason)
    {
        reason = string.Empty;
        if (disabled is null
            || enabled is null
            || maximumPositiveDeltas is null
            || disabled.Mode != VisualMeasurementMode.DISABLED
            || enabled.Mode == VisualMeasurementMode.DISABLED
            || !string.Equals(disabled.CandidateSha, enabled.CandidateSha, StringComparison.Ordinal)
            || !string.Equals(disabled.RunId, enabled.RunId, StringComparison.Ordinal)
            || !string.Equals(disabled.Scenario, enabled.Scenario, StringComparison.Ordinal)
            || !string.Equals(disabled.Configuration, enabled.Configuration, StringComparison.Ordinal)
            || disabled.Classification != enabled.Classification
            || disabled.SampleNumber != enabled.SampleNumber
            || disabled.MachineTopology != enabled.MachineTopology)
        {
            reason = "paired samples are not comparable by candidate/run/scenario/configuration/sample/topology";
            return false;
        }
        if (!ValidateDisabled(disabled, out string disabledReason))
        {
            reason = $"paired disabled baseline is invalid: {disabledReason}";
            return false;
        }
        if (!ValidateEnabled(enabled, out string enabledReason))
        {
            reason = $"paired enabled sample is invalid: {enabledReason}";
            return false;
        }
        if (disabled.BeforeResources.ProcessIdentity != disabled.AfterResources.ProcessIdentity
            || enabled.BeforeResources.ProcessIdentity != enabled.AfterResources.ProcessIdentity)
        {
            reason = "paired process generation changed during resource comparison";
            return false;
        }
        if (maximumPositiveDeltas.Count != RequiredResourceMetrics.Length
            || RequiredResourceMetrics.Any(metric => !maximumPositiveDeltas.ContainsKey(metric))
            || maximumPositiveDeltas.Any(pair => pair.Value < 0))
        {
            reason = "paired resource comparison lacks complete non-negative budgets";
            return false;
        }

        foreach (KeyValuePair<string, long> budget in maximumPositiveDeltas)
        {
            long? disabledValue = disabled.AfterResources.Value(budget.Key);
            long? enabledValue = enabled.AfterResources.Value(budget.Key);
            if (!disabledValue.HasValue || !enabledValue.HasValue)
            {
                reason = $"paired resource metric '{budget.Key}' is unavailable";
                return false;
            }
            long delta = enabledValue.Value - disabledValue.Value;
            if (delta > budget.Value)
            {
                reason = $"paired resource metric '{budget.Key}' increased by {delta}, "
                    + $"limit {budget.Value}";
                return false;
            }
        }
        return true;
    }
    private static bool TryValidateResourceObservation(VisualMeasurementSample sample, out string reason)
    {
        foreach (VisualResourceObservation observation in new[]
        {
            sample.BeforeResources,
            sample.AfterResources,
        })
        {
            if (observation.MeasurementError is not null)
            {
                reason = $"resource observation is unavailable: {observation.MeasurementError}";
                return false;
            }

            foreach (string metric in RequiredResourceMetrics)
            {
                if (!observation.Value(metric).HasValue)
                {
                    reason = $"resource observation metric '{metric}' is unavailable";
                    return false;
                }
            }
        }

        reason = string.Empty;
        return true;
    }

}

internal static class VisualMeasurementReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Write(VisualMeasurementReport report, string outputDirectory)
    {
        report.Validate();
        Directory.CreateDirectory(outputDirectory);
        string path = Path.Combine(outputDirectory, "visual-performance-measurements.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
        return path;
    }

    public static string WriteJUnit(VisualMeasurementReport report, string outputDirectory)
    {
        report.Validate();
        Directory.CreateDirectory(outputDirectory);
        string path = Path.Combine(outputDirectory, "visual-performance-measurements.junit.xml");
        bool passed = string.Equals(report.Outcome, "PASS", StringComparison.Ordinal);
        string failure = report.Limitation ?? "visual measurement report is not a proven PASS";
        string escaped = System.Security.SecurityElement.Escape(failure) ?? "visual measurement report is not a proven PASS";
        string failureElement = passed ? string.Empty : $"    <failure message=\"{escaped}\" />\n";
        string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            + $"<testsuite name=\"TabDock.VisualMeasurements\" tests=\"{report.Cells.Count}\" failures=\"{(passed ? 0 : 1)}\" skipped=\"0\">\n"
            + "  <testcase classname=\"TabDock.ValidationDriver.VisualMeasurements\" name=\"visual-measurements\">\n"
            + failureElement
            + "  </testcase>\n"
            + "</testsuite>\n";
        File.WriteAllText(path, xml);
        return path;
    }
}

internal static class VisualResourceObservationFactory
{
    public static VisualResourceObservation Synthetic(
        VisualMeasurementMode mode,
        long artifactCount = 0,
        long artifactBytes = 0,
        long ringBytes = 0)
        => new(
            mode,
            new ResourceProcessIdentity(1, 1),
            100,
            10,
            20,
            2,
            2,
            5,
            100L * 1024 * 1024,
            50L * 1024 * 1024,
            8,
            0,
            0,
            1,
            ringBytes,
            artifactCount,
            artifactBytes);
}
