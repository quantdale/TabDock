## ADDED Requirements

### Requirement: Captured guests SHALL require a healthy complete WinEvent monitor
Capture admission SHALL be disabled while the complete WinEvent hook set is not
healthy. A transient install failure MAY use the existing bounded retry cadence,
but a permanent failure SHALL release and normalize all captured members,
preserve their layout metadata, show a visible warning, and remain disabled
until restart. No unbounded polling fallback is permitted.

#### Scenario: Injected hook failure fails closed
- **WHEN** the hook seam fails every bounded installation attempt while guests are captured
- **THEN** new capture is refused, existing guests are safely released and removed from the live captured index, metadata is retained, and the failure is visible

#### Scenario: A transient hook failure recovers
- **WHEN** the first installation attempt fails but a bounded retry installs every hook
- **THEN** capture admission is restored and the captured guests remain managed without a polling loop
