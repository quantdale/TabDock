using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Hermetic regression fixtures for state-file classification, schema policy,
/// and backup recovery. These tests deliberately use a caller-supplied temp
/// tree so the diagnostic self-test never reads or mutates the user's real
/// AppData state.
/// </summary>
internal static class PersistenceSelfTest
{
    internal static string? LastAccessDeniedFixtureStatus { get; private set; }

    public static (int Checks, int Failures) Run()
    {
        int checks = 0;
        int failures = 0;
        void Check(bool condition)
        {
            checks++;
            if (!condition) failures++;
        }

        string root = Path.Combine(Path.GetTempPath(), "TabDock-persistence-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var log = new LoggingService(Path.Combine(root, "logs"));

            // AppData can be unavailable even though the process itself can
            // still launch. Logging degrades to an in-memory tail, while the
            // persistence and recovery services fail closed rather than
            // pretending that an empty state is safe.
            string blockedLogPath = Path.Combine(root, "blocked-log-path");
            File.WriteAllText(blockedLogPath, "not-a-directory");
            using (var degradedLog = new LoggingService(blockedLogPath))
            {
                Check(!degradedLog.IsFileBacked);
                degradedLog.Log("memory-only diagnostic line");
                degradedLog.Dispose();
                Check(degradedLog.MemoryLines.Any(line => line.Contains("memory-only diagnostic line", StringComparison.Ordinal)));
            }

            string blockedStatePath = Path.Combine(root, "blocked-state-path", "state.json");
            File.WriteAllText(Path.GetDirectoryName(blockedStatePath)!, "not-a-directory");
            var blockedPersistence = new PersistenceService(log, blockedStatePath);
            Check(!blockedPersistence.IsStorageAvailable);
            Check(blockedPersistence.Load().Count == 0 && blockedPersistence.StateLoadFailed);
            blockedPersistence.Save(Array.Empty<Group>());
            Check(File.Exists(Path.GetDirectoryName(blockedStatePath)!));

            string blockedJournalPath = Path.Combine(root, "blocked-journal-path");
            Directory.CreateDirectory(blockedJournalPath);
            var blockedShepherd = new WindowShepherdService(log, blockedJournalPath);
            Check(!blockedShepherd.RecoveryJournalStorageAvailable);

            // Malformed primary is proven corruption: quarantine it, recover a
            // valid backup, and permit a subsequent save to establish v2.
            string malformedPrimary = Path.Combine(root, "malformed-primary.json");
            string malformedBackup = malformedPrimary + ".bak";
            File.WriteAllText(malformedPrimary, "{\"Groups\":[");
            File.WriteAllText(malformedBackup, StateJson("Backup", 2));
            var recovered = new PersistenceService(log, malformedPrimary);
            Check(recovered.Load().Count == 1);
            Check(recovered.Load().Count == 1); // cached on disk, not an empty fallback
            Check(FindCorruptCopies(malformedPrimary).Any());
            recovered.Save(new[] { new Group { Name = "Recovered" } });
            Check(File.Exists(malformedPrimary));
            Check(File.ReadAllText(malformedPrimary).Contains("\"Version\": 2", StringComparison.Ordinal));

            // JSON null is also corrupt, and must take the same quarantine /
            // backup path.
            string nullPrimary = Path.Combine(root, "null-primary.json");
            File.WriteAllText(nullPrimary, "null");
            File.WriteAllText(nullPrimary + ".bak", StateJson("NullBackup", 2));
            var nullRecovery = new PersistenceService(log, nullPrimary);
            Check(nullRecovery.Load().Count == 1);
            Check(FindCorruptCopies(nullPrimary).Any());

            // A missing primary with a valid backup is recoverable; no unknown
            // primary data exists to protect in this case.
            string missingPrimary = Path.Combine(root, "missing-primary.json");
            File.WriteAllText(missingPrimary + ".bak", StateJson("MissingBackup", 2));
            var missingRecovery = new PersistenceService(log, missingPrimary);
            Check(missingRecovery.Load().Count == 1);

            // Both files corrupt: preserve evidence and block a later empty
            // overwrite in this process.
            string bothCorrupt = Path.Combine(root, "both-corrupt.json");
            File.WriteAllText(bothCorrupt, "{");
            File.WriteAllText(bothCorrupt + ".bak", "not-json");
            var bothRecovery = new PersistenceService(log, bothCorrupt);
            Check(bothRecovery.Load().Count == 0);
            bothRecovery.Save(Array.Empty<Group>());
            Check(!File.Exists(bothCorrupt));
            Check(FindCorruptCopies(bothCorrupt).Any());
            Check(File.ReadAllText(bothCorrupt + ".bak") == "not-json");

            // A directory at the primary path is a deterministic stand-in for
            // an access-denied/unreadable primary on Windows. Backup fallback
            // is forbidden because the primary was not proven corrupt.
            string unreadablePrimary = Path.Combine(root, "unreadable-primary.json");
            Directory.CreateDirectory(unreadablePrimary);
            string unreadableBackup = unreadablePrimary + ".bak";
            File.WriteAllText(unreadableBackup, StateJson("ShouldNotRecover", 2));
            string backupBefore = File.ReadAllText(unreadableBackup);
            var unreadable = new PersistenceService(log, unreadablePrimary);
            Check(unreadable.Load().Count == 0 && unreadable.StateLoadFailed);
            unreadable.Save(Array.Empty<Group>());
            Check(Directory.Exists(unreadablePrimary));
            Check(File.ReadAllText(unreadableBackup) == backupBefore);

            // Exercise an injected access-denied primary. This specifically
            // guards against a filesystem API translating access denied into
            // "missing" and incorrectly accepting the backup, without leaving
            // a deny ACE behind in a non-elevated self-test process.
            Check(AccessDeniedPrimaryIsNotRecovered(root, log));

            // Future schema is preserved and never silently downgraded, even
            // when a valid backup exists.
            string futurePrimary = Path.Combine(root, "future-primary.json");
            string futureJson = StateJson("Future", PersistedState.CurrentVersion + 1);
            File.WriteAllText(futurePrimary, futureJson);
            File.WriteAllText(futurePrimary + ".bak", StateJson("OlderBackup", 2));
            var future = new PersistenceService(log, futurePrimary);
            Check(future.Load().Count == 0 && future.StateLoadFailed);
            future.Save(Array.Empty<Group>());
            Check(File.ReadAllText(futurePrimary) == futureJson);

            // A valid primary always wins over an older backup.
            string validPrimary = Path.Combine(root, "valid-primary.json");
            File.WriteAllText(validPrimary, StateJson("Primary", 2));
            File.WriteAllText(validPrimary + ".bak", StateJson("Older", 1));
            var valid = new PersistenceService(log, validPrimary);
            Check(valid.Load().Single().Name == "Primary");

            // Version 1 is a supported in-memory migration and is rewritten as
            // v2 only when a later durable save is explicitly requested.
            string v1Path = Path.Combine(root, "v1.json");
            File.WriteAllText(v1Path, StateJson("Migrated", 1));
            var v1 = new PersistenceService(log, v1Path);
            Check(v1.Load().Single().Name == "Migrated");
            v1.Save(v1.Load());
            Check(File.ReadAllText(v1Path).Contains("\"Version\": 2", StringComparison.Ordinal));

            // Fresh group shells are session-only. They must not be written,
            // while a group carrying persisted tab intent must survive the
            // same save. This is the regression for repeated zero-tab groups.
            string emptyGroupPath = Path.Combine(root, "empty-group-filter.json");
            var freshEmpty = new Group { Name = "Fresh shell" };
            var materialized = new Group { Name = "Materialized" };
            materialized.PersistedTabs.Add(new PersistedTabMetadata
            {
                ExePath = @"C:\\Apps\\materialized.exe",
                OriginalTitle = "Materialized tab",
                Left = 10,
                Top = 20,
                Right = 810,
                Bottom = 620,
            });
            var emptyFilter = new PersistenceService(log, emptyGroupPath);
            emptyFilter.Save(new[] { freshEmpty, materialized });
            PersistedState? filteredState = JsonSerializer.Deserialize(
                File.ReadAllText(emptyGroupPath),
                TabDockJsonContext.Default.PersistedState);
            Check(filteredState?.Groups.Count == 1);
            Check(filteredState?.Groups[0].Name == "Materialized"
                && filteredState.Groups[0].Tabs.Count == 1);

            // A legacy valid record with no tabs is ignored on load, but a
            // sibling record with actual layout intent remains recoverable.
            string legacyEmptyPath = Path.Combine(root, "legacy-empty-group.json");
            File.WriteAllText(legacyEmptyPath, StateJsonWithEmptyAndMaterializedGroups());
            var legacyEmpty = new PersistenceService(log, legacyEmptyPath);
            List<Group> restoredLegacy = legacyEmpty.Load();
            Check(restoredLegacy.Count == 1);
            Check(restoredLegacy[0].Name == "Legacy materialized"
                && restoredLegacy[0].PersistedTabs.Count == 1);

            // A malformed nested record is salvaged at record granularity.
            // The root names deliberately vary in case so manual
            // classification and the source-generated serializer exercise the
            // same case-insensitive contract.
            Check(NestedMalformedRecordsAreSalvagedAndSaved(root, log));

            string mixedCaseFuturePath = Path.Combine(root, "mixed-case-future.json");
            string mixedCaseFuture = "{\"vErSiOn\":3,\"gRoUpS\":[]}";
            File.WriteAllText(mixedCaseFuturePath, mixedCaseFuture);
            var mixedCaseFutureService = new PersistenceService(log, mixedCaseFuturePath);
            Check(mixedCaseFutureService.Load().Count == 0 && mixedCaseFutureService.StateLoadFailed);
            mixedCaseFutureService.Save(Array.Empty<Group>());
            Check(File.ReadAllText(mixedCaseFuturePath) == mixedCaseFuture);
        }
        catch
        {
            failures++;
            checks++;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // The test result is already recorded; cleanup failure must not
                // turn a deterministic self-test into a process crash.
            }
        }

        return (checks, failures);
    }

    private static string StateJson(string name, int version, bool includeTab = true)
    {
        var state = new PersistedState
        {
            Version = version,
            Groups = new()
            {
                new PersistedGroup
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    AccentColor = "#2196F3",
                    ActiveIndex = 0,
                },
            },
        };
        if (includeTab)
        {
            state.Groups[0].Tabs.Add(new PersistedTab
            {
                ExePath = @"C:\\Apps\\fixture.exe",
                OriginalTitle = name + " tab",
                Left = 10,
                Top = 20,
                Right = 810,
                Bottom = 620,
            });
        }
        return JsonSerializer.Serialize(state, TabDockJsonContext.Default.PersistedState);
    }

    private static bool NestedMalformedRecordsAreSalvagedAndSaved(string root, LoggingService log)
    {
        string path = Path.Combine(root, "nested-malformed.json");
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        var firstTabs = new JsonArray
        {
            new JsonObject
            {
                ["eXePaTh"] = "C:/Apps/first-a.exe",
                ["oRiGiNaLtItLe"] = "first a",
                ["lEfT"] = 10,
                ["tOp"] = 20,
                ["rIgHt"] = 410,
                ["bOtToM"] = 320,
            },
            null,
            new JsonObject
            {
                ["eXePaTh"] = new JsonArray("not", "a", "string"),
                ["lEfT"] = 30,
            },
            new JsonObject
            {
                ["eXePaTh"] = "C:/Apps/first-b.exe",
                ["oRiGiNaLtItLe"] = "first b",
                ["lEfT"] = 30,
                ["tOp"] = 40,
                ["rIgHt"] = 430,
                ["bOtToM"] = 340,
            },
        };
        var rootNode = new JsonObject
        {
            ["version"] = 2,
            ["GROUPS"] = new JsonArray
            {
                null,
                new JsonObject
                {
                    ["iD"] = firstId.ToString(),
                    ["nAmE"] = "Salvaged first",
                    ["aCcEnTcOlOr"] = "#2196F3",
                    ["aCtIvEiNdEx"] = 99,
                    ["tAbS"] = firstTabs,
                },
                new JsonObject
                {
                    ["iD"] = secondId.ToString(),
                    ["nAmE"] = "Valid second",
                    ["tAbS"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["eXePaTh"] = "C:/Apps/second.exe",
                            ["oRiGiNaLtItLe"] = "second",
                            ["lEfT"] = 50,
                            ["tOp"] = 60,
                            ["rIgHt"] = 450,
                            ["bOtToM"] = 360,
                        },
                    },
                },
                new JsonObject
                {
                    ["iD"] = Guid.NewGuid().ToString(),
                    ["nAmE"] = "Malformed group",
                    ["tAbS"] = "not-an-array",
                },
            },
        };
        File.WriteAllText(path, rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var persistence = new PersistenceService(log, path);
        List<Group> restored = persistence.Load();
        if (persistence.StateLoadFailed
            || restored.Count != 2
            || restored[0].Name != "Salvaged first"
            || restored[0].PersistedTabs.Count != 2
            || restored[0].PersistedActiveIndex != 1
            || restored[1].Name != "Valid second"
            || restored[1].PersistedTabs.Count != 1)
        {
            return false;
        }

        persistence.Save(restored);
        PersistedState? saved = JsonSerializer.Deserialize(
            File.ReadAllText(path),
            TabDockJsonContext.Default.PersistedState);
        return saved?.Groups.Count == 2
            && saved.Groups[0].Tabs.Count == 2
            && saved.Groups[0].ActiveIndex == 1
            && saved.Groups[1].Tabs.Count == 1
            && saved.Groups.All(group => group.Tabs.All(tab => tab != null));
    }

    private static string StateJsonWithEmptyAndMaterializedGroups()
    {
        var state = new PersistedState
        {
            Version = PersistedState.CurrentVersion,
            Groups = new()
            {
                new PersistedGroup
                {
                    Id = Guid.NewGuid(),
                    Name = "Legacy shell",
                    AccentColor = "#2196F3",
                    ActiveIndex = 0,
                },
                new PersistedGroup
                {
                    Id = Guid.NewGuid(),
                    Name = "Legacy materialized",
                    AccentColor = "#2196F3",
                    ActiveIndex = 0,
                    Tabs = new()
                    {
                        new PersistedTab
                        {
                            ExePath = @"C:\\Apps\\legacy.exe",
                            OriginalTitle = "Legacy tab",
                            Left = 10,
                            Top = 20,
                            Right = 810,
                            Bottom = 620,
                        },
                    },
                },
            },
        };
        return JsonSerializer.Serialize(state, TabDockJsonContext.Default.PersistedState);
    }

    private static string[] FindCorruptCopies(string primary)
        => Directory.EnumerateFiles(
                Path.GetDirectoryName(primary)!,
            Path.GetFileName(primary) + ".corrupt.*")
            .ToArray();

    private static bool AccessDeniedPrimaryIsNotRecovered(string root, LoggingService log)
    {
        string primary = Path.Combine(root, "acl-denied-primary.json");
        string backup = primary + ".bak";
        string primaryJson = StateJson("AclPrimary", 2);
        string backupJson = StateJson("AclBackup", 2);
        File.WriteAllText(primary, primaryJson);
        File.WriteAllText(backup, backupJson);
        string primaryFullPath = Path.GetFullPath(primary);
        var persistence = new PersistenceService(
            log,
            primary,
            path => string.Equals(Path.GetFullPath(path), primaryFullPath, StringComparison.OrdinalIgnoreCase)
                ? throw new UnauthorizedAccessException("self-test access-denied fixture")
                : File.GetAttributes(path),
            path => string.Equals(Path.GetFullPath(path), primaryFullPath, StringComparison.OrdinalIgnoreCase)
                ? throw new UnauthorizedAccessException("self-test access-denied fixture")
                : File.ReadAllText(path));

        bool safe = persistence.Load().Count == 0 && persistence.StateLoadFailed;
        bool preserved = File.ReadAllText(primary) == primaryJson
            && File.ReadAllText(backup) == backupJson;
        LastAccessDeniedFixtureStatus = safe && preserved ? "pass" : "denial-not-observed";
        return safe && preserved;
    }

}
