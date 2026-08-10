# Tasks — tabdock-ui-ux-hardening

## Phase 1 — Split symmetry (Defect A)
- [x] `ReleaseTab`: honor the already-promoted split survivor before the
  positional-neighbour fallback (regression: partner pop-out with ≥3 tabs hid
  the survivor).
- [x] Canonical `FocusSplitMember(TabViewModel)`: fields first, `SetActiveTab`,
  changed-guarded `SPLIT[focus]`, `LayoutSplitPanes`, real foreground.
- [x] Route every member-focus entry point through it: half click, active-tab
  sync, WinEvent direct click, WM_ACTIVATE reassert.
- [x] `LayoutSplitPanes` short-circuit verifies the pair's internal z-order
  (`GW_HWNDNEXT`) before the cheap pin; falls through to the atomic batch when
  stale.
- [x] `StartSplitFrom` re-validates initiator/partner against `Tabs` (stale
  context-menu race).

## Phase 2 — Window-state geometry (Defect B)
- [x] `StateChanged` restore/maximize branch re-glues via `RequestRelayout`
  (final rect authoritative); minimize hides stay synchronous.
- [x] Bounded `STATE[transition]` diagnostic per transition.

## Phase 3 — Cross-machine hardening (Defect C)
- [x] `EnvironmentFingerprint` (startup/launcher/container + extended
  `STATE[settled]`) with `EnumDisplayMonitors` table.
- [x] DPI-unaware capture refusal at non-100% system scale (fail-open probe).
- [x] `PositionGuestsDeferred` return checks + fallback + rate-limited failure
  log.
- [x] Caption glyph fallback (Segoe MDL2 Assets) + work-area size clamps.

## Phase 4 — Deterministic geometry testing
- [x] `SplitGeometry.Partition` extraction; `--selftest-geometry` matrix + fuzz
  (14,718,730 checks PASS, seed 20260810).

## Phase 5 — Harness
- [x] AutomationIds (composite, halves, per-half ×, group selector,
  rename/delete/new-group, add-window, capture UI, tab close).
- [x] `Uia.FindDescendantByAutomationId`; per-half × resolution by ID.
- [x] Member-scoped `SPLIT[focus]` parsing + foreground assertions.
- [x] z-order probe after menu close; WaitUntil transitions in
  add-window-toggle/group-rename-menu; self-close margin 20 s; reorder log
  wait.
- [x] New scenarios: `split-three-tab-partner-popout`,
  `split-focus-bidirectional`, `split-partner-permutation`,
  `split-maximize-restore-no-overlap` (all four transitions) — registered in
  `AllOrder` + runner.
- [x] Supervised ValidationDriver batch run (BLOCKED: repo rule — no
  unattended SendInput; batch plan in `.agent/STATE.md`).

## Phase 6 — Documentation
- [x] Waypoint + `.agent/STATE.md` updated at each milestone.
- [x] `README.md` / `docs/ARCHITECTURE.md` / `docs/TESTING.md` hardening notes.
- [x] OpenSpec change (this one).

## Phase 7 — Split persistence + drag-end reconciliation (Round 4)
- [x] `SyncShepherdActiveWindow`: non-member activation while split is active is
  REJECTED — newly-visible non-members are hidden journal-safely
  (`SPLIT[persist]`), the logical active tab is reverted to the focused member
  via `FocusSplitMember`, null-tab teardown keeps the guarded `ExitSplit`.
- [x] Ctrl+Tab while split cycles only between the pair's members.
- [x] `WM_EXITSIZEMOVE` schedules one coalesced `RequestRelayout` (final
  reconciliation after the native move loop; per-frame drag path untouched).
- [x] Redundant-glue short-circuit validates the local pairing
  (`WindowShepherdService.IsContainerBelowGuest` upward `GW_HWNDPREV` walk
  skipping invisible windows) before skipping writes; chrome-active gate;
  `PairZOrderBehind` no-op guard uses the same invariant (topmost-guest banding
  and hidden IME helpers never churn).
- [x] Harness: `split-third-tab-hover-persists`, `split-third-tab-click-persists`
  (incl. Ctrl+Tab step and settled-final-index assertions),
  `split-drag-release-render-stability`, `drag-release-render-stability`
  (`Input.DragPolyline` multi-segment trajectory, phase-robust 3-frame pulse
  liveness probe, top-window-at-pane-center vacuity guards); rewritten
  `split-click-third` + `split-composite` non-member section; composite-aware
  `TabCount` corrections in six pre-existing 2-tab-split scenarios.
- [x] Docs/OpenSpec: waypoint Round 4, ARCHITECTURE/TESTING/README, delta spec
  requirements (persistence contract + drag-end reconciliation).
- [x] Supervised ValidationDriver batch run (BLOCKED: repo rule — no
  unattended SendInput; run with a human supervisor present).
