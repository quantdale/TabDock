# Plan: Separate OpenSpec proposals for the repository audit

**Status:** complete — planning only
**Updated:** 2026-10-20

## Objective

Create a separate spec-driven OpenSpec change for each of audit findings A01–A11, with all proposal/design/delta-spec/task artifacts ready for review. Planning only; no application or test implementation, commits, or pushes.

## Scope and constraints

- Source: `.agent/investigations/repository-audit-2026-10-20.md`.
- Preserve existing dirty application/motion/test/configuration files and existing audit records.
- Use the repository-pinned OpenSpec 1.8.0 CLI, configured default schema, and CLI-resolved paths/instructions. Do not regenerate skills or edit canonical capability specs.
- Keep findings separate; explicitly record shared-file dependencies, validation safety prerequisites, and the current uncommitted motion prerequisite.
- Optional cache eviction and live reduced-motion preference refresh remain follow-up opportunities, not extra confirmed-defect proposals.

## Steps

- [x] Reconcile Git/current audit and confirm no active OpenSpec changes exist.
- [x] Read relevant main specs; scaffold A01–A11 changes and inspect artifact instructions.
- [x] Write and re-read proposal artifacts; check status before dependent artifacts.
- [x] Write and re-read design/delta-spec artifacts with measurable acceptance scenarios.
- [x] Write actionable unchecked task lists; verify every transitive required artifact exists.
- [x] Validate every change strictly, check the complete spec/change set, and create an indexed handoff.
- [x] Verify product/test/tooling source unchanged and update checkpoint; stop before implementation.

## Evidence and decisions

- Initial Git: `main` at eafe922d925b54001d35886f3807e1312cdb16cd, equal to origin/main; existing dirty files preserved.
- Initial `openspec list --json` reported zero active changes; the nearest local OpenSpec root is this repository. No registered store was requested or selected.
- Handoff/index: `.agent/plans/audit-remediation-index-2026-10-20.md` maps all 11 findings to separate changes, code/validation prerequisites and the recommended order.
- All 11 changes have proposal/design/specs/tasks ready: 15 delta-spec files and 108 unchecked implementation tasks. No conditional artifact was skipped.
- Each change passes strict validation with no reported issues; full strict validation is 49/49 valid (38 main specs plus 11 changes). Artifact dependency closure, capability coverage, noncolliding requirement names and unchecked task counts were independently checked.
- Product/test/tooling hashes remain unchanged, canonical main specs are untouched, and `git diff --check` passes. No application tests/builds were rerun for planning-only changes.

## Handoff

**Next action:** User reviews the index and explicitly selects `/opsx-apply <change-name>`; do not begin implementation automatically.
**Blockers:** None for planning. A02/A10 are prerequisites for integrated process/ZIP validation; motion changes require the existing uncommitted motion baseline.
