using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Migrated from the former PersistenceSelfTest (Wave 4): state-file
/// classification, schema policy, and backup recovery. Every fixture uses a
/// caller-owned temp tree; the former access-denied fixture keeps its injected
/// UnauthorizedAccessException delegates, so it runs deterministically on every
/// machine without leaving a deny ACE behind in a non-elevated test process.
/// </summary>
public class PersistenceServiceClassificationTests : IDisposable
{
    private readonly string _root;
    private readonly LoggingService _log;

    public PersistenceServiceClassificationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TabDock-persistence-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _log = new LoggingService(Path.Combine(_root, "logs"));
    }

    public void Dispose()
    {
        _log.Dispose();
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void LoggingService_WhenLogPathIsBlocked_DegradesToMemoryTail()
    {
        // AppData can be unavailable even though the process itself can still
        // launch: logging degrades to an in-memory tail instead of crashing.
        string blockedLogPath = Path.Combine(_root, "blocked-log-path");
        File.WriteAllText(blockedLogPath, "not-a-directory");
        var degradedLog = new LoggingService(blockedLogPath);

        Assert.False(degradedLog.IsFileBacked);
        degradedLog.Log("memory-only diagnostic line");
        degradedLog.Dispose();

        Assert.Contains(
            degradedLog.MemoryLines,
            line => line.Contains("memory-only diagnostic line", StringComparison.Ordinal));
    }

    [Fact]
    public void PersistenceService_WhenStatePathIsAFile_FailsClosedAndSaveRecreatesDirectory()
    {
        string blockedStatePath = Path.Combine(_root, "blocked-state-path", "state.json");
        File.WriteAllText(Path.GetDirectoryName(blockedStatePath)!, "not-a-directory");
        var persistence = new PersistenceService(_log, blockedStatePath);

        Assert.False(persistence.IsStorageAvailable);
        Assert.Empty(persistence.Load());
        Assert.True(persistence.StateLoadFailed);

        // R21-016: CommitJson recreates a deleted state directory.
        persistence.Save(Array.Empty<Group>());
        Assert.True(File.Exists(Path.GetDirectoryName(blockedStatePath)));
    }

    [Fact]
    public void ShepherdJournal_WhenJournalPathIsADirectory_ReportsStorageUnavailable()
    {
        string blockedJournalPath = Path.Combine(_root, "blocked-journal-path");
        Directory.CreateDirectory(blockedJournalPath);
        var shepherd = new WindowShepherdService(_log, blockedJournalPath);
        Assert.False(shepherd.RecoveryJournalStorageAvailable);
    }

    [Fact]
    public void MalformedPrimaryWithValidBackup_IsQuarantinedRecoveredAndNextSaveEstablishesV2()
    {
        string malformedPrimary = Path.Combine(_root, "malformed-primary.json");
        File.WriteAllText(malformedPrimary, "{\"Groups\":[");
        File.WriteAllText(malformedPrimary + ".bak", StateJson("Backup", 2));
        var recovered = new PersistenceService(_log, malformedPrimary);

        List<Group> groups = recovered.Load();
        Assert.Single(groups);
        // Cached from disk, not an empty fallback.
        Assert.Single(recovered.Load());
        Assert.NotEmpty(FindCorruptCopies(malformedPrimary));

        recovered.Save(new[] { new Group { Name = "Recovered" } });
        Assert.True(File.Exists(malformedPrimary));
        Assert.Contains("\"Version\": 2", File.ReadAllText(malformedPrimary), StringComparison.Ordinal);
    }

    [Fact]
    public void NullJsonPrimary_IsCorruptAndTakesTheQuarantineBackupPath()
    {
        string nullPrimary = Path.Combine(_root, "null-primary.json");
        File.WriteAllText(nullPrimary, "null");
        File.WriteAllText(nullPrimary + ".bak", StateJson("NullBackup", 2));
        var recovery = new PersistenceService(_log, nullPrimary);

        Assert.Single(recovery.Load());
        Assert.NotEmpty(FindCorruptCopies(nullPrimary));
    }

    [Fact]
    public void MissingPrimaryWithValidBackup_IsRecoverable()
    {
        string missingPrimary = Path.Combine(_root, "missing-primary.json");
        File.WriteAllText(missingPrimary + ".bak", StateJson("MissingBackup", 2));
        var recovery = new PersistenceService(_log, missingPrimary);
        Assert.Single(recovery.Load());
    }

    [Fact]
    public void BothFilesCorrupt_EvidencePreservedAndOverwriteBlocked()
    {
        string bothCorrupt = Path.Combine(_root, "both-corrupt.json");
        File.WriteAllText(bothCorrupt, "{");
        File.WriteAllText(bothCorrupt + ".bak", "not-json");
        var recovery = new PersistenceService(_log, bothCorrupt);

        Assert.Empty(recovery.Load());
        recovery.Save(Array.Empty<Group>());
        Assert.False(File.Exists(bothCorrupt));
        Assert.NotEmpty(FindCorruptCopies(bothCorrupt));
        Assert.Equal("not-json", File.ReadAllText(bothCorrupt + ".bak"));
    }

    [Fact]
    public void UnreadableDirectoryShapedPrimary_BackupFallbackForbidden()
    {
        // A directory at the primary path is a deterministic stand-in for an
        // access-denied/unreadable primary: backup fallback is forbidden
        // because the primary was not proven corrupt.
        string unreadablePrimary = Path.Combine(_root, "unreadable-primary.json");
        Directory.CreateDirectory(unreadablePrimary);
        string backup = unreadablePrimary + ".bak";
        File.WriteAllText(backup, StateJson("ShouldNotRecover", 2));
        string backupBefore = File.ReadAllText(backup);
        var persistence = new PersistenceService(_log, unreadablePrimary);

        Assert.Empty(persistence.Load());
        Assert.True(persistence.StateLoadFailed);
        persistence.Save(Array.Empty<Group>());
        Assert.True(Directory.Exists(unreadablePrimary));
        Assert.Equal(backupBefore, File.ReadAllText(backup));
    }

    [Fact]
    public void AccessDeniedPrimary_IsNotRecoveredFromBackup()
    {
        string primary = Path.Combine(_root, "acl-denied-primary.json");
        string backup = primary + ".bak";
        string primaryJson = StateJson("AclPrimary", 2);
        string backupJson = StateJson("AclBackup", 2);
        File.WriteAllText(primary, primaryJson);
        File.WriteAllText(backup, backupJson);
        string primaryFullPath = Path.GetFullPath(primary);
        var persistence = new PersistenceService(
            _log,
            primary,
            path => string.Equals(Path.GetFullPath(path), primaryFullPath, StringComparison.OrdinalIgnoreCase)
                ? throw new UnauthorizedAccessException("test access-denied fixture")
                : File.GetAttributes(path),
            path => string.Equals(Path.GetFullPath(path), primaryFullPath, StringComparison.OrdinalIgnoreCase)
                ? throw new UnauthorizedAccessException("test access-denied fixture")
                : File.ReadAllText(path));

        // Guards against a filesystem API translating access denied into
        // "missing" and incorrectly accepting the backup.
        Assert.Empty(persistence.Load());
        Assert.True(persistence.StateLoadFailed);
        Assert.Equal(primaryJson, File.ReadAllText(primary));
        Assert.Equal(backupJson, File.ReadAllText(backup));
    }

    [Fact]
    public void FutureSchemaPrimary_PreservedAndNeverDowngradedEvenWithValidBackup()
    {
        string futurePrimary = Path.Combine(_root, "future-primary.json");
        string futureJson = StateJson("Future", PersistedState.CurrentVersion + 1);
        File.WriteAllText(futurePrimary, futureJson);
        File.WriteAllText(futurePrimary + ".bak", StateJson("OlderBackup", 2));
        var future = new PersistenceService(_log, futurePrimary);

        Assert.Empty(future.Load());
        Assert.True(future.StateLoadFailed);
        future.Save(Array.Empty<Group>());
        Assert.Equal(futureJson, File.ReadAllText(futurePrimary));
    }

    [Fact]
    public void ValidPrimary_WinsOverOlderBackup()
    {
        string validPrimary = Path.Combine(_root, "valid-primary.json");
        File.WriteAllText(validPrimary, StateJson("Primary", 2));
        File.WriteAllText(validPrimary + ".bak", StateJson("Older", 1));
        var valid = new PersistenceService(_log, validPrimary);
        Assert.Equal("Primary", valid.Load().Single().Name);
    }

    [Fact]
    public void Version1_MigratesInMemoryAndRewritesV2OnlyOnExplicitSave()
    {
        string v1Path = Path.Combine(_root, "v1.json");
        File.WriteAllText(v1Path, StateJson("Migrated", 1));
        var v1 = new PersistenceService(_log, v1Path);

        Assert.Equal("Migrated", v1.Load().Single().Name);
        v1.Save(v1.Load());
        Assert.Contains("\"Version\": 2", File.ReadAllText(v1Path), StringComparison.Ordinal);
    }

    [Fact]
    public void Save_FreshEmptyGroupShellsOmittedWhileMaterializedIntentSurvives()
    {
        string emptyGroupPath = Path.Combine(_root, "empty-group-filter.json");
        var freshEmpty = new Group { Name = "Fresh shell" };
        var materialized = new Group { Name = "Materialized" };
        materialized.PersistedTabs.Add(new PersistedTabMetadata
        {
            ExePath = @"C:\Apps\materialized.exe",
            OriginalTitle = "Materialized tab",
            Left = 10,
            Top = 20,
            Right = 810,
            Bottom = 620,
        });
        var filter = new PersistenceService(_log, emptyGroupPath);
        filter.Save(new[] { freshEmpty, materialized });

        PersistedState? state = JsonSerializer.Deserialize(
            File.ReadAllText(emptyGroupPath),
            TabDockJsonContext.Default.PersistedState);
        Assert.NotNull(state);
        Assert.Single(state!.Groups);
        Assert.Equal("Materialized", state.Groups[0].Name);
        Assert.Single(state.Groups[0].Tabs);
    }

    [Fact]
    public void Load_LegacyZeroTabRecordIgnoredWhileMaterializedSiblingRecovers()
    {
        string legacyEmptyPath = Path.Combine(_root, "legacy-empty-group.json");
        File.WriteAllText(legacyEmptyPath, StateJsonWithEmptyAndMaterializedGroups());
        var legacy = new PersistenceService(_log, legacyEmptyPath);

        List<Group> restored = legacy.Load();
        Assert.Single(restored);
        Assert.Equal("Legacy materialized", restored[0].Name);
        Assert.Single(restored[0].PersistedTabs);
    }

    [Fact]
    public void NestedMalformedRecords_AreSalvagedAtRecordGranularityAndSavedCleanly()
    {
        // The root names deliberately vary in case so manual classification and
        // the source-generated serializer exercise the same case-insensitive
        // contract.
        string path = Path.Combine(_root, "nested-malformed.json");
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

        var persistence = new PersistenceService(_log, path);
        List<Group> restored = persistence.Load();

        Assert.False(persistence.StateLoadFailed);
        Assert.Equal(2, restored.Count);
        Assert.Equal("Salvaged first", restored[0].Name);
        Assert.Equal(2, restored[0].PersistedTabs.Count);
        Assert.Equal(1, restored[0].PersistedActiveIndex);
        Assert.Equal("Valid second", restored[1].Name);
        Assert.Single(restored[1].PersistedTabs);

        persistence.Save(restored);
        PersistedState? saved = JsonSerializer.Deserialize(
            File.ReadAllText(path),
            TabDockJsonContext.Default.PersistedState);
        Assert.NotNull(saved);
        Assert.Equal(2, saved!.Groups.Count);
        Assert.Equal(2, saved.Groups[0].Tabs.Count);
        Assert.Equal(1, saved.Groups[0].ActiveIndex);
        Assert.Single(saved.Groups[1].Tabs);
        Assert.All(saved.Groups, group => Assert.All(group.Tabs, tab => Assert.NotNull(tab)));
    }

    [Fact]
    public void MixedCaseFutureVersion_IsPreservedAndBlocksLaterSaves()
    {
        string path = Path.Combine(_root, "mixed-case-future.json");
        const string mixedCaseFuture = "{\"vErSiOn\":3,\"gRoUpS\":[]}";
        File.WriteAllText(path, mixedCaseFuture);
        var service = new PersistenceService(_log, path);

        Assert.Empty(service.Load());
        Assert.True(service.StateLoadFailed);
        service.Save(Array.Empty<Group>());
        Assert.Equal(mixedCaseFuture, File.ReadAllText(path));
    }

    private static string StateJson(string name, int version)
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
                    Tabs =
                    {
                        new PersistedTab
                        {
                            ExePath = @"C:\Apps\fixture.exe",
                            OriginalTitle = name + " tab",
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
                            ExePath = @"C:\Apps\legacy.exe",
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
}
