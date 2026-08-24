## Why

PR #12 substantially improves TabDock's presentation, but an independent
release-candidate review found correctness and interaction gaps at the seams it
changed: picker selection identity is weaker than the native admission identity,
bulk picker actions can flood WPF command requery, and key controls remain
mouse-only or visually unfocused for keyboard and assistive-technology users.
These gaps matter now because the branch is the candidate under review, and
green headless CI does not exercise refresh races, large candidate sets, or
keyboard/UI Automation semantics.

## What Changes

- Make picker refresh continuity use an explicit Windows-aware identity key that
  includes HWND, PID, process-start identity, class, and case-insensitive
  executable identity; retain authoritative final native admission validation.
- Fail closed when a selected target disappears, changes process instance, or
  cannot provide the identity needed for capture; preserve valid selections
  through filtering and refresh without applying them to replacements.
- Batch picker selection-state notifications and coalesce background icon-result
  dispatcher work so large candidate sets remain responsive without weakening
  refresh-generation cancellation.
- Restore keyboard-complete operation for tabs, split halves, close/pop-out
  actions, rename, capture controls, menus, and picker rows, with visible focus,
  differentiated accessible names, and preserved existing ValidationDriver IDs.
- Reconcile shared WPF styles and adaptive layouts for dark surfaces, disabled
  and warning states, long text, small windows, and high-DPI scaling without
  changing the Shepherd content-host or physical-coordinate architecture.
- Add deterministic picker/performance/accessibility/source-contract tests and
  update supervised ValidationDriver scenarios, documentation, and release
  evidence vocabulary for the redesigned product.

## Capabilities

### New Capabilities

- `capture-picker-identity`: refresh continuity and fail-closed selection
  identity for candidate windows.
- `accessibility-keyboard-completeness`: keyboard, focus, UI Automation, and
  state-announcement contracts for critical product surfaces.

### Modified Capabilities

- `openspec/specs/native-window-identity/spec.md`: picker selection identity
  SHALL align with the native process-instance/class/executable contract before
  the authoritative capture transaction.
- `openspec/specs/ui-ux-hardening/spec.md`: redesigned tabs, split halves,
  chrome, and responsive surfaces SHALL remain keyboard-complete and accessible.
- `openspec/specs/capture-picker-icons/spec.md`: lazy icon work SHALL preserve
  generation safety while coalescing UI-dispatch updates for large candidate
  sets.
- `openspec/specs/e2e-scenario-coverage/spec.md`: the supervised inventory SHALL
  cover redesigned picker, keyboard, split, layout, and recovery interactions;
  unavailable desktop scenarios remain explicitly blocked.

## Impact

Affected code is limited to `Models/WindowCaptureTarget`, the capture picker
view-model and views, shared WPF resources, launcher/container XAML and small
input handlers, unit/source-contract tests, and ValidationDriver scenario
registration. No new package or native positioning authority is introduced.
The existing `WindowIdentityGate`, `WindowShepherdService`, `GroupManager`,
`DisplayTabs` projection, `ActiveTab` authority, split controller/policy, and
release qualification workflows remain the source of truth.
