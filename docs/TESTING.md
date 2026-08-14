# TabDock Testing & Validation Playbook

This document consolidates how to validate changes to TabDock without rediscovering the harness, checklist, and repro techniques from scratch every session. It is a companion to the user-facing `README.md` manual checklist — it does not replace it.

## CLI-safe diagnostics

The version, doctor, support-bundle, and pending-recovery discovery commands are safe to run from a normal
non-elevated terminal and do not start the main UI or WinEvent hooks:

```powershell
dotnet build TabDock.csproj -c Release
& .\bin\Release\net8.0-windows\win-x64\TabDock.exe --version
& .\bin\Release\net8.0-windows\win-x64\TabDock.exe --doctor
& .\bin\Release\net8.0-windows\win-x64\TabDock.exe --pending-recovery
& .\bin\Release\net8.0-windows\win-x64\TabDock.exe --selftest-geometry
& .\bin\Release\net8.0-windows\win-x64\TabDock.exe --selftest-diagnostics
```

`--doctor` must exit 0 when state is absent, malformed, or an optional native
probe is unavailable, and it must not create or modify `%APPDATA%\TabDock`.
Use `--doctor --output <path>` for a copyable text file and
`--support-bundle --output <path>.zip` for a portable local bundle. During a
live repro, `Ctrl+Alt+Shift+D` captures the in-process logical group/split
snapshot even if the header is hidden. Inspect the ZIP before sending it:
titles are hashes/lengths, paths are redacted, and no full personal files are
collected. `--pending-recovery` is also read-only and uses stable session entry
IDs; it does not rewrite or mutate pending evidence. `--recover-pending` is a
separate intentionally mutating workflow and must be run only under supervised
operator control: it acquires the same product-mutation lease as normal
TabDock, requires selecting the entry, selecting the exact live top-level
candidate, and typing `YES`. Never use an automatic recover-all command or a
raw Win32 tool for legacy evidence.

The Release validation script also launches the actual WinExe with isolated
`APPDATA`, redirected stdin/stdout/stderr, and EOF input. That hosted/process
smoke proves the no-pending command lifecycle without touching the user's
journal. A real PowerShell/cmd parent-console attach is a separate local
Windows-console qualification; it is not implied by the redirected smoke.

---

## A. Validation harness reference

### Location and projects

The automated real-input harness lives under:

```
tests/ValidationDriver/
├── TabDock.ValidationDriver/   # Console driver that orchestrates scenarios
└── TabDock.GuineaPig/          # Disposable WinForms target window
```

Both projects are **not** in `TabDock.sln`; the validation script builds them by
project path. The CI-safe entry point is:

```powershell
.\scripts\validate.ps1 -Configuration Release -Ci -Publish
```

It performs audited restore, solution/app/Spike/driver/GuineaPig Release
builds, geometry and diagnostics/persistence/privacy self-tests, `--version`,
`--doctor`, support-bundle ZIP inspection, OpenSpec validation, and a
self-contained publish smoke. It never sends desktop input.

### What `TabDock.GuineaPig` is for

`TabDock.GuineaPig` is a tiny WinForms app whose only job is to be captured, released, tab-switched, and dragged by the driver while logging the window messages it receives. It accepts command-line switches such as `--title`, `--color`, `--pulse`, `--hide-on-close`, `--minimize-then-hide-on-close`, `--self-close-after`, `--click-counter-button`, and `--text-box`, so scenarios can test specific behaviors (hide-to-tray, self-close, keyboard input into a text box, etc.) against a deterministic guest.

### What `TabDock.ValidationDriver` does

`TabDock.ValidationDriver` is a console harness that:

1. Discovers `TabDock.exe` and `TabDock.GuineaPig.exe` from the selected
   configuration (`Debug` or `Release`) and RID (`auto`, `none`, or
   `win-x64`). Use `--tabdock <path>` and `--guineapig <path>` for explicit
   artifacts. The pig remains a framework-dependent WinForms artifact without
   a `win-x64` RID segment by default. Both paths resolve relative to the repo
   root, located by walking up from the driver assembly until `TabDock.sln` is
   found.
2. Spawns a fresh TabDock instance plus guinea-pig windows.
3. Drives them exclusively with real `SendInput` mouse/keyboard events at UIA-read coordinates.
4. Asserts on window state, screen pixels, the TabDock log, and the pigs' window-message logs.
5. Kills every process it spawned when the scenario finishes (or fails).

Because it sends real input, the run must be supervised: do not touch the mouse or keyboard during a scenario.

### How to invoke it

```powershell
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- [options] <scenario|shard|all>
```

Options:

- `--yes` — skip the interactive confirmation (still requires a supervised run).
- `--cycles N` — cycle count for `maximize-repro` (default 3), `repeat-cycles`
  (default 5), and `reattach-repeated-cycles` (minimum 3).
- `--guest KIND` — guest app for scenarios that need one (default `pig`). The full set of kinds is `pig`, `wt`, `chrome-nogpu`, `chrome-gpu` (`maximize-repro`), `chrome-normal`, `edge-normal`, `firefox-normal` (`browser-*` scenarios), and `codex`, `chatgptclassic` (`realapp` — attaches to your own already-running app). Dispatch is defined in `Program.cs` and `Scenarios.cs`.
- `--configuration Debug|Release` — select the artifact configuration (default
  `Debug`).
- `--rid auto|none|win-x64` — select RID-aware discovery (default `auto`).
- `--tabdock PATH` / `--guineapig PATH` — override artifact discovery.
- `--shard NAME` — run one named bounded shard.
- `--list` — print every dispatchable scenario and shard assignment, then exit
  without starting TabDock or sending input.

Core scenarios (from `Program.cs` / `Scenarios.cs`):

```
rename, popout, closewin, closewin-hide, selfclose, selfhide, selfminhide,
tabswitch-hidesafety, minrestore, maximize-repro, repeat-cycles, crossfeature,
hotkey-afterclose, persist-kill, dragreorder, chrometabdrag, closegroupprompt,
exitpopulated,
container-minimize-retains-tabs, hotkey-hold-single-picker, popout-inactive-keeps-active,
double-capture-refused, persist-active-tab-index, restored-group-survives-member-reclose,
selfminimize-timer-vs-teardown, launcher-empty-state-hint
```

Split-screen scenarios (pig-only, hermetic, join `all`):

```
split-single-disabled, split-two-auto, split-select-partner, split-exit,
split-resize, split-move, split-minrestore, split-reorder,
split-popout-left, split-popout-right, split-selfclose,
split-native-move-reassert, split-native-resize-reassert,
split-contextmenu-render-stability, split-closebutton-left,
split-closebutton-right, split-click-third, split-directclick,
split-repeat-cycles, contextmenu-render-stability,
chrome-click-render-stability, tab-closebutton-popout,
tab-middleclick-popout, group-create-inline
```

UI/UX-stabilization scenarios (pig-only, hermetic, join `all`; bodies in
`Scenarios.Split.cs` — the composite split tab, group menu, and Add Window
toggle coverage from the UI/UX pass):

```
group-dropdown-stability, add-window-toggle, group-rename-menu,
group-delete-populated, split-composite,
split-three-tab-partner-popout, split-focus-bidirectional,
split-partner-permutation, split-maximize-restore-no-overlap
```

Hardening-round scenarios (2026-08-11, `tabdock-ui-ux-hardening` change):

- `split-three-tab-partner-popout` — the survivor-promotion regression: focus
  the partner of a 3-tab split, pop it out, and assert the survivor takes full
  width, stays visible, and the third tab stays hidden.
- `split-focus-bidirectional` — alternating LEFT/RIGHT half clicks ×N cycles;
  every cycle asserts the member-scoped `SPLIT[focus]` (member parsed from the
  log), the real foreground window, pane geometry, and no split exit.
- `split-partner-permutation` — both construction orders (A→B and B→A) behave
  identically; the partner half is clicked first in each case because the
  initiator is already focused right after entering (the changed-guard emits no
  `SPLIT[focus]` for it).
- `split-maximize-restore-no-overlap` — all four window-state transitions
  (Normal↔Maximized, Normal→Minimized→Normal, Maximized→Minimized→Maximized)
  ×N cycles with an exact partition assertion (no overlap, no gap, both
  visible, split still active).

Split-persistence / drag-end stabilization scenarios (2026-08-11 Round 4,
`tabdock-ui-ux-hardening` change):

- `split-third-tab-hover-persists` — with A+B split and a third tab C present,
  hover C's tab ×N cycles (pointer verifiably moved onto the tab rect, then
  away); every cycle asserts pair identity, both panes glued and visible, C
  hidden, no `SPLIT[exit]`/`SPLIT[member-gone]`, no `SHEPHERD[hide]`, no
  release, no switch away from the pair, and both pane centers resolve to their
  guests (`WindowFromPoint` — a covered-but-correctly-sized pane fails).
- `split-third-tab-click-persists` — per cycle: click C, click LEFT half, click
  C, click RIGHT half, click C, right-click C + dismiss, Ctrl+Tab; the pair
  stays A+B the whole time, C never becomes the active single guest (asserted
  via the SETTLED `Switched group … to tab N` final index — the funnel
  transiently logs C's index before reverting, so the assertion is on the final
  index, never on "no switch lines"), no `SPLIT[exit]`, no release.
- `split-click-third` (rewritten for the persistence contract) — a single
  click on C leaves the pair untouched; previously it asserted the old
  exit-on-click contract.
- `drag-release-render-stability` — ONE captured guest; drag the CONTAINER
  caption through a multi-segment trajectory (right/down/left/up/diagonal/
  return via `Input.DragPolyline`, many intermediate `WM_WINDOWPOSCHANGED`
  events) ×N cycles; IMMEDIATELY after release (no tab interaction — tab
  switching itself repairs the defect and would make the test invalid) assert:
  container moved, ≥2 `SHEPHERD[position]` lines (real re-glue churn), guest
  visible, glued, TOP window at the content center, one tab, no EXCEPTION;
  every 5th cycle a 3-frame pulse liveness probe (phase-robust: 400ms gaps,
  any adjacent pair must differ).
- `split-drag-release-render-stability` — same trajectory with A+B split;
  alternate the focused member per cycle; immediately after release assert the
  exact partition (no overlap, no gap), both pane centers resolve to their
  guests, split still active, no EXCEPTION.

Notes on the composite split tab in the harness:

- During split the strip renders the pair as ONE tab item `[ A | B ]`, so the
  tab ListBox holds one fewer ListItem than the group has tabs; `TabCount`
  assertions during split must expect the composite count: a 2-tab group in
  split renders exactly ONE ListItem (`TabCount == 1`); a 3-tab group renders
  composite + C (`TabCount == 2`).
- `ClickTabCloseButton` is composite-aware: the per-half `×` buttons carry
  AutomationIds (`SplitCloseLeft`/`SplitCloseRight`, goal §33) and are resolved
  by ID first, falling back to the horizontal-nearest heuristic — so the
  correct member pops out regardless of strip stretch or title width.
- `SPLIT[focus]` assertions are member-scoped: `WaitForSplitFocus` parses the
  `guest=0x…` from the log line and requires the expected member's HWND
  (`ContainerWindow.FocusSplitMember` emits it only when the focused member
  actually changes).
- `split-reorder` reorders in NORMAL mode and then enters split: the composite
  is deliberately not draggable while split is active (documented in the goal
  waypoint), so the scenario asserts the pair-identity guarantee on the
  reorder-then-split path instead.

Pane membership is asserted from the content-host rect halves (LEFT =
`{host.left, host.top, host.left + host.Width/2, host.bottom}`, RIGHT = the
remainder) within the existing tolerance — never `GetParent`. The scenarios
assert the `SPLIT[enter]`/`[exit]`/`[member-gone]` log lines, which are emitted
by committed application source.

**Deterministic geometry check (no input, any machine):**
`TabDock.exe --selftest-geometry` runs the partition matrix + seeded fuzz
(`SplitGeometry.RunSelfTest`, seed 20260810; widths 1..4096 × heights ×
origins incl. negative, odd widths 799..1921, 100 k fuzz rects) and exits 0
only when every invariant holds; the result is logged as
`SELFTEST[geometry] … result=PASS|FAIL`. It runs before the single-instance
mutex/UI and is safe unattended — use it for cross-machine pane-math
verification and as a fast smoke after any split-geometry change.

The diagnostics self-test also poisons and verifies the synthetic
`WM_GETMINMAXINFO` probe buffer. This catches the cross-process difference
between a real USER32 message (which supplies initialized `MINMAXINFO` storage)
and TabDock's manually allocated probe buffer.

**Run-budget note:** each driver process has a bounded 12-spawn scenario cap
and 10-minute safety budget. `all` is now a guarded parent orchestrator: it
launches the named hermetic shards as separate child driver processes, so each
shard receives its own bounded budget. It does not remove caps and it does not
include browser/real-app families that require an explicit guest choice.

Named shards are:
`core-lifecycle`, `capture-group`, `split-core`, `split-render`, `split-focus`,
`drag-z-order`, `crash-recovery`, `keyboard-input`, `dpi-multi-monitor`,
`startup`, and `diagnostics`. The split family is deliberately divided into
three bounded processes because its original single shard exceeded the fixed
10-minute driver budget. `browser` and `real-app` are explicit-only shards.

Run one bounded shard during development:

```powershell
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- --configuration Release --yes --shard split-focus
```

Run the complete hermetic suite through the guarded orchestrator:

```powershell
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- --configuration Release --yes all
```

Browser and real-app families remain deliberately explicit because they require
`--guest` or attach to the user's own live applications. Standalone extras are
also listed by `--list` but are not silently folded into a bounded hermetic
shard. See `Scenarios.cs` for the full dispatch tables.

### What passing output looks like

A successful run prints one `=== SCENARIO <name> ===` block per scenario, individual `PASS`/`FAIL` assertions, and ends with:

```
ALL <N> SCENARIO(S) PASSED.
```

returning exit code `0`. Any failure prints `ONE OR MORE SCENARIOS FAILED.` and returns exit code `5`.

Example help output (no scenario supplied):

```
Usage: TabDock.ValidationDriver.exe [options] <scenario|shard|all>

Scenarios:
  rename
  popout
  closewin
  ...
  all            runs every bounded hermetic shard in separate child processes

Options:
  --yes          skip the interactive confirmation (supervised runs)
  --cycles N     cycle count for maximize-repro (default 3) and repeat-cycles (default 5)
  --guest KIND   guest app for maximize-repro: pig (default), wt, chrome-nogpu, chrome-gpu;
                 browser-* scenarios: chrome-normal, edge-normal, firefox-normal;
                 realapp: codex, chatgptclassic
  --configuration Debug|Release
  --rid auto|none|win-x64
  --tabdock PATH --guineapig PATH
  --shard NAME    run one bounded shard
  --list          list scenarios and shard assignments
```

> **Note:** The `LAYOUT[drift]`/`LAYOUT[movesize]`/`LAYOUT[capture]` and `unhealthy`
> log assertions that used to reference instrumentation absent from committed
> source were removed or retargeted in the `expand-e2e-coverage` change. Every
> remaining log-substring assertion (e.g. `SHEPHERD[position]`,
> `SHEPHERD[dragout]`, `SHEPHERD[rescue]`, `Reordered tab`, `hid itself`,
> `destroyed; removing its tab`, `Global hotkey Ctrl+Alt+G pressed`) has been
> verified against committed application source. No scenario may assert on
> instrumentation absent from committed source — see §D.

### Run constraints

Verified facts about the harness's own limits — several are easy to mistake for
scenario failures:

- **Bounded budgets.** Each driver process has a 10-minute cancellation token
  and each scenario has a 12-spawn cap. The `all` parent starts each hermetic
  shard in a separate guarded child process, so a growing scenario catalog does
  not consume one impossible global budget. A budget abort still returns code
  `5` and is not a pass.
- **No TabDock may already be running.** The run banner advertises that the
  driver spawns a fresh TabDock and aborts if one is already running
  (`Program.cs:108`); the enforcement is a per-scenario preflight in
  `StartScenario` that refuses to proceed while a TabDock process it did not
  spawn is alive (`Scenarios.cs:356-362`). A second *driver* instance is
  refused separately by a named mutex (`Program.cs:84-89`, exit code 2).
- **Exit codes.** `0` — all scenarios passed; `1` — usage error (unknown
  option/scenario, or a scenario family missing its required `--guest`);
  `2` — another driver instance holds the mutex; `3` — user declined the
  interactive confirmation; `4` — `TabDock.exe` or `TabDock.GuineaPig.exe`
  build not found (`Program.cs:91-102`); `5` — any scenario failure **or** the
  10-minute budget abort.
- **Artifact and guest selection.** `--configuration`, `--rid`, explicit
  executable paths, and `--guest` are validated before input begins. `--help`
  and `--list` are authoritative and do not start TabDock. Supported guest
  kinds include `pig`, `wt`, `chrome-nogpu`, `chrome-gpu`, browser guests,
  `codex`, and `chatgptclassic`; browser/real-app shards remain explicit-only.

### How to add a new scenario

A scenario is a `static void (Ctx ctx, Options opt)` method plus CLI
registration. To add one:

1. **Write the body** in `Scenarios.cs` as a method of that shape. Use
   `ctx.Check(bool, "description")` for every assertion — it logs `PASS`/`FAIL`
   and accumulates into `ctx.Pass` (`Scenarios.cs:70-74`); a false check or any
   thrown exception marks the scenario FAIL (runner at `Scenarios.cs:304-323`).
   For guests use `SpawnPig`/`SpawnGuest` (`Scenarios.cs:559-636`), and read
   `opt.Cycles`/`opt.Guest` from the `Options` struct (`Scenarios.cs:15-20`)
   when the scenario is parameterized. Waits should go through `Util.WaitUntil`,
   which honors the run budget (`GuardedProc.cs:211-227`). `Ctx` also carries
   `TabDock`/`TabDockPid`/`MainHwnd`, `Guests`, `Containers`, and `LogOffset`
   (`Scenarios.cs:59-75`).
2. **Register the name** in the runner switch (`Scenarios.cs:226-290`) so
   `RunScenario` can dispatch it.
3. **Add it to the right registration array(s)** (`Scenarios.cs:153-219`):
   - `AllOrder` (153-165) — pig-only, hermetic scenarios that should join `all`
     (fresh TabDock per scenario, no `--guest` needed; none of `all` reads
     `opt.Guest`).
   - `BrowserOnlyScenarios` (189-192) — needs
     `--guest {chrome-normal|edge-normal|firefox-normal}`; `Program.cs:77-81`
     validates the kind.
   - `StandaloneExtraScenarios` (207-219) — dispatchable by name but not in
     `all` (spawns its own hardcoded guest, or is slow/risky).
   - `realapp` and `browser-multi` are special-cased by name in
     `Program.cs:68-71`; `realapp` additionally requires
     `--guest {codex|chatgptclassic}` (`Program.cs:75-76`).
   The `known`-scenario check in `Program.cs:66-81` rejects any name that
   matches none of these — a scenario needing a `--guest` kind the validation
   does not accept is unreachable from the CLI (historically, mislabeling
   exactly this made `all` fail its own argument validation; see the comment
   at `Scenarios.cs:181-188`).
4. **State isolation is automatic.** `StartScenario` and `Cleanup`
   (`Scenarios.cs:407-647`)
   reset the per-scenario spawn budget, make atomic write-ahead disk copies at
   `state.json.driver-snapshot` and `state.json.bak.driver-snapshot` before
   deleting both the user's primary and backup state files, and `Cleanup`
   restores both copies before removing them. This is required because the
   product intentionally recovers a valid backup when the primary is missing;
   isolating only `state.json` would repopulate an empty scenario from stale
   validation data. If a driver run crashes, the next scenario recovers the
   leftover snapshots first; neither snapshot is deleted before its complete
   restore. The method refuses to start while an unspawned TabDock is running,
   and spawns a fresh TabDock instance. Any process you start must go through
   `GuardedProc.SpawnGuarded`/`Track` (`GuardedProc.cs:47-81`), or `Cleanup`
   will not kill it.
5. **If you assert on a log line**, first verify the substring exists in
   committed application source — §D forbids asserting on absent
   instrumentation, and scenarios that only run with a `--guest` do not join
   `all`.

The legacy PowerShell e2e scripts (`tests/e2e-capture-release.ps1`, `tests/e2e-stress-and-drag.ps1`) were removed: their Reparent-era release assertions (e.g. treating `GetParent(guest) == IntPtr.Zero` as "released") are invalid under the Shepherd never-reparent model, they hardcoded `D:\Documents\...` machine paths, and the ValidationDriver harness above supersedes them.

---

## B. Generic manual validation checklist

Use this structure for validating **any** fix, not just one bug batch. For detailed step-by-step instructions on the standard build-verification flow, see `README.md` § "Manual test checklist"; the list below is organized by risk category and is meant to be a reusable reminder, not a replacement.

### 1. Build verification

```powershell
dotnet build -p:EnableWindowsTargeting=true
```

Clean output looks like:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Any warnings should be understood before merging; the project expects zero warnings.

### 2. Core interaction regressions (real mouse/keyboard)

- **Capture:** open several unrelated apps, press `Ctrl+Alt+G`, select them, and group them.
- **Tab switching:** click each tab; verify the correct guest is shown and the others are hidden.
- **Drag reorder:** drag a tab left/right; verify order updates and no oscillation occurs.
- **Pop out:** right-click a tab and choose **Pop out**, click its `×`, or
  middle-click it; verify the guest returns to standalone at its original
  size/position/style and the external process remains alive.
- **Native guest movement:** drag a captured guest by its own title bar or edge;
  verify it is re-glued to its assigned content rect and remains a tab. Native
  title movement is not a pop-out gesture.
- **Close from guest UI:** close a captured app from its own chrome; verify the tab disappears and the container closes if it was the last tab.
- **Group identity:** rename a group and change its accent color; verify the UI updates.
- **Context/chrome stability:** repeatedly right-click and dismiss tabs, click
  tab/chrome controls, and verify GPU-rendered content never becomes black or
  remains covered by the container marker.

Historical closure note (2026-08-10): an earlier bounded run reproduced a
normal-mode z-order defect in `directclick-foreground-pairing`. The defect was
closed by correlating the desktop-level `EVENT_OBJECT_REORDER` callback with a
callback-time foreground HWND and revalidating that HWND on the UI thread. The
final closure run passed direct-click pairing 10/10, including foreground,
adjacency, text input, bounded repair latency, process-liveness, and
no-exception checks. Production readiness for this milestone is **PASS**.

The final high-risk smoke set also passed fresh supervised runs of
`contextmenu-render-stability`, `split-contextmenu-render-stability`,
`chrome-click-render-stability`, `split-directclick`,
`split-native-move-reassert`, `split-native-resize-reassert`,
`tab-closebutton-popout`, and `tab-middleclick-popout`.

### 3. Guest-type-specific checks

- **Normal Win32 apps** (Notepad, Windows Terminal, etc.) — basic capture/release behavior.
- **Electron / tray-hide apps** (apps that hide to tray on close rather than exiting) — these have broken validation twice before. Verify specifically:
  - Hiding the guest from its own UI does not leave it force-reshown on the next TabDock launch.
  - The crash-recovery journal does not resurrect a deliberately hidden tray app.
- **GPU-rendered / browser guests** — verify live rendering (not black/frozen) after capture, tab switch, and restore.
- **Elevated windows** — run TabDock as standard user and confirm a clear refusal when trying to capture an elevated target.

### 4. Crash/kill resilience

- Force-kill TabDock (`taskkill /F /IM TabDock.exe`) while a hidden tab is captured.
- Relaunch TabDock.
- Verify every captured guest is restored to its original reversible
  presentation state (normal/maximized/minimized placement, visibility, and
  transition state) when identity still matches.
- Repeat with an intentionally self-hidden/tray-style guest; it must remain
  hidden after relaunch and must not be resurrected.
- Repeat with two guests in split, rapid tab switching, and a drag in progress.
- Check `%APPDATA%\TabDock\state.json` and `hidden-windows.json` are valid JSON
  after the kill; recycled HWND/PID identities must not be touched. If an
  upgraded installation finds a legacy tokenless v1/v2 journal, it must leave
  the original bytes in `hidden-windows.json.pending*`, report
  `manual-recovery-pending` from `--doctor`, and perform no native rescue.
  Run `--pending-recovery` to inspect it read-only, then use the supervised
  `--recover-pending` command to select and explicitly confirm one live target;
  failed recovery retains the entry and resolving one entry preserves siblings.
- Create a group without capturing a tab, exit, and relaunch: the empty shell
  must not return. Also exercise a picker attempt where every selected target
  fails; the provisional picker-created group must close and must not be saved.
  A restored group with persisted tab metadata must remain available for
  repopulation.

The journal is versioned and written through before capture/presentation
mutation. If its directory is unavailable, startup warns and capture is
disabled; a memory-only log fallback does not weaken this safety gate.

#### Legacy journal recovery

The current crash journal is schema v3. Historical v1 and v2 files are
intentionally not auto-restored: they have useful PID/thread/class/executable
and (for v2) process-start evidence, but no per-capture HWND-generation token,
so a same-process HWND destroy/recycle cannot be ruled out. Startup moves the
exact original bytes to `%APPDATA%\TabDock\hidden-windows.json.pending` (or a
unique numbered sibling), emits a `SHEPHERD[journal-pending]` diagnostic, and
does not retry it on every launch. Do not delete or edit that file before
recovery is complete.

Run `TabDock.exe --pending-recovery` for read-only discovery. It reports counts,
schema versions, stable per-session entry IDs, available historical fields, and
sanitized current-window status without exposing full pending paths or window
titles. If an entry may be recoverable, run `TabDock.exe --recover-pending`
from a supervised terminal. Select one entry, select the exact live top-level
window from the local candidate list, and type `YES`; a rejection or mismatch
performs no native mutation. Recovery validates every historical field present,
refuses existing capture/recovery tokens, writes a durable prepared record
before the external recovery property, installs a new temporary generation
guard, and revalidates it immediately before each presentation mutation. v1
restores visibility only; v2 restores its recorded placement, visibility, and
DWM transition state unless `DoNotRescue=true`, in which case it never shows
or repositions the guest and only cleans the historically required transition
state. A hard kill after the durable native-complete phase performs cleanup
without repeating native presentation work. A failed or unverifiable
transaction retains the pending evidence. A successful entry gets a durable
resolution marker; the pending source bytes remain immutable while any sibling
is unresolved, and the sidecar ledger supplies logical retirement. Only an
all-resolved source is deleted as one unit, preserving sibling fingerprints,
positions, and unknown JSON fields. Startup never performs tokenless legacy
recovery.

### 5. Cross-monitor / DPI

- `GetDpiForMonitor` is intentionally not used: Microsoft documents it as
  not DPI-aware and unsuitable from a PerMonitorV2 thread. Production probes
  an arbitrary target monitor with the hidden PMv2 helper in
  `Services/MonitorDpiService.cs` and `GetDpiForWindow(helper)`; an unaware
  guest's own `GetDpiForWindow` result of 96 is therefore not used as the
  monitor scale.
- `--selftest-diagnostics` exercises the injectable DPI helper lifecycle and
  probe/conversion seams. The deterministic contract is: known DPI-unaware
  plus a valid monitor DPI is accepted; unknown/unreadable awareness or monitor
  DPI is refused; a 96-DPI logical minimum-track value of 500 becomes 750 at
  144 DPI and remains 500 at 96 DPI; aware guests do not receive unaware
  scaling. These checks do not pretend to qualify physical mixed-DPI hardware.
- Move a container between monitors with different scaling (e.g. 100% and 150%).
- Verify the content area re-lays out and the active guest fills it.
- Maximize on a larger secondary monitor (1440p/4K), including negative monitor
  coordinates, and confirm the work area—not the primary monitor—controls the
  bounds and taskbar clearance.
- Repeat normal → maximize → restore and maximize → minimize → maximize with an
  active split; both panes must remain an exact partition.
- For a deterministic no-hardware check, run `--selftest-geometry`; the real
  multi-monitor/DPI matrix remains supervised hardware validation.

### 6. Lifecycle and diagnostics hardening

- Use the diagnostics self-test to exercise persistence versions, valid backup
  recovery, unreadable-primary protection, monitor-hook failure injection,
  storage-degraded startup fixtures, native deferred-position chaining, and
  adversarial support-bundle sanitization.
- A real support ZIP must be inspected entry-by-entry for the username,
  profile/AppData paths, executable paths, and credential-like values.
- Hook-install failure after the bounded retry budget must release guests,
  disable capture, persist layout intent, and show a restart-required warning;
  it must not leave unsupported captured guests active.
- A Windows logoff/shutdown cancellation test is supervised and destructive.
  On a disposable, instrumented Release session only: (1) capture two
  GuineaPig windows and create a split, (2) initiate Windows logoff or
  shutdown, (3) cancel the request from a separately controlled application,
  (4) verify TabDock has still exited after releasing guests and normalizing
  layout intent, rather than resuming half-torn-down with hooks stopped, (5)
  verify both guests are standalone and alive, (6) relaunch TabDock, (7)
  confirm no stale recovery journal remains and persisted group metadata is
  coherent, and (8) confirm repeated exit/session-ending callbacks do not
  produce prompts or duplicate mutations. Do not run this on an unsaved
  production session or on the agent workstation without a supervised recovery
  plan.

---

## C. Repro-technique reference

The project has a standing safety rule: **do not run synthesized mouse/keyboard input (`SendInput`, UIA clicks, etc.) on the live desktop unattended** — a prior harness incident accidentally drove input into a live user window. Whenever a bug is really about logic or state transitions, prefer the programmatic/helper-based techniques below over UI automation.

### Pattern 1: Mimic a truncate-in-place write without corrupting real app state

**General technique:** build a tiny standalone helper that copies the target write pattern, run it against disposable test files in a temp directory, and kill it mid-write. Once the torn-file behavior is confirmed, point the real application at the torn file to confirm the real failure mode (e.g., parse throw on launch).

**Worked example from this session — `state.json` / `hidden-windows.json` torn write:**

1. Create a throwaway console helper that writes a large JSON payload to `test.json` using `File.WriteAllText` in a loop with an artificial delay.
2. While it is running, execute `taskkill /F /T /IM helper.exe`.
3. Inspect `test.json`: it is truncated mid-content and invalid JSON.
4. Copy the torn file over `%APPDATA%\TabDock\hidden-windows.json` (or `state.json`).
5. Launch TabDock; observe `LoadJournal` throw and crash recovery become permanently disabled.

**Fix verification:** repeat the same kill-mid-write against the fixed code (write to `.tmp`, then `File.Move(tmp, path, overwrite: true)`). The destination file is either the old content or the new content, never torn. Then hand-corrupt a journal file and confirm launch no longer throws — the corrupt file is renamed to `.corrupt.<timestamp>` and an empty journal is used instead.

### Pattern 2: Reproduce an event/state-transition bug programmatically

**General technique:** when the defect is "code path A raises event X but code path B doesn't," invoke both paths directly against the same object state and diff the observed behavior. This avoids needing SendInput/UIA for what is actually a logic bug.

**Worked example from this session — `EmptiedByPopOut` not raised on drag-out:**

1. Construct a `GroupViewModel` with exactly one tab.
2. Call the context-menu pop-out path (`OnPopOutRequested`) and record whether `EmptiedByPopOut` fires.
3. Reset to an identical single-tab state.
4. Call the drag-out path (`ReleaseTab`) and record whether `EmptiedByPopOut` fires.
5. Compare: path 2 does not raise the event, leaving an empty container open.

**Fix verification:** after adding the event raise to `ReleaseTab` when `Tabs.Count == 0`, repeat both direct invocations and confirm both now raise `EmptiedByPopOut`.

### Pattern 3: Reproduce a timing/race bug by manually driving the state machine

**General technique:** identify what a pending callback would read and do, then manually set up that exact state and invoke the callback's logic directly, rather than trying to time a real timer against a real close.

**Worked example from this session — stale timer after container close:**

1. Dock a guest and note its position/size.
2. Set the shutdown/closed flag on the container manually.
3. Destroy the host `NativeHwndHost` HWND (or simulate its destruction).
4. Directly invoke the logic a pending `WM_ACTIVATE`/restore timer would run.
5. Observe the now-standalone guest get repositioned to `(0,0)` with size `0x0` because the destroyed host returned an empty rect.

**Fix verification:** after clearing `_shepherdActiveWindow` on container close, nulling `NativeHwndHost._hwnd` on teardown, and guarding the rect read with `IsWindow()`, repeat the same direct state-machine drive. The callback now sees the invalid handle, skips the `SetWindowPos` call, and the guest's position/size is untouched.

---

## D. No assertion may reference instrumentation absent from committed source

TabDock previously had documentation and tests reference instrumentation that was not actually present in committed code (the `LAYOUT[...]` and `unhealthy` log assertions are the confirmed examples; both were removed/retargeted in `expand-e2e-coverage`). The rule is now enforced by spec (`e2e-scenario-coverage`): before a scenario asserts on a log line, event, or signal, the asserted substring SHALL be verified against committed application source; assertions that fail the check SHALL be retargeted at an observable equivalent (window geometry, `SHEPHERD[*]` lines, pixel checks) or removed — they must never pass vacuously or fail unconditionally. Before relying on any doc claim in a test or repro — especially a claim of the form "the app logs X on every Y" — verify it against actual committed source in under two minutes; if you cannot confirm it, treat it as unconfirmed and either verify it or update the doc before depending on it.
