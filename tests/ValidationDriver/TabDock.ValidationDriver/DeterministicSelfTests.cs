using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
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
            tests.AddRange(CatalogTests());
            tests.AddRange(CapabilityTests());
            tests.AddRange(LeaseTests());
            tests.AddRange(TimelineTests());
        }
        if (normalized is "all" or "manifest" or "visual" or "deterministic")
        {
            tests.AddRange(ManifestTests());
            tests.AddRange(VisualTests());
        }
        if (normalized is "all" or "topology" or "deterministic")
            tests.AddRange(TopologyTests());
        if (normalized is "all" or "stress" or "deterministic")
            tests.AddRange(StressTests());
        if (normalized is "all" or "foreground" or "deterministic")
            tests.AddRange(ForegroundTests());
        if (normalized is "all" or "resource" or "deterministic")
            tests.AddRange(ResourceTests());
        if (tests.Count == 0)
        {
            Console.WriteLine($"Unknown self-test suite '{suite}'. Use split, identity, foreground, manifest, visual, resource, or all.");
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
        if (normalized is "all" or "topology" or "deterministic")
            QualificationResultWriter.WriteTopologyLab(VirtualTopologyLab.Run());
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

    private static IEnumerable<(string Id, Func<bool> Test)> ForegroundTests()
    {
        WindowIdentity target = Window(new IntPtr(0x710), 710, 71, @"C:\TabDock.ValidationDriver.exe", 7100);

        yield return ("FG01-setforeground-success-no-click", () =>
        {
            var runtime = new ForegroundTestRuntime
            {
                ArrangeResult = true,
                TargetForeground = true,
            };
            ForegroundQualificationResult result = new ForegroundQualification(runtime).Qualify(target);
            return result.IsValid
                && result.Kind == ForegroundQualificationKind.AlreadyForeground
                && runtime.ArrangeCount == 1
                && runtime.ClickCount == 0;
        });

        yield return ("FG02-arrangement-fails-owned-point-clicks-once", () =>
        {
            var runtime = new ForegroundTestRuntime
            {
                ArrangeResult = false,
                ClickSetsForeground = true,
            };
            ForegroundQualificationResult result = new ForegroundQualification(runtime).Qualify(target);
            return result.IsValid
                && result.UsedActivationClick
                && runtime.ArrangeCount == 1
                && runtime.PointVerificationCount == 1
                && runtime.ClickCount == 1
                && runtime.ForegroundProofCount == 1;
        });

        yield return ("FG03-foreign-activation-point-zero-click", () =>
        {
            var runtime = new ForegroundTestRuntime
            {
                ArrangeResult = false,
                PointVerified = false,
            };
            ForegroundQualificationResult result = new ForegroundQualification(runtime).Qualify(target);
            return !result.IsValid && runtime.ClickCount == 0;
        });

        yield return ("FG04-stale-target-zero-input", () =>
        {
            var runtime = new ForegroundTestRuntime
            {
                TargetCurrent = false,
            };
            ForegroundQualificationResult result = new ForegroundQualification(runtime).Qualify(target);
            return !result.IsValid
                && runtime.ArrangeCount == 0
                && runtime.ClickCount == 0;
        });

        yield return ("FG05-activation-click-wrong-foreground", () =>
        {
            var runtime = new ForegroundTestRuntime
            {
                ArrangeResult = false,
                ClickSetsForeground = false,
            };
            ForegroundQualificationResult result = new ForegroundQualification(runtime).Qualify(target);
            return !result.IsValid
                && runtime.ClickCount == 1
                && runtime.ForegroundProofCount == 1;
        });

        yield return ("FG06-lease-invalidates-preactivation", () =>
        {
            var runtime = new ForegroundTestRuntime
            {
                ArrangeResult = false,
                InvalidateLeaseOnPointVerification = true,
            };
            ForegroundQualificationResult result = new ForegroundQualification(runtime).Qualify(target);
            return !result.IsValid
                && !runtime.LeaseIsActive
                && runtime.ClickCount == 0;
        });

        yield return ("FG07-target-disappears-before-arrangement", () =>
        {
            var runtime = new ForegroundTestRuntime
            {
                TargetCurrent = false,
            };
            ForegroundQualificationResult result = new ForegroundQualification(runtime).Qualify(target);
            return result.Reason == "target-identity-not-current"
                && runtime.ClickCount == 0;
        });

        yield return ("FG08-no-safe-point-zero-input", () =>
        {
            var runtime = new ForegroundTestRuntime
            {
                ArrangeResult = false,
                SafePointAvailable = false,
            };
            ForegroundQualificationResult result = new ForegroundQualification(runtime).Qualify(target);
            return !result.IsValid
                && result.Reason == "no-safe-activation-point"
                && runtime.ClickCount == 0;
        });

        yield return ("FG09-success-bounded-arrangement-and-input", () =>
        {
            var runtime = new ForegroundTestRuntime
            {
                ArrangeResult = false,
                ClickSetsForeground = true,
            };
            ForegroundQualificationResult result = new ForegroundQualification(runtime).Qualify(target);
            return result.IsValid
                && runtime.ArrangeCount <= 1
                && runtime.ClickCount <= 1
                && runtime.PointVerificationCount == 1;
        });

        yield return ("FG10-failure-zero-input", () =>
        {
            var runtime = new ForegroundTestRuntime
            {
                ArrangeResult = false,
                SafePointAvailable = false,
            };
            ForegroundQualificationResult result = new ForegroundQualification(runtime).Qualify(target);
            return !result.IsValid && runtime.ClickCount == 0;
        });
    }

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

    private static IEnumerable<(string Id, Func<bool> Test)> CatalogTests()
    {
        yield return ("CAT01-catalog-generation-is-stable", () =>
            ScenarioCatalog.Generation == "scenario-catalog-2026-09-01-v2"
            && ScenarioCatalog.All.Count == 135);

        yield return ("CAT02-catalog-validates-without-errors", () =>
        {
            ScenarioCatalog.Validate();
            return true;
        });

        yield return ("CAT03-every-entry-resolves-a-handler", () =>
            ScenarioCatalog.All.All(item => ScenarioCatalog.TryResolve(item.Id, out _, out _)));

        yield return ("CAT04-all-order-is-a-catalog-projection", () =>
            ScenarioCatalog.AllOrder.SequenceEqual(
                ScenarioCatalog.All.Where(item => item.IncludeInAll).Select(item => item.Id)));

        yield return ("CAT05-explicit-browser-and-user-app-are-out-of-all", () =>
            ScenarioCatalog.All
                .Where(item => item.ExecutionClass is ScenarioExecutionClass.Browser or ScenarioExecutionClass.UserOwnedApplication)
                .All(item => !item.IncludeInAll && ScenarioCatalog.ExplicitOnlyShardNames.Contains(item.Shard)));

        yield return ("CAT06-duplicate-ID-is-rejected", () =>
        {
            ScenarioDefinition first = ScenarioCatalog.All[0];
            IReadOnlyList<string> errors = ScenarioCatalog.ValidateDefinitions(
                new[] { first, first },
                ScenarioCatalog.AllShards);
            return errors.Any(error => error.Contains("duplicate scenario IDs", StringComparison.Ordinal));
        });

        yield return ("CAT07-unknown-shard-is-rejected", () =>
        {
            ScenarioDefinition invalid = ScenarioCatalog.All[0] with { Shard = "missing-shard" };
            IReadOnlyList<string> errors = ScenarioCatalog.ValidateDefinitions(
                new[] { invalid },
                ScenarioCatalog.AllShards);
            return errors.Any(error => error.Contains("unknown shard", StringComparison.Ordinal));
        });

        yield return ("CAT08-budget-overflow-is-rejected", () =>
        {
            ScenarioDefinition invalid = ScenarioCatalog.All[0] with { Shard = "tiny-shard", ExpectedRuntimeSeconds = 60 };
            var shard = new ScenarioShardDefinition("tiny-shard", true, false, 1, 1);
            IReadOnlyList<string> errors = ScenarioCatalog.ValidateDefinitions(new[] { invalid }, new[] { shard });
            return errors.Any(error => error.Contains("runtime budget", StringComparison.Ordinal));
        });

        yield return ("CAT09-shard-membership-is-disjoint", () =>
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ScenarioShardDefinition shard in ScenarioCatalog.AllShards)
            {
                foreach (string scenario in ScenarioCatalog.GetShardScenarios(shard.Name))
                {
                    if (!seen.Add(shard.Name + "\u0000" + scenario))
                        return false;
                }
            }
            return ScenarioCatalog.All.All(item => seen.Contains(item.Shard + "\u0000" + item.Id));
        });

        yield return ("CAT10-catalog-entry-preserves-release-policy-fields", () =>
        {
            ScenarioDefinition browser = ScenarioCatalog.All.Single(item => item.Id == "browser-multi");
            ScenarioDefinition dpi = ScenarioCatalog.All.Single(item => item.Id == "capture-dpi-unaware-guest");
            return !browser.MayContributeReleaseEvidence
                && browser.RequiredBrowsers.Contains("chrome-and-edge")
                && dpi.RequiresNonDefaultDpi
                && dpi.RequiresSupervision
                && dpi.DestructiveState == ScenarioDestructiveState.TestOwnedMutation;
        });

        yield return ("CAT11-explicit-candidate-does-not-require-repo-marker", () =>
        {
            string candidate = Path.Combine(Path.GetTempPath(), "tabdock-explicit-candidate.exe");
            string previousCandidate = Scenarios.TabDockExe;
            string previousPig = Scenarios.PigExe;
            try
            {
                Scenarios.ConfigureArtifacts("Release", "none", candidate, null);
                return string.Equals(Scenarios.TabDockExe, Path.GetFullPath(candidate), StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrEmpty(Scenarios.PigExe);
            }
            finally
            {
                Scenarios.ConfigureArtifacts(
                    Scenarios.SelectedConfiguration,
                    Scenarios.SelectedRid,
                    previousCandidate,
                    string.IsNullOrEmpty(previousPig) ? null : previousPig);
            }
        });
    }

    private static IEnumerable<(string Id, Func<bool> Test)> ManifestTests()
    {
        yield return ("MAN01-valid-hierarchy-aggregates-all-shards", () =>
        {
            using var fixture = new ManifestFixture();
            var imports = new List<ChildManifestVerification>();
            foreach (string shard in ScenarioCatalog.OrchestratedShardNames)
                imports.Add(fixture.WriteAndImport(shard));

            ParentManifestWriteResult parent = fixture.WriteParent(imports);
            return parent.Outcome.Kind == ScenarioOutcomeKind.Pass
                && parent.Errors.Count == 0
                && QualificationManifestVerifier.VerifyParent(parent.ManifestPath, out IReadOnlyList<string> errors)
                && errors.Count == 0;
        });

        yield return ("MAN02-missing-child-manifest-is-fail-harness", () =>
        {
            using var fixture = new ManifestFixture();
            ChildManifestVerification result = fixture.ImportMissing("startup", 0);
            return !result.Valid
                && result.Outcome.Kind == ScenarioOutcomeKind.FailHarness
                && result.FailureReason?.Contains("missing", StringComparison.OrdinalIgnoreCase) == true;
        });

        yield return ("MAN03-child-exit-disagreement-is-rejected", () =>
        {
            using var fixture = new ManifestFixture();
            fixture.WriteChild("startup");
            ChildManifestVerification result = fixture.ImportExisting(
                "startup",
                ScenarioOutcomeContract.ExitCode(ScenarioOutcomeKind.FailHarness));
            return !result.Valid
                && result.FailureReason?.Contains("exit code", StringComparison.OrdinalIgnoreCase) == true;
        });

        yield return ("MAN04-candidate-and-shard-mismatch-is-rejected", () =>
        {
            using var fixture = new ManifestFixture();
            fixture.WriteChild("startup", manifestShard: "split-core", candidateSha: new string('C', 40));
            ChildManifestVerification result = fixture.ImportExisting("startup", 0);
            return !result.Valid
                && result.FailureReason?.Contains("candidate", StringComparison.OrdinalIgnoreCase) == true
                && result.FailureReason.Contains("shard", StringComparison.OrdinalIgnoreCase);
        });

        yield return ("MAN05-modified-artifact-is-rejected", () =>
        {
            using var fixture = new ManifestFixture();
            string manifest = fixture.WriteChild("startup");
            fixture.TamperFirstArtifact("startup");
            ChildManifestVerification result = fixture.ImportExisting("startup", 0);
            return !result.Valid
                && result.ManifestPath == manifest
                && result.FailureReason?.Contains("hash/existence mismatch", StringComparison.OrdinalIgnoreCase) == true;
        });

        yield return ("MAN06-duplicate-json-property-is-rejected", () =>
        {
            using var fixture = new ManifestFixture();
            fixture.WriteChild("startup", duplicateRunKind: true);
            ChildManifestVerification result = fixture.ImportExisting("startup", 0);
            return !result.Valid
                && result.FailureReason?.Contains("duplicate JSON property", StringComparison.Ordinal) == true;
        });

        yield return ("MAN07-path-traversal-artifact-is-rejected", () =>
        {
            using var fixture = new ManifestFixture();
            fixture.WriteChild("startup", pathTraversal: true);
            ChildManifestVerification result = fixture.ImportExisting("startup", 0);
            return !result.Valid
                && result.FailureReason?.Contains("artifact reference invalid", StringComparison.OrdinalIgnoreCase) == true;
        });

        yield return ("MAN08-parent-duplicate-scenario-ownership-is-rejected", () =>
        {
            using var fixture = new ManifestFixture();
            fixture.WriteChild("startup");
            ChildManifestVerification first = fixture.ImportExisting("startup", 0);
            ChildManifestVerification duplicate = first with
            {
                ExpectedShard = "split-core",
                ChildRelativeDirectory = "children/split-core/duplicate-run",
            };
            ParentManifestWriteResult parent = fixture.WriteParent(
                new[] { first, duplicate },
                new[] { "startup", "split-core" });
            return parent.Outcome.Kind == ScenarioOutcomeKind.FailHarness
                && parent.Errors.Any(error => error.Contains("appears in shards", StringComparison.Ordinal));
        });

        yield return ("MAN09-partial-all-run-is-never-pass", () =>
        {
            using var fixture = new ManifestFixture();
            ChildManifestVerification missing = fixture.ImportMissing("capture-group", 1);
            ParentManifestWriteResult parent = fixture.WriteParent(
                new[] { missing },
                ScenarioCatalog.OrchestratedShardNames);
            return parent.Outcome.Kind == ScenarioOutcomeKind.FailHarness
                && parent.Errors.Count > 0;
        });

        yield return ("MAN10-stale-schema-is-rejected", () =>
        {
            using var fixture = new ManifestFixture();
            fixture.WriteChild("startup", schemaVersion: 1);
            ChildManifestVerification result = fixture.ImportExisting("startup", 0);
            return !result.Valid
                && result.FailureReason?.Contains("schemaVersion", StringComparison.Ordinal) == true;
        });

        yield return ("MAN11-parent-reverification-catches-late-tamper", () =>
        {
            using var fixture = new ManifestFixture();
            var imports = ScenarioCatalog.OrchestratedShardNames
                .Select(fixture.WriteAndImport)
                .ToList();
            ParentManifestWriteResult parent = fixture.WriteParent(imports);
            fixture.TamperFirstArtifact("startup");
            return !QualificationManifestVerifier.VerifyParent(parent.ManifestPath, out IReadOnlyList<string> errors)
                && errors.Any(error => error.Contains("re-verification", StringComparison.OrdinalIgnoreCase));
        });
    }

    private sealed class ManifestFixture : IDisposable
    {
        public ManifestFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "TabDock-qualification-manifest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ParentRunId = Guid.NewGuid().ToString("D");
            ParentStartedUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
            CandidateSha = "0123456789abcdef0123456789abcdef01234567";
            CandidateExecutableSha = new string('A', 64);
            DriverSha = new string('B', 64);
        }

        public string Root { get; }
        public string ParentRunId { get; }
        public DateTimeOffset ParentStartedUtc { get; }
        public string CandidateSha { get; }
        public string CandidateExecutableSha { get; }
        public string DriverSha { get; }

        public string WriteChild(
            string shard,
            string? manifestShard = null,
            string? candidateSha = null,
            int schemaVersion = QualificationManifestVerifier.CurrentSchemaVersion,
            bool duplicateRunKind = false,
            bool pathTraversal = false)
        {
            string childRoot = Path.Combine(Root, "children", shard);
            string runId = Guid.NewGuid().ToString("D");
            string runDirectory = Path.Combine(childRoot, runId);
            Directory.CreateDirectory(runDirectory);
            DateTimeOffset started = ParentStartedUtc.AddMilliseconds(100);
            DateTimeOffset ended = started.AddMilliseconds(100);
            var scenarios = new List<object>();
            var artifactIndex = new List<object>();
            IReadOnlyList<string> catalogScenarios = ScenarioCatalog.GetShardScenarios(shard);
            for (int index = 0; index < catalogScenarios.Count; index++)
            {
                string scenario = catalogScenarios[index];
                string stem = scenario + (index == 0 ? string.Empty : $"-{index}");
                string jsonArtifact = $"{stem}.json";
                string junitArtifact = $"{stem}.junit.xml";
                string timelineArtifact = $"{stem}.timeline.json";
                if (pathTraversal && index == 0)
                    jsonArtifact = "../outside.json";

                WriteArtifact(runDirectory, jsonArtifact, "result-" + scenario);
                WriteArtifact(runDirectory, junitArtifact, "junit-" + scenario);
                WriteArtifact(runDirectory, timelineArtifact, "timeline-" + scenario);
                scenarios.Add(new
                {
                    scenario,
                    attempt = 1,
                    result = ScenarioOutcomeContract.Code(ScenarioOutcomeKind.Pass),
                    reason = (string?)null,
                    jsonArtifact,
                    junitArtifact,
                    timelineArtifact,
                    startedUtc = started,
                    endedUtc = ended,
                });
                artifactIndex.Add(Artifact(runDirectory, jsonArtifact, "scenario-result"));
                artifactIndex.Add(Artifact(runDirectory, junitArtifact, "junit"));
                artifactIndex.Add(Artifact(runDirectory, timelineArtifact, "timeline"));
            }

            var manifest = new
            {
                schemaVersion,
                runKind = "shard",
                runId,
                parentRunId = ParentRunId,
                shard = manifestShard ?? shard,
                manifestRelativePath = "run-manifest.json",
                catalogGeneration = ScenarioCatalog.Generation,
                candidateSha = candidateSha ?? CandidateSha,
                startedUtc = started,
                endedUtc = ended,
                outcome = ScenarioOutcomeContract.Code(ScenarioOutcomeKind.Pass),
                aggregateCounts = new Dictionary<string, int>
                {
                    [ScenarioOutcomeContract.Code(ScenarioOutcomeKind.Pass)] = catalogScenarios.Count,
                },
                executableSha256 = new { candidate = CandidateExecutableSha, test = DriverSha },
                driverIdentity = new { fileName = "ValidationDriver.exe", sha256 = DriverSha },
                scenarios,
                artifactIndex,
            };
            string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            if (duplicateRunKind)
                json = json.Replace(
                    "\"runKind\": \"shard\",",
                    "\"runKind\": \"shard\", \"runKind\": \"shard\",",
                    StringComparison.Ordinal);
            string manifestPath = Path.Combine(runDirectory, "run-manifest.json");
            File.WriteAllText(manifestPath, json);
            return manifestPath;
        }

        public ChildManifestVerification WriteAndImport(string shard)
        {
            WriteChild(shard);
            return ImportExisting(shard, ScenarioOutcomeContract.ExitCode(ScenarioOutcomeKind.Pass));
        }

        public ChildManifestVerification ImportExisting(string shard, int exitCode)
            => QualificationManifestVerifier.ImportChild(
                Path.Combine(Root, "children", shard),
                Root,
                shard,
                ParentRunId,
                CandidateSha,
                CandidateExecutableSha,
                DriverSha,
                ParentStartedUtc,
                exitCode);

        public ChildManifestVerification ImportMissing(string shard, int exitCode)
            => ImportExisting(shard, exitCode);

        public ParentManifestWriteResult WriteParent(
            IReadOnlyList<ChildManifestVerification> imports,
            IReadOnlyList<string>? expectedShards = null)
            => QualificationParentManifestWriter.Write(
                Root,
                ParentRunId,
                ParentStartedUtc,
                CandidateSha,
                CandidateExecutableSha,
                DriverSha,
                expectedShards ?? ScenarioCatalog.OrchestratedShardNames,
                imports);

        public void TamperFirstArtifact(string shard)
        {
            string childRoot = Path.Combine(Root, "children", shard);
            string runDirectory = Directory.GetDirectories(childRoot).Single();
            string artifact = Directory.GetFiles(runDirectory, "*.json")
                .First(path => !string.Equals(Path.GetFileName(path), "run-manifest.json", StringComparison.Ordinal)
                    && !path.EndsWith(".timeline.json", StringComparison.Ordinal));
            File.AppendAllText(artifact, "tampered");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Test cleanup must not hide the assertion result.
            }
        }

        private static void WriteArtifact(string runDirectory, string relativePath, string content)
        {
            string fullPath = Path.GetFullPath(Path.Combine(runDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(Path.GetFullPath(runDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return;
            File.WriteAllText(fullPath, content);
        }

        private static object Artifact(string runDirectory, string relativePath, string kind)
        {
            string fullPath = Path.Combine(runDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            bool exists = File.Exists(fullPath);
            string hash = exists
                ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath)))
                : "MISSING";
            return new { relativePath, kind, sha256 = hash, exists };
        }
    }

    private static IEnumerable<(string Id, Func<bool> Test)> VisualTests()
    {
        static PhysicalTopologySnapshot Topology()
            => PhysicalTopologySnapshot.Create(
                DateTimeOffset.UtcNow,
                new string('c', 40),
                new string('d', 64),
                new string('e', 64),
                "visual-run",
                "visual-scenario",
                1,
                new QualificationRect(0, 0, 1920, 1200),
                "monitor-001",
                new[]
                {
                    new PhysicalMonitorSnapshot(
                        "monitor-001",
                        new QualificationRect(0, 0, 1920, 1200),
                        new QualificationRect(0, 0, 1920, 1140),
                        true,
                        120,
                        125,
                        "primary",
                        new TaskbarDelta(0, 0, 0, 60)),
                });

        yield return ("VIS01-topology-binding-rejects-synthetic-context", () =>
        {
            VisualTopologyBinding binding = VisualTopologyBinding.From(
                Topology(),
                "visual-scenario",
                1);
            VisualTopologyBinding synthetic = binding with { SyntheticTopology = true };
            return binding.IsPhysicalEligible
                && !synthetic.IsPhysicalEligible;
        });

        yield return ("VIS02-physical-artifacts-carry-topology-binding", () =>
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "TabDock-visual-binding-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                PhysicalTopologySnapshot topology = Topology();
                VisualTopologyBinding binding = VisualTopologyBinding.From(
                    topology,
                    "visual-scenario",
                    1);
                var target = new VisualTargetIdentity(
                    "0x123",
                    1,
                    1,
                    "Fixture.Window",
                    1,
                    "ControlledGuest",
                    "OWNED");
                var provider = new StaticVisualCaptureProvider(new VisualFrame(
                    2,
                    2,
                    new[]
                    {
                        unchecked((int)0xFF000000),
                        unchecked((int)0xFFFFFFFF),
                        unchecked((int)0xFF112233),
                        unchecked((int)0xFF445566),
                    },
                    DateTimeOffset.UtcNow,
                    new VisualRect(0, 0, 2, 2),
                    new VisualRect(0, 0, 2, 2),
                    VisualCaptureMethod.SCREEN_COMPOSITION,
                    VisualCaptureScopeKind.GUEST_WINDOW,
                    target,
                    VisualPrivacyClass.TEST_OWNED,
                    120,
                    "monitor-001"));
                var recorder = new VisualEvidenceRecorder(
                    VisualEvidencePolicy.SafeDefaults(
                        VisualEvidenceLevel.CHECKPOINTS,
                        buildReviewPacket: true),
                    root,
                    "visual-scenario",
                    1,
                    provider,
                    binding);
                VisualCaptureScope scope = VisualCaptureScope.ForWindow(
                    VisualCaptureScopeKind.GUEST_WINDOW,
                    target,
                    VisualPrivacyClass.TEST_OWNED);
                VisualCheckpointResult checkpoint = recorder.Checkpoint(new VisualCheckpointRequest(
                    "baseline",
                    VisualCheckpointPhase.BASELINE,
                    "bounded fixture guest is visible",
                    new[] { scope },
                    VisualCaptureRequiredness.REQUIRED));
                VisualEvidenceManifest manifest = recorder.CreateManifest(
                    topology.CandidateSha,
                    topology.RunId,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow);
                return checkpoint.Captured
                    && manifest.TopologyBinding is not null
                    && manifest.Artifacts.Length == 1
                    && manifest.Artifacts[0].TopologyBinding is not null
                    && manifest.Artifacts[0].TopologyBinding!.MatchesAttempt(binding);
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        });

        yield return ("VIS03-strict-verifier-rejects-tampered-topology-result", () =>
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "TabDock-visual-verifier-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                PhysicalTopologySnapshot topology = Topology();
                VisualTopologyBinding binding = VisualTopologyBinding.From(
                    topology,
                    "visual-scenario",
                    1);
                var target = new VisualTargetIdentity(
                    "0x123",
                    1,
                    1,
                    "Fixture.Window",
                    1,
                    "ControlledGuest",
                    "OWNED");
                var recorder = new VisualEvidenceRecorder(
                    VisualEvidencePolicy.SafeDefaults(
                        VisualEvidenceLevel.CHECKPOINTS,
                        buildReviewPacket: true),
                    root,
                    "visual-scenario",
                    1,
                    new StaticVisualCaptureProvider(new VisualFrame(
                        2,
                        2,
                        new[]
                        {
                            unchecked((int)0xFF000000),
                            unchecked((int)0xFFFFFFFF),
                            unchecked((int)0xFF112233),
                            unchecked((int)0xFF445566),
                        },
                        DateTimeOffset.UtcNow,
                        new VisualRect(0, 0, 2, 2),
                        new VisualRect(0, 0, 2, 2),
                        VisualCaptureMethod.SCREEN_COMPOSITION,
                        VisualCaptureScopeKind.GUEST_WINDOW,
                        target,
                        VisualPrivacyClass.TEST_OWNED,
                        120,
                        "monitor-001")),
                    binding);
                VisualCaptureScope scope = VisualCaptureScope.ForWindow(
                    VisualCaptureScopeKind.GUEST_WINDOW,
                    target,
                    VisualPrivacyClass.TEST_OWNED);
                recorder.Checkpoint(new VisualCheckpointRequest(
                    "baseline",
                    VisualCheckpointPhase.BASELINE,
                    "bounded fixture guest is visible",
                    new[] { scope },
                    VisualCaptureRequiredness.REQUIRED));
                DateTimeOffset now = DateTimeOffset.UtcNow;
                VisualEvidenceManifest preliminary = recorder.CreateManifest(
                    topology.CandidateSha,
                    topology.RunId,
                    now,
                    now);
                if (!VisualReviewPacketBuilder.TryBuild(
                        preliminary,
                        new Dictionary<string, string> { ["fixture"] = "physical-binding" },
                        out VisualReviewPacketBuildResult? built,
                        out _)
                    || built is null)
                {
                    return false;
                }
                (VisualStoredArtifact packetArtifact, _) = recorder.WriteReviewPacket(built);
                VisualEvidenceManifest finalManifest = recorder.CreateManifest(
                    topology.CandidateSha,
                    topology.RunId,
                    now,
                    DateTimeOffset.UtcNow,
                    packetArtifact.RelativePath,
                    packetArtifact.Sha256);
                recorder.WriteManifest(
                    finalManifest,
                    $"visual/visual-scenario/attempt-001/manifest.json");
                byte[] packetBytes = File.ReadAllBytes(
                    Path.Combine(root, built.PacketRelativePath.Replace('/', Path.DirectorySeparatorChar)));
                string packetSha = Convert.ToHexString(SHA256.HashData(packetBytes)).ToLowerInvariant();
                var review = new VisualReviewResult(
                    VisualEvidenceSchema.ReviewResult,
                    VisualEvidenceSchema.CurrentVersion,
                    packetSha,
                    topology.CandidateSha,
                    topology.RunId,
                    "visual-scenario",
                    1,
                    VisualReviewVerdict.VISUAL_OK,
                    "deterministic",
                    "fixture",
                    DateTimeOffset.UtcNow,
                    built.Packet.Images.Select(image => new VisualReviewReviewedImage(
                        image.ArtifactId,
                        image.CheckpointId,
                        image.Sha256)).ToArray(),
                    Array.Empty<VisualReviewFinding>(),
                    "fixture review",
                    Array.Empty<string>(),
                    binding);
                string resultPath = Path.Combine(
                    root,
                    built.ResultRelativePath.Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllBytes(resultPath, JsonSerializer.SerializeToUtf8Bytes(review, VisualJson.Options));
                VisualReviewVerificationResult valid = VisualReviewVerifier.VerifyFiles(
                    root,
                    built.PacketRelativePath,
                    built.ResultRelativePath,
                    requirePhysicalTopology: true,
                    expectedTopology: binding);
                VisualReviewResult tampered = review with
                {
                    TopologyBinding = binding with { SnapshotId = new string('f', 64) },
                };
                File.WriteAllBytes(resultPath, JsonSerializer.SerializeToUtf8Bytes(tampered, VisualJson.Options));
                VisualReviewVerificationResult invalid = VisualReviewVerifier.VerifyFiles(
                    root,
                    built.PacketRelativePath,
                    built.ResultRelativePath,
                    requirePhysicalTopology: true,
                    expectedTopology: binding);
                return valid.Valid
                    && !invalid.Valid
                    && invalid.Failures.Any(failure =>
                        failure.Contains("topology", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        });
    }

    private static IEnumerable<(string Id, Func<bool> Test)> TopologyTests()
    {
        yield return ("TOPO01-lab-is-explicitly-synthetic", () =>
        {
            VirtualTopologyLabReport report = VirtualTopologyLab.Run();
            return report.SyntheticTopology
                && report.SchemaVersion == VirtualTopologyLab.SchemaVersion
                && report.Generation == VirtualTopologyLab.Generation
                && report.Cases.Any(item => item.Name == "seeded-transition-stress");
        });

        yield return ("TOPO02-fixed-matrix-covers-boundaries", () =>
        {
            string[] required =
            {
                "single-96", "dual-horizontal", "dual-vertical", "negative-left",
                "above-origin", "staggered-work-areas", "asymmetric-work-areas",
                "mixed-100-125-150-175-200", "odd-width", "narrow-work-area",
                "large-coordinates", "removal-reorder-transition",
            };
            string[] actual = VirtualTopologyLab.FixedTopologies().Select(topology => topology.Name).ToArray();
            return required.All(name => actual.Contains(name, StringComparer.Ordinal));
        });

        yield return ("TOPO03-fixed-seed-is-reproducible", () =>
        {
            VirtualTopologyLabReport first = VirtualTopologyLab.Run();
            VirtualTopologyLabReport second = VirtualTopologyLab.Run();
            return first.Seed == VirtualTopologyLab.Seed
                && first.NormalizedSha256 == second.NormalizedSha256
                && first.AssertionCount == second.AssertionCount
                && first.DpiTransitions.Select(item => item.Name)
                    .SequenceEqual(second.DpiTransitions.Select(item => item.Name), StringComparer.Ordinal);
        });

        yield return ("TOPO04-all-lab-invariants-pass", () =>
            VirtualTopologyLab.Run().Passed);

        yield return ("TOPO05-mixed-dpi-matrix-is-observed-in-model", () =>
        {
            LabTopology mixed = VirtualTopologyLab.FixedTopologies()
                .Single(topology => topology.Name == "mixed-100-125-150-175-200");
            return mixed.HasMixedDpi
                && new[] { 96, 120, 144, 168, 192 }.All(
                    dpi => mixed.Monitors.Any(monitor => monitor.EffectiveDpi == dpi));
        });

        yield return ("TOPO06-negative-above-staggered-and-asymmetric-rectangles-are-preserved", () =>
        {
            LabTopology negative = VirtualTopologyLab.FixedTopologies().Single(topology => topology.Name == "negative-left");
            LabTopology above = VirtualTopologyLab.FixedTopologies().Single(topology => topology.Name == "above-origin");
            LabTopology staggered = VirtualTopologyLab.FixedTopologies().Single(topology => topology.Name == "staggered-work-areas");
            LabTopology asymmetric = VirtualTopologyLab.FixedTopologies().Single(topology => topology.Name == "asymmetric-work-areas");
            return negative.HasNegativeXMonitor
                && above.HasAboveOriginMonitor
                && staggered.HasStaggeredPlacement
                && asymmetric.HasAsymmetricWorkAreas
                && negative.VirtualBounds.Left < 0
                && above.VirtualBounds.Top < 0;
        });

        yield return ("TOPO07-transition-removal-and-reorder-clamps-to-new-primary", () =>
        {
            LabTopology oldTopology = VirtualTopologyLab.FixedTopologies()
                .Single(topology => topology.Name == "mixed-100-125-150-175-200");
            LabTopology newTopology = VirtualTopologyLab.FixedTopologies()
                .Single(topology => topology.Name == "removal-reorder-transition");
            LabRect restored = VirtualTopologyPolicy.RestoreAfterTransition(
                new LabRect(-2000, 300, -1500, 700),
                oldTopology,
                newTopology);
            return newTopology.Primary.WorkArea.Contains(restored)
                && VirtualTopologyPolicy.FindMonitorForRectangle(restored, newTopology) == newTopology.Primary;
        });

        yield return ("TOPO08-required-dpi-transitions-run-in-both-directions", () =>
        {
            VirtualTopologyLabReport report = VirtualTopologyLab.Run();
            return report.DpiTransitions.Count == VirtualTopologyLab.RequiredDpiTransitions.Count
                && report.DpiTransitions.All(item => item.Passed)
                && VirtualTopologyLab.RequiredDpiTransitions.All(pair =>
                    report.DpiTransitions.Any(item =>
                        item.SourceDpi == pair.SourceDpi
                        && item.DestinationDpi == pair.DestinationDpi));
        });

        yield return ("TOPO09-title-inputs-cover-width-classes", () =>
        {
            VirtualTopologyLabReport report = VirtualTopologyLab.Run();
            return report.Cases
                .Where(item => item.Name != "seeded-transition-stress")
                .All(item => item.TitleAssertionCount == item.MonitorCount * 3 * 3);
        });

        yield return ("TOPO10-artifact-reports-boundary-metadata", () =>
        {
            VirtualTopologyLabReport report = VirtualTopologyLab.Run();
            return report.Cases.Any(item => item.StaggeredPlacement)
                && report.Cases.Any(item => item.AsymmetricWorkAreas)
                && report.Cases.Any(item => item.RelativePlacements.Any(
                    placement => placement.Contains(":left", StringComparison.Ordinal)
                        || placement.Contains(":above", StringComparison.Ordinal)));
        });

        yield return ("TOPO11-synthetic-topology-is-ineligible-for-physical-gate", () =>
        {
            string candidateSha = new string('0', 40);
            string executableSha = new string('a', 64);
            string driverSha = new string('b', 64);
            var metadata = new PhysicalTopologyCaptureMetadata(
                candidateSha,
                executableSha,
                driverSha,
                "run-fixture",
                "scenario-fixture",
                1);
            PhysicalTopologySnapshot snapshot = PhysicalTopologySnapshot.Create(
                DateTimeOffset.UtcNow,
                candidateSha,
                executableSha,
                driverSha,
                "run-fixture",
                "scenario-fixture",
                1,
                new QualificationRect(0, 0, 1920, 1080),
                "monitor-001",
                new[]
                {
                    new PhysicalMonitorSnapshot(
                        "monitor-001",
                        new QualificationRect(0, 0, 1920, 1080),
                        new QualificationRect(0, 0, 1920, 1040),
                        true,
                        96,
                        100,
                        "primary",
                        new TaskbarDelta(0, 0, 0, 40)),
                },
                syntheticTopology: true);
            IReadOnlyList<string> failures = PhysicalTopologyGate.ValidatePhysicalSnapshot(
                snapshot,
                metadata,
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(1));
            return !PhysicalTopologyGate.IsPhysicalEligible(snapshot)
                && failures.Any(failure => failure.Contains("synthetic", StringComparison.OrdinalIgnoreCase));
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

        string fixtureCandidateSha = new string('c', 40);
        string fixtureExecutableSha = new string('d', 64);
        string fixtureDriverSha = new string('e', 64);
        PhysicalTopologyCaptureMetadata fixtureMetadata = new(
            fixtureCandidateSha,
            fixtureExecutableSha,
            fixtureDriverSha,
            "run-fixture",
            "scenario-fixture",
            1);
        PhysicalTopologySnapshot fixtureTopology = PhysicalTopologySnapshot.Create(
            DateTimeOffset.UtcNow,
            fixtureCandidateSha,
            fixtureExecutableSha,
            fixtureDriverSha,
            "run-fixture",
            "scenario-fixture",
            1,
            new QualificationRect(0, 0, 3840, 1200),
            "monitor-001",
            new[]
            {
                new PhysicalMonitorSnapshot(
                    "monitor-001",
                    new QualificationRect(0, 0, 1920, 1200),
                    new QualificationRect(0, 0, 1920, 1140),
                    true,
                    120,
                    125,
                    "primary",
                    new TaskbarDelta(0, 0, 0, 60)),
                new PhysicalMonitorSnapshot(
                    "monitor-002",
                    new QualificationRect(1920, 0, 3840, 1080),
                    new QualificationRect(1920, 0, 3840, 1032),
                    false,
                    96,
                    100,
                    "right",
                    new TaskbarDelta(0, 0, 0, 48)),
            });
        ScenarioCapabilitySnapshot physicalComplete = complete with
        {
            MonitorCount = 2,
            MixedDpiAvailable = true,
            NonDefaultDpiAvailable = true,
            Topology = fixtureTopology,
            TopologyProbeFailure = null,
        };

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
        yield return ("C09-physical-plan-blocks-before-supervision", () =>
        {
            IReadOnlyList<PhysicalQualificationPlanRow> rows = PhysicalQualificationPlan.BuildRows(
                physicalComplete,
                fixtureMetadata,
                supervisionConfirmed: false,
                DateTimeOffset.UtcNow);
            return rows.Count == PhysicalQualificationPlan.Cells.Count
                && rows.All(row => row.Outcome == PhysicalCellOutcome.BLOCKED_SUPERVISED)
                && rows.All(row => row.Reason?.Contains("supervised", StringComparison.OrdinalIgnoreCase) == true);
        });
        yield return ("C10-physical-plan-exposes-only-supported-cells", () =>
        {
            IReadOnlyList<PhysicalQualificationPlanRow> rows = PhysicalQualificationPlan.BuildRows(
                physicalComplete,
                fixtureMetadata,
                supervisionConfirmed: true,
                DateTimeOffset.UtcNow);
            PhysicalQualificationPlanRow split = rows.Single(row => row.Cell.Id == "single-split-containment");
            PhysicalQualificationPlanRow highDpi = rows.Single(row => row.Cell.Id == "dpi-150-percent");
            PhysicalQualificationPlanRow negative = rows.Single(row => row.Cell.Id == "topology-negative-x");
            return split.Outcome == PhysicalCellOutcome.RUNNABLE
                && highDpi.Outcome == PhysicalCellOutcome.BLOCKED_CAPABILITY
                && negative.Outcome == PhysicalCellOutcome.BLOCKED_CAPABILITY
                && rows.All(row => row.Outcome != PhysicalCellOutcome.BLOCKED_SUPERVISED);
        });
        yield return ("C11-stale-topology-is-rejected", () =>
        {
            PhysicalTopologySnapshot stale = fixtureTopology with
            {
                ObservedUtc = DateTimeOffset.UtcNow.AddMinutes(-6),
            };
            IReadOnlyList<string> failures = PhysicalTopologyGate.ValidatePhysicalSnapshot(
                stale,
                fixtureMetadata,
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(5));
            return failures.Any(failure => failure.Contains("stale", StringComparison.OrdinalIgnoreCase));
        });
        yield return ("C12-candidate-mismatch-is-rejected", () =>
        {
            PhysicalTopologyCaptureMetadata wrongCandidate = fixtureMetadata with
            {
                CandidateSha = new string('f', 40),
            };
            IReadOnlyList<string> failures = PhysicalTopologyGate.ValidatePhysicalSnapshot(
                fixtureTopology,
                wrongCandidate,
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(5));
            return failures.Any(failure => failure.Contains("candidate", StringComparison.OrdinalIgnoreCase));
        });
        yield return ("C13-invalid-monitor-id-is-rejected", () =>
        {
            PhysicalTopologySnapshot invalid = fixtureTopology with
            {
                PrimaryMonitorId = "monitor-999",
            };
            invalid = invalid with { SnapshotId = PhysicalTopologySnapshot.ComputeSnapshotId(invalid) };
            IReadOnlyList<string> failures = PhysicalTopologyGate.ValidatePhysicalSnapshot(
                invalid,
                fixtureMetadata,
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(5));
            return failures.Any(failure => failure.Contains("primary identity", StringComparison.OrdinalIgnoreCase));
        });
        yield return ("C14-monitor-disappearance-is-restoration-mismatch", () =>
        {
            PhysicalTopologySnapshot missingMonitor = PhysicalTopologySnapshot.Create(
                DateTimeOffset.UtcNow,
                fixtureCandidateSha,
                fixtureExecutableSha,
                fixtureDriverSha,
                "run-fixture",
                "scenario-fixture",
                1,
                fixtureTopology.VirtualScreen,
                fixtureTopology.PrimaryMonitorId,
                new[] { fixtureTopology.Monitors[0] });
            bool equivalent = PhysicalTopologySnapshot.EquivalentTopology(
                fixtureTopology,
                missingMonitor,
                out string reason);
            return !equivalent && reason.Contains("count", StringComparison.OrdinalIgnoreCase);
        });
        yield return ("C15-topology-change-between-preflight-and-input-is-rejected", () =>
        {
            PhysicalMonitorSnapshot changedMonitor = fixtureTopology.Monitors[1] with
            {
                WorkArea = new QualificationRect(1920, 0, 3840, 1031),
                TaskbarDelta = new TaskbarDelta(0, 0, 0, 49),
            };
            PhysicalTopologySnapshot changed = PhysicalTopologySnapshot.Create(
                DateTimeOffset.UtcNow,
                fixtureCandidateSha,
                fixtureExecutableSha,
                fixtureDriverSha,
                "run-fixture",
                "scenario-fixture",
                1,
                fixtureTopology.VirtualScreen,
                fixtureTopology.PrimaryMonitorId,
                new[] { fixtureTopology.Monitors[0], changedMonitor });
            bool equivalent = PhysicalTopologySnapshot.EquivalentTopology(
                fixtureTopology,
                changed,
                out string reason);
            return !equivalent && reason.Contains("geometry", StringComparison.OrdinalIgnoreCase);
        });
        yield return ("C16-dpi-probe-unavailable-is-environment-block", () =>
        {
            ScenarioCapabilitySnapshot unavailable = complete with
            {
                MonitorCount = 0,
                MixedDpiAvailable = false,
                NonDefaultDpiAvailable = false,
                Topology = null,
                TopologyProbeFailure = "native monitor enumeration or DPI probe failed",
            };
            ScenarioCapabilityResolution result = ScenarioCapabilities.Resolve(
                new ScenarioDescriptor("physical-mixed-dpi", RequiresMixedDpi: true),
                unavailable);
            return !result.Runnable
                && result.Outcome == ScenarioOutcomeKind.BlockedEnvironment
                && result.Reason?.Contains("DPI probe", StringComparison.OrdinalIgnoreCase) == true;
        });
        yield return ("C17-source-destination-dpi-mismatch-is-blocked", () =>
        {
            PhysicalQualificationPlanRow transition = PhysicalQualificationPlan.BuildRows(
                physicalComplete,
                fixtureMetadata,
                supervisionConfirmed: true,
                DateTimeOffset.UtcNow)
                .Single(row => row.Cell.Id == "transition-120-to-144");
            return transition.Outcome == PhysicalCellOutcome.BLOCKED_CAPABILITY
                && transition.Reason?.Contains("144", StringComparison.Ordinal) == true;
        });
        yield return ("C18-operator-display-protocol-gates-input", () =>
            PhysicalDisplayStateProtocol.Steps.Count == 10
            && PhysicalDisplayStateProtocol.Steps.Count(step => step.InputAllowed) == 1
            && PhysicalDisplayStateProtocol.Steps.Single(step => step.InputAllowed).Id == "run-bounded-cell"
            && PhysicalDisplayStateProtocol.ProhibitedOperations.Count == 4
            && !PhysicalDisplayStateProtocol.InputMayBegin(
                PhysicalCellOutcome.BLOCKED_CAPABILITY,
                supervisionConfirmed: true,
                topologyVerified: true,
                leaseActive: true)
            && PhysicalDisplayStateProtocol.InputMayBegin(
                PhysicalCellOutcome.RUNNABLE,
                supervisionConfirmed: true,
                topologyVerified: true,
                leaseActive: true));
    }

    private static IEnumerable<(string Id, Func<bool> Test)> LeaseTests()
    {
        static DesktopWindowObservation Window(
            IntPtr hwnd,
            RunOwnershipKind ownership,
            string role = "TabDockContainer",
            string key = "fixture-window")
            => new(hwnd, key, ownership, role, true, true);

        PhysicalTopologySnapshot FixtureTopology()
            => PhysicalTopologySnapshot.Create(
                DateTimeOffset.UtcNow,
                new string('1', 40),
                new string('2', 64),
                new string('3', 64),
                "lease-run",
                "lease-scenario",
                1,
                new QualificationRect(0, 0, 3840, 1200),
                "monitor-001",
                new[]
                {
                    new PhysicalMonitorSnapshot(
                        "monitor-001",
                        new QualificationRect(0, 0, 1920, 1200),
                        new QualificationRect(0, 0, 1920, 1140),
                        true,
                        120,
                        125,
                        "primary",
                        new TaskbarDelta(0, 0, 0, 60)),
                    new PhysicalMonitorSnapshot(
                        "monitor-002",
                        new QualificationRect(1920, 0, 3840, 1080),
                        new QualificationRect(1920, 0, 3840, 1032),
                        false,
                        96,
                        100,
                        "right",
                        new TaskbarDelta(0, 0, 0, 48)),
                });

        DesktopQualificationSnapshot Snapshot(
            bool locked = false,
            PhysicalTopologySnapshot? topology = null)
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
                TestRunnerIdentity: "pid=fixture",
                Topology: topology);

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
        yield return ("LSE06-owned-foreground-source-is-admitted", () =>
        {
            var probe = new FakeDesktopProbe(Snapshot());
            var lease = new DesktopQualificationLease(probe, new NativeInteractionTimeline());
            lease.Start();
            DesktopLeaseCheckpoint checkpoint = lease.Checkpoint(
                "foreground-source-before-switch",
                requireForeground: true);
            return lease.IsValid && checkpoint.IsValid;
        });
        yield return ("LSE07-foreign-foreground-source-invalidates", () =>
        {
            var probe = new FakeDesktopProbe(Snapshot());
            probe.Foregrounds.Clear();
            probe.Foregrounds.Enqueue(Window(new IntPtr(0x400), RunOwnershipKind.Foreign, "ForeignOverlay", "foreign"));
            var lease = new DesktopQualificationLease(probe, new NativeInteractionTimeline());
            lease.Start();
            DesktopLeaseCheckpoint checkpoint = lease.Checkpoint(
                "foreground-source-before-switch",
                requireForeground: true);
            return !lease.IsValid && checkpoint.Kind == DesktopLeaseCheckpointKind.ForeignForeground;
        });
        yield return ("LSE08-missing-topology-restore-proof-invalidates", () =>
        {
            var lease = new DesktopQualificationLease(
                new FakeDesktopProbe(Snapshot()),
                new NativeInteractionTimeline());
            lease.Start();
            return !lease.VerifyRestored()
                && lease.State == DesktopLeaseState.Invalidated
                && lease.LastFailureReason?.Contains("topology", StringComparison.OrdinalIgnoreCase) == true;
        });
        yield return ("LSE09-equivalent-topology-restores-successfully", () =>
        {
            DesktopQualificationSnapshot baseline = Snapshot(topology: FixtureTopology());
            var probe = new FakeDesktopProbe(baseline);
            var lease = new DesktopQualificationLease(probe, new NativeInteractionTimeline());
            lease.Start();
            bool restored = lease.VerifyRestored();
            return restored
                && lease.IsValid
                && lease.LastFailureReason == null;
        });
        yield return ("LSE10-changed-topology-restoration-invalidates", () =>
        {
            DesktopQualificationSnapshot baseline = Snapshot(topology: FixtureTopology());
            PhysicalMonitorSnapshot changedMonitor = baseline.Topology!.Monitors[1] with
            {
                WorkArea = new QualificationRect(1920, 0, 3840, 1031),
                TaskbarDelta = new TaskbarDelta(0, 0, 0, 49),
            };
            PhysicalTopologySnapshot changed = PhysicalTopologySnapshot.Create(
                DateTimeOffset.UtcNow,
                baseline.Topology.CandidateSha,
                baseline.Topology.CandidateExecutableSha,
                baseline.Topology.DriverSha,
                baseline.Topology.RunId,
                baseline.Topology.Scenario,
                baseline.Topology.Attempt,
                baseline.Topology.VirtualScreen,
                baseline.Topology.PrimaryMonitorId,
                new[] { baseline.Topology.Monitors[0], changedMonitor });
            var probe = new FakeDesktopProbe(baseline);
            probe.Captures.Enqueue(baseline with { Topology = changed });
            var lease = new DesktopQualificationLease(probe, new NativeInteractionTimeline());
            lease.Start();
            bool restored = lease.VerifyRestored();
            return !restored
                && !lease.IsValid
                && lease.LastFailureReason?.Contains("restored", StringComparison.OrdinalIgnoreCase) == true;
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

    private static IEnumerable<(string Id, Func<bool> Test)> ResourceTests()
    {
        yield return ("R01-flat-series-passes", () =>
        {
            ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze(
                "flat",
                ResourceSnapshots(Enumerable.Repeat(100L, 10).ToArray()));
            return result.Passed
                && result.Metrics.All(metric => metric.Trend == ResourceTrend.Flat);
        });

        yield return ("R02-warmup-growth-reaches-settled-plateau", () =>
        {
            ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze(
                "warmup",
                ResourceSnapshots(new long[] { 100, 140, 180, 180, 180, 180, 180, 180 }));
            ResourceMetricAnalysis handles = result.Metrics.Single(metric => metric.Metric == ResourceMetric.HandleCount);
            return result.Passed && handles.Baseline == 180 && handles.FinalDelta == 0;
        });

        yield return ("R03-bounded-native-noise-passes", () =>
        {
            ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze(
                "noise",
                ResourceSnapshots(new long[] { 100, 101, 100, 102, 101, 100, 101, 100 }));
            return result.Passed
                && result.Metrics.Single(metric => metric.Metric == ResourceMetric.HandleCount).Trend
                    == ResourceTrend.BoundedNoise;
        });

        foreach (ResourceMetric metric in new[]
        {
            ResourceMetric.HandleCount,
            ResourceMetric.UserObjectCount,
            ResourceMetric.GdiObjectCount,
        })
        {
            ResourceMetric captured = metric;
            yield return ($"R04-native-{metric}-leak-fails", () =>
            {
                ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze(
                    "native-leak",
                    ResourceSnapshots(
                        Enumerable.Range(0, 10).Select(index => 100L + index).ToArray(),
                        captured));
                ResourceMetricAnalysis analysis = result.Metrics.Single(item => item.Metric == captured);
                return result.Outcome == ResourceStabilityOutcome.Fail
                    && analysis.Trend == ResourceTrend.MonotonicGrowth;
            });
        }

        yield return ("R05-private-memory-growth-fails", () =>
        {
            ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze(
                "private-memory-leak",
                ResourceSnapshots(
                    Enumerable.Range(0, 10)
                        .Select(index => 100L * 1024 * 1024 + index * 2L * 1024 * 1024)
                        .ToArray(),
                    ResourceMetric.PrivateBytes));
            return result.Outcome == ResourceStabilityOutcome.Fail
                && result.Metrics.Single(item => item.Metric == ResourceMetric.PrivateBytes).Trend
                    == ResourceTrend.MonotonicGrowth;
        });

        yield return ("R06-transient-spike-recovering-passes", () =>
        {
            ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze(
                "spike",
                ResourceSnapshots(new long[] { 100, 100, 100, 100, 150, 100, 101, 100, 100, 100 }));
            ResourceMetricAnalysis analysis = result.Metrics.Single(item => item.Metric == ResourceMetric.HandleCount);
            return result.Passed
                && analysis.Trend == ResourceTrend.TransientSpike
                && analysis.PeakDelta == 50;
        });

        yield return ("R07-late-leak-is-not-hidden", () =>
        {
            ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze(
                "late-leak",
                ResourceSnapshots(new long[] { 100, 100, 100, 100, 100, 101, 102, 103, 104, 105 }));
            ResourceMetricAnalysis analysis = result.Metrics.Single(item => item.Metric == ResourceMetric.HandleCount);
            return result.Outcome == ResourceStabilityOutcome.Fail
                && analysis.Trend is ResourceTrend.LateGrowth or ResourceTrend.MonotonicGrowth;
        });

        yield return ("R08-process-generation-change-blocks", () =>
        {
            List<ResourceSnapshot> snapshots = ResourceSnapshots(Enumerable.Repeat(100L, 8).ToArray()).ToList();
            snapshots[6] = snapshots[6] with
            {
                ProcessIdentity = new ResourceProcessIdentity(999, 2),
            };
            return ResourceSeriesAnalyzer.Analyze("generation", snapshots).Outcome
                == ResourceStabilityOutcome.Blocked;
        });

        yield return ("R09-missing-metric-blocks", () =>
        {
            List<ResourceSnapshot> snapshots = ResourceSnapshots(Enumerable.Repeat(100L, 8).ToArray())
                .Select(snapshot => snapshot with { GdiObjectCount = null })
                .ToList();
            ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze("missing", snapshots);
            return result.Outcome == ResourceStabilityOutcome.Blocked
                && result.Metrics.Single(item => item.Metric == ResourceMetric.GdiObjectCount).Trend
                    == ResourceTrend.Unavailable;
        });

        yield return ("R10-counter-reset-blocks", () =>
        {
            ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze(
                "reset",
                ResourceSnapshots(new long[] { 100, 100, 100, 100, 0, 1, 1, 1 }));
            ResourceMetricAnalysis analysis = result.Metrics.Single(item => item.Metric == ResourceMetric.HandleCount);
            return result.Outcome == ResourceStabilityOutcome.Blocked
                && analysis.Trend == ResourceTrend.CounterReset;
        });

        yield return ("R11-invalid-order-blocks", () =>
        {
            List<ResourceSnapshot> snapshots = ResourceSnapshots(Enumerable.Repeat(100L, 8).ToArray()).ToList();
            snapshots[4] = snapshots[4] with { Sequence = snapshots[3].Sequence };
            return ResourceSeriesAnalyzer.Analyze("invalid-order", snapshots).Outcome
                == ResourceStabilityOutcome.Blocked;
        });

        yield return ("R12-insufficient-settled-samples-blocks", () =>
        {
            ResourceSeriesAnalysis result = ResourceSeriesAnalyzer.Analyze(
                "short",
                ResourceSnapshots(new long[] { 100, 100, 100, 100 }),
                new ResourceStabilityOptions { WarmupSamples = 3, MinimumSettledSamples = 2 });
            return result.Outcome == ResourceStabilityOutcome.Blocked;
        });

        yield return ("R13-unavailable-process-identity-blocks", () =>
        {
            List<ResourceSnapshot> snapshots = ResourceSnapshots(Enumerable.Repeat(100L, 8).ToArray()).ToList();
            snapshots[5] = snapshots[5] with
            {
                ProcessIdentity = new ResourceProcessIdentity(42, 0),
                MeasurementError = "process-exited",
            };
            return ResourceSeriesAnalyzer.Analyze("process-exit", snapshots).Outcome
                == ResourceStabilityOutcome.Blocked;
        });

        yield return ("R14-all-snapshot-metrics-are-explicit", () =>
        {
            ResourceSnapshot snapshot = ResourceSnapshots(new long[] { 1, 1, 1, 1, 1, 1, 1, 1 })[0];
            return Enum.GetValues<ResourceMetric>().All(metric => snapshot.Value(metric) is not null);
        });
    }

    private static IReadOnlyList<ResourceSnapshot> ResourceSnapshots(
        long[] values,
        ResourceMetric focus = ResourceMetric.HandleCount)
    {
        var snapshots = new List<ResourceSnapshot>(values.Length);
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        for (int i = 0; i < values.Length; i++)
        {
            long? handles = values[i];
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
                handles,
                user,
                gdi,
                privateBytes,
                workingSet,
                threads,
                windows));
        }
        return snapshots;
    }

    private sealed class ForegroundTestRuntime : IForegroundQualificationRuntime
    {
        public bool LeaseIsActive { get; set; } = true;
        public bool TargetCurrent { get; set; } = true;
        public bool ArrangeResult { get; set; }
        public bool TargetForeground { get; set; }
        public bool SafePointAvailable { get; set; } = true;
        public bool PointVerified { get; set; } = true;
        public bool ClickResult { get; set; } = true;
        public bool ClickSetsForeground { get; set; }
        public bool ForegroundProof { get; set; } = true;
        public bool InvalidateLeaseOnPointVerification { get; set; }
        public int ArrangeCount { get; private set; }
        public int PointVerificationCount { get; private set; }
        public int ClickCount { get; private set; }
        public int ForegroundProofCount { get; private set; }

        public bool IsTargetCurrent(WindowIdentity expected) => TargetCurrent;

        public bool TryArrangeForeground(WindowIdentity expected)
        {
            ArrangeCount++;
            if (ArrangeResult)
                TargetForeground = true;
            return ArrangeResult;
        }

        public bool IsTargetForeground(WindowIdentity expected) => TargetForeground;

        public bool TryGetSafeActivationPoint(
            WindowIdentity expected,
            out ForegroundActivationPoint point)
        {
            point = new ForegroundActivationPoint(500, 1);
            return SafePointAvailable;
        }

        public bool VerifyActivationPoint(
            WindowIdentity expected,
            ForegroundActivationPoint point)
        {
            PointVerificationCount++;
            if (InvalidateLeaseOnPointVerification)
                LeaseIsActive = false;
            return PointVerified && LeaseIsActive;
        }

        public bool ClickActivationPoint(
            WindowIdentity expected,
            ForegroundActivationPoint point)
        {
            ClickCount++;
            if (ClickResult && ClickSetsForeground)
                TargetForeground = true;
            return ClickResult;
        }

        public bool VerifyForegroundAfterActivation(WindowIdentity expected)
        {
            ForegroundProofCount++;
            return ForegroundProof && TargetForeground;
        }
    }

    private sealed class StaticVisualCaptureProvider : IVisualCaptureProvider
    {
        private readonly VisualFrame _frame;

        public StaticVisualCaptureProvider(VisualFrame frame)
            => _frame = frame;

        public bool TryCapture(
            VisualCaptureScope scope,
            out VisualFrame? frame,
            out string reason)
        {
            frame = _frame;
            reason = string.Empty;
            return true;
        }
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
        public Queue<DesktopQualificationSnapshot> Captures { get; } = new();

        public DesktopQualificationSnapshot Capture()
            => Captures.Count == 0 ? _snapshot : Captures.Dequeue();

        public DesktopWindowObservation ObserveForeground()
            => Foregrounds.Count == 0 ? _snapshot.Foreground : Foregrounds.Dequeue();

        public DesktopWindowObservation ObservePoint(int x, int y)
            => Points.Count == 0 ? _snapshot.Foreground : Points.Dequeue();
    }
}
