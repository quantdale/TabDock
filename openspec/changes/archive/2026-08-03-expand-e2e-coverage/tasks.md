# Tasks — expand-e2e-coverage

All work is in `tests/ValidationDriver/TabDock.ValidationDriver/` (primarily `Scenarios.cs`, `Program.cs`) plus docs. No production code changes. Every new/modified scenario must satisfy the `e2e-input-safety` spec: build only on existing primitives (`SpawnPig`/`SpawnNotepad`, `Discover` PID/class/title verification, `EnsureClickable`, `GuardedProc`, scenario try/finally `Cleanup`).

## 1. Retarget stale assertions (do first — stops false confidence)

- [x] 1.1 In `browser-lifecycle` (`Scenarios.cs:2467`): remove the positive `LAYOUT[capture]` assertion; replace capture-success verification with `IsDocked` (guest rect == content-area marker) plus a fresh `SHEPHERD[*]` line for the guest — first grep the main app source to confirm the exact `SHEPHERD` substring asserted exists in committed code
- [x] 1.2 Remove the vacuous `FindDriftWithoutPrecedingMovesize` checks from `browser-lifecycle` and `browser-tabswitch-hidesafety` (the `LAYOUT[drift]`/`LAYOUT[movesize]` instrumentation does not exist; behavioral successor is already covered by `chrometabdrag`/`dragout-by-titlebar`)
- [x] 1.3 Remove the vacuous zero-`unhealthy` assertions from `browser-soak` and `browser-multi` (`RenderHealthService` was deleted); keep their EXCEPTION/aliveness checks
- [x] 1.4 Grep the whole driver for any remaining log-substring assertions and verify each against main app source; fix or remove any others found

## 2. H2 — drag-reorder oscillation bound

- [x] 2.1 Add a log-analysis helper that, for a drag's line range, counts `Reordered tab` lines and detects immediate flip-back pairs (X→Y directly followed by Y→X for the same tab)
- [x] 2.2 In `dragreorder`: capture the log offset before the drag, then assert zero flip-back pairs and count ≤ bound (run once to observe the passing count, set the bound with generous headroom, record the observed value in a comment)
- [x] 2.3 Apply the same assertions to `browser-dragreorder`
- [x] 2.4 Sanity-check the detection: temporarily reason through (or hand-simulate) a flip-pair log sequence and confirm the helper flags it

## 3. H6 — container minimize retains tabs

- [x] 3.1 Add `container-minimize-retains-tabs` scenario: spawn 2 pigs, `CaptureIntoGroup`, click the container minimize button (`ClickMinimizeButton`), restore, assert `TabCount` unchanged, zero `hid itself`/release lines attributable to the minimize, and active guest `IsDocked` after restore
- [x] 3.2 Register the scenario in the dispatch table and `AllOrder`; verify it runs as part of `all`

## 4. H4 — Chromium render across tab switches

- [x] 4.1 In `browser-tabswitch-hidesafety`: navigate the browser to the deterministic local test page (the `chromeinput` pattern), and after each switch to the browser tab capture it via `CaptureWindowViaPrintWindow` and assert brightness/variance above the floors proven in `maximize-repro`/`realapp-multi-render`
- [x] 4.2 Keep thresholds tolerant (relative liveness, not exact pixels); record chosen constants with justification in comments

## 5. Second-tier pig flows (all join `AllOrder`)

- [x] 5.1 `hotkey-hold-single-picker`: hold `Ctrl+Alt+G` ~2 s (key repeat), assert exactly one picker window, Esc-dismiss, assert zero
- [x] 5.2 `popout-inactive-keeps-active`: 3 pigs in one group, make tab 3 active, pop out tab 1 via context menu, assert active tab is still the former tab 3 and its guest is docked/visible
- [x] 5.3 `double-capture-refused`: capture a pig, reopen the picker (Ctrl+Alt+G), assert the captured pig's title is absent from the picker list (or selection is rejected) and the group is unchanged
- [x] 5.4 `persist-active-tab-index`: extend the `persist-kill` pattern — persist a group with active tab index > 0, kill, relaunch, let the debounced save run, assert `state.json` still records the original index

## 6. Bug-hunt-derived flows with no automated coverage today (all join `AllOrder`)

Session-4 static-audit fixes (`KNOWN_ISSUES.md`, 2026-07-25) that are "reviewed-and-reasoned, not runtime-confirmed" and not exercised by any existing scenario, including the four in section 5.

- [x] 6.1 `restored-group-survives-member-reclose`: extend the `persist-kill` pattern one step further — capture + rename a pig, kill, relaunch (restored empty shell), re-capture a pig into that shell, then destroy the pig (WM_CLOSE) and separately (a second run of the same shape, or a second phase in this one) tray-hide it via `--hide-on-close`; assert `state.json` still contains the group name and original tab metadata after each. This is the `RemoveDeadMember` guard (`App.xaml.cs:365`), distinct from `persist-kill`'s `OnContainerClosed` guard (`:815`).
- [x] 6.2 `selfminimize-timer-vs-teardown`: capture a pig, click its own native title-bar minimize button (same technique family as `dragout-by-titlebar`, not `ClickMinimizeButton` which targets the container's chrome), then immediately pop out its tab (or close the container) well inside the 200ms `RestoreMinimizedWindow` delay; wait past the delay with headroom (e.g. 500ms) and assert the guest was not force-restored/repositioned by the stale timer
- [x] 6.3 `launcher-empty-state-hint`: on fresh TabDock launch with zero groups, read the "No groups yet" hint's UIA state (`IsOffscreen`/bounding rect) via `Uia.FromHwnd`/`FindDescendantByName` on the launcher window and assert it's visible; capture a pig into a new group and assert the hint is no longer visible
- [x] 6.4 Register all three in the dispatch table and `AllOrder`; verify they run as part of `all`

## 7. Validation and docs

- [x] 7.1 `dotnet build` the ValidationDriver project with zero warnings
- [x] 7.2 Supervised run: `all` passes with the new scenarios included; individually run each modified/new browser scenario with an available `--guest` — post-fix `all` run: 22/25 PASS, all 8 new scenarios PASS, the only failures are the pre-existing documented baseline (`maximize-repro`/`repeat-cycles` — guarded `UpdateLayout` geometry regression, `KNOWN_ISSUES.md:256-269`; `hotkey-afterclose` — `ForceForeground` env flake, `KNOWN_ISSUES.md:342-350`). Browser scenarios individually green with the context-menu fix: `browser-lifecycle`, `browser-tabswitch-hidesafety`, `browser-soak` (chrome-normal), `browser-multi`, `browser-dragreorder` (edge-normal). H4 per-switch liveness check hardened with a resample loop against the documented Chrome background-timer-throttling flake (`KNOWN_ISSUES.md:553-561`).
- [x] 7.3 Update `docs/TESTING.md`: scenario list, remove/rewrite the §A stale-instrumentation note for the retargeted scenarios, confirm §D reflects the now-enforced rule
- [x] 7.4 Update `AGENTS.md` testing section if the scenario inventory or `all` composition changed
- [x] 7.5 Final per-scenario audit against the five `e2e-input-safety` requirements (identity re-verification, ownership scoping, try/finally cleanup, no blind retries, zero-orphan-window assertion) — for the last one, confirm every scenario touched or added in this change calls `NoOrphanPigWindows` (or equivalent) in its final assertions
