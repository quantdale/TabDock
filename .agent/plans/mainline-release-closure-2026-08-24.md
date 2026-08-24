# Plan: Mainline Release-Candidate Closure

**Status:** complete — internal closure achieved; external gates remain
**Owner/session:** Codex
**Updated:** 2026-08-24
**Integration branch:** `codex/mainline-release-closure-20260824`
**Starting source:** `origin/main` at `c2c480707f9e9fdfa753b4885f53bca270c775bc`

## Objective

Create one main-bound, reviewable integration lineage containing current
mainline planner/executor adapters plus the ship-readiness, native-interaction
determinism, and qualification-control-plane campaigns. Close completed
OpenSpec changes, reconcile repository state and governance documentation, run
the full deterministic qualification matrix, and produce fresh exact-candidate
evidence for the final integrated source. Do not merge to `main`, publish, or
invent product work outside this closure campaign.

## Authoritative topology

- Common base of `origin/main` and the campaign stack:
  `ba3115a138eed81e4a56c023aa3381f2c14a20cd`.
- `origin/main`: `c2c480707f9e9fdfa753b4885f53bca270c775bc`.
- Ship-readiness head: `eca8670759f9bc42aee58ec5f59b33fd0adab3f0`.
- Native-determinism head: `09a204d660e31d423f5e8ab1561655c32e097581`.
- Qualification-control-plane head: `b0101a3debb00311631c2a4e892da3a5bc73e933`.
- The campaign heads are strict descendants in the order ship-readiness →
  native-determinism → qualification-control-plane. Main has one independent
  adapter commit after the common base. A two-tip merge-tree preview is
  conflict-free; use a normal history-preserving merge.
- PR #12 is the original main-targeting draft review surface. GitHub CLI is
  currently unauthenticated; re-check public metadata and authenticated
  operations after the final push without mutating PR #12.

## Workstreams

1. **Integrate:** merge the latest qualification-control-plane ref into this
   branch from current `origin/main`; preserve campaign ancestry and all five
   mainline adapter files.
2. **Reconcile governance:** make `main` the sole integration/release
   authority while documenting temporary review branches as normal, finite
   review surfaces. Preserve canonical `AGENTS.md` ownership of generated
   harness adapters and run the synchronization mechanism when required.
3. **Close OpenSpec:** inspect proposal/design/tasks/delta specs for
   `release-candidate-hardening`, `native-interaction-determinism`, and
   `qualification-control-plane`; verify implementation and task completion,
   sync deltas into canonical specs through the OpenSpec workflow, archive in
   dependency order, and validate strictly.
4. **Audit seams:** review the merged diff and the product/native,
   persistence/recovery, ValidationDriver, and release trust-boundary seams.
   Change code only for a reproduced or deterministically proven defect, with
   focused regression coverage.
5. **Qualify:** run Debug/Release builds and tests, ValidationDriver
   deterministic self-tests, release-tooling tests, strict OpenSpec,
   canonical Release validation/publish qualification, parse/static checks,
   and full-diff review. Explain any count change or warning.
6. **Bind evidence:** after the last substantive source commit, generate a
   new qualification-only candidate for that exact SHA; verify the bundle,
   export and round-trip the independent package/report, and record every
   source/candidate/driver/bundle/package hash and schema.
7. **Hosted/external gates:** push the integration branch; create a new draft
   PR targeting `main` only when authentication permits; monitor exact-head
   CI. Keep physical, mixed-DPI, Windows 10/independent Windows 11, signing,
   and human-smoke gates BLOCKED when prerequisites are absent.

## Guardrails

- Never rebase, squash, force-push, reset, clean, or rewrite the historical
  campaign branches; do not merge to `main` or publish.
- Preserve Shepherd/no-reparent, `WindowShepherdService` authority,
  `ContentHost`, split ownership, identity gates, recovery ordering, and
  Stage-B data-only trust boundaries.
- Do not run guarded `SendInput` without a proven exclusive supervised desktop
  lease and provenance-safe foreground/topology state.
- Keep generated qualification artifacts, machine paths, secrets, and caches
  out of Git.
- Keep `.agent/STATE.md` concise and current; it must link this plan and never
  self-reference its containing commit.

## Acceptance gates

- Integrated source contains current main and all intended campaign history;
  clean semantic diff and no known internally reproducible Critical/High
  regression.
- Completed campaigns no longer appear active; canonical and archived
  OpenSpec validation is strict and green.
- Debug/Release builds and unit suites, ValidationDriver deterministic
  corpus, release-tooling, and canonical `scripts/validate.ps1
  -Configuration Release -Ci -Publish` are green with no unexplained warning
  or count reduction.
- Fresh exact-candidate, bundle, package, and deterministic returned-report
  lineage verifies offline and remains explicitly synthetic where applicable.
- Exact integration branch is pushed; exact-head hosted CI is green when
  GitHub is available, otherwise the authentication/infrastructure blocker is
  recorded precisely. Physical/external prerequisites remain honest BLOCKED.
- Final worktree is clean and local/remote heads match.

## Final handoff state

The normal merge checkpoint was `7ed8769`, followed by the reconciliation and
final handoff-state commit. The final exact source SHA must be resolved
dynamically from `HEAD`; it is the binding for the fresh candidate and all
qualification evidence retained under the ignored
`.artifacts/mainline-release-closure-<final-head>/` directory.

The complete deterministic matrix, strict OpenSpec validation, fresh
qualification-only candidate, offline bundle verification, portable package
verification, deterministic returned-machine round trip, and data-only report
import are green. The branch is pushed and local/remote heads match. A new
main-targeting draft PR and exact-head hosted pull-request CI are blocked only
by missing GitHub authentication in this environment; PR #12 remains open,
draft, and unchanged.

No internally reproducible Critical/High regression is known. Remaining work
is external qualification only: supervised physical repetitions, mixed-DPI
hardware, Windows 10/independent Windows 11 evidence, production signing, and
human smoke. Do not merge to `main`, publish, or start another development
campaign from this checkpoint.
