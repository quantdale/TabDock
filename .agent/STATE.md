# Agent state

## Current checkpoint — STARTUP GROUP VISIBILITY HARDENING (2026-08-12)

Objective: fix the HIGH-priority startup z-order defect where a restored/opened
TabDock group can be hidden behind an already-existing overlapping desktop
window at launch. Readdres the production-readiness gate.

Status: **FIX IMPLEMENTED + BUILD/VALIDATION GREEN. RESOLVED PENDING MANUAL
VISUAL CONFIRMATION** (deterministic CLI-safe reproduction of the burial was
not achievable in this session; the supervised scenario is ready to run).

## POST-HARDENING FINDING

> During TabDock startup, a restored/opened group can end up hidden behind an
> already-existing desktop window when the TabDock group initially overlaps
> that window.

### Root cause (source-verified, High confidence)

The startup-restore path (`App.Application_Startup` -> `OpenContainer`) shows
each restored (empty) container with a bare `window.Show()` and never issues an
explicit z-order or activation claim, then hides the launcher. Whether the
container lands above or below an overlapping pre-existing window depends
entirely on the OS foreground grant at the moment of `Show()`. When that grant
is missing (auto-start, slow startup, user focused elsewhere) the container is
parked beneath the overlapping window. Nothing repairs it: restored groups are
EMPTY (persistence is metadata-only), so `IsMonitoringNeeded` is false and the
WinEvent hooks are not installed; and every container z-order memory
(`LayoutShepherdActiveWindow`, the `WM_ACTIVATE` reassert,
`PairZOrderBehindGuest`) requires a live guest, which an empty restored
container has none of. The burial persists for the session.

### Fix (minimal, at the z-order authority)

New one-shot `App.ReconcileRestoredContainerZOrder()` called from
`Application_Startup` right after the restored-container loop. It raises each
restored container to the top of the **normal** z-order band via the existing
`WindowShepherdService.RaiseContainerForChrome(hwnd)` primitive
(`HWND_TOP` + `SWP_NOACTIVATE`), bounded (one write per container, once), with
a single `STARTUP[reconcile]` diagnostic. No `Activate`/`SetForegroundWindow`:
z-order-only, cannot steal focus, respects later user activation. (TabDock has
no supported background/no-activation launch path, so there is no such mode to
preserve; the accurate policy is "raised in the normal band without taking
focus.") Preserves the Shepherd `guest-above-container` invariant
(vacuous at startup; re-established by `PositionAndShow` on first capture).
No guest style/owner/geometry mutation; no SetParent/WS_CHILD/HWND_TOPMOST/
Topmost/loop.

### Reproduction / evidence

- CLI-safe native check (temporary script, snapshot/restore of real
  `state.json`, no SendInput): post-fix the restored container is visible, its
  center resolves to the TabDock PID via `WindowFromPoint`, and it is above the
  overlapping blocker in `GW_HWNDNEXT` z-order. Real user state restored.
- **Honest caveat**: the CLI-safe run could NOT force the burial — a launched
  TabDock still received foreground, so pre-fix also kept the container on top.
  The burial is an OS foreground-grant race. The discriminating proof is the
  supervised scenario `startup-group-not-hidden-behind-existing-window`, which
  uses the harness's background-launch + real-input path; it needs supervision
  and was NOT run in this session.

### Tests added (ValidationDriver, supervised; built + discoverable, not run)

- `startup-group-not-hidden-behind-existing-window` (reproduction; native
  z-order/WindowFromPoint assertions)
- `startup-does-not-steal-foreground-after-external-activation` (guard)
- `startup-local-stack-above-unrelated-when-guest-present` (guard, joins `all`)
- File: `tests/ValidationDriver/.../Scenarios.StartupHide.cs`; registered in
  `Scenarios.cs` (`AllOrder`, `StandaloneExtraScenarios`, `RunScenario`).

### Validation (CLI-safe, all PASS)

- Builds: `TabDock.csproj`, `TabDock.sln`, ValidationDriver, GuineaPig, Spike —
  0 warnings / 0 errors.
- `scripts/validate.ps1`: PASS. `TabDock.exe --selftest-geometry`: PASS (exit 0).
- `openspec validate --all --no-interactive`: 13/13 PASS.
- `git diff --check`: PASS (only expected LF/CRLF conversion warnings).
- Architecture audit: no SetParent, WS_CHILD, HWND_BOTTOM, HWND_TOPMOST,
  Topmost, SetForegroundWindow loop, guest style/owner mutation; the
  `window.Show()` activation semantics are unchanged (WPF handles it); the
  one-shot raise is z-order-only.

### OpenSpec

- New change `startup-group-visibility` (proposal + spec + design + tasks),
  `openspec validate --change` PASS. NOT archived (completion criteria not yet
  fully proven — supervised run outstanding).

### Manual visual acceptance

NOT YET CONFIRMED by the user. Exact checklist in the final report / goal §41:
Open an unrelated app overlapping TabDock's launch position, launch TabDock,
confirm the group is not hidden behind it, then click the unrelated app (it
comes in front, TabDock does not re-steal) and click TabDock again (stack
restores). Repeat several cycles; test Explorer/Edge/Terminal, full/partial
overlap, maximized external window.

## Next action

Run the supervised ValidationDriver batch (at minimum the three new startup
scenarios plus adjacent z-order/direct-click/popup/split/maximize scenarios),
complete manual visual acceptance, then archive the OpenSpec change and create
the milestone commit. Do NOT push. Final assessment must remain "RESOLVED
PENDING MANUAL VISUAL CONFIRMATION" until the user confirms visually.
