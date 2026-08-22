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
/// Migrated from the former PendingRecoverySelfTest (Wave 4): read-only
/// discovery and catalog semantics of the supervised tokenless-journal
/// workflow.
/// </summary>
public class PendingRecoveryDiscoveryTests
{
    [Fact]
    public void NoPendingEvidence_IsReadOnly()
    {
        string root = CreateRoot();
        try
        {
            PendingRecoveryCatalog catalog = PendingRecoveryService.Discover(root, new FakePendingApi());
            Assert.Null(catalog.Error);
            Assert.Empty(catalog.Files);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void V1AndV2Evidence_AreDiscoveredAsPotentiallyRecoverable()
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
                PendingTarget.For(100, 41, 1041, "legacy.exe", "Legacy", 0),
                PendingTarget.For(200, 42, 1042, "modern.exe", "Modern", 4202));

            PendingRecoveryCatalog catalog = PendingRecoveryService.Discover(root, api);
            PendingRecoveryEntry[] entries = catalog.Files.SelectMany(file => file.Entries).ToArray();
            PendingRecoveryEntry v1 = entries.Single(entry => entry.Version == 1);
            PendingRecoveryEntry v2 = entries.Single(entry => entry.Version == 2);

            Assert.Null(catalog.Error);
            Assert.Equal(2, entries.Length);
            Assert.True(v1.IsV1);
            Assert.False(v1.Fields.HasClass);
            Assert.True(v2.Fields.HasClass);
            Assert.True(v2.Fields.HasProcessStart);
            Assert.All(entries, entry => Assert.Equal("potentially-recoverable", entry.Status));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void MultipleFilesAndEntries_AreAllListed()
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
                PendingTarget.For(301, 51, 1051, "one.exe", "Pig", 5101),
                PendingTarget.For(302, 52, 1052, "two.exe", "Pig", 5202),
                PendingTarget.For(303, 53, 1053, "three.exe", "Pig", 0),
                PendingTarget.For(304, 54, 1054, "four.exe", "Pig", 0));

            PendingRecoveryCatalog catalog = PendingRecoveryService.Discover(root, api);

            Assert.Equal(3, catalog.Files.Count);
            Assert.Contains(catalog.Files, file => file.Entries.Count == 2);
            Assert.Equal(4, catalog.Files.Sum(file => file.Entries.Count));
            Assert.Equal(3, PendingRecoveryService.CountActivePendingFiles(root));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void MalformedFutureAndInaccessibleEvidence_IsRetained()
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

            Assert.Equal(2, catalog.Files.Count);
            Assert.Contains(catalog.Files, file => file.Status.StartsWith("malformed", StringComparison.Ordinal));
            Assert.Contains(catalog.Files, file => file.Status == "future-schema" && file.Entries.Count == 1);
            Assert.Equal("unreadable (not-a-directory)", inaccessible.Error);
            Assert.True(File.Exists(Path.Combine(root, "hidden-windows.json.pending")));
            Assert.True(File.Exists(Path.Combine(root, "hidden-windows.json.pending.001")));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void MalformedRecoveryLedger_IsRetainedAndFailsClosed()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(450, 65, "ledger-corrupt.exe", 6501)));
            File.WriteAllText(path + ".recovered", "{");
            var api = new FakePendingApi(PendingTarget.For(450, 65, 1065, "ledger-corrupt.exe", "Modern", 6501));
            PendingRecoveryCatalog catalog = PendingRecoveryService.Discover(root, api);

            int result = PendingRecoveryService.RunInteractive(
                new StringReader(string.Empty),
                new StringWriter(),
                root,
                api,
                Array.Empty<PendingRecoveryCandidate>());

            Assert.StartsWith("unreadable (recovery-ledger):", catalog.Files.Single().Status, StringComparison.Ordinal);
            Assert.Equal(2, result);
            Assert.True(File.Exists(path));
            Assert.True(File.Exists(path + ".recovered"));
            Assert.Equal(0, api.MutationCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void HistoricalFields_MustAllMatchTheLiveCandidate()
    {
        string root = CreateRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "hidden-windows.json.pending"),
                JournalJson(2, EntryV2(500, 71, "match.exe", 7101)));
            var api = new FakePendingApi(PendingTarget.For(500, 71, 1071, "match.exe", "Pig", 7101));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();

            PendingRecoveryCandidate candidate = CandidateFor(entry, "C001");
            PendingRecoveryCandidate wrongPid = CopyCandidate(candidate, "C002", processId: 72);
            PendingRecoveryCandidate wrongExe = CopyCandidate(candidate, "C003", exePath: "other.exe");
            PendingRecoveryCandidate wrongStart = CopyCandidate(candidate, "C004", processStart: 7102);
            PendingRecoveryCandidate wrongHwnd = CopyCandidate(candidate, "C005", hwnd: new IntPtr(501));

            Assert.True(PendingRecoveryService.MatchesHistoricalEvidence(entry, candidate));
            Assert.False(PendingRecoveryService.MatchesHistoricalEvidence(entry, wrongPid));
            Assert.False(PendingRecoveryService.MatchesHistoricalEvidence(entry, wrongExe));
            Assert.False(PendingRecoveryService.MatchesHistoricalEvidence(entry, wrongStart));
            Assert.False(PendingRecoveryService.MatchesHistoricalEvidence(entry, wrongHwnd));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void OrphanedTemporaryFiles_AreSweptByAge()
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

            Assert.Null(catalog.Error);
            Assert.False(File.Exists(staleTmp));
            Assert.True(File.Exists(freshTmp));
            Assert.True(File.Exists(pendingPath));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void RetiredLedger_CompactionBoundsHistoryWithoutTouchingUnresolvedSiblings()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1560, 177, "history-sibling.exe", 17701),
                EntryV2(1561, 178, "history-active.exe", 17801)));
            var api = new FakePendingApi(
                PendingTarget.For(1560, 177, 1177, "history-sibling.exe", "Modern", 17701),
                PendingTarget.For(1561, 178, 1178, "history-active.exe", "Modern", 17801));
            PendingRecoveryEntry[] entries = PendingRecoveryService.Discover(root, api).Files.Single().Entries.ToArray();
            var ledger = new JsonObject { ["Transactions"] = new JsonArray() };
            var transactions = ledger["Transactions"]!.AsArray();
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

            Assert.Equal(0, result);
            Assert.InRange(retiredCount, 1, 64);
            Assert.True(File.Exists(path));
            Assert.False(finalEntries.Single(entry => entry.Entry.Hwnd == 1560).AlreadyResolved);
            Assert.True(finalEntries.Single(entry => entry.Entry.Hwnd == 1561).AlreadyResolved);
        }
        finally { DeleteRoot(root); }
    }
}
