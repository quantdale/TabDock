# TabDock Testing & Validation Playbook

This document consolidates how to validate changes to TabDock without rediscovering the harness, checklist, and repro techniques from scratch every session. It is a companion to the user-facing `README.md` manual checklist — it does not replace it.

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

`TabDock.GuineaPig` is a tiny WinForms app whose only job is to be captured, released, tab-switched, and dragged by the driver while logging the window messages it receives. It accepts command-line switches such as `--title`, `--color`, `--pulse`, `--hide-on-close`, `--minimize-then-hide-on-close`, `--self-close-after`, `--click-counter-button`, and `--text-box`, so scenarios can test specific behaviors (hide-to-tray, self-close, keyboard input into a text box, etc.) against a deterministic guest.

### What `TabDock.ValidationDriver` does

`TabDock.ValidationDriver` is a console harness that:

1. Builds/expects `TabDock.exe` and `TabDock.GuineaPig.exe` to already exist in `bin\Debug\net8.0-windows\win-x64\`.
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
- `--cycles N` — cycle count for `maximize-repro` (default 3) and `repeat-cycles` (default 5).
- `--guest KIND` — guest app for `maximize-repro` (`pig`, `wt`, `chrome-nogpu`, `chrome-gpu`).

Core scenarios (from `Program.cs` / `Scenarios.cs`):

```
rename, popout, closewin, closewin-hide, selfclose, selfhide, selfminhide,
tabswitch-hidesafety, minrestore, maximize-repro, repeat-cycles, crossfeature,
hotkey-afterclose, persist-kill, dragreorder, chrometabdrag, closegroupprompt,
exitpopulated
```

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
Usage: TabDock.ValidationDriver.exe [--yes] [--cycles N] [--guest pig|wt|chrome-nogpu|chrome-gpu] <scenario|all>

Scenarios:
  rename
  popout
  closewin
  ...
  all            runs every scenario above in order (fresh TabDock per scenario)

Options:
  --yes          skip the interactive confirmation (supervised runs)
  --cycles N     cycle count for maximize-repro (default 3) and repeat-cycles (default 5)
  --guest KIND   guest app for maximize-repro: pig (default), wt, chrome-nogpu, chrome-gpu
```

> **Note:** The harness code references log substrings such as `LAYOUT[drift]` and `LAYOUT[movesize]` for assertions. As of the last source audit, these patterns exist in `TabDockLog.cs` but were **not** found in the main TabDock application source. Treat any scenario that asserts on them as potentially stale until you confirm the corresponding instrumentation is committed.

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
- **Pop out:** right-click a tab and choose **Pop out**; verify the guest returns to standalone at its original size/position/style.
- **Drag out:** drag a tab out of the strip or title-bar area; verify the same.
- **Close from guest UI:** close a captured app from its own chrome; verify the tab disappears and the container closes if it was the last tab.
- **Group identity:** rename a group and change its accent color; verify the UI updates.

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

## D. Known stale-doc risk

TabDock has had documentation and tests reference instrumentation that was not actually present in committed code (the `LAYOUT[...]` log assertions are a confirmed example). Before relying on any doc claim in a test or repro — especially a claim of the form "the app logs X on every Y" — verify it against actual committed source in under two minutes. If you cannot confirm it, treat it as unconfirmed and either verify it or update the doc before depending on it.
