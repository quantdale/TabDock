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
/// A01 regression coverage: a present but wrongly typed root Version must be
/// classified as corruption, not escape Load as a type-conversion exception.
/// The load boundary is driven through GroupManager's real restore and
/// emergency/exit save paths so a parsed-but-unsaved result cannot pass.
/// </summary>
public sealed class PersistenceMalformedVersionRecoveryTests : IDisposable
{
    private readonly string _root;
    private readonly LoggingService _log;
    private readonly string _statePath;
    private readonly string _journalPath;

    public PersistenceMalformedVersionRecoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TabDock-version-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _log = new LoggingService(Path.Combine(_root, "logs"));
        _statePath = Path.Combine(_root, "state.json");
        _journalPath = Path.Combine(_root, "hidden-windows.json");
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

    [Theory]
    [InlineData("\"oops\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("1.5")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("999999999999999999999999")]
    public void InvalidVersionToken_IsClassifiedCorruptAndRecoversPreservedBackup(string versionToken)
    {
        string originalPrimary = StateJsonWithRawVersion(versionToken, "Corrupt primary");
        string backupJson = StateJson("Recovered backup");
        File.WriteAllText(_statePath, originalPrimary);
        File.WriteAllText(_statePath + ".bak", backupJson);
        var persistence = new PersistenceService(_log, _statePath);

        List<Group> restored = persistence.Load();

        Group recovered = Assert.Single(restored);
        Assert.Equal("Recovered backup", recovered.Name);
        Assert.Single(recovered.PersistedTabs);
        Assert.False(persistence.StateLoadFailed);
        Assert.Equal(backupJson, File.ReadAllText(_statePath + ".bak"));
        Assert.Contains(FindCorruptCopies(), path => File.ReadAllText(path) == originalPrimary);
    }

    [Theory]
    [InlineData("\"oops\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void WrongTypeVersion_WithValidBackup_SurvivesEmergencyAndExitSaves(string versionToken)
    {
        string originalPrimary = StateJsonWithRawVersion(versionToken, "Corrupt primary");
        string backupJson = StateJson("Recovered backup");
        File.WriteAllText(_statePath, originalPrimary);
        File.WriteAllText(_statePath + ".bak", backupJson);
        var persistence = new PersistenceService(_log, _statePath);
        var manager = new GroupManager(
            new WindowShepherdService(_log, _journalPath),
            persistence,
            _log);

        manager.RestoreState();
        manager.EmergencyReleaseAll();
        manager.SaveState();

        Assert.False(persistence.StateLoadFailed);
        Assert.Contains(FindCorruptCopies(), path => File.ReadAllText(path) == originalPrimary);
        Assert.Equal(backupJson, File.ReadAllText(_statePath + ".bak"));

        PersistedState? saved = JsonSerializer.Deserialize(
            File.ReadAllText(_statePath),
            TabDockJsonContext.Default.PersistedState);
        Assert.NotNull(saved);
        PersistedGroup group = Assert.Single(saved!.Groups);
        Assert.Equal("Recovered backup", group.Name);
        Assert.Single(group.Tabs);
        Assert.Equal("Recovered backup tab", group.Tabs[0].OriginalTitle);
    }

    [Fact]
    public void QuarantineFailure_PreservesPrimaryAndBackupAndBlocksCleanupSave()
    {
        string originalPrimary = StateJsonWithRawVersion("\"oops\"", "Corrupt primary");
        string backupJson = StateJson("Recovered backup");
        File.WriteAllText(_statePath, originalPrimary);
        File.WriteAllText(_statePath + ".bak", backupJson);
        var persistence = new PersistenceService(_log, _statePath);
        var manager = new GroupManager(
            new WindowShepherdService(_log, _journalPath),
            persistence,
            _log);

        using (var denyDelete = new FileStream(
            _statePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            manager.RestoreState();
            Assert.Empty(manager.Groups);
            Assert.True(persistence.StateLoadFailed);

            manager.EmergencyReleaseAll();
            manager.SaveState();
        }

        Assert.Equal(originalPrimary, File.ReadAllText(_statePath));
        Assert.Equal(backupJson, File.ReadAllText(_statePath + ".bak"));
        Assert.Empty(FindCorruptCopies());
    }

    [Fact]
    public void UnreadableBackupAfterQuarantine_PreservesBackupBytesAndBlocksCleanupSave()
    {
        string originalPrimary = StateJsonWithRawVersion("\"oops\"", "Corrupt primary");
        string backupJson = StateJson("Recovered backup");
        File.WriteAllText(_statePath, originalPrimary);
        File.WriteAllText(_statePath + ".bak", backupJson);
        string backupFullPath = Path.GetFullPath(_statePath + ".bak");
        var persistence = new PersistenceService(
            _log,
            _statePath,
            path => File.GetAttributes(path),
            path => string.Equals(Path.GetFullPath(path), backupFullPath, StringComparison.OrdinalIgnoreCase)
                ? throw new UnauthorizedAccessException("test unreadable-backup fixture")
                : File.ReadAllText(path));

        Assert.Empty(persistence.Load());
        Assert.True(persistence.StateLoadFailed);
        persistence.Save(Array.Empty<Group>());

        Assert.Equal(backupJson, File.ReadAllText(_statePath + ".bak"));
        Assert.Contains(FindCorruptCopies(), path => File.ReadAllText(path) == originalPrimary);
    }

    [Fact]
    public void MissingVersion_RestoresLegacyAndCleanupSaveRetainsMaterializedMetadata()
    {
        string originalPrimary = StateJsonWithRawVersion(null, "Legacy group");
        File.WriteAllText(_statePath, originalPrimary);
        var persistence = new PersistenceService(_log, _statePath);
        var manager = new GroupManager(
            new WindowShepherdService(_log, _journalPath),
            persistence,
            _log);

        manager.RestoreState();
        Group restored = Assert.Single(manager.Groups);
        Assert.Equal("Legacy group", restored.Name);
        Assert.Single(restored.PersistedTabs);

        manager.EmergencyReleaseAll();
        manager.SaveState();

        PersistedState? saved = JsonSerializer.Deserialize(
            File.ReadAllText(_statePath),
            TabDockJsonContext.Default.PersistedState);
        Assert.NotNull(saved);
        Assert.Equal(PersistedState.CurrentVersion, saved!.Version);
        PersistedGroup group = Assert.Single(saved.Groups);
        Assert.Equal("Legacy group", group.Name);
        PersistedTab tab = Assert.Single(group.Tabs);
        Assert.Equal("Legacy group tab", tab.OriginalTitle);
        Assert.False(persistence.StateLoadFailed);
    }

    [Fact]
    public void RepresentableFutureVersion_IsUnsupportedAndPreservedThroughCleanupSaves()
    {
        string futureJson = StateJsonWithRawVersion(
            (PersistedState.CurrentVersion + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Future");
        string backupJson = StateJson("Older backup");
        File.WriteAllText(_statePath, futureJson);
        File.WriteAllText(_statePath + ".bak", backupJson);
        var persistence = new PersistenceService(_log, _statePath);
        var manager = new GroupManager(
            new WindowShepherdService(_log, _journalPath),
            persistence,
            _log);

        manager.RestoreState();
        Assert.Empty(manager.Groups);
        Assert.True(persistence.StateLoadFailed);

        manager.EmergencyReleaseAll();
        manager.SaveState();

        Assert.Equal(futureJson, File.ReadAllText(_statePath));
        Assert.Equal(backupJson, File.ReadAllText(_statePath + ".bak"));
        Assert.Empty(FindCorruptCopies());
    }

    private static string StateJson(string groupName, int version = PersistedState.CurrentVersion)
    {
        var state = new PersistedState
        {
            Version = version,
            Groups = new()
            {
                new PersistedGroup
                {
                    Id = Guid.NewGuid(),
                    Name = groupName,
                    AccentColor = "#2196F3",
                    ActiveIndex = 0,
                    Tabs =
                    {
                        new PersistedTab
                        {
                            ExePath = @"C:\Apps\fixture.exe",
                            OriginalTitle = groupName + " tab",
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

    private static string StateJsonWithRawVersion(string? rawVersionToken, string groupName)
    {
        var root = new JsonObject
        {
            ["Groups"] = new JsonArray(new JsonObject
            {
                ["Id"] = Guid.NewGuid().ToString("D"),
                ["Name"] = groupName,
                ["AccentColor"] = "#2196F3",
                ["ActiveIndex"] = 0,
                ["Tabs"] = new JsonArray(new JsonObject
                {
                    ["ExePath"] = @"C:\Apps\fixture.exe",
                    ["OriginalTitle"] = groupName + " tab",
                    ["Left"] = 10,
                    ["Top"] = 20,
                    ["Right"] = 810,
                    ["Bottom"] = 620,
                }),
            }),
        };
        if (rawVersionToken != null)
            root["Version"] = rawVersionToken == "null" ? null : JsonNode.Parse(rawVersionToken);
        return root.ToJsonString();
    }

    private string[] FindCorruptCopies()
        => Directory.EnumerateFiles(
                _root,
                Path.GetFileName(_statePath) + ".corrupt.*")
            .ToArray();
}
