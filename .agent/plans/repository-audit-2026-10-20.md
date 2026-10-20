# Plan: Evidence-led repository audit

**Status:** complete — investigation only
**Updated:** 2026-10-20

## Objective

Find bugs, errors, performance gaps, and actionable improvements in the actual current repository. Investigation and reporting only; no implementation or commits.

## Scope and constraints

- Audit committed application/services, current uncommitted motion/UI work, tests, and build/release tooling.
- Baseline: main at eafe922d925b54001d35886f3807e1312cdb16cd, equal to origin/main; pre-existing dirty files remain untouched.
- Treat docs/history as leads, not proof. Read source and run fresh validation/probes.
- No unattended real input, mutations of user state, recovery operations, or external-window manipulation. Reproductions use temp directories and never-shown, process-owned WPF objects.
- Only audit records and checkpoint updates are added to the repository.

## Steps

- [x] Establish current source/build/test baseline and map critical ownership paths.
- [x] Audit motion/UI changes with runtime probes for suspected regressions.
- [x] Audit capture/release, identity/recovery, persistence/concurrency, diagnostics/privacy, and lifecycle/performance paths.
- [x] Audit validation/release coverage and run applicable CI-safe gates.
- [x] Reproduce strongest findings, prioritize with exact file/line references, and distinguish confirmed failures from risks and qualification limits.
- [x] Finalize durable investigation report; verify implementation files unchanged by audit and update checkpoint.

## Evidence and decisions

- Repowise MCP tools/resources unavailable in this session; use source reads, Git, local code-intelligence tools where available, and focused searches.
- Full desktop scenario certification is outside safe unattended scope; no physical PASS will be inferred from synthetic/unit tests.
- Report: `.agent/investigations/repository-audit-2026-10-20.md` — 11 defects (3 high, 6 medium, 2 low), separate improvement opportunities, and evidence/limits.
- Fresh gates: Debug/Release builds clean; unit tests 847/847 in each configuration; Release CI/publish gate pass; OpenSpec 38/38; release-tooling 179/179; driver deterministic 173/173. Targeted actual-code probes reproduce 19 failed assertions in normal and reduced-motion modes; separate child probe proves APPDATA isolation failure.
- Only audit records/checkpoint edited; application/test/tooling source hashes remain unchanged. No commits, fixes, or real-input scenarios.

## Handoff

**Next action:** User chooses a remediation scope; implementation remains deferred. Prioritize state/backup loss, test isolation, and motion lifetime ownership.
**Blockers:** None for the completed audit. Physical interaction/hardware certification was not claimed.
