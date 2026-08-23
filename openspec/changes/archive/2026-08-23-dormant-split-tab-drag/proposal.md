## Why

The live specs are silent about ordinary tab-strip interaction while a split
pair is defined but merely dormant. The implementation historically snapshotted
drag midpoints over authoritative-Tabs-space containers while the strip ListBox
is bound to the DisplayTabs projection — one slot shorter and containing a
composite item whenever a pair exists — so every drag silently lost its drop
targeting and reordering was dead for as long as a pair was merely defined,
contradicting the documented architecture intent ("the composite is not a drag
unit; normal-mode drag reorder is unchanged"). This change specifies exactly
that missing behavioral contract.

## What Changes

- Ordinary non-member tabs SHALL remain fully draggable — reorder within the
  strip and drag-out beyond the container — while a split pair is defined but
  not presented.
- The split composite SHALL remain a deliberate non-drag unit in both pair
  states; it remains a valid drop-boundary region so other tabs can be placed
  before or after the pair without positional-arithmetic assumptions.
- While the pair is presented, existing behavior is unchanged: the composite
  stays the selected tab-strip unit and ordinary-tab presses stay swallowed.
- Drop targeting SHALL resolve through visible-slot identity against live
  authoritative indexes (no DisplayTabs↔Tabs index arithmetic), preserving the
  bounded-reorder-per-drag anti-oscillation rule.

## Capabilities

### New Capabilities

- None.

### Modified Capabilities

- `ui-ux-hardening`: ADDED requirement covering dormant-pair tab-strip drag.

## Impact

Affected areas: `Views/ContainerWindow.xaml.cs` drag snapshot/drop-index code,
new pure helper `Services/TabStripDragProjection.cs`, and their tests. No
split-presentation authority, Shepherd native mutation, or persistence change;
presented-pair protection is untouched.
