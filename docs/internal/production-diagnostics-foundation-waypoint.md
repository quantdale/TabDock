# Production diagnostics foundation waypoint

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
