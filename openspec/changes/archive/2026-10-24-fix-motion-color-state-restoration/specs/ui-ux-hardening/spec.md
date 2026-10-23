## ADDED Requirements

### Requirement: Animated color feedback SHALL restore independent base-state tokens

Visual feedback SHALL determine destination colors from current semantic state and independent authoritative base tokens, never from a previous animation's intermediate or filled effective value. With selection, hover and focus all inactive, visible fill and border SHALL return to their current base colors. Selection fill SHALL take precedence over hover fill; keyboard-focus border SHALL take precedence over selected border, followed by base border. Interrupted transitions and reduced motion SHALL obey the same destination rules.

#### Scenario: Selection round trip restores normal colors

- **WHEN** a row is selected, allowed to settle, then deselected with no other feedback state active
- **THEN** its visible background and border return to the pre-selection authoritative base colors instead of retaining the selected endpoint

#### Scenario: Focus exit preserves remaining selection

- **WHEN** a selected row gains and loses keyboard focus while remaining selected
- **THEN** its border changes from focus to selected feedback while its selected fill remains correct

#### Scenario: Rapid reversal does not redefine normal state

- **WHEN** hover or selection is reversed before its transition finishes and all feedback states subsequently end
- **THEN** the final visible colors equal the independent base tokens, not an intermediate sampled color

#### Scenario: Reduced motion preserves the same state endpoints

- **WHEN** the same complete and interrupted state sequences run with reduced motion
- **THEN** each state resolves to the same destination colors without a temporal transition or accumulated color drift
