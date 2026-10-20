# Plan: Review applied OpenSpec work and prepare a follow-up prompt

**Status:** complete — review only; correction/follow-up handed off
**Updated:** 2026-10-20

## Objective

Review all actually applied audit-remediation changes from the separate session against their specifications and repository standards, then provide a correction/follow-up prompt. Review only; do not implement fixes or mark pending tasks complete.

## Scope and constraints

- Git baseline: main/HEAD/origin-main at eafe922d925b54001d35886f3807e1312cdb16cd. Implementation is uncommitted; include relevant new source/test files explicitly, not only tracked diffs.
- Current application evidence identifies A01 and A04 as applied (8/9 each); the other nine changes have no completed tasks and are not implementations to approve.
- Exclude pre-existing motion/UI/configuration work from this review, except as a preserved worktree prerequisite.
- Use actual code and fresh targeted, explicit-fixture tests. Do not run the full suite, process/ZIP gate, real-input driver or recovery commands while A02/A10 are absent.
- Preserve all source, existing task states and other-session changes. Record snapshot hashes and recheck them before reporting.

## Steps

- [x] Reconcile current Git, all change task counts and claimed implementation scope.
- [x] Read A01/A04 source/tests/specs and assess standards and scope separately.
- [x] Build the solution and run the safe focused persistence/diagnostic classification tests.
- [x] Reproduce strongest remaining findings with disposable actual-code probes; distinguish bugs from missing validation.
- [x] Write a prioritized review and ready-to-use correction/follow-up prompt; recheck input stability and stop without implementing.

## Evidence and decisions

- Fresh solution build: 0 warnings/errors. Focused persistence plus diagnostic classification tests: Debug 80/80, Release 80/80.
- A01/A04 each retain one explicitly deferred full-gate task; no isolated/full release PASS is claimed. Other nine changes still have zero completed tasks.
- Confirmed R1/P2: future-schema doctor status becomes corrupt if Groups is missing/null/object, while restore correctly remains protected. R2/P3: A04 retry regression constructs different Guid-bearing snapshots; current actual identical-object retry works.
- Unexpected-load protection passed an isolated fault probe but needs maintained direct regression coverage; no new production defect was claimed for it.
- Report: `.agent/investigations/openspec-apply-review-2026-10-20.md`. Ready-to-use prompt: `.agent/plans/openspec-apply-followup-2026-10-20.md`.
- Review inputs are fingerprinted and unchanged; ignored evidence/probe archive is under `artifacts/openspec-apply-review-2026-10-20/`. No source, task-checkbox, commit or push changes.

## Handoff

**Next action:** Implementation session follows the correction prompt, then stages A10/A02 before deferred full qualification.
**Blockers:** None for completed review; A02/A10 still gate full qualification.
