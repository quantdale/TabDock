# presentation-integrity

## ADDED Requirements

### Requirement: Shepherd presentation SHALL remain contained across monitor and DPI transitions

A captured guest moved through a supported real monitor/DPI transition SHALL
remain the same logical member and verified native target generation where
applicable. Maximize/restore, single/split layout, local z-order, and point
ownership SHALL reconcile to the intended destination without unrelated monitor
jump, opaque-container cover, corrective tab switch, or continuous native fight.

#### Scenario: Guest transfers to a left-negative monitor
- **WHEN** a supervised transfer moves a controlled captured guest to a verified negative-X monitor
- **THEN** it remains captured, contained, visually live, and locally paired with its container

#### Scenario: Guest maximizes after mixed-DPI transfer
- **WHEN** the guest moves between different-DPI monitors and receives caption maximize or Win+Up
- **THEN** it stays in the same group/on the intended monitor and reconciles correctly after restore

### Requirement: Container title centering SHALL remain physical-DPI invariant

The visible title SHALL remain centered in the container physical width across
supported 96–192 DPI monitors, short/long names, narrow/default/wide widths, and
after transfer. Qualification SHALL measure the midpoint numerically.

#### Scenario: Long title moves from 120 DPI to 192 DPI
- **WHEN** the same container transfers between verified 120- and 192-DPI monitors
- **THEN** title midpoint remains within the documented physical-pixel tolerance of container midpoint at both observations
