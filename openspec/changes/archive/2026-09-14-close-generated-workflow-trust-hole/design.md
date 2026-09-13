## Context

See `proposal.md` for motivation. Evidence on disk right now:

```yaml
# .github/workflows/copilot-setup-steps.yml (untracked)
runs-on: ubuntu-latest
- uses: actions/checkout@v4
- run: npm install -g @fission-ai/openspec
```

`openspec/config.yaml` (unstaged) adds `githubCopilot.cloudAgent: true`.
Pinned CLI remains `tools/openspec` `@fission-ai/openspec@1.8.0`.
`scripts/release-tooling-tests.ps1` `all-actions-pinned-to-immutable-shas`
and `release-workflow-checkouts-have-no-unnecessary-persisted-credentials`
hard-code four workflow paths. `Get-ChildItem` is used for run-block
interpolation tests, which is why 179/179 still passed: the generated file
has no dispatch interpolations, and it is invisible to the pin/credential
allowlists.

This change is independent of `align-native-content-host-void`. It shares
`ci-tooling` with `retire-obsolete-spike-surface` only as additional ADDED
requirements (performance matrix vs hosted OpenSpec install). Apply either
order; do not merge the deltas into contradictory text.

## Goals / Non-Goals

**Goals:**

- Prevent the generated Copilot workflow from landing as-is.
- Close the allowlist hole so any future `.github/workflows/*.yml` is gated.
- Keep the existing four release/build workflows' pins unchanged.

**Non-Goals:**

- Enabling Copilot cloud agent as a product feature.
- Upgrading OpenSpec 1.8.0 → 1.9.0.
- Rewriting `scripts/sync-agent-configs.ps1` for every new harness directory
  unless those directories are intentionally committed (separate decision).
- Changing TabDock application behavior.

## Decisions

1. **Fail closed: do not keep the generated workflow unmodified.**
   Alternatives: (a) rewrite it to Windows + SHA-pinned checkout +
   `persist-credentials: false` + `tools/openspec` `npm ci --ignore-scripts`;
   (b) delete it and omit `githubCopilot.cloudAgent` until a compliant setup
   exists. Prefer (b) unless an operator explicitly needs Copilot cloud agent
   in this change. Ubuntu cannot build `net8.0-windows`; a setup that only
   installs a global OpenSpec CLI still violates `ci-tooling`.

2. **Enumerate all workflows in the static tests.**
   Replace the four-path arrays with `Get-ChildItem .github/workflows *.yml`.
   Keep the expected SHA map for known `actions/*` identities. A new file
   with `@v4` must fail even if it is not a release workflow.

3. **Do not expand `persist-credentials: true` exceptions.**
   Default of `actions/checkout@v4` is persist true. Requiring an explicit
   `persist-credentials: false` on every checkout remains the rule.

4. **Leave unrelated untracked harness copies alone unless they reintroduce
   this workflow.**
   `.agent/skills`, `.codebuddy`, `.pi`, and skill-mirror dirty files are
   agent-config noise. This change only owns CI trust-boundary files and the
   tests/specs that enforce them.

## Risks / Trade-offs

- **[Risk] An operator wanted Copilot cloud agent and this deletes the file.**
  → Mitigation: design permits a rewritten compliant workflow; default is
  delete. Record the choice in the apply session.

- **[Risk] Get-ChildItem picks up experimental local yml files.**
  → Mitigation: that is the point. Untracked policy-violating yml must fail
  `release-tooling-tests.ps1` before commit.

- **[Risk] Two active changes add to `ci-tooling`.**
  → Mitigation: both are ADDED requirements with different names; archive
  concatenates. Do not MODIFY the same requirement in both.

## Migration Plan

No product data. If the generated workflow is already committed on a machine,
delete or rewrite it in the same change as the test expansion so CI cannot go
red then green by ignoring the file.

## Open Questions

None for the trust-boundary rule. Whether Copilot cloud agent is desired at
all is deferred: this change does not enable it.
