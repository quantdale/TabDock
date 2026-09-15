# TabDock agent state

## Objective and status — 2026-09-15

Refresh stale agent instructions, documentation, Markdown, and related non-code
guidance. Completed plan: `.agent/plans/documentation-refresh-2026-09-15.md`.
Status: complete in the working tree, uncommitted. Audit evidence:
`.agent/investigations/documentation-refresh-2026-09-15.md`.

## Important facts

- Resolve HEAD, branch, `origin/main`, and worktree state dynamically with Git.
  Do not treat an embedded historical SHA as the commit containing this file.
- The preceding iconic-guest repair is committed in `ea1ad43`. Its investigation
  is `.agent/investigations/residual-multi-capture-black-screen-2026-09-15.md`.
- Initial-capture reconciliation and consolidation are complete; see
  `.agent/plans/initial-capture-black-screen-2026-09-15.md`.
- Prior checkpoint details and validation are preserved in
  `.agent/investigations/checkpoint-history-2026-09-15.md`.
- The broader eventually-recovering presentation report still needs supervised
  physical verification. Historical local tests do not close that report.
- Signing, physical mixed-DPI/Windows qualification, and human release gates
  still require their own evidence. Historical hosted runner failures do not
  establish the status of a new CI run.

## Completed and validation

- Refreshed entrypoints, onboarding, architecture/testing/release guidance,
  historical boundaries, and generated agent workflows; added `docs/README.md`.
- Synchronizer covers 142 generated files; `-Check` reports zero drift.
- Focused presentation tests: 26/26; release-tooling tests: 179/179;
  OpenSpec validation: 38/38. No active OpenSpec changes.
- Markdown link/source-reference audit and `git diff --check` passed, with the
  preserved vendor research link exception documented in the audit.
- Live GitHub protection queries returned a plan-related 403; protection
  enforcement is unverified, not assumed from historical records.

## Next action

Hand off the completed documentation and agent-tooling diff. No further
documentation work is identified. No commit or push is authorized.
