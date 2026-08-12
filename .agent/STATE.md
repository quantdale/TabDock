# Agent state

## Current checkpoint — PRODUCTION DIAGNOSTICS FOUNDATION (2026-08-12)

Objective: make every distributed TabDock executable self-identifying and make
a broken session diagnosable locally without a debugger, checkout, telemetry,
or product-state mutation. This is Phase 0 instrumentation only; Shepherd V2,
group-shell changes, and split-semantic changes remain deferred.

Status: **COMPLETE; FINAL RELEASE ARTIFACT VERIFIED**

The repository baseline was clean `main` at `04c214f5776b25b2dbe9cfc1ed02e7d982e9561b`,
equal to `origin/main`; the terminal is non-elevated. The final diagnostics
commit is unpushed. The prior active
`startup-group-visibility` OpenSpec change is preserved and untouched. New
OpenSpec change: `production-diagnostics-foundation`.

Completed in this phase:

- Generated assembly metadata + `BuildIdentity` expose semantic version,
  informational version, SourceRevisionId/commit, configuration, RID,
  deployment model, architecture, file version, executable path, and explicit
  command-time SHA-256. No build timestamp was added.
- Early CLI dispatch provides UI-free `--version`, read-only `--doctor`,
  `--doctor --output`, `--support-bundle --output`, and deterministic
  `--selftest-diagnostics`. Normal startup logs exactly one `BUILD[identity]`
  line before ordinary startup work.
- Structured environment/persistence/monitor-DPI/display-adapter probes,
  privacy-safe observed HWND rows, z-order/foreground/visibility/iconic state,
  DWM/DPI/process identity, and fixed header/content/split WindowFromPoint
  probes are implemented. Destroyed/access-denied probes degrade per row.
- `ContainerWindow.CreateDiagnosticSnapshot` supplies current logical group,
  active guest, split pair/focus, expected panes, visibility/window state, and
  chrome interaction without layout or native writes. The in-process
  `Ctrl+Alt+Shift+D` hotkey exports this richer live snapshot; command-line
  doctor/bundle remains machine/native/persistence capable without IPC.
- `DiagnosticTrace` is a thread-safe 1024-entry sequence-numbered ring with
  selected callback/dispatch WinEvents, lifecycle/group/split/activation,
  move-size, and Shepherd repair outcomes. No global location-change hook or
  health polling was added. Bundles contain sanitized log tails and no upload.
- README, architecture/testing docs, and an internal diagnostics waypoint are
  updated with the friend-machine workflow and deferred roadmap.

Validation already green: `dotnet build TabDock.csproj --no-restore` (0/0),
`--selftest-diagnostics` (15 checks, 0 failures), OpenSpec all (14/14),
`git diff --check`, CLI version/doctor no-state mutation, doctor file export,
and support ZIP contents (all 9 expected files; no obvious user-path/token
matches). Release/solution/ValidationDriver/GuineaPig/Spike builds,
`scripts/validate.ps1`, geometry self-test, Release `--version`/`--doctor`,
support-bundle inspection, publish identity comparison, reproducibility check,
and final privacy/static review are green. The final Release `--version` and
`--doctor` report the final source HEAD and artifact SHA; a second canonical
publish produced the same SHA. The final support ZIP exited 0, contains all 9
documented entries, and has no matches for the local username, machine name,
or credential-like terms. The final OpenSpec handoff tasks are all complete.

Handoff is ready. No cross-machine reproduction was performed in this local
session; that remains an explicit qualification step for the known artifact.

## External-machine qualification checkpoint (2026-08-12)

The evidence-only qualification campaign is active. The exact self-contained
Release publish at
`bin/Release/net8.0-windows/win-x64/publish/TabDock.exe` is proven to be
commit `860ab0708e2dd20dbbc1a53e06bbfc233ac46bc8`, with the expected SHA-256
`BA06F87561C23A32A0B73B00DE1D7A13EF987607E46A8D546BF29B3DA9A5518F`.

Evidence root: `C:\Users\Michael Roy\Desktop\TabDock-Qualification-20260812-205431`.
The baseline doctor and support bundle succeeded and contain all nine expected
entries. `%APPDATA%\TabDock` was preserved; it contained only `logs\TabDock.log`
and no `state.json` or `hidden-windows.json`. No production code was changed.

Independent CIM fingerprint reports Windows 11 Home Single Language, build
26200, one 1920x1080 100% monitor, AMD Radeon(TM) Graphics driver
32.0.21045.1000, and NVIDIA GeForce RTX 2050 driver 32.0.16.1062. The doctor
labels the Windows product as Windows 10 while reporting the same build; this
diagnostic discrepancy is recorded for the final instrumentation review.

Current phase: awaiting the human-supervised header-disappearance reproduction
and its in-process `Ctrl+Alt+Shift+D` bundle before any recovery action.

---

## Historical checkpoint — STARTUP GROUP VISIBILITY HARDENING (2026-08-12)

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
