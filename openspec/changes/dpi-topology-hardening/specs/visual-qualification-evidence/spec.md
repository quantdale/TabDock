# visual-qualification-evidence

## ADDED Requirements

### Requirement: Physical topology visual evidence SHALL be topology-bound and scope-restricted

Physical topology checkpoints SHALL bind retained images to exact candidate,
run/scenario/attempt, topology snapshot, target monitor, effective DPI, and
approved capture scope. Before/after evidence SHOULD cover title centering, one
mixed-DPI transfer, one controlled topmost interaction, and one
maximize/restore transition where available.

Whole-desktop capture SHALL NOT be implicitly enabled to make a topology test
pass.

#### Scenario: Mixed-DPI before/after images are reviewed
- **WHEN** a supervised transfer retains approved before/after checkpoints
- **THEN** packet/result verification proves both images match the same attempt and expected source/destination topology/DPI

#### Scenario: Synthetic topology image is submitted to physical gate
- **WHEN** a fixture/laboratory image has synthetic topology provenance
- **THEN** the physical mixed-DPI visual gate remains non-pass even if review is VISUAL_OK
