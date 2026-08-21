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
        Check(MalformedRecoveryLedgerIsRetained());
        Check(HistoricalFieldsMustMatch());
        Check(UserCancellationDoesNotMutate());
        Check(V1RecoveryIsVisibilityOnly());
        Check(V2RecoveryRestoresPresentation());
        Check(RecoveryFailuresRetainEvidence());
        Check(GenerationChangesStopLaterMutations());
        Check(SuccessfulRecoveryRetiresOneEntryOnly());
        Check(InterruptedSiblingSurvivesSiblingRetirement());
        Check(InterruptedFirstSiblingSurvivesReverseRetirement());
        Check(ThreeSiblingIndicesRemainStable());
        Check(DuplicateEntriesDoNotCollapseSiblingEvidence());
        Check(OldRewrittenLedgerRebindsOnlyWhenUnique());
        Check(OldRewrittenTokenRemovedRebindConvergesAndRetires());
        Check(OldRewrittenNativeRecoveryCompleteRebindConverges());
        Check(OldRewrittenLedgerWithDuplicateSurvivorsFailsClosed());
        Check(OldRewrittenLedgerWithMultipleCandidatesFailsClosed());
        Check(OldRewrittenForeignTokenFailsClosed());
        Check(ResolvedEntryRetirementCanBeRetried());
        Check(ExistingTokensRefuseRecovery());
        Check(DoNotRescueNeverResurrectsGuest());
        Check(DoNotRescueWithoutRecordedTransitionStateStillCleansDwm());
        Check(RecoveryTransactionFaultsAreResumable());
        Check(ResolutionMarkerAndFinalRetirementFaultsAreResumable());
        Check(CompletedRecoveryIdentityCasesAreDiskOnly());
        Check(InterruptedInteractiveRecoveryResumesAndRetires());
        Check(RetirementFaultPreservesSiblingEvidence());
        Check(RandomRecoveryTokenIsDurableAndNonzero());
        Check(RecoveryTitlesAreTerminalSafe());
        Check(RecoveryConsoleFieldsAreTerminalSafe());
        Check(GenerationIdentitySeparatesRecoveryGenerations());
        Check(SameGenerationReplayResolvesAsDuplicate());
        Check(LegacyPendingWithoutInstanceIdStillConsumesAndRecords());
        Check(InterruptedTransactionAcrossGenerationsResumes());
        Check(UnreadablePendingDoesNotBlockOtherRetirement());
        Check(OrphanedTemporaryFilesAreSweptByAge());
        Check(RetiredLedgerCompactionBoundsHistory());
        Check(AbandonPathRequiresVerifiablyGoneTarget());
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

    private static bool MalformedRecoveryLedgerIsRetained()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(450, 65, "ledger-corrupt.exe", 6501)));
            File.WriteAllText(path + ".recovered", "{");
            var api = new FakePendingApi(Target.For(450, 65, 1065, "ledger-corrupt.exe", "Modern", 6501));
            PendingRecoveryCatalog catalog = PendingRecoveryService.Discover(root, api);
            int result = PendingRecoveryService.RunInteractive(
                new StringReader(string.Empty),
                new StringWriter(),
                root,
                api,
                Array.Empty<PendingRecoveryCandidate>());
            return catalog.Files.Single().Status.StartsWith("unreadable (recovery-ledger):", StringComparison.Ordinal)
                && result == 2
                && File.Exists(path)
                && File.Exists(path + ".recovered")
                && api.MutationCount == 0;
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
                && after.Files[0].Entries.Count == 2
                && after.Files[0].Entries.Single(entry => entry.Entry.Hwnd == 1100).AlreadyResolved
                && !after.Files[0].Entries.Single(entry => entry.Entry.Hwnd == 1101).AlreadyResolved
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
                && after.Files.Single().Entries.Count == 2
                && after.Files.Single().Entries.Single(entry => entry.EntryIndex == 0).AlreadyResolved
                && !after.Files.Single().Entries.Single(entry => entry.EntryIndex == 1).AlreadyResolved
                && after.Files.Single().Entries.Single(entry => entry.EntryIndex == 1).Status == "potentially-recoverable"
                && after.Files.Single().Entries.Single(entry => entry.EntryIndex == 1).Transaction == null
                && File.ReadAllText(path).Contains("unknown-root-field", StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool InterruptedSiblingSurvivesSiblingRetirement()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1160, 136, "first-sibling.exe", 13601),
                EntryV2(1161, 137, "interrupted-sibling.exe", 13701)));
            var api = new FakePendingApi(
                Target.For(1160, 136, 1136, "first-sibling.exe", "Modern", 13601),
                Target.For(1161, 137, 1137, "interrupted-sibling.exe", "Modern", 13701));
            PendingRecoveryEntry interrupted = PendingRecoveryService.Discover(root, api).Files.Single().Entries[1];
            bool prepared = FaultAfterStage(interrupted, api, "after-setprop", 0x5161);
            PendingRecoveryEntry first = PendingRecoveryService.Discover(root, api).Files.Single().Entries[0];
            int result = RunInteractiveFor(first, api, root);
            PendingRecoveryEntry[] after = PendingRecoveryService.Discover(root, api).Files.Single().Entries.ToArray();
            PendingRecoveryEntry survivor = after.Single(entry => entry.Entry.Hwnd == 1161);
            bool supported = prepared
                && result == 0
                && File.Exists(path)
                && survivor.EntryIndex == 1
                && survivor.Status == "interrupted-transaction"
                && survivor.Transaction != null
                && survivor.Transaction.RecoveryToken == new IntPtr(0x5161).ToInt64()
                && api.Targets[new IntPtr(1161)].RecoveryToken != IntPtr.Zero;
            int resumed = supported ? RunInteractiveFor(survivor, api, root) : 2;
            return supported && resumed == 0 && !File.Exists(path);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool InterruptedFirstSiblingSurvivesReverseRetirement()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1170, 138, "interrupted-first.exe", 13801),
                EntryV2(1171, 139, "second-sibling.exe", 13901)));
            var api = new FakePendingApi(
                Target.For(1170, 138, 1138, "interrupted-first.exe", "Modern", 13801),
                Target.For(1171, 139, 1139, "second-sibling.exe", "Modern", 13901));
            PendingRecoveryEntry interrupted = PendingRecoveryService.Discover(root, api).Files.Single().Entries[0];
            bool prepared = FaultAfterStage(interrupted, api, "after-setprop", 0x5170);
            PendingRecoveryEntry second = PendingRecoveryService.Discover(root, api).Files.Single().Entries[1];
            int result = RunInteractiveFor(second, api, root);
            PendingRecoveryEntry[] after = PendingRecoveryService.Discover(root, api).Files.Single().Entries.ToArray();
            PendingRecoveryEntry survivor = after.Single(entry => entry.Entry.Hwnd == 1170);
            bool supported = prepared
                && result == 0
                && File.Exists(path)
                && survivor.EntryIndex == 0
                && survivor.Status == "interrupted-transaction"
                && survivor.Transaction != null
                && survivor.Transaction.RecoveryToken == new IntPtr(0x5170).ToInt64();
            int resumed = supported ? RunInteractiveFor(survivor, api, root) : 2;
            return supported && resumed == 0 && !File.Exists(path);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool ThreeSiblingIndicesRemainStable()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1180, 140, "first-of-three.exe", 14001),
                EntryV2(1181, 141, "middle-of-three.exe", 14101),
                EntryV2(1182, 142, "last-of-three.exe", 14201)));
            string before = File.ReadAllText(path);
            var api = new FakePendingApi(
                Target.For(1180, 140, 1140, "first-of-three.exe", "Modern", 14001),
                Target.For(1181, 141, 1141, "middle-of-three.exe", "Modern", 14101),
                Target.For(1182, 142, 1142, "last-of-three.exe", "Modern", 14201));
            PendingRecoveryEntry middle = PendingRecoveryService.Discover(root, api).Files.Single().Entries[1];
            int result = RunInteractiveFor(middle, api, root);
            PendingRecoveryEntry[] after = PendingRecoveryService.Discover(root, api).Files.Single().Entries.ToArray();
            return result == 0
                && before == File.ReadAllText(path)
                && after.Length == 3
                && after.Single(entry => entry.Entry.Hwnd == 1180).EntryIndex == 0
                && after.Single(entry => entry.Entry.Hwnd == 1181).AlreadyResolved
                && after.Single(entry => entry.Entry.Hwnd == 1182).EntryIndex == 2;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool OldRewrittenLedgerRebindsOnlyWhenUnique()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1190, 143, "removed-before-rebind.exe", 14301),
                EntryV2(1191, 144, "unique-rebind.exe", 14401),
                EntryV2(1192, 145, "other-rebind.exe", 14501)));
            var api = new FakePendingApi(
                Target.For(1190, 143, 1143, "removed-before-rebind.exe", "Modern", 14301),
                Target.For(1191, 144, 1144, "unique-rebind.exe", "Modern", 14401),
                Target.For(1192, 145, 1145, "other-rebind.exe", "Modern", 14501));
            PendingRecoveryEntry oldEntry = PendingRecoveryService.Discover(root, api).Files.Single().Entries[1];
            if (!FaultAfterStage(oldEntry, api, "after-setprop", 0x5191))
                return false;
            RemoveFirstPendingEntry(path);
            PendingRecoveryEntry rebound = PendingRecoveryService.Discover(root, api).Files.Single().Entries[0];
            int result = RunInteractiveFor(rebound, api, root);
            PendingRecoveryEntry[] after = PendingRecoveryService.Discover(root, api).Files.Single().Entries.ToArray();
            bool ledgerConverged = LedgerHasSingleRetiredCurrentTransaction(
                path + ".recovered",
                rebound.SourceFileSha256,
                rebound.EntryIndex,
                rebound.EntryFingerprint,
                new IntPtr(0x5191).ToInt64());
            return rebound.TransactionNeedsRebind
                && rebound.Status == "interrupted-transaction"
                && result == 0
                && File.Exists(path)
                && File.Exists(path + ".recovered")
                && after.Single(entry => entry.Entry.Hwnd == 1191).AlreadyResolved
                && after.Single(entry => entry.Entry.Hwnd == 1192).EntryIndex == 1
                && api.Targets[new IntPtr(1191)].RecoveryToken == IntPtr.Zero
                && ledgerConverged;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool OldRewrittenTokenRemovedRebindConvergesAndRetires()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1210, 147, "removed-before-token-removed.exe", 14701),
                EntryV2(1211, 148, "token-removed-rebind.exe", 14801)));
            var api = new FakePendingApi(
                Target.For(1210, 147, 1147, "removed-before-token-removed.exe", "Modern", 14701),
                Target.For(1211, 148, 1148, "token-removed-rebind.exe", "Modern", 14801));
            PendingRecoveryEntry oldEntry = PendingRecoveryService.Discover(root, api).Files.Single().Entries[1];
            const long recoveryToken = 0x5301;
            if (!FaultAfterStage(oldEntry, api, "after-native-complete", recoveryToken))
                return false;

            // The old implementation had already removed the native token;
            // preserve that exact durable boundary while rewriting the source
            // in the same way the legacy implementation removed a sibling.
            api.Targets[new IntPtr(1211)].RecoveryToken = IntPtr.Zero;
            int placementBefore = api.PlacementCount;
            int showBefore = api.ShowCount;
            int transitionBefore = api.TransitionCount;
            RemoveFirstPendingEntry(path);
            if (!SetLedgerTransactionPhase(path + ".recovered", PendingRecoveryService.RecoveryPhase.TokenRemoved))
                return false;

            PendingRecoveryEntry rebound = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool literalFixture = rebound.TransactionNeedsRebind
                && rebound.Transaction?.Phase == PendingRecoveryService.RecoveryPhase.TokenRemoved
                && rebound.EntryIndex == 0
                && oldEntry.EntryIndex == 1
                && !string.Equals(oldEntry.SourceFileSha256, rebound.SourceFileSha256, StringComparison.OrdinalIgnoreCase);
            int result = RunInteractiveFor(rebound, api, root);
            bool noNativeRepeat = api.PlacementCount == placementBefore
                && api.ShowCount == showBefore
                && api.TransitionCount == transitionBefore
                && api.RemovePropertyCount == 0
                && api.Targets[new IntPtr(1211)].RecoveryToken == IntPtr.Zero;
            bool ledgerConverged = LedgerHasSingleRetiredCurrentTransaction(
                path + ".recovered",
                rebound.SourceFileSha256,
                rebound.EntryIndex,
                rebound.EntryFingerprint,
                recoveryToken);
            int repeated = PendingRecoveryService.RunInteractive(
                new StringReader(string.Empty),
                new StringWriter(),
                root,
                api,
                Array.Empty<PendingRecoveryCandidate>());
            return literalFixture
                && result == 0
                && noNativeRepeat
                && ledgerConverged
                && !File.Exists(path)
                && repeated == 0
                && PendingRecoveryService.Discover(root, api).Files.Count == 0;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool OldRewrittenNativeRecoveryCompleteRebindConverges()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1220, 149, "removed-before-native-complete.exe", 14901),
                EntryV2(1221, 150, "native-complete-rebind.exe", 15001)));
            var api = new FakePendingApi(
                Target.For(1220, 149, 1149, "removed-before-native-complete.exe", "Modern", 14901),
                Target.For(1221, 150, 1150, "native-complete-rebind.exe", "Modern", 15001));
            PendingRecoveryEntry oldEntry = PendingRecoveryService.Discover(root, api).Files.Single().Entries[1];
            const long recoveryToken = 0x5302;
            if (!FaultAfterStage(oldEntry, api, "after-native-complete", recoveryToken))
                return false;
            int placementBefore = api.PlacementCount;
            int showBefore = api.ShowCount;
            int transitionBefore = api.TransitionCount;
            RemoveFirstPendingEntry(path);
            PendingRecoveryEntry rebound = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool sourceChanged = rebound.TransactionNeedsRebind
                && rebound.Transaction?.Phase == PendingRecoveryService.RecoveryPhase.NativeRecoveryComplete
                && !string.Equals(oldEntry.SourceFileSha256, rebound.SourceFileSha256, StringComparison.OrdinalIgnoreCase);
            int result = RunInteractiveFor(rebound, api, root);
            bool nativeOnlyCleanup = api.PlacementCount == placementBefore
                && api.ShowCount == showBefore
                && api.TransitionCount == transitionBefore
                && api.RemovePropertyCount == 1
                && api.Targets[new IntPtr(1221)].RecoveryToken == IntPtr.Zero;
            bool ledgerConverged = LedgerHasSingleRetiredCurrentTransaction(
                path + ".recovered",
                rebound.SourceFileSha256,
                rebound.EntryIndex,
                rebound.EntryFingerprint,
                recoveryToken);
            return sourceChanged
                && result == 0
                && nativeOnlyCleanup
                && ledgerConverged
                && !File.Exists(path);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool OldRewrittenLedgerWithDuplicateSurvivorsFailsClosed()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            JsonObject duplicate = EntryV2(1201, 146, "duplicate-rebind.exe", 14601);
            File.WriteAllText(path, JournalJson(2,
                JsonNode.Parse(duplicate.ToJsonString())!.AsObject(),
                JsonNode.Parse(duplicate.ToJsonString())!.AsObject(),
                JsonNode.Parse(duplicate.ToJsonString())!.AsObject()));
            var api = new FakePendingApi(Target.For(1201, 146, 1146, "duplicate-rebind.exe", "Modern", 14601));
            PendingRecoveryEntry oldEntry = PendingRecoveryService.Discover(root, api).Files.Single().Entries[0];
            if (!FaultAfterStage(oldEntry, api, "after-setprop", 0x5201))
                return false;
            RemoveFirstPendingEntry(path);
            PendingRecoveryEntry[] survivors = PendingRecoveryService.Discover(root, api).Files.Single().Entries.ToArray();
            return survivors.Length == 2
                && survivors.All(entry => entry.Status == "unverifiable-transaction")
                && survivors.All(entry => entry.Transaction == null)
                && api.Targets[new IntPtr(1201)].RecoveryToken == new IntPtr(0x5201);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool OldRewrittenLedgerWithMultipleCandidatesFailsClosed()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1230, 151, "multiple-legacy-candidates.exe", 15101)));
            var api = new FakePendingApi(Target.For(1230, 151, 1151, "multiple-legacy-candidates.exe", "Modern", 15101));
            PendingRecoveryEntry oldEntry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            const long recoveryToken = 0x5303;
            if (!FaultAfterStage(oldEntry, api, "after-setprop", recoveryToken)
                || !DuplicateLedgerTransaction(path + ".recovered", "legacy-second-sha", 7))
                return false;
            RewritePendingSource(path);
            PendingRecoveryEntry ambiguous = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            int result = RunInteractiveFor(ambiguous, api, root);
            JsonObject ledger = JsonNode.Parse(File.ReadAllText(path + ".recovered"))!.AsObject();
            int transactionCount = ledger["Transactions"]?.AsArray().Count ?? 0;
            return ambiguous.TransactionAmbiguous
                && ambiguous.Transaction == null
                && ambiguous.Status == "unverifiable-transaction"
                && result == 2
                && transactionCount == 2
                && api.PlacementCount == 0
                && api.ShowCount == 0
                && api.TransitionCount == 0
                && api.Targets[new IntPtr(1230)].RecoveryToken == new IntPtr(recoveryToken);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool OldRewrittenForeignTokenFailsClosed()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1240, 152, "foreign-recovery-token.exe", 15201)));
            var api = new FakePendingApi(Target.For(1240, 152, 1152, "foreign-recovery-token.exe", "Modern", 15201));
            PendingRecoveryEntry oldEntry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            const long recoveryToken = 0x5304;
            if (!FaultAfterStage(oldEntry, api, "after-native-complete", recoveryToken))
                return false;
            string oldSourceSha = oldEntry.SourceFileSha256;
            if (!SetLedgerTransactionPhase(path + ".recovered", PendingRecoveryService.RecoveryPhase.TokenRemoved))
                return false;
            api.Targets[new IntPtr(1240)].RecoveryToken = new IntPtr(0x5BAD);
            RewritePendingSource(path);
            PendingRecoveryEntry rebound = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            int result = PendingRecoveryService.RunInteractive(
                new StringReader(string.Empty),
                new StringWriter(),
                root,
                api,
                Array.Empty<PendingRecoveryCandidate>());
            JsonObject ledger = JsonNode.Parse(File.ReadAllText(path + ".recovered"))!.AsObject();
            JsonObject transaction = ledger["Transactions"]!.AsArray().Single()!.AsObject();
            return rebound.TransactionNeedsRebind
                && result == 2
                && api.Targets[new IntPtr(1240)].RecoveryToken == new IntPtr(0x5BAD)
                && api.RemovePropertyCount == 0
                && api.PlacementCount == 1
                && api.ShowCount == 1
                && api.TransitionCount == 1
                && string.Equals(transaction["SourceFileSha256"]?.GetValue<string>(), oldSourceSha, StringComparison.OrdinalIgnoreCase)
                && transaction["Phase"]?.GetValue<string>() == PendingRecoveryService.RecoveryPhase.TokenRemoved
                && File.Exists(path);
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

    private static bool RecoveryConsoleFieldsAreTerminalSafe()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            string hostileExe = "C:\\Apps\\bad\u001B[31m.exe\r\n";
            string hostileClass = "Class\u001B]0;spoof\u0007\t\u0085";
            JsonObject entryJson = EntryV2(1480, 190, hostileExe, 19001);
            entryJson["ClassName"] = hostileClass;
            File.WriteAllText(path, JournalJson(2, entryJson));
            var api = new FakePendingApi(Target.For(1480, 190, 1190, hostileExe, hostileClass, 19001));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            PendingRecoveryCandidate candidate = new()
            {
                CandidateId = "C001",
                Hwnd = new IntPtr(1480),
                ProcessId = 190,
                WindowThreadId = 1190,
                ExePath = hostileExe,
                ClassName = hostileClass,
                ProcessStartTimeUtcTicks = 19001,
                Title = "title\u001B[2J\u001B]52;c;secret\u0007\r\n\t\u0080\u007F\u2028\u2029 😀 中文",
            };
            using var output = new StringWriter();
            using var input = new StringReader("P01-E001\nC001\nNO\n");
            int result = PendingRecoveryService.RunInteractive(input, output, root, api, new[] { candidate });
            string rendered = output.ToString();
            string displayFields = rendered.Replace("\r\n", string.Empty, StringComparison.Ordinal)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
            bool safe = displayFields.EnumerateRunes().All(rune =>
            {
                int scalar = rune.Value;
                return scalar > 0x1F
                    && scalar != 0x7F
                    && (scalar < 0x80 || scalar > 0x9F)
                    && scalar != 0x2028
                    && scalar != 0x2029;
            });
            return result == 1
                && safe
                && rendered.Contains("😀", StringComparison.Ordinal)
                && rendered.Contains("中文", StringComparison.Ordinal)
                && File.Exists(path)
                && api.MutationCount == 0;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool ResolutionMarkerAndFinalRetirementFaultsAreResumable()
    {
        bool marker = RunPostResolutionFaultCase("after-resolution-marker", expectPendingAfterFault: true);
        bool finalRetirement = RunPostResolutionFaultCase("after-retirement", expectPendingAfterFault: false);
        return marker && finalRetirement;
    }

    private static bool RunPostResolutionFaultCase(string stage, bool expectPendingAfterFault)
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1420, 182, "post-resolution.exe", 18201)));
            var api = new FakePendingApi(Target.For(1420, 182, 1182, "post-resolution.exe", "Modern", 18201));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool injected = false;
            try
            {
                RunInteractiveForWithFault(entry, api, root, stage);
            }
            catch (Exception ex) when (ex.Message.Contains("Injected recovery fault", StringComparison.Ordinal))
            {
                injected = true;
            }

            bool pendingAfterFault = File.Exists(path);
            int retry = PendingRecoveryService.RunInteractive(
                new StringReader(string.Empty),
                new StringWriter(),
                root,
                api,
                Array.Empty<PendingRecoveryCandidate>());
            return injected
                && pendingAfterFault == expectPendingAfterFault
                && retry == 0
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

    private static void RunInteractiveForWithFault(
        PendingRecoveryEntry entry,
        FakePendingApi api,
        string root,
        string stage)
    {
        PendingRecoveryCandidate candidate = CandidateFor(entry, "C001");
        using var input = new StringReader($"{entry.SessionId}\n{candidate.CandidateId}\nYES\n");
        using var output = new StringWriter();
        PendingRecoveryService.RunInteractive(
            input,
            output,
            root,
            api,
            new[] { candidate },
            faultInjector: value => value == stage);
    }

    private static bool CompletedRecoveryIdentityCasesAreDiskOnly()
    {
        bool exactToken = RunCompletedCleanupCase(
            1430,
            target => { },
            expectedResult: 0,
            expectedFile: false,
            expectedRemoveCount: 1);
        bool exactTokenAlreadyGone = RunCompletedCleanupCase(
            1431,
            target => target.RecoveryToken = IntPtr.Zero,
            expectedResult: 0,
            expectedFile: false,
            expectedRemoveCount: 0);
        bool destroyed = RunCompletedCleanupCase(
            1432,
            target => target.Exists = false,
            expectedResult: 0,
            expectedFile: false,
            expectedRemoveCount: 0);
        bool replacedByPid = RunCompletedCleanupCase(
            1433,
            target =>
            {
                target.Pid = 2433;
                target.ThreadId = 3433;
                target.Exe = "replacement.exe";
                target.RecoveryToken = new IntPtr(0x7333);
            },
            expectedResult: 0,
            expectedFile: false,
            expectedRemoveCount: 0,
            expectedRecoveryToken: new IntPtr(0x7333));
        bool replacedByProcessStart = RunCompletedCleanupCase(
            1434,
            target =>
            {
                target.ProcessStartTicks++;
                target.RecoveryToken = new IntPtr(0x7334);
            },
            expectedResult: 0,
            expectedFile: false,
            expectedRemoveCount: 0,
            expectedRecoveryToken: new IntPtr(0x7334));
        bool unverifiable = RunCompletedCleanupCase(
            1435,
            target => target.ProcessStartTicks = 0,
            expectedResult: 2,
            expectedFile: true,
            expectedRemoveCount: 0);
        return exactToken
            && exactTokenAlreadyGone
            && destroyed
            && replacedByPid
            && replacedByProcessStart
            && unverifiable;
    }

    private static bool RunCompletedCleanupCase(
        long hwnd,
        Action<Target> mutate,
        int expectedResult,
        bool expectedFile,
        int expectedRemoveCount,
        IntPtr? expectedRecoveryToken = null)
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            uint pid = (uint)(hwnd - 1000);
            long start = hwnd + 10000;
            string exe = "complete-" + hwnd + ".exe";
            File.WriteAllText(path, JournalJson(2, EntryV2(hwnd, pid, exe, start)));
            var api = new FakePendingApi(Target.For(hwnd, pid, pid + 1000, exe, "Modern", start));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool interrupted = FaultAfterStage(entry, api, "after-native-complete", 0x7000 + hwnd);
            Target target = api.Targets[new IntPtr(hwnd)];
            mutate(target);
            int result = PendingRecoveryService.RunInteractive(
                new StringReader(string.Empty),
                new StringWriter(),
                root,
                api,
                Array.Empty<PendingRecoveryCandidate>());
            return interrupted
                && result == expectedResult
                && File.Exists(path) == expectedFile
                && api.RemovePropertyCount == expectedRemoveCount
                && api.PlacementCount == 1
                && api.ShowCount == 1
                && api.TransitionCount == 1
                && (!expectedRecoveryToken.HasValue || target.RecoveryToken == expectedRecoveryToken.Value);
        }
        finally
        {
            DeleteRoot(root);
        }
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
                && after.Files.Single().Entries.Count == 2
                && after.Files.Single().Entries.Single(entry => entry.Entry.Hwnd == 1460).AlreadyResolved
                && after.Files.Single().Entries.Single(entry => entry.Entry.Hwnd == 1461).EntryIndex == 1
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

    private static bool GenerationIdentitySeparatesRecoveryGenerations()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            var api = new FakePendingApi(Target.For(1500, 171, 1171, "gen.exe", "Modern", 17101));
            File.WriteAllText(
                path,
                JournalJson(2, "11111111-1111-1111-1111-111111111111", EntryV2(1500, 171, "gen.exe", 17101)));
            PendingRecoveryEntry first = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool firstCompleted = RunInteractiveFor(first, api, root) == 0 && !File.Exists(path);

            // Generation B: byte-identical JSON under a DIFFERENT
            // SourceInstanceId. It must not match generation A's resolution.
            File.WriteAllText(
                path,
                JournalJson(2, "22222222-2222-2222-2222-222222222222", EntryV2(1500, 171, "gen.exe", 17101)));
            PendingRecoveryEntry second = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool stillRecoverable = !second.AlreadyResolved
                && second.Status == "potentially-recoverable"
                && second.Transaction == null;
            bool secondCompleted = stillRecoverable && RunInteractiveFor(second, api, root) == 0 && !File.Exists(path);
            return firstCompleted && stillRecoverable && secondCompleted;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool SameGenerationReplayResolvesAsDuplicate()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            var api = new FakePendingApi(Target.For(1510, 172, 1172, "replay.exe", "Modern", 17201));
            string body = JournalJson(2, "33333333-3333-3333-3333-333333333333", EntryV2(1510, 172, "replay.exe", 17201));
            File.WriteAllText(path, body);
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool firstCompleted = RunInteractiveFor(entry, api, root) == 0 && !File.Exists(path);

            // Identical bytes AND identical SourceInstanceId: dedup within one
            // generation still resolves as already handled.
            File.WriteAllText(path, body);
            PendingRecoveryEntry replay = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            int result = PendingRecoveryService.RunInteractive(
                new StringReader(string.Empty),
                new StringWriter(),
                root,
                api,
                Array.Empty<PendingRecoveryCandidate>());
            return firstCompleted
                && replay.AlreadyResolved
                && result == 0
                && !File.Exists(path)
                && api.PlacementCount == 1;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool LegacyPendingWithoutInstanceIdStillConsumesAndRecords()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            var api = new FakePendingApi(Target.For(1525, 173, 1173, "legacy-migration.exe", "Modern", 17301));
            string body = JournalJson(2, EntryV2(1525, 173, "legacy-migration.exe", 17301));
            File.WriteAllText(path, body);
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool firstCompleted = RunInteractiveFor(entry, api, root) == 0 && !File.Exists(path);

            // Pre-upgrade evidence keeps its bounded legacy matching: an
            // identical no-id replay is consumed by the fingerprint fallback,
            // and the ledger stored whatever identity was available (null).
            File.WriteAllText(path, body);
            PendingRecoveryEntry replay = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            JsonObject ledger = JsonNode.Parse(File.ReadAllText(path + ".recovered"))!.AsObject();
            bool resolutionStoredWithoutInstance = ledger["Resolutions"]!.AsArray()
                .All(item => item!.AsObject()["SourceInstanceId"] == null
                    || string.IsNullOrEmpty(item.AsObject()["SourceInstanceId"]?.GetValue<string>()));
            int result = PendingRecoveryService.RunInteractive(
                new StringReader(string.Empty),
                new StringWriter(),
                root,
                api,
                Array.Empty<PendingRecoveryCandidate>());
            return firstCompleted
                && resolutionStoredWithoutInstance
                && replay.AlreadyResolved
                && result == 0
                && !File.Exists(path);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool InterruptedTransactionAcrossGenerationsResumes()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            var api = new FakePendingApi(Target.For(1530, 174, 1174, "cross-gen.exe", "Modern", 17401));
            File.WriteAllText(
                path,
                JournalJson(2, "44444444-4444-4444-4444-444444444444", EntryV2(1530, 174, "cross-gen.exe", 17401)));
            PendingRecoveryEntry first = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool firstCompleted = RunInteractiveFor(first, api, root) == 0 && !File.Exists(path);

            File.WriteAllText(
                path,
                JournalJson(2, "55555555-5555-5555-5555-555555555555", EntryV2(1530, 174, "cross-gen.exe", 17401)));
            PendingRecoveryEntry second = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool interrupted = FaultAfterStage(second, api, "after-setprop", 0x5601);
            PendingRecoveryEntry resumed = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool transactionSurvived = interrupted
                && !resumed.AlreadyResolved
                && resumed.Status == "interrupted-transaction"
                && resumed.Transaction != null;
            int result = transactionSurvived ? RunInteractiveFor(resumed, api, root) : 2;
            return firstCompleted && transactionSurvived && result == 0 && !File.Exists(path);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool UnreadablePendingDoesNotBlockOtherRetirement()
    {
        string root = CreateRoot();
        try
        {
            string readablePath = Path.Combine(root, "hidden-windows.json.pending");
            string unreadablePath = Path.Combine(root, "hidden-windows.json.pending.002");
            File.WriteAllText(readablePath, JournalJson(2, EntryV2(1540, 175, "readable.exe", 17501)));
            File.WriteAllText(unreadablePath, "{not-json");
            var api = new FakePendingApi(Target.For(1540, 175, 1175, "readable.exe", "Modern", 17501));
            PendingRecoveryFile readableFile = PendingRecoveryService.Discover(root, api)
                .Files.Single(file => file.FileName == "hidden-windows.json.pending");
            bool interrupted = FaultAfterStage(readableFile.Entries.Single(), api, "after-native-complete", 0x5701);

            // The unreadable sibling must not short-circuit disk-only cleanup
            // of the other completed evidence; only new supervised selection
            // stays fail-closed (exit code 2).
            int result = PendingRecoveryService.RunInteractive(
                new StringReader(string.Empty),
                new StringWriter(),
                root,
                api,
                Array.Empty<PendingRecoveryCandidate>());
            return interrupted
                && result == 2
                && !File.Exists(readablePath)
                && File.Exists(unreadablePath)
                && File.Exists(readablePath + ".recovered")
                && api.RemovePropertyCount == 1
                && api.PlacementCount == 1;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool OrphanedTemporaryFilesAreSweptByAge()
    {
        string root = CreateRoot();
        try
        {
            string pendingPath = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(pendingPath, JournalJson(2, EntryV2(1550, 176, "sweep.exe", 17601)));
            string staleTmp = Path.Combine(root, "hidden-windows.json.pending.recovered.tmp");
            File.WriteAllText(staleTmp, "{ partial write");
            File.SetLastWriteTimeUtc(staleTmp, DateTime.UtcNow - TimeSpan.FromHours(25));
            string freshTmp = Path.Combine(root, "hidden-windows.json.pending.001.recovered.tmp");
            File.WriteAllText(freshTmp, "{ partial write");

            PendingRecoveryCatalog catalog = PendingRecoveryService.Discover(root, new FakePendingApi());
            return catalog.Error == null
                && !File.Exists(staleTmp)
                && File.Exists(freshTmp)
                && File.Exists(pendingPath);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool RetiredLedgerCompactionBoundsHistory()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1560, 177, "history-sibling.exe", 17701),
                EntryV2(1561, 178, "history-active.exe", 17801)));
            var api = new FakePendingApi(
                Target.For(1560, 177, 1177, "history-sibling.exe", "Modern", 17701),
                Target.For(1561, 178, 1178, "history-active.exe", "Modern", 17801));
            PendingRecoveryEntry[] entries = PendingRecoveryService.Discover(root, api).Files.Single().Entries.ToArray();
            var ledger = new JsonObject { ["Transactions"] = new JsonArray() };
            JsonArray transactions = ledger["Transactions"]!.AsArray();
            DateTimeOffset baseTime = DateTimeOffset.UtcNow.AddHours(-2);
            for (int i = 0; i < 70; i++)
            {
                transactions.Add(new JsonObject
                {
                    ["SchemaVersion"] = PendingRecoveryService.RecoveryTransactionSchemaVersion,
                    ["SourceFileId"] = entries[0].FileName,
                    ["SourceFileSha256"] = entries[0].SourceFileSha256,
                    ["EntryFingerprint"] = entries[0].EntryFingerprint,
                    ["EntryIndex"] = 0,
                    ["CandidateHwnd"] = 1560L,
                    ["CandidatePid"] = 177u,
                    ["CandidateWindowThreadId"] = 1177u,
                    ["CandidateExePath"] = "history-sibling.exe",
                    ["CandidateClassName"] = "Modern",
                    ["RecoveryToken"] = 9000L + i,
                    ["RecoveryMode"] = "v2-presentation",
                    ["Phase"] = PendingRecoveryService.RecoveryPhase.Retired,
                    ["PreparedUtc"] = baseTime.AddMinutes(-i),
                    ["UpdatedUtc"] = baseTime.AddMinutes(-i),
                });
            }
            File.WriteAllText(path + ".recovered", ledger.ToJsonString());

            // Completing the second entry runs MarkResolved, which compacts
            // retired history. The unresolved sibling's evidence is untouched.
            int result = RunInteractiveFor(entries[1], api, root);
            JsonObject after = JsonNode.Parse(File.ReadAllText(path + ".recovered"))!.AsObject();
            int retiredCount = after["Transactions"]!.AsArray().Count;
            PendingRecoveryEntry[] finalEntries = PendingRecoveryService.Discover(root, api).Files.Single().Entries.ToArray();
            return result == 0
                && retiredCount <= 64
                && retiredCount >= 1
                && File.Exists(path)
                && !finalEntries.Single(entry => entry.Entry.Hwnd == 1560).AlreadyResolved
                && finalEntries.Single(entry => entry.Entry.Hwnd == 1561).AlreadyResolved;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool AbandonPathRequiresVerifiablyGoneTarget()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            var api = new FakePendingApi(Target.For(1570, 179, 1179, "abandon.exe", "Modern", 17901));
            File.WriteAllText(path, JournalJson(2, EntryV2(1570, 179, "abandon.exe", 17901)));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            if (!FaultAfterStage(entry, api, "after-setprop", 0x5801))
                return false;

            // A live target refuses abandonment.
            using (var liveInput = new StringReader("abandon P01-E001\n"))
            using (var liveOutput = new StringWriter())
            {
                int liveResult = PendingRecoveryService.RunInteractive(
                    liveInput, liveOutput, root, api, Array.Empty<PendingRecoveryCandidate>());
                if (liveResult != 2 || !File.Exists(path))
                    return false;
            }

            // A verifiably destroyed target may be discarded, with zero native
            // mutations and a durable abandoned-resolution record.
            api.Targets[new IntPtr(1570)].Exists = false;
            int mutationsBefore = api.MutationCount;
            using var input = new StringReader("abandon P01-E001\n");
            using var output = new StringWriter();
            int result = PendingRecoveryService.RunInteractive(input, output, root, api, Array.Empty<PendingRecoveryCandidate>());
            JsonObject ledger = JsonNode.Parse(File.ReadAllText(path + ".recovered"))!.AsObject();
            return result == 0
                && !File.Exists(path)
                && api.MutationCount == mutationsBefore
                && ledger["Resolutions"]!.AsArray().Single()!.AsObject()["Result"]?.GetValue<string>() == "abandoned-target-gone"
                && ledger["Transactions"]!.AsArray().Single()!.AsObject()["Phase"]?.GetValue<string>() == PendingRecoveryService.RecoveryPhase.Retired;
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static bool FaultAfterStage(
        PendingRecoveryEntry entry,
        FakePendingApi api,
        string stage,
        long token)
    {
        try
        {
            PendingRecoveryService.ExecuteRecovery(
                entry,
                CandidateFor(entry, "C001"),
                api,
                out _,
                tokenFactory: () => new IntPtr(token),
                faultInjector: value => value == stage);
            return false;
        }
        catch (Exception ex) when (ex.Message.Contains("Injected recovery fault", StringComparison.Ordinal))
        {
            return true;
        }
    }

    private static int RunInteractiveFor(PendingRecoveryEntry entry, FakePendingApi api, string root)
    {
        PendingRecoveryCandidate candidate = CandidateFor(entry, "C001");
        using var input = new StringReader($"{entry.SessionId}\n{candidate.CandidateId}\nYES\n");
        using var output = new StringWriter();
        return PendingRecoveryService.RunInteractive(input, output, root, api, new[] { candidate });
    }

    private static void RemoveFirstPendingEntry(string path)
    {
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["Entries"]!.AsArray().RemoveAt(0);
        File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static void RewritePendingSource(string path)
    {
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["unknown-root-field"] = "rewritten-source";
        File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool SetLedgerTransactionPhase(string path, string phase)
    {
        try
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            if (root["Transactions"] is not JsonArray transactions
                || transactions.Count != 1
                || transactions[0] is not JsonObject transaction)
            {
                return false;
            }

            transaction["Phase"] = phase;
            File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool DuplicateLedgerTransaction(string path, string secondSourceSha, int secondEntryIndex)
    {
        try
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            if (root["Transactions"] is not JsonArray transactions
                || transactions.Count != 1
                || transactions[0] is not JsonObject first)
            {
                return false;
            }

            JsonObject second = JsonNode.Parse(first.ToJsonString())!.AsObject();
            second["SourceFileSha256"] = secondSourceSha;
            second["EntryIndex"] = secondEntryIndex;
            transactions.Add(second);
            File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool LedgerHasSingleRetiredCurrentTransaction(
        string path,
        string sourceSha256,
        int entryIndex,
        string entryFingerprint,
        long recoveryToken)
    {
        try
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            if (root["Transactions"] is not JsonArray transactions
                || transactions.Count != 1
                || transactions[0] is not JsonObject transaction)
            {
                return false;
            }

            return transaction["Phase"]?.GetValue<string>() == PendingRecoveryService.RecoveryPhase.Retired
                && string.Equals(
                    transaction["SourceFileSha256"]?.GetValue<string>(),
                    sourceSha256,
                    StringComparison.OrdinalIgnoreCase)
                && transaction["EntryIndex"]?.GetValue<int>() == entryIndex
                && string.Equals(
                    transaction["EntryFingerprint"]?.GetValue<string>(),
                    entryFingerprint,
                    StringComparison.OrdinalIgnoreCase)
                && transaction["RecoveryToken"]?.GetValue<long>() == recoveryToken;
        }
        catch
        {
            return false;
        }
    }

    private static string JournalJson(int? version, params JsonObject[] entries)
        => JournalJson(version, sourceInstanceId: null, entries);

    private static string JournalJson(int? version, string? sourceInstanceId, params JsonObject[] entries)
    {
        var root = new JsonObject
        {
            ["unknown-root-field"] = "preserve-me",
        };
        if (version.HasValue)
            root["Version"] = version.Value;
        if (sourceInstanceId != null)
            root["SourceInstanceId"] = sourceInstanceId;
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
        public uint Pid { get; set; }
        public uint ThreadId { get; set; }
        public string Exe { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public long ProcessStartTicks { get; set; }
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
