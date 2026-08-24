using System;
using System.Collections.Generic;
using System.Linq;
using TabDock.ValidationDriver;
using Xunit;

namespace TabDock.UnitTests;

public sealed class ResourceStabilityAnalyzerTests
{
    private static readonly ResourceMetric[] AllMetrics = Enum.GetValues<ResourceMetric>();

    [Fact]
    public void FlatSeriesPasses()
    {
        ResourceSeriesAnalysis result = Analyze(Enumerable.Repeat(100L, 10).ToArray());

        Assert.True(result.Passed);
        Assert.Equal(ResourceStabilityOutcome.Pass, result.Outcome);
        Assert.All(result.Metrics, metric => Assert.Equal(ResourceTrend.Flat, metric.Trend));
    }

    [Fact]
    public void WarmupGrowthFollowedByPlateauPassesAndUsesSettledBaseline()
    {
        long[] values = { 100, 140, 180, 180, 180, 180, 180, 180 };
        ResourceSeriesAnalysis result = Analyze(values);
        ResourceMetricAnalysis handles = Metric(result, ResourceMetric.HandleCount);

        Assert.True(result.Passed);
        Assert.Equal(180, handles.Baseline);
        Assert.Equal(180, handles.Final);
        Assert.Equal(0, handles.FinalDelta);
    }

    [Fact]
    public void BoundedNativeNoisePasses()
    {
        ResourceSeriesAnalysis result = Analyze(new long[] { 100, 101, 100, 102, 101, 100, 101, 100 });
        ResourceMetricAnalysis handles = Metric(result, ResourceMetric.HandleCount);

        Assert.True(result.Passed);
        Assert.Equal(ResourceTrend.BoundedNoise, handles.Trend);
    }

    [Theory]
    [InlineData(ResourceMetric.HandleCount)]
    [InlineData(ResourceMetric.UserObjectCount)]
    [InlineData(ResourceMetric.GdiObjectCount)]
    public void PersistentNativeObjectGrowthFails(ResourceMetric metric)
    {
        long[] values = Enumerable.Range(0, 10).Select(index => 100L + index).ToArray();
        ResourceSeriesAnalysis result = Analyze(values, metric);
        ResourceMetricAnalysis analysis = Metric(result, metric);

        Assert.Equal(ResourceStabilityOutcome.Fail, result.Outcome);
        Assert.False(analysis.Passed);
        Assert.Equal(ResourceTrend.MonotonicGrowth, analysis.Trend);
    }

    [Fact]
    public void LateGrowthIsNotHiddenByAnInitiallyStableTail()
    {
        long[] values = { 100, 100, 100, 100, 100, 101, 102, 103, 104, 105 };
        ResourceSeriesAnalysis result = Analyze(values);
        ResourceMetricAnalysis analysis = Metric(result, ResourceMetric.HandleCount);

        Assert.Equal(ResourceStabilityOutcome.Fail, result.Outcome);
        Assert.False(analysis.Passed);
        Assert.True(analysis.Trend is ResourceTrend.LateGrowth or ResourceTrend.MonotonicGrowth);
    }

    [Fact]
    public void TransientSettledSpikeDoesNotBecomeLeak()
    {
        long[] values = { 100, 100, 100, 100, 150, 100, 101, 100, 100, 100 };
        ResourceSeriesAnalysis result = Analyze(values);
        ResourceMetricAnalysis analysis = Metric(result, ResourceMetric.HandleCount);

        Assert.True(result.Passed);
        Assert.Equal(ResourceTrend.TransientSpike, analysis.Trend);
        Assert.Equal(50, analysis.PeakDelta);
    }

    [Fact]
    public void MonotonicPrivateMemoryGrowthFails()
    {
        long[] values = Enumerable.Range(0, 10)
            .Select(index => 100L * 1024 * 1024 + index * 2L * 1024 * 1024)
            .ToArray();
        ResourceSeriesAnalysis result = Analyze(values, ResourceMetric.PrivateBytes);
        ResourceMetricAnalysis analysis = Metric(result, ResourceMetric.PrivateBytes);

        Assert.Equal(ResourceStabilityOutcome.Fail, result.Outcome);
        Assert.Equal(ResourceTrend.MonotonicGrowth, analysis.Trend);
    }

    [Fact]
    public void WorkingSetSpikeWithRecoveryPasses()
    {
        long[] values = { 100, 100, 100, 100, 20_000_000, 100, 100, 100, 100 };
        ResourceSeriesAnalysis result = Analyze(values, ResourceMetric.WorkingSet);

        Assert.True(result.Passed);
        Assert.Equal(ResourceTrend.TransientSpike, Metric(result, ResourceMetric.WorkingSet).Trend);
    }

    [Fact]
    public void ProcessGenerationChangeBlocksInsteadOfJoiningRestarts()
    {
        List<ResourceSnapshot> snapshots = CreateSeries(Enumerable.Repeat(100L, 8).ToArray()).ToList();
        snapshots[6] = snapshots[6] with
        {
            ProcessIdentity = new ResourceProcessIdentity(999, 2),
        };

        ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze("restart", snapshots);

        Assert.Equal(ResourceStabilityOutcome.Blocked, result.Outcome);
        Assert.Contains("generation", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingMetricBlocksAndNeverPassesAsZero()
    {
        List<ResourceSnapshot> snapshots = CreateSeries(Enumerable.Repeat(100L, 8).ToArray())
            .Select(snapshot => snapshot with { GdiObjectCount = null })
            .ToList();

        ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze("missing", snapshots);
        ResourceMetricAnalysis gdi = Metric(result, ResourceMetric.GdiObjectCount);

        Assert.Equal(ResourceStabilityOutcome.Blocked, result.Outcome);
        Assert.False(gdi.Available);
        Assert.Equal(ResourceTrend.Unavailable, gdi.Trend);
    }

    [Fact]
    public void ProcessExitBetweenSamplesBlocksOnUnavailableIdentity()
    {
        List<ResourceSnapshot> snapshots = CreateSeries(Enumerable.Repeat(100L, 8).ToArray()).ToList();
        snapshots[5] = snapshots[5] with
        {
            ProcessIdentity = new ResourceProcessIdentity(42, 0),
            MeasurementError = "process-exited",
        };

        ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze("exit", snapshots);

        Assert.Equal(ResourceStabilityOutcome.Blocked, result.Outcome);
        Assert.Contains("identity", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CounterResetBlocksSameGenerationEvidence()
    {
        ResourceSeriesAnalysis result = Analyze(new long[] { 100, 100, 100, 100, 0, 1, 1, 1 });
        ResourceMetricAnalysis handles = Metric(result, ResourceMetric.HandleCount);

        Assert.Equal(ResourceStabilityOutcome.Blocked, result.Outcome);
        Assert.Equal(ResourceTrend.CounterReset, handles.Trend);
        Assert.False(handles.Available);
    }

    [Fact]
    public void InvalidSequenceAndTimestampAreBlocked()
    {
        List<ResourceSnapshot> snapshots = CreateSeries(Enumerable.Repeat(100L, 8).ToArray()).ToList();
        snapshots[4] = snapshots[4] with { Sequence = snapshots[3].Sequence };
        ResourceSeriesAnalysis sequenceResult = ResourceSeriesAnalyzer.Analyze("sequence", snapshots);
        Assert.Equal(ResourceStabilityOutcome.Blocked, sequenceResult.Outcome);

        snapshots = CreateSeries(Enumerable.Repeat(100L, 8).ToArray()).ToList();
        snapshots[4] = snapshots[4] with { TimestampUtc = snapshots[3].TimestampUtc.AddSeconds(-1) };
        ResourceSeriesAnalysis timestampResult = ResourceSeriesAnalyzer.Analyze("timestamp", snapshots);
        Assert.Equal(ResourceStabilityOutcome.Blocked, timestampResult.Outcome);
    }

    [Fact]
    public void InsufficientSettledSamplesAreBlocked()
    {
        ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze(
            "short",
            CreateSeries(new long[] { 100, 100, 100, 100 }).ToArray(),
            new ResourceStabilityOptions { WarmupSamples = 3, MinimumSettledSamples = 2 });

        Assert.Equal(ResourceStabilityOutcome.Blocked, result.Outcome);
        Assert.Contains("insufficient", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitUnavailableSnapshotIsBlocked()
    {
        ResourceSnapshot snapshot = CreateSeries(Enumerable.Repeat(100L, 8).ToArray())[3] with
        {
            HandleCount = null,
            UserObjectCount = null,
            GdiObjectCount = null,
            PrivateBytes = null,
            WorkingSet = null,
            ThreadCount = null,
            TopLevelWindowCount = null,
            MeasurementError = "access-denied",
        };
        List<ResourceSnapshot> snapshots = CreateSeries(Enumerable.Repeat(100L, 8).ToArray()).ToList();
        snapshots[3] = snapshot;

        ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze("blocked", snapshots);

        Assert.Equal(ResourceStabilityOutcome.Blocked, result.Outcome);
        Assert.Empty(result.Metrics);
        Assert.Contains("measurement", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MeasurementErrorBlocksEvenWhenValuesWereRetained()
    {
        List<ResourceSnapshot> snapshots = CreateSeries(Enumerable.Repeat(100L, 8).ToArray()).ToList();
        snapshots[6] = snapshots[6] with { MeasurementError = "probe-failed" };

        ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze("measurement-error", snapshots);

        Assert.Equal(ResourceStabilityOutcome.Blocked, result.Outcome);
        Assert.Contains("measurement", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SnapshotValueMapsEveryMetricWithoutSensitiveFields()
    {
        ResourceSnapshot snapshot = CreateSeries(new long[] { 1, 1, 1, 1, 1, 1, 1, 1 })[0] with
        {
            UserObjectCount = 1,
            GdiObjectCount = 1,
            PrivateBytes = 1,
            WorkingSet = 1,
            ThreadCount = 1,
            TopLevelWindowCount = 1,
        };

        Assert.All(AllMetrics, metric => Assert.Equal(1, snapshot.Value(metric)));
        Assert.DoesNotContain("title", snapshot.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path", snapshot.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static ResourceSeriesAnalysis Analyze(long[] values, ResourceMetric focus = ResourceMetric.HandleCount)
    {
        IReadOnlyList<ResourceSnapshot> snapshots = CreateSeries(values, focus);
        return ResourceSeriesAnalyzer.Analyze("unit", snapshots);
    }

    private static IReadOnlyList<ResourceSnapshot> CreateSeries(
        long[] values,
        ResourceMetric focus = ResourceMetric.HandleCount)
    {
        var snapshots = new List<ResourceSnapshot>(values.Length);
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        for (int i = 0; i < values.Length; i++)
        {
            long? handle = values[i];
            long? user = 100;
            long? gdi = 100;
            long? privateBytes = 100L * 1024 * 1024;
            long? workingSet = 50L * 1024 * 1024;
            long? threads = 10;
            long? windows = 1;
            switch (focus)
            {
                case ResourceMetric.UserObjectCount: user = values[i]; break;
                case ResourceMetric.GdiObjectCount: gdi = values[i]; break;
                case ResourceMetric.PrivateBytes: privateBytes = values[i]; break;
                case ResourceMetric.WorkingSet: workingSet = values[i]; break;
                case ResourceMetric.ThreadCount: threads = values[i]; break;
                case ResourceMetric.TopLevelWindowCount: windows = values[i]; break;
            }
            snapshots.Add(new ResourceSnapshot(
                i + 1,
                i < 3 ? "warmup" : "settled",
                start.AddSeconds(i),
                new ResourceProcessIdentity(42, 1),
                handle,
                user,
                gdi,
                privateBytes,
                workingSet,
                threads,
                windows));
        }
        return snapshots;
    }

    private static ResourceMetricAnalysis Metric(ResourceSeriesAnalysis result, ResourceMetric metric)
        => Assert.Single(result.Metrics, item => item.Metric == metric);
}
