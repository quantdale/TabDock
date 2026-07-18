using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
            var state = new PersistedState();
            foreach (var g in groups)
            {
                var pg = new PersistedGroup
                {
                    Id = g.Id,
                    Name = g.Name,
                    AccentColor = g.AccentColor,
                    ActiveIndex = g.ActiveIndex,
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
            File.WriteAllText(_statePath, json);
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

            string json = File.ReadAllText(_statePath);
            PersistedState? state = JsonSerializer.Deserialize(json, TabDockJsonContext.Default.PersistedState);
            if (state?.Groups == null)
                return result;

            foreach (var pg in state.Groups)
            {
                var group = new Group
                {
                    Id = pg.Id == Guid.Empty ? Guid.NewGuid() : pg.Id,
                    Name = string.IsNullOrWhiteSpace(pg.Name) ? "Group" : pg.Name,
                    AccentColor = string.IsNullOrWhiteSpace(pg.AccentColor) ? "#2196F3" : pg.AccentColor,
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

                group.ActiveIndex = pg.ActiveIndex;
                result.Add(group);
            }

            _log.Log($"Restored {result.Count} group(s) from {_statePath}");
        }
        catch (Exception ex)
        {
            _log.LogException("PersistenceService.Load", ex);
        }
        return result;
    }
}
