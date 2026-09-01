# monitor-dpi-probing

## ADDED Requirements

### Requirement: Physical high-DPI transition qualification SHALL preserve effective monitor identity

Physical transition evidence SHALL record source/destination monitor geometry,
effective DPI/scale, and candidate/run identity. Coverage SHOULD exercise 144,
168, and 192 DPI (150%, 175%, 200%) where the supervised host supports them.
Unavailable physical scale classes remain blocked; synthetic conversion coverage
never promotes them to physical PASS.

#### Scenario: Guest transfers from 120 DPI to 192 DPI
- **WHEN** an exact-candidate supervised scenario moves a controlled guest from a verified 120-DPI monitor to a verified 192-DPI monitor
- **THEN** destination DPI, physical outer geometry, containment, and subsequent restore/maximize observations bind to the 192-DPI destination

#### Scenario: Requested high-DPI hardware is unavailable
- **WHEN** no real monitor exposes the requested high-DPI cell
- **THEN** deterministic tests may pass but the physical cell remains BLOCKED_CAPABILITY or BLOCKED_ENVIRONMENT
