# Agent state

## Current checkpoint — WHOLE-CODEBASE DEEP-AUDIT REMEDIATION (2026-08-13)

Objective: re-audit the current HEAD against the 2026-08-12 findings and
produce a production-grade, validated remediation without claiming unrun
desktop acceptance.

Status: **REMEDIATION IMPLEMENTED; AUTOMATED VALIDATION GREEN; TARGETED
SUPERVISED RUNS GREEN; HUMAN VISUAL QUALIFICATION PENDING**

- Branch: `agent/tabdock-deep-audit-remediation`, based on current HEAD
  `d0cea29fd1b8b60008eb3d7021b3c6859951583a`. The initial worktree was clean;
  no unrelated changes were present or modified.
- Implementation commit: `0fca47d33d5955b4cb6fcba5a24c26fb44adf89c`
  (`fix: harden whole-codebase audit contracts`). State-sync commit:
  `edd14bcc43ce7cf6d9554d785255c45256a34738` (`docs: record audit
  remediation checkpoint`). The final release artifact is rebuilt from the
  final branch HEAD after this checkpoint; its exact identity is recorded in
  the handoff.
- CI guard follow-up: `4e405803d751003285d0ce40accb675877328e89` makes the
  hosted SendInput exclusion check inspect command lines without matching its
  own regex source. The replacement hosted run `31642601057` passed all steps
  for the pushed branch; the earlier run `31642239988` correctly exposed and
  failed on that self-match.
- Final code-bearing branch commit: `4a92b8fdc12c5a543e3a775806b8bbbe3bac9245`.
  Hosted run `31642798762` passed all steps for that commit. The verified
  self-contained Release artifact built from it reports the same commit and
  SHA-256 `BB566397C13D36DCC6C72E6FED1578A06864D62D06CAFFAA4EF691DAD3CC500A`.
  A subsequent state-only handoff commit may advance HEAD without changing
  the audited application or artifact.
- Active plan: `.agent/plans/deep-audit-remediation-2026-08-13.md`.
- Implemented: pointer-sized HDWP chaining/fallback, corrected
  `WINDOWPLACEMENT` ABI and layout, ShowWindow postcondition handling,
  mutable-title exclusion from ongoing capture identity, removal of production
  DWM mutation, embedded privacy redaction, Windows-generation normalization,
  SID-scoped cross-session instance guard, deterministic geometry/diagnostic
  checks, mutable-title ValidationDriver coverage, CI deterministic gates, and
  the interop audit note.
- Additional confirmed interop fix: `WINDOWPLACEMENT` had omitted `rcDevice`
  and was passed as `out`; it now matches the SDK and is initialized by callers.
- ContainerWindow extraction remains deliberately deferred. No safe seam was
  required for the correctness fixes, and a line-count-only refactor would
  increase HWND ownership risk.
- Observed supervised Windows-desktop runs in this remediation: mutable-title
  capture, DWM-attribute unchanged-on-capture, split entry, split direct-click
  text input, split native move re-glue, split native resize re-glue,
  split minimize/restore, restored-container visibility above a blocker,
  startup foreground non-steal, and local guest/container z-order above an
  unrelated blocker. Each completed PASS with driver cleanup and user-state
  restoration.
- Per-user duplicate-process check also passed: a first Debug instance held
  the SID-scoped guard and a second same-user instance exited 0; no TabDock
  process remained afterward.
- Remaining work: complete the state-only handoff commit and confirm the
  pushed draft PR. No application remediation remains; the manual visual and
  broader real-desktop qualification gate remains explicitly open.
- Manual gate: the targeted scenarios above are evidence for their named
  invariants, not a full visual acceptance. Browser/real-app coverage,
  mixed-DPI hardware, crash-rescue, the full ValidationDriver batch, and the
  human visual checklist remain explicitly unclaimed.

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
32.0.21045.1000, and NVIDIA GeForce RTX 2050 driver 32.0.16.1062. The
historical artifact's doctor labeled the product as Windows 10 while reporting
the same build; the current remediation retains the raw value and normalizes
build 26200 to Windows 11.

Historical campaign phase (2026-08-12): awaiting the human-supervised
header-disappearance reproduction and its in-process `Ctrl+Alt+Shift+D` bundle
before any recovery action.

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
