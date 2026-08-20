#!/usr/bin/env python3
"""
tabdock_persistence_single_writer_fix.py

Makes TabDock's state.json persistence a true single-writer subsystem.

Invariants enforced by this fix (per the runtime-stabilization goal):

  * one disk writer for state.json / .bak / .tmp  -> CommitJson is the only
    method that touches those paths, and every call is serialized by a gate.
  * monotonic latest-wins save generations          -> every Save/SaveAsync
    attempt claims the next Interlocked generation; only the most-recently
    attempted generation may touch disk.
  * rapid SaveAsync coalescing                      -> a burst of async saves
    collapses to a single disk write (the newest wins).
  * synchronous Save uses the same serialized gate   -> no interleave with a
    concurrent debounced write.
  * older async snapshots can never overwrite a newer attempted generation.
  * ordinary active-tab switching keeps its off-thread (non-synchronous) path.
  * recovery journal remains separate and synchronous (unchanged here).

Usage:
    python tabdock_persistence_single_writer_fix.py <repo-root> --check
    python tabdock_persistence_single_writer_fix.py <repo-root> --apply
"""

from __future__ import annotations

import os
import sys

TARGET_REL = os.path.join("Services", "PersistenceService.cs")

# ---- Old (un-applied) anchors ------------------------------------------------
OLD_FIELDS = """    // A read/access failure or unsupported future file is not evidence that the
    // user's state is empty. Block later saves in that process.
    private bool _stateLoadFailed;
"""

OLD_SAVE = """    public void Save(IEnumerable<Group> groups)
    {
        string? json = BuildStateJson(groups);
        if (json == null)
            return;
        CommitJson(json);
    }

    /// <summary>
    /// Debounced / high-frequency save path. Builds the immutable JSON snapshot
    /// on the calling (UI) thread, then performs the blocking WriteThrough +
    /// fsync + atomic rename off-thread so a rapid tab switch or container drag
    /// never pays synchronous disk I/O on the input/render turn. Safety-critical
    /// boundaries (capture, release, group mutation) use <see cref="Save"/>
    /// instead and stay synchronous.
    /// </summary>
    public void SaveAsync(IEnumerable<Group> groups)
    {
        string? json = BuildStateJson(groups);
        if (json == null)
            return;
        System.Threading.Tasks.Task.Run(() => CommitJson(json));
    }
"""

OLD_COMMIT = """    /// <summary>
    /// Performs the durable, atomic state write. Shared by the synchronous
    /// <see cref="Save"/> (safety boundaries) and the off-thread
    /// <see cref="SaveAsync"/> (debounced preference writes).
    /// </summary>
    private void CommitJson(string json)
    {
        try
        {
            // A backup copy is part of the same save transaction. If it cannot
            // be made, the catch below prevents the primary from being touched.
            if (File.Exists(_statePath))
                File.Copy(_statePath, BackupPath, overwrite: true);

            string tempPath = _statePath + ".tmp";
            WriteDurableText(tempPath, json);
            File.Move(tempPath, _statePath, overwrite: true);
            _lastSavedJson = json;
            _log.Log($"Saved state to {DiagnosticEnvironmentService.RedactPath(_statePath)} (schema={PersistedState.CurrentVersion})");
        }
        catch (Exception ex)
        {
            _log.LogException("PersistenceService.Save", ex);
        }
    }
"""

# ---- New (applied) replacements ---------------------------------------------
NEW_FIELDS = """    // A read/access failure or unsupported future file is not evidence that the
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
"""

NEW_SAVE = """    /// <summary>
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
        System.Threading.Tasks.Task.Run(() => CommitJson(json, generation));
    }
"""

NEW_COMMIT = """    /// <summary>
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
                // A backup copy is part of the same save transaction. If it cannot
                // be made, the catch below prevents the primary from being touched.
                if (File.Exists(_statePath))
                    File.Copy(_statePath, BackupPath, overwrite: true);

                string tempPath = _statePath + ".tmp";
                WriteDurableText(tempPath, json);
                File.Move(tempPath, _statePath, overwrite: true);
                _lastSavedJson = json;
                _log.Log($"Saved state to {DiagnosticEnvironmentService.RedactPath(_statePath)} (schema={PersistedState.CurrentVersion})");
            }
            catch (Exception ex)
            {
                _log.LogException("PersistenceService.Save", ex);
            }
        }
    }
"""

APPLIED_MARKERS = (
    "private readonly object _writeGate = new();",
    "private long _lastAttemptedGeneration = -1;",
    "private void CommitJson(string json, long generation)",
    "Task.Run(() => CommitJson(json, generation))",
)


def resolve_target(repo_root: str) -> str:
    path = os.path.join(repo_root, TARGET_REL)
    if not os.path.isfile(path):
        raise SystemExit(f"ERROR: expected {path} to exist")
    return path


def is_applied(text: str) -> bool:
    return all(marker in text for marker in APPLIED_MARKERS)


def read_text(path: str) -> str:
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def write_text(path: str, text: str) -> None:
    # The repo is a Windows project checked out with CRLF; preserve that style.
    with open(path, "w", encoding="utf-8", newline="\r\n") as handle:
        handle.write(text)


def apply(text: str) -> str:
    replacements = [
        (OLD_FIELDS, NEW_FIELDS),
        (OLD_SAVE, NEW_SAVE),
        (OLD_COMMIT, NEW_COMMIT),
    ]
    for old, new in replacements:
        if old not in text:
            raise SystemExit(
                "ERROR: expected anchor not found; refusing to apply a partial fix."
            )
        text = text.replace(old, new, 1)
    return text


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__)
        return 2

    repo_root = argv[1]
    mode = None
    for arg in argv[2:]:
        if arg in ("--check", "--apply"):
            mode = arg
    if mode is None:
        print("ERROR: specify --check or --apply")
        return 2

    path = resolve_target(repo_root)
    text = read_text(path)

    if mode == "--check":
        if is_applied(text):
            print("CHECK: single-writer fix already applied.")
            return 0
        print("CHECK: single-writer fix NOT applied.")
        return 1

    # mode == --apply
    if is_applied(text):
        print("APPLY: already applied; nothing to do.")
        return 0

    new_text = apply(text)
    write_text(path, new_text)
    if not is_applied(new_text):
        raise SystemExit("ERROR: apply did not produce the expected markers.")
    print("APPLY: single-writer fix applied to " + path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
