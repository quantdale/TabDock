using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Saves and restores group metadata to %APPDATA%\TabDock\state.json.
/// Only metadata is persisted; live HWNDs are intentionally not reattached.
/// </summary>
public sealed class PersistenceService
{
    private readonly LoggingService _log;
    private readonly string _statePath;

    // The exact JSON last written to disk, so an unchanged save can skip the
    // write + atomic-rename round trip entirely (PERF25-07).
    private string? _lastSavedJson;

    // A read/access failure is not evidence that the user's state is empty.
    // Block later saves in that process so an exit path cannot replace an
    // unreadable but potentially valid state file with an empty one.
    private bool _stateLoadFailed;

    public PersistenceService(LoggingService log)
    {
        _log = log;
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "TabDock");
        Directory.CreateDirectory(dir);
        _statePath = Path.Combine(dir, "state.json");
    }

    public void Save(IEnumerable<Group> groups)
    {
        try
        {
            if (_stateLoadFailed)
            {
                _log.Log("PersistenceService.Save skipped because the existing state could not be read safely this session.");
                return;
            }

            var state = new PersistedState();
            foreach (var g in groups)
            {
                var pg = new PersistedGroup
                {
                    Id = g.Id,
                    Name = g.Name,
                    AccentColor = g.AccentColor,
                    // Mirrors the Tabs handling below: a group with no live members
                    // has only loaded metadata, and Group.ActiveIndex clamps to -1
                    // against an empty Members collection, so reading it here would
                    // overwrite the restored index with -1 on the first save.
                    ActiveIndex = g.Members.Count > 0 ? g.ActiveIndex : g.PersistedActiveIndex,
                };
                if (g.Members.Count > 0)
                {
                    foreach (var m in g.Members)
                    {
                        pg.Tabs.Add(new PersistedTab
                        {
                            ExePath = m.ExePath,
                            OriginalTitle = m.OriginalTitle,
                            CustomLabel = m.CustomLabel,
                            Left = m.OriginalBounds.left,
                            Top = m.OriginalBounds.top,
                            Right = m.OriginalBounds.right,
                            Bottom = m.OriginalBounds.bottom,
                            WasMaximized = m.WasMaximized,
                        });
                    }
                }
                else
                {
                    // A restored group that has not been re-populated has no live
                    // members, only loaded metadata. Carry that metadata forward so
                    // saves (now frequent — they are debounced onto every state
                    // change, not just clean exit) cannot wipe the layout intent.
                    foreach (var pm in g.PersistedTabs)
                    {
                        pg.Tabs.Add(new PersistedTab
                        {
                            ExePath = pm.ExePath,
                            OriginalTitle = pm.OriginalTitle,
                            CustomLabel = pm.CustomLabel,
                            Left = pm.Left,
                            Top = pm.Top,
                            Right = pm.Right,
                            Bottom = pm.Bottom,
                            WasMaximized = pm.WasMaximized,
                        });
                    }
                }
                state.Groups.Add(pg);
            }

            string json = JsonSerializer.Serialize(state, TabDockJsonContext.Default.PersistedState);

            // Saves are debounced onto every state change and then repeated
            // outright by the exit/crash paths, so the same bytes are commonly
            // written several times in a row. Skip the write when nothing
            // changed (PERF25-07) — the file must still be on disk for the skip
            // to be safe, otherwise a state.json deleted underneath a running
            // TabDock would never be recreated.
            if (string.Equals(json, _lastSavedJson, StringComparison.Ordinal) && File.Exists(_statePath))
                return;

            // Preserve the previous state file so an interrupted write or a
            // subsequent bad load never leaves the user with no recoverable copy.
            if (File.Exists(_statePath))
            {
                string backupPath = _statePath + ".bak";
                File.Copy(_statePath, backupPath, overwrite: true);
            }

            string tempPath = _statePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _statePath, overwrite: true);
            _lastSavedJson = json;
            _log.Log($"Saved {state.Groups.Count} group(s) to {_statePath}");
        }
        catch (Exception ex)
        {
            _log.LogException("PersistenceService.Save", ex);
        }
    }

    public List<Group> Load()
    {
        var result = new List<Group>();
        try
        {
            if (!File.Exists(_statePath))
            {
                _log.Log("No persisted state found.");
                return result;
            }

            string json;
            try
            {
                json = File.ReadAllText(_statePath);
            }
            catch (Exception readEx)
            {
                _stateLoadFailed = true;
                _log.LogException("PersistenceService.Load read", readEx);
                return result;
            }

            PersistedState? state;
            try
            {
                state = JsonSerializer.Deserialize(json, TabDockJsonContext.Default.PersistedState);
            }
            catch (JsonException jsonEx)
            {
                _log.LogException("PersistenceService.Load JSON", jsonEx);
                if (!QuarantineCorruptStateFile())
                    _stateLoadFailed = true;
                return result;
            }

            if (state?.Groups == null)
            {
                // A syntactically valid JSON null/root-with-null-Groups is
                // still not a recoverable application state. Preserve it with
                // the same evidence path as other corrupt state rather than
                // allowing the next save to erase it silently.
                _log.Log("PersistenceService.Load: state has no Groups array; treating it as corrupt.");
                if (!QuarantineCorruptStateFile())
                    _stateLoadFailed = true;
                return result;
            }

            // A single null entry in the persisted array must not prevent the
            // well-formed groups from restoring.
            state.Groups.RemoveAll(g => g == null);
            var usedGroupIds = new HashSet<Guid>();

            foreach (var pg in state.Groups)
            {
                try
                {
                    // A group with a syntactically valid but null Tabs list
                    // restores as an empty group rather than throwing on enumeration.
                    pg.Tabs ??= new List<PersistedTab>();

                    string accent = string.IsNullOrWhiteSpace(pg.AccentColor) ? "#2196F3" : pg.AccentColor;
                    if (!TryParseAccentColor(accent, out _))
                    {
                        _log.Log($"Invalid AccentColor '{pg.AccentColor}' for group {pg.Id}; falling back to default.");
                        accent = "#2196F3";
                    }

                    Guid groupId = pg.Id;
                    if (groupId == Guid.Empty || !usedGroupIds.Add(groupId))
                    {
                        Guid originalId = groupId;
                        do
                        {
                            groupId = Guid.NewGuid();
                        }
                        while (!usedGroupIds.Add(groupId));

                        _log.Log(originalId == Guid.Empty
                            ? $"Persisted group had an empty ID; assigned {groupId}."
                            : $"Duplicate persisted group ID {originalId}; assigned {groupId} to the later group.");
                    }

                    var group = new Group
                    {
                        Id = groupId,
                        Name = string.IsNullOrWhiteSpace(pg.Name) ? "Group" : pg.Name,
                        AccentColor = accent,
                    };

                    foreach (var pt in pg.Tabs)
                    {
                        // Live HWNDs are not restored across reboots. Keep the metadata as
                        // layout intent only; the group starts empty and the user re-populates it.
                        group.PersistedTabs.Add(new PersistedTabMetadata
                        {
                            ExePath = pt.ExePath ?? string.Empty,
                            OriginalTitle = pt.OriginalTitle ?? string.Empty,
                            CustomLabel = pt.CustomLabel ?? string.Empty,
                            Left = pt.Left,
                            Top = pt.Top,
                            Right = pt.Right,
                            Bottom = pt.Bottom,
                            WasMaximized = pt.WasMaximized,
                        });
                    }

                    // Keep the loaded index as layout intent (see
                    // Group.PersistedActiveIndex) as well as assigning it, since the
                    // assignment is clamped to -1 while the group has no live members.
                    group.PersistedActiveIndex = pg.ActiveIndex;
                    group.ActiveIndex = pg.ActiveIndex;
                    result.Add(group);
                }
                catch (Exception groupEx)
                {
                    _log.LogException($"PersistenceService.Load group {pg?.Id}", groupEx);
                }
            }

            _log.Log($"Restored {result.Count} group(s) from {_statePath}");
        }
        catch (Exception ex)
        {
            _log.LogException("PersistenceService.Load", ex);
            _stateLoadFailed = true;
        }
        return result;
    }

    private bool TryParseAccentColor(string value, out object? color)
    {
        color = null;
        try
        {
            color = ColorConverter.ConvertFromString(value);
            return color != null;
        }
        catch
        {
            return false;
        }
    }

    private bool QuarantineCorruptStateFile()
    {
        try
        {
            if (!File.Exists(_statePath))
                return true;

            string corruptPath = GetUniqueCorruptPath();
            File.Move(_statePath, corruptPath);
            _log.Log($"Quarantined corrupt state file to {corruptPath}");
            return true;
        }
        catch (Exception quarantineEx)
        {
            _log.LogException("PersistenceService.Load quarantine", quarantineEx);
            return false;
        }
    }

    private string GetUniqueCorruptPath()
    {
        string basePath = $"{_statePath}.corrupt.{DateTime.Now:yyyyMMddHHmmssfff}";
        if (!File.Exists(basePath))
            return basePath;

        for (int i = 1; i < 1000; i++)
        {
            string candidate = $"{basePath}.{i:D3}";
            if (!File.Exists(candidate))
                return candidate;
        }

        return $"{basePath}.{Guid.NewGuid():N}";
    }
}
