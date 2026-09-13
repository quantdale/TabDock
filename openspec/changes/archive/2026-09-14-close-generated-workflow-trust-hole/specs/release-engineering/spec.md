## ADDED Requirements

### Requirement: Every hosted workflow SHALL pin actions to immutable SHAs
Every `uses: actions/*` line in every YAML workflow under
`.github/workflows/` SHALL be a full 40-character commit SHA with a trailing
`# vX` comment. Mutable tags such as `@v4` SHALL NOT appear. Generated
Copilot or coding-agent setup workflows are included. The static
release-tooling suite SHALL discover workflows by enumerating
`.github/workflows/*.yml`, not by a four-file allowlist, so a newly added
file cannot skip the pin gate.

#### Scenario: A generated setup workflow with checkout@v4 is refused
- **WHEN** `.github/workflows/` contains a file that uses
  `actions/checkout@v4` or another `actions/*@vN` tag
- **THEN** `scripts/release-tooling-tests.ps1` fails on that file

#### Scenario: Existing release workflows remain pinned
- **WHEN** `build.yml`, `prepare-release-candidate.yml`,
  `publish-release.yml`, and `qualify-candidate.yml` are inspected
- **THEN** their current SHA pins and version comments still pass

## MODIFIED Requirements

### Requirement: Release checkouts do not persist credentials
Every `actions/checkout` step in every YAML workflow under
`.github/workflows/` SHALL set `persist-credentials: false`, because none of
these workflows performs an authenticated git push from a checkout and no
credentials SHALL be persisted in `.git/config` on the runner. This includes
`publish-release.yml` (trusted policy checkouts and candidate-source
checkout), `prepare-release-candidate.yml`, `qualify-candidate.yml`,
`build.yml`, and any generated Copilot or coding-agent setup workflow. The
cross-run `actions/download-artifact@v7` steps SHALL keep their explicitly
passed `github-token` inputs (that mechanism genuinely requires them). The
static suite SHALL enumerate all workflow files, not only the four
release/build names.

#### Scenario: A release checkout never persists credentials
- **WHEN** a checkout step in any workflow under `.github/workflows/` is
  statically inspected
- **THEN** it sets `persist-credentials: false` and no workflow sets
  `persist-credentials: true`

#### Scenario: A generated setup workflow is not exempt
- **WHEN** a Copilot setup workflow checks out the repository
- **THEN** that checkout also sets `persist-credentials: false`, or the
  workflow is absent
