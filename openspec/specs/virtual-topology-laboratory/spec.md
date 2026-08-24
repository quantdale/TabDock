# virtual-topology-laboratory

## Purpose

Provides deterministic, native-free monitor, DPI, virtual-screen, placement,
and split-policy coverage while making synthetic topology explicit and
permanently ineligible for physical release qualification.

## Requirements

### Requirement: The topology laboratory SHALL model Windows monitor geometry explicitly

The laboratory SHALL represent monitor rectangles, work areas, effective DPI,
primary identity, negative virtual coordinates, odd dimensions, taskbar
offsets, narrow work areas, and large coordinates without creating native
monitors or HWNDs. It SHALL support monitor removal, reordering, and topology
transitions as first-class observations.

#### Scenario: A boundary topology is evaluated

- **WHEN** a matrix includes single 96-DPI, dual horizontal, dual vertical,
  left-negative, above-origin, asymmetric-work-area, odd-width,
  narrow-work-area, or large-coordinate monitors
- **THEN** placement, containment, split partition, clamp, restore, and drag
  projection policies return deterministic bounded results without native calls

### Requirement: Topology policy coverage SHALL be reproducible

Laboratory runs SHALL use recorded seeds and a declared boundary matrix, emit
the topology observations and policy assertions in bounded artifacts, and record
a stable `syntheticTopology=true` marker. Monitor removal/reordering and DPI
transitions SHALL be exercised in both fixed examples and seeded stress cases.

#### Scenario: The same seed is replayed

- **WHEN** a laboratory suite is run twice with the same seed and matrix
  generation
- **THEN** the normalized observations, assertion names, outcomes, and artifact
  hash are identical

#### Scenario: A transition removes the active monitor

- **WHEN** the active monitor is removed or reordered during a modeled placement
  transition
- **THEN** the policy chooses a deterministic surviving monitor or bounded
  clamp/restore result and records the transition rather than throwing or
  inventing physical evidence

### Requirement: Synthetic topology SHALL never satisfy a physical gate

Any laboratory result, replay fixture, simulated DPI report, or synthetic
monitor observation SHALL be visibly classified as synthetic and SHALL be
rejected when submitted as physical mixed-DPI, Windows compatibility,
independent-machine, or final human-smoke evidence.

#### Scenario: Synthetic mixed-DPI evidence is submitted for publication

- **WHEN** external-evidence tooling receives a bundle whose topology
  observations are synthetic
- **THEN** the mixed-DPI gate remains blocked and publication verification fails
  closed
