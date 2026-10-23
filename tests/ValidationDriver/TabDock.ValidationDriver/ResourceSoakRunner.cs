using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using TabDock.Services;

namespace TabDock.ValidationDriver;

/// <summary>
/// Entry point for the safe resource qualification command. The default local
/// mode starts only the freshly selected TabDock artifact inside a verified
/// run-owned validation storage root (never the production profile) and never
/// sends input. --resource-headless runs the same lifecycle
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
        string? validationRunId = null;
        string validationCandidate = Path.GetFullPath(Scenarios.TabDockExe);
        string? validationRoot = null;
        string target = synthetic
            ? "headless deterministic lifecycle fixtures"
            : "run-owned TabDock process";
        string? harnessFailure = null;
        VisualMeasurementReport? visualMeasurements = null;

        TestRunProvenance.BeginRun();
        // The run identity is minted by BeginRun; capture it only afterwards so
        // the storage marker, child proof and run manifest all bind the SAME
        // current run ID (capturing first produced the pre-init empty GUID).
        validationRunId = TestRunProvenance.RunId;
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
                // A02 storage isolation: create a fresh run-owned product
                // directory with an ownership marker, then obtain matching
                // read-only root/candidate/run proof from the exact child
                // candidate BEFORE any mutating child is started. A missing,
                // stale or mismatched proof refuses the launch here — before
                // normal application startup — and never authorizes any
                // production-profile cleanup.
                validationRoot = ValidationStorageProof.CreateRunOwnedRoot(
                    temporaryRoot, validationRunId, validationCandidate);
                string? proofRefusal = ValidationStorageProof.VerifyChildProof(
                    Scenarios.TabDockExe, validationRoot, validationRunId, validationCandidate);
                if (proofRefusal != null)
                    throw new InvalidOperationException(
                        $"storage isolation preflight refused the mutating launch ({proofRefusal})");
                // Marker/proof/manifest identity agreement, verified before any
                // child starts and without launching the UI soak.
                if (!string.Equals(validationRunId, TestRunProvenance.RunId, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "storage isolation run identity drifted from the initialized run manifest");
                snapshots = CaptureProcessSeries(
                    options,
                    cycles,
                    temporaryRoot,
                    validationRoot,
                    validationRunId,
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
            TryDeleteTemporaryRoot(temporaryRoot, validationRoot, validationRunId, validationCandidate);
        }
    }

    private static IReadOnlyList<ResourceSnapshot> CaptureProcessSeries(
        Options options,
        int cycles,
        string temporaryRoot,
        string validationRoot,
        string validationRunId,
        out Process? targetProcess)
    {
        if (HasUnownedTabDockProcess())
            throw new InvalidOperationException("an unowned TabDock process is already running");

        var startInfo = new ProcessStartInfo(Scenarios.TabDockExe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Scenarios.TabDockExe)!,
        };
        // The paired validation inputs are the authoritative isolation (A02):
        // the child revalidates its own root and fail-closes on any mismatch.
        // The APPDATA/LOCALAPPDATA redirects below are defense in depth only
        // and are never accepted as isolation proof.
        startInfo.Environment[ValidationStorageProof.RootEnvironmentVariable] = validationRoot;
        startInfo.Environment[ValidationStorageProof.RunIdEnvironmentVariable] = validationRunId;
        startInfo.Environment["APPDATA"] = Path.Combine(temporaryRoot, "appdata", "Roaming");
        startInfo.Environment["LOCALAPPDATA"] = Path.Combine(temporaryRoot, "appdata", "Local");
        Directory.CreateDirectory(Path.Combine(temporaryRoot, "appdata", "Roaming"));
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
        // Prescribed counters only. They stay labeled synthetic and are not
        // evidence that styled motion controls were created or collected.
        // Actual-control collectability is MotionFeedbackLifecycleTests.
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

    /// <summary>
    /// Run-owned cleanup (A02): recursive deletion of the temporary tree is
    /// authorized only while the ownership marker in the validation product
    /// root still identifies this run. Lost or ambiguous ownership retains the
    /// tree for review and never broadens the cleanup scope; a failed preflight
    /// never authorizes any production-profile cleanup. Internal and
    /// result-returning so the maintained self-tests can drive this exact
    /// outer cleanup boundary.
    /// </summary>
    internal static bool TryDeleteTemporaryRoot(string root, string? validationRoot, string? validationRunId, string validationCandidate)
    {
        try
        {
            if (validationRoot != null)
            {
                // R2: a RECORDED root must re-prove current ownership even
                // when it no longer exists (missing, renamed, replaced). The
                // enclosing tree may hold the moved evidence; it is retained
                // for review instead of being deleted by parent cleanup.
                if (!Directory.Exists(validationRoot)
                    || !ValidationStorageProof.TryCleanupRunOwnedRoot(
                        validationRoot, validationRunId ?? string.Empty, validationCandidate, out string? refusalReason))
                {
                    GuardedProc.Log(
                        "Resource soak retained ambiguous validation storage for review; no recursive cleanup was performed.");
                    return false;
                }
            }
            // R1: never delete through a changed boundary — the enclosing
            // cleanup path must still be free of reparse components.
            if (ProductStorage.HasReparsePointComponent(root))
            {
                GuardedProc.Log(
                    "Resource soak retained its temporary tree: a cleanup path component is a reparse point.");
                return false;
            }
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            GuardedProc.Log($"Resource soak temporary cleanup left residue: {ex.GetType().Name}");
            return false;
        }
    }
}
