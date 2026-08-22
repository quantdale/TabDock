# ci-tooling Specification

## Purpose
TBD - created by archiving change post-remediation-review-followup-2026-08-13. Update Purpose after archive.
## Requirements
### Requirement: Hosted OpenSpec validation SHALL use a reviewed lifecycle policy

The hosted build SHALL install the pinned `@fission-ai/openspec@1.8.0`
package with lifecycle scripts disabled unless a reviewed package-specific
requirement proves a script is necessary. The workflow SHALL not globally
approve arbitrary npm scripts or suppress installation stderr. The validation
command SHALL still run successfully under that policy.

#### Scenario: The pinned CLI validates with scripts disabled

- **WHEN** CI installs `@fission-ai/openspec@1.8.0` using `--ignore-scripts`
- **THEN** `openspec --version` SHALL report `1.8.0` and
  `openspec validate --all --no-interactive` SHALL pass

#### Scenario: An optional completion postinstall is not approved broadly

- **WHEN** the pinned package's postinstall only offers an opt-in shell
  completion hint
- **THEN** CI SHALL keep lifecycle scripts disabled rather than enabling all
  pending scripts or adding a global ignore-scripts override

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

