using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Deterministic coverage for the explicitly supervised tokenless-journal
/// workflow. No real HWND is enumerated or mutated by these tests.
/// </summary>
internal static class PendingRecoverySelfTest
{
    public static (int Checks, int Failures) Run()
    {
        int checks = 0;
        int failures = 0;
        void Check(bool condition)
        {
            checks++;
            if (!condition)
                failures++;
        }

        Check(NoPendingEvidenceIsReadOnly());
        Check(V1AndV2EvidenceIsDiscovered());
        Check(MultipleFilesAndEntriesAreListed());
        Check(MalformedFutureAndInaccessibleEvidenceIsRetained());
        Check(HistoricalFieldsMustMatch());
        Check(UserCancellationDoesNotMutate());
        Check(V1RecoveryIsVisibilityOnly());
        Check(V2RecoveryRestoresPresentation());
        Check(RecoveryFailuresRetainEvidence());
        Check(GenerationChangesStopLaterMutations());
        Check(SuccessfulRecoveryRetiresOneEntryOnly());
        Check(DuplicateEntriesDoNotCollapseSiblingEvidence());
        Check(ResolvedEntryRetirementCanBeRetried());
        Check(ExistingTokensRefuseRecovery());
        Check(DoNotRescueNeverResurrectsGuest());
        Check(DoNotRescueWithoutRecordedTransitionStateStillCleansDwm());
        Check(RecoveryTransactionFaultsAreResumable());
        Check(InterruptedInteractiveRecoveryResumesAndRetires());
        Check(RetirementFaultPreservesSiblingEvidence());
        Check(RandomRecoveryTokenIsDurableAndNonzero());
        Check(RecoveryTitlesAreTerminalSafe());
        return (checks, failures);
    }

    private static bool NoPendingEvidenceIsReadOnly()
    {
        string root = CreateRoot();
        try
        {
            PendingRecoveryCatalog catalog = PendingRecoveryService.Discover(root, new FakePendingApi());
            return catalog.Error == null && catalog.Files.Count == 0;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool V1AndV2EvidenceIsDiscovered()
    {
        string root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending"),
                JournalJson(null, EntryV1(100, 41, "legacy.exe")));
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending.001"),
                JournalJson(2, EntryV2(200, 42, "modern.exe", 4202)));
            var api = new FakePendingApi(
                Target.For(100, 41, 1041, "legacy.exe", "Legacy", 0),
                Target.For(200, 42, 1042, "modern.exe", "Modern", 4202));
            PendingRecoveryCatalog catalog = PendingRecoveryService.Discover(root, api);
            PendingRecoveryEntry[] entries = catalog.Files.SelectMany(file => file.Entries).ToArray();
            PendingRecoveryEntry v1 = entries.Single(entry => entry.Version == 1);
            PendingRecoveryEntry v2 = entries.Single(entry => entry.Version == 2);
            return catalog.Error == null
                && entries.Length == 2
                && v1.IsV1
                && !v1.Fields.HasClass
                && v2.Fields.HasClass
                && v2.Fields.HasProcessStart
                && entries.All(entry => entry.Status == "potentially-recoverable");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool MultipleFilesAndEntriesAreListed()
    {
        string root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending"),
                JournalJson(2,
                    EntryV2(301, 51, "one.exe", 5101),
                    EntryV2(302, 52, "two.exe", 5202)));
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending.001"),
                JournalJson(1, EntryV1(303, 53, "three.exe")));
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending.002"),
                JournalJson(1, EntryV1(304, 54, "four.exe")));
            var api = new FakePendingApi(
                Target.For(301, 51, 1051, "one.exe", "Pig", 5101),
                Target.For(302, 52, 1052, "two.exe", "Pig", 5202),
                Target.For(303, 53, 1053, "three.exe", "Pig", 0),
                Target.For(304, 54, 1054, "four.exe", "Pig", 0));
            PendingRecoveryCatalog catalog = PendingRecoveryService.Discover(root, api);
            return catalog.Files.Count == 3
                && catalog.Files.Any(file => file.Entries.Count == 2)
                && catalog.Files.Sum(file => file.Entries.Count) == 4
                && PendingRecoveryService.CountActivePendingFiles(root) == 3;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool MalformedFutureAndInaccessibleEvidenceIsRetained()
    {
        string root = CreateRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "hidden-windows.json.pending"), "{not-json");
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending.001"),
                JournalJson(99, EntryV2(401, 61, "future.exe", 6101)));
            PendingRecoveryCatalog catalog = PendingRecoveryService.Discover(root, new FakePendingApi());
            string inaccessiblePath = Path.Combine(root, "not-a-directory");
            File.WriteAllText(inaccessiblePath, "still evidence");
            PendingRecoveryCatalog inaccessible = PendingRecoveryService.Discover(inaccessiblePath, new FakePendingApi());
            return catalog.Files.Count == 2
                && catalog.Files.Any(file => file.Status.StartsWith("malformed", StringComparison.Ordinal))
                && catalog.Files.Any(file => file.Status == "future-schema" && file.Entries.Count == 1)
                && inaccessible.Error == "unreadable (not-a-directory)"
                && File.Exists(Path.Combine(root, "hidden-windows.json.pending"))
                && File.Exists(Path.Combine(root, "hidden-windows.json.pending.001"));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool HistoricalFieldsMustMatch()
    {
        string root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending"),
                JournalJson(2, EntryV2(500, 71, "match.exe", 7101)));
            var api = new FakePendingApi(Target.For(500, 71, 1071, "match.exe", "Pig", 7101));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            PendingRecoveryCandidate candidate = CandidateFor(entry, "C001");
            PendingRecoveryCandidate wrongPid = CandidateFor(entry, "C002");
            wrongPid = CopyCandidate(wrongPid, processId: 72);
            PendingRecoveryCandidate wrongExe = CopyCandidate(candidate, "C003", exePath: "other.exe");
            PendingRecoveryCandidate wrongStart = CopyCandidate(candidate, "C004", processStart: 7102);
            PendingRecoveryCandidate wrongHwnd = CopyCandidate(candidate, "C005", hwnd: new IntPtr(501));
            return PendingRecoveryService.MatchesHistoricalEvidence(entry, candidate)
                && !PendingRecoveryService.MatchesHistoricalEvidence(entry, wrongPid)
                && !PendingRecoveryService.MatchesHistoricalEvidence(entry, wrongExe)
                && !PendingRecoveryService.MatchesHistoricalEvidence(entry, wrongStart)
                && !PendingRecoveryService.MatchesHistoricalEvidence(entry, wrongHwnd);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool UserCancellationDoesNotMutate()
    {
        string root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending"),
                JournalJson(2, EntryV2(600, 81, "cancel.exe", 8101)));
            var api = new FakePendingApi(Target.For(600, 81, 1081, "cancel.exe", "Modern", 8101));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            PendingRecoveryCandidate candidate = CandidateFor(entry, "C001");
            using var input = new StringReader($"{entry.SessionId}\n{candidate.CandidateId}\nNO\n");
            using var output = new StringWriter();
            int result = PendingRecoveryService.RunInteractive(
                input, output, root, api, new[] { candidate });
            return result == 1
                && api.MutationCount == 0
                && File.Exists(entry.FullPath)
                && !File.Exists(entry.FullPath + ".recovered");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool V1RecoveryIsVisibilityOnly()
    {
        string root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending"),
                JournalJson(null, EntryV1(700, 91, "v1.exe")));
            var api = new FakePendingApi(Target.For(700, 91, 1091, "v1.exe", "Legacy", 0));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool recovered = PendingRecoveryService.ExecuteRecovery(
                entry, CandidateFor(entry, "C001"), api, out _);
            return recovered
                && api.PlacementCount == 0
                && api.ShowCount == 1
                && api.TransitionCount == 0
                && api.RemovePropertyCount == 1
                && api.Targets[new IntPtr(700)].Visible;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool V2RecoveryRestoresPresentation()
    {
        string root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending"),
                JournalJson(2, EntryV2(800, 101, "v2.exe", 10101)));
            var api = new FakePendingApi(Target.For(800, 101, 1101, "v2.exe", "Modern", 10101));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool recovered = PendingRecoveryService.ExecuteRecovery(
                entry, CandidateFor(entry, "C001"), api, out _);
            return recovered
                && api.PlacementCount == 1
                && api.ShowCount == 1
                && api.TransitionCount == 1
                && api.RemovePropertyCount == 1
                && api.Targets[new IntPtr(800)].Visible;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool RecoveryFailuresRetainEvidence()
    {
        bool placement = RunFailureCase(failPlacement: true);
        bool show = RunFailureCase(failShow: true);
        bool transitions = RunFailureCase(failTransitions: true);
        return placement && show && transitions;
    }

    private static bool RunFailureCase(
        bool failPlacement = false,
        bool failShow = false,
        bool failTransitions = false)
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(900, 111, "failure.exe", 11101)));
            var api = new FakePendingApi(Target.For(900, 111, 1111, "failure.exe", "Modern", 11101))
            {
                FailPlacement = failPlacement,
                FailShow = failShow,
                FailTransitions = failTransitions,
            };
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool recovered = PendingRecoveryService.ExecuteRecovery(
                entry, CandidateFor(entry, "C001"), api, out _);
            return !recovered
                && File.Exists(path)
                && File.Exists(path + ".recovered")
                && !File.ReadAllText(path + ".recovered").Contains("presentation-restored", StringComparison.Ordinal)
                && api.Targets[new IntPtr(900)].RecoveryToken == IntPtr.Zero;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool GenerationChangesStopLaterMutations()
    {
        return RunGenerationCase(changeAfterSet: true, expectedPlacement: 0, expectedShow: 0, expectedTransitions: 0, expectedRemove: 0)
            && RunGenerationCase(changeAfterPlacement: true, expectedPlacement: 1, expectedShow: 0, expectedTransitions: 0, expectedRemove: 0)
            && RunGenerationCase(changeAfterShow: true, expectedPlacement: 1, expectedShow: 1, expectedTransitions: 0, expectedRemove: 0)
            && RunGenerationCase(changeAfterTransitions: true, expectedPlacement: 1, expectedShow: 1, expectedTransitions: 1, expectedRemove: 0);
    }

    private static bool RunGenerationCase(
        bool changeAfterSet = false,
        bool changeAfterPlacement = false,
        bool changeAfterShow = false,
        bool changeAfterTransitions = false,
        int expectedPlacement = 0,
        int expectedShow = 0,
        int expectedTransitions = 0,
        int expectedRemove = 0)
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1000, 121, "race.exe", 12101)));
            var api = new FakePendingApi(Target.For(1000, 121, 1121, "race.exe", "Modern", 12101))
            {
                ChangeAfterSetProperty = changeAfterSet,
                ChangeAfterPlacement = changeAfterPlacement,
                ChangeAfterShow = changeAfterShow,
                ChangeAfterTransitions = changeAfterTransitions,
            };
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool recovered = PendingRecoveryService.ExecuteRecovery(
                entry, CandidateFor(entry, "C001"), api, out _);
            return !recovered
                && api.PlacementCount == expectedPlacement
                && api.ShowCount == expectedShow
                && api.TransitionCount == expectedTransitions
                && api.RemovePropertyCount == expectedRemove
                && File.Exists(path);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool SuccessfulRecoveryRetiresOneEntryOnly()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(
                path,
                JournalJson(2,
                    EntryV2(1100, 131, "first.exe", 13101),
                    EntryV2(1101, 132, "sibling.exe", 13201)));
            var api = new FakePendingApi(
                Target.For(1100, 131, 1131, "first.exe", "Modern", 13101),
                Target.For(1101, 132, 1132, "sibling.exe", "Modern", 13201));
            PendingRecoveryCatalog before = PendingRecoveryService.Discover(root, api);
            PendingRecoveryEntry entry = before.Files.Single().Entries[0];
            PendingRecoveryCandidate candidate = CandidateFor(entry, "C001");
            using var input = new StringReader($"{entry.SessionId}\n{candidate.CandidateId}\nYES\n");
            using var output = new StringWriter();
            int result = PendingRecoveryService.RunInteractive(
                input, output, root, api, new[] { candidate });
            PendingRecoveryCatalog after = PendingRecoveryService.Discover(root, api);
            return result == 0
                && after.Files.Count == 1
                && after.Files[0].Entries.Count == 1
                && after.Files[0].Entries[0].Entry.Hwnd == 1101
                && File.Exists(path + ".recovered")
                && File.ReadAllText(path).Contains("unknown-root-field", StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool ResolvedEntryRetirementCanBeRetried()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1150, 135, "resolved.exe", 13501)));
            var api = new FakePendingApi(Target.For(1150, 135, 1135, "resolved.exe", "Modern", 13501));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            var ledger = new JsonObject
            {
                ["Resolutions"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["EntryFingerprint"] = entry.EntryFingerprint,
                        ["SchemaVersion"] = entry.Version,
                        ["ResolvedUtc"] = "2026-08-14T00:00:00+00:00",
                        ["Result"] = "presentation-restored",
                    },
                },
            };
            File.WriteAllText(path + ".recovered", ledger.ToJsonString());

            using var input = new StringReader(string.Empty);
            using var output = new StringWriter();
            int result = PendingRecoveryService.RunInteractive(input, output, root, api, Array.Empty<PendingRecoveryCandidate>());
            return result == 0
                && !File.Exists(path)
                && File.Exists(path + ".recovered")
                && api.MutationCount == 0;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool DuplicateEntriesDoNotCollapseSiblingEvidence()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            JsonObject duplicate = EntryV2(1140, 134, "duplicate.exe", 13401);
            File.WriteAllText(path, JournalJson(2, duplicate, JsonNode.Parse(duplicate.ToJsonString())!.AsObject()));
            var api = new FakePendingApi(Target.For(1140, 134, 1134, "duplicate.exe", "Modern", 13401));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries[0];
            PendingRecoveryCandidate candidate = CandidateFor(entry, "C001");
            using var input = new StringReader($"{entry.SessionId}\n{candidate.CandidateId}\nYES\n");
            using var output = new StringWriter();
            int result = PendingRecoveryService.RunInteractive(input, output, root, api, new[] { candidate });
            PendingRecoveryCatalog after = PendingRecoveryService.Discover(root, api);
            return result == 0
                && after.Files.Single().Entries.Count == 1
                && !after.Files.Single().Entries[0].AlreadyResolved
                && after.Files.Single().Entries[0].Status == "unverifiable-transaction";
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool ExistingTokensRefuseRecovery()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1200, 141, "token.exe", 14101)));
            var api = new FakePendingApi(Target.For(1200, 141, 1141, "token.exe", "Modern", 14101));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            PendingRecoveryCandidate candidate = CandidateFor(entry, "C001");
            api.Targets[new IntPtr(1200)].CaptureToken = new IntPtr(7001);
            bool captureRefused = !PendingRecoveryService.ExecuteRecovery(entry, candidate, api, out _)
                && api.MutationCount == 0;
            api.Targets[new IntPtr(1200)].CaptureToken = IntPtr.Zero;
            api.Targets[new IntPtr(1200)].RecoveryToken = new IntPtr(7002);
            bool recoveryRefused = !PendingRecoveryService.ExecuteRecovery(entry, candidate, api, out _)
                && api.MutationCount == 0;
            return captureRefused && recoveryRefused && File.Exists(path);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool DoNotRescueNeverResurrectsGuest()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1300, 151, "intentional.exe", 15101, doNotRescue: true)));
            var api = new FakePendingApi(Target.For(1300, 151, 1151, "intentional.exe", "Modern", 15101));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool recovered = PendingRecoveryService.ExecuteRecovery(entry, CandidateFor(entry, "C001"), api, out _);
            return recovered
                && entry.RecoveryMode == "v2-intentional-hide"
                && api.PlacementCount == 0
                && api.ShowCount == 0
                && api.TransitionCount == 1
                && api.RemovePropertyCount == 1
                && !api.Targets[new IntPtr(1300)].Visible;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool DoNotRescueWithoutRecordedTransitionStateStillCleansDwm()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1310, 152, "intentional-unrecorded.exe", 15201, doNotRescue: true, hasTransitions: false)));
            var api = new FakePendingApi(Target.For(1310, 152, 1152, "intentional-unrecorded.exe", "Modern", 15201));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool recovered = PendingRecoveryService.ExecuteRecovery(entry, CandidateFor(entry, "C001"), api, out _);
            return recovered
                && api.PlacementCount == 0
                && api.ShowCount == 0
                && api.TransitionCount == 1
                && !api.Targets[new IntPtr(1310)].Visible;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool RecoveryTransactionFaultsAreResumable()
    {
        string[] stages =
        {
            "after-prepared", "after-setprop", "after-placement", "after-visibility",
            "after-dwm", "after-native-complete", "after-remove-property",
        };
        foreach (string stage in stages)
        {
            string root = CreateRoot();
            try
            {
                string path = Path.Combine(root, "hidden-windows.json.pending");
                File.WriteAllText(path, JournalJson(2, EntryV2(1400, 161, "fault.exe", 16101)));
                var api = new FakePendingApi(Target.For(1400, 161, 1161, "fault.exe", "Modern", 16101));
                PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
                bool threw = false;
                try
                {
                    PendingRecoveryService.ExecuteRecovery(
                        entry,
                        CandidateFor(entry, "C001"),
                        api,
                        out _,
                        tokenFactory: () => new IntPtr(0x123456),
                        faultInjector: value => value == stage);
                }
                catch (Exception ex) when (ex.Message.Contains("Injected recovery fault", StringComparison.Ordinal))
                {
                    threw = true;
                }
                if (!threw || !File.Exists(path))
                    return false;

                PendingRecoveryEntry resumed = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
                bool resumedResult = PendingRecoveryService.ExecuteRecovery(
                    resumed,
                    CandidateFor(resumed, "C001"),
                    api,
                    out _,
                    tokenFactory: () => new IntPtr(0x123456));
                if (!resumedResult)
                    return false;
                if (stage == "after-native-complete" && api.PlacementCount != 1)
                    return false;
                if (stage == "after-remove-property" && api.RemovePropertyCount != 1)
                    return false;
            }
            finally
            {
                DeleteRoot(root);
            }
        }
        return true;
    }

    private static bool RecoveryTitlesAreTerminalSafe()
    {
        string title = "\u001B[31mRED\u001B]0;spoof\u0007\r\n\t\0\u007F\u0085\u2028\u2029 ordinary 😀 中文 "
            + new string('x', 140);
        string sanitized = PendingRecoveryService.SanitizeConsoleTitle(title);
        return sanitized.Length <= 96
            && !sanitized.Any(character => character == '\u001B'
                || character == '\r'
                || character == '\n'
                || character == '\0'
                || character == '\u007F'
                || (character >= '\u0080' && character <= '\u009F')
                || character == '\u2028'
                || character == '\u2029')
            && sanitized.Contains("RED", StringComparison.Ordinal)
            && sanitized.Contains("😀", StringComparison.Ordinal)
            && sanitized.Contains("中文", StringComparison.Ordinal);
    }

    private static bool InterruptedInteractiveRecoveryResumesAndRetires()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1450, 166, "interactive.exe", 16601)));
            var api = new FakePendingApi(Target.For(1450, 166, 1166, "interactive.exe", "Modern", 16601));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool injected = false;
            try
            {
                PendingRecoveryService.ExecuteRecovery(
                    entry,
                    CandidateFor(entry, "C001"),
                    api,
                    out _,
                    tokenFactory: () => new IntPtr(0x223344),
                    faultInjector: stage => stage == "after-setprop");
            }
            catch (Exception ex) when (ex.Message.Contains("Injected recovery fault", StringComparison.Ordinal))
            {
                injected = true;
            }
            PendingRecoveryEntry interrupted = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            PendingRecoveryCandidate candidate = CandidateFor(interrupted, "C001");
            using var input = new StringReader($"{interrupted.SessionId}\n{candidate.CandidateId}\nYES\n");
            using var output = new StringWriter();
            int result = PendingRecoveryService.RunInteractive(input, output, root, api, new[] { candidate });
            return injected
                && interrupted.Status == "interrupted-transaction"
                && result == 0
                && !File.Exists(path)
                && api.PlacementCount == 1
                && api.ShowCount == 1
                && api.TransitionCount == 1;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool RetirementFaultPreservesSiblingEvidence()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1460, 167, "retire.exe", 16701),
                EntryV2(1461, 168, "sibling.exe", 16801)));
            var api = new FakePendingApi(
                Target.For(1460, 167, 1167, "retire.exe", "Modern", 16701),
                Target.For(1461, 168, 1168, "sibling.exe", "Modern", 16801));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries[0];
            PendingRecoveryCandidate candidate = CandidateFor(entry, "C001");
            using var input = new StringReader($"{entry.SessionId}\n{candidate.CandidateId}\nYES\n");
            using var output = new StringWriter();
            bool injected = false;
            try
            {
                PendingRecoveryService.RunInteractive(
                    input,
                    output,
                    root,
                    api,
                    new[] { candidate },
                    faultInjector: stage => stage == "during-retirement");
            }
            catch (Exception ex) when (ex.Message.Contains("Injected recovery fault", StringComparison.Ordinal))
            {
                injected = true;
            }
            PendingRecoveryCatalog after = PendingRecoveryService.Discover(root, api);
            return injected
                && after.Files.Single().Entries.Count == 1
                && after.Files.Single().Entries[0].Entry.Hwnd == 1461
                && File.Exists(path + ".recovered")
                && File.ReadAllText(path + ".recovered").Contains(entry.EntryFingerprint, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool RandomRecoveryTokenIsDurableAndNonzero()
    {
        long first = RunAndReadRecoveryToken(1470, 169, "random-a.exe", 16901);
        long second = RunAndReadRecoveryToken(1471, 170, "random-b.exe", 17001);
        return first != 0 && second != 0 && first != second;
    }

    private static long RunAndReadRecoveryToken(long hwnd, uint pid, string exe, long start)
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(hwnd, pid, exe, start)));
            var api = new FakePendingApi(Target.For(hwnd, pid, pid + 1000, exe, "Modern", start));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            if (!PendingRecoveryService.ExecuteRecovery(entry, CandidateFor(entry, "C001"), api, out _))
                return 0;
            JsonNode? rootNode = JsonNode.Parse(File.ReadAllText(path + ".recovered"));
            return rootNode?["Transactions"]?[0]?["RecoveryToken"]?.GetValue<long>() ?? 0;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string JournalJson(int? version, params JsonObject[] entries)
    {
        var root = new JsonObject
        {
            ["unknown-root-field"] = "preserve-me",
        };
        if (version.HasValue)
            root["Version"] = version.Value;
        var array = new JsonArray();
        foreach (JsonObject entry in entries)
            array.Add(entry);
        root["Entries"] = array;
        return root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject EntryV1(long hwnd, uint pid, string exe)
        => new()
        {
            ["Hwnd"] = hwnd,
            ["Pid"] = pid,
            ["ExePath"] = exe,
        };

    private static JsonObject EntryV2(
        long hwnd,
        uint pid,
        string exe,
        long start,
        bool doNotRescue = false,
        bool hasTransitions = true)
        => new()
        {
            ["Hwnd"] = hwnd,
            ["Pid"] = pid,
            ["ExePath"] = exe,
            ["ClassName"] = "Modern",
            ["ProcessStartTimeUtcTicks"] = start,
            ["OriginallyVisible"] = true,
            ["HasOriginalPlacement"] = true,
            ["OriginalPlacementFlags"] = 0,
            ["OriginalShowCommand"] = NativeMethods.SW_SHOW,
            ["OriginalNormalLeft"] = 10,
            ["OriginalNormalTop"] = 20,
            ["OriginalNormalRight"] = 410,
            ["OriginalNormalBottom"] = 320,
            ["HasOriginalTransitionsState"] = hasTransitions,
            ["OriginalTransitionsDisabled"] = false,
            ["DoNotRescue"] = doNotRescue,
        };

    private static PendingRecoveryCandidate CandidateFor(PendingRecoveryEntry entry, string id)
        => new()
        {
            CandidateId = id,
            Hwnd = new IntPtr(entry.Entry.Hwnd),
            ProcessId = entry.Entry.Pid,
            WindowThreadId = entry.Entry.Pid + 1000,
            ExePath = entry.Entry.ExePath,
            ClassName = string.IsNullOrWhiteSpace(entry.Entry.ClassName) ? "Legacy" : entry.Entry.ClassName,
            ProcessStartTimeUtcTicks = entry.Entry.ProcessStartTimeUtcTicks,
            Title = "local test title",
        };

    private static PendingRecoveryCandidate CopyCandidate(
        PendingRecoveryCandidate source,
        string? id = null,
        IntPtr? hwnd = null,
        uint? processId = null,
        string? exePath = null,
        long? processStart = null)
        => new()
        {
            CandidateId = id ?? source.CandidateId,
            Hwnd = hwnd ?? source.Hwnd,
            ProcessId = processId ?? source.ProcessId,
            WindowThreadId = source.WindowThreadId,
            ExePath = exePath ?? source.ExePath,
            ClassName = source.ClassName,
            ProcessStartTimeUtcTicks = processStart ?? source.ProcessStartTimeUtcTicks,
            Title = source.Title,
            Visible = source.Visible,
            Iconic = source.Iconic,
        };

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "TabDock-pending-recovery-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch { }
    }

    private sealed class Target
    {
        public bool Exists { get; set; } = true;
        public IntPtr Hwnd { get; init; }
        public uint Pid { get; init; }
        public uint ThreadId { get; init; }
        public string Exe { get; init; } = string.Empty;
        public string ClassName { get; init; } = string.Empty;
        public long ProcessStartTicks { get; init; }
        public IntPtr CaptureToken { get; set; }
        public IntPtr RecoveryToken { get; set; }
        public bool Visible { get; set; }

        public static Target For(long hwnd, uint pid, uint thread, string exe, string className, long start)
            => new()
            {
                Hwnd = new IntPtr(hwnd),
                Pid = pid,
                ThreadId = thread,
                Exe = exe,
                ClassName = className,
                ProcessStartTicks = start,
            };
    }

    private sealed class FakePendingApi : IPendingRecoveryNativeApi
    {
        public FakePendingApi(params Target[] targets)
        {
            foreach (Target target in targets)
                Targets[target.Hwnd] = target;
        }

        public Dictionary<IntPtr, Target> Targets { get; } = new();
        public int MutationCount { get; private set; }
        public int PlacementCount { get; private set; }
        public int ShowCount { get; private set; }
        public int TransitionCount { get; private set; }
        public int RemovePropertyCount { get; private set; }
        public bool FailPlacement { get; set; }
        public bool FailShow { get; set; }
        public bool FailTransitions { get; set; }
        public bool ChangeAfterSetProperty { get; set; }
        public bool ChangeAfterPlacement { get; set; }
        public bool ChangeAfterShow { get; set; }
        public bool ChangeAfterTransitions { get; set; }

        public bool IsWindow(IntPtr hwnd) => Find(hwnd).Exists;
        public uint GetProcessId(IntPtr hwnd) => Find(hwnd).Pid;
        public uint GetWindowThreadId(IntPtr hwnd) => Find(hwnd).ThreadId;
        public string? GetProcessImagePath(uint pid) => Targets.Values.FirstOrDefault(target => target.Pid == pid)?.Exe;
        public string? GetClassName(IntPtr hwnd) => Find(hwnd).ClassName;
        public long GetProcessStartTimeUtcTicks(uint pid)
            => Targets.Values.FirstOrDefault(target => target.Pid == pid)?.ProcessStartTicks ?? 0;

        public IntPtr GetProperty(IntPtr hwnd, string propertyName)
        {
            Target target = Find(hwnd);
            return propertyName == NativeWindowIdentityApi.CaptureIdentityPropertyName
                ? target.CaptureToken
                : propertyName == PendingRecoveryService.TemporaryRecoveryPropertyName
                    ? target.RecoveryToken
                    : IntPtr.Zero;
        }

        public bool SetProperty(IntPtr hwnd, string propertyName, IntPtr value)
        {
            Target target = Find(hwnd);
            if (propertyName != PendingRecoveryService.TemporaryRecoveryPropertyName
                || target.RecoveryToken != IntPtr.Zero)
                return false;
            target.RecoveryToken = value;
            if (ChangeAfterSetProperty)
                ChangeGeneration(target);
            return true;
        }

        public bool RemoveProperty(IntPtr hwnd, string propertyName, IntPtr expectedValue)
        {
            Target target = Find(hwnd);
            if (propertyName != PendingRecoveryService.TemporaryRecoveryPropertyName
                || target.RecoveryToken != expectedValue)
                return false;
            RemovePropertyCount++;
            target.RecoveryToken = IntPtr.Zero;
            return true;
        }

        public bool SetWindowPlacement(IntPtr hwnd, ref NativeMethods.WINDOWPLACEMENT placement)
        {
            MutationCount++;
            PlacementCount++;
            if (ChangeAfterPlacement)
                ChangeGeneration(Find(hwnd));
            return !FailPlacement;
        }

        public bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags)
        {
            MutationCount++;
            PlacementCount++;
            return !FailPlacement;
        }

        public bool ShowWindow(IntPtr hwnd, int command)
        {
            MutationCount++;
            ShowCount++;
            Target target = Find(hwnd);
            bool previous = target.Visible;
            if (FailShow)
                return previous;
            target.Visible = command != NativeMethods.SW_HIDE;
            if (ChangeAfterShow)
                ChangeGeneration(target);
            return previous;
        }

        public bool IsWindowVisible(IntPtr hwnd) => Find(hwnd).Visible;

        public int SetTransitionsDisabled(IntPtr hwnd, int value)
        {
            MutationCount++;
            TransitionCount++;
            if (ChangeAfterTransitions)
                ChangeGeneration(Find(hwnd));
            return FailTransitions ? -1 : 0;
        }

        private Target Find(IntPtr hwnd)
            => Targets.TryGetValue(hwnd, out Target? target)
                ? target
                : throw new InvalidOperationException("unknown test HWND");

        private static void ChangeGeneration(Target target)
            => target.RecoveryToken = new IntPtr(0x7FFF);
    }
}
