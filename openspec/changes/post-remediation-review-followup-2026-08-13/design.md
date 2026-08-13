## Decision summary

Use two identity tiers. Layout/re-glue calls remain hot and validate the
existing HWND/PID/thread/class contract, a live `CapturedWindow` binding, and a
reversible per-capture HWND token. Slow or destructive calls additionally
require a nonzero captured process-start time to equal a native
`GetProcessTimes` probe for the current PID and, where appropriate, the
executable path. The gate returns `Match`, `Mismatch`, or `Unverifiable`;
only `Match` permits native mutation. The native probe uses `OpenProcess` and
`GetProcessTimes` directly, so it allocates no managed `Process` object. A
captured-object map rejects delayed operations against an old object after an
HWND is released and re-captured in the same process.

The platform does not expose a universal HWND generation number for an
external top-level window. TabDock therefore sets a unique, reversible HWND
property at capture and requires that token on every mutation; Windows removes
the property when that HWND is destroyed, so a same-process destroy/recreate
race fails closed even when PID, thread, class, and executable still match.
The token is journaled for crash rescue and removed only after release/rescue
is finalized. The WinEvent destroy path and group index remain the authoritative
lifecycle removal path, while the token is the native last-mile identity gate.
Journal cleanup is also scoped to the captured HWND/PID/thread/process-start/
executable/class/token identity rather than HWND alone, so a stale release
callback cannot erase a newer same-HWND recovery entry.

`ShowWindow`'s BOOL is never used as an operation-success result. Restore
operations call it and verify that the target is visible and no longer
iconic/zoomed; visibility operations call it and compare `IsWindowVisible`
with the requested post-state. This keeps a benign hidden-to-restore false
return from consuming the per-HWND positioning-failure suppression slot.

For monitor DPI, `GetDpiForMonitor` is removed from production and test code.
Microsoft documents it as not DPI-aware and says not to call it from a
per-monitor-aware thread. A hidden, non-activating top-level helper HWND is
created on the target monitor under an explicit PerMonitorV2 thread context;
`GetDpiForWindow(helper)` then returns the effective DPI for that helper's
monitor. The helper is immediately destroyed and fails closed if creation,
monitor placement, context verification, or the DPI query fails. The helper
is used only on capture, dirty min-track, diagnostics, and qualification paths,
never on the per-frame glue path.

The crash journal now uses schema v3 for the GUI-thread and per-capture HWND
generation fields. The historical v1 shape is the original no-version
`Hwnd`/`Pid`/`ExePath` record; historical v2 is the full presentation and
process-start record from the deep-audit remediation. A legacy v1/v2 record
cannot prove that a same-process HWND was not destroyed and recycled after the
old process wrote the journal, so startup performs no automatic native rescue.
It writes the exact source bytes through a durable unique
`hidden-windows.json.pending*` sidecar, removes the active path only after the
sidecar is durable, emits a manual-recovery diagnostic, and does not retry the
sidecar automatically. Incomplete v3 entries use the same preservation path;
future versions remain at the active path untouched. Current v3 entries use
the tri-state recovery gate: positive stale evidence is safe cleanup,
unverifiable evidence remains an active retry, and a full match is consumed
only after presentation and token cleanup succeed.

## Deterministic seams

- `IWindowIdentityNativeApi` drives identity fixtures without touching a real
  desktop. Tests cover PID reuse, same-PID process-start mismatch, same-process
  HWND-token mismatch, executable/class mismatch, delayed stale-object
  rejection, valid current members, zero process-start fail-closed behavior,
  and release/journal ownership on unavailable probes. A hot-path loop asserts
  no start-time probe occurs.
- `ShowWindowSemanticsSelfTest` models prior visibility separately from the
  post-state and checks hidden, minimized, visible, still-hidden, still-iconic,
  still-zoomed, hide, release/show, and intentional-hide outcomes.
- `IMonitorDpiProbe` and the pure min-track conversion seam allow 96/120/144/
  192 DPI cases and probe failure to be tested without mixed-DPI hardware.
  `IMonitorDpiNativeApi` also verifies helper creation, monitor association,
  helper destruction, and thread-context restoration on success and early
  returns without touching a real desktop.

Hide returns an explicit `Hidden`, `TargetGoneOrRecycled`, or
`RecoveryPending` outcome. An unverifiable hide keeps the captured member and
journal; the single-tab active transition rolls back, while split transitions
remain in their prior logical mode rather than silently presenting an extra
guest.

## Compatibility and safety

No reparenting, style mutation, `AttachThreadInput`, `SetForegroundWindow`
loop, or unsafe input is introduced. Known DPI-unaware guests remain accepted;
only an unknown/failing awareness or monitor probe refuses capture. Physical
screen-coordinate shepherding remains the coordinate contract.
