# Production diagnostics foundation waypoint

## Current remediation overlay (2026-08-13)

The diagnostics foundation is implemented, but the earlier artifact-level
completion note is historical and does not close the whole-codebase release
gate. Current behavior retains raw registry `ProductName` as forensic evidence,
reports a build-derived `ProductFamily`/normalized product label (build 22000+
is Windows 11), replaces embedded sensitive roots case-insensitively in support
bundle text, and keeps doctor/export read-only with no upload side effect.
`scripts/validate.ps1` now generates an isolated doctor report and support ZIP,
fingerprints the isolated state tree before/after doctor, and scans the actual
ZIP contents for paths, usernames, machine names, credential-like tokens, and
raw title fields. Hosted CI runs this deterministic gate; supervised desktop
qualification remains separate and is not implied by a green CI run.

## Scope

This milestone adds support evidence only. It does not implement Shepherd V2,
merge group shells, change split semantics, add polling, or change the
no-reparent architecture.

## Evidence boundary

- `BuildIdentity` is generated-metadata based. `SourceRevisionId` is reused
  through `AssemblyInformationalVersion`; no runtime Git dependency or build
  timestamp was added.
- `--version` is dispatched before mutex/state/hotkey/hook startup.
- `--doctor` reads machine, monitor/DPI, display-adapter, persistence,
  TabDock-process, native HWND, and bounded trace evidence. Optional failures
  are classified and do not abort the report.
- `ContainerWindow.CreateDiagnosticSnapshot` is a desired/logical snapshot;
  `NativeSnapshotService` is an observed-native snapshot. Snapshot/export has
  no native write calls and cannot repair the captured state.
- The in-memory trace is capped at 1024 significant events and uses monotonic
  sequence numbers. It records callback/dispatch WinEvent timing for selected
  foreground/reorder/move-size events plus group, split, guest, activation,
  and repair outcomes.
- `Ctrl+Alt+Shift+D` exports a sanitized ZIP to the desktop while the session
  is running. A command-line support bundle remains available without an
  instance; it intentionally has no IPC authority and therefore cannot invent
  live logical state.

## Failure readiness

The live bundle can correlate each container and guest HWND with visibility,
iconic state, geometry, monitor/DPI, foreground, topmost/cloaked state,
previous/next z-order neighbors, and header/content/split `WindowFromPoint`
results. It records logical active/split members and expected pane rectangles,
so the header-disappear case separates hidden/minimized/covered/foreground
misses and the guest-move case shows MOVESIZESTART/END, assigned pane,
requested re-glue, and observed post-state. Multiple container rows make
cross-group layering visible.

## Deferred roadmap

Shepherd V2 group-wide desired/observed reconciliation, the single-shell spike,
split UX decisions, and cross-machine qualification remain future work. This
waypoint is instrumentation, not a correctness refactor.
