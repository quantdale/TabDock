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
