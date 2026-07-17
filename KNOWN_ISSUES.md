# TabDock — Known Issues (H-series summary)

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
