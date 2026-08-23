## ADDED Requirements

### Requirement: Ordinary tab drag SHALL keep working while a split pair is dormant

While a split pair is defined but NOT presented, ordinary non-member tabs SHALL
remain fully draggable: reorder within the tab strip SHALL map drop positions
through the visible strip projection to authoritative tab order by slot item
identity (never by DisplayTabs↔Tabs positional arithmetic), and drag-out beyond
the container SHALL continue to release the dragged tab through the existing
release path. The split composite SHALL remain a deliberate non-drag unit in
both pair states, and SHALL remain a valid drop-boundary region so other tabs
can be reordered around the pair without dissolving or corrupting it. While the
pair IS presented, the composite remains the selected tab-strip unit and
ordinary-tab presses remain swallowed as today. Drop-target geometry SHALL be
snapshotted once per drag; reorders SHALL NOT invalidate that snapshot because
they change no collection counts, preserving the bounded-reorder-per-drag rule.

#### Scenario: Reordering an ordinary tab while a pair is dormant succeeds
- **WHEN** a split pair A|B is defined but a non-member C is active, and C is dragged past another non-member D in the strip
- **THEN** the reorder applies to the authoritative tab order, both collections agree, and the pair's member identities are unchanged

#### Scenario: The composite itself is never grabbed as a drag unit
- **WHEN** a press-and-drag begins on the split composite while the pair is defined (presented or dormant)
- **THEN** no tab-strip drag starts and neither member is released or moved by that gesture

#### Scenario: Drop boundaries resolve around the composite without index arithmetic
- **WHEN** a drop lands before, between, or after the composite slot regardless of how many non-members precede or follow the pair
- **THEN** the resulting authoritative insertion index equals the pair's LEFT member position or the neighbouring tab's live position, never a fixed offset

#### Scenario: Presented-pair behavior is unchanged
- **WHEN** a left press lands on an ordinary tab while the pair is presented
- **THEN** the press stays swallowed exactly as before this change and no drag begins

#### Scenario: Reorders do not resnapshot drag geometry
- **WHEN** repeated reorder moves occur during one drag with unchanged tab count
- **THEN** the drag-start slot snapshot remains authoritative for the whole drag and no flip-back oscillation can form; only a genuine structural count change may resnapshot
