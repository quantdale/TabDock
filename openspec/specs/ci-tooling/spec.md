# ci-tooling Specification

## Purpose
Hosted and local qualification tooling: pinned OpenSpec CLI lifecycle policy, repository-owned OpenSpec install, and compile-only Performance harness restore/build of in-repo projects.
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

### Requirement: Performance build matrix SHALL compile only in-repo projects
When the optional performance script includes its build matrix, every restore
and compile step SHALL target a project path that exists in the current
repository: `TabDock.sln`, `tests/ValidationDriver/TabDock.ValidationDriver`,
`tests/ValidationDriver/TabDock.GuineaPig`, and
`tests/Performance/TabDock.Performance`. The matrix SHALL NOT compile
`Spike/TabDock.Spike` or any other deleted project. Canonical CI's
compile-only Performance restore/build in `validate.ps1` remains the gating
path and stays non-executing for benchmarks.

#### Scenario: Matrix build on current HEAD
- **WHEN** an operator runs the performance script's build-matrix switch on a
  tree that no longer contains Spike
- **THEN** the matrix completes its restore/build steps without invoking a
  missing Spike `.csproj`

#### Scenario: Deleted-project path is not a silent skip
- **WHEN** a listed matrix project path is absent
- **THEN** that is treated as a matrix defect to remove or repair the path,
  not as a project the repository is still expected to ship

### Requirement: Every hosted OpenSpec install SHALL use the repository-owned pin
Any GitHub Actions workflow under `.github/workflows/` that installs or
invokes OpenSpec, including generated Copilot or coding-agent setup jobs,
SHALL install the repository-owned `@fission-ai/openspec@1.8.0` dependency
from `tools/openspec` using `npm ci --ignore-scripts` (or an equivalent
locked, scripts-disabled install of that exact version) and SHALL invoke that
local binary. A workflow SHALL NOT run `npm install -g @fission-ai/openspec`
or any other unpinned/global OpenSpec install. A generated setup workflow is
not exempt from this rule.

#### Scenario: Generated Copilot setup cannot install a floating CLI
- **WHEN** a Copilot or coding-agent setup workflow is present under
  `.github/workflows/`
- **THEN** it does not contain `npm install -g @fission-ai/openspec` or an
  unpinned OpenSpec package spec, and any OpenSpec it runs reports `1.8.0`
  from the repository lockfile

#### Scenario: Hosted build remains the existing pinned path
- **WHEN** `build.yml` installs OpenSpec
- **THEN** it still uses `tools/openspec` + `npm ci --ignore-scripts` and
  does not switch to a global install
