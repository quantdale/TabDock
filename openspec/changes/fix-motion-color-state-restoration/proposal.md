## Why

Audit finding A06 selects and deselects a motion-enabled row but leaves its background/border at the selected colors in both normal and reduced-motion modes. The update code treats animated brush values as the original normal-state tokens.

## What Changes

- Keep normal/base color tokens independent of animated effective values.
- Resolve current visual state deterministically and restore the correct base colors on state exit.
- Replace/interrupt animations without treating their intermediate or filled values as new normal tokens.
- Add real WPF round-trip tests for selection, hover, focus, and rapid interruption under both motion policies.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ui-ux-hardening`: require reversible animated feedback with independent normal-state color tokens.

## Impact

Targets border feedback in the uncommitted MotionProfile and its behavioral tests. Keep existing durations, easing, palette, and native guest behavior. Source evidence: audit A06.

## Non-goals

Do not replace style/binding authority (A07), change picker selection sources (A08), or redesign theme tokens. Live OS motion-preference refresh is a separate optional improvement. Planning only.

## Sequencing

Requires `fix-motion-subscription-lifecycle` and the current motion baseline. Apply before `preserve-motion-brush-state-authority`; that change may update base tokens when the style/binding source changes, but must never derive them from an animated endpoint.
