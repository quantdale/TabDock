# Plan: Refero-guided frontend overhaul

**Status:** implementation complete; CI validation pending
**Owner/session:** ChatGPT
**Updated:** 2026-09-12

## Objective

Modernize TabDock's entire WPF frontend while preserving its existing native-window shepherding behavior, keyboard flows, UI Automation contracts, and release qualification architecture.

## Design research and direction

Refero MCP was used to inspect dense productivity/workspace interfaces rather than marketing surfaces. The selected direction combines:

- compact, low-contrast dark workspace chrome;
- layered charcoal surfaces with restrained elevation;
- one blue focus/action system instead of competing accent treatments;
- small-radius cards and controls suited to desktop utility density;
- high-information rows with clear selected, hover, focus, disabled, warning, and destructive states;
- visible but subordinate operational/status information.

Primary reference patterns came from dark productivity/dashboard screens such as Mercury, Suno, and ElevenMusic in Refero. Decorative glass, large gradients, oversized cards, and web-style navigation shells were intentionally rejected because TabDock is a native desktop window manager and needs efficient chrome rather than a web dashboard imitation.

## Baseline findings

- UI is WPF on .NET 8; there is no browser frontend framework.
- `App.xaml` is the correct shared design-system authority but previously exposed a relatively small token/style set.
- `Views/MainWindow.xaml`, `Views/CapturePickerWindow.xaml`, and `Views/ContainerWindow.xaml` are the principal user-facing surfaces.
- `ContainerWindow` has strict native geometry and automation contracts; visual work must not alter shepherding/native presentation semantics.
- Existing automation IDs are consumed by the supervised validation driver and therefore are compatibility contracts.
- The launcher's primary actions, workspace list, recovery state, and empty state competed for hierarchy.
- Picker selection state was functionally correct but visually weak, with inconsistent row/check/focus treatment.
- Container chrome used several hard-coded colors and locally divergent interaction styles.
- Floating WPF surfaces such as context menus/tooltips could visually fall back toward system defaults instead of the product theme.

## Implementation

- [x] Expand `App.xaml` into a compact shared design system: chrome/surface/input/focus tokens, cards, badges, toolbar buttons, checkbox template, typography styles, dark menus and tooltips.
- [x] Rebuild `Views/MainWindow.xaml` around one workspace-management hierarchy with actions adjacent to their target content.
- [x] Add a genuinely actionable launcher empty state while retaining the existing capture/new-workspace commands.
- [x] Keep pending-recovery and capture-admission warnings visually distinct and accessible via live regions.
- [x] Rebuild `Views/CapturePickerWindow.xaml` around a stronger filter/destination toolbar, selected-row treatment, consistent checkbox styling, and footer action hierarchy.
- [x] Refresh `Views/ContainerWindow.xaml` chrome, tab states, split states, inline capture panel, empty state, and popup surfaces while preserving native marker/content ownership.
- [x] Preserve stable UI Automation IDs including `LauncherCaptureButton`, `GroupSelector`, `WorkspaceTabs`, `SplitAffordance`, `SplitHalfLeft`, `SplitHalfRight`, `CaptureRefresh`, `CaptureGroupThese`, `CaptureAddSelected`, and `CaptureCancel`.
- [x] Preserve the true-centered `* Auto *` title geometry and `CaptionHeight=38` contract in the container.
- [x] Add `tests/UnitTests/FrontendDesignContractTests.cs` to lock the shared design tokens and key automation/presentation contracts against accidental regression.

## UX rationale

### Layout and navigation

TabDock has a small number of primary workflows, so adding a permanent sidebar would increase chrome without improving orientation. The redesign instead uses local grouping: workspace actions live with the workspace list, capture filters live with capture results, and container-level actions stay in the title/tab chrome.

### Responsiveness

The WPF windows retain their existing min-size behavior and scrollable collections. The redesign avoids fixed multi-column dashboards that would collapse badly at the application's current minimum widths.

### Accessibility

Keyboard navigation, command bindings, stable automation IDs, focus borders, disabled states, and visible warning/status text are preserved. Important status surfaces use UI Automation live settings where appropriate. Selection no longer relies on subtle color alone: borders, check state, and row surfaces reinforce it.

### Visual consistency

All principal windows consume the same shared surface, border, text, focus, action, warning, danger, and control styles. Hard-coded colors remain only where they communicate workspace-specific accent identity or specialized split-state emphasis.

## Files changed

- `App.xaml`
- `Views/MainWindow.xaml`
- `Views/CapturePickerWindow.xaml`
- `Views/ContainerWindow.xaml`
- `tests/UnitTests/FrontendDesignContractTests.cs`
- `.agent/plans/refero-frontend-overhaul-2026-09-12.md`

## Validation

- [x] Branch diff audited against `main` for frontend-only scope plus contract tests/documentation.
- [x] Key automation IDs and native presentation structural contracts covered by new unit assertions.
- [ ] GitHub CI / build / test validation on PR head.
- [ ] Supervised visual validation on Windows remains appropriate before release because this environment cannot directly operate TabDock's native desktop UI.

## Handoff

**Next action:** open PR, inspect CI, fix any failures, then perform supervised visual qualification on a Windows desktop before merge/release.
**Blockers:** native visual qualification requires a Windows interactive desktop; it cannot be truthfully claimed from connector-only repository access.
