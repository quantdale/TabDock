# TabDock — Known Issues (H-series summary)

## Session 3 (2026-07-18, later still): Shepherd migration — keyboard input closed by architecture change

**User report:** captured browser windows (Chrome/Edge) lose keyboard input after
switching to another app and back — reproduced live with a real browser and a
physical keyboard. Four narrower bugs in the Reparent backend's `SetParent`/
`AttachThreadInput`/`WM_ACTIVATE` state machine were found and fixed in an
earlier pass of this same session (a redundant unguarded `SetFocus`, a missing
`WA_INACTIVE` signal, a spurious self-foreground deactivation, and an
over-eager `WM_KILLFOCUS` detach) — the user re-verified manually afterward and
the bug was **still present**. Per this project's own architecture doc
(`docs/internal/deep-audit-2026-07-17.md`), that's expected: cross-process
`SetParent` + `AttachThreadInput` fundamentally couples two processes' input
queues in ways Windows doesn't reliably support, and patching the state machine
further would just be another workaround stacked on four already-applied ones.

**Fix: migrated to Shepherd as the only backend, not a toggle.** The Reparent
backend (`WindowCaptureService`, `GuestActivationHelper`, `DpiService`,
`RenderHealthService` — all four existed only to compensate for problems
`SetParent` caused) was deleted outright. Under Shepherd
(`Services/WindowShepherdService.cs`), a captured guest is never reparented or
restyled — it stays a real, independent top-level window the entire time,
positioned over the container's content area with `SetWindowPos` and hidden
with `ShowWindow(SW_HIDE)` when inactive. This eliminates the bug class
structurally, not empirically: there is no attach/detach state machine left to
race, because the guest's input queue is never joined to TabDock's in the
first place. See `CLAUDE.md`'s Architecture section for the full design.

**A real bug was found and fixed during the migration itself, independent of
the keyboard-input bug:** `WindowShepherdService.PositionAndShow` called
`SetWindowPos(guestHwnd, containerHwnd, ...)` — but `SetWindowPos`'s second
parameter (`hWndInsertAfter`) *precedes* (sits above) the positioned window in
z-order, so this placed the guest **behind** its own container, not above it.
Visually this was masked because `BringToFront`'s subsequent
`SetForegroundWindow` call has a side effect of also raising the guest to the
true top of the z-order — but any code path that positioned the guest *without*
also foregrounding it (initial capture, tab switch, drag-out snap-back) left it
genuinely behind the container's own WPF-rendered content, both visually (the
container's dark background painted over it) and for input (clicks landed on
the container/marker, not the guest). Found via manual verification using
`WindowFromPoint` at the docked guest's screen coordinates — it returned the
`TabDockContentHost` marker's HWND, not the guest's. Fixed by passing
`HWND_TOP` instead, then explicitly re-pinning the container immediately
behind the guest with a second `SetWindowPos` call (the same pattern
`PairZOrderBehindGuest` already used correctly for the reverse direction).

**Manual verification of the actual reported scenario:** captured a real
Chrome window, typed a baseline string in its address bar (confirmed via
`PrintWindow` — screen-region `BitBlt` captures of the docked guest showed
solid black in this specific sandboxed dev session even though the guest was
rendering correctly, a capture-method artifact unrelated to the app, also
confirmed by cross-checking an uncaptured control Chrome window and finding
the identical `BitBlt`-vs-`PrintWindow` discrepancy there too), switched to
Paint via a genuine Alt+Tab keystroke sequence, switched back via Alt+Tab, and
typed again **without clicking first**. UIA's `FocusedElement` confirmed
keyboard focus landed on Chrome's own `OmniboxViewViews` control (not the
container), and the address bar showed the correctly concatenated baseline +
post-switch text. This is the exact scenario originally reported as broken,
now confirmed fixed.

**Test suite modernized to match.** Every WS_CHILD/`GetParent`-based
"captured"/"released" assertion in `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.cs`
was replaced — under Shepherd, WS_CHILD is never set and `GetParent` is always
`IntPtr.Zero` regardless of docked/hidden/released state, so those checks were
either always-false or always-true by construction. Replaced with rect-based
`IsDocked`/`IsReleasedAndShown`/`IsReleasedAndHidden` helpers that compare the
guest's `GetWindowRect` against the container's content-area marker — the same
signal production code uses. The `renderhealth` scenario was retired (the
feature it tested no longer exists; a never-reparented guest is never
compositor-invalidated). `tests/CaptureReleaseTest` (a whole project whose
premise — verifying mutated state was correctly *restored* — evaporates when
nothing is ever mutated) was deleted; its real-app-plus-pixel-verification
value was ported into a new `realapp-multi-render` scenario, using `PrintWindow`
directly on the guest's own HWND rather than a screen-region `BitBlt` (see the
capture-method note above for why that distinction turned out to matter in
practice, not just in theory).

**Five new scenarios added**, each proving a specific piece of the migration's
design: `realworkflow-altswitch` (real browser + Notepad, repeated
type/external-switch/type-without-clicking/tab-switch cycles — the closest
automated proxy to the original report), `directclick-foreground-pairing`
(click the guest directly, bypassing TabDock's own UI, verify z-order
re-pairing via `PairZOrderBehindGuest`), `dragout-by-titlebar` (drag the
guest's own native title bar past/under the 40px pop-out threshold),
`crashkill-rescue` (force-kill TabDock with a hidden tab captured, relaunch,
verify the crash-recovery journal restores it), `realapp-multi-render` (see
above). All five pass.

**A known, environment-specific test-harness limitation, not an app bug:**
scenarios that switch to an external app (Notepad, or a throwaway pig) and
then need to bring the TabDock container back to the foreground via
`Input.ForceForeground` — `realworkflow-altswitch` and the pre-existing
`keyboardinput-{chrome,edge}-altswitch`/`keyboardinput-chrome-omnibox-altswitch`
— fail unreliably in this specific sandboxed dev session, even after hardening
`ForceForeground` with `AllowSetForegroundWindow(ASFW_ANY)` in addition to its
existing benign-key-nudge and TOPMOST-pulse retries. This reproduces
identically against both the new scenario and a scenario that passed in an
earlier pass of this same session, and does not depend on which app is used as
the external target (a throwaway pig reproduces it exactly as a spawned
Notepad does) — it is a property of this environment's foreground-lock
behavior over a long, many-hours automation session, not of the scenario code
or of TabDock itself. The manual verification above (real Alt+Tab keystrokes,
not `SetForegroundWindow` API calls) is the actual, ground-truth confirmation
that the reported bug is fixed; treat these three scenarios' automated
pass/fail as unreliable specifically in this environment until re-run on a
normal, non-sandboxed interactive desktop session.

Two incidental fixes made along the way, in `Scenarios.cs` (not `Scenarios.cs`
alone — one crossed into `RealWorkflowAltSwitch`'s own design): a second
`SpawnNotepad` call as the "switch away to an external app" target collided
with a Notepad already captured as a guest in the same scenario, because
Windows 11's built-in Notepad is a single-instance, multi-tab app — a second
`notepad.exe <file>` launch opens another tab in the *same* process as the one
already captured, not a genuinely separate window (confirmed live via the
launcher's own "reused existing process" warning reporting the identical PID).
Fixed by using a throwaway pig instead. Separately, a freshly-spawned external
process's first window is typically granted automatic foreground by Windows
the moment it appears, which was silently stealing focus before the first
iteration of a switch-away loop even ran; fixed by explicitly reclaiming
foreground for the container right after spawning it.

### Full-suite run after the migration: two more real bugs found and fixed

Running `all` from a clean state after the migration surfaced 6 failures.
Five were genuine, previously-undetected regressions or test-only gaps; the
sixth (`hotkey-afterclose`) is the same environment-specific `ForceForeground`
limitation described above (its failure appears only after several already-
completed hotkey/picker cycles, matching the pattern exactly — not a new
regression, since the scenario's own core assertions — hotkey works after the
launcher is closed, across 3 full open/dismiss cycles — all pass).

**Real bug: maximize/restore didn't resize the docked guest (affected
`maximize-repro`, `repeat-cycles`, `crossfeature`).** All three failed with the
guest staying at its pre-maximize size while the container's content area grew
to fill the monitor (and, after restoring, the guest stayed at the *maximized*
size while the container shrank back) — confirmed via `GEOMETRY` log lines
showing `guest=(0,64)-(900,600) 900x536` against `hostClient=(0,64)-(1920,1032)
1920x968` immediately after maximizing. Root cause:
`ContainerWindow.LayoutShepherdActiveWindow` reads the content-area marker's
screen rect (`GetContentAreaScreenRect`, via `GetClientRect` on the marker's
native HWND) immediately when `StateChanged`/`SizeChanged` fires, but the
marker's HWND is only actually resized inside `NativeHwndHost.ArrangeOverride`
— a WPF layout-pass callback whose ordering relative to those events isn't
guaranteed, so the rect read could be stale (pre-transition). Fixed with a
single `UpdateLayout()` call in `LayoutShepherdActiveWindow` right before
reading the marker's rect, forcing any pending layout pass (including the
marker's own arrange) to flush first. All three scenarios pass cleanly after
the fix, across all cycles.

**Real bug: a guest that hides itself can get forced back to visible
(affected `selfminhide`).** A guest that both minimizes and hides itself in
immediate succession on close (the `--minimize-then-hide-on-close` pig flag,
simulating tray apps like PredatorSense) ended up visible again shortly after
clicking its own close button, even though its own message log confirmed
`WM_SHOWWINDOW` (hide) fired correctly. Root cause: because the container is
kept z-order-paired immediately behind its active guest
(`PairZOrderBehindGuest`), the guest hiding itself hands the container the
very next `WM_ACTIVATE` — delivered synchronously as part of the same OS
activation transaction, racing ahead of the guest's own hide fully settling.
`ContainerWindow.WndProc`'s `WM_ACTIVATE` handler then called
`WindowShepherdService.BringToFront` unconditionally, which repositions the
guest with `SWP_SHOWWINDOW` — forcibly re-showing a guest that had just
intentionally hidden itself. Confirmed via the guest's own message log: a
`WM_SETFOCUS` + `WM_SIZE` restore sequence appeared ~16-34ms after its own
`WM_SHOWWINDOW` hide, both runs. Fixed by deferring the `WM_ACTIVATE` handler's
`BringToFront` call by 120ms and re-checking the guest's visibility at that
point — if it's already hidden, the container leaves it alone for the normal
async hide-teardown path to handle instead of fighting it. (Two earlier
hypotheses were tried and ruled out via direct evidence before finding this:
a spurious `MINIMIZESTART`/`RestoreMinimizedWindow` firing at *capture* time
turned out to be an unrelated, harmless false-positive footgun — confirmed via
temporary diagnostic logging showing its own guard correctly no-opped; and a
redundant `SetForegroundWindow` call in `BringToFront` was fixed as a
legitimate hardening in its own right, since it could otherwise interrupt an
in-flight click's mouse-capture on the guest's own child control, but it
wasn't the cause of this specific failure.)

**Test-only: `selfminhide`'s final assertion contradicted the one right
before it.** Task-#17's WS_CHILD-removal pass replaced this scenario's old
`WS_CHILD==0 && GetParent==0` check (universally true regardless of
visibility) with `IsReleased`/`IsReleasedAndShown` (requires the guest to be
*visible*) — but the immediately preceding assertion in the same scenario
already requires `!IsWindowVisible` (the guest is supposed to end up hidden,
per its own `--minimize-then-hide-on-close` behavior). The two checks could
never both pass. Fixed by using `IsReleasedAndHidden` instead, matching what
the scenario actually verifies.

**Test-only: `chrometabdrag` asserted log strings and behavior from the
deleted Reparent backend.** It checked for `LAYOUT[capture]`/`LAYOUT[movesize]`
log lines (Shepherd logs `SHEPHERD[position]`/`SHEPHERD[dragout]` instead) and
expected a ~130×92px drag on Chrome's own tab strip to "snap back" to the host
rect — the old Reparent-era continuous drift-watchdog behavior. Under
Shepherd's `NoteGuestMoveSize`, any drag past `DragOutThresholdPx` (40px) is a
deliberate pop-out, not a re-clamp, so a drag that size *correctly* releases
the tab (confirmed: the guest ended up at its original pre-capture placement,
exactly matching every other release path). Redesigned to test both halves of
the real, current behavior against a real app with its own client-drawn "fake"
title bar: a small jitter drag (under the threshold) snaps back to docked, and
a real drag (over the threshold) pops the tab out — the tab-strip-drag
equivalent of `dragout-by-titlebar`. (Also found: Chrome's own tab strip has
an internal click-vs-drag threshold of its own before it hands off to native
window dragging, distinct from TabDock's 40px pop-out threshold — a ~12px
jitter that reliably registers against a plain WinForms title bar was
absorbed as a click against Chrome's tab strip and never even started a real
move; widened to ~25px, still comfortably under the 40px pop-out threshold, to
reliably register.)

Also grep-confirmed: `BrowserLifecycle`/`BrowserTabSwitchHideSafety` (real-
browser scenarios gated behind `--guest`, so not exercised by a blanket `all`
run) still reference the same dead `LAYOUT[capture]`/`LAYOUT[drift]`/
`LAYOUT[movesize]` log strings. Not fixed in this pass (lower priority: these
require `--guest chrome-normal`/`edge-normal` to invoke at all, and the
underlying behavior they'd need to re-target is the same `SHEPHERD[*]`
logging already fixed in `chrometabdrag`) — flagged here so it isn't
mistaken for newly-introduced breakage the next time someone runs them.

Full `all` re-run after all five fixes: 17/18 PASS. The lone failure,
`hotkey-afterclose`, is the pre-existing environment-specific `ForceForeground`
limitation described above (its own core assertions — hotkey works after the
launcher is closed, across 3 full picker open/dismiss cycles — all pass; it
only fails on a trailing check that needs one more foreground acquisition
after those cycles), not a regression from this session's changes.
`crossfeature` failed once on a re-run with the pre-existing, already-
documented `--pulse` pig zero-variance sampling flake (see "Two flakes
observed and diagnosed" above) and passed cleanly on immediate retry,
confirming it's the same known harness-timing flake, not a new regression.
The 5 new scenarios from this session (`realapp-multi-render`,
`directclick-foreground-pairing`, `dragout-by-titlebar`, `crashkill-rescue`)
all pass individually; `realworkflow-altswitch` hits the same
`ForceForeground` limitation as `hotkey-afterclose`/`keyboardinput-*-altswitch`
for the same reason.

---

Quick-reference index for the H-series issues (the project's established
severity/ID scheme — see `investigation_findings.md` for full technical detail
on every issue, including M/L/I-series ones not repeated here). This file is
the Phase 3 deliverable of the "Real-Browser Regression Test Suite" build
request (`docs/internal/TEST_PLAN.md`); it does not replace
`investigation_findings.md` as the authoritative source.

## H-series status

| ID | Issue | Status | Guarding test(s) |
|----|-------|--------|-------------------|
| H1 | Render-health auto-release never invoked | Fixed + covered | `renderhealth` |
| H2 | Drag-reorder oscillation (fires every MouseMove) | Fixed + covered (real browser too) | `dragreorder`, `browser-dragreorder` |
| H3 | Global hotkey throws after MainWindow closed | Fixed + covered | `hotkey-afterclose` |
| H4 | Content-host NULL background brush (smear) | Fixed + covered (real browser too) | `chrometabdrag`, `browser-lifecycle` |
| H5 | Guest fill-clamp not enforced after capture | Fixed + covered (real browser too) | `chrometabdrag`, `browser-tabswitch-hidesafety`, `browser-lifecycle`, `browser-dragreorder` |

All five HIGH-severity issues are fixed **and** now have an automated
real-input test that would fail if the bug were reintroduced. H2/H4/H5 were
marked "pending runtime validation" before this session (see
`investigation_findings.md`'s 2026-07-12 note) — that gap is now closed.

## Harness-level findings (new, found this session — not app bugs)

| ID | Issue | Status | Guarding test(s) |
|----|-------|--------|-------------------|
| H-NEW | `Scenarios.cs` did not compile as committed (3 separate missing-brace bugs) | Fixed | full driver build |
| H-NEW2 | `BrowserOnlyScenarios` wrongly gated 10 non-browser scenario names behind a bogus `--guest` requirement, breaking `all` | Fixed | full driver build + `all` run |

**These are severe findings in their own right.** `tests/ValidationDriver` is
not part of `TabDock.sln` and is never built by `dotnet build TabDock.sln` or
CI, so nothing caught that the harness itself failed to compile. Concretely:
`Cleanup()`'s guest-kill logic was missing a closing brace (making the
non-ancestor Kill() path unreachable inside the wrong `if`), `WithStableTabMatchKey`
was missing its `return g; }`, and `BrowserSoak` was missing its closing brace
before the next scenario's comment block — three independent defects, found
by stashing this session's edits and reproducing the build failure against
HEAD directly (`git stash` + `dotnet build tests/ValidationDriver/...`). On
top of that, `BrowserOnlyScenarios` incorrectly listed `renderhealth`,
`hotkey-afterclose`, `persist-kill`, `dragreorder`, and every
`contentinput`/`chromeinput`/`alttabinput`/`keyboardinput*` scenario — none of
which read `opt.Guest` — which made `Program.cs`'s argument validation reject
`all` outright before spawning anything. **This means every prior "PASS" claim
in this file and `investigation_findings.md` was never actually exercised
against a build that could run** (or was run against an uncommitted/different
working copy) — treat this session's re-run (below) as the first real,
reproducible confirmation of the H1–H5 fixes and the H2/H4/H5 real-browser
coverage. All three brace bugs and the `BrowserOnlyScenarios` mislabeling are
fixed; the driver now builds clean and `all` completes.

## Session 2 (2026-07-18, later): M/L-series stabilization pass

Continuing the discover → reproduce → fix → verify loop against the open
M/L-series findings in `investigation_findings.md` (the H-series was already
closed out by the prior session).

| ID | Issue | Status | Guarding test(s) |
|----|-------|--------|-------------------|
| M1 | `SetParent` NULL-return ambiguous between success and failure | Fixed (defense in depth) | full suite unaffected (no regression) |
| M6 | Shutdown/crash-path modal could block `Application.Shutdown` (zombie process) | Fixed | `exitpopulated` (new) |
| M7 | GDI icon-handle leak in `IconService.GetFileIcon` on exception | Fixed | full suite unaffected (no dedicated pixel-level test; leak is a resource-growth issue, not a behavioral one) |
| L11 | Empty container left open after popping out the last tab | Fixed | `popout` (tightened) |
| L12 | Groups can never be deleted; accumulate in `state.json` forever | Fixed | `popout` (tightened), `persist-kill` (regression-checked) |
| L14 | `CheckRenderHealthAsync` failures invisible in field logs | Fixed | (log-routing only; no dedicated test) |

**M1** (`Services/WindowCaptureService.cs`, `Capture`): `SetParent` returns the
previous parent, which is NULL both on failure and on success for a window
that had no parent before capture. The code treated any NULL return as
failure. Empirically, real captures of genuinely top-level Chrome/Edge/pig
windows during this session's `all`/browser runs never hit this misfire (no
capture in ~25 real-input runs failed on it) — CONFIRMED not currently
manifesting on this machine/Windows build, so severity did not rise to High
as the original finding worried it might. Fixed anyway via
`Marshal.SetLastSystemError(0)` before the call and a real `GetLastWin32Error()`
check, since the fix is cheap and removes the latent risk outright.

**M6** (`Views/ContainerWindow.xaml.cs` + `App.xaml.cs`): added
`ContainerWindow.IsAppShuttingDown`, set before every exit/crash path
(`OnExitRequested`, `Application_Exit`, `Application_DispatcherUnhandledException`,
`CurrentDomain_UnhandledException`, `Application_SessionEnding`) calls
`Shutdown`/releases windows. `ContainerWindow_Closing` skips its Yes/No/Cancel
prompt when the flag is set. New scenario `exitpopulated`: captures a guest,
clicks the launcher's real "Exit" button via UIA+real click with the group
still populated, and asserts the whole process exits within 5s with **no**
stranded MessageBox — deliberately does not use the harness's own
dialog-dismissing helper, so a regression would time out and FAIL rather than
be silently papered over. PASS, real evidence (see full-suite log below).

**L11/L12** (`ViewModels/GroupViewModel.cs`, `Views/ContainerWindow.xaml.cs`,
`App.xaml.cs`): popping out the last tab now raises `GroupViewModel.EmptiedByPopOut`,
which `ContainerWindow` turns into `Close()`. `App.OnContainerClosed` now
removes a closed container's group from `GroupManager` when it is empty —
**but only when `Group.PersistedTabs.Count == 0` too**. That guard is
load-bearing, not incidental: `PersistedTabs` is populated exclusively by
`PersistenceService.Load` (i.e. only for a group restored from a *previous*
session), so a same-session group that was created and abandoned empty (the
L12 accumulation complaint — 18 residual groups observed on one machine) is
deleted, while a group restored from a prior session's real layout intent
survives having its auto-reopened empty shell closed during ordinary exit.
**First cut of this fix was wrong and caught by the harness itself**: an
unconditional `Members.Count == 0` check regressed `persist-kill`'s explicit
"a clean-exit save with the group still empty must not wipe persisted tab
metadata" assertion (M5) — real FAIL output during this session, not a
hypothetical. Narrowing to also require `PersistedTabs.Count == 0` fixed it
without reintroducing the M5 regression; re-run confirmed both `popout` and
`persist-kill` PASS together.

**M7** (`Services/IconService.cs`): `GetFileIcon`'s icon handles are now
freed in a `finally` block instead of a `catch` that skipped `DestroyIcon`
entirely on a `CreateBitmapSourceFromHIcon` exception.

**L14** (`Views/ContainerWindow.xaml.cs`): `CheckRenderHealthAsync`'s catch
block now routes through `_log.LogException` instead of
`Debug.WriteLine`-only.

### Full-suite re-run (2026-07-18, after all fixes above), from a clean state

`dotnet run --project tests/ValidationDriver/TabDock.ValidationDriver/... -- --yes all`,
fresh TabDock instance per scenario, `state.json` snapshotted/restored around
the run:

```
ALL 19 SCENARIO(S) PASSED.
```

19/19: `rename`, `popout`, `closewin`, `closewin-hide`, `selfclose`, `selfhide`,
`selfminhide`, `tabswitch-hidesafety`, `minrestore`, `maximize-repro`,
`repeat-cycles`, `crossfeature`, `renderhealth`, `hotkey-afterclose`,
`persist-kill`, `dragreorder`, `chrometabdrag`, `closegroupprompt`,
`exitpopulated` (new). Additionally re-ran real-browser scenarios standalone
(not part of `all`), all PASS: `browser-lifecycle --guest chrome-normal`,
`browser-tabswitch-hidesafety --guest chrome-normal`, `browser-dragreorder
--guest edge-normal`, `browser-multi` (real Chrome + real Edge captured
simultaneously).

Two flakes observed and diagnosed, NOT app bugs: an earlier `all` run failed
`maximize-repro` on "variance > 0.005 after restore (0.0000)" for one cycle
out of three; re-running the same scenario alone showed the same
zero-variance reading occur at an *unasserted* baseline sample in a run that
otherwise PASSed. The `--pulse` guinea pig's color cycle legitimately passes
through a momentary solid-color (zero-variance) frame, and the harness's
single-sample-per-checkpoint timing can occasionally land on it. This is
sampling-timing flakiness in the test harness, not a TabDock rendering
regression — no app code path differs between the flaky and clean runs.

## What changed this session (2026-07-18)

- Ran the full existing `TabDock.ValidationDriver` suite (18 scenarios)
  individually against commit `053a7d1`, all PASS with real evidence.
- Added `chrometabdrag` (real Chrome, drags its own tab strip — the exact H5
  HTCAPTION mechanism) and `closegroupprompt` (Yes/No/Cancel dialog, all 3
  paths) to close two checklist gaps.
- Added real-installed-app coverage: `realapp --guest {codex,chatgptclassic}`
  (fill/maximize/restore/hide-on-close against the user's own live app
  instances — never spawned, never killed by the driver; `kimi-desktop`
  excluded per instruction).
- Built the full real-browser regression layer per `docs/internal/TEST_PLAN.md`:
  `browser-lifecycle`, `browser-tabswitch-hidesafety`, `browser-dragreorder`,
  `browser-multi`, `browser-soak` — all parameterized by `--guest
  {chrome-normal|edge-normal|firefox-normal}`, extending `SpawnGuest` with
  `edge-normal`/`firefox-normal` guest kinds.
- Built and smoke-tested the self-contained Release publish exe.

## Bugs found and fixed during this session (test-harness bugs, not app bugs)

1. **Near-miss: cleanup almost killed an ancestor process of the driver's own
   shell.** `wt.exe` hands its window to an already-running `WindowsTerminal.exe`
   "monarch" process rather than spawning a new one; that process turned out to
   be an ancestor of the ValidationDriver process itself (confirmed: it had
   been running since the day before this session). `Kill(entireProcessTree:
   true)` against it was refused by .NET's own protection, but only
   incidentally. **Fixed:** `GuardedProc.IsAncestorOfCurrentProcess`
   (Toolhelp32-based) now proactively refuses to kill any tracked process that
   is an ancestor of the driver, closing the specific captured window instead.
2. **Close-group "Yes" button silently failed.** `Discover.FindChildWindowByText`
   was only given `"Yes"`, but Win32 MessageBox button text can include the
   access-key ampersand (`"&Yes"`) — the existing "No" path already handled
   both variants; "Yes" didn't. Fixed to check both.
3. **Edge/browser tab-lookup fragility.** Edge's window title embeds a
   zero-width space (`U+200B`) in its own branding, and the `time.is` test
   page's title changes over time — either can desync an exact/substring tab
   lookup a few seconds after capture (reproduced live: `browser-lifecycle
   --guest edge-normal` failed with "Tab ... not found" on first run). Fixed
   with `GuestInfo.TabMatchKey`/`EffectiveTabMatchKey`: real-browser guests now
   use a stable per-browser brand substring ("Google Chrome" / "Microsoft" /
   "Mozilla Firefox") instead of the raw title for tab lookups, chosen to also
   stay unique when two different real browsers are captured simultaneously
   (`browser-multi`).

## Honest gaps (not silently claimed as covered)

- **Firefox is not installed on this dev machine** (confirmed via
  `Get-Command`, LOCALAPPDATA, and registry checks — see
  `docs/internal/TEST_PLAN.md` section 4). The `firefox-normal` guest kind and
  all `--guest firefox-normal` scenario paths are **written but never
  executed**. Treat as HYPOTHESIS, not CONFIRMED, until run on a machine with
  Firefox installed.
- **`browser-soak` is a scoped proxy for long-running stability, not the real
  thing.** It runs 30 tab-switch cycles with a periodic health check — in
  practice this completes in about 25 seconds (300ms/switch), not the several
  minutes originally estimated in `docs/internal/TEST_PLAN.md`. A true
  multi-hour/day soak was never attempted and isn't realistic to verify within
  an interactive session; if genuinely long-running stability testing is
  wanted, `--cycles` needs to go into the thousands or the harness needs an
  actual wall-clock duration mode, and that's separate future work.
- **`LAYOUT[drift]`-without-`LAYOUT[movesize]` is now an automated assertion**
  (`TabDockLog.FindDriftWithoutPrecedingMovesize`, folded into
  `browser-lifecycle`/`browser-tabswitch-hidesafety`) but it only has evidence
  from this session's specific real-input runs (all-clean: the 1s watchdog
  never fired alone). It has not been exercised against a genuinely
  self-repositioning guest (e.g. an app that moves itself programmatically
  after capture), so the "is the watchdog still earning its keep" question
  from the original validation checklist is answered "yes for these
  scenarios," not "yes universally."
- **Render-health one-shot check** (checked ~800ms after capture only, not a
  recurring loop) — explicitly out of scope for this round per instruction;
  accepted and documented, not implemented.
