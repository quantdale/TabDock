## ADDED Requirements

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
