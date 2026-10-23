## ADDED Requirements

### Requirement: Capture-row visual selection SHALL follow checked-target authority

Both standalone and inline capture rows SHALL derive selected fill/border feedback from the authoritative checked-target selection, independently of list focus or the list's current single selected row. Multiple checked candidates SHALL be highlighted simultaneously. Hover/focus-only operations SHALL NOT change submitted targets. Filtering, refresh and recycling SHALL preserve the existing identity-safe selection rules while displaying only the current row model's checked state.

#### Scenario: Checking an unfocused row highlights it

- **WHEN** a candidate is checked while its list item is not the selected or focused list row
- **THEN** that candidate displays selected feedback and belongs to the same submitted target set as before the visual repair

#### Scenario: Bulk selection and clearing update every affected row

- **WHEN** Select visible or Clear changes multiple candidate selections
- **THEN** each realized affected row reflects its checked state without moving list focus to every checked row

#### Scenario: Filtering and identity-safe refresh retain correct highlights

- **WHEN** a checked candidate is hidden by search then shown again, or refreshed with the same proven identity
- **THEN** its checked selection and highlight remain aligned; a changed/recycled identity does not inherit either

#### Scenario: A recycled checked row becomes an unchecked candidate

- **WHEN** a visual container previously used for a checked candidate is rebound to an unchecked candidate
- **THEN** its selected feedback clears to the new model state and no old selection listener updates it later

#### Scenario: Motion policy does not change selection meaning

- **WHEN** checked-row sequences run with enabled and reduced motion
- **THEN** final highlighted rows match the same checked-target set in both modes and ordinary workspace-tab selection semantics are unchanged
