using System;
using System.Collections.Generic;
using System.Linq;

namespace TabDock.ValidationDriver;

/// <summary>
/// Resource signals that can be sampled without reading window titles, paths,
/// URLs, command lines, or user documents. The model is shared with the
/// headless unit tests; Windows acquisition lives in ResourceSnapshotProbe.cs.
/// </summary>
public enum ResourceMetric
{
    HandleCount,
    UserObjectCount,
    GdiObjectCount,
    PrivateBytes,
    WorkingSet,
    ThreadCount,
    TopLevelWindowCount,
}

public enum ResourceStabilityOutcome
{
    Pass,
    Fail,
    Blocked,
}

public enum ResourceTrend
{
    Flat,
    WarmupPlateau,
    BoundedNoise,
    TransientSpike,
    MonotonicGrowth,
    LateGrowth,
    CounterReset,
    Unavailable,
    Invalid,
}

/// <summary>Stable process-generation identity used to reject cross-restart series.</summary>
public readonly record struct ResourceProcessIdentity(
    uint ProcessId,
    long ProcessStartTimeUtcTicks)
{
    public bool IsValid => ProcessId != 0 && ProcessStartTimeUtcTicks > 0;
}

/// <summary>
/// Immutable point-in-time resource observation. Null metric values mean the
/// signal was unavailable at acquisition time; they are never interpreted as
/// zero and never allow a PASS.
/// </summary>
public sealed record ResourceSnapshot(
    int Sequence,
    string Phase,
    DateTimeOffset TimestampUtc,
    ResourceProcessIdentity ProcessIdentity,
    long? HandleCount,
    long? UserObjectCount,
    long? GdiObjectCount,
    long? PrivateBytes,
    long? WorkingSet,
    long? ThreadCount,
    long? TopLevelWindowCount,
    string? MeasurementError = null)
{
    public long? Value(ResourceMetric metric) => metric switch
    {
        ResourceMetric.HandleCount => HandleCount,
        ResourceMetric.UserObjectCount => UserObjectCount,
        ResourceMetric.GdiObjectCount => GdiObjectCount,
        ResourceMetric.PrivateBytes => PrivateBytes,
        ResourceMetric.WorkingSet => WorkingSet,
        ResourceMetric.ThreadCount => ThreadCount,
        ResourceMetric.TopLevelWindowCount => TopLevelWindowCount,
        _ => null,
    };
}

/// <summary>
/// A per-metric budget. The absolute limits apply to the settled final value
/// and the tail of the series; the slope limit catches a small leak repeated
/// on every cycle. These are intentionally small for native GUI resources and
/// larger only for noisy byte counters.
/// </summary>
public readonly record struct ResourceMetricBudget(
    long MaxFinalDelta,
    long MaxTailDelta,
    double MaxPositiveSlopePerCycle);

public sealed class ResourceStabilityOptions
{
    public int WarmupSamples { get; init; } = 3;
    public int MinimumSettledSamples { get; init; } = 5;
    public int TailSampleCount { get; init; } = 5;

    public IReadOnlyDictionary<ResourceMetric, ResourceMetricBudget> Budgets { get; init; }
        = CreateDefaultBudgets();

    public static IReadOnlyDictionary<ResourceMetric, ResourceMetricBudget> CreateDefaultBudgets()
        => new Dictionary<ResourceMetric, ResourceMetricBudget>
        {
            // GUI/native object counts are comparatively low-noise. A two-object
            // allowance covers one-time WPF laziness without hiding +1/cycle.
            [ResourceMetric.HandleCount] = new(2, 2, 0.25),
            [ResourceMetric.UserObjectCount] = new(2, 2, 0.25),
            [ResourceMetric.GdiObjectCount] = new(2, 2, 0.25),
            [ResourceMetric.ThreadCount] = new(2, 2, 0.25),
            [ResourceMetric.TopLevelWindowCount] = new(1, 1, 0.25),
            // Byte counters can move transiently while WPF and the CLR lazily
            // allocate. The tail slope still catches sustained multi-megabyte
            // growth rather than relying on final <= initial.
            [ResourceMetric.PrivateBytes] = new(4 * 1024 * 1024, 2 * 1024 * 1024, 1 * 1024 * 1024),
            [ResourceMetric.WorkingSet] = new(8 * 1024 * 1024, 4 * 1024 * 1024, 2 * 1024 * 1024),
        };
}

public sealed record ResourceMetricAnalysis(
    ResourceMetric Metric,
    bool Available,
    bool Passed,
    int SettledSampleCount,
    long? Baseline,
    long? Final,
    long? FinalDelta,
    long? PeakDelta,
    long? TailDelta,
    double? SlopePerCycle,
    ResourceTrend Trend,
    string? Reason);

public sealed record ResourceSeriesAnalysis(
    string Profile,
    ResourceStabilityOutcome Outcome,
    int WarmupSamples,
    int SettledSamples,
    IReadOnlyList<ResourceMetricAnalysis> Metrics,
    string? FailureReason)
{
    public bool Passed => Outcome == ResourceStabilityOutcome.Pass;
}

/// <summary>
/// Deterministic analyzer for a single process generation. It ignores only the
/// explicitly configured warm-up prefix, analyzes settled checkpoints, and
/// distinguishes a plateau/transient from repeated positive growth. Invalid,
/// missing, reordered, or cross-generation evidence is BLOCKED.
/// </summary>
public static class ResourceSeriesAnalyzer
{
    public static ResourceSeriesAnalysis Analyze(
        string profile,
        IReadOnlyList<ResourceSnapshot> snapshots,
        ResourceStabilityOptions? options = null)
    {
        options ??= new ResourceStabilityOptions();
        string safeProfile = string.IsNullOrWhiteSpace(profile) ? "unknown" : profile;
        var metrics = new List<ResourceMetricAnalysis>();

        if (snapshots == null || snapshots.Count == 0)
            return Blocked(safeProfile, options.WarmupSamples, 0, "no resource snapshots were captured");

        string? structuralError = ValidateSeries(snapshots);
        if (structuralError != null)
            return Blocked(safeProfile, options.WarmupSamples, 0, structuralError);

        if (options.WarmupSamples < 0
            || options.MinimumSettledSamples < 1
            || options.TailSampleCount < 2
            || options.WarmupSamples + options.MinimumSettledSamples > snapshots.Count)
        {
            return Blocked(
                safeProfile,
                options.WarmupSamples,
                Math.Max(0, snapshots.Count - Math.Max(0, options.WarmupSamples)),
                "insufficient settled samples for the configured warm-up");
        }

        IReadOnlyList<ResourceSnapshot> settled = snapshots.Skip(options.WarmupSamples).ToArray();
        foreach ((ResourceMetric metric, ResourceMetricBudget budget) in options.Budgets)
            metrics.Add(AnalyzeMetric(metric, budget, settled, options.TailSampleCount));

        ResourceMetricAnalysis? firstBlocked = metrics.FirstOrDefault(metric => !metric.Available);
        ResourceMetricAnalysis? firstFailed = metrics.FirstOrDefault(metric => metric.Available && !metric.Passed);
        if (firstBlocked != null)
        {
            return new ResourceSeriesAnalysis(
                safeProfile,
                ResourceStabilityOutcome.Blocked,
                options.WarmupSamples,
                settled.Count,
                metrics,
                $"{firstBlocked.Metric}: {firstBlocked.Reason ?? "measurement unavailable"}");
        }

        if (firstFailed != null)
        {
            return new ResourceSeriesAnalysis(
                safeProfile,
                ResourceStabilityOutcome.Fail,
                options.WarmupSamples,
                settled.Count,
                metrics,
                $"{firstFailed.Metric}: {firstFailed.Reason ?? "resource growth exceeded budget"}");
        }

        return new ResourceSeriesAnalysis(
            safeProfile,
            ResourceStabilityOutcome.Pass,
            options.WarmupSamples,
            settled.Count,
            metrics,
            null);
    }

    private static ResourceMetricAnalysis AnalyzeMetric(
        ResourceMetric metric,
        ResourceMetricBudget budget,
        IReadOnlyList<ResourceSnapshot> settled,
        int tailSampleCount)
    {
        long?[] nullableValues = settled.Select(snapshot => snapshot.Value(metric)).ToArray();
        if (nullableValues.Any(value => !value.HasValue || value.Value < 0))
        {
            return new ResourceMetricAnalysis(
                metric,
                Available: false,
                Passed: false,
                settled.Count,
                null,
                null,
                null,
                null,
                null,
                null,
                ResourceTrend.Unavailable,
                "metric is missing or contains a negative value");
        }

        long[] values = nullableValues.Select(value => value!.Value).ToArray();
        for (int i = 1; i < values.Length; i++)
        {
            // A same-generation counter dropping to zero is not a legitimate
            // plateau. Treat it as invalid evidence rather than laundering a
            // reset into a low final value.
            if (values[i] == 0 && values[i - 1] > 0)
            {
                return new ResourceMetricAnalysis(
                    metric,
                    Available: false,
                    Passed: false,
                    settled.Count,
                    values[0],
                    values[^1],
                    SafeDelta(values[^1], values[0]),
                    SafeDelta(values.Max(), values[0]),
                    SafeDelta(values[^1], values[Math.Max(0, values.Length - tailSampleCount)]),
                    null,
                    ResourceTrend.CounterReset,
                    "counter reset within one process generation");
            }
        }

        long baseline = values[0];
        long final = values[^1];
        long peak = values.Max();
        long finalDelta = SafeDelta(final, baseline);
        long peakDelta = SafeDelta(peak, baseline);
        int tailStart = Math.Max(0, values.Length - tailSampleCount);
        long tailDelta = SafeDelta(final, values[tailStart]);
        double slope = LinearSlope(values);
        double tailSlope = LinearSlope(values.Skip(tailStart).ToArray());
        int positiveSteps = CountPositiveSteps(values);
        bool hasTransientSpike = peakDelta > budget.MaxFinalDelta
            && finalDelta <= budget.MaxFinalDelta
            && tailDelta <= budget.MaxTailDelta;
        bool monotonicGrowth = finalDelta > budget.MaxFinalDelta
            && (tailDelta > budget.MaxTailDelta
                || tailSlope > budget.MaxPositiveSlopePerCycle
                || (positiveSteps >= Math.Max(2, values.Length / 3)
                    && slope > budget.MaxPositiveSlopePerCycle));
        bool lateGrowth = tailDelta > budget.MaxTailDelta
            && tailSlope > budget.MaxPositiveSlopePerCycle;
        bool passed = !monotonicGrowth && !lateGrowth;
        ResourceTrend trend;
        string? reason = null;

        if (monotonicGrowth || lateGrowth)
        {
            trend = lateGrowth && !monotonicGrowth ? ResourceTrend.LateGrowth : ResourceTrend.MonotonicGrowth;
            reason = $"settled growth finalDelta={finalDelta} tailDelta={tailDelta} slope={slope:0.###}";
        }
        else if (hasTransientSpike)
        {
            trend = ResourceTrend.TransientSpike;
        }
        else if (slope == 0 && finalDelta == 0)
        {
            trend = ResourceTrend.Flat;
        }
        else if (positiveSteps == 0 && finalDelta <= budget.MaxFinalDelta)
        {
            trend = ResourceTrend.WarmupPlateau;
        }
        else
        {
            trend = ResourceTrend.BoundedNoise;
        }

        return new ResourceMetricAnalysis(
            metric,
            Available: true,
            Passed: passed,
            settled.Count,
            baseline,
            final,
            finalDelta,
            peakDelta,
            tailDelta,
            slope,
            trend,
            reason);
    }

    private static string? ValidateSeries(IReadOnlyList<ResourceSnapshot> snapshots)
    {
        ResourceSnapshot first = snapshots[0];
        if (!first.ProcessIdentity.IsValid)
            return "process identity is unavailable";
        if (!string.IsNullOrWhiteSpace(first.MeasurementError))
            return "resource snapshot reported a measurement error";

        for (int i = 1; i < snapshots.Count; i++)
        {
            ResourceSnapshot previous = snapshots[i - 1];
            ResourceSnapshot current = snapshots[i];
            if (current.Sequence <= previous.Sequence)
                return "snapshot sequence is not strictly increasing";
            if (current.TimestampUtc < previous.TimestampUtc)
                return "snapshot timestamps are not ordered";
            if (!current.ProcessIdentity.IsValid)
                return "process identity is unavailable";
            if (current.ProcessIdentity != first.ProcessIdentity)
                return "process generation changed during one resource series";
            if (!string.IsNullOrWhiteSpace(current.MeasurementError))
                return "resource snapshot reported a measurement error";
        }
        return null;
    }

    private static ResourceSeriesAnalysis Blocked(string profile, int warmup, int settled, string reason)
        => new(
            profile,
            ResourceStabilityOutcome.Blocked,
            warmup,
            settled,
            Array.Empty<ResourceMetricAnalysis>(),
            reason);

    private static int CountPositiveSteps(IReadOnlyList<long> values)
    {
        int count = 0;
        for (int i = 1; i < values.Count; i++)
            if (values[i] > values[i - 1])
                count++;
        return count;
    }

    private static double LinearSlope(IReadOnlyList<long> values)
    {
        if (values.Count < 2)
            return 0;

        double xMean = (values.Count - 1) / 2.0;
        double yMean = values.Average(value => (double)value);
        double numerator = 0;
        double denominator = 0;
        for (int i = 0; i < values.Count; i++)
        {
            double x = i - xMean;
            numerator += x * (values[i] - yMean);
            denominator += x * x;
        }
        return denominator == 0 ? 0 : numerator / denominator;
    }

    private static long SafeDelta(long left, long right)
    {
        try
        {
            return checked(left - right);
        }
        catch (OverflowException)
        {
            return left >= right ? long.MaxValue : long.MinValue;
        }
    }
}
