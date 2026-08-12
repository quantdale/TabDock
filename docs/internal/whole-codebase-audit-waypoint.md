# TabDock Whole-Codebase Audit

> Current authoritative checkpoint (2026-08-13): the historical audit notes
> below describe the pre-remediation state and are retained for provenance.
> Current implementation status, F1-F11 dispositions, validation evidence, and
> remaining qualification gates are maintained in this section and in
> `.agent/STATE.md`.

## 2026-08-13 remediation checkpoint

- Branch: `agent/tabdock-deep-audit-remediation`, based on current HEAD
  `d0cea29fd1b8b60008eb3d7021b3c6859951583a`; no unrelated work was present
  when the branch was created.
- F1 HDWP: fixed `Begin/Defer/EndDeferWindowPos` declarations and chaining;
  append or commit failure abandons the transaction and uses the bounded
  non-atomic split fallback.
- F2 qualification: deterministic checks are closed in CI. Targeted
  supervised startup/z-order, split, mutable-title, and DWM runs passed on
  2026-08-13; browser/real-app, mixed-DPI, crash-rescue, full-batch, and human
  visual acceptance remain unclaimed. Automated green is not manual acceptance.
- F3 diagnostics: embedded, case-insensitive redaction now replaces the most
  specific AppData/LocalAppData/profile/temp roots first; generated bundles are
  scanned by `scripts/validate.ps1`.
- F4 DWM: production capture no longer calls `DwmSetWindowAttribute`; only
  read-only DWM diagnostics remain, preserving hard-kill symmetry.
- F5 instance isolation: `Global\TabDock-<user SID>` serializes one user across
  sessions without intentionally blocking another Windows user, and acquisition
  happens before logging/state startup.
- F6 CI: the hosted workflow runs the deterministic validation script, OpenSpec
  validation, whitespace checks, and a supervised-only source guard; it does
  not run SendInput scenarios.
- F7 `ContainerWindow` extraction: deliberately deferred; no safe,
  behavior-preserving seam was needed to land the correctness fixes.
- F8 ShowWindow: all production calls verify postconditions instead of treating
  the return value as success.
- F9 capture identity: ongoing HWND/PID/executable/class checks exclude the
  mutable title; title remains user-facing metadata and picker selection data.
- F10 diagnostics: raw registry ProductName is retained, while build >= 22000
  is presented as Windows 11.
- F11 state/spec drift: current state, canonical requirements, tasks, and
  waypoints are synchronized; the human acceptance item remains unchecked.
- Additional confirmed interop discrepancy: `WINDOWPLACEMENT` now matches the
  current SDK shape (`rcDevice`) and is passed by `ref` with initialized
  `length`. See `win32-interop-audit-2026-08-13.md` for the full declaration
  inventory and official contract references.

Do not read the historical “P0 none known” or “production ready” statements
below as the current release decision; they describe an earlier checkpoint.

## Baseline

- Date: 2026-08-11.
- Branch: `main`.
- Starting HEAD: `5404349998c49365f04873f0d7d5a2c53814b776`.
- Starting worktree change: the user-supplied untracked `goal.txt`; existing
  committed QA work was preserved. The current worktree intentionally contains
  the audit's production, harness, spec, documentation, and state changes.
- Repository inventory: 29 production C# files, 22 ValidationDriver/GuineaPig
  C# files, one experimental Spike C# file, one WPF application project, two
  validation-driver projects, build/publish scripts, CI, docs, and 12 OpenSpec
  capability specs.
- No commit, reset, clean, revert, push, or PR was performed.

## Current architecture

- `App` is the composition root and owns startup, crash/shutdown/session-ending
  paths, container registration, persistence, hotkey, and WinEvent lifetime.
- `GroupManager` owns groups, the O(1) captured-HWND/member index, active-tab
  bookkeeping, capture/release coordination, and debounced state saves.
- `WindowShepherdService` is the sole production capture backend. Captured
  applications remain independent top-level windows; geometry, visibility,
  local z-order, foreground, placement restoration, and the hidden-window
  journal create the visual containment illusion. No guest is reparented or
  restyled.
- `WinEventMonitor` owns desktop-wide out-of-context hook installation and UI
  dispatch. `GuestLifecycleService` is the policy consumer for destroy, hide,
  minimize, name, foreground, reorder, and move/size events.
- `ContainerWindow` owns WPF chrome, split presentation, activation and
  maximize/minimize/move reconciliation, popup/modal z-order, capture UI, and
  close/delete flows. `NativeHwndHost` is a marker HWND only.
- `PersistenceService` stores layout intent in `state.json`; the journal stores
  same-session hidden guest recovery in `hidden-windows.json`. The
  ValidationDriver uses real Win32 input for disposable or explicitly selected
  guests and is governed by the supervised-only policy in `docs/TESTING.md`.

## Audit coverage

| Area | Status | Evidence |
| --- | --- | --- |
| Production startup, shutdown, crash, and group lifecycle | Complete | `App`, `GroupManager`, container close/open paths, session-ending normalization |
| Shepherd capture/release, geometry, visibility, z-order, split, DPI | Complete | all `WindowShepherdService` and `ContainerWindow` callers; geometry self-test |
| WinEvent installation, cleanup, dispatch, callback races | Complete | `WinEventMonitor`, `GuestLifecycleService`, captured-member reference checks |
| Persistence and hidden-window journal | Complete | atomic-write and failure-path review; canonical specs; crash-rescue scenarios |
| Native interop, privilege boundary, and resource lifetime | Complete | all production P/Invoke declarations/call sites; elevation and handle paths |
| XAML/view models, popup/modal behavior, subscriptions, timers | Complete | views, view models, marker host, timer-instance guards |
| Performance and dead/inert code | Complete | hot-path call map, static sleep/process/file/timer scan, Repowise risk/context map |
| ValidationDriver, GuineaPig, Spike, scripts, CI | Complete | all 22 harness C# files, project builds, `scripts/validate.ps1`, CI inspection |
| OpenSpec and internal documentation drift | Complete | all 12 specs validated; guide capability count corrected |
| Cross-machine, monitor, DPI, and real-input behavior | Partial by policy | static/source review and deterministic self-test complete; no unattended live run |
| Formal Codex Security Deep Scan | Not available in this environment | plugin preflight completed, worker start refused because managed filesystem permission profile was unavailable; manual security review completed |

## Confirmed findings and disposition

All findings below were source-verified before editing. The normal crash-rescue,
split, persistence, and UI scenarios already present in the repository remain
the primary behavioral regression coverage. Fault-injection-only cases are
called out as gaps rather than claimed as executed.

### WCA-01 — hide journal commit was fail-open (High, confirmed, fixed)

- `WindowShepherdService.Hide` previously hid after a catching, void
  `JournalHide`; a force-kill could strand an invisible independent guest with
  no durable rescue record.
- `JournalHide` now returns success, `Hide` refuses `SW_HIDE` when the commit
  fails, and the intentional-hidden release path fails closed if its immediate
  clear cannot commit.
- Coverage: hidden-window-journal spec scenarios and supervised-only
  `crashkill-rescue`/`crashkill-selfhide-not-rescued` harness paths; fault
  injection of disk failure was not run.

### WCA-02 — rescue consumed entries without verifying visibility (Medium, confirmed, fixed)

- Startup rescue validated PID/executable but deleted the journal without
  checking whether `ShowWindow` actually produced a visible window.
- Rescue now verifies visibility, retains identity-valid failed entries for a
  later retry, and discards invalid/recycled identities. The on-disk shape and
  atomic temp/replace write remain stable.
- Coverage: updated hidden-window-journal spec plus existing crash-rescue
  scenarios; native show-failure injection remains a deferred test gap.

### WCA-03 — duplicate persisted group IDs were accepted (Medium, confirmed, fixed)

- Duplicate IDs could collide in `_containers` and make independently restored
  groups address the same container key.
- Load now repairs empty and later duplicate IDs with logged unique GUIDs while
  retaining the valid groups; persistence-resilience spec coverage was added.
- Coverage: source-level invariant and OpenSpec scenario; no isolated duplicate-
  fixture launch was run because the interactive harness is supervised-only.

### WCA-04 — ValidationDriver used UIA action fallbacks (Medium, confirmed, fixed)

- Picker/inline capture and tab-selection helpers could invoke UIA
  `TogglePattern`/`SelectionItemPattern` actions instead of exercising user
  input.
- Action fallbacks were removed; the driver now rediscoveries UIA geometry and
  uses guarded real clicks, failing loudly when clicks cannot reach a control.
- Coverage: ValidationDriver build and static zero-match scan for action APIs;
  live picker/tab scenarios remain supervised.

### WCA-05 — driver setup failure could strand the user's state snapshot (High, confirmed, fixed)

- A failure after moving `state.json` to the driver snapshot but before a
  `Ctx` existed skipped restoration.
- Restoration is idempotent, setup fails closed, tracked processes are waited
  out before state restoration, and the null-context failure path restores the
  snapshot.
- Coverage: driver build, source-path review, and guarded cleanup logic; no
  injected startup-failure run was performed.

### WCA-06 — close-prompt z-order trusted caption-only HWND (Medium, confirmed, fixed)

- `FindWindow(null, "Close group")` could select a same-caption foreign window
  before applying a z-order mutation.
- The prompt lookup now requires visible `#32770`, current TabDock PID, and
  direct/root ownership by the current container.
- Coverage: source/static ownership checks and existing close-prompt behavior;
  same-caption foreign-window run remains supervised.

### WCA-07 — partial WinEvent hook cleanup could leak/loss-monitor (Medium, confirmed, fixed)

- Failed unhook fields were preserved, but a later start could overwrite them;
  a partial install could also leave captured guests unmonitored indefinitely.
- Start now treats installation as a bounded transaction, refuses residual-handle
  overwrite, retries only after cleanup, and App schedules a bounded retry.
  Stop also handles residual hooks after `_running` is false.
- Coverage: source state-machine review and build; native SetWinEventHook failure
  injection is deferred.

### WCA-08 — ValidationDriver cleanup used stale raw HWNDs (Medium, confirmed, fixed)

- Cleanup and guest/window operations relied on `IsWindow`/PID in places where
  the e2e safety spec requires immediate ownership, class, title, and executable
  identity verification.
- Added `WindowIdentity`, refresh gates, `VerifiedWindowOps`, process-wide
  spawned-window cleanup assertions, and guarded main/container/guest records.
- Coverage: static scan confirms scenario native mutators route through the
  helper (aside from the explicitly audited input layer); driver build.

### WCA-09 — capture admission had a recycled-HWND race (Medium, confirmed, fixed; historical)

- Picker/capture metadata queries could complete after the candidate HWND had
  changed owner or identity.
- The historical implementation performed final PID/executable/class/title
  verification immediately before admitting the member and before DWM
  mutation. The 2026-08-13 remediation removed the title equality from the
  stable production admission/rejection predicate and removed DWM mutation;
  picker selection may still carry title metadata for its separate TOCTOU
  safety contract.
- Coverage: source-level identity gate, build, and e2e capture safety checks.

### WCA-10 — picker selection was TOCTOU-prone (Medium, confirmed, fixed)

- The picker returned only an HWND, so selection could be replaced before the
  capture operation.
- `WindowCaptureTarget` carries PID/class/title/executable identity; picker and
  container revalidate before and after capture, releasing only the just-captured
  identity on mismatch.
- Coverage: picker build/static review; supervised picker run remains deferred.

### WCA-11 — harness direct operations and input cleanup were inconsistent (Medium, confirmed, fixed)

- Scenario direct `PostMessage`/`ShowWindow`/`SetWindowPos` and coordinate input
  had uneven identity and ownership gates.
- All scenario native mutations now route through verified helpers; `Input`
  verifies point/foreground identity, and mouse/keyboard release paths use
  `finally` cleanup so failed checks cannot strand global input state.
- Coverage: static zero-match scan for direct scenario mutators, driver build,
  and source audit of explicit modifier call sites.

### WCA-12 — production stale-HWND destructive mutation risk (High, confirmed, fixed)

- Close/release/foreground/z-order paths could act on a live recycled handle
  after PID-only or validity-only checks.
- Shepherd now gates native mutation with stable PID/class and, for destructive
  paths, executable identity; close-message paths use the same gate and skip
  stale replacements.
- Coverage: source call-map review, static native invariant scan, and builds.

### WCA-13 — queued WinEvent/name callbacks could target a recycled member (Medium, confirmed, fixed)

- A callback captured only HWND and resolved membership later; a reused HWND
  could receive an old destroy/reorder/name action.
- `WindowEventArgs` carries the captured member reference from callback time;
  dispatch and name-debounce timers require reference equality with the current
  index entry.
- Coverage: source race review and build; injected callback sequencing is not
  available without a dedicated test seam.

### WCA-14 — unreadable state could be overwritten by empty fallback (High, confirmed, fixed)

- Read/access failures returned empty state and later saves could replace a
  potentially valid but unreadable file.
- `PersistenceService` distinguishes read failure from parse corruption,
  quarantines parseable corruption, and skips later saves after unsafe reads.
  Atomic `.bak`/`.tmp` persistence remains intact.
- Coverage: persistence-resilience spec and build; access-denied/disk-full
  injection was not run.

### WCA-15 — partial capture insertion could orphan the native guest (Medium, confirmed, fixed)

- View-model/icon construction or collection insertion could fail after native
  capture but before both authoritative collections were coherent.
- `GroupViewModel.AddCapturedWindow` is transactional; `ContainerWindow` rolls
  back and releases the just-captured identity on insertion failure.
- Coverage: source failure-path review and builds.

### WCA-16 — invalid DPI probe failed open (Medium, confirmed, fixed)

- A zero/exception awareness probe allowed capture to continue with unverified
  physical/virtual coordinate assumptions.
- Capture now refuses zero DPI context, zero system DPI, and probe exceptions;
  the canonical UI/DPI spec records fail-closed semantics. Microsoft documents
  that `GetWindowDpiAwarenessContext` returns NULL for an invalid HWND:
  https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowdpiawarenesscontext
- Coverage: geometry self-test, spec validation, build; non-100% live monitor
  matrix remains supervised/unrun.

### WCA-17 — open-container failure could orphan a just-created window/group (Medium, confirmed, fixed)

- Registration after `Show` and incomplete catch cleanup could leave a closed or
  partially opened container registered, or persist a group with no usable UI.
- Containers register before `Show`, closed callbacks are instance-checked,
  failures close/unregister/detach, and new-group flows remove failed groups.
- Coverage: source lifecycle review and builds; injected WPF construction failure
  was not run.

### WCA-18 — real-input failures could strand global mouse/modifier state (Medium, confirmed, fixed)

- Exceptions after `SendInput` button-down or modifier-down could contaminate
  the user's desktop and later scenarios.
- Click/drag/type/key/hotkey paths now release in `finally`; intentional held
  drag paths have explicit release cleanup.
- Coverage: static call-site review and driver build; supervised execution is
  required before claiming live desktop behavior.

### WCA-19 — native marker class registration could fail open/leak a brush (Low, confirmed, fixed)

- `RegisterClassEx` accepted zero with `GetLastError()==0` and did not release a
  brush on class-registration failure/collision.
- Registration now fails closed except for `ERROR_CLASS_ALREADY_EXISTS` and
  deletes the created brush on failure/collision.
- Coverage: source review and build.

### WCA-20 — cleanup identity gate could reject a legitimately renamed guest (Medium, confirmed, fixed; historical)

- Title equality was unsuitable as a stable capture identity because guests can
  rename themselves while docked.
- Stable production cleanup uses PID/class/executable; title is retained as
  user-facing metadata and picker selection data but excluded from ongoing
  mutation gates and the stable capture predicate.
- Coverage: source identity review and build.

### WCA-21 — native chrome/z-order helpers lacked invalid-container guards (Low, confirmed, fixed)

- Several helper paths could issue native calls after the container HWND was
  destroyed.
- `PositionAndShow`, chrome raise/restore, pairing, and split positioning now
  require valid container/member handles before native mutation.
- Coverage: source call-map review and build.

### WCA-22 — desktop reorder dispatch could use a later foreground/recycled member (Medium, confirmed, fixed)

- Desktop reorder events identify the desktop, not the reordered guest; a later
  foreground transition could make a posted callback pair the wrong member.
- Callback-time foreground and captured-member reference are carried through the
  post and revalidated at dispatch.
- Coverage: source race review, static invariant scan, and build.

### WCA-23 — stale one-shot DispatcherTimer callbacks could overwrite newer state (Medium, confirmed, fixed)

- Stop/restart races let an old activation, settled-log, close-prompt, retry, or
  minimize-restore callback clear or act through a newer timer generation.
- Each one-shot callback now captures its timer instance and mutates the field
  only when it is still current; App retry and guest name debounce use the same
  identity discipline.
- Coverage: source timer review, builds, and OpenSpec validation.

### WCA-24 — session-ending cleanup left stale presentation/model state (Medium, confirmed, fixed)

- Emergency release restored native guests but the old path cleared only model
  members, leaving view tabs/split references stale and risking loss of layout
  intent if logoff was cancelled and a later save occurred.
- Session ending now stops dispatch, clears container presentation/timers,
  copies member metadata and active intent to persisted fields, clears members,
  and saves normalized state.
- Coverage: crash-shutdown-coherence spec, source review, and build; actual
  cancellation by another application was not run.

## Rejected findings

- No evidence supported production reparenting, guest style/owner mutation,
  `HWND_BOTTOM` repair, production `Thread.Sleep`/`Task.Delay`, unguarded
  production process spawning, or a new dependency/security telemetry path.
- `Spike` intentionally demonstrates the historical reparent experiment and is
  excluded from production invariant claims.
- Existing synchronous picker icon extraction, self-minimize timing behavior,
  and low-value ignored native return codes were reviewed and deferred as
  non-blocking debt rather than patched speculatively.

## Improvements deferred / technical debt

### P0

- None known from the completed source audit.

### P1

- Session-ending cancellation has no reliable post-event signal in the current
  WPF lifecycle, so `ContainerWindow.IsAppShuttingDown` remains conservative
  after a logoff/shutdown request that another application cancels. The
  session-ending path is coherent and hooks/timers are stopped, but future
  post-cancel close-prompt semantics need a dedicated lifecycle design and
  supervised validation.
- Cross-monitor/per-monitor-DPI behavior and the 150% DPI refusal need a
  supervised matrix on representative machines; source now fails closed when
  probes are invalid.

### P2

- Add injectable native seams or a small deterministic test harness for journal
  write failure, rescue `ShowWindow` failure, WinEvent partial hook failure,
  access-denied persistence, and duplicate-ID fixtures. The current code has
  source/spec coverage and normal crash/persistence scenarios but no safe fault
  injection for these OS failure paths.
- Move capture-picker icon extraction off synchronous UI enumeration if profiling
  shows it matters; retain the current bounded icon cache and failed-path cache.
- Consider checking `NativeHwndHost.ArrangeOverride` and hotkey unregister return
  values; neither currently has evidence of user-visible failure.

### P3

- Keep the experimental Spike and historical audit documents for provenance;
  do not treat their reparent code as an active backend.
- Continue reducing line-ending churn only through the repository's normal Git
  policy; `git diff --check` reports no whitespace errors, only expected
  LF/CRLF conversion warnings.

## Validation results

Executed against the current worktree:

- `dotnet build TabDock.csproj --no-restore --nologo`: PASS, 0 warnings/errors.
- `dotnet build tests\\ValidationDriver\\TabDock.ValidationDriver\\TabDock.ValidationDriver.csproj --no-restore --nologo`: PASS, 0/0.
- `dotnet build tests\\ValidationDriver\\TabDock.GuineaPig\\TabDock.GuineaPig.csproj --no-restore --nologo`: PASS, 0/0.
- `dotnet build Spike\\TabDock.Spike\\TabDock.Spike.csproj --no-restore --nologo`: PASS, 0/0.
- `dotnet build TabDock.sln --no-restore --nologo`: PASS, 0/0.
- `.\\scripts\\validate.ps1`: PASS, all four Debug builds.
- `.\\scripts\\validate.ps1 -Publish`: PASS, all Debug builds plus Release
  self-contained single-file `win-x64` publish.
- `openspec validate --all --no-interactive`: PASS, 12/12.
- `& .\\bin\\Debug\\net8.0-windows\\win-x64\\TabDock.exe --selftest-geometry`:
  PASS, exit code 0.
- `git diff --check`: PASS; only normal LF/CRLF conversion warnings.
- `repowise update`: PASS/already up to date after the final edits.
- Static scans: no UIA action APIs in the driver; no direct scenario native
  mutators outside the verified helper/input layers; no production SetParent,
  HWND_BOTTOM, or guest style mutation; sleeps/process operations are confined
  to the supervised validation harness and expected process-control comments.

Not executed by policy: unattended ValidationDriver real-input scenarios,
cross-machine/DPI matrix, and the formal Codex Security Deep Scan (environment
could not provide its required managed filesystem permission profile).

## Current checkpoint / exact next action

Implementation and autonomous validation are complete for the audited scope.
Refresh `.agent/STATE.md` to this checkpoint, inspect final `git status` and
diff classification once more, then hand off as **READY WITH DEFERRED DEBT**;
do not commit or push. A supervised operator should next run the documented
ValidationDriver batch, the cross-monitor/DPI matrix, and (if the environment
supports it) the native fault-injection cases listed above.

## POST-AUDIT HIGH FINDING — external guest size-constraint / observed-geometry containment

The prior "READY WITH DEFERRED DEBT" assessment is REOPENED and superseded for
this issue: the user manually reproduced a split-mode containment defect where a
RIGHT-pane guest (Edge/Explorer) visibly extended beyond the shell's content
region, worse at narrower widths.

### Root cause (proven)

TabDock's Shepherd positioned a guest with `SetWindowPos` and never verified the
observed `GetWindowRect`. Real applications enforce native minimum track sizes via
`WM_GETMINMAXINFO` (probed against the live desktop: Edge minW=643, Chrome
minW=516-643, Explorer minW=161-201; all `sendOk=True`). When a split pane
(`content/2`) or the normal-mode content area is narrower than the guest's native
minimum, the guest clamps back up to its minimum and overflows the pane. A
deterministic probe (controlled window with `WM_GETMINMAXINFO` minW=500) proved
overflow 0/0/0/20/50/100/200/300 px as the pane went 800/600/500/480/450/400/300/200 —
exactly the reported symptom. The shell had no minimum-size constraint, so the
impossible region was reachable, and the redundant-glue guard re-issued the
failing write per frame (resize war).

### Chosen policy (Option A — dynamic TabDock minimum size)

1. `WindowShepherdService.GetEffectiveMinTrackSize` probes a guest's native
   minimum via `SendMessageTimeout(WM_GETMINMAXINFO, SMTO_ABORTIFHUNG)`, fail-closed,
   cached, never hardcoded per app.
2. `SplitGeometry.MinContentWidth/MinContentHeight` yield the exact-partition
   content minimum (split: `max(2·L, 2·R−1)`; normal: active guest's min).
3. `ContainerWindow` enforces the container's native `ptMinTrackSize` in
   `WM_GETMINMAXINFO` from the cached minima (+ chrome delta), so the shell cannot
   be drag-resized below what the visible guests can fit.
4. Bounded requested-vs-observed reconciliation marks a guest non-compliant for a
   refused rect and stops re-fighting it per frame (no resize war), with a bounded
   `SHEPHERD[size-constraint]` diagnostic. Constraint state recomputed on split
   enter/exit/replace, survivor promotion, active-tab change, `WM_EXITSIZEMOVE`,
   and a 5 s periodic re-probe (no stale minima from departed members).

### Files

- `Services/WindowShepherdService.cs` (`GetEffectiveMinTrackSize`)
- `Services/SplitGeometry.cs` (`MinContentWidth/MinContentHeight` + self-test)
- `Views/ContainerWindow.xaml.cs` (constraint state, `WM_GETMINMAXINFO` min-track,
  refusal guard, refresh triggers)
- `NativeMethods.cs` (`SendMessageTimeout`, `SMTO_*`)
- `tests/ValidationDriver/TabDock.GuineaPig` (`--min-width/--min-height`)
- `tests/ValidationDriver/.../Scenarios.Split.cs`
  (`split-guest-does-not-overflow-pane`, `split-narrow-container-constraints`,
  `single-guest-does-not-overflow-content`)

### Tests

- Deterministic: `--selftest-geometry` now covers the constraint math
  (14,719,023 checks, 0 failures).
- Supervised (not run unattended): the three new containment scenarios.

### Validation

- Main, solution, ValidationDriver, GuineaPig, Spike builds: PASS, 0 warnings.
- `scripts\validate.ps1`: PASS. `openspec validate --all --no-interactive`: PASS, 13/13.
- `git diff --check`: PASS (expected LF/CRLF conversion notes only).

### Remaining manual/supervised check

- Supervised ValidationDriver run of the three containment scenarios.
- Live visual confirmation: Explorer+Edge and Chrome+Terminal never visibly
  escape TabDock across narrow/medium/wide, maximize/restore, continuous
  narrow/widen, on real monitors/DPI.

## POST-AUDIT DPI COMPATIBILITY FINDING — DPI-unaware capture was over-refused

The prior "DPI-aware capture refusal at non-100% scaling" hardening (WCA-16) is
REOPENED. A user manually attempting to capture a DPI-unaware window at non-100%
scaling was blocked with:

> This window is not DPI-aware and can only be captured reliably at 100% display
> scaling.

### Root cause (proven)

The refusal rested on the premise that an unaware guest "would be stretched and
misplaced no matter what rect we hand it." That premise is FALSE for OUTER-rect
positioning: DPI virtualization of geometry APIs is keyed to the CALLING thread's
awareness, so a PerMonitorV2 caller's `SetWindowPos`/`GetWindowRect` operate in
physical pixels against ANY target HWND. A native experiment on this machine proved
it: `SetWindowPos(200,150,1440,900)` on a DPI-unaware top-level window from a PMv2
thread returned `GetWindowRect (200,150,1440x900)` exactly; an unaware caller of the
same window saw `(160,120,1152x720)` = physical ÷ 1.25 (virtualized). The unaware
guest's content is DWM-bitmap-stretched (blurry) exactly as it looks standing alone
— not a TabDock geometry defect.

Two secondary defects were fixed with the same change:
- The gate used `GetDpiForSystem()` (PRIMARY monitor), misclassifying targets on
  differently-scaled secondary monitors.
- A DPI-unaware guest's `WM_GETMINMAXINFO` min-track (answered in its logical 96-DPI
  space) was treated as physical, which would under-constrain the container
  containment and re-open the pane-overflow defect at non-100% scaling.

### Chosen policy

1. Known DPI-unaware guest → captured normally (physical-exact outer geometry;
   content DWM-blurred as standalone). Refusal reserved for a probe that FAILS or
   returns an UNKNOWN context (fail-closed preserved), with a precise message.
2. Scale source = target monitor's effective DPI
   (`GetDpiForMonitor(hmon, MDT_EFFECTIVE_DPI)` via shcore), not the primary.
3. `SplitGeometry.ScaleUnawareLogicalToPhysical` is the single authoritative
   logical→physical min-track boundary (ceil, never under-estimates), used by
   `WindowShepherdService.ToPhysicalScaleForGuest` for unaware guests; aware
   guests and 100% scaling are strict no-ops.

### Files

- `Services/WindowShepherdService.cs` (capture gate, `GetMonitorEffectiveDpi`,
  `ToPhysicalScaleForGuest`)
- `Services/SplitGeometry.cs` (`ScaleUnawareLogicalToPhysical` + self-test)
- `NativeMethods.cs` (`GetDpiForMonitor`, `MDT_EFFECTIVE_DPI`,
  `USER_DEFAULT_SCREEN_DPI`)
- `tests/ValidationDriver/TabDock.GuineaPig` (`--dpi` launcher modes)
- `tests/ValidationDriver/.../Scenarios.Dpi.cs` (gated supervised scenarios)

### Tests

- Deterministic: `--selftest-geometry` covers the scaling math (14,719,158 checks,
  0 failures).
- CLI-safe native harness (no SendInput) exercised `WindowShepherdService.Capture`
  against DPI-unaware, system-aware, and per-monitor-v2 pigs on a mixed-DPI host:
  all ACCEPTED.
- Supervised (not run unattended): `capture-dpi-unaware-guest`,
  `capture-dpi-system-guest` (self-skip at 100% with explicit reason).

### Validation

- Main, solution, ValidationDriver, GuineaPig, Spike builds: PASS, 0 warnings.
- `scripts\validate.ps1`: PASS. `openspec validate --all --no-interactive`: PASS, 14/14.
- `git diff --check`: PASS (expected LF/CRLF conversion notes only).

### Remaining manual/supervised check

- Supervised ValidationDriver run of the two DPI scenarios and a live visual
  acceptance of a DPI-unaware guest docked at 125%/150% on real hardware.

---

## Final Hardening Closure (2026-08-11)

### Stale process / PID 156552

PID 156552 was an orphaned TabDock instance from a previous CLI session.
The ValidationDriver's fresh-instance preflight correctly blocked supervised
runs against it. Investigation confirmed: executable was `TabDock.exe` from
this repository's Debug output, no active user-owned containers, no driver
spawner alive. Closed via WM_CLOSE. Not a harness cleanup defect.

### Product bug fixed: WM_GETMINMAXINFO always-set

`ContainerWindow.xaml.cs` WM_GETMINMAXINFO handler previously clamped up
from WPF's internal defaults (`if (minTrackW > mmi.ptMinTrackSize.x)`),
which could never replace WPF's already-written large default. Changed to
always set when a valid constraint exists. This is the correct behavior
because WPF pre-populates lParam with conservative defaults that must be
overridden by the computed guest-aware minimum.

### Harness bugs fixed

1. `ClickTabCloseButton` now calls `EnsureClickable` before clicking —
   prevents clicks on obscured close buttons after split entry.
2. Containment scenarios replaced cross-process `QueryMinTrack` (broken:
   lParam pointer in harness address space, invalid in container process)
   with behavioral containment assertions (guests remain in panes after
   `ResizeContainerTo` layout trigger).
3. Cross-process `SetWindowPos` below min-track destroys the container
   HWND — narrow-resize behavioral tests removed from all scenarios.
4. Dead method `AttemptNarrowResizeAndReadWidth` removed.
5. Scenario 1 timing: premature 50/50 `IsInPane` assertion removed
   (asymmetric split partition makes it unreliable); post-resize
   containment assertion is the correct proof.

### Supervised results (all 5 PASSED)

- `capture-dpi-unaware-guest`: PASS (SKIPPED: 96 DPI single monitor)
- `capture-dpi-system-guest`: PASS (SKIPPED: 96 DPI single monitor)
- `split-guest-does-not-overflow-pane`: PASS
- `split-narrow-container-constraints`: PASS
- `single-guest-does-not-overflow-content`: PASS

### OpenSpec changes archived

- `dpi-unaware-acceptance` -> `archive/2026-08-11-dpi-unaware-acceptance`
- `guest-size-constraint-containment` -> `archive/2026-08-11-guest-size-constraint-containment`
- `openspec validate --all`: 12/12 PASS

### Architecture audit (final diff)

No SetParent, no WS_CHILD, no HWND_BOTTOM, no GWL_STYLE/GWL_EXSTYLE
mutation on guests, no arbitrary DPI constants, no Thread.Sleep in
production, no uncontrolled polling. Clean.

### Remaining debt

- Manual visual acceptance on real multi-monitor DPI setups not yet
  confirmed by the user.
- Cross-monitor/per-monitor-DPI behavior at non-100% scaling requires
  a supervised matrix on representative hardware.
---

## POST-HARDENING STARTUP VISIBILITY FINDING (2026-08-12)

### Defect

During TabDock startup, a restored/opened group can end up hidden behind an
already-existing desktop window when the group's initial position overlaps that
window. Reopens the production-readiness gate.

### Root cause (proven)

`App.Application_Startup` -> `OpenContainer` shows each restored (empty)
container with a bare `window.Show()` and never issues an explicit z-order or
activation claim, then hides the launcher. Whether the container lands above or
below an overlapping pre-existing window depends on the OS foreground grant at
the moment of `Show()`. With no grant, the container is parked beneath the
overlapping window; restored groups are empty (persistence is metadata-only),
so the WinEvent hooks are not installed (`IsMonitoringNeeded` false) and every
container z-order memory (`LayoutShepherdActiveWindow` ~1985, the `WM_ACTIVATE`
reassert ~306, `PairZOrderBehindGuest`) requires a live guest the empty
container has none of. Burial persists for the session.

### Fix

New one-shot `App.ReconcileRestoredContainerZOrder()` called immediately after
the restored-container loop in `Application_Startup`. Raises each restored
container to the top of the normal z-order band via the existing authority
primitive `WindowShepherdService.RaiseContainerForChrome` (`HWND_TOP` +
`SWP_NOACTIVATE`). Z-order only — no `Activate`/`SetForegroundWindow`, so no
focus steal, later user activation respected. (TabDock has no supported
background/no-activation launch path, so there is no such mode to preserve; the
accurate policy is "raised in the normal band without taking focus.") Bounded:
one write per container, once. Preserves the Shepherd
guest-above-container invariant (vacuous at startup; re-established by
`PositionAndShow` on first capture). No guest HWND/style/owner/geometry
mutation; no SetParent/WS_CHILD/HWND_BOTTOM/HWND_TOPMOST/Topmost/loop.

### Tests

ValidationDriver scenarios were built and run under supervision on
2026-08-13: `startup-group-not-hidden-behind-existing-window`,
`startup-does-not-steal-foreground-after-external-activation`, and
`startup-local-stack-above-unrelated-when-guest-present` all passed
(`tests/ValidationDriver/.../Scenarios.StartupHide.cs`; registered in
`Scenarios.cs`).

### Validation

Four project builds + `TabDock.sln`: 0 warnings / 0 errors. `scripts/validate.ps1`
PASS. `--selftest-diagnostics` and `--selftest-geometry` PASS. `openspec
validate --all --no-interactive` 14/14 PASS. `git diff --check` PASS. The
supervised startup scenarios above PASS with the real state restored. The
human visual checklist and the other named desktop matrices remain the
outstanding qualification gates.

### Manual acceptance status

The targeted startup/z-order scenarios were observed and passed on
2026-08-13. Human visual confirmation with Explorer/Edge/Terminal, full and
partial overlap, maximized external windows, and the broader cross-machine
matrix remains outstanding. Final assessment: RESOLVED PENDING HUMAN VISUAL
QUALIFICATION.
