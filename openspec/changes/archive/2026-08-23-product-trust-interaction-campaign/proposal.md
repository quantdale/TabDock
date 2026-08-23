## Why

TabDock's hardened recovery, capture-admission, tab-navigation, and split
machinery already protects the difficult native-window cases, but important
states and actions are still discoverable only through logs, focus-dependent
keyboard input, or an existing context menu. The next product campaign makes
those boundaries visible and dependable while preserving the existing
Shepherd, supervised-recovery, navigation-policy, and split-controller
authorities.

## What Changes

- Add a non-modal launcher warning for unresolved pending-recovery evidence,
  including the pending count and the exact read-only and supervised commands.
  The surface performs read-only discovery and never starts recovery or edits
  evidence.
- Make `GroupManager` capture admission an observable state with a current
  reason; project it into launcher, container, and capture-panel controls so
  blocked capture is disabled/clearly explained and healthy/retry transitions
  update live. Keep shortcut availability separate from capture availability.
- Register `Ctrl+Alt+PageUp` and `Ctrl+Alt+PageDown` with `MOD_NOREPEAT` for
  previous/next tab navigation. Scope the global path to a proven current
  captured guest or its owning TabDock container/chrome, while retaining local
  Ctrl+Tab and Ctrl+Shift+Tab as the fallback.
- Add an always-visible `Split ▾` container-chrome control. It exposes eligible
  partners, presented-pair focus/end actions, and dormant-pair resume/end
  actions, routing every mutation through the existing split controller and
  canonical container paths with stale-identity guards.
- Add deterministic regression and UI Automation/accessibility coverage,
  integration coverage for the combined states, documentation, and OpenSpec
  contracts.

## Capabilities

### New Capabilities

- `pending-recovery-visibility`: launcher-level read-only attention projection
  for unresolved pending-recovery evidence and supervised command guidance.
- `capture-admission-presentation`: observable canonical capture-admission
  state and consistent blocked-state presentation across capture surfaces.
- `focus-independent-tab-navigation`: scoped global previous/next navigation,
  registration diagnostics, and shared navigation operation semantics.
- `split-affordance`: persistent split discovery and action projection over the
  existing split relationship/presentation controller.

### Modified Capabilities

<!-- The new capabilities deliberately layer over existing journal, split,
     and UI hardening contracts without changing their native safety rules. -->

## Impact

- Affected production areas: `PendingRecoveryService`, `GroupManager`,
  `HotkeyService`, `App`, `MainViewModel`, `CapturePickerViewModel`,
  `GroupViewModel`, `MainWindow`, `ContainerWindow`, and focused policy seams.
- Affected tests: pending-recovery discovery, admission transitions, hotkey
  scoping/registration, navigation policy, split controller/affordance, source
  contracts, and integration/accessibility projections.
- Affected documentation: architecture, testing, README/help, agent state,
  and canonical OpenSpec capability specifications.
- No new package, native window-parenting model, recovery mechanism, or split
  state authority is introduced.
