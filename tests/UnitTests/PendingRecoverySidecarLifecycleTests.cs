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
/// Regression matrix for the resolution-ledger lifecycle campaign:
/// liveness-bounded compaction of <c>Resolutions</c>, sidecar removal on full
/// source retirement, crash convergence for orphaned sidecars on the mutating
/// supervised path, fail-closed retention of unreadable or live-transaction
/// orphans, and generation-scoped bookkeeping hygiene.
/// </summary>
public class PendingRecoverySidecarLifecycleTests
{
    private const string LiveInstanceId = "aaaaaaaa-1111-1111-1111-111111111111";
    private const string DeadInstanceId = "bbbbbbbb-2222-2222-2222-222222222222";

    [Fact]
    public void ResolutionRewrite_DropsDeadGenerations_KeepsLiveRecordsAndNonRetiredTransactions()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, LiveInstanceId,
                EntryV2(1600, 181, "live-marked.exe", 18101),
                EntryV2(1601, 182, "now-recovering.exe", 18201),
                EntryV2(1602, 183, "still-unresolved.exe", 18301)));
            var api = new FakePendingApi(
                PendingTarget.For(1600, 181, 1181, "live-marked.exe", "Modern", 18101),
                PendingTarget.For(1601, 182, 1182, "now-recovering.exe", "Modern", 18201),
                PendingTarget.For(1602, 183, 1183, "still-unresolved.exe", "Modern", 18301));
            PendingRecoveryEntry[] entries = PendingRecoveryService.Discover(root, api).Files.Single().Entries.ToArray();

            var ledger = new JsonObject
            {
                ["Resolutions"] = new JsonArray(),
                ["Transactions"] = new JsonArray(),
            };
            var resolutions = ledger["Resolutions"]!.AsArray();
            var transactions = ledger["Transactions"]!.AsArray();

            // One LIVE-generation resolution marks entry 0 as already handled.
            resolutions.Add(ResolutionJson(entries[0].FileName, LiveInstanceId, entries[0].SourceFileSha256, entries[0].EntryIndex, entries[0].EntryFingerprint));
            // 70 unreachable dead-generation resolutions under the reused filename.
            for (int i = 0; i < 70; i++)
                resolutions.Add(ResolutionJson(entries[0].FileName, DeadInstanceId, entries[0].SourceFileSha256, 0, entries[0].EntryFingerprint));
            // An empty-keyed legacy migration marker is unreachable for a
            // new-format source and must be compacted away too.
            resolutions.Add(new JsonObject
            {
                ["SourceFileId"] = string.Empty,
                ["SourceFileSha256"] = string.Empty,
                ["EntryFingerprint"] = entries[0].EntryFingerprint,
                ["EntryIndex"] = 0,
                ["SchemaVersion"] = 2,
                ["ResolvedUtc"] = "2026-08-01T00:00:00+00:00",
                ["Result"] = "presentation-restored",
            });
            // Historical retired transactions (bounded) plus ONE NON-RETIRED
            // record that must survive every rewrite untouched.
            DateTimeOffset baseTime = DateTimeOffset.UtcNow.AddHours(-2);
            for (int i = 0; i < 70; i++)
            {
                transactions.Add(TransactionJson(
                    entries[0].FileName, DeadInstanceId, entries[0].SourceFileSha256,
                    entries[0].EntryFingerprint, 9000 + i,
                    PendingRecoveryService.RecoveryPhase.Retired, baseTime.AddMinutes(-i)));
            }
            transactions.Add(TransactionJson(
                entries[0].FileName, DeadInstanceId, entries[0].SourceFileSha256,
                entries[0].EntryFingerprint, 9999,
                PendingRecoveryService.RecoveryPhase.Prepared, baseTime));
            File.WriteAllText(path + ".recovered", ledger.ToJsonString());

            int result = RunInteractiveFor(entries[1], api, root);
            JsonObject after = JsonNode.Parse(File.ReadAllText(path + ".recovered"))!.AsObject();
            int resolutionCount = after["Resolutions"]!.AsArray().Count;
            var survivingTransactions = after["Transactions"]!.AsArray();
            int retiredCount = survivingTransactions.Count(item =>
                item!.AsObject()["Phase"]?.GetValue<string>() == PendingRecoveryService.RecoveryPhase.Retired);
            bool nonRetiredSurvived = survivingTransactions.Any(item =>
                item!.AsObject()["Phase"]?.GetValue<string>() == PendingRecoveryService.RecoveryPhase.Prepared);
            PendingRecoveryCatalog catalogAfter = PendingRecoveryService.Discover(root, api);

            Assert.Equal(0, result);
            Assert.True(File.Exists(path), "an unresolved sibling keeps the source alive");
            Assert.True(File.Exists(path + ".recovered"));
            Assert.Equal(2, resolutionCount); // entry0 + entry1 live records only
            Assert.InRange(retiredCount, 1, 64);
            Assert.True(nonRetiredSurvived, "interrupted/non-retired transactions are never touched by compaction");
            Assert.True(catalogAfter.Files.Single().Entries.Single(e => e.Entry.Hwnd == 1600).AlreadyResolved);
            Assert.False(catalogAfter.Files.Single().Entries.Single(e => e.Entry.Hwnd == 1602).AlreadyResolved);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void FullRetirement_RemovesSidecarOnlyWhenEverySiblingIsDone()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1610, 184, "first-half.exe", 18401),
                EntryV2(1611, 185, "second-half.exe", 18501)));
            var api = new FakePendingApi(
                PendingTarget.For(1610, 184, 1184, "first-half.exe", "Modern", 18401),
                PendingTarget.For(1611, 185, 1185, "second-half.exe", "Modern", 18501));
            PendingRecoveryEntry[] entries = PendingRecoveryService.Discover(root, api).Files.Single().Entries.ToArray();

            Assert.Equal(0, RunInteractiveFor(entries[0], api, root));
            Assert.True(File.Exists(path), "partial source stays");
            Assert.True(File.Exists(path + ".recovered"), "partially resolved source keeps its sidecar");

            // The second recovery's disk-only cleanup pass retires BOTH now-
            // resolved entries and removes source plus sidecar together.
            Assert.Equal(0, RunInteractiveFor(entries[1], api, root));
            Assert.False(File.Exists(path));
            Assert.False(File.Exists(path + ".recovered"), "full retirement removes the sidecar");
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void CrashAfterSourceDeletion_BeforeSidecarDeletion_ConvergesOnNextSupervisedRun()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            File.WriteAllText(path, JournalJson(2, EntryV2(1620, 186, "crashy.exe", 18601)));
            var api = new FakePendingApi(PendingTarget.For(1620, 186, 1186, "crashy.exe", "Modern", 18601));
            PendingRecoveryEntry entry = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single();

            bool injected = false;
            try
            {
                RunInteractiveWithFault(entry, api, root, "after-retirement");
            }
            catch (Exception ex) when (ex.Message.Contains("Injected recovery fault", StringComparison.Ordinal))
            {
                injected = true;
            }

            Assert.True(injected);
            Assert.False(File.Exists(path), "the fault lands after source deletion");
            Assert.True(File.Exists(path + ".recovered"), "the interrupted invocation leaves the orphan sidecar behind");

            using var output = new StringWriter();
            int result = PendingRecoveryService.RunInteractive(
                new StringReader(string.Empty),
                output,
                root,
                api,
                Array.Empty<PendingRecoveryCandidate>());

            Assert.Equal(0, result);
            Assert.False(File.Exists(path + ".recovered"), "the next supervised run sweeps the orphan");
            Assert.Contains("Retired orphaned recovery ledger", output.ToString(), StringComparison.Ordinal);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void UnreadableOrphanSidecar_IsRetainedFailClosed()
    {
        string root = CreateRoot();
        try
        {
            string orphan = Path.Combine(root, "hidden-windows.json.pending.recovered");
            File.WriteAllText(orphan, "{ unreadable fragment");

            using var output = new StringWriter();
            int result = PendingRecoveryService.RunInteractive(
                new StringReader(string.Empty),
                output,
                root,
                new FakePendingApi(),
                Array.Empty<PendingRecoveryCandidate>());

            Assert.Equal(0, result);
            Assert.True(File.Exists(orphan), "unreadable evidence-shaped bookkeeping is never silently destroyed");
            Assert.Contains("retained for review", output.ToString(), StringComparison.Ordinal);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void OrphanHoldingNonRetiredTransaction_IsRetained()
    {
        string root = CreateRoot();
        try
        {
            string orphan = Path.Combine(root, "hidden-windows.json.pending.recovered");
            var ledger = new JsonObject
            {
                ["Resolutions"] = new JsonArray(),
                ["Transactions"] = new JsonArray
                {
                    TransactionJson(
                        "hidden-windows.json.pending", DeadInstanceId, "sha-not-verifiable",
                        "fingerprint", 4242,
                        PendingRecoveryService.RecoveryPhase.TokenInstalled,
                        DateTimeOffset.UtcNow),
                },
            };
            File.WriteAllText(orphan, ledger.ToJsonString());

            using var output = new StringWriter();
            int result = PendingRecoveryService.RunInteractive(
                new StringReader(string.Empty),
                output,
                root,
                new FakePendingApi(),
                Array.Empty<PendingRecoveryCandidate>());

            Assert.Equal(0, result);
            Assert.True(File.Exists(orphan), "a possible interrupted-recovery trace is retained");
            Assert.Contains("unfinished recovery transaction", output.ToString(), StringComparison.Ordinal);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void OrphanWithOnlyHistoricalContent_IsDeletedByTheSupervisedSweep()
    {
        string root = CreateRoot();
        try
        {
            string orphan = Path.Combine(root, "hidden-windows.json.pending.recovered");
            var ledger = new JsonObject
            {
                ["Resolutions"] = new JsonArray
                {
                    ResolutionJson("hidden-windows.json.pending", DeadInstanceId, "old-sha", 0, "old-fingerprint"),
                },
                ["Transactions"] = new JsonArray
                {
                    TransactionJson(
                        "hidden-windows.json.pending", DeadInstanceId, "old-sha",
                        "old-fingerprint", 777,
                        PendingRecoveryService.RecoveryPhase.Retired,
                        DateTimeOffset.UtcNow.AddDays(-1)),
                },
            };
            File.WriteAllText(orphan, ledger.ToJsonString());

            using var output = new StringWriter();
            int result = PendingRecoveryService.RunInteractive(
                new StringReader(string.Empty),
                output,
                root,
                new FakePendingApi(),
                Array.Empty<PendingRecoveryCandidate>());

            Assert.Equal(0, result);
            Assert.False(File.Exists(orphan));
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public void LegacyLiveSource_RewriteKeepsEmptyKeyedMarkers_DropsForeignGenerations()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "hidden-windows.json.pending");
            // No SourceInstanceId: pre-upgrade evidence.
            File.WriteAllText(path, JournalJson(2,
                EntryV2(1630, 187, "legacy-marked.exe", 18701),
                EntryV2(1631, 188, "legacy-recovering.exe", 18801),
                EntryV2(1632, 189, "still-unresolved.exe", 18901)));
            var api = new FakePendingApi(
                PendingTarget.For(1630, 187, 1187, "legacy-marked.exe", "Modern", 18701),
                PendingTarget.For(1631, 188, 1188, "legacy-recovering.exe", "Modern", 18801),
                PendingTarget.For(1632, 189, 1189, "still-unresolved.exe", "Modern", 18901));
            PendingRecoveryEntry[] entries = PendingRecoveryService.Discover(root, api).Files.Single().Entries.ToArray();

            var ledger = new JsonObject
            {
                ["Resolutions"] = new JsonArray
                {
                    // Empty-keyed content-only marker: consumed by the bounded
                    // legacy fingerprint fallback for this null-id source.
                    new JsonObject
                    {
                        ["SourceFileId"] = string.Empty,
                        ["SourceFileSha256"] = string.Empty,
                        ["EntryFingerprint"] = entries[0].EntryFingerprint,
                        ["EntryIndex"] = 0,
                        ["SchemaVersion"] = 2,
                        ["ResolvedUtc"] = "2026-08-01T00:00:00+00:00",
                        ["Result"] = "presentation-restored",
                    },
                    // Foreign-generation junk under the same filename.
                    ResolutionJson(entries[0].FileName, DeadInstanceId, entries[0].SourceFileSha256, 0, entries[0].EntryFingerprint),
                },
                ["Transactions"] = new JsonArray
                {
                    // A supported interruptable transaction on entry 1 drives a
                    // real PersistTransaction rewrite when it is resumed below.
                    new JsonObject
                    {
                        ["SchemaVersion"] = PendingRecoveryService.RecoveryTransactionSchemaVersion,
                        ["SourceFileId"] = entries[1].FileName,
                        ["SourceInstanceId"] = null,
                        ["SourceFileSha256"] = entries[1].SourceFileSha256,
                        ["EntryFingerprint"] = entries[1].EntryFingerprint,
                        ["EntryIndex"] = entries[1].EntryIndex,
                        ["CandidateHwnd"] = entries[1].Entry.Hwnd,
                        ["CandidatePid"] = entries[1].Entry.Pid,
                        ["CandidateWindowThreadId"] = entries[1].Entry.Pid + 1000,
                        ["CandidateExePath"] = entries[1].Entry.ExePath,
                        ["CandidateClassName"] = "Modern",
                        ["CandidateProcessStartTimeUtcTicks"] = entries[1].Entry.ProcessStartTimeUtcTicks,
                        ["RecoveryToken"] = 5555L,
                        ["RecoveryMode"] = entries[1].RecoveryMode,
                        ["Phase"] = PendingRecoveryService.RecoveryPhase.Prepared,
                        ["PreparedUtc"] = DateTimeOffset.UtcNow,
                        ["UpdatedUtc"] = DateTimeOffset.UtcNow,
                    },
                },
            };
            File.WriteAllText(path + ".recovered", ledger.ToJsonString());

            PendingRecoveryEntry resumed = PendingRecoveryService.Discover(root, api).Files.Single().Entries.Single(e => e.Entry.Hwnd == 1631);
            Assert.Equal("interrupted-transaction", resumed.Status);
            Assert.Equal(0, RunInteractiveFor(resumed, api, root));

            JsonObject after = JsonNode.Parse(File.ReadAllText(path + ".recovered"))!.AsObject();
            var surviving = after["Resolutions"]!.AsArray();
            bool legacyMarkerSurvived = surviving.Any(item =>
                string.IsNullOrEmpty(item!.AsObject()["SourceFileId"]?.GetValue<string>())
                && string.IsNullOrEmpty(item.AsObject()["SourceFileSha256"]?.GetValue<string>()));
            bool foreignGenerationDropped = !surviving.Any(item =>
                item!.AsObject()["SourceInstanceId"]?.GetValue<string>() == DeadInstanceId);
            PendingRecoveryCatalog catalog = PendingRecoveryService.Discover(root, api);

            Assert.True(legacyMarkerSurvived, "the legacy migration marker survives rewrites while its null-id source is live");
            Assert.True(foreignGenerationDropped);
            Assert.Equal(2, surviving.Count); // empty-keyed marker + entry1's exact record
            Assert.True(catalog.Files.Single().Entries.Single(e => e.Entry.Hwnd == 1630).AlreadyResolved,
                "the sibling stays resolvable through the preserved marker");
            Assert.False(catalog.Files.Single().Entries.Single(e => e.Entry.Hwnd == 1632).AlreadyResolved);
            Assert.True(File.Exists(path), "an unresolved sibling keeps the source alive");
            Assert.True(File.Exists(path + ".recovered"));
        }
        finally { DeleteRoot(root); }
    }

    private static JsonObject ResolutionJson(
        string fileName,
        string? sourceInstanceId,
        string sha256,
        int entryIndex,
        string fingerprint)
        => new()
        {
            ["SourceFileId"] = fileName,
            ["SourceInstanceId"] = sourceInstanceId,
            ["SourceFileSha256"] = sha256,
            ["EntryFingerprint"] = fingerprint,
            ["EntryIndex"] = entryIndex,
            ["SchemaVersion"] = 2,
            ["ResolvedUtc"] = "2026-08-01T00:00:00+00:00",
            ["Result"] = "presentation-restored",
        };

    private static JsonObject TransactionJson(
        string fileName,
        string? sourceInstanceId,
        string sha256,
        string fingerprint,
        long token,
        string phase,
        DateTimeOffset updated)
        => new()
        {
            ["SchemaVersion"] = PendingRecoveryService.RecoveryTransactionSchemaVersion,
            ["SourceFileId"] = fileName,
            ["SourceInstanceId"] = sourceInstanceId,
            ["SourceFileSha256"] = sha256,
            ["EntryFingerprint"] = fingerprint,
            ["EntryIndex"] = 0,
            ["CandidateHwnd"] = 0L,
            ["CandidatePid"] = 0u,
            ["CandidateWindowThreadId"] = 0u,
            ["CandidateExePath"] = string.Empty,
            ["CandidateClassName"] = string.Empty,
            ["RecoveryToken"] = token,
            ["RecoveryMode"] = "v2-presentation",
            ["Phase"] = phase,
            ["PreparedUtc"] = updated,
            ["UpdatedUtc"] = updated,
        };
}
