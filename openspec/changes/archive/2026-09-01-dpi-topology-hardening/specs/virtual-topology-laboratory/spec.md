# virtual-topology-laboratory

## ADDED Requirements

### Requirement: Boundary topology coverage SHALL include high-DPI directional transitions

The laboratory SHALL include left-negative, above-origin, vertical staggering,
asymmetric work areas, odd dimensions, and DPI 96/120/144/168/192. It SHALL
exercise 96↔144/168/192 and 120↔144/168/192 transitions in both directions and
retain normalized reproducible topology artifacts.

#### Scenario: Above-origin 192-DPI monitor is modeled
- **WHEN** a 192-DPI secondary is above the primary with negative virtual Y
- **THEN** placement, containment, split, clamp, restore, title inputs, and drag projection remain bounded and deterministic

#### Scenario: Synthetic high-DPI coverage is offered as physical evidence
- **WHEN** a laboratory PASS is submitted to a real mixed-DPI/title/transfer gate
- **THEN** the physical gate rejects it and keeps the result as synthetic coverage only
