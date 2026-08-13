## Context

The current Shepherd implementation is structurally sound, but several safety
boundaries are incomplete: deferred native positioning mishandles HDWP
ownership, the crash journal describes only hidden members, state backup is not
used after proven corruption, lifecycle monitoring can become unavailable
without a fail-closed response, and diagnostic redaction is not applied at all
serialization boundaries. The validation harness and CI have also outgrown
their original single-binary assumptions.

## Goals / Non-Goals

**Goals:**

- Preserve the top-level-window Shepherd model and local split z-order
  invariants.
- Make every reversible guest presentation mutation recoverable after abrupt
  termination, with conservative identity checks and explicit self-hide
  semantics.
- Make persistence classification, backup recovery, and schema evolution
  evidence-preserving and testable without a live desktop.
- Make monitor/session failure policies coherent and visible.
- Make support-bundle privacy an end-to-end property of every generated entry.
- Qualify hermetic behavior in Release builds and keep real-input validation
  bounded and supervised.

**Non-Goals:**

- No `SetParent`, `WS_CHILD`, `AttachThreadInput`, global `HWND_BOTTOM`, or
  third-party runtime dependency.
- No unsafe SendInput automation in hosted CI.
- No attempt to restore unrelated desktop z-order or resurrect an application
  the user intentionally hid.
- No high-frequency polling fallback for WinEvent failure.

## Decisions

### D1 — Chain HDWP handles and abandon failed batches

`DeferWindowPos` returns an updated HDWP. The next call receives that value and
`EndDeferWindowPos` receives the final value. A failed middle call abandons the
batch without calling End; the existing per-guest fallback retains geometry,
visibility, local z-order, journal, and no-activation semantics.

### D2 — Use a versioned capture-session recovery journal

The existing `hidden-windows.json` filename remains for compatibility, but its
version-2 entries represent every captured guest until release, not only
inactive hidden guests. An entry contains HWND, PID, executable, class,
best-effort process start time, original bounds/placement/show state, original
visibility, and best-effort DWM transition state. The entry is synchronously
written before the first guest mutation. Rescue requires all available identity
fields to match and restores placement/show state without changing unrelated
z-order. A self-hide first commits a no-rescue marker, then clears/ hides; a
failure to commit leaves the guest visible.

### D3 — Classify persistence failures before backup fallback

The primary state file is classified as missing, valid, supported legacy,
unsupported future, corrupt, or unreadable. Only missing or successfully
quarantined corrupt primary data may use a valid backup. Unreadable and future
files remain in place and block saves. Version 1 migrates in memory to current
version 2; future versions are never rewritten.

### D4 — Fail closed on lifecycle capability loss

WinEvent hook installation uses an injectable API seam. Capture admission is
disabled while the complete hook set is unhealthy. After bounded retries fail,
TabDock releases and normalizes all live members, preserves layout metadata,
shows a warning, and remains capture-disabled until restart. No polling loop is
added.

### D5 — Exit after session-ending teardown

Session-ending teardown is deliberately one-way. TabDock saves, flushes,
releases, stops monitoring, normalizes containers, and calls `Shutdown` rather
than attempting to resume if another application cancels the Windows operation.
The operation is idempotent so `Application_Exit` is safe afterward.

### D6 — Bound min-track probes with identity-scoped last-known values

The dirty-refresh design remains, but the synchronous native wait is reduced to
a documented 100 ms bound. A timeout or native failure keeps a previously
successful result for the same captured object; a first probe failure means
unknown/no constraint. Moves and normal relayouts do not probe unless dirty.

### D7 — Sanitize at text boundaries and qualify by shards

Path variants are replaced case-insensitively wherever they occur, then
absolute-path and credential-like patterns are sanitized before any doctor,
JSON, trace, log, or ZIP entry is emitted. The ValidationDriver accepts
configuration/RID/path options and launches bounded named shards as separate
guarded processes for `all`; safety caps remain unchanged per process.

### D8 — Storage-unavailable degraded mode

Logging may retain a bounded in-memory stream when its directory is unavailable.
Persistence is disabled with a warning, and Shepherd capture is refused unless
the recovery journal can be durably written. The app may still show existing
launcher/UI state without silently weakening crash safety.

## Risks / Trade-offs

- A full-session journal increases small synchronous writes on semantic capture,
  hide, and release transitions; this is the cost of preventing stranded
  guests and is bounded by the number of captured windows.
- A future-version state file is preserved rather than automatically falling
  back to an older backup, prioritizing user data preservation over startup
  convenience.
- A permanent WinEvent failure releases guests rather than attempting a risky
  degraded mode; the user must restart after the underlying OS condition is
  corrected.
- Hardware-dependent multi-monitor, session-cancellation, and full real-input
  recovery scenarios remain supervised qualification, not CI claims.

## Migration Plan

State version 1 and journal version 1 are read and migrated in memory. Existing
corrupt evidence is never deleted during fallback. The next successful save
writes state version 2 and the next capture writes the version-2 journal shape.
The filename and atomic temporary-file write pattern remain stable.

## Open Questions

- A real multi-monitor matrix and Windows session-cancellation run require a
  supervised desktop with the relevant hardware/OS controls.
