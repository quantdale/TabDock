# Agent state

## Current checkpoint — final durable state before handoff (2026-08-13 20:26 +08:00)

Committed-tree verification passed against `f1dc7ab3ac616d6f1517efafdced5eb6418d3462`:
Release solution build 0/0; diagnostics `84 checks / 0 failures`; OpenSpec
`15 passed / 0 failed`; canonical
`.\scripts\validate.ps1 -Configuration Release -Ci -Publish` passed audited
restore with no vulnerable packages, all builds, doctor, support-bundle
privacy, OpenSpec, and self-contained publish/version smoke. Version output
contains the committed SHA. No test processes or repository-local generated
artifacts remain. The tree is temporarily dirty only for this final durable
record; `origin/main` remains the original baseline and no push occurred.

Final result: **READY WITH EXTERNAL QUALIFICATION REMAINING**. H1, M1, M3,
M4, M5, M7, M9, L1, L2, L3, L5, R1, and R2 are RESOLVED; L4 is DISPROVED /
NOT MATERIAL. M2 is BLOCKED_ENVIRONMENT because no destructive OS shutdown or
logoff was initiated; M6 is BLOCKED_EXTERNAL because hosted GitHub CI was not
run and pushing is prohibited; M8 is BLOCKED_ENVIRONMENT because this desktop
has one 1920x1080 primary monitor at 100% DPI. Chrome-dependent cases remain
unrun because `chrome.exe` is unavailable. Split-render has 12/13 available
scenarios passing; `split-third-tab-click-persists` safely fails closed when
Windows will not prove the exact container foreground. H1 itself is resolved
by the passing split-focus and dedicated focus/drag scenarios.

Next three actions: commit this durable final record locally; verify clean
`main`, final SHA/log, no processes/artifacts, and `origin/main` unchanged;
handoff the exact M2/M8/M6/split-render/Chrome external procedures. A fresh
session must not redo the audit, completed H1 evidence, or committed-tree
canonical gate.

## Current checkpoint — post-remediation commit verification (2026-08-13 20:21 +08:00)

The verified remediation candidate was committed locally on `main` as
`f1dc7ab` (`fix: complete TabDock deep audit remediation`), with starting HEAD
`d0cea29fd1b8b60008eb3d7021b3c6859951583a`. `origin/main` remains at the
starting HEAD; no push or remote operation was performed. The tree is
temporarily dirty only because this post-commit checkpoint must record the
commit and the final verification that follows.

Pre-commit validation was green: Release builds, diagnostics `84/0`, geometry,
driver help/list, audited canonical validation, actual ZIP privacy, OpenSpec
`15/15`, publish smoke, state-file preservation, no test processes, and no
repository-local generated artifacts. H1 is RESOLVED. M2 and M8 remain
environment-blocked; M6 remains externally blocked; split-render has one safe
foreground-activation limitation and Chrome-dependent cases were unavailable.

Next three actions: run canonical Release build/validation/OpenSpec against
`f1dc7ab`; record the post-commit result and final status in this file and the
execplan; commit the durable final-state record and verify a clean `main`.

## Current checkpoint — pre-commit acceptance gate (2026-08-13 20:18 +08:00)

Branch `main`; starting HEAD and `origin/main` remain
`d0cea29fd1b8b60008eb3d7021b3c6859951583a`; the tree is dirty with the
reconciled remediation, OpenSpec change, self-test seams, and durable state.
No commit has yet been created and no destructive Git operation has been used.

Pre-commit gate: `dotnet build TabDock.sln -c Release --nologo`, driver and
GuineaPig Release builds, `--help`, `--list`, geometry, and diagnostics all
passed; diagnostics reported `84 checks / 0 failures`. The canonical
`.\scripts\validate.ps1 -Configuration Release -Ci -Publish` passed audited
restore, no vulnerable packages, all Release builds, doctor, actual support
bundle privacy, OpenSpec `15 passed / 0 failed`, and self-contained publish
version smoke. A separately generated support ZIP had 9 entries and zero
personal-path hits. OpenSpec status reports the change complete. No test
processes or repository-local generated artifacts remain. `state.json` and
`state.json.bak` hashes/lengths remain respectively
`ACB57C6A5FB9001C1EAC82738E9DB6D82A8EC6D7DE4849C1798DB08F8D078F45`/37 and
`15DD48589157598CF791158D8045897F7DFB7CA2E43E3605EC75DED498117140`/13708.

Open external qualification remains M2 supervised shutdown/logoff
cancellation, M6 hosted GitHub CI, M8 multi-monitor/mixed-DPI hardware,
missing Chrome-dependent scenarios, and the split-render foreground
activation limitation. The privacy key-normalization hardening is included in
the candidate. Next three actions: stage and commit the reconciled candidate;
rerun the canonical committed-tree verification and inspect final status/log;
record commit SHA/results and hand off with exact external procedures. A
fresh session must not redo completed scenario evidence or the full audit.

## Current checkpoint — privacy hardening before final gate (2026-08-13 20:12 +08:00)

Current branch `main`; HEAD and `origin/main` remain
`d0cea29fd1b8b60008eb3d7021b3c6859951583a`; the working tree is dirty with
48 short-status entries and no commit has been created. No destructive Git
operation has been used.

The final source review found and fixed one narrow diagnostic privacy gap:
JSON sensitive-property matching now normalizes punctuation, so `api_key` and
`api-key` values are redacted in addition to text-form credential patterns.
The adversarial privacy self-test now covers that shape. The existing support
bundle sanitizer remains parseable for JSON and trace JSONL.

Last validation command: `git diff --check`; result PASS (exit 0, with only
expected LF/CRLF conversion warnings). The privacy change has not yet been
rebuilt or rerun through self-tests.

Open qualification items remain M2 supervised shutdown/logoff cancellation,
M6 hosted GitHub CI, M8 multi-monitor/mixed-DPI hardware, missing Chrome
coverage, and the split-render foreground-activation limitation. Next three
actions: rebuild and run the privacy/self-test plus canonical Release gate;
complete final diff/CI and process hygiene review; stage, commit locally, and
rerun verification against the committed tree. A fresh session must not redo
the completed H1, split-focus/core, direct-click, drag, lifecycle, M9, or
privacy investigation unless new validation exposes a regression.

## Current checkpoint — final bounded-shard qualification (2026-08-13 20:04 +08:00)

Current branch `main`; HEAD and `origin/main` remain
`d0cea29fd1b8b60008eb3d7021b3c6859951583a`; the tree is dirty with 61 Git
status lines (41 tracked remediation files plus the expected untracked
execplan, OpenSpec change, and helper files). No commit has been created and
no destructive Git operation has been used.

Since the previous checkpoint:

- Fresh Release `hung-guest-mintrack` passed all checks, including the
  deliberately non-pumping `WM_GETMINMAXINFO` evidence and bounded diagnostic;
  the earlier M9 miss was not reproducible and required no product change.
- Release `drag-z-order` passed every available pig/Notepad scenario. The only
  failure was `chrometabdrag`, because `chrome.exe` is not installed here;
  cleanup and both state-file restorations passed.
- Release `split-render` passed 12/13 scenarios. The remaining
  `split-third-tab-click-persists` case is an intermittent desktop activation
  limitation. Its harness path was strengthened with an exact-container,
  identity-checked atomic Ctrl+Tab batch; focused reruns now either pass the
  cycles or fail closed when Windows will not prove the container foreground.
  No unsafe input is sent. This does not change H1: H1 focus/drag evidence is
  resolved by `split-focus` and its dedicated scenarios.
- No tracked TabDock/ValidationDriver/GuineaPig processes remain; `git diff
  --check` exits 0.

Open qualification items are M2 supervised Windows shutdown/logoff
cancellation, M6 hosted GitHub CI, M8 multi-monitor/mixed-DPI hardware, the
missing Chrome-dependent scenarios, and the split-render activation limitation.
Next three actions: complete the remaining source/diff and workflow review;
run the canonical Release CI-equivalent, privacy, persistence, OpenSpec, and
process hygiene gate; then commit locally and run committed-tree verification.
A fresh session must not redo completed H1, split-focus/core, M9, direct-click,
drag, diagnostics, or lifecycle qualification unless later source review finds
a regression.

## Current checkpoint — FINAL QUALIFICATION / INTEGRATION PASS (2026-08-13)

### Qualification checkpoint — drag/z-order defect and harness fixes (2026-08-13 19:21 +08:00)

Current branch `main`; HEAD remains
`d0cea29fd1b8b60008eb3d7021b3c6859951583a`; the tree is dirty with 41 changed
paths shown by Git (including the prior untracked execplan/OpenSpec and helper
files), and no commit has been created. Since the prior checkpoint, the
ordinary-window z-order predicate was corrected to require direct visible
guest/container pairing while preserving topmost cross-band handling. The
ValidationDriver gained point-validated post-popout click fallbacks, current
inline-picker assertions, and an exact registered-window transition during a
held real drag.

Validation since the previous checkpoint:

- `directclick-foreground-pairing`: PASS; the seeded external z-order gap was
  repaired in 221 ms, direct click foregrounded the guest, text input arrived,
  and no exception occurred.
- `dragreorder`: PASS; reorder, zero flip-back pairs, bounded reorder count,
  drag-out release, liveness, and no-orphan assertions passed.
- `dragreorder-then-immediate-popout`: PASS; immediate pop-out, both remaining
  tab switches, inline `+` capture surface open/dismiss, liveness, and cleanup
  passed.
- `dragprobe`: PASS; topmost-pinned reorder and drag-out passed after the
  held-button guest transition was identity-validated.
- Release TabDock and ValidationDriver builds: PASS, 0 warnings/errors.

The remaining external/environment items are: intermittent
`split-third-tab-click-persists` foreground activation, Chrome executable
coverage where `chrome.exe` is unavailable, M2 supervised shutdown/logoff
cancellation, M6 hosted GitHub CI, and M8 multi-monitor/mixed-DPI hardware.
No identity guard was removed or weakened. Next three actions: rerun the
affected bounded shards and remaining shard matrix, run canonical validation
and final privacy/persistence/process checks, then complete diff review and
the local commit/post-commit gate. A fresh session must not redo the completed
H1, split-focus/core, direct-click, drag, diagnostics, selfminhide, or
hotkey-afterclose qualification unless source changes invalidate it.

### Qualification checkpoint — lifecycle and post-close harness fixes (2026-08-13 18:43 +08:00)

Current branch `main`; HEAD remains
`d0cea29fd1b8b60008eb3d7021b3c6859951583a`; the tree is dirty and no commit
has been created. Since the prior checkpoint, the remediation added a bounded
minimize-to-hide recovery path: iconic guests are not re-shown by concurrent
relayout, and the lifecycle service probes the settled hidden state before
classifying it through the existing active-member, container-state, and
identity gates. The WinEvent hide path also retains visibility observed at the
native callback. The ValidationDriver now refreshes the live container target
between repeated post-close hotkeys, and recognizes the documented hidden
launcher state while preserving point/foreground checks.

Validation since the previous checkpoint:

- `selfminhide`: PASS, including guest hide detection, tab removal, hidden
  release, and no restore loop.
- `hotkey-afterclose`: PASS, three global-hotkey cycles plus inline picker
  activation with the populated launcher hidden; the safe caption-click
  fallback handled Windows foreground-lock refusal.
- Release solution and ValidationDriver builds: PASS, 0 warnings/errors.

The open qualification items remain the intermittent `split-render`
foreground activation limitation, M2 supervised shutdown/logoff cancellation,
M6 hosted GitHub execution, and M8 multi-monitor/mixed-DPI hardware. Next
actions: (1) finish the remaining bounded shards and final deterministic matrix,
(2) rerun canonical validation and inspect the complete diff, and (3) commit
the verified remediation and perform post-commit validation. A fresh session
must not redo the completed H1, split-core/focus, diagnostics, selfminhide, or
hotkey-afterclose qualification unless source changes invalidate it.

### Qualification checkpoint — split sub-shards and H1 (2026-08-13 18:08 +08:00)

Current branch `main`; HEAD remains
`d0cea29fd1b8b60008eb3d7021b3c6859951583a`; the tree is dirty with 44 paths
(37 tracked modifications and 7 untracked), and no commit has been created.
The strict process-start identity refresh check was also added to the
ValidationDriver cleanup/native-operation path after review of the H1 fix.

Validation since the previous checkpoint:

- `--shard split-core`: PASS, all 10 registered scenarios.
- `--shard split-render`: 12 scenarios passed; one run failed only in
  `split-third-tab-click-persists` when Windows refused a safe container
  foreground activation before Ctrl+Tab at cycle 8. The same scenario had
  passed in an earlier isolated retry, but a later isolated retry reproduced
  the foreground refusal. This is an OS desktop activation limitation, not a
  product assertion or an identity-guard bypass; the driver correctly refused
  keyboard input.
- `--shard split-focus`: PASS, all 7 registered scenarios, including
  `split-focus-bidirectional`, `split-drag-release-render-stability`,
  `split-partner-permutation`, and maximize/restore. H1 focus/drag evidence is
  now complete and safe; H1 is RESOLVED.
- No TabDock, ValidationDriver, or GuineaPig processes remained after these
  runs.

Current open items: the intermittent split-render foreground activation needs
to remain honestly recorded as a supervised desktop limitation; M2 OS
shutdown/logoff cancellation, M6 hosted GitHub execution, and M8
multi-monitor/mixed-DPI hardware remain externally blocked. The next three
actions are: (1) finish M2/M8 source and environment qualification, (2) run
the full deterministic/CI-equivalent and privacy/recovery acceptance matrix,
and (3) complete the full diff review and local commit gate. A fresh session
must not redo the completed H1 root-cause investigation or split-core/focus
runs unless source changes invalidate them.

### M2/M8 qualification analysis (2026-08-13 18:12 +08:00)

M2 source review confirms the one-way lifecycle policy: the first
`SessionEnding` callback sets the idempotence gate, marks application shutdown,
saves and flushes the journal, releases guests, stops WinEvent dispatch/retry,
normalizes container/group layout intent, saves again, and calls `Shutdown(0)`.
`Application_Exit` and repeated stop/release paths are guarded/idempotent, and
the deterministic policy self-test is part of `--selftest-diagnostics`. No OS
logoff/shutdown was initiated because it could terminate the agent session and
the user desktop. M2 therefore remains `BLOCKED_ENVIRONMENT`; the supervised
checklist is now in `docs/TESTING.md`.

M8 safe environment inspection via the current doctor report found exactly one
usable monitor: primary bounds `0,0,1920x1080`, work area `0,0,1920x1032`,
effective DPI `96x96` / `100%`. No secondary, negative-coordinate, or
mixed-DPI hardware qualification was possible. Source review confirms the
primary-monitor WPF `MaxWidth`/`MaxHeight` clamp is absent and
`WM_GETMINMAXINFO` uses the containing monitor's work area. M8 remains
`BLOCKED_ENVIRONMENT`; deterministic geometry validation is still required in
the acceptance run. The temporary doctor report was written outside the
repository and is not a repository artifact.

Last validation command: current Release doctor monitor probe; result PASS
for environment discovery. Next three actions: run the complete deterministic
self-test/privacy/recovery matrix, finish subsystem diff and CI review, then
perform the pre-commit gate and local commit.

### Additional harness qualification fixes (2026-08-13 18:24 +08:00)

The first complete `diagnostics` shard run found three harness-only defects:

- `hotkey-hold-single-picker` opened the legitimate picker on the first
  hotkey, then kept sending subsequent simulated repeat keys while the old
  launcher was still the expected target; the identity gate correctly refused
  that unsafe mismatch. The scenario now re-targets the currently visible,
  registered TabDock picker for later repeat taps.
- `picker-owner-is-requesting-container` and
  `picker-owner-falls-back-when-container-closed` expected a legacy top-level
  `Capture windows` popup from the container `+` button. Current product
  behavior intentionally uses an inline capture surface, so both scenarios now
  verify the `Add selected` surface belongs to the requesting container and is
  absent from the other container, including after launcher closure.
- The driver stable-refresh path now checks process-start identity during
  cleanup/native operations, and the temporary mismatch diagnostic was scoped
  to the safety refusal log.

Focused reruns: hotkey hold PASS; both picker-owner scenarios PASS. The initial
diagnostics shard was FAIL only because of those stale harness assumptions; it
must be rerun after the fixes. No product safety guard was weakened.

Last validation command: focused harness reruns; result PASS. Current open
qualification item is the pending diagnostics-shard rerun plus the known
split-render foreground-activation limitation. Next three actions: rerun
diagnostics and the remaining bounded matrix, run final OpenSpec/diff hygiene,
then commit and post-verify.

Objective: qualify, consolidate, locally commit, and post-verify the existing
TabDock deep-audit remediation without pushing or destroying working-tree work.

Status: **IN PROGRESS — RECONCILED; FINAL REVIEW AND QUALIFICATION RUNNING**

Checkpoint timestamp: 2026-08-13 17:17:24 +08:00. Branch `main`; HEAD and
`origin/main` are both
`d0cea29fd1b8b60008eb3d7021b3c6859951583a`. The tree is dirty with 42
changed/untracked paths (35 tracked modifications and 7 untracked), matching
the expected prior remediation campaign. No reset, stash, restore, clean,
branch switch, or other destructive operation was used. No commit has been
created in this session.

Recovery/reconciliation: the required instructions, current state, campaign
execplan, OpenSpec proposal/design/specs/tasks, testing/architecture docs, CI,
scripts, and Git state were read. OpenSpec change
`deep-audit-remediation-2026-08-13` is the active source of truth and reports
18/20 tasks complete; tasks 1.4 and 5.3 are the final qualification/review
items. The live baseline and dirty diff agree with the previous campaign
record; source/Git remain authoritative if later evidence disagrees.

Completed this pass: final-qualification checkpoint recorded in the execplan;
`git diff --check` returned exit 0 with only expected line-ending warnings.

Last validation result before new work: `scripts\validate.ps1 -Configuration
Release -Ci -Publish` PASS, diagnostics `84/0`, OpenSpec `15/15`, and focused
Release `split-resize` PASS after the initialized min-track probe buffer fix.

Open findings/blockers: H1 focus/drag input is not yet qualified because the
ValidationDriver rejects its live target before input; M2 OS cancellation and
M8 multi-monitor/mixed-DPI require safe supervised capabilities; M6 hosted CI
cannot run without a pushed event, which is prohibited. No implementation
defect is currently known, but H1's guard rejection must be investigated.

Next three actions: review the full diff and reproduce H1; complete safe M2/M8
analysis and hermetic revalidation; pass the acceptance gate, create coherent
local commit(s), and post-verify the committed tree.

Active plan: `.agent/execplans/tabdock-deep-audit-remediation-2026-08-13.md`.

H1 investigation result: the focused Release run launched TabDock PID `25512`
(main `0x281094`), GuineaPig PIDs `22860`/`3336` (HWNDs `0x1D10C6`/`0x1D10D8`),
and container `0x3210EA`/host `0x241106`. Split entry geometry and visibility
passed. The first pane click at `(368,178)` was refused because the driver
required its previous `_activeTarget`—a now-destroyed transient context-menu
popup—to remain valid. This is a harness transition bug: the current point
root was not independently logged as untrusted before the stale-anchor check.
The planned fix will preserve current point root and registered process/class/
executable validation, allowing only a verified transition after a popup dies.

H1 fix/requalification: ValidationDriver Release builds cleanly after adding
optional process-start identity to `WindowIdentity`, per-process executable/
start scope, and safe current-root transitions when a transient popup closes.
The rerun `--configuration Release --yes split-focus-bidirectional` passed all
four alternating right/left cycles, exact pane/visibility/foreground/split
assertions, composite-tab count, no-exception check, and state/guest cleanup.
H1 is no longer blocked by the focus scenario; drag-release and adjacent split
coverage remain to run. The guard was not removed or weakened.

New finding: the full `--shard split` run is oversized. It registered 29
scenarios and hit the existing 10-minute driver budget after 27 scenarios;
the final maximize/restore scenario passed, while `split-partner-permutation`
was aborted by the budget. This is a ValidationDriver orchestration defect,
not a product failure. No timeout was increased and cleanup left no known
TabDock/GuineaPig processes. The next fix is to add bounded split sub-shards,
update registration/docs/OpenSpec, and rerun them independently.

## Current checkpoint — TABDOCK DEEP AUDIT REMEDIATION (2026-08-13)

Objective: remediate and production-qualify the H1/M1-M9/L1-L5 findings from
the latest deep audit. The campaign source of truth is
`.agent/execplans/tabdock-deep-audit-remediation-2026-08-13.md`.

Status: **READY WITH EXTERNAL QUALIFICATION REMAINING — FINAL HANDOFF**

Actual repository state at campaign start: clean `main` at
`d0cea29fd1b8b60008eb3d7021b3c6859951583a`, equal to `origin/main`; Windows 11
build `26200`; .NET SDK `8.0.424`; non-elevated. Existing historical
diagnostics and startup-group records below are preserved and are not treated
as evidence that this new matrix is resolved.

Completed: instructions/state/docs/audit/CI/OpenSpec reconnaissance; durable
campaign plan created; initial finding matrix recorded.

H1 is confirmed from current source and the official Win32 contract: the
`DeferWindowPos` P/Invoke returned `bool` instead of updated `HDWP`, reused the
original handle, and called `EndDeferWindowPos` after a failed middle call. The
fix and deterministic changed-handle/failure seam are implemented. Split
creation, move, resize, minimize/restore, five-cycle torture, and split crash
rescue validation passed. Focus and drag-release supervised runs are blocked
by ValidationDriver live-target identity rejection before the action; they
remain external blockers, not claimed green.

Last successful checks: `scripts\validate.ps1 -Configuration Release -Ci -Publish`
(audited restore, Release builds, geometry/diagnostics/persistence/privacy,
doctor/version/support ZIP, NuGet report, self-contained publish, and OpenSpec
15/15), plus the focused Release `split-resize` rerun after the min-track probe
buffer fix. Diagnostics reported `84/0`; no unexpected build warnings/errors
were emitted.

M8 has its primary-monitor WPF max clamp removed and a deterministic
containing-monitor contract test; real multi-monitor hardware remains pending.
M9 has a 100 ms bounded probe, per-guest last-known cache, and a passing
deliberate non-pumping GuineaPig scenario. Recovery/persistence now has state
schema v2, version-aware backup recovery, `File.GetAttributes` path
classification, write-through journal/state commits, full capture-session
entries, durable journal-before-mutation, degraded storage gates, and
deterministic fixtures. Final ACL/ordering review passed through deterministic
injected access-denied and ordering checks; direct hardware/OS qualification is
listed below rather than implied by those seams.

L4 is disproved as not materially observable: 467 real desktop windows were
enumerated in 12 ms with five candidates, followed by a 0 ms cached refresh.
The latest user-reported regression was repeated groups with zero tabs. Source
confirmation found that fresh empty groups were saved/restored, picker refresh
could reset the destination to <New group>, and a picker all-fail path retained
a newly created shell. The ValidationDriver additionally isolated state.json
while leaving state.json.bak, allowing correct backup recovery to repopulate
validation runs with 17 stale groups. R1 is now fixed: saves/load skip
unmaterialized shells, picker cleanup/selection are corrected, and the driver
isolates/restores both state files. Debug diagnostics reports 83/0, Release
launcher-empty-state-hint and persist-kill passed with both snapshots. The
Release group-create-inline rerun remains blocked by the supervised foreground
identity guard during the second-container menu action; capture and cleanup
passed and the guard was not weakened. The latest bounded Release split batch
found a real `split-resize` failure before the probe-buffer fix: both
post-maximize left/right geometry assertions failed because the synthetic
`WM_GETMINMAXINFO` buffer was uninitialized and produced a 65,535px minimum
height. The buffer is now initialized, has a poisoned-buffer regression
self-test, and the focused Release rerun passes; the broader maximize/restore
scenario remains input-guard blocked. Preserve
Shepherd/no-reparent, identity safety,
journal-before-hide, exact split invariants, and fail-closed
persistence/privilege behavior. Do not push, merge, or mark findings resolved
without validation evidence. Current user state was only inspected; no AppData
state was changed; the stale state.json.bak observed during diagnosis was not
deleted or rewritten. Remaining external qualification is H1 focus/drag input,
M2 OS cancellation, M6 hosted workflow execution, and M8 multi-monitor/
mixed-DPI hardware.

Next 3 concrete actions for a resumed session:
1. Run the hosted Windows GitHub workflow for this uncommitted remediation once
   an explicitly authorized commit/CI event exists; do not push from this state.
2. On a controlled supervised desktop, run H1 focus/drag-release and the full
   split maximize/restore cases, then the Windows logoff/shutdown-cancel check.
3. On multi-monitor hardware, run the M8 primary/secondary and mixed-DPI matrix;
   update this checkpoint and the plan only from observed evidence.

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
