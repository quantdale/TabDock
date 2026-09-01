using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace TabDock.ValidationDriver;

/// <summary>
/// Writes the common scenario result contract used by every interactive tier.
/// Values are role/identity/geometry evidence; arbitrary desktop titles and
/// URLs are never copied into the result artifact.
/// </summary>
internal static class QualificationResultWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object ManifestSync = new();
    private static readonly List<ScenarioManifestEntry> ManifestEntries = new();
    private static DateTimeOffset _runStartedUtc;

    private sealed record ScenarioManifestEntry(
        string Scenario,
        int Attempt,
        string Result,
        string? Reason,
        object? Capabilities,
        string JsonArtifact,
        string JUnitArtifact,
        string TimelineArtifact,
        DateTimeOffset StartedUtc,
        DateTimeOffset EndedUtc,
        VisualEvidencePolicy? VisualPolicy = null,
        string? VisualManifestArtifact = null,
        string? VisualManifestSha256 = null,
        IReadOnlyList<VisualArtifactRecord>? VisualArtifacts = null,
        string? VisualReviewPacketArtifact = null,
        string? VisualReviewPacketSha256 = null,
        string? VisualReviewInstructionsArtifact = null,
        IReadOnlyList<string>? VisualDerivedFailureIds = null,
        string? VisualReviewResultArtifact = null,
        string? VisualPerformanceArtifact = null,
        string? VisualPerformanceJUnitArtifact = null);

    public static void BeginRun()
    {
        lock (ManifestSync)
        {
            ManifestEntries.Clear();
            _runStartedUtc = DateTimeOffset.UtcNow;
        }
    }

    public static void WriteDeterministic(string suite, IReadOnlyList<AssertionEvidence> assertions)
    {
        string root = ResultRoot();
        Directory.CreateDirectory(root);
        int failed = assertions.Count(a => !a.Passed);
        var result = new
        {
            runId = TestRunProvenance.RunId,
            scenario = $"deterministic-{suite}",
            iteration = 1,
            startedUtc = (DateTimeOffset?)null,
            endedUtc = DateTimeOffset.UtcNow,
            candidateSha = CandidateSha(),
            applicationVersion = ApplicationVersion(),
            environment = EnvironmentFingerprint(),
            result = ScenarioOutcomeContract.Code(failed == 0
                ? ScenarioOutcomeKind.Pass
                : ScenarioOutcomeKind.FailHarness),
            failureReason = failed == 0 ? null : $"{failed} deterministic contract assertion(s) failed",
            expectedState = "all selected native-free split and provenance contracts pass",
            observedState = $"passed={assertions.Count - failed} failed={failed} total={assertions.Count}",
            splitRelationshipMembers = Array.Empty<string>(),
            splitPairPresented = (bool?)null,
            activeGuest = (string?)null,
            visibleHwndSet = Array.Empty<object>(),
            foregroundHwnd = "0x0",
            guestRectangles = Array.Empty<object>(),
            paneRectangles = Array.Empty<object>(),
            clientRenderingEvidence = Array.Empty<object>(),
            testIdentities = TestRunProvenance.ScopeSummary(),
            assertions,
            diagnosticLogOffset = 0L,
            traceArtifacts = new[] { "<validation-artifact>/deterministic-selftest.json" },
        };
        string stem = SafeFileName($"deterministic-{suite}");
        File.WriteAllText(Path.Combine(root, $"{stem}.json"), JsonSerializer.Serialize(result, JsonOptions), Encoding.UTF8);
        WriteDeterministicJUnit(root, stem, suite, assertions, failed);
        ScenarioOutcome outcome = new(
            failed == 0 ? ScenarioOutcomeKind.Pass : ScenarioOutcomeKind.FailHarness,
            failed == 0 ? null : $"{failed} deterministic contract assertion(s) failed");
        RegisterManifestEntry(new ScenarioManifestEntry(
            $"deterministic-{suite}",
            1,
            outcome.Code,
            outcome.Reason,
            null,
            $"{stem}.json",
            $"{stem}.junit.xml",
            string.Empty,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
        GuardedProc.Log($"RESULT_JSON scenario=deterministic-{suite} status={outcome.Code} artifact=<validation-artifact>/{stem}.json");
    }

    public static void WriteTopologyLab(VirtualTopologyLabReport report)
    {
        string root = ResultRoot();
        Directory.CreateDirectory(root);
        const string stem = "virtual-topology-lab";
        var assertions = report.Cases
            .Select(item => new AssertionEvidence(item.Name, item.Passed))
            .ToArray();
        int failed = assertions.Count(item => !item.Passed);
        var payload = new
        {
            schemaVersion = report.SchemaVersion,
            labGeneration = report.Generation,
            syntheticTopology = report.SyntheticTopology,
            seed = report.Seed,
            passed = report.Passed,
            assertionCount = report.AssertionCount,
            normalizedSha256 = report.NormalizedSha256,
            topologies = report.Cases.Select(item => new
            {
                name = item.Name,
                monitorCount = item.MonitorCount,
                dpiValues = item.DpiValues,
                negativeCoordinates = item.NegativeCoordinates,
                aboveOrigin = item.AboveOrigin,
                staggeredPlacement = item.StaggeredPlacement,
                asymmetricWorkAreas = item.AsymmetricWorkAreas,
                relativePlacements = item.RelativePlacements,
                titleAssertionCount = item.TitleAssertionCount,
                passed = item.Passed,
                assertionCount = item.AssertionCount,
                failure = item.Failure,
            }).ToArray(),
            dpiTransitions = report.DpiTransitions.Select(item => new
            {
                name = item.Name,
                sourceMonitorId = item.SourceMonitorId,
                destinationMonitorId = item.DestinationMonitorId,
                sourceDpi = item.SourceDpi,
                destinationDpi = item.DestinationDpi,
                passed = item.Passed,
                assertionCount = item.AssertionCount,
                failure = item.Failure,
            }).ToArray(),
        };
        string jsonPath = Path.Combine(root, $"{stem}.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8);
        WriteDeterministicJUnit(root, stem, "virtual-topology-lab", assertions, failed);
        ScenarioOutcome outcome = new(
            report.Passed ? ScenarioOutcomeKind.Pass : ScenarioOutcomeKind.FailHarness,
            report.Passed ? null : "one or more virtual topology laboratory assertions failed");
        RegisterManifestEntry(new ScenarioManifestEntry(
            "virtual-topology-lab",
            1,
            outcome.Code,
            outcome.Reason,
            new { syntheticTopology = true, seed = report.Seed, labGeneration = report.Generation },
            $"{stem}.json",
            $"{stem}.junit.xml",
            string.Empty,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
        GuardedProc.Log($"RESULT_JSON scenario=virtual-topology-lab status={outcome.Code} syntheticTopology=true seed={report.Seed} artifact=<validation-artifact>/{stem}.json");
    }

    internal static void WriteResourceStability(ResourceStabilityRunArtifact artifact)
    {
        string root = ResultRoot();
        string jsonPath = ResourceStabilityArtifactWriter.Write(artifact, root);
        string junitPath = ResourceStabilityArtifactWriter.WriteJUnit(artifact, root);
        string? performancePath = null;
        string? performanceJUnitPath = null;
        if (artifact.VisualMeasurements is { } report)
        {
            performancePath = Path.GetFileName(VisualMeasurementReportWriter.Write(report, root));
            performanceJUnitPath = Path.GetFileName(VisualMeasurementReportWriter.WriteJUnit(report, root));
        }
        RegisterManifestEntry(new ScenarioManifestEntry(
            "resource-stability",
            1,
            artifact.Outcome,
            artifact.FailureReason,
            new
            {
                resourceOnly = true,
                syntheticMeasurements = artifact.SyntheticMeasurements,
                measurementTarget = artifact.MeasurementTarget,
                visualPerformanceArtifact = performancePath,
                visualPerformanceOutcome = artifact.VisualMeasurements?.Outcome,
            },
            Path.GetFileName(jsonPath),
            Path.GetFileName(junitPath),
            string.Empty,
            artifact.StartedUtc,
            artifact.EndedUtc,
            VisualPerformanceArtifact: performancePath,
            VisualPerformanceJUnitArtifact: performanceJUnitPath));
        GuardedProc.Log(
            $"RESULT_JSON scenario=resource-stability status={artifact.Outcome} " +
            $"syntheticMeasurements={artifact.SyntheticMeasurements.ToString().ToLowerInvariant()} " +
            $"visualPerformance={artifact.VisualMeasurements?.Outcome ?? "none"} " +
            "artifact=<validation-artifact>/resource-stability.json");
    }

    public static void WriteScenario(Ctx ctx)
    {
        ctx.FinishedUtc ??= DateTimeOffset.UtcNow;
        string root = ResultRoot();
        Directory.CreateDirectory(root);
        SplitEvidence split = ReadSplitEvidence(ctx);
        string[] relationshipMembers = ctx.LiveSplitRelationshipMembers ?? split.Members;
        bool pairPresented = ctx.LiveSplitPairPresented ?? split.Presented;
        string? activeGuest = ctx.LiveActiveGuest ?? split.ActiveGuest;

        string stem = SafeFileName(ctx.Name);
        if (ctx.Attempt > 1)
            stem += $"-attempt-{ctx.Attempt}";
        string timelineName = $"{stem}.timeline.json";
        string timelinePath = Path.Combine(root, timelineName);
        try
        {
            ctx.Timeline.Record("scenario-result", data: new Dictionary<string, string>
            {
                ["result"] = ctx.Outcome.Code,
            });
            ctx.Timeline.Write(timelinePath);
        }
        catch (Exception ex)
        {
            GuardedProc.Log($"  Timeline artifact unavailable: {ex.GetType().Name}.");
        }

        VisualStoredArtifact? visualManifest = null;
        VisualStoredArtifact? reviewPacket = null;
        VisualStoredArtifact? reviewInstructions = null;
        string? reviewResultArtifact = null;
        if (ctx.Visual is { } visual)
        {
            try
            {
                if (visual.Policy.BuildReviewPacket
                    && !visual.TryBuildContactSheet(out _, out string contactSheetReason))
                {
                    GuardedProc.Log($"  Contact-sheet unavailable: {contactSheetReason}.");
                }

                VisualEvidenceManifest preliminaryManifest = visual.CreateManifest(
                    CandidateSha(),
                    TestRunProvenance.RunId,
                    ctx.StartedUtc,
                    ctx.FinishedUtc.Value);
                if (visual.Policy.BuildReviewPacket)
                {
                    if (VisualReviewPacketBuilder.TryBuild(
                        preliminaryManifest,
                        ReviewCorrelations(ctx),
                        out VisualReviewPacketBuildResult? packet,
                        out string packetReason)
                        && packet != null)
                    {
                        try
                        {
                            (reviewPacket, reviewInstructions) = visual.WriteReviewPacket(packet);
                            reviewResultArtifact = packet.ResultRelativePath;
                        }
                        catch (Exception ex)
                        {
                            visual.RecordReviewUnavailable($"visual review packet write failed: {ex.GetType().Name}");
                            GuardedProc.Log($"  Visual review packet unavailable: {ex.GetType().Name}.");
                        }
                    }
                    else
                    {
                        visual.RecordReviewUnavailable(packetReason);
                        GuardedProc.Log($"  Visual review packet unavailable: {packetReason}.");
                    }
                }

                VisualEvidenceManifest manifest = visual.CreateManifest(
                    CandidateSha(),
                    TestRunProvenance.RunId,
                    ctx.StartedUtc,
                    ctx.FinishedUtc.Value,
                    reviewPacket?.RelativePath,
                    reviewPacket?.Sha256);
                visualManifest = visual.WriteManifest(
                    manifest,
                    $"visual/{SafeFileName(ctx.Name)}/attempt-{ctx.Attempt:D3}/manifest.json");
            }
            catch (Exception ex)
            {
                ctx.FailHarness($"visual manifest could not be finalized: {ex.GetType().Name}");
                GuardedProc.Log($"  Visual manifest unavailable: {ex.GetType().Name}.");
            }
        }
        var traceArtifacts = new List<string>
        {
            $"<validation-artifact>/{Path.GetFileName(TestRunProvenance.ArtifactDirectory)}",
            $"<validation-artifact>/{timelineName}",
        };
        if (visualManifest != null)
            traceArtifacts.Add($"<validation-artifact>/{visualManifest.RelativePath}");
        if (reviewPacket != null)
            traceArtifacts.Add($"<validation-artifact>/{reviewPacket.RelativePath}");
        if (reviewInstructions != null)
            traceArtifacts.Add($"<validation-artifact>/{reviewInstructions.RelativePath}");


        var result = new
        {
            runId = TestRunProvenance.RunId,
            scenario = ctx.Name,
            iteration = ctx.Attempt,
            startedUtc = ctx.StartedUtc,
            endedUtc = ctx.FinishedUtc,
            candidateSha = CandidateSha(),
            applicationVersion = ApplicationVersion(),
            environment = EnvironmentFingerprint(),
            capabilities = CapabilityEvidence(ctx.Capabilities),
            desktopQualification = DesktopEvidence(
                ctx.DesktopLease?.Snapshot,
                ctx.DesktopLease?.RestoredSnapshot,
                ctx.DesktopTopologyRestored),
            result = ctx.Outcome.Code,
            outcomeReason = ctx.Outcome.Reason,
            failureReason = ctx.FailureReasons.Count == 0 ? null : string.Join("; ", ctx.FailureReasons),
            expectedState = ctx.ExpectedState,
            observedState = ctx.ObservedState,
            splitRelationshipMembers = relationshipMembers,
            splitPairPresented = pairPresented,
            activeGuest = activeGuest,
            visibleHwndSet = ctx.LiveVisibleHwndSet ?? VisibleGuests(ctx),
            foregroundHwnd = ctx.LiveForegroundHwnd ?? Hwnd(NativeMethods.GetForegroundWindow()),
            guestRectangles = ctx.LiveGuestRectangles ?? GuestGeometry(ctx),
            paneRectangles = ctx.LivePaneRectangles ?? PaneGeometry(ctx, split),
            clientRenderingEvidence = ctx.LiveClientRenderingEvidence ?? GuestGeometry(ctx),
            testIdentities = TestRunProvenance.ScopeSummary(),
            ownership = TestRunProvenance.OwnershipSummary(),
            assertions = ctx.Assertions,
            diagnosticLogOffset = ctx.LogOffset,
            visualEvidence = ctx.Visual == null
                ? null
                : new
                {
                    policy = VisualPolicyResolver.Describe(ctx.Visual.Policy),
                    topologyBinding = ctx.Visual.TopologyBinding is { } binding
                        ? TopologyBindingEvidence(binding)
                        : null,
                    manifestArtifact = visualManifest?.RelativePath,
                    manifestSha256 = visualManifest?.Sha256,
                    reviewPacketArtifact = reviewPacket?.RelativePath,
                    reviewPacketSha256 = reviewPacket?.Sha256,
                    reviewInstructionsArtifact = reviewInstructions?.RelativePath,
                    reviewResultArtifact = reviewResultArtifact,
                    artifactCount = ctx.Visual.Artifacts.Count,
                    unavailableCount = ctx.Visual.Unavailable.Count,
                    derivedArtifactFailureCount = ctx.Visual.DerivedFailures.Count,
                    derivedArtifactFailureIds = ctx.Visual.DerivedFailures
                        .Select(failure => failure.FailureId)
                        .ToArray(),
                    counters = ctx.Visual.Counters,
                },
            traceArtifacts = traceArtifacts.ToArray(),
        };
        string jsonPath = Path.Combine(root, $"{stem}.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(result, JsonOptions), Encoding.UTF8);
        WriteJUnit(root, stem, ctx);
        RegisterManifestEntry(new ScenarioManifestEntry(
            ctx.Name,
            ctx.Attempt,
            ctx.Outcome.Code,
            ctx.Outcome.Reason,
            CapabilityEvidence(ctx.Capabilities),
            $"{stem}.json",
            $"{stem}.junit.xml",
            timelineName,
            ctx.StartedUtc,
            ctx.FinishedUtc.Value,
            ctx.Visual?.Policy,
            visualManifest?.RelativePath,
            visualManifest?.Sha256,
            ctx.Visual?.Artifacts.ToArray(),
            reviewPacket?.RelativePath,
            reviewPacket?.Sha256,
            reviewInstructions?.RelativePath,
            ctx.Visual?.DerivedFailures.Select(failure => failure.FailureId).ToArray(),
            reviewResultArtifact));
        GuardedProc.Log($"RESULT_JSON scenario={ctx.Name} status={ctx.Outcome.Code} artifact=<validation-artifact>/{Path.GetFileName(jsonPath)}");
    }

    /// <summary>Writes the single root manifest and returns its canonical run outcome.</summary>
    public static ScenarioOutcome WriteRunManifest()
    {
        string root = ResultRoot();
        Directory.CreateDirectory(root);
        ScenarioManifestEntry[] entries;
        DateTimeOffset started;
        lock (ManifestSync)
        {
            entries = ManifestEntries
                .OrderBy(entry => entry.Scenario, StringComparer.Ordinal)
                .ThenBy(entry => entry.StartedUtc)
                .ToArray();
            started = _runStartedUtc == default ? DateTimeOffset.UtcNow : _runStartedUtc;
        }

        var scenarioAggregates = entries
            .GroupBy(entry => entry.Scenario, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                ScenarioAttempt[] attempts = group
                    .OrderBy(entry => entry.Attempt)
                    .Select(entry => new ScenarioAttempt(
                        entry.Scenario,
                        entry.Attempt,
                        new ScenarioOutcome(ParseOutcome(entry.Result), entry.Reason)))
                    .ToArray();
                ScenarioOutcome final = new ScenarioAggregate(group.Key, attempts).FinalOutcome;
                return new
                {
                    scenario = group.Key,
                    first = attempts[0].Outcome.Code,
                    final = final.Code,
                    finalReason = final.Reason,
                    attempts = attempts.Select(attempt => new
                    {
                        attempt = attempt.Attempt,
                        result = attempt.Outcome.Code,
                        reason = attempt.Outcome.Reason,
                    }).ToArray(),
                };
            })
            .ToArray();
        ScenarioOutcome[] finalOutcomes = scenarioAggregates
            .Select(aggregate => new ScenarioOutcome(ParseOutcome(aggregate.final), aggregate.finalReason))
            .ToArray();
        ScenarioOutcome outcome = finalOutcomes.Length == 0
            ? new ScenarioOutcome(ScenarioOutcomeKind.FailHarness, "no scenario result was recorded")
            : ScenarioOutcomeContract.Aggregate(finalOutcomes);

        var counts = Enum.GetValues<ScenarioOutcomeKind>()
            .ToDictionary(
                kind => ScenarioOutcomeContract.Code(kind),
                kind => finalOutcomes.Count(final => final.Kind == kind),
                StringComparer.Ordinal);
        var attemptCounts = Enum.GetValues<ScenarioOutcomeKind>()
            .ToDictionary(
                kind => ScenarioOutcomeContract.Code(kind),
                kind => entries.Count(entry => string.Equals(entry.Result, ScenarioOutcomeContract.Code(kind), StringComparison.Ordinal)),
                StringComparer.Ordinal);
        string driverPath = DriverIdentityPath();
        var manifest = new
        {
            schemaVersion = 2,
            runKind = RunKind(),
            runId = TestRunProvenance.RunId,
            parentRunId = Environment.GetEnvironmentVariable("TABDOCK_VALIDATION_PARENT_RUN_ID"),
            shard = Environment.GetEnvironmentVariable("TABDOCK_VALIDATION_SHARD"),
            manifestRelativePath = "run-manifest.json",
            catalogGeneration = ScenarioCatalog.Generation,
            candidateSha = CandidateSha(),
            branch = GitBranch(),
            applicationVersion = ApplicationVersion(),
            startedUtc = started,
            endedUtc = DateTimeOffset.UtcNow,
            environment = EnvironmentFingerprint(),
            outcome = outcome.Code,
            outcomeReason = outcome.Reason,
            aggregateCounts = counts,
            attemptCounts,
            capabilityMatrix = entries
                .Where(entry => entry.Capabilities != null)
                .OrderBy(entry => entry.Scenario, StringComparer.Ordinal)
                .ThenBy(entry => entry.Attempt)
                .Select(entry => new
                {
                    scenario = entry.Scenario,
                    attempt = entry.Attempt,
                    capabilities = entry.Capabilities,
                })
                .ToArray(),
            executableSha256 = new
            {
                candidate = Sha256File(Scenarios.TabDockExe),
                test = Sha256File(driverPath),
            },
            driverIdentity = new
            {
                fileName = Path.GetFileName(driverPath),
                sha256 = Sha256File(driverPath),
            },
            scenarios = entries.Select(entry => new
            {
                scenario = entry.Scenario,
                attempt = entry.Attempt,
                result = entry.Result,
                reason = entry.Reason,
                capabilities = entry.Capabilities,
                jsonArtifact = entry.JsonArtifact,
                junitArtifact = entry.JUnitArtifact,
                timelineArtifact = entry.TimelineArtifact,
                visualManifestArtifact = entry.VisualManifestArtifact,
                visualManifestSha256 = entry.VisualManifestSha256,
                visualReviewPacketArtifact = entry.VisualReviewPacketArtifact,
                visualReviewPacketSha256 = entry.VisualReviewPacketSha256,
                visualReviewInstructionsArtifact = entry.VisualReviewInstructionsArtifact,
                visualDerivedFailureIds = entry.VisualDerivedFailureIds,
                visualReviewResultArtifact = entry.VisualReviewResultArtifact,
                visualPerformanceArtifact = entry.VisualPerformanceArtifact,
                visualPerformanceJUnitArtifact = entry.VisualPerformanceJUnitArtifact,
                visualArtifactCount = entry.VisualArtifacts?.Count ?? 0,
                startedUtc = entry.StartedUtc,
                endedUtc = entry.EndedUtc,
            }).ToArray(),
            scenarioAggregates,
            artifactIndex = ArtifactIndex(entries, root),
            ownership = TestRunProvenance.OwnershipSummary(),
        };
        string path = Path.Combine(root, "run-manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions), Encoding.UTF8);
        GuardedProc.Log($"RUN_MANIFEST result={outcome.Code} artifact=<validation-artifact>/run-manifest.json");
        return outcome;
    }

    private static string RunKind()
    {
        string? configured = Environment.GetEnvironmentVariable("TABDOCK_VALIDATION_RUN_KIND");
        return configured is "direct" or "shard" or "all" or "deterministic"
            ? configured
            : "direct";
    }

    private static object[] ArtifactIndex(ScenarioManifestEntry[] entries, string root)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<object>();
        foreach (ScenarioManifestEntry entry in entries)
        {
            Add(entry.JsonArtifact, "scenario-result");
            Add(entry.JUnitArtifact, "junit");
            Add(entry.TimelineArtifact, "timeline");
            Add(entry.VisualManifestArtifact, "visual-manifest");
            Add(entry.VisualReviewPacketArtifact, "visual-review-packet");
            Add(entry.VisualReviewInstructionsArtifact, "visual-review-instructions");
            Add(entry.VisualReviewResultArtifact, "visual-review-result");
            Add(entry.VisualPerformanceArtifact, "visual-performance-report");
            Add(entry.VisualPerformanceJUnitArtifact, "visual-performance-junit");
            foreach (VisualArtifactRecord artifact in entry.VisualArtifacts ?? Array.Empty<VisualArtifactRecord>())
                Add(artifact.RelativePath, artifact.Derived ? "visual-derived-image" : "visual-image");
        }
        return result.ToArray();

        void Add(string? relativePath, string kind)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || !paths.Add(relativePath))
                return;
            string fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            bool exists = File.Exists(fullPath);
            result.Add(new
            {
                relativePath,
                kind,
                exists,
                sha256 = exists ? Sha256File(fullPath) : "MISSING",
            });
        }
    }
    private static IReadOnlyDictionary<string, string> ReviewCorrelations(Ctx ctx)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nativeOutcome"] = ctx.Outcome.Code,
            ["desktopLeaseValid"] = (ctx.DesktopLeaseValidAtCompletion
                ?? ctx.DesktopLease?.IsValid
                ?? false).ToString().ToLowerInvariant(),
            ["assertionCount"] = ctx.Assertions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["timelineArtifact"] = ctx.Attempt > 1
                ? $"{SafeFileName(ctx.Name)}-attempt-{ctx.Attempt}.timeline.json"
                : $"{SafeFileName(ctx.Name)}.timeline.json",
        };

    public static void CaptureLiveEvidence(Ctx ctx)
    {
        try
        {
            SplitEvidence split = ReadSplitEvidence(ctx);
            ctx.LiveSplitRelationshipMembers = split.Members;
            ctx.LiveSplitPairPresented = split.Presented;
            ctx.LiveActiveGuest = split.ActiveGuest;
            ctx.LiveVisibleHwndSet = VisibleGuests(ctx);
            ctx.LiveForegroundHwnd = Hwnd(NativeMethods.GetForegroundWindow());
            ctx.LiveGuestRectangles = GuestGeometry(ctx);
            ctx.LivePaneRectangles = PaneGeometry(ctx, split);
            ctx.LiveClientRenderingEvidence = GuestGeometry(ctx);
        }
        catch (Exception ex)
        {
            GuardedProc.Log($"  Result evidence snapshot unavailable before cleanup: {ex.GetType().Name}.");
        }
    }

    private sealed record SplitEvidence(string[] Members, bool Presented, string? ActiveGuest);

    /// <summary>
    /// Reconstructs the final logical split projection from the app's bounded
    /// SPLIT telemetry. This is diagnostic evidence only; pass/fail assertions
    /// remain the live geometry/UIA checks in the scenarios themselves.
    /// </summary>
    private static SplitEvidence ReadSplitEvidence(Ctx ctx)
    {
        string? left = null;
        string? right = null;
        string? active = null;
        bool presented = false;

        foreach (string line in TabDockLog.ReadNewLines(ctx.LogOffset))
        {
            Match enter = Regex.Match(line, @"SPLIT\[enter\] left=0x([0-9A-Fa-f]+) right=0x([0-9A-Fa-f]+)");
            if (enter.Success)
            {
                left = Hwnd(ParseHex(enter.Groups[1].Value));
                right = Hwnd(ParseHex(enter.Groups[2].Value));
                active = left;
                presented = true;
                continue;
            }

            Match suspend = Regex.Match(line, @"SPLIT\[(?:suspend|single)\] guest=0x([0-9A-Fa-f]+)");
            if (suspend.Success)
            {
                active = Hwnd(ParseHex(suspend.Groups[1].Value));
                presented = false;
                continue;
            }

            Match resume = Regex.Match(line, @"SPLIT\[resume\].*focused=0x([0-9A-Fa-f]+)");
            if (resume.Success)
            {
                active = Hwnd(ParseHex(resume.Groups[1].Value));
                presented = true;
                continue;
            }

            Match focus = Regex.Match(line, @"SPLIT\[focus\] guest=0x([0-9A-Fa-f]+)");
            if (focus.Success)
            {
                active = Hwnd(ParseHex(focus.Groups[1].Value));
                presented = true;
                continue;
            }

            if (line.Contains("SPLIT[exit]", StringComparison.Ordinal)
                || line.Contains("SPLIT[member-gone]", StringComparison.Ordinal))
            {
                left = null;
                right = null;
                active = null;
                presented = false;
            }
        }

        return new SplitEvidence(
            left != null && right != null ? new[] { left, right } : Array.Empty<string>(),
            left != null && right != null && presented,
            active);
    }

    private static IntPtr ParseHex(string value)
    {
        return long.TryParse(value, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out long parsed)
            ? new IntPtr(parsed)
            : IntPtr.Zero;
    }

    private static object[] PaneGeometry(Ctx ctx, SplitEvidence split)
    {
        if (!split.Presented || split.Members.Length != 2)
            return Array.Empty<object>();

        var result = new List<object>();
        string[] sides = { "left", "right" };
        for (int i = 0; i < split.Members.Length; i++)
        {
            IntPtr hwnd = ParseHwnd(split.Members[i]);
            GuestInfo? guest = ctx.Guests.FirstOrDefault(item => item.Hwnd == hwnd);
            if (guest == null || !NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
                continue;
            result.Add(new { side = sides[i], hwnd = Hwnd(hwnd), rect = Rect(rect), role = guest.Role });
        }
        return result.ToArray();
    }

    private static IntPtr ParseHwnd(string value)
    {
        string digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        return ParseHex(digits);
    }

    private static string ResultRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("TABDOCK_VALIDATION_RESULT_ROOT");
        return string.IsNullOrWhiteSpace(configured)
            ? TestRunProvenance.ArtifactDirectory
            : Path.GetFullPath(configured);
    }

    private static object EnvironmentFingerprint()
        => new
        {
            os = Environment.OSVersion.VersionString,
            architecture = RuntimeInformation.OSArchitecture.ToString(),
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            framework = RuntimeInformation.FrameworkDescription,
            screen = new
            {
                width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN),
                height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN),
            },
        };

    private static object? DesktopEvidence(
        DesktopQualificationSnapshot? snapshot,
        DesktopQualificationSnapshot? restoredSnapshot,
        bool? topologyRestored)
    {
        if (snapshot == null)
            return null;
        return new
        {
            foreground = WindowEvidence(snapshot.Foreground),
            visibleTestWindows = snapshot.VisibleTestWindows.Select(WindowEvidence).ToArray(),
            monitors = snapshot.Monitors.Select(monitor => new
            {
                left = monitor.Left,
                top = monitor.Top,
                right = monitor.Right,
                bottom = monitor.Bottom,
                dpi = monitor.Dpi,
            }).ToArray(),
            virtualScreen = new
            {
                left = snapshot.VirtualLeft,
                top = snapshot.VirtualTop,
                width = snapshot.VirtualWidth,
                height = snapshot.VirtualHeight,
            },
            physicalTopology = snapshot.Topology is null ? null : TopologyEvidence(snapshot.Topology),
            restoration = new
            {
                verified = topologyRestored,
                physicalTopology = restoredSnapshot?.Topology is null
                    ? null
                    : TopologyEvidence(restoredSnapshot.Topology),
            },
            interactiveSessionAvailable = snapshot.InteractiveSessionAvailable,
            workstationLockedKnown = snapshot.WorkstationLockedKnown,
            workstationLocked = snapshot.WorkstationLocked,
            inputDesktop = snapshot.InputDesktop,
            tabDockCandidateIdentity = snapshot.TabDockCandidateIdentity,
            testRunnerIdentity = snapshot.TestRunnerIdentity,
        };
    }

    private static object? CapabilityEvidence(ScenarioCapabilitySnapshot? snapshot)
    {
        if (snapshot == null)
            return null;
        return new
        {
            chromeAvailable = snapshot.ChromeAvailable,
            edgeAvailable = snapshot.EdgeAvailable,
            braveAvailable = snapshot.BraveAvailable,
            firefoxAvailable = snapshot.FirefoxAvailable,
            windowsTerminalAvailable = snapshot.WindowsTerminalAvailable,
            notepadAvailable = snapshot.NotepadAvailable,
            notepadBrokerBehaviorDetectable = snapshot.NotepadBrokerBehaviorDetectable,
            monitorCount = snapshot.MonitorCount,
            multiMonitorAvailable = snapshot.MultiMonitorAvailable,
            mixedDpiAvailable = snapshot.MixedDpiAvailable,
            nonDefaultDpiAvailable = snapshot.NonDefaultDpiAvailable,
            negativeVirtualCoordinatesAvailable = snapshot.NegativeVirtualCoordinatesAvailable,
            interactiveSessionAvailable = snapshot.InteractiveSessionAvailable,
            workstationLockedKnown = snapshot.WorkstationLockedKnown,
            workstationLocked = snapshot.WorkstationLocked,
            sendInputAvailable = snapshot.SendInputAvailable,
            candidateSigningConfigured = snapshot.CandidateSigningConfigured,
            stageBAvailable = snapshot.StageBAvailable,
            topologyProbeFailure = snapshot.TopologyProbeFailure,
            physicalTopology = snapshot.Topology is null ? null : TopologyEvidence(snapshot.Topology),
        };
    }

    internal static object TopologyEvidence(PhysicalTopologySnapshot topology)
        => new
        {
            schemaVersion = topology.SchemaVersion,
            generation = topology.Generation,
            syntheticTopology = topology.SyntheticTopology,
            provenance = topology.Provenance.ToString().ToUpperInvariant(),
            observedUtc = topology.ObservedUtc,
            candidateSha = topology.CandidateSha,
            candidateExecutableSha = topology.CandidateExecutableSha,
            driverSha = topology.DriverSha,
            runId = topology.RunId,
            scenario = topology.Scenario,
            attempt = topology.Attempt,
            virtualScreen = new
            {
                left = topology.VirtualScreen.Left,
                top = topology.VirtualScreen.Top,
                right = topology.VirtualScreen.Right,
                bottom = topology.VirtualScreen.Bottom,
                width = topology.VirtualScreen.Width,
                height = topology.VirtualScreen.Height,
            },
            primaryMonitorId = topology.PrimaryMonitorId,
            snapshotId = topology.SnapshotId,
            monitors = topology.Monitors.Select(monitor => new
            {
                monitorId = monitor.MonitorId,
                bounds = new
                {
                    left = monitor.Bounds.Left,
                    top = monitor.Bounds.Top,
                    right = monitor.Bounds.Right,
                    bottom = monitor.Bounds.Bottom,
                    width = monitor.Bounds.Width,
                    height = monitor.Bounds.Height,
                },
                workArea = new
                {
                    left = monitor.WorkArea.Left,
                    top = monitor.WorkArea.Top,
                    right = monitor.WorkArea.Right,
                    bottom = monitor.WorkArea.Bottom,
                    width = monitor.WorkArea.Width,
                    height = monitor.WorkArea.Height,
                },
                isPrimary = monitor.IsPrimary,
                effectiveDpi = monitor.EffectiveDpi,
                scalePercent = monitor.ScalePercent,
                relativePlacement = monitor.RelativePlacement,
                taskbarDelta = new
                {
                    left = monitor.TaskbarDelta.Left,
                    top = monitor.TaskbarDelta.Top,
                    right = monitor.TaskbarDelta.Right,
                    bottom = monitor.TaskbarDelta.Bottom,
                },
            }).ToArray(),
        };

    internal static object TopologyBindingEvidence(VisualTopologyBinding binding)
        => new
        {
            snapshotId = binding.SnapshotId,
            syntheticTopology = binding.SyntheticTopology,
            provenance = binding.Provenance.ToString().ToUpperInvariant(),
            candidateSha = binding.CandidateSha,
            runId = binding.RunId,
            scenario = binding.Scenario,
            attempt = binding.Attempt,
            monitorId = binding.MonitorId,
            effectiveDpi = binding.EffectiveDpi,
            sourceMonitorId = binding.SourceMonitorId,
            sourceDpi = binding.SourceDpi,
            destinationMonitorId = binding.DestinationMonitorId,
            destinationDpi = binding.DestinationDpi,
        };

    private static object WindowEvidence(DesktopWindowObservation observation)
        => new
        {
            hwnd = observation.HwndCode,
            identity = observation.IdentityKey,
            ownership = observation.Ownership.ToString().ToUpperInvariant(),
            role = observation.Role,
            visible = observation.Visible,
            identityAvailable = observation.IdentityAvailable,
        };

    private static object[] VisibleGuests(Ctx ctx)
        => ctx.Guests.Select(g => new
        {
            role = g.Role,
            pid = g.Pid,
            hwnd = Hwnd(g.Hwnd),
            visible = g.Hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(g.Hwnd),
            alive = g.Proc is { HasExited: false },
        }).ToArray();

    private static object[] GuestGeometry(Ctx ctx)
    {
        var evidence = new List<object>();
        foreach (GuestInfo guest in ctx.Guests)
        {
            NativeMethods.RECT outer = default;
            NativeMethods.RECT client = default;
            bool hasOuter = guest.Hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(guest.Hwnd, out outer);
            bool hasClient = guest.Hwnd != IntPtr.Zero && NativeMethods.GetClientRect(guest.Hwnd, out client);
            int resizeEvidence = guest.IsPig ? PigLog.CountLines(guest.Pid, "CLIENT_PRESENT") : 0;
            evidence.Add(new
            {
                role = guest.Role,
                hwnd = Hwnd(guest.Hwnd),
                outer = hasOuter ? Rect(outer) : null,
                client = hasClient ? Rect(client) : null,
                resizeEvidence,
                visible = guest.Hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(guest.Hwnd),
            });
        }
        return evidence.ToArray();
    }

    private static void WriteJUnit(string root, string stem, Ctx ctx)
    {
        string path = Path.Combine(root, $"{stem}.junit.xml");
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using XmlWriter writer = XmlWriter.Create(path, settings);
        (int failures, int skipped) = ScenarioOutcomeContract.JUnitCounts(ctx.Outcome.Kind);
        writer.WriteStartElement("testsuite");
        writer.WriteAttributeString("name", "TabDock.SplitQualification");
        writer.WriteAttributeString("tests", "1");
        writer.WriteAttributeString("failures", failures.ToString());
        writer.WriteAttributeString("skipped", skipped.ToString());
        writer.WriteStartElement("testcase");
        writer.WriteAttributeString("classname", "TabDock.ValidationDriver");
        writer.WriteAttributeString("name", ctx.Name);
        if (failures != 0)
        {
            writer.WriteStartElement("failure");
            writer.WriteAttributeString("message", string.Join("; ", ctx.FailureReasons));
            writer.WriteEndElement();
        }
        else if (skipped != 0)
        {
            writer.WriteStartElement("skipped");
            writer.WriteAttributeString("message", string.Join("; ", ctx.FailureReasons));
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteDeterministicJUnit(
        string root,
        string stem,
        string suite,
        IReadOnlyList<AssertionEvidence> assertions,
        int failed)
    {
        string path = Path.Combine(root, $"{stem}.junit.xml");
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using XmlWriter writer = XmlWriter.Create(path, settings);
        writer.WriteStartElement("testsuite");
        writer.WriteAttributeString("name", "TabDock.SplitQualification.Deterministic");
        writer.WriteAttributeString("tests", assertions.Count.ToString());
        writer.WriteAttributeString("failures", failed.ToString());
        writer.WriteAttributeString("skipped", "0");
        foreach (AssertionEvidence assertion in assertions)
        {
            writer.WriteStartElement("testcase");
            writer.WriteAttributeString("classname", "TabDock.ValidationDriver.Deterministic");
            writer.WriteAttributeString("name", assertion.Name);
            if (!assertion.Passed)
            {
                writer.WriteStartElement("failure");
                writer.WriteAttributeString("message", "deterministic contract assertion failed");
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    internal static string CandidateSha()
    {
        string? fromCi = Environment.GetEnvironmentVariable("GITHUB_SHA");
        if (!string.IsNullOrWhiteSpace(fromCi))
            return fromCi;
        try
        {
            string? root = FindRepoRoot();
            if (root == null)
                return "unknown";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                ArgumentList = { "rev-parse", "HEAD" },
            });
            if (process == null)
                return "unknown";
            string value = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return value.Length == 40 ? value : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static void RegisterManifestEntry(ScenarioManifestEntry entry)
    {
        lock (ManifestSync)
            ManifestEntries.Add(entry);
    }

    private static ScenarioOutcomeKind ParseOutcome(string value)
    {
        foreach (ScenarioOutcomeKind kind in Enum.GetValues<ScenarioOutcomeKind>())
        {
            if (string.Equals(ScenarioOutcomeContract.Code(kind), value, StringComparison.Ordinal))
                return kind;
        }
        return ScenarioOutcomeKind.FailHarness;
    }

    private static string DriverIdentityPath()
    {
        string? configured = Environment.GetEnvironmentVariable("TABDOCK_VALIDATION_DRIVER_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);

        string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && Path.GetFileNameWithoutExtension(processPath).Contains("ValidationDriver", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(processPath);
        return assemblyPath;
    }

    private static string GitBranch()
    {
        try
        {
            string? root = FindRepoRoot();
            if (root == null)
                return "unknown";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                ArgumentList = { "branch", "--show-current" },
            });
            if (process == null)
                return "unknown";
            string value = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return string.IsNullOrWhiteSpace(value) ? "detached" : value;
        }
        catch
        {
            return "unknown";
        }
    }

    internal static string Sha256File(string? path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
                ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))
                : "unavailable";
        }
        catch
        {
            return "unavailable";
        }
    }

    internal static string DriverIdentitySha256()
        => Sha256File(DriverIdentityPath());

    private static string ApplicationVersion()
    {
        try
        {
            string? version = FileVersionInfo.GetVersionInfo(Scenarios.TabDockExe).ProductVersion;
            return string.IsNullOrWhiteSpace(version) ? "unknown" : version;
        }
        catch
        {
            return "unknown";
        }
    }

    private static string? FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TabDock.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static object Rect(NativeMethods.RECT rect)
        => new { left = rect.left, top = rect.top, width = rect.Width, height = rect.Height };

    private static string Hwnd(IntPtr hwnd)
        => hwnd == IntPtr.Zero ? "0x0" : $"0x{hwnd.ToInt64():X}";

    private static string SafeFileName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char ch in value)
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-');
        return builder.Length == 0 ? "scenario" : builder.ToString();
    }
}
