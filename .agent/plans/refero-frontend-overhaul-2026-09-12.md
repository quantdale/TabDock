# Plan: Refero-guided frontend overhaul

**Status:** implementation and supervised runtime visual QA complete; final exact-SHA hosted CI rerun pending
**Owner/session:** OpenCode goal continuation (2026-09-12)
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

## Implementation

- [x] Expand `App.xaml` into a compact shared design system: chrome/surface/input/focus tokens, cards, badges, toolbar buttons, checkbox template, typography styles, dark menus and tooltips.
- [x] Rebuild `Views/MainWindow.xaml` around one workspace-management hierarchy with actions adjacent to their target content.
- [x] Add a genuinely actionable launcher empty state while retaining the existing capture/new-workspace commands.
- [x] Keep pending-recovery and capture-admission warnings visually distinct and accessible via live regions.
- [x] Rebuild `Views/CapturePickerWindow.xaml` around a stronger filter/destination toolbar, selected-row treatment, consistent checkbox styling, and footer action hierarchy.
- [x] Refresh `Views/ContainerWindow.xaml` chrome, tab states, split states, inline capture panel, empty state, and popup surfaces while preserving native marker/content ownership.
- [x] Preserve stable UI Automation IDs including `LauncherCaptureButton`, `GroupSelector`, `WorkspaceTabs`, `SplitAffordance`, `SplitHalfLeft`, `SplitHalfRight`, `CaptureRefresh`, `CaptureGroupThese`, `CaptureAddSelected`, and `CaptureCancel`.
- [x] Preserve the true-centered `* Auto *` title geometry and `CaptionHeight=38` contract in the container.
- [x] Add `tests/UnitTests/FrontendDesignContractTests.cs` to lock shared design tokens and key automation/presentation contracts against accidental regression.

## Runtime visual qualification (2026-09-12)

The redesign was validated on the real application (Release build, interactive Windows 11 desktop) by driving the actual WPF UI through UI Automation and capturing the rendered windows with vision review. Captured states:

- launcher: no-workspace empty state, populated 3-workspace list, primary-button hover, keyboard focus, minimum width (620), tall window;
- picker: populated list, multiple selected, search with results, search with zero results, destination drop-down open, disabled commit action;
- container: empty workspace, empty workspace with long name, one tab, two tabs, tab hover, tab keyboard focus, split engaged, right-half focus, workspace menu open, split menu open, split-active menu, inline Add-windows panel (empty and selected), narrow width (620).

### Defects found in runtime rendering and corrected

1. **System light/accent caption and border on standard windows.** `MainWindow` and `CapturePickerWindow` inherited the user's system title bar and border color, which clashed with the dark product surface. Added `Infrastructure/WindowChromeTheme.ApplyDarkChrome` (DWM immersive dark mode plus explicit caption/text/border colors) applied from both windows' `SourceInitialized`.
2. **ComboBox leaked the system theme.** The default ComboBox template rendered a light accent-filled selection box and — with the custom template — then inherited a near-black system foreground, making the selected text unreadable. Replaced with a complete dark ComboBox/ComboBoxItem template (including `PART_Popup`, trim-safe width, and explicit `Foreground` propagation).
3. **List scrollbars rendered with the system light theme.** Added compact dark `ScrollBar`/`Thumb` templates for both orientations.
4. **Context-menu highlight used the system accent color.** Replaced the MenuItem template with a fully styled dark template (including submenu popup support and a dark `Separator`).
5. **ToolTip fell back to system styling.** Added a dark ToolTip template.
6. **Empty-workspace prompt was invisible and its CTA unclickable.** The native content marker is a child HWND and always paints above WPF siblings (airspace), so the empty-state panel could never render. Moved it into an activation-gated `Popup` overlay (`EmptyStateOverlay`) that also closes while the inline capture panel is open, and kept the native marker untouched.
7. **No hover feedback on dense rows/tabs.** Added hover triggers to picker rows, inline-panel rows, tabs, and split halves.
8. **Keyboard focus shifted content by one pixel.** Focus is now communicated by border color instead of border thickness for buttons, text boxes, rows, and tabs.

### Known non-blocking observation

- A split-affordance menu overlapped by a guest window observed during scripted capture was reproduced as a harness artifact of the capture tool's temporary topmost manipulation; a clean interaction capture shows the menu above the guest. The container code-behind (z-order/presentation authority) is unchanged from `main`.

## Accessibility

Keyboard navigation, command bindings, stable automation IDs, focus borders, disabled states, and visible warning/status text are preserved. Important status surfaces use UI Automation live settings where appropriate. Selection no longer relies on subtle color alone: borders, check state, and row surfaces reinforce it. Automation IDs required by `ValidationDriver` are covered by contract tests.

## Files changed

- `App.xaml` — shared design system, full dark templates for system-themed controls
- `Views/MainWindow.xaml` / `Views/MainWindow.xaml.cs` — launcher redesign, dark DWM chrome
- `Views/CapturePickerWindow.xaml` / `Views/CapturePickerWindow.xaml.cs` — picker redesign, dark DWM chrome
- `Views/ContainerWindow.xaml` — container chrome, tabs, split, inline capture, popup empty state
- `Infrastructure/WindowChromeTheme.cs` — DWM dark-chrome helper
- `NativeMethods.cs` — DWM attribute constants
- `tests/UnitTests/FrontendDesignContractTests.cs` — structural/design contracts
- `.agent/plans/refero-frontend-overhaul-2026-09-12.md` — this record

## Validation

- [x] Branch diff audited against `main` for frontend-only scope plus contract tests/documentation.
- [x] Key automation IDs and native presentation structural contracts covered by unit assertions.
- [x] `dotnet build TabDock.sln` Debug and Release — 0 warnings, 0 errors.
- [x] `dotnet test tests/UnitTests/TabDock.UnitTests.csproj` Debug and Release — 820/820 pass.
- [x] `scripts/validate.ps1 -Configuration Release -Ci -Publish` — exit 0.
- [x] `scripts/release-tooling-tests.ps1` — 179/179 pass.
- [x] Runtime visual qualification on the real application (see above).
- [x] PR #13 opened for review and hosted validation.
- [ ] Hosted CI for the final exact SHA (previous run `34698091147` failed before runner allocation: zero steps, no runner assigned — infrastructure, not a source failure). Rerun on the final commit.

## Handoff

**Next action:** confirm hosted CI passes for the final commit SHA, then complete human review/merge.
**Blockers:** hosted Actions runner availability (infrastructure). High-DPI (125%/150%) visual cells and pending-recovery/capture-blocked runtime states are not reproducible on this single 96-DPI environment; they remain supervised/manual qualification items.
