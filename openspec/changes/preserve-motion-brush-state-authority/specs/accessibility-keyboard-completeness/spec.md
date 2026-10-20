## ADDED Requirements

### Requirement: Keyboard focus feedback SHALL survive motion attachment

Critical styled inputs, picker rows, workspace tabs and split halves SHALL retain a visible keyboard-focus indication after motion attachment, reload and recycling. Focus feedback SHALL remain distinguishable while hover, checked selection or split-active feedback is also present, without requiring a pointer event or changing the action/selection model.

#### Scenario: Keyboard-only picker row gains focus after load

- **WHEN** a standalone or inline picker row gains keyboard focus after its feedback is attached
- **THEN** its focus border is visible even if its base border comes from a style trigger and the row is already checked or hovered

#### Scenario: Focus moves between split halves

- **WHEN** keyboard focus moves from LEFT to RIGHT on a loaded composite
- **THEN** the visible focus indication follows RIGHT while semantic active-member feedback and existing activation behavior remain independent and correct
