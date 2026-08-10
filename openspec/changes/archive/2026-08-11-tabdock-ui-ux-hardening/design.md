# Design — TabDock UI/UX hardening

## Split pair = one logical selection unit

- The pair is the selected tab-strip item (`[ A | B ]` composite); the focused
  member is a separate concept. The composite projection (`DisplayTabs`) is the
  ONLY strip representation of a member while split is active — no ordinary
  individual tab survives for a member (verified).
- One canonical operation, `ContainerWindow.FocusSplitMember(TabViewModel)`:
  1. sets `_splitForeground` and `_shepherdActiveWindow` (field-first, so the
     `ActiveTab` PropertyChanged echo is a no-op — no re-entrancy),
  2. `_viewModel.SetActiveTab` (half highlight + `Group.ActiveIndex`),
  3. bounded `SPLIT[focus] guest=0x…` log, only when the focused member
     changes,
  4. `LayoutSplitPanes` (both panes re-glued, new member on top),
  5. `_shepherd.SetForeground(member)` — real foreground for strip clicks
     (early-returns when already foreground).
- Every member-focus entry point routes through it: composite half click,
  `SyncShepherdActiveWindow` split branch, `PairZOrderBehindGuest` (WinEvent
  direct click), WM_ACTIVATE 120 ms reassert. No initiator/partner special
  cases remain.

## Z-order invariant enforcement

- `LayoutSplitPanes`' "both glued" short-circuit pins the container below the
  partner — but only after verifying the pair's INTERNAL order with
  `GetWindow(top, GW_HWNDNEXT) == bottom`. When the focused member changed since
  the last glue (strip-initiated switch, no native raise), the cheap pin alone
  wedges the container between the panes and occludes the focused member; the
  order check falls through to the atomic `PositionGuestsDeferred` batch, which
  restores `focused → partner → container`. Steady state stays 0-1 writes.

## Window-state reconciliation

- `ContainerWindow_StateChanged`: minimize hides stay synchronous (journal-safe);
  restore/maximize re-glue through `RequestRelayout()` (one coalesced
  Render-priority pass that runs after the layout pass resized the marker =
  final content rect authoritative). One bounded `STATE[transition]` line per
  transition records the pre-layout rect for diagnosability. The
  `LayoutUpdated` hook is the net that guarantees a fresh pass after every
  layout pass; a hidden guest always fails the epsilon guard, so re-show uses
  `SWP_SHOWWINDOW` on every positioning path.

## Deterministic partition + self-test

- `SplitGeometry.Partition` is the single partition definition (floor/remainder;
  odd pixel to the right pane). `--selftest-geometry` (before mutex/UI) runs
  widths 1..4096 × heights × origins incl. negative + odd widths + 100 k seeded
  fuzz rects (seed 20260810), asserts exact coverage/zero overlap/zero gap/no
  overflow, exits 0/1, logs `SELFTEST[geometry]`.

## Cross-machine rules

- Capture refuses DPI-unaware guests when `GetDpiForSystem() != 96` (their
  DWM-virtualized 96-DPI space cannot be glued with physical pixels); the probe
  is try/catch fail-open. System-aware guests work on single-DPI systems
  (documented limitation: mixed-DPI multi-monitor).
- `EnvironmentFingerprint`: `ENV[startup]` (OS/.NET/bitness +
  `EnumDisplayMonitors` table with primary flags), `ENV[launcher]` (system DPI
  once the launcher exists), `ENV[container]` (active monitor/DPI/container/
  host/guest rects per open container), `STATE[settled]` extended. All bounded,
  invariant formatting, internally guarded.
- `PositionGuestsDeferred` checks every `DeferWindowPos` and
  `EndDeferWindowPos` return; on failure logs once per window and falls back to
  the per-guest path (same rects/z semantics).

## Harness observability

- AutomationIds on all new-feature elements; per-half × resolution by ID.
- `WaitForSplitFocus` parses the member from `SPLIT[focus]`; half-click
  assertions also check `GetForegroundWindow`.
- New scenarios: `split-three-tab-partner-popout` (survivor regression),
  `split-focus-bidirectional`, `split-partner-permutation`, and
  `split-maximize-restore-no-overlap` (partition assertion for all four
  window-state transitions, repeat cycles).

## Round 4 — Split persistence + drag-end reconciliation

### Split pair = persistent selected unit
- `SyncShepherdActiveWindow` split branch: a non-member activation (click,
  Ctrl+Tab, fresh capture) is REJECTED: if the window is newly visible (a fresh
  capture was never hidden by tab switching) it is hidden journal-safely with
  one bounded `SPLIT[persist]` line; the logical active tab is reverted to the
  focused member through `FocusSplitMember` (re-syncs `Group.ActiveIndex`, the
  half highlight, the pair glue, and foreground). `newWindow == null` (group
  teardown) keeps the guarded `ExitSplit`. No path can hide a member, release
  a window, or exit the split. The transient `Switched group … to tab C` log
  from the funnel is accepted (the settled index is the member's); harness
  assertions are settled-final-index based.
- Ctrl+Tab while split cycles only between the two members (`FocusSplitMember`).

### Drag-end authoritative reconciliation
- `WM_EXITSIZEMOVE` → `_inNativeMoveLoop = false` + one coalesced
  `RequestRelayout()` (Render priority, after layout). The modal loop's final
  z-order finalization can land after the last per-frame glue, leaving
  `[container, guest]` with the guest rect exactly matching — the old
  epsilon-only guard skipped every repair; the new pass re-validates.
- The redundant-glue short-circuit now also validates the LOCAL PAIRING via
  `WindowShepherdService.IsContainerBelowGuest` (upward `GW_HWNDPREV` walk from
  the container, skipping invisible windows, until the guest is found). A
  strict-adjacency probe was rejected: it can never hold for a topmost guest in
  a separate z-order band (taskbar between) and would re-issue `SetWindowPos`
  on every relayout pass, and it would reorder unrelated TabDock containers
  when they sit between guest and container. The walk checks the invariant the
  repair is about ("container below guest"), so healthy states write zero and a
  broken pairing heals with exactly one idempotent pin.
- The repair is gated while chrome is raised (`_chromePopupActive ||
  IsContainerChromeInteractionActive()`): the container above the guest is BY
  DESIGN during popups, and the popup-close `forceZOrder` path reconciles.
- `PairZOrderBehind`'s own no-op guard switched from "next visible below the
  guest is the container" to the same upward-walk invariant — identical cost,
  correct under banding; fixes both the single-guest guard and the split cheap
  pin's guard (topmost split members).
