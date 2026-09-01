using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace TabDock.ValidationDriver;

/// <summary>
/// Entry point for the safe resource qualification command. The default local
/// mode starts only the freshly selected TabDock artifact with an isolated
/// APPDATA and never sends input. --resource-headless runs the same lifecycle
/// profiles with deterministic synthetic snapshots for ordinary CI.
/// </summary>
internal static class ResourceSoakRunner
{
    public static int Run(Options options)
    {
        int cycles = options.Cycles ?? 100;
        string outputRoot = string.IsNullOrWhiteSpace(options.ResourceArtifactOutput)
            ? Path.Combine(Path.GetTempPath(), "TabDock-ResourceStability-" + Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(options.ResourceArtifactOutput);
        Environment.SetEnvironmentVariable("TABDOCK_VALIDATION_ARTIFACT_ROOT", outputRoot);
        Environment.SetEnvironmentVariable("TABDOCK_VALIDATION_RESULT_ROOT", null);

        Scenarios.ConfigureArtifacts(
            options.Configuration,
            options.Rid,
            options.TabDockPath,
            options.GuineaPigPath);
        if (!options.ResourceHeadless && !File.Exists(Scenarios.TabDockExe))
        {
            Console.WriteLine($"Resource soak requires a built TabDock executable: {Scenarios.TabDockExe}");
            return 4;
        }

        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "TabDock-resource-soak-" + Guid.NewGuid().ToString("N"));
        DateTimeOffset started = DateTimeOffset.UtcNow;
        IReadOnlyList<ResourceProfileResult> profiles = Array.Empty<ResourceProfileResult>();
        IReadOnlyList<ResourceSnapshot> snapshots = Array.Empty<ResourceSnapshot>();
        ResourceSeriesAnalysis series = ResourceSeriesAnalyzer.Analyze(
            "resource-soak",
            Array.Empty<ResourceSnapshot>());
        Process? targetProcess = null;
        bool synthetic = options.ResourceHeadless;
        string target = synthetic
            ? "headless deterministic lifecycle fixtures"
            : "run-owned TabDock process";
        string? harnessFailure = null;
        VisualMeasurementReport? visualMeasurements = null;

        TestRunProvenance.BeginRun();
        QualificationResultWriter.BeginRun();
        GuardedProc.ResetScenarioBudget();
        try
        {
            profiles = ResourceLifecycleProfiles.Run(
                options.ResourceProfile,
                cycles,
                options.ResourceSeed,
                temporaryRoot);

            if (options.ResourceHeadless)
            {
                snapshots = SyntheticSnapshots(cycles);
                visualMeasurements = VisualMeasurementRunner.RunSynthetic(
                    QualificationResultWriter.CandidateSha(),
                    TestRunProvenance.RunId,
                    QualificationResultWriter.DriverIdentitySha256(),
                    options.Configuration,
                    Math.Max(1, Math.Min(30, cycles)),
                    options.ResourceSeed,
                    Path.Combine(temporaryRoot, "visual-measurements"));
            }
            else
            {
                snapshots = CaptureProcessSeries(
                    options,
                    cycles,
                    temporaryRoot,
                    out targetProcess);
            }

            series = ResourceSeriesAnalyzer.Analyze(
                options.ResourceHeadless ? "headless-synthetic" : "tabdock-process",
                snapshots,
                new ResourceStabilityOptions
                {
                    WarmupSamples = 3,
                    MinimumSettledSamples = 5,
                    TailSampleCount = Math.Min(8, Math.Max(5, snapshots.Count / 3)),
                });
        }
        catch (OperationCanceledException)
        {
            harnessFailure = "resource soak exceeded the bounded driver budget or was cancelled";
        }
        catch (Exception ex)
        {
            harnessFailure = $"resource soak harness failure: {ex.GetType().Name}";
            GuardedProc.Log($"Resource soak error: {ex.Message}");
        }
        finally
        {
            RequestClose(targetProcess);
            GuardedProc.CleanupTrackedProcesses();
            targetProcess?.Dispose();
        }

        DateTimeOffset ended = DateTimeOffset.UtcNow;
        bool profilePass = profiles.Count > 0 && profiles.All(profile => profile.Passed);
        string outcome;
        string? failureReason;
        if (harnessFailure != null)
        {
            outcome = "BLOCKED_ENVIRONMENT";
            failureReason = harnessFailure;
        }
        else if (!profilePass)
        {
            outcome = "FAIL_PRODUCT";
            failureReason = profiles.FirstOrDefault(profile => !profile.Passed)?.FailureReason
                ?? "one or more bounded lifecycle profiles failed";
        }
        else if (series.Outcome == ResourceStabilityOutcome.Fail)
        {
            outcome = "FAIL_PRODUCT";
            failureReason = series.FailureReason;
        }
        else if (series.Outcome == ResourceStabilityOutcome.Blocked)
        {
            outcome = "BLOCKED_ENVIRONMENT";
            failureReason = series.FailureReason;
        }
        else if (visualMeasurements?.Outcome == "FAIL")
        {
            outcome = "FAIL_PRODUCT";
            failureReason = visualMeasurements.Limitation ?? "visual performance budget gate failed";
        }
        else if (visualMeasurements?.Outcome == "BLOCKED")
        {
            outcome = "BLOCKED_ENVIRONMENT";
            failureReason = visualMeasurements.Limitation ?? "visual performance measurement was blocked";
        }
        else
        {
            outcome = "PASS";
            failureReason = null;
        }

        ResourceStabilityOptions reportOptions = new();
        var artifact = new ResourceStabilityRunArtifact(
            SchemaVersion: 1,
            ArtifactKind: "resource-stability",
            RunId: TestRunProvenance.RunId,
            SourceSha: QualificationResultWriter.CandidateSha(),
            DriverSha: QualificationResultWriter.DriverIdentitySha256(),
            SyntheticMeasurements: synthetic,
            MeasurementTarget: target,
            StartedUtc: started,
            EndedUtc: ended,
            CycleCount: cycles,
            ProfileSelection: options.ResourceProfile,
            MetricDefinitions: ResourceStabilityArtifactWriter.Definitions(reportOptions),
            Profiles: profiles,
            ProcessSeries: series,
            Snapshots: snapshots,
            Outcome: outcome,
            FailureReason: failureReason,
            VisualMeasurements: visualMeasurements);
        try
        {
            QualificationResultWriter.WriteResourceStability(artifact);
            ScenarioOutcome runOutcome = QualificationResultWriter.WriteRunManifest();
            PrintSummary(artifact, outputRoot);
            return runOutcome.IsReleasePass ? 0 : outcome switch
            {
                "FAIL_PRODUCT" => 20,
                "BLOCKED_ENVIRONMENT" => 12,
                _ => 21,
            };
        }
        finally
        {
            TryDeleteTemporaryRoot(temporaryRoot);
        }
    }

    private static IReadOnlyList<ResourceSnapshot> CaptureProcessSeries(
        Options options,
        int cycles,
        string temporaryRoot,
        out Process? targetProcess)
    {
        if (HasUnownedTabDockProcess())
            throw new InvalidOperationException("an unowned TabDock process is already running");

        string appData = Path.Combine(temporaryRoot, "appdata", "Roaming");
        Directory.CreateDirectory(appData);
        var startInfo = new ProcessStartInfo(Scenarios.TabDockExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Scenarios.TabDockExe)!,
        };
        startInfo.Environment["APPDATA"] = appData;
        startInfo.Environment["LOCALAPPDATA"] = Path.Combine(temporaryRoot, "appdata", "Local");
        targetProcess = GuardedProc.SpawnGuarded(startInfo);
        if (!TestRunProvenance.RegisterLaunchedProcess(
                targetProcess,
                "ResourceSoakTabDock",
                out string processReason))
        {
            throw new InvalidOperationException($"resource target provenance failed: {processReason}");
        }

        uint pid = (uint)targetProcess.Id;
        IntPtr main = Discover.WaitForTopLevelWindow(
            pid,
            _ => true,
            20_000);
        if (main == IntPtr.Zero)
            throw new InvalidOperationException("resource target main window did not appear within 20 seconds");

        int sampleCount = options.ResourceDurationSeconds > 0
            ? Math.Max(8, options.ResourceDurationSeconds * 10)
            : Math.Max(8, cycles);
        var snapshots = new List<ResourceSnapshot>(sampleCount);
        for (int i = 0; i < sampleCount; i++)
        {
            Util.ThrowIfCancelled();
            ResourceSnapshotProbe.TryCapture(
                pid,
                i + 1,
                i < 3 ? "warmup" : "settled",
                out ResourceSnapshot snapshot,
                out _);
            snapshots.Add(snapshot);
            if (i + 1 < sampleCount)
                Thread.Sleep(100);
        }
        return snapshots;
    }

    private static bool HasUnownedTabDockProcess()
    {
        foreach (Process process in Process.GetProcessesByName("TabDock"))
        {
            using (process)
            {
                if (!process.HasExited)
                    return true;
            }
        }
        return false;
    }

    private static IReadOnlyList<ResourceSnapshot> SyntheticSnapshots(int cycles)
    {
        int sampleCount = Math.Max(8, cycles);
        var snapshots = new List<ResourceSnapshot>(sampleCount);
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        for (int i = 0; i < sampleCount; i++)
        {
            long warmup = i < 3 ? i * 10 : 30;
            snapshots.Add(new ResourceSnapshot(
                i + 1,
                i < 3 ? "warmup" : "settled",
                start.AddSeconds(i),
                new ResourceProcessIdentity(1, 1),
                100 + warmup,
                20 + warmup,
                30 + warmup,
                100L * 1024 * 1024 + warmup * 1024,
                50L * 1024 * 1024 + warmup * 1024,
                8,
                1));
        }
        return snapshots;
    }

    private static void RequestClose(Process? process)
    {
        if (process == null)
            return;
        try
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                process.WaitForExit(5_000);
            }
        }
        catch (Exception ex)
        {
            GuardedProc.Log($"Resource soak target close failed: {ex.GetType().Name}");
        }
    }

    private static void PrintSummary(
        ResourceStabilityRunArtifact artifact,
        string outputRoot)
    {
        Console.WriteLine();
        Console.WriteLine("RESOURCE STABILITY SUMMARY");
        Console.WriteLine($"source SHA: {artifact.SourceSha}");
        Console.WriteLine($"profile: {artifact.ProfileSelection}");
        Console.WriteLine($"cycles: {artifact.CycleCount}");
        Console.WriteLine($"measurement target: {artifact.MeasurementTarget}");
        if (artifact.VisualMeasurements is { } visual)
        {
            Console.WriteLine(
                $"visual measurements: outcome={visual.Outcome} classification={visual.Classification} "
                + $"samples={visual.Samples.Count} cells={visual.Cells.Count} pairs={visual.PairComparisons?.Count ?? 0}");
            if (visual.Limitation != null)
                Console.WriteLine($"visual measurement note: {visual.Limitation}");
        }
        foreach (ResourceMetricAnalysis metric in artifact.ProcessSeries.Metrics)
        {
            string baseline = metric.Baseline?.ToString() ?? "unavailable";
            string final = metric.Final?.ToString() ?? "unavailable";
            string trend = metric.Trend.ToString();
            Console.WriteLine($"{metric.Metric}: {baseline} -> {final} trend={trend} delta={metric.FinalDelta?.ToString() ?? "unavailable"}");
        }
        Console.WriteLine($"outcome: {artifact.Outcome}");
        if (artifact.FailureReason != null)
            Console.WriteLine($"reason: {artifact.FailureReason}");
        Console.WriteLine($"artifact directory: {outputRoot}");
    }

    private static void TryDeleteTemporaryRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (Exception ex)
        {
            GuardedProc.Log($"Resource soak temporary cleanup left residue: {ex.GetType().Name}");
        }
    }
}
