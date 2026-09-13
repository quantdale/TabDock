# TabDock — Frontend guide

Maintainer reference for the WPF frontend: the shared design system, the
presentation contracts the native shepherd depends on, and the automation
contracts the ValidationDriver and accessibility tooling rely on.

The visual direction comes from the 2026-09-12 Refero-guided overhaul
(`.agent/plans/refero-frontend-overhaul-2026-09-12.md`): compact dark
productivity chrome, layered charcoal surfaces, one blue focus/action system,
small radii, and dense high-information rows.

## Where the UI lives

| Surface | Window | Contents |
| --- | --- | --- |
| Launcher | `Views/MainWindow.xaml` | workspace list, capture entry point, pending-recovery and admission banners |
| Picker | `Views/CapturePickerWindow.xaml` | standalone capture flow (hotkey/launcher): filter, destination, footer |
| Container | `Views/ContainerWindow.xaml` | workspace chrome: caption, tabs, split halves, inline capture panel, empty state |

`App.xaml` owns the shared design system. Window-local resources exist only
for surface-specific chrome (`Views/ContainerWindow.xaml`: capture-button,
chrome-pill, and tab-close styles).

## Design tokens (`App.xaml`)

Colors are `SolidColorBrush` resources, all prefixed `Td`. Views consume tokens
only; the design-contract tests fail if a raw font stack or split tint
reappears in a view.

Surfaces, in elevation order: `TdWindowBackgroundBrush` (#080A0E),
`TdChromeBrush` (#0C0F14), `TdSurfaceBrush` (#0F1217),
`TdSurfaceRaisedBrush` (#151A22), `TdSurfaceInteractiveBrush` (#1B212B),
`TdSurfaceHoverBrush` (#222A36), `TdInputBrush` (#11151C).

Borders and text: `TdBorderBrush` (#272D38), `TdBorderStrongBrush` (#394252),
`TdTextBrush` (#F7F8FA), `TdTextMutedBrush` (#ADB4C0), `TdTextSubtleBrush`
(#7E8898).

Focus/action system: `TdAccentBrush` (#6798FF, rails/icons),
`TdAccentTextBrush` (#A9C3FF), `TdAccentSoftBrush` (#18233F, selected
surfaces), `TdFocusBrush` (#9CB8FF, keyboard-focus borders),
`TdPrimaryFillBrush` (#486ED8), `TdPrimaryHoverBrush` (#557BE3),
`TdPrimaryPressedBrush` (#3D5FBF).

Status and scroll: `TdWarningBrush`, `TdWarningSurfaceBrush`,
`TdWarningBorderBrush`, `TdWarningIconSurfaceBrush`, `TdDangerBrush`,
`TdDangerSurfaceBrush`, `TdDangerBorderBrush`, `TdDangerHoverBrush`,
`TdScrollThumbBrush`, `TdScrollThumbHoverBrush`.

Typography: UI text uses `Segoe UI Variable Text, Segoe UI` (display headings
add `Segoe UI Variable Display`); monospace diagnostics use `Cascadia Mono,
Consolas`; caption glyphs use `Segoe Fluent Icons, Segoe MDL2 Assets`. Text
styles: `TdHeading` (22), `TdSubheading` (15), `TdSectionLabel` (11 semi-bold),
`TdBodyMuted` (12).

## Component styles

Keyed: `TdCard`, `TdBadge`; `TdButton` (base) with `TdPrimaryButton`,
`TdSecondaryButton`, `TdGhostButton`, `TdToolbarButton`, `TdDangerButton`;
`TdTextBox`, `TdComboBox`, `TdCheckBox`, `TdListItem`, `TdFocusVisualStyle`,
`TdScrollThumb`, `TdVerticalScrollBar`, `TdHorizontalScrollBar`.

Implicit (system-theme-leak suppression; do not remove): `ContextMenu`,
`MenuItem`, `Separator`, `ToolTip`, `ComboBoxItem`, `ScrollBar`, and the
app-level `ListBoxItem` alignment defaults. The default templates inherit the
user's system theme/accent color on some desktops.

Interaction states are expressed with border color, surface color, and text
color — never by changing border thickness, which shifts content by a pixel.
Lists that need richer rows supply `TdListItem`, which provides hover,
selected, and `IsKeyboardFocusWithin` border cues. `FocusVisualStyle` is nulled
where a custom cue exists so the dotted rectangle does not double up.

## Window chrome

- The launcher and picker are standard chrome windows. Their
  `SourceInitialized` calls `WindowChromeTheme.ApplyDarkChrome`, which sets DWM
  immersive dark mode plus explicit caption/text/border colors (best-effort:
  unsupported attributes are rejected by the OS and the default remains).
  `DWMWA_*` constants stay in `NativeMethods.cs`.
- The container uses a custom `WindowChrome` with `CaptionHeight="38"` and a
  true-centered `* Auto *` title grid. `CaptionCenteringTests` locks the
  geometry; do not restructure the caption columns.
- New standard-chrome windows must call `ApplyDarkChrome` from
  `SourceInitialized` or they will render with the user's light system caption.

## Airspace rule (container empty state)

The container's native content marker is a child HWND and always paints above
WPF siblings. Any overlay that must appear over it has to live in a `Popup`
(see `EmptyStateOverlay`, an activation-gated popup that closes while the
inline capture panel is open). A sibling `Grid` child is invisible at runtime
even though the XAML designer renders it; `FrontendDesignContractTests` guards
the nesting.

Because the marker paints above siblings, its native class background is the
visible void wherever a guest does not cover the content rect (empty workspace,
tab-switch gaps, layout gaps). `NativeHwndHost` MUST register that brush with
the same RGB as `TdContentVoidBrush` (`#07090D` → COLORREF `0x000D0907`); a
palette change is incomplete until both sides move together.
`FrontendDesignContractTests.NativeContentHostFillMatchesSharedContentVoidToken`
locks the token, the packed COLORREF, and the discovery class name.

## Automation contracts

The ValidationDriver locates controls by automation ID first; removing or
renaming an ID is a breaking change. `FrontendDesignContractTests` locks the
load-bearing set:

- Launcher: `LauncherCaptureButton`, `LauncherEmptyStateHeading`,
  `PendingRecoveryBanner`, `PendingRecoverySummary`,
  `PendingRecoveryInspectCommand`, `PendingRecoveryRecoverCommand`,
  `CaptureAdmissionStatus`, `TabNavigationAvailability`.
- Picker: `CaptureRefresh`, `CaptureSelectionSummary`, `CaptureGroupThese`,
  `CaptureCancel`, `CaptureAdmissionStatus`.
- Container: `ContentHost`, `GroupSelector`, `WorkspaceTabs`,
  `SplitAffordance`, `SplitHalfLeft`, `SplitHalfRight`, `TabClose`,
  `SplitCloseLeft`, `SplitCloseRight`, `SplitCompositeItem`, `AddWindowButton`,
  `CaptureRefresh`, `CaptureAddSelected`, `CapturePanel`, `EmptyStateOverlay`.

Runtime-built menus assign IDs in code (`NewGroup`, `RenameGroup`,
`DeleteGroup`, `SplitAffordanceMenu`, `SplitCandidate`, `SplitScreen`,
`ExitSplitScreen`, `PopOut`, `CloseWindow`).

Accessibility conventions: every interactive control and status region carries
an `AutomationProperties.Name`, plus `HelpText` where the action needs
explanation; changing status regions use `LiveSetting`; the native content
marker is `Focusable="False"` so it is never a dead tab stop.

## Binding-error discipline

WPF binding failures are logged, not thrown, so they rot silently. Conventions
that keep the log clean:

- A panel whose DataContext is injected later supplies an explicit
  `DataContext="{x:Null}"` local value (inline capture panel).
- Lists set explicit content alignment instead of relying on theme styles'
  `FindAncestor` bindings (app-level `ListBoxItem` defaults).
- `BindingTraceDiagnostics.EnableIfRequested()` is opt-in: set
  `TABDOCK_TRACE_BINDINGS=<log path>`, drive the app, and inspect
  `System.Windows.Data Error` lines. A full drive of launcher, picker, and
  container must log zero.

## Testing and evidence

- `tests/UnitTests/FrontendDesignContractTests.cs` — tokens, automation IDs,
  chrome wiring, airspace nesting, focus conventions, dense-strip contract.
- `tests/UnitTests/CaptionCenteringTests.cs` — true-centered title geometry.
- `tests/UnitTests/BindingTraceDiagnosticsTests.cs` — trace opt-in.
- Runtime visual qualification uses the driver's UI Automation actions plus
  vision review; see `.agent/workflows/visual-evidence-review.md` and the
  2026-09-12 plan's captured-state list. Structural contract tests do not
  replace a rendered check for a new surface.
