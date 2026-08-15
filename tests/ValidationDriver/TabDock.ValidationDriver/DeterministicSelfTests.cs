using System;
using System.Collections.Generic;
using System.Linq;
using TabDock.Models;

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
        return passed == tests.Count ? 0 : 5;
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
    }

    private static WindowIdentity Window(IntPtr hwnd, uint pid, uint tid, string exe, long start)
        => new(hwnd, pid, tid, "TestWindowClass", "TDTEST:fixture", exe, start);
}
