# qualification-control-plane

## ADDED Requirements

### Requirement: Portable topology evidence SHALL bind monitor geometry and physical classification

Topology evidence SHALL index virtual-screen geometry, monitor/work rectangles,
primary identity, effective DPI/scale, relative placement, candidate/run/attempt
identity, and explicit synthetic/physical classification. Offline verification
SHALL reject missing, stale, contradictory, or candidate-mismatched topology
records.

A physical gate SHALL reject synthetic topology, visual packets bound to another
snapshot, or results whose physical topology/DPI capability was not proven
before input.

#### Scenario: Physical transfer bundle has matching topology evidence
- **WHEN** a child declares physical mixed-DPI transfer
- **THEN** verifier matches child, visual evidence, and before/after topology snapshots to the same candidate/run/scenario/attempt

#### Scenario: Synthetic topology is relabeled
- **WHEN** a bundle uses synthetic topology for physical mixed-DPI/title/transfer
- **THEN** verification fails closed
