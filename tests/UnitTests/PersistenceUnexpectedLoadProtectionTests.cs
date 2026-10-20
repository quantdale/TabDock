using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TabDock.Models;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// A01 safety-boundary coverage for the outer PersistenceService.Load catch:
/// a load failure outside the inner read-error classifier must latch save
/// protection so later sync, async, manager emergency and exit-style empty
/// saves cannot replace unresolved evidence. The fault is an injected text
/// reader that returns null, provoking a parse argument failure that bypasses
/// the inner UnauthorizedAccess/IOException/JsonException classification
/// entirely; removing the outer catch makes these tests throw instead of pass.
/// </summary>
public sealed class PersistenceUnexpectedLoadProtectionTests : IDisposable
{
    private readonly string _root;
    private readonly LoggingService _log;
    private readonly string _statePath;
    private readonly string _journalPath;

    public PersistenceUnexpectedLoadProtectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TabDock-load-protection-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// Real path classification with an injected reader that returns null for
    /// the observed file, so only the text-read seam carries the fault.
    /// </summary>
    private PersistenceService CreateServiceWithNullReader(string observedPath)
    {
        string observedFullPath = Path.GetFullPath(observedPath);
        return new PersistenceService(
            _log,
            _statePath,
            path => File.GetAttributes(path),
            path => string.Equals(Path.GetFullPath(path), observedFullPath, StringComparison.OrdinalIgnoreCase)
                ? null!
                : File.ReadAllText(path));
    }

    private static Group MakeGroup(string name)
    {
        var g = new Group { Name = name };
        g.Members.Add(new CapturedWindow
        {
            Hwnd = new IntPtr(0x9000),
            ProcessId = 9001u,
            WindowThreadId = 9002u,
            WindowIdentityToken = 9003,
            ExePath = @"C:\Apps\fixture.exe",
            OriginalClassName = "Pig",
            OriginalTitle = name + " window",
        });
        return g;
    }

    private static string StateJson(string groupName)
    {
        var state = new PersistedState
        {
            Version = PersistedState.CurrentVersion,
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

    private void WriteEvidence()
    {
        File.WriteAllText(_statePath, StateJson("Evidence group"));
        File.WriteAllText(_statePath + ".bak", StateJson("Backup group"));
    }

    private void AssertEvidenceUnchanged(string primaryJson, string backupJson)
    {
        Assert.Equal(primaryJson, File.ReadAllText(_statePath));
        Assert.Equal(backupJson, File.ReadAllText(_statePath + ".bak"));
        Assert.False(File.Exists(_statePath + ".tmp"), "no primary temp may be produced while protection is latched");
        Assert.False(File.Exists(_statePath + ".bak.tmp"), "no backup temp may be produced while protection is latched");
        Assert.Empty(FindCorruptCopies());
    }

    private string[] FindCorruptCopies()
        => Directory.EnumerateFiles(
                _root,
                Path.GetFileName(_statePath) + ".corrupt.*")
            .ToArray();

    [Fact]
    public async System.Threading.Tasks.Task UnexpectedLoadFailure_LatchesProtectionAndBlocksDirectSaves()
    {
        WriteEvidence();
        string primaryJson = File.ReadAllText(_statePath);
        string backupJson = File.ReadAllText(_statePath + ".bak");
        var persistence = CreateServiceWithNullReader(_statePath);

        List<Group> restored = persistence.Load();

        Assert.Empty(restored);
        Assert.True(persistence.StateLoadFailed, "an unexpected load failure must latch save protection");

        // Every later save shape that could replace evidence must be refused.
        persistence.Save(new[] { MakeGroup("replacement-sync") });
        AssertEvidenceUnchanged(primaryJson, backupJson);

        persistence.Save(Array.Empty<Group>());
        AssertEvidenceUnchanged(primaryJson, backupJson);

        persistence.SaveAsync(new[] { MakeGroup("replacement-async") });
        await persistence.WhenWritesSettledAsync();
        AssertEvidenceUnchanged(primaryJson, backupJson);

        persistence.SaveAsync(Array.Empty<Group>());
        await persistence.WhenWritesSettledAsync();
        AssertEvidenceUnchanged(primaryJson, backupJson);
    }

    [Fact]
    public void UnexpectedLoadFailure_ManagerEmergencyAndExitSavesCannotReplaceEvidence()
    {
        WriteEvidence();
        string primaryJson = File.ReadAllText(_statePath);
        string backupJson = File.ReadAllText(_statePath + ".bak");
        var persistence = CreateServiceWithNullReader(_statePath);
        var manager = new GroupManager(
            new WindowShepherdService(_log, _journalPath),
            persistence,
            _log);

        manager.RestoreState();

        Assert.Empty(manager.Groups);
        Assert.True(persistence.StateLoadFailed);

        manager.EmergencyReleaseAll();
        manager.SaveState();
        AssertEvidenceUnchanged(primaryJson, backupJson);

        manager.RequestDurableSave("unexpected-load-protection test");
        AssertEvidenceUnchanged(primaryJson, backupJson);
    }
}
