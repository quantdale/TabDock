# validation-qualification

## ADDED Requirements

### Requirement: Physical topology qualification SHALL be exact-candidate, supervised, and reversible

A physical monitor/DPI cell SHALL run only after capability planning proves the
exact candidate, supervision, input lease, topology, effective DPI, identity,
foreground, and point ownership. Temporary layout/scale state SHALL be
snapshotted, verified, restored, and re-verified.

The harness SHALL NOT use registry hacks, blind Display Settings input,
unrelated-window manipulation, unsupported display mutation, or destructive
automated monitor hot-unplug.

#### Scenario: Temporary left-negative layout is qualified
- **WHEN** the operator establishes and verifies a left-negative secondary and the exact-candidate cell passes
- **THEN** before/after topology snapshots and restoration evidence are required for physical acceptance

#### Scenario: Restore cannot be proven
- **WHEN** temporary display state cannot be verified as restored
- **THEN** further physical input stops and the campaign records an environment/harness block

#### Scenario: Requested physical scale is unavailable
- **WHEN** planning cannot prove 150%, 175%, or 200% physical DPI
- **THEN** the cell blocks before input and cannot inherit synthetic PASS
