# Plan: Independently re-review A02 S1–S6 corrections

**Status:** complete — changes requested; no implementation edits
**Updated:** 2026-10-21

## Objective

Verify the latest uncommitted correction implementation against the six findings in `.agent/investigations/validation-safety-review-2026-10-21.md`, including residual cleanup/proof/process failure paths. Review only; do not fix source or change task acceptance checkboxes.

## Scope and constraints

- Preserve all dirty motion/configuration/product work; no commits, pushes, reset, clean, archives or unrelated implementation.
- Git authority: HEAD/main/origin/main dynamically verified at `eafe922d925b54001d35886f3807e1312cdb16cd` at review start.
- Use only disposable fake production/run roots and safe child programs for destructive/failure probes. No real UI, native recovery, desktop input or process soak.
- Compare current actual source and maintained fixtures with the prior review/snapshot, not just completion claims. No full process/release gates until safety boundaries are established.

## Steps

- [x] Inspect Git/state, prior requirements, current resolver, launcher/proof/cleanup source and maintained tests.
- [x] Fingerprint reviewed inputs; run fresh builds and focused Debug/Release tests plus inspected script/driver safety suites.
- [x] Probe residual marker/path-replacement/missing-root, exit-status and inherited-pipe cases using actual methods and owned fixtures.
- [x] Report standards/spec findings or explicitly bounded approval; retain correction guidance and update checkpoint. Verify source remains unchanged.

## Evidence and decisions

- Current source appears to address ordinary alias/blank, marker-field, proof-shape and global-test-state cases. Unexpected-load tests now await settlement, and resource run ID is captured after BeginRun.
- Reproduced four residual findings: R1/P1 both cleanup authorities follow a replaced ancestor junction and delete relocated evidence; R2/P2 missing expected root skips proof and deletes the parent (actual compiled soak method and PS authority); R3/P2 driver accepts exit-4 child with valid proof; R4/P2 PS inherited-pipe drain returns after 4154 ms against 300 ms budget.
- Fresh validation: focused unit Debug/Release 136/136 each, driver storage 25/25, PS safety 20/20; strict A02 valid; forced Release product rebuild 0 warnings/errors. No full process/UI gates rerun after safety defects reproduced.
- All 48 reviewed input hashes unchanged. Report: `.agent/investigations/validation-safety-rereview-2026-10-21.md`; follow-up prompt: `.agent/plans/validation-safety-rereview-followup-2026-10-21.md`.
- Evidence root: ignored `artifacts/validation-safety-rereview-2026-10-21/` with logs/TRX/source hashes and a probe/safety-source archive. All destructive paths and fake processes review-owned; no fake probe/holder remained running.

## Handoff

**Next action:** Implementation session repairs R1–R4 and adds maintained failure-path coverage, then requalifies dependent gates under the corrected boundary. Preserve successful original corrections.
**Blockers:** Review finished; A02 unconditional acceptance remains blocked by reproduced cleanup/proof/deadline failures. No fixes, task checkbox changes, commits or pushes by this review.
