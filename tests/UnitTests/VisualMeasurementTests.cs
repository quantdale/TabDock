using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabDock.ValidationDriver;
using Xunit;

namespace TabDock.UnitTests;

public sealed class VisualMeasurementTests
{
    [Fact]
    public void DisabledGateRejectsVisualWorkAndPreservesControlOverhead()
    {
        VisualMeasurementSample sample = CreateSample(
            VisualMeasurementMode.DISABLED,
            sampleNumber: 1,
            controlOverhead: 2,
            work: new VisualMeasurementWork(
                1, 1, 0, 1, 0, 0, 0, 0, 0, 1, 10, 1, 10, 0, 0));

        Assert.False(VisualMeasurementGates.ValidateDisabled(sample, out string reason));
        Assert.Contains("visual work", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, sample.Timing.ControlOverheadMilliseconds);
    }

    [Fact]
    public void ReportDoesNotInventP95ForUnderSampledCell()
    {
        VisualMeasurementReport report = VisualMeasurementReportBuilder.Build(
            Enumerable.Range(1, 19)
                .Select(number => CreateSample(VisualMeasurementMode.DISABLED, number))
                .ToArray(),
            new string('b', 64));
        VisualMeasurementStatistic capture = Assert.Single(
            report.Cells[0].Statistics,
            statistic => statistic.Metric == "timing.captureMs");

        Assert.Equal(19, capture.SampleCount);
        Assert.Null(capture.P95);
        Assert.Contains("20", capture.Limitation!, StringComparison.Ordinal);
        Assert.Equal("PROVISIONAL", report.Outcome);
    }

    [Fact]
    public void ReportSupportsP95AfterTwentyComparableSamples()
    {
        VisualMeasurementSample[] samples = Enumerable.Range(1, 20)
            .SelectMany(number => new[]
            {
                CreateSample(VisualMeasurementMode.DISABLED, number),
                CreateSample(VisualMeasurementMode.CHECKPOINTS, number),
            })
            .ToArray();
        VisualMeasurementReport report = VisualMeasurementReportBuilder.Build(
            samples,
            new string('b', 64));
        VisualMeasurementCell enabledCell = Assert.Single(
            report.Cells,
            cell => cell.Mode == VisualMeasurementMode.CHECKPOINTS);
        VisualMeasurementStatistic capture = Assert.Single(
            enabledCell.Statistics,
            statistic => statistic.Metric == "timing.captureMs");

        Assert.True(capture.Available);
        Assert.Equal(20, capture.SampleCount);
        Assert.NotNull(capture.P95);
        Assert.Equal("PASS", report.Outcome);

        IReadOnlyList<VisualMeasurementBudget> budgets = VisualMeasurementBudgetSelector.Derive(report);
        Assert.NotEmpty(budgets);
        Assert.All(budgets, budget => Assert.False(budget.DiagnosticOnly));
    }

    [Fact]
    public void MissingRequiredResourceObservationBlocksEnabledSample()
    {
        VisualMeasurementSample sample = CreateSample(VisualMeasurementMode.CHECKPOINTS, 1)
            with
            {
                AfterResources = VisualResourceObservationFactory.Synthetic(VisualMeasurementMode.CHECKPOINTS)
                    with { HdcCount = null },
            };
        Assert.False(VisualMeasurementGates.ValidateEnabled(sample, out string reason));
        Assert.Contains("unavailable", reason, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void MeasuredBudgetGateRejectsDiagnosticAndOverBudgetSamples()
    {
        VisualMeasurementSample[] samples = Enumerable.Range(1, 20)
            .SelectMany(number => new[]
            {
                CreateSample(VisualMeasurementMode.DISABLED, number),
                CreateSample(VisualMeasurementMode.CHECKPOINTS, number),
            })
            .ToArray();
        VisualMeasurementReport measured = VisualMeasurementReportBuilder.Build(
            samples,
            new string('b', 64));
        IReadOnlyList<VisualMeasurementBudget> budgets = VisualMeasurementBudgetSelector.Derive(measured);
        VisualMeasurementSample overBudget = samples.Single(sample =>
                sample.Mode == VisualMeasurementMode.CHECKPOINTS
                && sample.SampleNumber == 20)
            with
            {
                Timing = samples.Single(sample =>
                        sample.Mode == VisualMeasurementMode.CHECKPOINTS
                        && sample.SampleNumber == 20)
                    .Timing with
                    {
                        CaptureMilliseconds = 10_000,
                    },
            };

        Assert.False(VisualMeasurementGates.ValidateMeasuredBudgets(
            overBudget,
            budgets,
            out string overBudgetReason));
        Assert.Contains("exceeds", overBudgetReason, StringComparison.OrdinalIgnoreCase);

        VisualMeasurementReport underSampled = VisualMeasurementReportBuilder.Build(
            new[]
            {
                CreateSample(VisualMeasurementMode.DISABLED, 1),
                CreateSample(VisualMeasurementMode.CHECKPOINTS, 1),
            },
            new string('b', 64));
        IReadOnlyList<VisualMeasurementBudget> diagnosticBudgets =
            VisualMeasurementBudgetSelector.Derive(underSampled);
        Assert.False(VisualMeasurementGates.ValidateMeasuredBudgets(
            underSampled.Samples.Single(sample => sample.Mode == VisualMeasurementMode.CHECKPOINTS),
            diagnosticBudgets,
            out string diagnosticReason));
        Assert.Contains("diagnostic", diagnosticReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PairedResourceGateRejectsEnabledNativeGrowth()
    {
        VisualMeasurementSample disabled = CreateSample(VisualMeasurementMode.DISABLED, 1);
        VisualMeasurementSample enabled = CreateSample(VisualMeasurementMode.CHECKPOINTS, 1)
            with
            {
                AfterResources = VisualResourceObservationFactory.Synthetic(
                    VisualMeasurementMode.CHECKPOINTS)
                    with { HdcCount = 3 },
            };
        var zeroDeltas = new Dictionary<string, long>(
            VisualMeasurementGates.RequiredResourceMetrics
                .Select(metric => new KeyValuePair<string, long>(metric, 0)));

        Assert.False(VisualMeasurementGates.ComparePairedResources(
            disabled,
            enabled,
            zeroDeltas,
            out string reason));
        Assert.Contains("hdc", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("increased", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportRequiresEveryEnabledSampleToHaveAPair()
    {
        VisualMeasurementReport report = VisualMeasurementReportBuilder.Build(
            new[]
            {
                CreateSample(VisualMeasurementMode.DISABLED, 1),
                CreateSample(VisualMeasurementMode.CHECKPOINTS, 1),
            },
            new string('b', 64));

        VisualMeasurementReport missingPair = report with
        {
            PairComparisons = Array.Empty<VisualResourcePairComparison>(),
        };

        Assert.Throws<ArgumentException>(() => missingPair.Validate());
    }



    [Fact]
    public void SyntheticRunnerEmitsPairedRepresentativeCells()
    {
        string root = Path.Combine(Path.GetTempPath(), "TabDock-visual-measurement-unit-" + Guid.NewGuid().ToString("N"));
        try
        {
            VisualMeasurementReport report = VisualMeasurementRunner.RunSynthetic(
                new string('a', 40),
                "run-id",
                new string('b', 64),
                "Debug",
                sampleCount: 1,
                seed: 20260824,
                root);

            Assert.Equal(30, report.Cells.Count);
            Assert.Equal(VisualMeasurementClassification.SYNTHETIC, report.Classification);
            Assert.Contains(report.Samples, sample => sample.Mode == VisualMeasurementMode.DISABLED);
            Assert.Contains(report.Samples, sample => sample.Mode == VisualMeasurementMode.CHECKPOINTS_PLUS_PACKET);
            Assert.Contains(report.Samples, sample => sample.Mode == VisualMeasurementMode.FLIGHT_HEALTHY_DISCARD);
            Assert.Contains(report.Samples, sample => sample.Mode == VisualMeasurementMode.FLIGHT_FAILURE_FLUSH);
            Assert.All(report.Samples, sample => Assert.True(sample.CleanupCompleted));
            Assert.Contains(report.Budgets, budget => budget.DiagnosticOnly);
            Assert.Equal(24, report.PairComparisons!.Count);
            Assert.All(report.PairComparisons, pair => Assert.Equal("PASS", pair.Outcome));
            Assert.All(report.Cells, cell => Assert.Equal(1, cell.SampleCount));
            Assert.Contains(
                report.Samples,
                sample => sample.Mode == VisualMeasurementMode.FLIGHT_FAILURE_FLUSH
                    && sample.Work.RingFlushes > 0
                    && sample.Timing.TriggerFrameMilliseconds >= 0);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static VisualMeasurementSample CreateSample(
        VisualMeasurementMode mode,
        int sampleNumber,
        long controlOverhead = 0,
        VisualMeasurementWork? work = null)
    {
        VisualEvidencePolicy policy = mode == VisualMeasurementMode.DISABLED
            ? VisualEvidencePolicy.Disabled
            : VisualEvidencePolicy.SafeDefaults(VisualEvidenceLevel.CHECKPOINTS);
        VisualMeasurementTiming timing = mode == VisualMeasurementMode.DISABLED
            ? new VisualMeasurementTiming(controlOverhead, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
            : new VisualMeasurementTiming(0, 4, 2, 1, 1, 1, 0, 0, 0, 0, 0);
        VisualMeasurementWork measuredWork = work ?? (mode == VisualMeasurementMode.DISABLED
            ? new VisualMeasurementWork(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
            : new VisualMeasurementWork(1, 1, 0, 1, 0, 0, 0, 1, 0, 1, 100, 1, 100, 0, 0));
        VisualResourceObservation resource = VisualResourceObservationFactory.Synthetic(mode);
        return new VisualMeasurementSample(
            DateTimeOffset.UtcNow,
            new string('a', 40),
            "run-id",
            "rename",
            "Debug",
            1,
            sampleNumber,
            mode,
            VisualMeasurementClassification.SYNTHETIC,
            new VisualMachineTopology("ci-synthetic", "single-monitor", 1, 96, "primary"),
            policy,
            mode == VisualMeasurementMode.DISABLED ? null : 96,
            mode == VisualMeasurementMode.DISABLED ? null : 64,
            mode == VisualMeasurementMode.DISABLED ? null : VisualCaptureMethod.SYNTHETIC,
            mode == VisualMeasurementMode.DISABLED ? null : VisualCaptureScopeKind.GUEST_WINDOW,
            timing,
            measuredWork,
            mode == VisualMeasurementMode.DISABLED ? 0 : 10,
            null,
            resource,
            resource,
            0,
            0,
            false,
            true);
    }
}
