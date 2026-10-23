# Plan: Review, correct, finalize and publish session work

**Status:** local implementation/validation complete; final publication is qualified by Git/hosted CI
**Updated:** 2026-10-24

## Objective

Audit all changes from the session baseline `df5785e` through `4d0ed7b`; correct implementation and overclaimed acceptance, finish the two open proposals, validate and push to main with exact-SHA hosted evidence.

## Scope and constraints

- Preserve existing work, native Shepherd identity/journal authority and storage isolation.
- Actual code/tests/ZIPs/render evidence outweigh earlier completion checkboxes.
- No unattended desktop input or physical release claims. Signing/Windows 10/mixed-DPI require external qualification.

## Steps

- [x] Audit motion, template geometry, lifecycle and test safety; correct defects and evidence claims.
- [x] Finish bounded, single-flight, frozen async picker/tab icons with consumer lifetime tests.
- [x] Capture immutable logical diagnostics on owner thread before background archive, with same-bundle status/issues and async hotkey.
- [x] Review diffs and actual renders; run focused tests and full Release CI/publish gate.
- [x] Sync/archive only fully evidenced proposals; reconcile records and independent review findings.
- Publication gate: commit, push main without force, verify exact-SHA hosted gate and clean Git state in session output. Do not make a state-only commit to embed its own publication.

## Evidence and decisions

- Start: clean main at `4d0ed7b`, equal origin/main. Hosted build 39999556313 succeeded for this SHA.
- Initial icon callback implementation was partial. The bounded shared pipeline and ownership corrections are now verified.
- [Correction review](../investigations/session-correction-review-2026-10-24.md): all independent review findings dispositioned; earlier archived test claims corrected.
- Final local Release CI/publish PASS, 998/998 units; reduced-motion focused 54/54; strict specs 39/39; actual picker template renders inspected at two widths/policies.

## Handoff

**Next action:** publish corrected commit and report its exact SHA/hosted CI; later sessions verify Git independently.
**Blockers:** none for local source/CI-safe validation.
