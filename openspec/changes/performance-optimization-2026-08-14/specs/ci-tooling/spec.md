## ADDED Requirements

### Requirement: OpenSpec validation uses repository-owned locked tooling

Local and hosted validation SHALL use the repository-owned exact
`@fission-ai/openspec@1.8.0` dependency from `tools/openspec/package-lock.json`.
Installation SHALL use `npm ci --ignore-scripts`; validation SHALL invoke the
local binary and SHALL not require a globally installed OpenSpec CLI.

#### Scenario: Clean locked install validates

- **WHEN** a clean environment runs `npm ci --ignore-scripts` in
  `tools/openspec`
- **THEN** the local CLI reports version `1.8.0` and
  `openspec validate --all --no-interactive` passes

#### Scenario: Lifecycle scripts remain disabled

- **WHEN** CI installs the repository tooling
- **THEN** arbitrary npm lifecycle scripts are not enabled or globally
  approved

### Requirement: Canonical validation compile-qualifies the performance harness

The isolated `tests/Performance/TabDock.Performance.csproj` engineering harness
SHALL remain non-gating for performance thresholds and benchmark execution, but
canonical CI SHALL include an audited restore and compile-only build of that
project so production changes cannot silently leave the repository-owned
performance tooling uncompilable.

#### Scenario: Performance project compilation drifts

- **WHEN** a production change makes `TabDock.Performance.csproj` fail to restore
  or compile
- **THEN** canonical Release qualification fails before reporting the repository
  healthy

#### Scenario: Performance measurements remain non-gating

- **WHEN** canonical CI compile-qualifies the performance project
- **THEN** it does not execute benchmark scenarios or enforce latency/allocation
  thresholds
