## ADDED Requirements

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
