## ADDED Requirements

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
