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
dispatch-only `release.yml` workflow, which re-verifies artifact provenance
before creating a `v*` tag.

## Deliberately NOT applied

### Branch protection on `main`

Not applied. This repository intentionally uses autonomous agents that push
validated changes directly to `main`; a required-status-check branch ruleset
would deadlock that workflow (the push would be blocked before the check it
triggers could run). The production-safety equivalent is achieved instead by:

- the release workflow being the only path that creates `v*` releases,
- the release workflow re-verifying exact-SHA provenance and `PASS`
  qualification before publication,
- the `release-tags` ruleset making published tags immutable.

If a PR-based workflow is adopted later, the recommended main ruleset is:
required status check `build` (must pass), no force push, no deletion,
no direct pushes. Not needed today; document before enabling.

### Required status checks on `v*` tags

Not applied. No workflow runs on tag pushes, so a required check on `v*`
tags would block `gh release create` entirely. The workflow-internal
verification is the effective gate.

## Recommended GitHub UI settings (equivalent, if rulesets are edited by hand)

1. Settings → Rules → Rulesets: `release-tags` (active, tag target,
   `refs/tags/v*`): enable "Block deletions" and "Block force pushes".
2. Never add a required-status-check rule to tag rulesets unless a workflow
   that reports checks on tag pushes exists.
