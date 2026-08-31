# Investigation: repository consolidation

**Date:** 2026-09-01
**Repository:** `quantdale/TabDock`
**Authority at orientation:** `main` / `914a25923bd4bb1f5c08d925bfb210bb9208853f`
**Origin at orientation:** `origin/main` / `914a25923bd4bb1f5c08d925bfb210bb9208853f`
**Worktree:** clean
**Open pull requests at orientation:** none

## Scope

This audit accounts for every non-main local and remote branch before any
branch deletion. Git refs and the GitHub branch collection were rediscovered
from the current repository; historical SHAs in earlier campaign notes were
not used as authority.

## Branch inventory

| Branch | Location | Tip | Merge base with `main` | Ahead / behind | Unique commits | Unique files | Classification |
| --- | --- | --- | --- | ---: | --- | --- | --- |
| `plan/repo-local-addons-2026-08-28` | local tracking ref + GitHub | `d6810f4139b22c1bcb34141991aea4688529e30a` | `f51effef4c08df3221b67e89cdc4f314df31c833` | 1 / 15 | `d6810f4139b22c1bcb34141991aea4688529e30a` (`docs: plan repository-local add-ons`) | `docs/agent-integrations/REPOSITORY_LOCAL_ADDONS_MASTER_PLAN.md` | `VALUABLE_TO_INTEGRATE` |
| `plan/visual-evidence-ai-review-20260831` | local tracking ref + GitHub | `82d862ead50a2df33e8c1a14b4413f4f070f0ef7` | `2e9397bae4095dba545bfe1ad17ac3a0acb2a1bb` | 7 / 2 | `f0849660b036d8ca298284757741f56f3e198ed7`, `8bc829debd90a1bcb850520b8b9296f1c98a0ac9`, `c98ef9d56b399d1964bdb470783847a8dd672723`, `866bc2afffdecf916e99746d2211a0941277c5e7`, `86541d2fb44c4a530957e9191f3d203f1c1934b1`, `b12c9e296a0ba886cc24d5978ae3b91af1f1ad54`, `82d862ead50a2df33e8c1a14b4413f4f070f0ef7` | six files under `openspec/changes/2026-08-31-visual-evidence-ai-review/` | `VALUABLE_TO_INTEGRATE` |

`git cherry main <branch>` marked every listed unique commit with `+`; neither
branch is patch-equivalent to current `main`. The add-ons branch is planning
content only. The visual branch is planning/OpenSpec content only, not product
or test implementation.

No other local branch exists: `refs/heads/` contains only `main`. The remote
collection at orientation contains `main` plus the two listed plan branches;
`origin/HEAD` is only a symbolic pointer to `origin/main`, not a GitHub branch.
The GitHub REST branch query returned the same three real branches. The GitHub
open-PR query returned an empty collection.

## Existing integration inventory used for reconciliation

Current main already contains and protects:

- root `.mcp.json` registering only the repository-local Repowise MCP;
- `.vscode/mcp.json` for the existing VS Code Repowise registration;
- committed adapter/skill surfaces under `.agent/`, `.agents/`, `.claude/`,
  `.cline/`, `.codex/`, `.cursor/`, `.kilocode/`, `.kimi*/`, and `.opencode/`;
- `AGENTS.md`, `ONBOARDING.md`, validation scripts, CI workflows, and durable
  state/investigation records.

No Microsoft Learn or Context7 registration/package was found in the inspected
integration and onboarding surfaces. The add-ons plan therefore remains a
future recommendation, not an implementation authorization. Existing
configurations are preserved without regeneration or global installation.

## Worktrees

`git worktree list --porcelain` reported only the primary worktree at the
repository root on `refs/heads/main`, with no auxiliary worktree, uncommitted
files, or associated branch to preserve or remove.

## Disposition

Both valuable plan branches will be copied/reconciled onto current `main` as
bounded planning content before their remote and local refs are deleted. No
production behavior, dependencies, global configuration, credentials, or
physical evidence are copied from either branch. Generated artifacts and
caches remain ignored and are not part of the salvage.
