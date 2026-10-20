## ADDED Requirements

### Requirement: Process qualification SHALL verify effective storage isolation

Resource-soak and redirected recovery-smoke launchers SHALL create a fresh marker-bound product directory, obtain matching read-only root proof from the exact child candidate, and pass the same verified inputs to any subsequent mutating child. APPDATA-only environment overrides SHALL NOT satisfy this prerequisite. Existing supervised scenario snapshot/restore behavior, guarded spawn limits, process identity, and input qualification SHALL remain in force.

#### Scenario: Redirected recovery smoke has no access to real pending evidence

- **WHEN** the recovery smoke runs with EOF input and production-like pending/ledger sentinel files exist outside its run-owned directory
- **THEN** the child uses only the verified fixture directory and those outside sentinels remain byte-for-byte unchanged

#### Scenario: Soak preflight fails closed

- **WHEN** effective-root proof is missing, malformed, stale, candidate-mismatched or points outside the requested directory
- **THEN** the soak reports a non-pass setup outcome before normal application startup and performs no production-profile cleanup

#### Scenario: Cleanup refuses lost ownership

- **WHEN** the ownership marker no longer identifies the current run at cleanup
- **THEN** the launcher retains ambiguous storage for review and does not recursively delete it or broaden its cleanup scope
