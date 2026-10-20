## Why

Audit finding A08 instantiates the actual picker DataTemplate and checks its WindowInfo model, but the row stays visually unselected. Motion observes ListBoxItem selection instead of the independent, potentially multi-row capture-selection model.

## What Changes

- Project `WindowInfo.IsSelected` into selected feedback for both standalone and inline picker rows.
- Keep keyboard/list focus independent from the set of checked capture targets.
- Preserve selection through filtering/identity-safe refresh and update all affected row visuals for Select visible/Clear.
- Test real templates and recycling rather than checking attached-property source markers alone.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `capture-picker-identity`: require capture-selection visuals to reflect the authoritative candidate selection, independently of list focus.

## Impact

Affects standalone/inline picker XAML, the motion state input contract, and picker WPF tests. Capture submission, identity keys, final Shepherd admission, and existing AutomationIds remain unchanged. Source evidence: audit A08.

## Non-goals

Do not couple ListBox selection to all checked rows, change the capture target set, or redesign picker interaction. Do not change workspace-tab selection semantics. Planning only.

## Sequencing

Requires `preserve-motion-brush-state-authority` and its lifecycle/base-token prerequisites. Keep this a separate product-state mapping change; do not absorb the generic brush-state repair into it. Current uncommitted motion templates must be retained/integrated first.
