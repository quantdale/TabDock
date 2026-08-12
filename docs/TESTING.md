# TabDock Testing & Validation Playbook

This document consolidates how to validate changes to TabDock without rediscovering the harness, checklist, and repro techniques from scratch every session. It is a companion to the user-facing `README.md` manual checklist — it does not replace it.

## CLI-safe diagnostics

The version, doctor, and support-bundle commands are safe to run from a normal
non-elevated terminal and do not start the main UI or WinEvent hooks:

```powershell
dotnet build TabDock.csproj -c Release -r win-x64
& .\bin\Release\net8.0-windows\win-x64\TabDock.exe --version
& .\bin\Release\net8.0-windows\win-x64\TabDock.exe --doctor
& .\bin\Release\net8.0-windows\win-x64\TabDock.exe --selftest-diagnostics
```

`--doctor` must exit 0 when state is absent, malformed, or an optional native
probe is unavailable, and it must not create or modify `%APPDATA%\TabDock`.
Use `--doctor --output <path>` for a copyable text file and
`--support-bundle --output <path>.zip` for a portable local bundle. During a
live repro, `Ctrl+Alt+Shift+D` captures the in-process logical group/split
snapshot even if the header is hidden. Inspect the ZIP before sending it:
titles are hashes/lengths, paths are redacted, and no full personal files are
collected.

### Hosted deterministic gate versus supervised acceptance

The repository workflow runs the CLI-safe gates on `windows-latest` through
`scripts/validate.ps1`: Debug builds of the app, Spike, ValidationDriver, and
GuineaPig; `--selftest-diagnostics`; `--selftest-geometry`; doctor; an explicit
support-bundle export; an isolated no-state-mutation fingerprint; support-bundle
privacy scans; source guardrails for native contracts; and `git diff --check`.
It also runs `openspec validate --all --no-interactive`. The workflow must not
run ValidationDriver scenarios because those scenarios use real `SendInput`.

The ValidationDriver remains a supervised/self-hosted desktop gate. A green
hosted workflow proves deterministic behavior and buildability only; it is not
evidence that a human visually accepted startup z-order, split rendering,
mixed-DPI behavior, browser chrome, or crash-rescue behavior on a specific
desktop. Record those observations separately in the current agent state.

---

## A. Validation harness reference

### Location and projects

The automated real-input harness lives under:

```
tests/ValidationDriver/
├── TabDock.ValidationDriver/   # Console driver that orchestrates scenarios
└── TabDock.GuineaPig/          # Disposable WinForms target window
```

Both projects are **not** in `TabDock.sln`; they are built and run by project path.

### What `TabDock.GuineaPig` is for

`TabDock.GuineaPig` is a tiny WinForms app whose only job is to be captured, released, tab-switched, and dragged by the driver while logging the window messages it receives. It accepts command-line switches such as `--title`, `--color`, `--pulse`, `--rename-every-ms`, `--rename-after-ms`, `--hide-on-close`, `--minimize-then-hide-on-close`, `--self-close-after`, `--click-counter-button`, and `--text-box`, so scenarios can test specific behaviors (including mutable-title capture, hide-to-tray, self-close, and keyboard input into a text box) against a deterministic guest.

### What `TabDock.ValidationDriver` does

`TabDock.ValidationDriver` is a console harness that:

1. Builds/expects `TabDock.exe` to already exist at `bin\Debug\net8.0-windows\win-x64\TabDock.exe` and `TabDock.GuineaPig.exe` at `tests\ValidationDriver\TabDock.GuineaPig\bin\Debug\net8.0-windows\TabDock.GuineaPig.exe` — note the pig path has **no `win-x64` RID segment**: build the pig project without `-r win-x64` (`dotnet build tests\ValidationDriver\TabDock.GuineaPig\TabDock.GuineaPig.csproj`, per the driver's own hint at `Program.cs:97-102`). Both paths resolve relative to the repo root, located by walking up from the driver assembly until `TabDock.sln` is found, so the driver works from any machine checkout (`Scenarios.cs:79-83`).
2. Spawns a fresh TabDock instance plus guinea-pig windows.
3. Drives them exclusively with real `SendInput` mouse/keyboard events at UIA-read coordinates.
4. Asserts on window state, screen pixels, the TabDock log, and the pigs' window-message logs.
5. Kills every process it spawned when the scenario finishes (or fails).

Because it sends real input, the run must be supervised: do not touch the mouse or keyboard during a scenario.

### How to invoke it

```powershell
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- [options] <scenario|all>
```

Options:

- `--yes` — skip the interactive confirmation (still requires a supervised run).
- `--cycles N` — cycle count for `maximize-repro` (default 3), `repeat-cycles`
  (default 5), and `reattach-repeated-cycles` (minimum 3).
- `--guest KIND` — guest app for scenarios that need one (default `pig`). The full set of kinds is `pig`, `wt`, `chrome-nogpu`, `chrome-gpu` (`maximize-repro`), `chrome-normal`, `edge-normal`, `firefox-normal` (`browser-*` scenarios), and `codex`, `chatgptclassic` (`realapp` — attaches to your own already-running app). Dispatch is defined in `Program.cs` and `Scenarios.cs`.
- `--list` — print every dispatchable scenario and registration group, then exit
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

**Run-budget note:** `AllOrder` now has 65 scenarios; the 90-spawn hard cap and
10-minute `GuardedProc.Cts` budget mean a single `--yes all` cannot complete —
run logical batches instead (see `docs/TESTING.md` §A batch plan in the goal
waypoint, or the split/group/movement groupings in `Scenarios.cs`).

Run `all` to execute the core scenarios in order, fresh TabDock per scenario:

```powershell
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- --yes all
```

Browser, real-app, and extra standalone scenarios exist but are deliberately excluded from `all` because they require an explicit `--guest` or attach to the user's own live applications. See `Scenarios.cs` for the full list (`BrowserOnlyScenarios`, `StandaloneExtraScenarios`, `RealAppGuestKinds`).

### What passing output looks like

A successful run prints one `=== SCENARIO <name> ===` block per scenario, individual `PASS`/`FAIL` assertions, and ends with:

```
ALL <N> SCENARIO(S) PASSED.
```

returning exit code `0`. Any failure prints `ONE OR MORE SCENARIOS FAILED.` and returns exit code `5`.

Example help output (no scenario supplied):

```
Usage: TabDock.ValidationDriver.exe [--yes] [--cycles N] [--guest pig|wt|chrome-nogpu|chrome-gpu|chrome-normal|edge-normal|firefox-normal|codex|chatgptclassic] <scenario|all>

Scenarios:
  rename
  popout
  closewin
  ...
  all            runs every scenario above in order (fresh TabDock per scenario)

Options:
  --yes          skip the interactive confirmation (supervised runs)
  --cycles N     cycle count for maximize-repro (default 3) and repeat-cycles (default 5)
  --guest KIND   guest app for maximize-repro: pig (default), wt, chrome-nogpu, chrome-gpu;
                 browser-* scenarios: chrome-normal, edge-normal, firefox-normal;
                 realapp: codex, chatgptclassic
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

- **10-minute whole-run budget.** `GuardedProc.Cts` is a `CancellationTokenSource`
  created with a 10-minute timeout (`GuardedProc.cs:34`) that is never reset
  between scenarios; the countdown effectively starts at the single-instance
  mutex acquisition (`Program.cs:84`) and covers the confirmation plus every
  scenario. When it fires mid-scenario, the driver logs "ABORTED: overall time
  budget exceeded or Ctrl+C" (`Scenarios.cs:304-310`) and exits with code `5`
  (`Program.cs:145-149`) — **indistinguishable from a scenario failure**. A
  full `all` run (26 scenarios) must fit inside the 10 minutes. Ctrl+C cancels
  the same token.
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
- **Full `--guest` kinds.** `pig`, `wt`, `chrome-nogpu`, `chrome-gpu`
  (`maximize-repro`), `chrome-normal`, `edge-normal`, `firefox-normal`
  (`browser-*`), `codex`, `chatgptclassic` (`realapp`). Per-family validation
  lives in `Program.cs:75-81`; the dispatch switch in `Scenarios.cs:573-636`.
  The driver's own `Usage()` text still prints only the old
  `pig|wt|chrome-nogpu|chrome-gpu` subset — the switch and the validation are
  authoritative, not the usage text.
- **No `--list` option.** The CLI accepts only `--yes`, `--cycles`, and
  `--guest` (`Program.cs:33-57`). To enumerate dispatchable scenario names,
  read the registration arrays in `Scenarios.cs`: `AllOrder` (lines 153-165 —
  the `all` set, also what `Usage()` prints), `BrowserOnlyScenarios`
  (189-192), `StandaloneExtraScenarios` (207-219), plus `RealAppGuestKinds`
  (173) and `BrowserGuestKinds` (193). `realapp` and `browser-multi` are
  recognized by name outside those arrays (`Program.cs:68-71`).

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
   resets the per-scenario spawn budget, makes an atomic write-ahead disk copy
   at `state.json.driver-snapshot` before deleting the user's `state.json`,
   and `Cleanup` restores the disk copy before removing it. If a driver run
   crashes, the next scenario recovers the leftover snapshot first; the
   snapshot is never deleted before a complete restore. The method refuses to
   start while an unspawned TabDock is running, and spawns a fresh TabDock
   instance. Any process you start must go through
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

- **Mutable-title capture:** run `capture-title-change-during-capture` to use a
  guinea pig that changes its native title continuously during the picker and
  capture flow; capture must succeed because title is metadata, not identity.

Historical closure note (2026-08-10, superseded for overall release status by
the 2026-08-13 deep-audit remediation): an earlier bounded run reproduced a
normal-mode z-order defect in `directclick-foreground-pairing`. The defect was
closed by correlating the desktop-level `EVENT_OBJECT_REORDER` callback with a
callback-time foreground HWND and revalidating that HWND on the UI thread. The
final closure run passed direct-click pairing 10/10, including foreground,
adjacency, text input, bounded repair latency, process-liveness, and
no-exception checks. Production readiness for that historical milestone was
**PASS**; this does not close the current deep-audit/manual qualification gate.

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
- Verify the hidden guest is restored and the previously active guest remains wherever it was.
- Check `%APPDATA%\TabDock\state.json` and `hidden-windows.json` are valid JSON after the kill.

### 5. Cross-monitor / DPI

- Move a container between monitors with different scaling (e.g. 100% and 150%).
- Verify the content area re-lays out and the active guest fills it.

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
