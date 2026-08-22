## MODIFIED Requirements

### Requirement: Release candidate qualification uses one exact binary
The executable that passes `--version` identity checks and the native ABI
self-test is the same binary that is hashed, manifested, and available for
publication. Hermetic behavioral qualification is owned by the xUnit suite,
which compiles against the same product sources in the same CI gate.

#### Scenario: The published executable must be the qualified executable
- **WHEN** a release candidate is qualified
- **THEN** the executable that passes `--version` identity and `--selftest-native-abi` is the same binary that is hashed, manifested, and available for publication
