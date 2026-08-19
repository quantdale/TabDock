# Repository Protection Policy

## Applied (2026-08-15, session with admin scope)

### Ruleset: `release-tags` (id 20878779, active, target: tag)

- Patterns: `refs/tags/v*`
- Rules:
  - **Deletion blocked** — a `v*` release tag cannot be deleted.
  - **Non-fast-forward blocked** — a `v*` release tag cannot be force-pushed
    or moved.

Rationale: release tags are the immutable pointer from a published release to
its exact qualified source commit. Nothing in this ruleset touches `main` or
the solo/autonomous-agent direct-push workflow: a push to `main` still runs
the canonical `build` workflow, and validated changes continue to reach `main`
directly. The production release path is additionally gated by the
dispatch-only `release.yml` workflow, which re-verifies artifact provenance,
`SHA256SUMS.txt` consistency, the external evidence record, and the
Authenticode signature before creating a `v*` tag (see
`docs/release/publication-gates.md`).

## Promotion Architecture (P2 exact-SHA staging promotion)

### Recommended path: `agent/staging` -> exact-SHA verify -> `main`

`main` admission is now gated through the `promote-staging` workflow
(`.github/workflows/promote-staging.yml`), the only workflow with
`permissions: contents: write` capable of fast-forwarding `main`.

- **Staging branch:** agents and humans push validated changes to
  `agent/staging` (not directly to `main`). A push to `agent/staging`
  triggers `build.yml` qualification on that SHA and, on success, is eligible
  for promotion.
- **Promotion workflow:** `promote-staging` is triggered either by a push to
  `agent/staging` (promotes `github.sha`) or by `workflow_dispatch` with an
  explicit `inputs.sha` (the exact commit on `agent/staging` to promote).
  It serializes via `concurrency: group: promote-main, cancel-in-progress:
  false` to avoid TOCTOU races between concurrent promotions.
- **Exact-SHA verification (fail-closed):**
  1. Resolve `expected SHA` = `inputs.sha` (dispatch) else `github.sha` (push).
     Reject unless `^[0-9a-f]{40}$`.
  2. Checkout `expected SHA` with `fetch-depth: 0` (pinned
     `actions/checkout@3d3c42e...`) and verify `HEAD == expected SHA`.
  3. Verify `origin/agent/staging == expected SHA` (`git rev-parse
     origin/agent/staging`). The branch must still point to the qualified
     commit — an unqualified tip cannot be promoted.
  4. Verify `build.yml` qualification passed for that exact SHA:
     `gh run list --workflow build.yml --commit $sha --json conclusion,status`
     must contain at least one `completed`/`success` run. No run = fail with
     recovery instructions (re-run `build.yml` on that SHA first).
  5. Verify fast-forward safety: `origin/main` is an ancestor of `expected
     SHA` (`git merge-base --is-ancestor origin/main $sha`) or `main == sha`
     (no-op success). Divergence = fail with rebase instructions.
  6. Promote via `gh api PATCH repos/{owner}/{repo}/git/refs/heads/main`
     with `force: false`. This fails closed (422) if `main` moved since fetch.
     On 422 `Reference update failed`, the workflow reports concurrent advance
     and recovery steps.

Only this workflow may fast-forward `main`; all other workflows remain
`contents: read`.

### Autonomous fallback (direct pushes to `main` remain technically possible)

No branch protection is enforced on `main` in this environment (see below), so
a direct `git push origin HEAD:main` is still technically possible. This is
intentional: it preserves the solo/autonomous-agent fallback when the staging
gate cannot be used (offline, emergency, or before the ruleset is enabled).
The fallback is now **non-recommended and auditable**:

- The recommended path is always `agent/staging` + `promote-staging`.
- Direct pushes bypass exact-SHA verification and are visible in the audit log
  as pushes not performed by `promote-staging`.
- Future enablement of the branch ruleset (below) will block direct pushes
  except for the promotion workflow's bypass.

## Branch Protection on `main` — Recommended Ruleset

### Why not yet enforced

This repository intentionally uses autonomous agents that push validated
changes directly. A required-status-check branch ruleset without a bypass
would deadlock that workflow (the push would be blocked before the check it
triggers could run). Until the ruleset below is enabled with a bypass, the
workflow-level exact-SHA gate in `promote-staging` provides the safety
property when the recommended path is used, while direct pushes remain as the
fallback.

### Exact ruleset to enable (when GitHub settings can be modified)

Create a branch ruleset targeting `main` (or a classic branch protection
rule) with:

- **Require status checks to pass:** `build` (the `build.yml` workflow) and
  `verify-and-promote` / `promote-staging` must be `success`. The promotion
  workflow itself already re-verifies `build.yml` for the exact SHA, so the
  required check is defense-in-depth.
- **Block force pushes** and **block deletions** on `main`.
- **Require linear history** (optional, recommended) — preserves the
  fast-forward invariant the promotion workflow enforces.
- **Do not require pull requests** — agents push to `agent/staging`, not via
  PRs; the staging branch is the integration point.
- **Bypass list (critical to avoid deadlock):** allow bypass for the actor
  that runs `promote-staging`:
  - Ruleset bypass: add `github-actions[bot]` (the `GITHUB_TOKEN` actor) or
    the specific GitHub App used for promotion to the ruleset's **Bypass
    actors** with `Bypass` permission, or
  - Classic protection: enable **Allow specified actors to bypass required
    pull requests / Allow GitHub Apps to bypass** and list the promotion app.
  Without this, the promotion workflow's `PATCH refs/heads/main` would itself
  be blocked by the rule it is meant to satisfy.

Example `gh` creation (requires admin scope; adjust actor IDs for your org):

```bash
gh api repos/{owner}/{repo}/rulesets --method POST --input - <<'JSON'
{
  "name": "main-promotion-gate",
  "target": "branch",
  "enforcement": "active",
  "conditions": { "ref_name": { "include": ["refs/heads/main"], "exclude": [] } },
  "rules": [
    { "type": "deletion" },
    { "type": "non_fast_forward" },
    { "type": "required_linear_history" },
    { "type": "required_status_checks", "parameters": {
        "strict_required_status_checks_policy": true,
        "required_status_checks": [
          { "context": "build" },
          { "context": "verify-and-promote" }
        ]
      }
    }
  ],
  "bypass_actors": [
    { "actor_id": 1, "actor_type": "OrganizationAdmin", "bypass_mode": "always" },
    { "actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "always" }
  ]
}
JSON
```

Replace `bypass_actors` with the actual App/bot that executes
`promote-staging` (use `gh api repos/{owner}/{repo}/rulesets --method GET`
on an existing ruleset to discover IDs). Test on a non-production branch
first.

### Fallback when repository settings cannot be changed

If the GitHub API/UI cannot create the ruleset from this environment (no
admin scope, no `repo` ruleset permission), the repository remains with only
the `release-tags` ruleset. In that case:

- `promote-staging` still provides exact-SHA safety **when used** — it is the
  documented, auditable promotion path.
- Direct pushes to `main` remain possible but are outside the recommended
  path and should be treated as exceptions requiring manual audit
  (`git log --first-parent`, `gh run list --branch main`).
- Re-attempt ruleset creation when admin scope is available; no code change
  is needed — the workflow is already the sole writer.

### Required status checks on `v*` tags

Not applied. No workflow runs on tag pushes, so a required check on `v*`
tags would block `gh release create` entirely. The workflow-internal
verification is the effective gate.

## Recovery — When Promotion Fails

All recovery instructions are also embedded as comments and error messages in
`promote-staging.yml`.

| Failure | Cause | Recovery |
|---------|-------|----------|
| `No successful completed build.yml run found for SHA ...` | The exact SHA has no `success` build. | Re-run or wait for `build.yml` on that SHA, then retry: `gh workflow run build.yml --ref agent/staging` or push the SHA again to `agent/staging`. For dispatch, retry with the same `sha` after the build succeeds. |
| `origin/agent/staging ... != expected SHA` | `inputs.sha` does not match the current tip of `agent/staging`. | `git fetch origin agent/staging && git rev-parse origin/agent/staging` — retry with that SHA, or push the intended SHA to `agent/staging` first. |
| `origin/main ... is NOT an ancestor of ... (branch diverged)` | `main` advanced or `agent/staging` diverged. | `git fetch origin; git checkout agent/staging; git rebase origin/main; git push --force-with-lease origin agent/staging` — wait for `build.yml` on the new tip, then retry with the new SHA. |
| `422 Reference update failed` | `main` moved between fetch and `PATCH` (concurrent promotion). | Same rebase flow as above — fetch, rebase `agent/staging` onto new `origin/main`, re-qualify, retry. |
| `main already at expected SHA — nothing to promote` | No-op. | Success — no action needed. |

General recovery loop:

```bash
git fetch origin
git checkout agent/staging
git rebase origin/main
# resolve conflicts if any
git push --force-with-lease origin agent/staging
# wait for build.yml success on the new tip:
gh run list --workflow build.yml --commit $(git rev-parse HEAD) --json conclusion,status
# then promote that exact SHA:
gh workflow run promote-staging.yml --ref agent/staging -f sha=$(git rev-parse HEAD)
```

## Recommended GitHub UI settings (equivalent, if rulesets are edited by hand)

1. Settings → Rules → Rulesets: `release-tags` (active, tag target,
   `refs/tags/v*`): enable "Block deletions" and "Block force pushes".
2. Settings → Rules → Rulesets: `main-promotion-gate` (active, branch target,
   `refs/heads/main`): enable "Block deletions", "Block force pushes",
   "Require linear history" (optional), "Require status checks" (`build`,
   `verify-and-promote` / `promote-staging`), and add a **Bypass** for
   `github-actions[bot]` / the promotion App so `promote-staging` can
   fast-forward `main` without deadlocking.
3. Never add a required-status-check rule to tag rulesets unless a workflow
   that reports checks on tag pushes exists.
