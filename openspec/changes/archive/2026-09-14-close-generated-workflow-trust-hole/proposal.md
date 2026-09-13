## Why

The first audit pass treated untracked OpenSpec 1.9 harness files as local
noise. Verification shows they include `.github/workflows/copilot-setup-steps.yml`,
which installs OpenSpec with `npm install -g @fission-ai/openspec` (unpinned,
lifecycle scripts enabled), checks out with `actions/checkout@v4` (mutable tag;
default `persist-credentials: true`), and runs on `ubuntu-latest` (cannot build
this `net8.0-windows` WPF product). That contradicts `ci-tooling` (repository-owned
`@fission-ai/openspec@1.8.0` via `npm ci --ignore-scripts`, no global CLI) and
the intent of `release-engineering` (SHA-pinned actions, `persist-credentials:
false`).

The static suite that should have caught this still **passes** (179/179)
because `all-actions-pinned-to-immutable-shas` and
`release-workflow-checkouts-have-no-unnecessary-persisted-credentials` inspect
a four-file allowlist (`build.yml`, `prepare-release-candidate.yml`,
`publish-release.yml`, `qualify-candidate.yml`) rather than every workflow
under `.github/workflows/`. A generated fifth file is therefore invisible to
the pin/credential gates. `openspec/config.yaml` also gained
`githubCopilot.cloudAgent: true` against the pinned 1.8.0 CLI.

## What Changes

- Do not keep the generated Copilot setup workflow as-is. Either delete it, or
  rewrite it to Windows (if a product build is required), SHA-pinned actions,
  `persist-credentials: false`, and the repository-owned OpenSpec 1.8.0 install
  with `--ignore-scripts`.
- Expand the static pin and credential tests to every `*.yml` under
  `.github/workflows/`, not a four-file allowlist.
- Extend `ci-tooling` and `release-engineering` so Copilot/setup and any future
  generated workflow are in the same trust boundary as `build.yml`.
- Do not enable Copilot cloud agent until that setup is policy-compliant.
  Unrelated harness skill mirrors remain out of scope except where they would
  reintroduce this workflow.

## Capabilities

### New Capabilities

- None.

### Modified Capabilities

- `ci-tooling`: hosted OpenSpec installs in **any** GitHub workflow, including
  generated Copilot/setup jobs, SHALL use the repository-owned 1.8.0 pin with
  lifecycle scripts disabled and SHALL NOT `npm install -g` an unpinned CLI.
- `release-engineering`: every workflow under `.github/workflows/` SHALL
  SHA-pin `actions/*` uses and set `persist-credentials: false` on checkout;
  the static suite SHALL enumerate all workflow files, not an allowlist.

## Impact

- `.github/workflows/copilot-setup-steps.yml` (delete or rewrite).
- `scripts/release-tooling-tests.ps1` (pin/credential cases).
- `openspec/config.yaml` `githubCopilot` key (keep only if the setup workflow
  is compliant; otherwise remove).
- Specs: `ci-tooling`, `release-engineering`.
- No product runtime behavior. Not **BREAKING** for TabDock.exe. Committing the
  generated workflow **without** this change **would** be a trust-boundary
  regression.
