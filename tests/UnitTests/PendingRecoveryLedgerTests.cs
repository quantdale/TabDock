using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using TabDock.Services;
using TabDock.UnitTests.TestInfrastructure;
using Xunit;
using static TabDock.UnitTests.TestInfrastructure.PendingRecoveryTestHarness;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former PendingRecoverySelfTest (Wave 4): the durable
/// resolution ledger — one-entry retirement, sibling evidence preservation,
/// source-rewrite rebinding, generation-scoped identity dedup, resumability of
/// every transaction phase, and disk-only cleanup of completed recoveries.
/// </summary>
public class PendingRecoveryLedgerTests
{
    [Fact]
    public void SuccessfulRecovery_RetiresExactlyOneEntryAndPreservesSiblings()
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
                PendingTarget.For(1100, 131, 1131, "first.exe", "Modern", 13101),
                PendingTarget.For(1101, 132, 1132, "sibling.exe", "Modern", 13201));
            PendingRecoveryCatalog before = PendingRecoveryService.Discover(root, api);
            PendingRecoveryEntry entry = before.Files.Single().Entries[0];

            int result = RunInteractiveFor(entry, api, root);
            PendingRecoveryCatalog after = PendingRecoveryService.Discover(root, api);

            Assert.Equal(0, result);
            Assert.Single(after.Files);
            Assert.Equal(2, after.Files[0].Entries.Count);
            Assert.True(after.Files[0].Entries.Single(e => e.Entry.Hwnd == 1100).AlreadyResolved);
            Assert.False(after.Files[0].Entries.Single(e => e.Entry.Hwnd == 1101).AlreadyResolved);
            Assert.True(File.Exists(path + ".recovered"));
            // Unknown JSON fields survive the rewrite byte-preserving pass.
            Assert.Contains("unknown-root-field", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void ResolvedEntryRetirement_CanBeRetriedToCompletion()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1150, 135, "resolved.exe", 13501)));
            var api = new FakePendingApi(PendingTarget.For(1150, 135, 1135, "resolved.exe", "Modern", 13501));
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

            int result = PendingRecoveryService.RunInteractive(
                new StringReader(string.Empty),
                new StringWriter(),
                root,
                api,
                Array.Empty<PendingRecoveryCandidate>());

            Assert.Equal(0, result);
            Assert.False(File.Exists(path));
            Assert.True(File.Exists(path + ".recovered"));
            Assert.Equal(0, api.MutationCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void DuplicateEntries_DoNotCollapseSiblingEvidence()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            JsonObject duplicate = EntryV2(1140, 134, "duplicate.exe", 13401);
            File.WriteAllText(path, JournalJson(2, duplicate, JsonNode.Parse(duplicate.ToJsonString())!.AsObject()));
            var api = new FakePendingApi(PendingTarget.For(1140, 134, 1134, "duplicate.exe", "Modern", 13401));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries[0];

            int result = RunInteractiveFor(entry, api, root);
            PendingRecoveryCatalog after = PendingRecoveryService.Discover(root, api);

            Assert.Equal(0, result);
            Assert.Equal(2, after.Files.Single().Entries.Count);
            Assert.True(after.Files.Single().Entries.Single(e => e.EntryIndex == 0).AlreadyResolved);
            Assert.False(after.Files.Single().Entries.Single(e => e.EntryIndex == 1).AlreadyResolved);
            Assert.Equal("potentially-recoverable", after.Files.Single().Entries.Single(e => e.EntryIndex == 1).Status);
            Assert.Null(after.Files.Single().Entries.Single(e => e.EntryIndex == 1).Transaction);
            Assert.Contains("unknown-root-field", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void InterruptedSecondSibling_SurvivesFirstSiblingRetirementAndResumes()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1160, 136, "first-sibling.exe", 13601),
                EntryV2(1161, 137, "interrupted-sibling.exe", 13701)));
            var api = new FakePendingApi(
                PendingTarget.For(1160, 136, 1136, "first-sibling.exe", "Modern", 13601),
                PendingTarget.For(1161, 137, 1137, "interrupted-sibling.exe", "Modern", 13701));
            PendingRecoveryEntry interrupted = PendingRecoveryService.Discover(root, api).Files.Single().Entries[1];
            Assert.True(FaultAfterStage(interrupted, api, "after-setprop", 0x5161), "fixture must prepare an interrupted transaction");

            PendingRecoveryEntry first = PendingRecoveryService.Discover(root, api).Files.Single().Entries[0];
            int result = RunInteractiveFor(first, api, root);
            PendingRecoveryEntry[] after = PendingRecoveryService.Discover(root, api).Files.Single().Entries.ToArray();
            PendingRecoveryEntry survivor = after.Single(e => e.Entry.Hwnd == 1161);

            Assert.Equal(0, result);
            Assert.True(File.Exists(path));
            Assert.Equal(1, survivor.EntryIndex);
            Assert.Equal("interrupted-transaction", survivor.Status);
            Assert.NotNull(survivor.Transaction);
            Assert.Equal(new IntPtr(0x5161).ToInt64(), survivor.Transaction!.RecoveryToken);
            Assert.NotEqual(IntPtr.Zero, api.Targets[new IntPtr(1161)].RecoveryToken);

            Assert.Equal(0, RunInteractiveFor(survivor, api, root));
            Assert.False(File.Exists(path));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void InterruptedFirstSibling_SurvivesReverseRetirementOrderAndResumes()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1170, 138, "interrupted-first.exe", 13801),
                EntryV2(1171, 139, "second-sibling.exe", 13901)));
            var api = new FakePendingApi(
                PendingTarget.For(1170, 138, 1138, "interrupted-first.exe", "Modern", 13801),
                PendingTarget.For(1171, 139, 1139, "second-sibling.exe", "Modern", 13901));
            PendingRecoveryEntry interrupted = PendingRecoveryService.Discover(root, api).Files.Single().Entries[0];
            Assert.True(FaultAfterStage(interrupted, api, "after-setprop", 0x5170), "fixture must prepare an interrupted transaction");

            PendingRecoveryEntry second = PendingRecoveryService.Discover(root, api).Files.Single().Entries[1];
            int result = RunInteractiveFor(second, api, root);
            PendingRecoveryEntry[] after = PendingRecoveryService.Discover(root, api).Files.Single().Entries.ToArray();
            PendingRecoveryEntry survivor = after.Single(e => e.Entry.Hwnd == 1170);

            Assert.Equal(0, result);
            Assert.True(File.Exists(path));
            Assert.Equal(0, survivor.EntryIndex);
            Assert.Equal("interrupted-transaction", survivor.Status);
            Assert.NotNull(survivor.Transaction);
            Assert.Equal(new IntPtr(0x5170).ToInt64(), survivor.Transaction!.RecoveryToken);

            Assert.Equal(0, RunInteractiveFor(survivor, api, root));
            Assert.False(File.Exists(path));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void ThreeSiblingIndices_RemainStableAcrossMiddleRetirement()
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
                PendingTarget.For(1180, 140, 1140, "first-of-three.exe", "Modern", 14001),
                PendingTarget.For(1181, 141, 1141, "middle-of-three.exe", "Modern", 14101),
                PendingTarget.For(1182, 142, 1142, "last-of-three.exe", "Modern", 14201));
            PendingRecoveryEntry middle = PendingRecoveryService.Discover(root, api).Files.Single().Entries[1];

            int result = RunInteractiveFor(middle, api, root);
            PendingRecoveryEntry[] after = PendingRecoveryService.Discover(root, api).Files.Single().Entries.ToArray();

            Assert.Equal(0, result);
            Assert.Equal(before, File.ReadAllText(path)); // source bytes untouched until full retirement
            Assert.Equal(3, after.Length);
            Assert.Equal(0, after.Single(e => e.Entry.Hwnd == 1180).EntryIndex);
            Assert.True(after.Single(e => e.Entry.Hwnd == 1181).AlreadyResolved);
            Assert.Equal(2, after.Single(e => e.Entry.Hwnd == 1182).EntryIndex);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void OldRewrittenLedger_RebindsOnlyWhenUniqueAndConverges()
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
                PendingTarget.For(1190, 143, 1143, "removed-before-rebind.exe", "Modern", 14301),
                PendingTarget.For(1191, 144, 1144, "unique-rebind.exe", "Modern", 14401),
                PendingTarget.For(1192, 145, 1145, "other-rebind.exe", "Modern", 14501));
            PendingRecoveryEntry oldEntry = PendingRecoveryService.Discover(root, api).Files.Single().Entries[1];
            Assert.True(FaultAfterStage(oldEntry, api, "after-setprop", 0x5191), "fixture must prepare an interrupted transaction");

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

            Assert.True(rebound.TransactionNeedsRebind);
            Assert.Equal("interrupted-transaction", rebound.Status);
            Assert.Equal(0, result);
            Assert.True(File.Exists(path));
            Assert.True(File.Exists(path + ".recovered"));
            Assert.True(after.Single(e => e.Entry.Hwnd == 1191).AlreadyResolved);
            Assert.Equal(1, after.Single(e => e.Entry.Hwnd == 1192).EntryIndex);
            Assert.Equal(IntPtr.Zero, api.Targets[new IntPtr(1191)].RecoveryToken);
            Assert.True(ledgerConverged);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void OldRewrittenLedger_TokenRemovedPhase_RebindConvergesWithoutNativeRepeat()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1210, 147, "removed-before-token-removed.exe", 14701),
                EntryV2(1211, 148, "token-removed-rebind.exe", 14801)));
            var api = new FakePendingApi(
                PendingTarget.For(1210, 147, 1147, "removed-before-token-removed.exe", "Modern", 14701),
                PendingTarget.For(1211, 148, 1148, "token-removed-rebind.exe", "Modern", 14801));
            PendingRecoveryEntry oldEntry = PendingRecoveryService.Discover(root, api).Files.Single().Entries[1];
            const long recoveryToken = 0x5301;
            Assert.True(FaultAfterStage(oldEntry, api, "after-native-complete", recoveryToken), "fixture must prepare an interrupted transaction");

            // The old implementation had already removed the native token;
            // preserve that exact durable boundary while rewriting the source.
            api.Targets[new IntPtr(1211)].RecoveryToken = IntPtr.Zero;
            int placementBefore = api.PlacementCount;
            int showBefore = api.ShowCount;
            int transitionBefore = api.TransitionCount;
            RemoveFirstPendingEntry(path);
            Assert.True(SetLedgerTransactionPhase(path + ".recovered", PendingRecoveryService.RecoveryPhase.TokenRemoved));

            PendingRecoveryEntry rebound = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            Assert.True(rebound.TransactionNeedsRebind);
            Assert.Equal(PendingRecoveryService.RecoveryPhase.TokenRemoved, rebound.Transaction?.Phase);
            Assert.Equal(0, rebound.EntryIndex);
            Assert.Equal(1, oldEntry.EntryIndex);
            Assert.False(string.Equals(oldEntry.SourceFileSha256, rebound.SourceFileSha256, StringComparison.OrdinalIgnoreCase), "the rebound transaction must reference the rewritten source SHA");

            int result = RunInteractiveFor(rebound, api, root);
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

            Assert.Equal(0, result);
            Assert.Equal(placementBefore, api.PlacementCount);
            Assert.Equal(showBefore, api.ShowCount);
            Assert.Equal(transitionBefore, api.TransitionCount);
            Assert.Equal(0, api.RemovePropertyCount);
            Assert.Equal(IntPtr.Zero, api.Targets[new IntPtr(1211)].RecoveryToken);
            Assert.True(ledgerConverged);
            Assert.False(File.Exists(path));
            Assert.Equal(0, repeated);
            Assert.Empty(PendingRecoveryService.Discover(root, api).Files);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void OldRewrittenLedger_NativeCompletePhase_ConvergesWithCleanupOnly()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1220, 149, "removed-before-native-complete.exe", 14901),
                EntryV2(1221, 150, "native-complete-rebind.exe", 15001)));
            var api = new FakePendingApi(
                PendingTarget.For(1220, 149, 1149, "removed-before-native-complete.exe", "Modern", 14901),
                PendingTarget.For(1221, 150, 1150, "native-complete-rebind.exe", "Modern", 15001));
            PendingRecoveryEntry oldEntry = PendingRecoveryService.Discover(root, api).Files.Single().Entries[1];
            const long recoveryToken = 0x5302;
            Assert.True(FaultAfterStage(oldEntry, api, "after-native-complete", recoveryToken), "fixture must prepare an interrupted transaction");

            int placementBefore = api.PlacementCount;
            int showBefore = api.ShowCount;
            int transitionBefore = api.TransitionCount;
            RemoveFirstPendingEntry(path);
            PendingRecoveryEntry rebound = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();

            Assert.True(rebound.TransactionNeedsRebind);
            Assert.Equal(PendingRecoveryService.RecoveryPhase.NativeRecoveryComplete, rebound.Transaction?.Phase);
            Assert.False(string.Equals(oldEntry.SourceFileSha256, rebound.SourceFileSha256, StringComparison.OrdinalIgnoreCase), "the rebound transaction must reference the rewritten source SHA");

            int result = RunInteractiveFor(rebound, api, root);
            bool ledgerConverged = LedgerHasSingleRetiredCurrentTransaction(
                path + ".recovered",
                rebound.SourceFileSha256,
                rebound.EntryIndex,
                rebound.EntryFingerprint,
                recoveryToken);

            Assert.Equal(0, result);
            Assert.Equal(placementBefore, api.PlacementCount);
            Assert.Equal(showBefore, api.ShowCount);
            Assert.Equal(transitionBefore, api.TransitionCount);
            Assert.Equal(1, api.RemovePropertyCount);
            Assert.Equal(IntPtr.Zero, api.Targets[new IntPtr(1221)].RecoveryToken);
            Assert.True(ledgerConverged);
            Assert.False(File.Exists(path));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void OldRewrittenLedger_DuplicateSurvivors_FailClosed()
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
            var api = new FakePendingApi(PendingTarget.For(1201, 146, 1146, "duplicate-rebind.exe", "Modern", 14601));
            PendingRecoveryEntry oldEntry = PendingRecoveryService.Discover(root, api).Files.Single().Entries[0];
            Assert.True(FaultAfterStage(oldEntry, api, "after-setprop", 0x5201), "fixture must prepare an interrupted transaction");

            RemoveFirstPendingEntry(path);
            PendingRecoveryEntry[] survivors = PendingRecoveryService.Discover(root, api).Files.Single().Entries.ToArray();

            Assert.Equal(2, survivors.Length);
            Assert.All(survivors, entry => Assert.Equal("unverifiable-transaction", entry.Status));
            Assert.All(survivors, entry => Assert.Null(entry.Transaction));
            Assert.Equal(new IntPtr(0x5201), api.Targets[new IntPtr(1201)].RecoveryToken);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void OldRewrittenLedger_MultipleLegacyCandidates_FailClosed()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1230, 151, "multiple-legacy-candidates.exe", 15101)));
            var api = new FakePendingApi(PendingTarget.For(1230, 151, 1151, "multiple-legacy-candidates.exe", "Modern", 15101));
            PendingRecoveryEntry oldEntry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            const long recoveryToken = 0x5303;
            Assert.True(FaultAfterStage(oldEntry, api, "after-setprop", recoveryToken), "fixture must prepare an interrupted transaction");
            Assert.True(DuplicateLedgerTransaction(path + ".recovered", "legacy-second-sha", 7));

            RewritePendingSource(path);
            PendingRecoveryEntry ambiguous = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            int result = RunInteractiveFor(ambiguous, api, root);
            JsonObject ledger = JsonNode.Parse(File.ReadAllText(path + ".recovered"))!.AsObject();
            int transactionCount = ledger["Transactions"]?.AsArray().Count ?? 0;

            Assert.True(ambiguous.TransactionAmbiguous);
            Assert.Null(ambiguous.Transaction);
            Assert.Equal("unverifiable-transaction", ambiguous.Status);
            Assert.Equal(2, result);
            Assert.Equal(2, transactionCount);
            Assert.Equal(0, api.PlacementCount);
            Assert.Equal(0, api.ShowCount);
            Assert.Equal(0, api.TransitionCount);
            Assert.Equal(new IntPtr(recoveryToken), api.Targets[new IntPtr(1230)].RecoveryToken);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void OldRewrittenLedger_ForeignRecoveryToken_FailsClosed()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1240, 152, "foreign-recovery-token.exe", 15201)));
            var api = new FakePendingApi(PendingTarget.For(1240, 152, 1152, "foreign-recovery-token.exe", "Modern", 15201));
            PendingRecoveryEntry oldEntry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            const long recoveryToken = 0x5304;
            Assert.True(FaultAfterStage(oldEntry, api, "after-native-complete", recoveryToken), "fixture must prepare an interrupted transaction");
            string oldSourceSha = oldEntry.SourceFileSha256;

            Assert.True(SetLedgerTransactionPhase(path + ".recovered", PendingRecoveryService.RecoveryPhase.TokenRemoved));
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

            Assert.True(rebound.TransactionNeedsRebind);
            Assert.Equal(2, result);
            Assert.Equal(new IntPtr(0x5BAD), api.Targets[new IntPtr(1240)].RecoveryToken);
            Assert.Equal(0, api.RemovePropertyCount);
            Assert.Equal(1, api.PlacementCount);
            Assert.Equal(1, api.ShowCount);
            Assert.Equal(1, api.TransitionCount);
            Assert.Equal(oldSourceSha, transaction["SourceFileSha256"]?.GetValue<string>());
            Assert.Equal(PendingRecoveryService.RecoveryPhase.TokenRemoved, transaction["Phase"]?.GetValue<string>());
            Assert.True(File.Exists(path));
        }
        finally { DeleteRoot(root); }
    }

    [Theory]
    [InlineData("after-prepared")]
    [InlineData("after-setprop")]
    [InlineData("after-placement")]
    [InlineData("after-visibility")]
    [InlineData("after-dwm")]
    [InlineData("after-native-complete")]
    [InlineData("after-remove-property")]
    public void RecoveryTransactionFault_AtEveryStage_IsResumable(string stage)
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1400, 161, "fault.exe", 16101)));
            var api = new FakePendingApi(PendingTarget.For(1400, 161, 1161, "fault.exe", "Modern", 16101));
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

            Assert.True(threw, $"fault at '{stage}' must be injected");
            Assert.True(File.Exists(path));

            PendingRecoveryEntry resumed = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            bool resumedResult = PendingRecoveryService.ExecuteRecovery(
                resumed,
                CandidateFor(resumed, "C001"),
                api,
                out _,
                tokenFactory: () => new IntPtr(0x123456));
            Assert.True(resumedResult, $"recovery interrupted at '{stage}' must resume to completion");
            if (stage == "after-native-complete")
                Assert.Equal(1, api.PlacementCount);
            if (stage == "after-remove-property")
                Assert.Equal(1, api.RemovePropertyCount);
        }
        finally { DeleteRoot(root); }
    }

    [Theory]
    [InlineData("after-resolution-marker", true)]
    [InlineData("after-retirement", false)]
    public void PostResolutionFault_IsResumableWithoutRepeatingNativeWork(string stage, bool expectPendingAfterFault)
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1420, 182, "post-resolution.exe", 18201)));
            var api = new FakePendingApi(PendingTarget.For(1420, 182, 1182, "post-resolution.exe", "Modern", 18201));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();

            bool injected = false;
            try
            {
                RunInteractiveWithFault(entry, api, root, stage);
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

            Assert.True(injected);
            Assert.Equal(expectPendingAfterFault, pendingAfterFault);
            Assert.Equal(0, retry);
            Assert.False(File.Exists(path));
            Assert.Equal(1, api.PlacementCount);
            Assert.Equal(1, api.ShowCount);
            Assert.Equal(1, api.TransitionCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void InterruptedInteractiveRecovery_ResumesAndRetires()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1450, 166, "interactive.exe", 16601)));
            var api = new FakePendingApi(PendingTarget.For(1450, 166, 1166, "interactive.exe", "Modern", 16601));
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
            int result = RunInteractiveFor(interrupted, api, root);

            Assert.True(injected);
            Assert.Equal("interrupted-transaction", interrupted.Status);
            Assert.Equal(0, result);
            Assert.False(File.Exists(path));
            Assert.Equal(1, api.PlacementCount);
            Assert.Equal(1, api.ShowCount);
            Assert.Equal(1, api.TransitionCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void RetirementFault_PreservesSiblingEvidenceInDurableLedger()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1460, 167, "retire.exe", 16701),
                EntryV2(1461, 168, "sibling.exe", 16801)));
            var api = new FakePendingApi(
                PendingTarget.For(1460, 167, 1167, "retire.exe", "Modern", 16701),
                PendingTarget.For(1461, 168, 1168, "sibling.exe", "Modern", 16801));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries[0];

            bool injected = false;
            try
            {
                PendingRecoveryService.RunInteractive(
                    new StringReader($"{entry.SessionId}\nC001\nYES\n"),
                    new StringWriter(),
                    root,
                    api,
                    new[] { CandidateFor(entry, "C001") },
                    faultInjector: stage => stage == "during-retirement");
            }
            catch (Exception ex) when (ex.Message.Contains("Injected recovery fault", StringComparison.Ordinal))
            {
                injected = true;
            }

            PendingRecoveryCatalog after = PendingRecoveryService.Discover(root, api);

            Assert.True(injected);
            Assert.Equal(2, after.Files.Single().Entries.Count);
            Assert.True(after.Files.Single().Entries.Single(e => e.Entry.Hwnd == 1460).AlreadyResolved);
            Assert.Equal(1, after.Files.Single().Entries.Single(e => e.Entry.Hwnd == 1461).EntryIndex);
            Assert.True(File.Exists(path + ".recovered"));
            Assert.Contains(entry.EntryFingerprint, File.ReadAllText(path + ".recovered"), StringComparison.Ordinal);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void GenerationIdentity_SeparatesByteIdenticalEvidenceAcrossGenerations()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            var api = new FakePendingApi(PendingTarget.For(1500, 171, 1171, "gen.exe", "Modern", 17101));
            File.WriteAllText(
                path,
                JournalJson(2, "11111111-1111-1111-1111-111111111111", EntryV2(1500, 171, "gen.exe", 17101)));
            PendingRecoveryEntry first = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            Assert.Equal(0, RunInteractiveFor(first, api, root));
            Assert.False(File.Exists(path));

            // Generation B: byte-identical JSON under a DIFFERENT
            // SourceInstanceId. It must not match generation A's resolution.
            File.WriteAllText(
                path,
                JournalJson(2, "22222222-2222-2222-2222-222222222222", EntryV2(1500, 171, "gen.exe", 17101)));
            PendingRecoveryEntry second = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();

            Assert.False(second.AlreadyResolved);
            Assert.Equal("potentially-recoverable", second.Status);
            Assert.Null(second.Transaction);
            Assert.Equal(0, RunInteractiveFor(second, api, root));
            Assert.False(File.Exists(path));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void SameGenerationReplay_ResolvesAsDuplicateWithoutRepeatWork()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            var api = new FakePendingApi(PendingTarget.For(1510, 172, 1172, "replay.exe", "Modern", 17201));
            string body = JournalJson(2, "33333333-3333-3333-3333-333333333333", EntryV2(1510, 172, "replay.exe", 17201));
            File.WriteAllText(path, body);
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            Assert.Equal(0, RunInteractiveFor(entry, api, root));
            Assert.False(File.Exists(path));

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

            Assert.True(replay.AlreadyResolved);
            Assert.Equal(0, result);
            Assert.False(File.Exists(path));
            Assert.Equal(1, api.PlacementCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void LegacyPendingWithoutInstanceId_StillConsumesViaFingerprintFallback()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            var api = new FakePendingApi(PendingTarget.For(1525, 173, 1173, "legacy-migration.exe", "Modern", 17301));
            string body = JournalJson(2, EntryV2(1525, 173, "legacy-migration.exe", 17301));
            File.WriteAllText(path, body);
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            Assert.Equal(0, RunInteractiveFor(entry, api, root));
            Assert.False(File.Exists(path));

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

            Assert.True(resolutionStoredWithoutInstance);
            Assert.True(replay.AlreadyResolved);
            Assert.Equal(0, result);
            Assert.False(File.Exists(path));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void InterruptedTransaction_AcrossGenerations_Resumes()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            var api = new FakePendingApi(PendingTarget.For(1530, 174, 1174, "cross-gen.exe", "Modern", 17401));
            File.WriteAllText(
                path,
                JournalJson(2, "44444444-4444-4444-4444-444444444444", EntryV2(1530, 174, "cross-gen.exe", 17401)));
            PendingRecoveryEntry first = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            Assert.Equal(0, RunInteractiveFor(first, api, root));
            Assert.False(File.Exists(path));

            File.WriteAllText(
                path,
                JournalJson(2, "55555555-5555-5555-5555-555555555555", EntryV2(1530, 174, "cross-gen.exe", 17401)));
            PendingRecoveryEntry second = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            Assert.True(FaultAfterStage(second, api, "after-setprop", 0x5601), "fixture must prepare an interrupted transaction");

            PendingRecoveryEntry resumed = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
            Assert.False(resumed.AlreadyResolved);
            Assert.Equal("interrupted-transaction", resumed.Status);
            Assert.NotNull(resumed.Transaction);

            Assert.Equal(0, RunInteractiveFor(resumed, api, root));
            Assert.False(File.Exists(path));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void UnreadablePendingSibling_DoesNotBlockOtherDiskOnlyCleanup()
    {
        string root = CreateRoot();
        try
        {
            string readablePath = Path.Combine(root, "hidden-windows.json.pending");
            string unreadablePath = Path.Combine(root, "hidden-windows.json.pending.002");
            File.WriteAllText(readablePath, JournalJson(2, EntryV2(1540, 175, "readable.exe", 17501)));
            File.WriteAllText(unreadablePath, "{not-json");
            var api = new FakePendingApi(PendingTarget.For(1540, 175, 1175, "readable.exe", "Modern", 17501));
            PendingRecoveryFile readableFile = PendingRecoveryService.Discover(root, api)
                .Files.Single(file => file.FileName == "hidden-windows.json.pending");
            Assert.True(FaultAfterStage(readableFile.Entries.Single(), api, "after-native-complete", 0x5701), "fixture must prepare an interrupted transaction");

            // The unreadable sibling must not short-circuit disk-only cleanup of
            // the other completed evidence; only new supervised selection stays
            // fail-closed (exit code 2).
            int result = PendingRecoveryService.RunInteractive(
                new StringReader(string.Empty),
                new StringWriter(),
                root,
                api,
                Array.Empty<PendingRecoveryCandidate>());

            Assert.Equal(2, result);
            Assert.False(File.Exists(readablePath));
            Assert.True(File.Exists(unreadablePath));
            Assert.True(File.Exists(readablePath + ".recovered"));
            Assert.Equal(1, api.RemovePropertyCount);
            Assert.Equal(1, api.PlacementCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void CompletedRecoveryCleanup_CasesAreDiskOnly()
    {
        // Exact token: cleanup removes it; already-gone token: nothing to do;
        // destroyed/replaced windows never get their tokens removed by cleanup;
        // unverifiable evidence is retained for supervised retry.
        Assert.True(RunCompletedCleanupCase(
            1430,
            target => { },
            expectedResult: 0,
            expectedFile: false,
            expectedRemoveCount: 1), "exact matching token must be removed");
        Assert.True(RunCompletedCleanupCase(
            1431,
            target => target.RecoveryToken = IntPtr.Zero,
            expectedResult: 0,
            expectedFile: false,
            expectedRemoveCount: 0), "already-gone token needs no removal");
        Assert.True(RunCompletedCleanupCase(
            1432,
            target => target.Exists = false,
            expectedResult: 0,
            expectedFile: false,
            expectedRemoveCount: 0), "destroyed window is a benign completion");
        Assert.True(RunCompletedCleanupCase(
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
            expectedRecoveryToken: new IntPtr(0x7333)), "replacement window keeps its own foreign token");
        Assert.True(RunCompletedCleanupCase(
            1434,
            target =>
            {
                target.ProcessStartTicks++;
                target.RecoveryToken = new IntPtr(0x7334);
            },
            expectedResult: 0,
            expectedFile: false,
            expectedRemoveCount: 0,
            expectedRecoveryToken: new IntPtr(0x7334)), "process-instance replacement keeps its foreign token");
        Assert.True(RunCompletedCleanupCase(
            1435,
            target => target.ProcessStartTicks = 0,
            expectedResult: 2,
            expectedFile: true,
            expectedRemoveCount: 0), "unverifiable identity retains evidence");

        static bool RunCompletedCleanupCase(
            long hwnd,
            Action<PendingTarget> mutate,
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
                var api = new FakePendingApi(PendingTarget.For(hwnd, pid, pid + 1000, exe, "Modern", start));
                PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();
                if (!FaultAfterStage(entry, api, "after-native-complete", 0x7000 + hwnd))
                    return false;
                PendingTarget target = api.Targets[new IntPtr(hwnd)];
                mutate(target);
                int result = PendingRecoveryService.RunInteractive(
                    new StringReader(string.Empty),
                    new StringWriter(),
                    root,
                    api,
                    Array.Empty<PendingRecoveryCandidate>());
                return result == expectedResult
                    && File.Exists(path) == expectedFile
                    && api.RemovePropertyCount == expectedRemoveCount
                    && api.PlacementCount == 1
                    && api.ShowCount == 1
                    && api.TransitionCount == 1
                    && (!expectedRecoveryToken.HasValue || target.RecoveryToken == expectedRecoveryToken.Value);
            }
            finally { DeleteRoot(root); }
        }
    }
}

