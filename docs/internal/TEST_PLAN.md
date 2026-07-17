# Real-Browser Regression Test Suite — Plan

Phase 1 deliverable for the "Real-Browser Regression Test Suite" build request.
Written 2026-07-18, against commit `053a7d1` (H2/H4/H5 fixes) on branch
`claude/tabdock-frame-smear-h2-plan-zsh3hq`.

---

## 1. What the existing test suite actually covers (read, not assumed)

**`tests/CaptureReleaseTest`** (`TabDock.CaptureReleaseTest`): spawns Paint,
Windows Terminal, Chrome, and Cursor, one at a time. Captures each into a
native host, verifies live rendering via two-frame `PrintWindow` diff,
releases, and diffs HWND state (parent/style/exstyle/bounds/placement)
against a pre-capture snapshot. **No tab strip, no multi-guest, no
drag/reorder, no maximize/restore, single browser (Chrome only).**

**`tests/ValidationDriver`** (`TabDock.ValidationDriver`) — the real-input
harness this plan extends. Existing scenarios, verified this session against
`053a7d1` (see `docs/internal/deep-audit-2026-07-17.md` conversation history
for full pass evidence):

| Scenario | Guests | What it covers |
|---|---|---|
| `rename`, `popout`, `closewin`, `closewin-hide` | guinea pig | rename, pop-out vs close-window distinction |
| `selfclose`, `selfhide`, `selfminhide` | guinea pig | guest-initiated close/hide/minimize-then-hide |
| `tabswitch-hidesafety` | 3 guinea pigs | 24 rapid real-mouse tab switches, hide-safety gate |
| `minrestore` | guinea pig | guest self-minimize, TabDock auto-restores it |
| `maximize-repro --guest {pig,wt,chrome-nogpu,chrome-gpu}` | 1 real guest | maximize/restore cycles, geometry + brightness/variance |
| `repeat-cycles` | guinea pig | 5x capture→maximize→restore→pop-out cycles |
| `crossfeature` | 2 guinea pigs | rename+close+maximize+popout chained in one run |
| `renderhealth` | guinea pigs (white/black) | black-tab auto-release, no false positive |
| `hotkey-afterclose` | guinea pig | hotkey/`+` button work after launcher window closes |
| `persist-kill` | guinea pig | force-kill + relaunch persistence roundtrip |
| `dragreorder` | **2 guinea pigs** | H2 drag-reorder-in-TabDock's-own-tab-strip + drag-out |
| `chrometabdrag` *(added this session)* | **1 real Chrome** | H4/H5: drag Chrome's own tab strip (HTCAPTION) inside the host, verify snap-back + no smear |
| `closegroupprompt` *(added this session)* | 2 guinea pigs | close-group Yes/No/Cancel dialog, all 3 paths |
| `realapp --guest {codex,chatgptclassic}` *(added this session)* | real installed app | fill/maximize/restore/hide-on-close against a live user app |

## 2. Gap list (what's untested against a real browser, specifically)

1. **Edge and Firefox are never touched anywhere in either test project.**
   Only Chrome has any coverage at all (`chrome-nogpu`/`chrome-gpu`/
   `chrome-normal`/`chrometabdrag`). This is the single biggest gap the build
   request calls out directly.
2. **H2 (drag-reorder-in-TabDock's-tab-strip) has zero real-browser
   coverage.** `dragreorder` uses two guinea pigs only. `chrometabdrag` drags
   Chrome's *own internal* tab strip (H5's clamp) — a different mechanism
   entirely, not TabDock's tab strip.
3. **No 20+-rapid-tab-switch test includes a real browser.**
   `tabswitch-hidesafety` uses 3 guinea pigs only.
4. **No test isolates the H4 hide/show smear scenario specifically.**
   `chrometabdrag` checks smear from a *drag*; nothing checks smear from a
   plain tab-switch hide→show cycle, which is the actual mechanism H4's fix
   targets (`TabDockContentHost`'s window-class background brush,
   `Infrastructure/NativeHwndHost.cs:67`).
5. **No test runs Chrome + Edge + Firefox (or even Chrome + Edge)
   simultaneously in one container.** Every existing scenario is
   single-real-guest.
6. **`LAYOUT[drift]` vs `LAYOUT[movesize]` ordering is not an automated
   assertion anywhere.** It was manually grepped once (ad hoc) during this
   session's validation pass, not encoded as a repeatable check.
7. **No long-running/extended-duration stability test exists.** Every
   existing scenario runs seconds to ~1 minute.

## 3. Tooling — confirmed, not assumed

Grepped the existing driver before specifying anything new. It uses:
- Raw Win32 P/Invoke (`SendInput`/`SetCursorPos` for input, `EnumWindows`/
  `GetWindowThreadProcessId`/`CreateToolhelp32Snapshot` for discovery),
  declared in the single shared `NativeMethods.cs` per the project's P/Invoke
  convention.
- `System.Windows.Automation` (UI Automation) for reading UI trees — pulled
  in via `<UseWPF>true</UseWPF>` in `TabDock.ValidationDriver.csproj`, not a
  separate package.

**No FlaUI, no Appium, no other third-party UI automation library is present
or will be introduced.** `CLAUDE.md`/`AGENTS.md` both state a hard "no
third-party NuGet packages" constraint for this repo; FlaUI would violate it.
All new tests below extend the existing `TabDock.ValidationDriver` project
using its established `Scenarios.cs`/`Discover.cs`/`Input.cs`/`Uia.cs`/
`Pixels.cs`/`GuardedProc.cs` helpers — same pattern as `chrometabdrag`,
`closegroupprompt`, and `realapp`, all added and verified working this
session.

## 4. Browser availability on this dev machine (CONFIRMED, not assumed)

```
Test-Path 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'  -> True
Test-Path 'C:\Program Files\Mozilla Firefox\firefox.exe'                  -> False
Test-Path 'C:\Program Files (x86)\Mozilla Firefox\firefox.exe'            -> False
Get-Command firefox                                                      -> not found
HKLM/HKCU StartMenuInternet\FIREFOX.EXE                                   -> not found
```

**Chrome and Edge are installed; Firefox is NOT installed on this machine.**
This is a genuine environment gap, not a test-writing gap: any Firefox-
specific test item below can be implemented (same pattern as Chrome/Edge,
window class `MozillaWindowClass` instead of `Chrome_WidgetWin_1`) but
**cannot be run and verified here**. It will be built and marked HYPOTHESIS
(code written, never executed) rather than CONFIRMED, and flagged plainly in
`KNOWN_ISSUES.md` — not silently skipped, not claimed as covered.

## 5. New scenarios to build (each extends `TabDock.ValidationDriver`)

For each: what it does, how it's automated, concrete pass/fail, what
confirms it.

### 5.1 `browser-lifecycle --guest {chrome-normal|edge-normal|firefox-normal}`
Covers: real reparent lifecycle (launch/attach/detach/close) + H4 hide→show
smear, per browser.
- **Automation:** extend `SpawnGuest`'s switch with `edge-normal` (msedge.exe,
  same `Chrome_WidgetWin_1` class as Chrome, Chromium engine) and
  `firefox-normal` (firefox.exe, `MozillaWindowClass`), both with an isolated
  temp profile dir, mirroring the existing `chrome-normal` case exactly.
- **Steps:** capture → assert `LAYOUT[capture]` + `GuestMatchesHost` →
  switch away to a second (guinea pig) tab → sample host brightness
  (`Pixels.CaptureHostScreenArea`+`ComputeAvgBrightness`) → switch back →
  sample again, assert brightness recovers to a bright baseline (not a dark
  `#1E1E1E` residue) → Pop out → assert parent/style restored.
- **Pass/fail:** `ctx.Check` booleans exactly as in existing scenarios — no
  new pattern. FAIL if brightness-after-return stays near the dark baseline
  (smear), or if `LAYOUT[capture]`/pop-out release assertions fail.

### 5.2 `browser-tabswitch-hidesafety --guest {chrome-normal|edge-normal}`
Covers: gap #3 (rapid tab-switch with a real browser mixed in).
- **Automation:** port `TabSwitchHideSafety`'s body, replacing one of the
  three guinea pigs with a real browser guest from 5.1's new kinds.
- **Pass/fail:** identical assertions to the existing `tabswitch-hidesafety`
  (tab count constant across 24 switches, zero `hid itself`/`destroyed`
  lines, all guests alive and still `WS_CHILD` at the end).

### 5.3 `browser-dragreorder --guest {chrome-normal|edge-normal}`
Covers: gap #2 — H2 with a real browser in TabDock's own tab strip.
- **Automation:** port `DragReorder`'s body, replacing one guinea pig with a
  real browser guest.
- **Pass/fail:** identical to existing `dragreorder` (`Reordered tab` count
  is a handful not hundreds, tab count stable, no `EXCEPTION` lines, guest
  alive/released correctly after drag-out).

### 5.4 `browser-multi` (Chrome + Edge simultaneously; + Firefox if installed)
Covers: gap #5.
- **Automation:** `CaptureIntoGroup(ctx, chromeGuest, edgeGuest[, firefoxGuest])`
  in one container. Switch through all tabs once each.
- **Pass/fail:** all guests alive and `WS_CHILD` at the end, tab count
  matches guest count throughout, no `EXCEPTION` lines, render-health does
  not false-positive on any of them (`renderhealth`-style check).

### 5.5 Drift-watchdog assertion (shared helper, not a standalone scenario)
Covers: gap #6.
- **Automation:** add `TabDockLog.HasDriftWithoutPrecedingMovesize(offset)` —
  scans log lines from `offset`, and for every `LAYOUT[drift]` line, checks
  whether a `LAYOUT[movesize]` line for the same guest HWND appears
  within the preceding N lines/M milliseconds. Fold this check into 5.1–5.4's
  assertions (`ctx.Check(!TabDockLog.HasDriftWithoutPrecedingMovesize(off), ...)`)
  rather than a separate scenario — it's about what happens *during* real
  interactions, not an action of its own.
- **Pass/fail:** FAIL if any bare drift line is found; log the exact line(s)
  either way (matches this session's Step 2 data-gathering approach, now
  made an actual assertion instead of an eyeballed grep).

### 5.6 `browser-soak --guest {chrome-normal|edge-normal} --cycles N`
Covers: gap #7 — **scoped down, honestly**. True multi-hour soak testing is
not realistic to run and verify within an interactive session. This is a
proxy: N tab-switch cycles (default 30, ~5–8 minutes) with a render-health
and `EXCEPTION`-count check every 5 cycles, not a single quick pass.
- **Pass/fail:** zero `EXCEPTION` lines across the whole run, guest still
  alive and `WS_CHILD` at the end, render-health never false-positives.
- **Will be documented in `KNOWN_ISSUES.md` as a scoped proxy, not equivalent
  to true long-running (hours/days) stability.**

## 6. Explicit non-goals / honesty notes for this plan

- Firefox tests will be **written** (5.1–5.4 parameterized to include it) but
  **cannot be executed or verified** on this machine — CONFIRMED only for
  Chrome/Edge, HYPOTHESIS for Firefox, stated plainly in `KNOWN_ISSUES.md`.
- 5.6 is a scoped proxy for "long-running," not the real thing.
- No new third-party package is introduced (confirmed against the repo's own
  constraint).

## 7. Self-review (per the build request's own instruction before Phase 2)

Every item above has: a concrete automation approach reusing proven existing
helpers, a concrete pass/fail condition expressed as `ctx.Check` booleans
(the same mechanism already producing real PASS/FAIL evidence all session),
and an explicit statement of what log line or measured state confirms it.
Items 5.1–5.5 are achievable and will be implemented and run for real
(Chrome + Edge). Item 5.6 and the Firefox parameterization are flagged as
scoped/uncertain up front rather than discovered as a surprise at the end.
