## Why

Audit finding A07 shows motion assigning local brush values on Loaded, defeating remaining split-active and picker-focus style triggers. Animations must decorate authoritative state, not override the bindings and triggers that define it.

## What Changes

- Preserve live style/binding ownership of base brushes rather than replacing it with motion-owned local values.
- Define one coordinated visual-state authority for the affected split halves, picker rows, tabs, and shared controls.
- Reflect later active/focus/style changes and restore the correct base source after feedback ends or detaches.
- Add actual-template precedence, binding-change, theme-brush, and recycling regressions.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ui-ux-hardening`: require motion to preserve base brush authority and live semantic state.
- `accessibility-keyboard-completeness`: require visible focus to survive feedback attachment and competing visual states.

## Impact

Affects the uncommitted MotionProfile, affected App/container/picker templates, and dispatcher-owned WPF tests. No native focus/foreground or split-controller behavior changes. Source evidence: audit A07.

## Non-goals

Do not add a second split state machine, change capture selection identity, or change the visual palette. A08 owns which selection source a picker row uses. Planning only.

## Sequencing

Apply after `fix-motion-subscription-lifecycle` and `fix-motion-color-state-restoration`. Reuse their attachment/base-token ownership. Implement `fix-capture-selection-highlighting` afterwards to bind the picker-specific state input without another competing brush writer.
