# TabDock UI/UX hardening — split symmetry, window-state geometry, cross-machine robustness

## Why

Manual testing after the vertical split-screen milestone found three priority
defects: (1) interacting through the split PARTNER member (the non-initiator)
could behave differently from the initiator — up to one pane failing to render;
(2) after the container's native maximize/restore transitions the two panes
could overlap; (3) behavior degrades on other machines with no way to diagnose
why. This change hardens the existing split-screen behavior (already specified
and archived) without changing its surface semantics.

## What Changes

- **Split pair = one logical selection unit (symmetry).** After split creation
  LEFT and RIGHT are peers. A single canonical member-focus operation
  (`FocusSplitMember`) is the only path that focuses a member; it updates the
  focused-member metadata, the logical active member, the bounded `SPLIT[focus]`
  diagnostic, the local z-order (focused guest above partner above container —
  verified against the actual window order before the cheap pin), and grants
  the clicked member real foreground. It never changes split membership or
  hides the partner.
- **Survivor promotion.** When a split member leaves the group, the surviving
  member is promoted to the single visible guest; no later positional
  neighbour-selection may hide or displace it (regression: popping the focused
  partner with ≥3 tabs previously hid the survivor).
- **Authoritative final content rect.** Window-state transitions (maximize,
  restore, minimize-restore) re-glue guests exclusively through the coalesced
  post-layout pass, never synchronously against the pre-transition marker
  rect; the split partition invariants (exact coverage, zero overlap, zero
  gap, odd-width remainder) hold for every transition.
- **Deterministic partition definition.** The 50/50 split math is extracted to
  a single `SplitGeometry.Partition`; `TabDock.exe --selftest-geometry` runs a
  deterministic matrix + seeded fuzz over the partition invariants on any
  machine with no UI or input.
- **Cross-machine hardening.** DPI-unaware guests are refused at capture on
  non-100% systems (physical-pixel glue cannot represent their virtualized
  coordinate space); caption glyphs fall back to Segoe MDL2 Assets (Win10);
  window initial sizes clamp to the primary work area; environment
  fingerprints (`ENV[startup]`/`ENV[launcher]`/`ENV[container]` + extended
  `STATE[settled]`) make customer machines diagnosable; deferred-positioning
  batch failures are checked, logged once per window, and fall back to
  per-guest positioning.
- **Harness robustness.** AutomationIds on the composite split tab, halves,
  per-half close buttons, group selector, rename/delete/new-group menu items,
  add-window button, and capture UI; member-scoped `SPLIT[focus]` parsing and
  real-foreground assertions; condition-based waits replacing fixed sleeps on
  the high-risk paths; four new scenarios (partner-popout regression,
  bidirectional focus, initiator/partner permutation, maximize/restore
  partition cycles incl. all four window-state transitions).
- **Split persistence (Round 4).** The pair is the persistent selected
  tab-strip unit: hover/click/context-menu on a non-member tab never exits the
  split or changes the visible set — a non-member activation is rejected and
  the logical active tab reverted to the focused member (`FocusSplitMember`),
  a newly captured window is hidden journal-safely, and Ctrl+Tab cycles only
  between the members. Split ends only via an explicit Split Screen operation
  or a structural member removal.
- **Drag-end reconciliation (Round 4).** `WM_EXITSIZEMOVE` schedules one
  coalesced post-layout reconciliation; the redundant-glue short-circuit
  validates the local z-order pairing (container below guest — upward
  `GW_HWNDPREV` walk skipping invisible windows) before skipping its native
  writes, gated off while TabDock chrome is raised, with zero writes in the
  healthy steady state and at most one repair write otherwise. Fixes the
  post-drag blanking where a correctly-sized guest sits below the container
  until a tab switch re-glues it.

Explicitly out of scope: reparenting or guest-style mutation (Shepherd
prohibition preserved), draggable split ratio, single-shell
rewrite, `SWP_NOREDRAW` on the hot path, `Global\TabDock` mutex namespace change.

## Capabilities

### New Capabilities
- `ui-ux-hardening`: the split-symmetry invariants, window-state geometry
  reconciliation, the deterministic partition self-test, the cross-machine
  capture/DPI rules, and the environment fingerprint.

### Modified Capabilities
(the archived `split-screen` capability's non-member-click semantics are
superseded by this change: clicking a non-paired tab no longer exits the split
— the pair persists until an explicit or structural teardown; recorded in the
new `ui-ux-hardening` delta requirements)

## Impact

- **Code**: `Views/ContainerWindow.xaml.cs` (canonical `FocusSplitMember`,
  z-order-order check in `LayoutSplitPanes`, `StateChanged` → coalesced
  re-glue, `StartSplitFrom` re-validation, AutomationIds; Round 4:
  non-member-activation revert in `SyncShepherdActiveWindow` + `SPLIT[persist]`
  hide, Ctrl+Tab pair-cycle, `WM_EXITSIZEMOVE` → coalesced reconciliation,
  z-order-validated redundant-glue guard with chrome gate),
  `ViewModels/GroupViewModel.cs`
  (survivor-honoring `ReleaseTab`), `Services/WindowShepherdService.cs`
  (checked deferred batch + fallback, DPI-unaware capture refusal,
  rate-limited failure log; Round 4: `IsContainerBelowGuest` upward-walk
  invariant predicate as the `PairZOrderBehind` no-op guard),
  `Services/SplitGeometry.cs` (new partition
  definition + self-test), `Services/EnvironmentFingerprint.cs` (new),
  `NativeMethods.cs` (EnumDisplayMonitors, DPI-awareness, `GW_HWNDNEXT`),
  `App.xaml.cs` (`--selftest-geometry` mode, `ENV[startup]`/`ENV[launcher]`),
  `Views/ContainerWindow.xaml` + `Views/CapturePickerWindow.xaml`
  (AutomationIds, glyph fallback, size clamps).
- **Tests**: `tests/ValidationDriver/.../Scenarios.Split.cs` (+8 scenarios
  incl. Round 4 `split-third-tab-hover-persists`, `split-third-tab-click-persists`,
  `split-drag-release-render-stability`; rewritten `split-click-third` +
  `split-composite` non-member section; member-scoped focus assertions,
  partition assertions, composite-aware `TabCount`), `Scenarios.Drag.cs`
  (+`drag-release-render-stability`, `Input.DragPolyline`,
  `BuildDragTrajectory`, `EnsureContainerInWorkArea`, `TopWindowPidAt`),
  `Scenarios.cs` (registration), `Uia.cs` (`FindDescendantByAutomationId`).
- **Docs**: `docs/ARCHITECTURE.md`, `docs/TESTING.md`, `README.md`,
  `docs/internal/ui-ux-stabilization-waypoint.md`.
- **No new dependencies, no new projects, no persisted-schema change, no
  reparenting.**
