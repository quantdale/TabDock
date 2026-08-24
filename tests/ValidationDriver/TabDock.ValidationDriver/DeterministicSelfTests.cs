using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TabDock.Models;
using TabDock.Services;

namespace TabDock.ValidationDriver;

/// <summary>
/// Tier-1 contracts. These tests deliberately stop at the logical transition
/// seam: native hide/show/foreground outcomes are explicit inputs, so a test
/// cannot accidentally report a native success that it never observed.
/// </summary>
internal static class DeterministicSelfTests
{
    public static int Run(string suite)
    {
        string normalized = string.IsNullOrWhiteSpace(suite) ? "all" : suite.ToLowerInvariant();
        var tests = new List<(string Id, Func<bool> Test)>();

        if (normalized is "all" or "split" or "deterministic")
            tests.AddRange(SplitTests());
        if (normalized is "all" or "identity" or "deterministic")
            tests.AddRange(IdentityTests());
        if (normalized is "all" or "outcome" or "deterministic")
            tests.AddRange(OutcomeTests());
        if (normalized is "all" or "wait" or "deterministic")
            tests.AddRange(WaitTests());
        if (normalized is "all" or "lease" or "capability" or "deterministic")
        {
            tests.AddRange(CapabilityTests());
            tests.AddRange(LeaseTests());
            tests.AddRange(TimelineTests());
        }
        if (normalized is "all" or "stress" or "deterministic")
            tests.AddRange(StressTests());
        if (tests.Count == 0)
        {
            Console.WriteLine($"Unknown self-test suite '{suite}'. Use split, identity, or all.");
            return 2;
        }

        int passed = 0;
        var evidence = new List<AssertionEvidence>();
        foreach ((string id, Func<bool> test) in tests)
        {
            bool ok;
            string? error = null;
            try
            {
                ok = test();
            }
            catch (Exception ex)
            {
                ok = false;
                error = ex.GetType().Name + ": " + ex.Message;
            }

            Console.WriteLine($"SELFTEST {(ok ? "PASS" : "FAIL")} {id}{(error == null ? string.Empty : $" error={error}")}");
            evidence.Add(new AssertionEvidence(id, ok));
            if (ok)
                passed++;
        }

        Console.WriteLine($"SELFTEST SUMMARY suite={normalized} passed={passed} failed={tests.Count - passed} total={tests.Count}");
        QualificationResultWriter.WriteDeterministic(normalized, evidence);
        QualificationResultWriter.WriteRunManifest();
        return ScenarioOutcomeContract.ExitCode(
            passed == tests.Count ? ScenarioOutcomeKind.Pass : ScenarioOutcomeKind.FailHarness);
    }

    private static IEnumerable<(string Id, Func<bool> Test)> SplitTests()
    {
        static SplitPresentationState Pair()
            => SplitPresentationPolicy.DefinePair("A", "B");

        static bool State(SplitPresentationState state, SplitPresentationMode mode, string? left, string? right, string? active)
            => state.Mode == mode
                && state.Left == left
                && state.Right == right
                && state.ActiveGuest == active;

        yield return ("S0-no-pair", () => State(SplitPresentationPolicy.NoPair(), SplitPresentationMode.None, null, null, null));
        yield return ("S0-define-pair-S1", () => State(Pair(), SplitPresentationMode.Pair, "A", "B", "A"));
        yield return ("S1-select-C-S2", () => State(SplitPresentationPolicy.SelectNonMember(Pair(), "C"), SplitPresentationMode.SingleGuest, "A", "B", "C"));
        yield return ("S2-select-D-S3", () =>
        {
            SplitPresentationState s = SplitPresentationPolicy.SelectNonMember(Pair(), "C");
            s = SplitPresentationPolicy.SelectNonMember(s, "D");
            return State(s, SplitPresentationMode.SingleGuest, "A", "B", "D");
        });
        yield return ("S2-select-A-S1", () =>
        {
            SplitPresentationState s = SplitPresentationPolicy.SelectNonMember(Pair(), "C");
            return State(SplitPresentationPolicy.SelectMember(s, "A"), SplitPresentationMode.Pair, "A", "B", "A");
        });
        yield return ("S2-select-B-S1", () =>
        {
            SplitPresentationState s = SplitPresentationPolicy.SelectNonMember(Pair(), "C");
            return State(SplitPresentationPolicy.SelectMember(s, "B"), SplitPresentationMode.Pair, "A", "B", "B");
        });
        yield return ("S3-select-A-or-B-S1", () =>
        {
            SplitPresentationState s = SplitPresentationPolicy.SelectNonMember(Pair(), "D");
            return State(SplitPresentationPolicy.SelectMember(s, "B"), SplitPresentationMode.Pair, "A", "B", "B");
        });
        yield return ("S1-explicit-exit-S0", () => State(SplitPresentationPolicy.ExplicitExit(Pair()), SplitPresentationMode.None, null, null, "A"));
        yield return ("S2-explicit-exit-retains-C", () =>
        {
            SplitPresentationState s = SplitPresentationPolicy.SelectNonMember(Pair(), "C");
            return State(SplitPresentationPolicy.ExplicitExit(s), SplitPresentationMode.None, null, null, "C");
        });
        yield return ("S3-explicit-exit-retains-D", () =>
        {
            SplitPresentationState s = SplitPresentationPolicy.SelectNonMember(Pair(), "D");
            return State(SplitPresentationPolicy.ExplicitExit(s), SplitPresentationMode.None, null, null, "D");
        });
        yield return ("S1-remove-left-survivor", () =>
        {
            SplitPresentationState s = SplitPresentationPolicy.RemoveMember(Pair(), "A");
            return State(s, SplitPresentationMode.None, null, null, "B");
        });
        yield return ("S1-remove-right-survivor", () =>
        {
            SplitPresentationState s = SplitPresentationPolicy.SelectMember(Pair(), "B");
            s = SplitPresentationPolicy.RemoveMember(s, "B");
            return State(s, SplitPresentationMode.None, null, null, "A");
        });
        yield return ("S2-remove-left-retains-C", () =>
        {
            SplitPresentationState s = SplitPresentationPolicy.SelectNonMember(Pair(), "C");
            return State(SplitPresentationPolicy.RemoveMember(s, "A"), SplitPresentationMode.None, null, null, "C");
        });
        yield return ("S2-remove-right-retains-C", () =>
        {
            SplitPresentationState s = SplitPresentationPolicy.SelectNonMember(Pair(), "C");
            return State(SplitPresentationPolicy.RemoveMember(s, "B"), SplitPresentationMode.None, null, null, "C");
        });
        yield return ("S3-remove-member-retains-D", () =>
        {
            SplitPresentationState s = SplitPresentationPolicy.SelectNonMember(Pair(), "D");
            return State(SplitPresentationPolicy.RemoveMember(s, "A"), SplitPresentationMode.None, null, null, "D");
        });
        yield return ("explicit-reconfigure-C-D", () =>
        {
            SplitPresentationState s = SplitPresentationPolicy.SelectNonMember(Pair(), "C");
            return State(SplitPresentationPolicy.Reconfigure(s, "C", "D"), SplitPresentationMode.Pair, "C", "D", "C");
        });
        yield return ("stale-settle-ignored-after-suspend", () =>
        {
            SplitPresentationState pair = Pair();
            SplitPresentationState dormant = SplitPresentationPolicy.SelectNonMember(pair, "C");
            return !SplitPresentationPolicy.IsCurrentSettle(dormant, pair.Generation)
                && !SplitPresentationPolicy.IsCurrentSettle(dormant, dormant.Generation - 1);
        });
        yield return ("current-settle-accepted-only-presented", () =>
        {
            SplitPresentationState pair = Pair();
            return SplitPresentationPolicy.IsCurrentSettle(pair, pair.Generation)
                && !SplitPresentationPolicy.IsCurrentSettle(pair, pair.Generation - 1);
        });
        yield return ("recovery-pending-suspend-retains-S1", () =>
        {
            SplitPresentationState pair = Pair();
            SplitPresentationState desired = SplitPresentationPolicy.SelectNonMember(pair, "C");
            return State(
                SplitPresentationPolicy.ResolveNativeTransition(pair, desired, SplitNativeTransitionOutcome.RecoveryPending),
                SplitPresentationMode.Pair, "A", "B", "A");
        });
        yield return ("recovery-pending-resume-retains-S2", () =>
        {
            SplitPresentationState dormant = SplitPresentationPolicy.SelectNonMember(Pair(), "C");
            SplitPresentationState desired = SplitPresentationPolicy.SelectMember(dormant, "A");
            return State(
                SplitPresentationPolicy.ResolveNativeTransition(dormant, desired, SplitNativeTransitionOutcome.RecoveryPending),
                SplitPresentationMode.SingleGuest, "A", "B", "C");
        });
        yield return ("identity-failure-does-not-apply-desired-state", () =>
        {
            SplitPresentationState pair = Pair();
            SplitPresentationState desired = SplitPresentationPolicy.SelectNonMember(pair, "C");
            SplitPresentationState result = SplitPresentationPolicy.ResolveNativeTransition(pair, desired, SplitNativeTransitionOutcome.IdentityMismatch);
            return result == pair;
        });
        yield return ("show-failure-does-not-apply-desired-state", () =>
        {
            SplitPresentationState pair = Pair();
            SplitPresentationState desired = SplitPresentationPolicy.SelectNonMember(pair, "C");
            SplitPresentationState result = SplitPresentationPolicy.ResolveNativeTransition(pair, desired, SplitNativeTransitionOutcome.ShowFailed);
            return result == pair;
        });
    }

    private static IEnumerable<(string Id, Func<bool> Test)> IdentityTests()
    {
        const string exe = @"C:\TestRun\TabDock.GuineaPig.exe";
        var expectedProcess = new TestRunProvenance.ProcessIdentity(100, 10, exe);
        var sameProcess = new TestRunProvenance.ProcessIdentity(100, 10, exe);
        var wrongStart = new TestRunProvenance.ProcessIdentity(100, 11, exe);
        var wrongExe = new TestRunProvenance.ProcessIdentity(100, 10, @"C:\Other\TabDock.GuineaPig.exe");
        WindowIdentity expectedWindow = Window(0x100, 100, 200, exe, 10);

        bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        bool Stable(WindowIdentity actual) => ProvenanceContract.WindowIdentityMatches(expectedWindow, actual, SamePath);
        bool Accept(bool processRegistered = true, bool processStartMatches = true,
            bool executableMatches = true, bool ancestryMatches = true,
            bool windowIdentityMatches = true, bool runMarkerMatches = true,
            bool registeredWindow = true, bool hasRegisteredOwner = false,
            string role = "GuineaPigA")
            => ProvenanceContract.AcceptWindowEvidence(
                processRegistered, processStartMatches, executableMatches, ancestryMatches,
                windowIdentityMatches, runMarkerMatches, registeredWindow,
                hasRegisteredOwner, role);

        yield return ("L01-test-window-process-accepted", () => ProvenanceContract.ProcessIdentityMatches(expectedProcess, sameProcess, SamePath));
        yield return ("L02-test-window-stable-identity-accepted", () => Stable(expectedWindow));
        yield return ("L03-recreated-hwnd-requires-fresh-registration", () => !Stable(Window(0x101, 100, 200, exe, 10)) && Accept(registeredWindow: false, hasRegisteredOwner: true, role: "TabDockContainer"));
        yield return ("L04-stale-old-hwnd-rejected", () => !Stable(Window(0x101, 100, 200, exe, 10)));
        yield return ("L05-same-pid-wrong-start-rejected", () => !ProvenanceContract.ProcessIdentityMatches(expectedProcess, wrongStart, SamePath));
        yield return ("L06-unrelated-process-rejected", () => !Accept(processRegistered: false));
        yield return ("L07-shell-root-rejected", () => !Accept(processRegistered: false, registeredWindow: false, role: "Shell"));
        yield return ("L08-overlay-rejected", () => !Accept(processRegistered: false, registeredWindow: false, role: "Overlay"));
        yield return ("L09-browser-descendant-accepted", () => Accept(registeredWindow: false, ancestryMatches: true, role: "BrowserEdge"));
        yield return ("L10-personal-browser-not-descendant-rejected", () => !Accept(registeredWindow: false, ancestryMatches: false, role: "BrowserEdge"));
        yield return ("L11-same-browser-executable-wrong-tree-rejected", () => !Accept(registeredWindow: false, ancestryMatches: false, role: "BrowserChrome"));
        yield return ("L12-missing-run-token-rejected", () => !Accept(runMarkerMatches: false));
        yield return ("L13-token-but-identity-mismatch-rejected", () => !Accept(windowIdentityMatches: false));
        yield return ("L14-recycled-hwnd-wrong-process-rejected", () => !Accept(processStartMatches: false, windowIdentityMatches: false));
        yield return ("L15-rejection-has-diagnostic-contract", () => IdentityDiagnostics.HasActionableReason("identity-mismatch"));
        yield return ("same-pid-wrong-executable-rejected", () => !ProvenanceContract.ProcessIdentityMatches(expectedProcess, wrongExe, SamePath));
        yield return ("L16-owned-process-cleanup-allowed", () =>
            ProvenanceContract.CleanupAllowed(RunOwnershipKind.OwnedProcess, processRegistered: true));
        yield return ("L17-unregistered-process-never-cleanup-owned", () =>
            !ProvenanceContract.CleanupAllowed(RunOwnershipKind.OwnedProcess, processRegistered: false));
        yield return ("L18-adopted-external-process-never-cleanup-owned", () =>
            !ProvenanceContract.CleanupAllowed(RunOwnershipKind.AdoptedExternalWindow, processRegistered: true));
        yield return ("L19-foreign-and-stale-never-cleanup-owned", () =>
            !ProvenanceContract.CleanupAllowed(RunOwnershipKind.Foreign, processRegistered: true)
            && !ProvenanceContract.CleanupAllowed(RunOwnershipKind.StaleRecycled, processRegistered: true));
        yield return ("L20-only-owned-or-adopted-windows-are-input-targets", () =>
            ProvenanceContract.InputAllowed(RunOwnershipKind.OwnedWindow)
            && ProvenanceContract.InputAllowed(RunOwnershipKind.AdoptedExternalWindow)
            && !ProvenanceContract.InputAllowed(RunOwnershipKind.OwnedProcess)
            && !ProvenanceContract.InputAllowed(RunOwnershipKind.Foreign)
            && !ProvenanceContract.InputAllowed(RunOwnershipKind.StaleRecycled));
        yield return ("L21-browser-descendant-dynamic-surface-admitted", () =>
            ProvenanceContract.DynamicWindowAllowed("BrowserChrome", hasRegisteredOwner: false));
        yield return ("L22-tabdock-popup-requires-registered-owner", () =>
            ProvenanceContract.DynamicWindowAllowed("TabDockContainer", hasRegisteredOwner: true)
            && !ProvenanceContract.DynamicWindowAllowed("TabDockContainer", hasRegisteredOwner: false));
        yield return ("L23-foreign-dynamic-surface-rejected", () =>
            !ProvenanceContract.DynamicWindowAllowed("ForeignOverlay", hasRegisteredOwner: true));
    }

    private static IEnumerable<(string Id, Func<bool> Test)> OutcomeTests()
    {
        ScenarioOutcomeKind[] kinds = Enum.GetValues<ScenarioOutcomeKind>();
        foreach (ScenarioOutcomeKind kind in kinds)
        {
            ScenarioOutcomeKind captured = kind;
            yield return ($"O01-code-{ScenarioOutcomeContract.Code(kind)}", () =>
                !string.IsNullOrWhiteSpace(ScenarioOutcomeContract.Code(captured))
                && ScenarioOutcomeContract.Code(captured) == ScenarioOutcomeContract.Code(captured).ToUpperInvariant());
            yield return ($"O02-junit-mapped-{ScenarioOutcomeContract.Code(kind)}", () =>
                ScenarioOutcomeContract.JUnitCounts(captured) is var counts
                && counts.Failures >= 0
                && counts.Skipped >= 0
                && counts.Failures + counts.Skipped <= 1);
        }

        yield return ("O03-only-pass-is-release-pass", () =>
            ScenarioOutcomeContract.IsReleasePass(ScenarioOutcomeKind.Pass)
            && !ScenarioOutcomeContract.IsReleasePass(ScenarioOutcomeKind.SkipCapability)
            && !ScenarioOutcomeContract.IsReleasePass(ScenarioOutcomeKind.BlockedEnvironment));
        yield return ("O04-product-failure-not-hidden-by-pass", () =>
            ScenarioOutcomeContract.Aggregate(new[]
            {
                new ScenarioOutcome(ScenarioOutcomeKind.FailProduct, "valid assertion"),
                ScenarioOutcome.Pass,
            }).Kind == ScenarioOutcomeKind.FailProduct);
        yield return ("O05-environment-blocker-not-product-failure", () =>
            ScenarioOutcomeContract.Aggregate(new[]
            {
                new ScenarioOutcome(ScenarioOutcomeKind.BlockedEnvironment, "foreign foreground"),
                ScenarioOutcome.Pass,
            }).Kind == ScenarioOutcomeKind.BlockedEnvironment);
        yield return ("O06-capability-skip-is-not-pass", () =>
            ScenarioOutcomeContract.Aggregate(new[]
            {
                new ScenarioOutcome(ScenarioOutcomeKind.SkipCapability, "browser absent"),
            }).Kind == ScenarioOutcomeKind.SkipCapability
            && ScenarioOutcomeContract.ExitCode(ScenarioOutcomeKind.SkipCapability) != 0);
        yield return ("O07-harness-failure-distinct", () =>
            ScenarioOutcomeContract.Code(ScenarioOutcomeKind.FailHarness) != ScenarioOutcomeContract.Code(ScenarioOutcomeKind.FailProduct));
        yield return ("O08-first-blocked-rerun-pass-retained", () =>
            new ScenarioAggregate("lease", new[]
            {
                new ScenarioAttempt("lease", 1, new ScenarioOutcome(ScenarioOutcomeKind.BlockedEnvironment, "foreign")),
                new ScenarioAttempt("lease", 2, ScenarioOutcome.Pass),
            }).FinalOutcome.Kind == ScenarioOutcomeKind.BlockedEnvironment);
        yield return ("O09-valid-failure-rerun-pass-is-flake", () =>
            new ScenarioAggregate("product", new[]
            {
                new ScenarioAttempt("product", 1, new ScenarioOutcome(ScenarioOutcomeKind.FailProduct, "assertion")),
                new ScenarioAttempt("product", 2, ScenarioOutcome.Pass),
            }).FinalOutcome.Kind == ScenarioOutcomeKind.FlakeUnclassified);
        yield return ("O10-two-valid-failures-remain-product", () =>
            new ScenarioAggregate("product", new[]
            {
                new ScenarioAttempt("product", 1, new ScenarioOutcome(ScenarioOutcomeKind.FailProduct, "first")),
                new ScenarioAttempt("product", 2, new ScenarioOutcome(ScenarioOutcomeKind.FailProduct, "second")),
            }).FinalOutcome.Kind == ScenarioOutcomeKind.FailProduct);
        yield return ("O11-aggregate-empty-is-pass", () =>
            ScenarioOutcomeContract.Aggregate(Array.Empty<ScenarioOutcome>()).Kind == ScenarioOutcomeKind.Pass);
        yield return ("O12-junit-failure-is-not-skipped", () =>
        {
            (int failures, int skipped) = ScenarioOutcomeContract.JUnitCounts(ScenarioOutcomeKind.FailHarness);
            return failures == 1 && skipped == 0;
        });
        yield return ("O13-junit-blocked-is-skipped", () =>
        {
            (int failures, int skipped) = ScenarioOutcomeContract.JUnitCounts(ScenarioOutcomeKind.BlockedSupervised);
            return failures == 0 && skipped == 1;
        });
        yield return ("O14-all-categories-unique", () =>
            kinds.Select(ScenarioOutcomeContract.Code).Distinct(StringComparer.Ordinal).Count() == kinds.Length);
        yield return ("O15-exit-code-round-trip", () =>
            kinds.All(kind => ScenarioOutcomeContract.FromExitCode(ScenarioOutcomeContract.ExitCode(kind)) == kind));
    }

    private static WindowIdentity Window(IntPtr hwnd, uint pid, uint tid, string exe, long start)
        => new(hwnd, pid, tid, "TestWindowClass", "TDTEST:fixture", exe, start);

    private static IEnumerable<(string Id, Func<bool> Test)> WaitTests()
    {
        yield return ("W01-success-has-monotonic-observation", () =>
        {
            long ticks = 0;
            int attempts = 0;
            ScenarioWaitResult result = ScenarioWait.Until(
                () => ++attempts >= 3,
                timeoutMilliseconds: 100,
                pollMilliseconds: 10,
                describe: () => $"attempt={attempts}",
                onTimeout: null,
                timestamp: () => ticks,
                delay: milliseconds => ticks += milliseconds * Stopwatch.Frequency / 1000,
                honorCancellation: false);
            return result.Succeeded && result.Iterations == 3 && result.LastObserved == "attempt=3";
        });
        yield return ("W02-timeout-retains-last-state", () =>
        {
            long ticks = 0;
            int attempts = 0;
            ScenarioWaitResult result = ScenarioWait.Until(
                () => ++attempts >= 99,
                timeoutMilliseconds: 25,
                pollMilliseconds: 10,
                describe: () => $"attempt={attempts}",
                onTimeout: null,
                timestamp: () => ticks,
                delay: milliseconds => ticks += milliseconds * Stopwatch.Frequency / 1000,
                honorCancellation: false);
            return result.TimedOut && !result.Succeeded && result.LastObserved == "attempt=4";
        });
        yield return ("W03-describe-failure-is-bounded", () =>
        {
            long ticks = 0;
            ScenarioWaitResult result = ScenarioWait.Until(
                () => false,
                timeoutMilliseconds: 1,
                pollMilliseconds: 1,
                describe: () => throw new InvalidOperationException("fixture"),
                onTimeout: null,
                timestamp: () => ticks,
                delay: milliseconds => ticks += milliseconds * Stopwatch.Frequency / 1000,
                honorCancellation: false);
            return result.TimedOut && result.LastObserved == "describe-error:InvalidOperationException";
        });
    }

    private static IEnumerable<(string Id, Func<bool> Test)> CapabilityTests()
    {
        ScenarioCapabilitySnapshot complete = new(
            ChromeAvailable: true,
            EdgeAvailable: true,
            BraveAvailable: true,
            FirefoxAvailable: true,
            WindowsTerminalAvailable: true,
            NotepadAvailable: true,
            NotepadBrokerBehaviorDetectable: true,
            MonitorCount: 2,
            MixedDpiAvailable: true,
            NonDefaultDpiAvailable: true,
            NegativeVirtualCoordinatesAvailable: true,
            InteractiveSessionAvailable: true,
            WorkstationLockedKnown: true,
            WorkstationLocked: false,
            SendInputAvailable: true,
            CandidateSigningConfigured: true,
            StageBAvailable: true);

        yield return ("C01-complete-browser-runnable", () =>
            ScenarioCapabilities.Resolve(
                new ScenarioDescriptor("browser-lifecycle", "chrome-normal"), complete).Runnable);
        yield return ("C02-browser-absence-is-capability-skip", () =>
        {
            ScenarioCapabilitySnapshot absent = complete with { ChromeAvailable = false };
            ScenarioCapabilityResolution result = ScenarioCapabilities.Resolve(
                new ScenarioDescriptor("browser-lifecycle", "chrome-normal"), absent);
            return !result.Runnable && result.Outcome == ScenarioOutcomeKind.SkipCapability;
        });
        yield return ("C03-topology-absence-is-environment-block", () =>
        {
            ScenarioCapabilityResolution result = ScenarioCapabilities.Resolve(
                new ScenarioDescriptor("dpi-multi-monitor", RequiresMultiMonitor: true),
                complete with { MonitorCount = 1 });
            return !result.Runnable && result.Outcome == ScenarioOutcomeKind.BlockedEnvironment;
        });
        yield return ("C04-signing-absence-is-capability-block", () =>
        {
            ScenarioCapabilityResolution result = ScenarioCapabilities.Resolve(
                new ScenarioDescriptor("candidate-signing", RequiresSigning: true),
                complete with { CandidateSigningConfigured = false });
            return !result.Runnable && result.Outcome == ScenarioOutcomeKind.BlockedCapability;
        });
        yield return ("C05-lock-is-environment-block", () =>
        {
            ScenarioCapabilityResolution result = ScenarioCapabilities.Resolve(
                new ScenarioDescriptor("physical", RequiresInteractiveSession: true),
                complete with { WorkstationLocked = true });
            return !result.Runnable && result.Outcome == ScenarioOutcomeKind.BlockedEnvironment;
        });
        yield return ("C06-direct-chrome-scenario-is-preflighted", () =>
        {
            ScenarioDescriptor descriptor = ScenarioCapabilities.Describe(
                "chromeinput", new Options { Guest = "pig" });
            ScenarioCapabilityResolution result = ScenarioCapabilities.Resolve(
                descriptor, complete with { ChromeAvailable = false });
            return descriptor.RequiredBrowser == "chrome-normal"
                && !result.Runnable
                && result.Outcome == ScenarioOutcomeKind.SkipCapability;
        });
        yield return ("C07-browser-multi-requires-both-browsers", () =>
        {
            ScenarioDescriptor descriptor = ScenarioCapabilities.Describe(
                "browser-multi", new Options { Guest = "chrome-normal" });
            ScenarioCapabilityResolution result = ScenarioCapabilities.Resolve(
                descriptor, complete with { EdgeAvailable = false });
            return descriptor.RequiredBrowser == "chrome-and-edge"
                && !result.Runnable
                && result.Outcome == ScenarioOutcomeKind.SkipCapability;
        });
        yield return ("C08-notepad-broker-is-capability-preflighted", () =>
        {
            ScenarioDescriptor descriptor = ScenarioCapabilities.Describe(
                "keyboardinput-notepad", new Options());
            ScenarioCapabilityResolution result = ScenarioCapabilities.Resolve(
                descriptor, complete with { NotepadBrokerBehaviorDetectable = false });
            return descriptor.RequiredApplication == "notepad-broker"
                && !result.Runnable
                && result.Outcome == ScenarioOutcomeKind.SkipCapability;
        });
    }

    private static IEnumerable<(string Id, Func<bool> Test)> LeaseTests()
    {
        static DesktopWindowObservation Window(
            IntPtr hwnd,
            RunOwnershipKind ownership,
            string role = "TabDockContainer",
            string key = "fixture-window")
            => new(hwnd, key, ownership, role, true, true);

        DesktopQualificationSnapshot Snapshot(bool locked = false)
            => new(
                Foreground: Window(new IntPtr(0x100), RunOwnershipKind.OwnedWindow),
                VisibleTestWindows: new[] { Window(new IntPtr(0x100), RunOwnershipKind.OwnedWindow) },
                Monitors: new[] { new DesktopMonitorObservation(0, 0, 1920, 1080, 96) },
                VirtualLeft: 0,
                VirtualTop: 0,
                VirtualWidth: 1920,
                VirtualHeight: 1080,
                InteractiveSessionAvailable: true,
                WorkstationLockedKnown: true,
                WorkstationLocked: locked,
                InputDesktop: "fixture",
                TabDockCandidateIdentity: "not-started",
                TestRunnerIdentity: "pid=fixture");

        yield return ("LSE01-active-lease-accepts-owned-point", () =>
        {
            var probe = new FakeDesktopProbe(Snapshot());
            probe.Points.Enqueue(Window(new IntPtr(0x100), RunOwnershipKind.OwnedWindow));
            var lease = new DesktopQualificationLease(probe, new NativeInteractionTimeline());
            lease.Start();
            DesktopLeaseCheckpoint checkpoint = lease.Checkpoint("click", x: 10, y: 10);
            return lease.IsValid && checkpoint.IsValid;
        });
        yield return ("LSE02-foreign-point-invalidates-environment", () =>
        {
            var probe = new FakeDesktopProbe(Snapshot());
            probe.Points.Enqueue(Window(new IntPtr(0x200), RunOwnershipKind.Foreign, "ForeignOverlay", "foreign"));
            var lease = new DesktopQualificationLease(probe, new NativeInteractionTimeline());
            lease.Start();
            DesktopLeaseCheckpoint checkpoint = lease.Checkpoint("click", x: 10, y: 10);
            return !lease.IsValid && checkpoint.Kind == DesktopLeaseCheckpointKind.ForeignCoverage;
        });
        yield return ("LSE03-adopted-external-remains-input-eligible", () =>
        {
            var probe = new FakeDesktopProbe(Snapshot());
            probe.Points.Enqueue(Window(new IntPtr(0x300), RunOwnershipKind.AdoptedExternalWindow, "External.Notepad", "adopted"));
            var lease = new DesktopQualificationLease(probe, new NativeInteractionTimeline());
            lease.Start();
            DesktopLeaseCheckpoint checkpoint = lease.Checkpoint("click", x: 10, y: 10);
            return lease.IsValid && checkpoint.IsValid;
        });
        yield return ("LSE04-locked-start-is-fail-closed", () =>
        {
            var lease = new DesktopQualificationLease(new FakeDesktopProbe(Snapshot(locked: true)), new NativeInteractionTimeline());
            lease.Start();
            return !lease.IsValid && lease.State == DesktopLeaseState.Invalidated;
        });
        yield return ("LSE05-identity-recycle-invalidates", () =>
        {
            var probe = new FakeDesktopProbe(Snapshot());
            probe.Points.Enqueue(Window(new IntPtr(0x100), RunOwnershipKind.StaleRecycled, "Unknown", "recycled"));
            var lease = new DesktopQualificationLease(probe, new NativeInteractionTimeline());
            lease.Start();
            DesktopLeaseCheckpoint checkpoint = lease.Checkpoint("click", x: 10, y: 10);
            return !lease.IsValid && checkpoint.Kind == DesktopLeaseCheckpointKind.IdentityChanged;
        });
    }

    private static IEnumerable<(string Id, Func<bool> Test)> TimelineTests()
    {
        yield return ("T01-timeline-is-bounded-and-ordered", () =>
        {
            var timeline = new NativeInteractionTimeline(16);
            for (int i = 0; i < 32; i++)
            {
                timeline.Record("fixture", data: new Dictionary<string, string>
                {
                    ["index"] = i.ToString(),
                    ["title"] = "private title must not persist",
                });
            }
            IReadOnlyList<NativeInteractionEvent> events = timeline.Snapshot();
            return events.Count == 16
                && events[0].Sequence == 17
                && events[^1].Sequence == 32
                && events[0].Data["title"] == "<redacted>";
        });
        yield return ("T02-timeline-role-and-hwnd-are-safe", () =>
        {
            var timeline = new NativeInteractionTimeline();
            timeline.Record("checkpoint", "TabDockContainer", new IntPtr(0x123));
            NativeInteractionEvent item = timeline.Snapshot().Single();
            return item.Role == "TabDockContainer" && item.Hwnd == "0x123";
        });
    }

    private static IEnumerable<(string Id, Func<bool> Test)> StressTests()
    {
        const int replaySeed = 0x5EED2026;
        yield return ($"S01-replay-determinism-seed-{replaySeed:X}", () =>
        {
            var random = new Random(replaySeed);
            for (int caseIndex = 0; caseIndex < 128; caseIndex++)
            {
                var events = new List<NativeInteractionReplayEvent>();
                for (int eventIndex = 0; eventIndex < 32; eventIndex++)
                {
                    string identity = new[] { "A", "B", "C", "foreign" }[random.Next(4)];
                    NativeReplayEventKind kind = (NativeReplayEventKind)random.Next(
                        Enum.GetValues<NativeReplayEventKind>().Length);
                    NativeReplayIdentityResult identityResult = random.Next(10) == 0
                        ? NativeReplayIdentityResult.Mismatch
                        : NativeReplayIdentityResult.Match;
                    events.Add(new NativeInteractionReplayEvent(kind, identity, identityResult));
                }

                var replay = new NativeInteractionReplayCase(new[] { "A", "B", "C" }, "A", events);
                NativeInteractionReplayResult first = NativeInteractionReplay.Run(replay);
                NativeInteractionReplayResult second = NativeInteractionReplay.Run(replay);
                if (!string.Equals(first.Foreground, second.Foreground, StringComparison.Ordinal)
                    || !first.Captured.SequenceEqual(second.Captured, StringComparer.Ordinal)
                    || !first.Intents.SequenceEqual(second.Intents)
                    || !first.Refusals.SequenceEqual(second.Refusals)
                    || first.Captured.Any(identity => identity == "foreign"))
                {
                    throw new InvalidOperationException($"replay divergence at case={caseIndex} seed={replaySeed}");
                }
            }
            return true;
        });

        const int splitSeed = 0x51_17_2026;
        yield return ($"S02-split-model-invariants-seed-{splitSeed:X}", () =>
        {
            var random = new Random(splitSeed);
            for (int run = 0; run < 64; run++)
            {
                SplitPresentationState state = SplitPresentationPolicy.NoPair();
                for (int step = 0; step < 64; step++)
                {
                    string target = new[] { "A", "B", "C", "D" }[random.Next(4)];
                    switch (random.Next(5))
                    {
                        case 0 when state.Mode == SplitPresentationMode.None:
                            state = SplitPresentationPolicy.DefinePair("A", "B");
                            break;
                        case 1:
                            state = SplitPresentationPolicy.SelectNonMember(state, target);
                            break;
                        case 2:
                            state = SplitPresentationPolicy.SelectMember(state, target);
                            break;
                        case 3:
                            state = SplitPresentationPolicy.RemoveMember(state, target);
                            break;
                        default:
                            state = SplitPresentationPolicy.ExplicitExit(state);
                            break;
                    }

                    bool valid = state.Mode != SplitPresentationMode.Pair
                        || (state.Left != null
                            && state.Right != null
                            && !string.Equals(state.Left, state.Right, StringComparison.Ordinal));
                    valid &= state.Mode != SplitPresentationMode.SingleGuest || state.ActiveGuest != null;
                    if (!valid)
                        throw new InvalidOperationException($"split invariant at run={run} step={step} seed={splitSeed}");
                }
            }
            return true;
        });

        yield return ($"S03-identity-matrix-seed-20260824", () =>
        {
            const string exe = "TabDock.GuineaPig.exe";
            var random = new Random(20260824);
            var expected = new TestRunProvenance.ProcessIdentity(42, 9001, exe);
            for (int i = 0; i < 512; i++)
            {
                var actual = new TestRunProvenance.ProcessIdentity(
                    random.Next(0, 3) == 0 ? 42u : (uint)random.Next(1, 1000),
                    random.Next(0, 4) == 0 ? 9001L : random.NextInt64(1, 20000),
                    random.Next(0, 5) == 0 ? exe : "foreign.exe");
                bool expectedMatch = actual.ProcessId == expected.ProcessId
                    && actual.ProcessStartTimeUtcTicks == expected.ProcessStartTimeUtcTicks
                    && string.Equals(actual.ExePath, expected.ExePath, StringComparison.OrdinalIgnoreCase);
                bool actualMatch = ProvenanceContract.ProcessIdentityMatches(
                    expected,
                    actual,
                    (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase));
                if (expectedMatch != actualMatch)
                    throw new InvalidOperationException($"identity mismatch at index={i} seed=20260824");
            }
            return true;
        });
    }

    private sealed class FakeDesktopProbe : IDesktopQualificationProbe
    {
        private readonly DesktopQualificationSnapshot _snapshot;

        public FakeDesktopProbe(DesktopQualificationSnapshot snapshot)
        {
            _snapshot = snapshot;
            Foregrounds.Enqueue(snapshot.Foreground);
        }

        public Queue<DesktopWindowObservation> Foregrounds { get; } = new();
        public Queue<DesktopWindowObservation> Points { get; } = new();

        public DesktopQualificationSnapshot Capture() => _snapshot;

        public DesktopWindowObservation ObserveForeground()
            => Foregrounds.Count == 0 ? _snapshot.Foreground : Foregrounds.Dequeue();

        public DesktopWindowObservation ObservePoint(int x, int y)
            => Points.Count == 0 ? _snapshot.Foreground : Points.Dequeue();
    }
}
