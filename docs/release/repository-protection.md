# Repository Protection Policy

## Current authority and verification

`main` is the sole integration and release authority. Temporary topic or
review branches target `main`; an explicitly authorized direct push is also
supported. This document does not authorize a commit, push, or settings change.
See [AGENTS.md](../../AGENTS.md).

The 2026-08-15 record reported an active `release-tags` ruleset (id 20878779)
blocking deletion and non-fast-forward updates to `refs/tags/v*`. That is
historical evidence, not proof of current enforcement. On 2026-09-15, read-only
queries for repository rulesets and classic `main` protection both returned
HTTP 403 with “Upgrade to GitHub Pro or make this repository public to enable
this feature.” Current protection settings could not be verified.

Recheck live settings before relying on them:

```powershell
gh api repos/{owner}/{repo}/rulesets
gh api repos/{owner}/{repo}/branches/main/protection
```

A failed query does not prove that a particular ruleset is enabled or absent.

## Repository-enforced qualification

- `.github/workflows/build.yml` runs on pushes to `main` and pull requests
  targeting `main`. Its `build` job runs Release qualification, including
  headless xUnit tests, resource/visual synthetic evidence, native ABI and
  diagnostic smokes, OpenSpec checks, and release-tooling regression tests.
- The separate `native-abi-evidence` job exercises the ABI contract on
  `windows-2022`. Both jobs must be assessed for the exact candidate SHA.
- Build jobs have read-only repository contents permission and do not push
  `main`. Running checks does not itself require a branch-rule bypass.
- A post-push check detects failures after integration. It does not prevent
  an unqualified commit from reaching `main`.
- Production publication is separately controlled by
  `prepare-release-candidate.yml` and `publish-release.yml`: immutable
  retained bytes, exact source/hash bindings, approved signing, external
  evidence, and trusted-policy verification. `qualify-candidate.yml` is
  qualification-only. See [publication gates](publication-gates.md).

## Settings guidance

When an administrator is authorized to configure protection:

1. Protect release tags against deletion and force updates.
2. Protect `main` against deletion and force updates. Choose the integration
   rule consistently with the authorized PR or direct-push workflow.
3. For required checks, select actual emitted job/check names and their trusted
   source. The current workflow defines `build` and `native-abi-evidence`;
   verify the names in live check results before configuring them.
4. If direct integration needs a bypass, scope it to the actor that actually
   updates the protected ref. Do not grant a broad bypass merely because an
   actor runs a read-only qualification job.
5. Keep any release-tag permission separate from branch integration authority.
   Do not invent actor IDs or copy an organization-admin bypass example.

GitHub documents [required checks and bypass rules](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets)
and [check-name matching](https://docs.github.com/en/enterprise-cloud%40latest/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/troubleshooting-rules).
Configuration is an administrative action; these instructions do not perform it.

If settings cannot be inspected or changed, record the limitation and retain
the exact-SHA workflow evidence. Do not describe workflow checks as enforced
branch protection.

## When qualification fails

| Evidence | Next action |
| --- | --- |
| Product/build/test failure | Make a corrective commit through the authorized integration path and qualify its exact SHA. Preserve shared history. |
| Runner/allocation failure before any step executes | Record the infrastructure failure; rerun the existing run when capacity is available. Do not infer a product PASS. |
| No successful completed run for the candidate SHA | Inspect that SHA's runs and obtain a successful qualification before release. A no-change push is not evidence of a new run. |
| Remote `main` advanced | Fetch and reconcile current changes without discarding work; validate the integrated result before an authorized push. |

Read-only status inspection:

```powershell
git rev-parse HEAD
git rev-parse origin/main
git status --short
gh run list --workflow build.yml --branch main --json headSha,conclusion,status,url
```

Match the reported `headSha` to the candidate. A green run for an earlier
commit does not qualify a later commit.
