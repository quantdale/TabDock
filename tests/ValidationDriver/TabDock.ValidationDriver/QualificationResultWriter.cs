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
        DateTimeOffset EndedUtc);

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
            desktopQualification = DesktopEvidence(ctx.DesktopLease?.Snapshot),
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
            traceArtifacts = new[]
            {
                $"<validation-artifact>/{Path.GetFileName(TestRunProvenance.ArtifactDirectory)}",
                $"<validation-artifact>/{timelineName}",
            },
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
            ctx.FinishedUtc.Value));
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
                test = Sha256File(System.Reflection.Assembly.GetExecutingAssembly().Location),
            },
            driverIdentity = new
            {
                fileName = Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                sha256 = Sha256File(System.Reflection.Assembly.GetExecutingAssembly().Location),
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
        }

        return result.ToArray();

        void Add(string relativePath, string kind)
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

    private static object? DesktopEvidence(DesktopQualificationSnapshot? snapshot)
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
        };
    }

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
