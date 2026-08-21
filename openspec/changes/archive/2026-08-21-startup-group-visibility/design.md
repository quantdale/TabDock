## Context

On the startup-restore path `App.Application_Startup` shows the launcher
(`_mainWindow.Show()`), then loops every persisted (empty) group into
`OpenContainer(group)`, which does `new ContainerWindow(...)`, registers it,
`window.Show()`, and `_mainWindow?.Hide()` — with **no explicit z-order or
activation claim**. Because restored groups are empty, `IsMonitoringNeeded` is
false, so the WinEvent hooks are not installed; and every container z-order
memory (`LayoutShepherdActiveWindow`, the `WM_ACTIVATE` reassert,
`PairZOrderBehindGuest`) requires a live guest, which an empty container has
none of. So when the OS foreground grant is missing at `Show()` (auto-start,
slow startup, user focused elsewhere), the container is parked beneath an
overlapping pre-existing window and nothing repairs it for the session.

## Goals / Non-Goals

**Goals:**
- Make restored-container placement above an overlapping unrelated window
  deterministic at startup, independent of OS foreground-grant luck.
- Keep the fix z-order-only: never steal focus, never fight a later user
  activation. (TabDock has no supported background/no-activation launch path to
  preserve.)
- Preserve the Shepherd local-stack invariant and the single z-order authority
  (`WindowShepherdService`).
- Add non-vacuous regression coverage.

**Non-Goals:**
- No `Topmost=true`, no global `HWND_TOPMOST`, no `SetForegroundWindow` loop.
- No guest HWND/style/owner/geometry mutation.
- No broad startup rewrite or persistence of container placement (out of scope).

## Decisions

- **Where**: a new one-shot method `App.ReconcileRestoredContainerZOrder()`,
  called from `Application_Startup` immediately after the restored-control
  loop, before `SyncWinEventMonitor()`/the "startup complete" log. It iterates
  the restored groups in order and, for each open container, raises it via the
  existing authority primitive `WindowShepherdService.RaiseContainerForChrome`
  (`HWND_TOP` + `SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE`). Restore order is
  preserved so the last-restored container (the one natural `Show()` ordering
  leaves on top) stays topmost among TabDock's own containers.
- **Why z-order-only / no `Activate()`**: `RaiseContainerForChrome` with
  `useTopmostBand:false` does `SetWindowPos(HWND_TOP, SWP_NOACTIVATE)`. It is
  explicitly non-activating, so it can never steal foreground; a later user
  activation of another app is respected. WPF's `Show()` already requests
  initial activation under ordinary rules; we do not add a second, riskier
  activation path. TabDock has **no supported background/silent/auto-start
  launch mode** (single-instance exits a second launch; there is no
  Run-key/tray/silent-launch flag), so there is no such mode whose
  non-intrusiveness the reconciliation would need to preserve — the accurate
  policy is simply "restored groups are raised in the normal z-order band
  without taking focus."
- **Why this exact timing**: z-order placement depends only on the container
  HWND existing and being shown, which `Show()` guarantees by the time the loop
  returns (`Loaded` already ran synchronously and registered the HWND). The
  raise is independent of layout, so no render-priority deferral is needed.
- **Why reusing `RaiseContainerForChrome`**: it is the existing z-order
  authority's container-raise primitive; reusing it keeps a single owner and
  avoids a competing z-order subsystem. The method name is accepted as the
  shared "raise this container HWND in the normal band" operation.
- **Boundedness / non-stealing**: one write per container, once at startup; no
  timer, no loop, no polling, no `SetForegroundWindow`. Nothing persists, so a
  later user activation of another app is never re-asserted.
- **Pairing/invariant**: at reconciliation time there are no guests, so the
  `guest-above-container` invariant is vacuous; when a guest is later captured,
  `PositionAndShow` raises the guest to `HWND_TOP` and `PairZOrderBehindCore`
  pins the container beneath it, cleanly replacing the transient startup
  placement. The one-shot never runs again.

## Risks / Trade-offs

- **Reproduction**: the defect is an OS foreground-grant race. It could not be
  deterministically reproduced CLI-safely in this environment (a launched
  TabDock still received foreground), so the discriminating proof lives in the
  supervised real-input scenario `startup-group-not-hidden-behind-existing-window`.
  The fix is safe by construction (no focus call) and its mechanism is
  source-verified.
- **Visible z-order vs focus**: the raise changes VISIBLE z-order (the restored
  surface is placed above any overlapping unrelated window) while leaving focus
  untouched. That is the intended user-initiated surface establishment. Because
  TabDock has no supported background/auto-start launch path, there is no
  "background" mode whose non-intrusiveness could be violated by this change; if
  such a mode is ever added, it must explicitly detect that intent (e.g.
  `STARTUPINFO` show-window) and suppress the raise — not inferred from an
  unreliable foreground heuristic.
- **CRLF/mixed line endings**: `App.xaml.cs` and `Scenarios.cs` have mixed
  line endings; edits were made byte-preserving and validated with a clean diff
  and `git diff --check`.
