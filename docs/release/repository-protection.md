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
dispatch-only Stage B `publish-release.yml` workflow — the ONLY workflow that
creates releases and `v*` tags (`gh release create`) — which re-verifies
artifact provenance, `SHA256SUMS.txt` consistency, the external evidence
record, and the Authenticode signature before publishing (see
`docs/release/publication-gates.md`; `release.yml` is RC-qualification-only
and has no publication path).

## Branch model — main only

The repository is **main-only**. `main` is the sole development and integration
branch. There is no `agent/staging` branch and no `promote-staging` workflow.

- All development, commits, and pushes happen against `main`.
- `main` is qualified directly on push by `build.yml` (exact-SHA hosted-CI
  gates: Release build, native/geometry/diagnostics/persistence self-tests,
  doctor/version/bundle privacy checks, OpenSpec validation, and the
  release-tooling regression suite).
- Pull requests targeting `main` are also qualified by `build.yml`.
- Future autonomous agents MUST NOT recreate `agent/staging` or any staging
  branch, and MUST NOT add a `promote-staging` (or similarly named) promotion
  workflow. `main` is authoritative; qualification occurs on the exact `main`
  SHA.

### Autonomous agents push directly to `main`

This repository intentionally uses autonomous agents that push validated
changes directly to `main`. A direct `git push origin HEAD:main` is the
expected, supported path:

- The canonical `build` workflow qualifies every pushed SHA on `main`.
- Direct pushes are visible in the audit log (`git log --first-parent`,
  `gh run list --branch main`).
- The exact-SHA qualification gate runs in `build.yml` on every push, so
  unqualified code does not silently ship.

## Branch Protection on `main` — Recommended Ruleset

### Why not yet enforced

This repository intentionally uses autonomous agents that push validated
changes directly. A required-status-check branch ruleset without a bypass
would deadlock that workflow (the push would be blocked before the check it
triggers could run). Until the ruleset below is enabled with a bypass, the
workflow-level exact-SHA gate in `build.yml` provides the qualification
property when the main-only path is used.

### Exact ruleset to enable (when GitHub settings can be modified)

Create a branch ruleset targeting `main` (or a classic branch protection
rule) with:

- **Require status checks to pass:** `build` (the `build.yml` workflow) must
  be `success`. `build.yml` already re-qualifies the exact SHA on push, so
  the required check is the authoritative gate.
- **Block force pushes** and **block deletions** on `main`.
- **Require linear history** (optional, recommended) — preserves a clean,
  auditable `main` history.
- **Do not require pull requests** — agents push directly to `main`; `main`
  is the integration point.
- **Bypass list (critical to avoid deadlock):** allow bypass for the actor
  that runs `build` (and, if used, the release publisher):
  - Ruleset bypass: add `github-actions[bot]` (the `GITHUB_TOKEN` actor) or
    the specific GitHub App to the ruleset's **Bypass actors** with `Bypass`
    permission, or
  - Classic protection: enable **Allow specified actors to bypass required
    pull requests / Allow GitHub Apps to bypass** and list the relevant app.
  Without this, the qualification workflow and release publisher could be
  blocked by the rule they are meant to satisfy.

Example `gh` creation (requires admin scope; adjust actor IDs for your org):

```bash
gh api repos/{owner}/{repo}/rulesets --method POST --input - <<'JSON'
{
  "name": "main-protection-gate",
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
          { "context": "build" }
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

Replace `bypass_actors` with the actual App/bot that executes the verification
workflows (use `gh api repos/{owner}/{repo}/rulesets --method GET` on an
existing ruleset to discover IDs). Test on a non-production branch first.

### Fallback when repository settings cannot be changed

If the GitHub API/UI cannot create the ruleset from this environment (no
admin scope, no `repo` ruleset permission), the repository remains with only
the `release-tags` ruleset. In that case:

- `build.yml` still provides exact-SHA qualification **when used** — it is the
  documented, auditable gate that runs on every push to `main`.
- Direct pushes to `main` remain possible and are the expected path; they are
  auditable via `git log --first-parent` and `gh run list --branch main`.
- Re-attempt ruleset creation when admin scope is available; no code change
  is needed — `build.yml` is already the sole push-time qualification gate.

### Required status checks on `v*` tags

Not applied. No workflow runs on tag pushes, so a required check on `v*`
tags would block `gh release create` entirely. The workflow-internal
verification is the effective gate.

## Recovery — When `main` Qualification Fails

All recovery instructions are embedded as comments and error messages in
`build.yml` and the release workflows.

| Failure | Cause | Recovery |
| --------- | ------- | ---------- |
| Build failure on push to `main` | The pushed SHA does not pass `build.yml` qualification (build, self-tests, OpenSpec, release-tooling suite, or privacy checks). | Fix the issue on `main` (commit + push); the corrected SHA is re-qualified automatically. For emergencies, amend/history-safe corrective commits are preferred over force-push. |
| `No successful completed build.yml run found for SHA ...` | The exact SHA has no `success` build. | Push again or re-run the build via the GitHub UI (Actions → build → Re-run) for that commit. |
| `422 Reference update failed` | A push raced while another push advanced `main`. | Fetch, rebase your local `main` onto `origin/main`, resolve conflicts, and push again. |

General development loop:

```bash
git fetch origin
git switch main
git pull --ff-only origin main
# make changes, then:
git commit -m "..."
git push origin main
# build.yml qualifies the pushed SHA on main
gh run list --workflow build.yml --branch main --json conclusion,status
```

## Recommended GitHub UI settings (equivalent, if rulesets are edited by hand)

1. Settings → Rules → Rulesets: `release-tags` (active, tag target,
   `refs/tags/v*`): enable "Block deletions" and "Block force pushes".
2. Settings → Rules → Rulesets: `main-protection-gate` (active, branch target,
   `refs/heads/main`): enable "Block deletions", "Block force pushes",
   "Require linear history" (optional), "Require status checks" (`build`), and
   add a **Bypass** for `github-actions[bot]` / the relevant App so the
   qualification and release workflows can run without deadlocking.
3. Never add a required-status-check rule to tag rulesets unless a workflow
   that reports checks on tag pushes exists.
