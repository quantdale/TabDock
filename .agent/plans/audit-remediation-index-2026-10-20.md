# Audit remediation: OpenSpec change index

**Status:** A01, A02, A04, and A10 implemented, reviewed, synced, and archived; A03, A05–A09, and A11 remain active proposals
**Updated:** 2026-10-21
**Source:** [current-tree audit, A01–A11](../investigations/repository-audit-2026-10-20.md)
**Planning record:** [proposal-generation plan](audit-openspec-proposals-2026-10-20.md)

All 11 changes use the configured `spec-driven` schema. A01, A02, A04, and A10 are complete and archived; their requirements are now in the main specs. A03, A05–A09, and A11 remain active proposals and are not implemented.

## Separate changes

| Finding | Priority | Change and location | Tasks | Dependencies / coordination |
| --- | --- | --- | --- | --- |
| A01 | High | [fix-malformed-state-version-recovery](../../openspec/changes/archive/2026-10-21-fix-malformed-state-version-recovery/) | 9/9 archived | First data-safety repair; explicit-path tests can run independently |
| A02 | High | [isolate-validation-storage-roots](../../openspec/changes/archive/2026-10-21-isolate-validation-storage-roots/) | 16/16 archived | Safety pair with A10 before full process/ZIP qualification |
| A03 | High | [fix-motion-subscription-lifecycle](../../openspec/changes/fix-motion-subscription-lifecycle/) | 10 | Requires the current uncommitted motion baseline; foundation for A06/A07/A08/A09 |
| A04 | Medium | [fix-persistence-latest-request-wins](../../openspec/changes/archive/2026-10-21-fix-persistence-latest-request-wins/) | 9/9 archived | Apply after A01; shared persistence safety boundaries |
| A05 | Medium | [fix-dispatcher-safe-diagnostic-snapshots](../../openspec/changes/fix-dispatcher-safe-diagnostic-snapshots/) | 10 | Independent fixture-based development; ZIP/CLI qualification after A02/A10 |
| A06 | Medium | [fix-motion-color-state-restoration](../../openspec/changes/fix-motion-color-state-restoration/) | 7 | After A03; establish base-token correctness before A07 |
| A07 | Medium | [preserve-motion-brush-state-authority](../../openspec/changes/preserve-motion-brush-state-authority/) | 10 | After A03/A06; shared template/rendering authority for A08 |
| A08 | Medium | [fix-capture-selection-highlighting](../../openspec/changes/fix-capture-selection-highlighting/) | 8 | After A07 and its prerequisites; picker model-to-visual mapping only |
| A09 | Low | [fix-button-feedback-template-timing](../../openspec/changes/fix-button-feedback-template-timing/) | 8 | After A03; verify incidental fixes rather than force redundant code changes |
| A10 | Low | [keep-recovery-discovery-read-only](../../openspec/changes/archive/2026-10-21-keep-recovery-discovery-read-only/) | 10/10 archived | Independent direct-directory regressions; combine with A02 for complete validation safety |
| A11 | Medium | [keep-tab-icons-off-ui-thread](../../openspec/changes/keep-tab-icons-off-ui-thread/) | 11 | Independent icon/membership seam; preserve UI lifetime and existing cache contracts |

## Recommended execution order

1. Repair **A01** with explicit-path tests to prevent malformed metadata from erasing the usable backup during cleanup.
2. Implement the **A10/A02 safety pair**, using direct-directory/non-mutating probe tests first. Their code work is independently stageable; they are a shared validation prerequisite, not a circular implementation dependency. Run full process/ZIP gates only when both are present.
3. Establish **A03** against the existing dirty motion implementation. Do not assume MotionProfile is present in HEAD, discard that work, or rebuild it from this proposal.
4. Finish persistence **A04**, diagnostics **A05**, and async tab icons **A11** with their focused regressions.
5. Follow the motion state chain **A03 → A06 → A07 → A08**. **A09** can follow A03 independently of that color/picker chain.
6. After the safety pair, run integrated full unit and Release CI/publish validation. Native interaction, visual/manual smoke, mixed-DPI, Windows 10 and signing remain separate qualifications with their existing safety policies.

## Shared-file and validation rules

- These are separate changes by defect, not separate concurrent writing assignments. MotionProfile, App/shared XAML, picker templates, persistence and diagnostic paths overlap; use one writer per working tree and rebase/reconcile each dependent delta against current source.
- Prerequisites can incidentally resolve a later symptom. Retest first, then add missing regression evidence or residual repairs; do not introduce gratuitous implementation changes solely to make every proposal contain a code patch.
- A01 replaces one full existing persistence requirement and retains its original scenarios. Other pending deltas append uniquely named requirements, so separate archival must not overwrite another change's requirements.
- Until A02/A10 land, do not rerun full suites or the canonical gate merely because an old APPDATA-isolated smoke was green. Use deliberately selected tests with explicit fixture paths and no product-profile inspection. When a gate is deferred, record the deferral rather than claiming PASS.
- Proposed A02 bootstrap inputs/probe are qualification-only and fail closed; default production storage and the SID-scoped product-mutation lease are preserved. No normal UI soak is authorized by these planning artifacts.
- Preserve the Shepherd/no-reparent architecture, existing native identity/journal authority, AutomationIds, and the user's uncommitted source/configuration edits. Do not hand-edit canonical main specs during implementation; use the OpenSpec sync/archive workflow after acceptance.

## Validation of the planning set

- Each change passes the repository-pinned `openspec validate <change> --strict --json`, with no issues reported for the new changes.
- `openspec validate --all --strict --no-interactive --json`: **49/49 valid** (38 existing main specs plus 11 changes). Existing main specs retain informational long-requirement notices; no errors/warnings block validation.
- CLI status and a separate dependency-closure check confirm all four required artifact groups exist for every change. `done` here means planning files exist, **not** that any implementation task is complete.
- Proposal capability declarations match all 15 delta files; modified requirement headers exist in main; added requirement names do not collide with main or another pending change.
- Application/test/tooling source hashes match the audit checkpoint. `git diff -- openspec/specs` is empty, and `git diff --check` passes apart from existing line-ending normalization notices.
- Machine-local CLI instruction/status/validation evidence is ignored under `artifacts/audit-openspec-proposals-2026-10-20/`; do not commit it.

Application builds/tests were not rerun for this planning-only request. The earlier audit's passing baselines remain historical evidence, not proof that these proposed fixes are implemented.

## Deliberately excluded follow-ups

The audit's optional icon-cache eviction and live Windows reduced-motion preference refresh ideas are not presented as confirmed-defect changes. Cache eviction would alter an explicit existing lifetime-cache contract; either follow-up deserves its own product-scope decision. All confirmed defects and their essential regression/test gaps are covered above.

## Handoff

A01, A02, A04, and A10 are archived under `openspec/changes/archive/2026-10-21-*` after their specs were synced. A03, A05–A09, and A11 remain active, unimplemented proposals. The motion/UI baseline is included with this publication; do not treat it as completion of those proposals.
