using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using TabDock.Models;

namespace TabDock.Services;

/// <summary>
/// Saves and restores group metadata to state.json. The service distinguishes
/// missing, corrupt, unsupported, and unreadable files so a fail-safe read
/// error can never be mistaken for an empty user state.
/// </summary>
public sealed class PersistenceService
{
    private enum StateReadKind
    {
        Valid,
        Corrupt,
        Unsupported,
        Unreadable,
    }

    private enum PathKind
    {
        Missing,
        File,
        Directory,
        Unreadable,
    }

    private readonly LoggingService _log;
    private readonly string _statePath;
    private readonly bool _storageAvailable;
    private readonly string? _storageFailureReason;
    private readonly Func<string, FileAttributes> _getAttributes;
    private readonly Func<string, string> _readAllText;
    private readonly Func<string, byte[]> _readAllBytes;
    private readonly Action<string, byte[]> _writeDurableBytes;
    private readonly Action<string, string> _writeDurableText;
    private readonly Action<string, string> _atomicMove;

    // The exact JSON last written to disk, so an unchanged save can skip the
    // write + atomic-rename round trip entirely.
    private volatile string? _lastSavedJson;

    // A read/access failure or unsupported future file is not evidence that the
    // user's state is empty. Block later saves in that process.
    private bool _stateLoadFailed;

    // Single-writer gate. Every state.json disk mutation (backup copy, temp
    // write, atomic rename) happens only inside this lock, so the synchronous
    // safety-boundary path and the off-thread debounced path can never interleave
    // and produce a torn .tmp/.bak/.json set.
    private readonly object _writeGate = new();

    // Monotonic save generation. Every Save/SaveAsync attempt claims the next
    // generation and is the only one permitted to touch disk when its generation
    // is still the most-recently-attempted one. A delayed/stale async snapshot
    // can therefore never overwrite a newer attempted save.
    private long _lastAttemptedGeneration = -1;

    // Handle to the most recently enqueued off-thread write, so a graceful
    // shutdown (or a deterministic test) can await the single-writer drain.
    // This is a reference only; it does not chain or serialize the tasks, so
    // rapid-save coalescing is preserved.
    private System.Threading.Tasks.Task? _lastWriteTask;

    public PersistenceService(LoggingService log, string? statePath = null)
        : this(log, statePath, path => File.GetAttributes(path), path => File.ReadAllText(path))
    {
    }

    internal PersistenceService(
        LoggingService log,
        string? statePath,
        Func<string, FileAttributes> getAttributes,
        Func<string, string> readAllText,
        Func<string, byte[]>? readAllBytes = null,
        Action<string, byte[]>? writeDurableBytes = null,
        Action<string, string>? writeDurableText = null,
        Action<string, string>? atomicMove = null)
    {
        _log = log;
        _getAttributes = getAttributes;
        _readAllText = readAllText;
        // Fault-injection seams for the backup/primary transaction stages.
        // Production resolves them to the real operations; deterministic tests
        // substitute throwing delegates per path to prove each failure window.
        _readAllBytes = readAllBytes ?? File.ReadAllBytes;
        _writeDurableBytes = writeDurableBytes ?? WriteDurableBytes;
        _writeDurableText = writeDurableText ?? WriteDurableText;
        _atomicMove = atomicMove ?? ((sourcePath, destinationPath) => File.Move(sourcePath, destinationPath, overwrite: true));
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(statePath) && string.IsNullOrWhiteSpace(appData))
        {
            // An empty AppData path would silently relocate durable state to a
            // process-relative (CWD-dependent) location. Fail closed instead.
            _statePath = Path.Combine("TabDock", "state.json");
            _storageAvailable = false;
            _storageFailureReason = "The AppData folder path is unavailable; refusing to persist to a process-relative location.";
            return;
        }
        _statePath = string.IsNullOrWhiteSpace(statePath)
            ? Path.Combine(appData, "TabDock", "state.json")
            : Path.GetFullPath(statePath);

        try
        {
            string directory = Path.GetDirectoryName(_statePath)!;
            Directory.CreateDirectory(directory);
            _storageAvailable = true;
            _storageFailureReason = null;
        }
        catch (Exception ex)
        {
            _storageAvailable = false;
            _storageFailureReason = ex.GetType().Name + ": " + ex.Message;
            _log.LogException("PersistenceService storage unavailable", ex);
        }
    }

    public string StatePath => _statePath;

    public string BackupPath => _statePath + ".bak";

    public bool IsStorageAvailable => _storageAvailable;

    public string? StorageFailureReason => _storageFailureReason;

    internal bool StateLoadFailed => _stateLoadFailed;

    /// <summary>
    /// Awaits the most recently enqueued off-thread write (if any). Used for a
    /// graceful shutdown flush and for deterministic tests; it never chains or
    /// serializes the writes, so coalescing is preserved.
    /// </summary>
    public System.Threading.Tasks.Task WhenWritesSettledAsync()
        => _lastWriteTask ?? System.Threading.Tasks.Task.CompletedTask;

    /// <summary>
    /// Safety-boundary save (capture, release, group mutation, rescue). Runs the
    /// full durable write synchronously on the calling thread but still goes
    /// through the single serialized disk gate so it can never interleave with a
    /// concurrent debounced write.
    /// </summary>
    public void Save(IEnumerable<Group> groups)
    {
        string? json = BuildStateJson(groups);
        if (json == null)
            return;
        long generation = System.Threading.Interlocked.Increment(ref _lastAttemptedGeneration);
        CommitJson(json, generation);
    }

    /// <summary>
    /// Debounced / high-frequency save path. Builds the immutable JSON snapshot
    /// on the calling (UI) thread, claims the next monotonic generation, then
    /// performs the blocking WriteThrough + fsync + atomic rename off-thread so a
    /// rapid tab switch or container drag never pays synchronous disk I/O on the
    /// input/render turn. Rapid bursts coalesce: only the most-recently-attempted
    /// generation is permitted to write, so a newer switch always wins even if an
    /// older async snapshot is still queued. Safety-critical boundaries (capture,
    /// release, group mutation) use <see cref="Save"/> instead and stay synchronous.
    /// </summary>
    public void SaveAsync(IEnumerable<Group> groups)
    {
        string? json = BuildStateJson(groups);
        if (json == null)
            return;
        long generation = System.Threading.Interlocked.Increment(ref _lastAttemptedGeneration);
        _lastWriteTask = System.Threading.Tasks.Task.Run(() => CommitJson(json, generation));
    }

    /// <summary>
    /// Builds the serialized state JSON (and validates the target path) on the
    /// caller's thread. Returns null when the write should be skipped (storage
    /// unavailable, unsafe load, or unchanged content).
    /// </summary>
    private string? BuildStateJson(IEnumerable<Group> groups)
    {
        if (!_storageAvailable)
        {
            _log.Log("PersistenceService.Save skipped because durable state storage is unavailable.");
            return null;
        }

        if (_stateLoadFailed)
        {
            _log.Log("PersistenceService.Save skipped because the existing state could not be read safely this session.");
            return null;
        }

        var state = new PersistedState { Version = PersistedState.CurrentVersion };
        foreach (Group g in groups)
        {
            if (!g.HasMaterializedTabs)
            {
                // Empty shells are useful during the current session but
                // have no durable layout intent. Persisting them makes a
                // fresh shell reappear at every startup and allowed
                // failed picker attempts to accumulate zero-tab groups.
                _log.Log($"PersistenceService.Save skipped unmaterialized empty group {g.Id}.");
                continue;
            }

            var pg = new PersistedGroup
            {
                Id = g.Id,
                Name = g.Name,
                AccentColor = g.AccentColor,
                // A group with no live members has only loaded metadata;
                // preserve its persisted active intent rather than the
                // clamped -1 live index.
                ActiveIndex = g.Members.Count > 0 ? g.ActiveIndex : g.PersistedActiveIndex,
            };

            if (g.Members.Count > 0)
            {
                foreach (CapturedWindow m in g.Members)
                    pg.Tabs.Add(ToPersistedTab(m));
            }
            else
            {
                foreach (PersistedTabMetadata pm in g.PersistedTabs)
                    pg.Tabs.Add(ToPersistedTab(pm));
            }
            state.Groups.Add(pg);
        }

        string json = JsonSerializer.Serialize(state, TabDockJsonContext.Default.PersistedState);
        if (string.Equals(json, _lastSavedJson, StringComparison.Ordinal) && File.Exists(_statePath))
            return null;

        PathKind currentPath = ClassifyPath(_statePath, out string currentReason);
        if (currentPath == PathKind.Directory || currentPath == PathKind.Unreadable)
        {
            _stateLoadFailed = true;
            _log.Log($"PersistenceService.Save skipped because the primary state path is not safely writable ({currentReason}).");
            return null;
        }

        return json;
    }

    /// <summary>
    /// Performs the durable, atomic state write. Shared by the synchronous
    /// <see cref="Save"/> (safety boundaries) and the off-thread
    /// <see cref="SaveAsync"/> (debounced preference writes). This is the ONLY
    /// method that touches state.json / .bak / .tmp; every call is serialized by
    /// <see cref="_writeGate"/> and gated by <paramref name="generation"/> so a
    /// stale (older) snapshot can never clobber a newer attempted save.
    /// </summary>
    private void CommitJson(string json, long generation)
    {
        lock (_writeGate)
        {
            // Latest-wins: only the most-recently-requested generation may touch
            // disk. A delayed async snapshot from an earlier attempt is dropped.
            if (generation != System.Threading.Interlocked.Read(ref _lastAttemptedGeneration))
                return;

            try
            {
                // The state directory can disappear externally between the
                // constructor probe and this write; recreate it so one
                // deletion does not disable persistence for the session.
                string? directory = Path.GetDirectoryName(_statePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                // The backup replacement is itself a durability boundary. A
                // direct overwrite (File.Copy over the live .bak) truncates
                // the previous known-good backup before the new primary even
                // exists, so a failure or power loss mid-copy could destroy
                // the last recovery evidence. Instead: read the primary once,
                // durably flush a candidate beside it, then install it with
                // one atomic move. Every failure below leaves the previous
                // .bak and the primary untouched; a missing primary skips the
                // stage entirely so an existing valid backup survives.
                if (File.Exists(_statePath))
                {
                    byte[] primaryBytes = _readAllBytes(_statePath);
                    string backupCandidatePath = BackupPath + ".tmp";
                    _writeDurableBytes(backupCandidatePath, primaryBytes);
                    _atomicMove(backupCandidatePath, BackupPath);
                }

                string tempPath = _statePath + ".tmp";
                _writeDurableText(tempPath, json);
                _atomicMove(tempPath, _statePath);
                _lastSavedJson = json;
                _log.Log($"Saved state to {DiagnosticEnvironmentService.RedactPath(_statePath)} (schema={PersistedState.CurrentVersion})");
            }
            catch (Exception ex)
            {
                _log.LogException("PersistenceService.Save", ex);
            }
        }
    }

    public List<Group> Load()
    {
        var result = new List<Group>();
        if (!_storageAvailable)
        {
            _stateLoadFailed = true;
            _log.Log("PersistenceService.Load disabled because durable state storage is unavailable.");
            return result;
        }

        PathKind primaryPath = ClassifyPath(_statePath, out string primaryPathReason);
        if (primaryPath == PathKind.Directory)
        {
            _stateLoadFailed = true;
            _log.Log("PersistenceService.Load primary path is a directory; treating it as unreadable and refusing overwrite.");
            return result;
        }
        if (primaryPath == PathKind.Unreadable)
        {
            _stateLoadFailed = true;
            _log.Log($"PersistenceService.Load primary path is unreadable ({primaryPathReason}); preserving it and refusing backup/empty fallback.");
            return result;
        }

        if (primaryPath == PathKind.Missing)
        {
            PathKind backupPath = ClassifyPath(BackupPath, out string backupPathReason);
            if (backupPath == PathKind.Missing)
            {
                _log.Log("No persisted state found.");
                return result;
            }
            if (backupPath != PathKind.File)
            {
                _stateLoadFailed = true;
                _log.Log($"PersistenceService.Load backup unavailable ({backupPathReason}); refusing to create an empty primary.");
                return result;
            }

            // A missing primary is not an unreadable primary. A valid backup is
            // safe to use because no unknown primary data is being replaced.
            StateReadKind backupKind = TryReadStateFile(BackupPath, out PersistedState? backup, out string backupReason);
            if (backupKind != StateReadKind.Valid || backup == null)
            {
                _stateLoadFailed = true;
                _log.Log($"PersistenceService.Load backup unavailable ({backupReason}); refusing to create an empty primary.");
                return result;
            }
            _log.Log("PersistenceService.Load recovered a valid backup because the primary state file was missing.");
            return RestoreGroups(backup, result);
        }

        StateReadKind primaryKind = TryReadStateFile(_statePath, out PersistedState? primary, out string primaryReason);
        if (primaryKind == StateReadKind.Valid && primary != null)
            return RestoreGroups(primary, result);

        if (primaryKind == StateReadKind.Unreadable)
        {
            _stateLoadFailed = true;
            _log.Log($"PersistenceService.Load primary is unreadable ({primaryReason}); preserving it and refusing backup/empty fallback.");
            return result;
        }

        if (primaryKind == StateReadKind.Unsupported)
        {
            _stateLoadFailed = true;
            _log.Log($"PersistenceService.Load primary uses an unsupported future schema ({primaryReason}); preserving it and refusing downgrade.");
            return result;
        }

        // Only proven corruption reaches this branch. Quarantine must succeed
        // before a backup can become authoritative; otherwise the unknown
        // primary evidence remains in place and saves are blocked.
        if (!QuarantineCorruptStateFile())
        {
            _stateLoadFailed = true;
            return result;
        }

        if (ClassifyPath(BackupPath, out string corruptBackupReason) != PathKind.File)
        {
            _stateLoadFailed = true;
            _log.Log($"PersistenceService.Load primary was corrupt and quarantined; no usable backup exists ({corruptBackupReason}).");
            return result;
        }

        StateReadKind backupFallbackKind = TryReadStateFile(BackupPath, out PersistedState? backupFallback, out string backupFallbackReason);
        if (backupFallbackKind == StateReadKind.Valid && backupFallback != null)
        {
            _log.Log("PersistenceService.Load recovered a valid backup after quarantining corrupt primary state.");
            return RestoreGroups(backupFallback, result);
        }

        _log.Log($"PersistenceService.Load backup was not usable ({backupFallbackReason}); corrupt primary evidence remains quarantined.");
        _stateLoadFailed = true;
        return result;
    }

    private PathKind ClassifyPath(string path, out string reason)
    {
        reason = string.Empty;
        try
        {
            FileAttributes attributes = _getAttributes(path);
            return (attributes & FileAttributes.Directory) != 0 ? PathKind.Directory : PathKind.File;
        }
        catch (FileNotFoundException)
        {
            return PathKind.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return PathKind.Missing;
        }
        catch (UnauthorizedAccessException ex)
        {
            reason = "access-denied: " + ex.Message;
            return PathKind.Unreadable;
        }
        catch (IOException ex)
        {
            reason = "io-error: " + ex.Message;
            return PathKind.Unreadable;
        }
        catch (Exception ex)
        {
            reason = ex.GetType().Name + ": " + ex.Message;
            return PathKind.Unreadable;
        }
    }

    private StateReadKind TryReadStateFile(string path, out PersistedState? state, out string reason)
    {
        state = null;
        reason = string.Empty;
        string json;
        try
        {
            json = _readAllText(path);
        }
        catch (UnauthorizedAccessException ex)
        {
            reason = "access-denied: " + ex.Message;
            return StateReadKind.Unreadable;
        }
        catch (IOException ex)
        {
            reason = "io-error: " + ex.Message;
            return StateReadKind.Unreadable;
        }
        catch (Exception ex)
        {
            reason = ex.GetType().Name + ": " + ex.Message;
            return StateReadKind.Unreadable;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                reason = "root is not an object";
                return StateReadKind.Corrupt;
            }

            int version = 1;
            if (TryGetPropertyIgnoreCase(document.RootElement, "Version", out JsonElement versionElement))
            {
                if (!versionElement.TryGetInt32(out version))
                {
                    reason = "Version is not an integer";
                    return StateReadKind.Corrupt;
                }
            }

            if (version > PersistedState.CurrentVersion)
            {
                reason = version.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return StateReadKind.Unsupported;
            }
            if (version < 1)
            {
                reason = version.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return StateReadKind.Corrupt;
            }

            if (!TryGetPropertyIgnoreCase(document.RootElement, "Groups", out JsonElement groups)
                || groups.ValueKind != JsonValueKind.Array)
            {
                reason = "Groups array is missing";
                return StateReadKind.Corrupt;
            }

            state = new PersistedState
            {
                Version = PersistedState.CurrentVersion,
                Groups = new List<PersistedGroup>(),
            };
            int skippedGroups = 0;
            int skippedTabs = 0;
            int groupIndex = 0;
            foreach (JsonElement groupElement in groups.EnumerateArray())
            {
                if (groupElement.ValueKind == JsonValueKind.Null)
                {
                    skippedGroups++;
                    _log.Log($"PersistenceService.Load skipped null group record at index {groupIndex}.");
                    groupIndex++;
                    continue;
                }
                if (!TryReadPersistedGroup(groupElement, groupIndex, out PersistedGroup? group, out int groupSkippedTabs))
                {
                    skippedGroups++;
                    groupIndex++;
                    continue;
                }
                skippedTabs += groupSkippedTabs;
                if (group != null)
                    state.Groups.Add(group);
                groupIndex++;
            }

            if (skippedGroups > 0 || skippedTabs > 0)
            {
                _log.Log($"PersistenceService.Load salvaged valid records; skipped {skippedGroups} group record(s) and {skippedTabs} nested tab record(s).");
            }

            if (version < PersistedState.CurrentVersion)
            {
                _log.Log($"PersistenceService.Load migrated schema version {version} to {PersistedState.CurrentVersion} in memory.");
            }
            state.Version = PersistedState.CurrentVersion;
            return StateReadKind.Valid;
        }
        catch (JsonException ex)
        {
            reason = "malformed-json: " + ex.Message;
            return StateReadKind.Corrupt;
        }
    }

    private bool TryReadPersistedGroup(
        JsonElement element,
        int groupIndex,
        out PersistedGroup? group,
        out int skippedTabs)
    {
        group = null;
        skippedTabs = 0;
        if (element.ValueKind != JsonValueKind.Object)
        {
            _log.Log($"PersistenceService.Load skipped malformed group record at index {groupIndex}.");
            return false;
        }

        var restored = new PersistedGroup();
        if (TryGetPropertyIgnoreCase(element, "Id", out JsonElement idElement)
            && idElement.ValueKind == JsonValueKind.String)
        {
            if (!idElement.TryGetGuid(out Guid id))
                _log.Log($"PersistenceService.Load ignored malformed group ID at index {groupIndex}.");
            else
                restored.Id = id;
        }
        if (TryGetPropertyIgnoreCase(element, "Name", out JsonElement nameElement))
        {
            if (nameElement.ValueKind == JsonValueKind.String)
                restored.Name = nameElement.GetString() ?? string.Empty;
            else if (nameElement.ValueKind != JsonValueKind.Null)
                _log.Log($"PersistenceService.Load ignored malformed group name at index {groupIndex}.");
        }
        if (TryGetPropertyIgnoreCase(element, "AccentColor", out JsonElement accentElement))
        {
            if (accentElement.ValueKind == JsonValueKind.String)
                restored.AccentColor = accentElement.GetString() ?? string.Empty;
            else if (accentElement.ValueKind != JsonValueKind.Null)
                _log.Log($"PersistenceService.Load ignored malformed group accent at index {groupIndex}.");
        }
        if (TryGetPropertyIgnoreCase(element, "ActiveIndex", out JsonElement activeElement)
            && activeElement.ValueKind == JsonValueKind.Number
            && activeElement.TryGetInt32(out int activeIndex))
        {
            restored.ActiveIndex = activeIndex;
        }

        if (!TryGetPropertyIgnoreCase(element, "Tabs", out JsonElement tabsElement)
            || tabsElement.ValueKind == JsonValueKind.Null)
        {
            group = restored;
            return true;
        }
        if (tabsElement.ValueKind != JsonValueKind.Array)
        {
            _log.Log($"PersistenceService.Load skipped group {groupIndex} because its Tabs record is malformed.");
            return false;
        }

        int tabIndex = 0;
        foreach (JsonElement tabElement in tabsElement.EnumerateArray())
        {
            if (tabElement.ValueKind == JsonValueKind.Null || tabElement.ValueKind != JsonValueKind.Object)
            {
                skippedTabs++;
                _log.Log($"PersistenceService.Load skipped malformed tab record at group {groupIndex}, index {tabIndex}.");
                tabIndex++;
                continue;
            }

            try
            {
                PersistedTab? tab = JsonSerializer.Deserialize(
                    tabElement.GetRawText(),
                    TabDockJsonContext.Default.PersistedTab);
                if (tab == null)
                {
                    skippedTabs++;
                    _log.Log($"PersistenceService.Load skipped null tab record at group {groupIndex}, index {tabIndex}.");
                }
                else
                {
                    restored.Tabs.Add(tab);
                }
            }
            catch (JsonException ex)
            {
                skippedTabs++;
                _log.Log($"PersistenceService.Load skipped malformed tab record at group {groupIndex}, index {tabIndex} ({ex.GetType().Name}).");
            }
            tabIndex++;
        }

        group = restored;
        return true;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private List<Group> RestoreGroups(PersistedState state, List<Group> result)
    {
        // A single null entry in the persisted array must not prevent the
        // well-formed groups from restoring.
        state.Groups.RemoveAll(g => g == null);
        var usedGroupIds = new HashSet<Guid>();

        foreach (PersistedGroup pg in state.Groups)
        {
            try
            {
                if (pg.Tabs == null || pg.Tabs.Count == 0)
                {
                    // Empty records from older versions are valid JSON but do
                    // not contain recoverable layout intent. Do not reopen
                    // them as empty containers; this also cleans up the
                    // accumulated shells that predated the save filter.
                    _log.Log($"PersistenceService.Load skipped unmaterialized empty group {pg.Id}.");
                    continue;
                }

                string accent = string.IsNullOrWhiteSpace(pg.AccentColor) ? "#2196F3" : pg.AccentColor;
                if (!TryParseAccentColor(accent, out _))
                {
                    _log.Log($"PersistenceService.Load found an invalid accent value for group {pg.Id}; falling back to default.");
                    accent = "#2196F3";
                }

                Guid groupId = pg.Id;
                if (groupId == Guid.Empty || !usedGroupIds.Add(groupId))
                {
                    Guid originalId = groupId;
                    do { groupId = Guid.NewGuid(); } while (!usedGroupIds.Add(groupId));
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
                foreach (PersistedTab? pt in pg.Tabs)
                {
                    if (pt == null)
                    {
                        _log.Log($"PersistenceService.Load skipped a null nested tab in group {pg.Id}.");
                        continue;
                    }
                    try
                    {
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
                    catch (Exception tabEx)
                    {
                        _log.LogException($"PersistenceService.Load skipped malformed tab in group {pg.Id}", tabEx);
                    }
                }
                int boundedActiveIndex = group.PersistedTabs.Count == 0
                    ? 0
                    : Math.Clamp(pg.ActiveIndex, 0, group.PersistedTabs.Count - 1);
                if (boundedActiveIndex != pg.ActiveIndex)
                    _log.Log($"PersistenceService.Load bounded active index for group {group.Id} from {pg.ActiveIndex} to {boundedActiveIndex}.");
                group.PersistedActiveIndex = boundedActiveIndex;
                group.ActiveIndex = boundedActiveIndex;
                result.Add(group);
            }
            catch (Exception groupEx)
            {
                _log.LogException($"PersistenceService.Load group {pg?.Id}", groupEx);
            }
        }

        _log.Log($"Restored {result.Count} group(s) from {DiagnosticEnvironmentService.RedactPath(_statePath)} (schema={state.Version})");
        return result;
    }

    private static void WriteDurableText(string path, string contents)
        => WriteDurableBytes(path, System.Text.Encoding.UTF8.GetBytes(contents));

    private static void WriteDurableBytes(string path, byte[] bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.WriteThrough);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static PersistedTab ToPersistedTab(CapturedWindow m)
        => new()
        {
            ExePath = m.ExePath,
            OriginalTitle = m.OriginalTitle,
            CustomLabel = m.CustomLabel,
            Left = m.OriginalBounds.left,
            Top = m.OriginalBounds.top,
            Right = m.OriginalBounds.right,
            Bottom = m.OriginalBounds.bottom,
            WasMaximized = m.WasMaximized,
        };

    private static PersistedTab ToPersistedTab(PersistedTabMetadata pm)
        => new()
        {
            ExePath = pm.ExePath,
            OriginalTitle = pm.OriginalTitle,
            CustomLabel = pm.CustomLabel,
            Left = pm.Left,
            Top = pm.Top,
            Right = pm.Right,
            Bottom = pm.Bottom,
            WasMaximized = pm.WasMaximized,
        };

    private static bool TryParseAccentColor(string value, out object? color)
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
            _log.Log($"Quarantined corrupt state file to {DiagnosticEnvironmentService.RedactPath(corruptPath)}");
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
