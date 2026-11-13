# Plan: evidence-backed frontend successor qualification

**Status:** locally complete; supervised/native release qualification pending
**Updated:** 2026-11-13

## Objective

Audit the landed interface overhaul against its actual WPF behavior, repair concrete UX/accessibility regressions, and strengthen executable render/interaction coverage without redesign churn.

## Scope and constraints

- Starting checkout: clean `main`, historical starting implementation `c0b28c7dc58cd4e782e6b6b388f456bed1d0838a`, matching origin/main. Resolve current Git dynamically.
- Preserve Shepherd/no-reparent, native content geometry, commands, stable automation IDs, storage safety, and the graphite/lavender identity.
- No unattended desktop input, UIA actions, production storage mutation, display changes, signing, or deployment. Managed offscreen controls and never-shown presentation sources are not physical qualification.
- Existing accessibility and capture specifications are the acceptance contract; conformance bug fixes need no speculative feature proposal.

## Initial assessment / priority

- No baseline P0 failure: audited Release restore/build, 1,004 unit tests, synthetic resource gate, native ABI, diagnostics/privacy, OpenSpec (39), self-contained publish/version all passed. Historical starting-SHA hosted build run 43271258605 succeeded.
- P1/P2: independently review actual capture keyboard/selection and minimum-size layout; existing source contracts/template fragments do not prove complete-screen layout or native input.
- P2: validate error/blocked/empty/long-content state containment and focus feedback using production XAML and repeatable managed tests; repair confirmed defects.
- External: supervised native capture/split/pop-out/keyboard, physical mixed-DPI, second-OS and signing remain qualifications, not reasons to weaken gates.
- Reject a second aesthetic overhaul, speculative architecture/dependency changes, and synthetic-to-physical evidence promotion.

## Steps

- [x] Establish Git/repository ground truth and inspect instructions, architecture, validation and canonical specs.
- [x] Run strongest safe baseline and check historical exact-SHA hosted CI.
- [x] Consult Refero: Loom no-results search screen `75d51a33-cdaf-430e-ab02-ec0101509617`; preserve explicit query context, distinguish no matches from no candidates, provide recovery rather than generic empty-state copy. Reference is inspiration, not product evidence.
- [x] Finish independent frontend review; reproduce concrete defects with red-capable managed tests.
- [x] Repair highest-impact confirmed issues and sibling paths; test and render actual WPF templates/layout.
- [x] Reassess reliability/security/test/documentation seams and execute justified successor fixes.
- [x] Run final Release/publish, driver deterministic and release-tooling gates; adversarially review final diff.
- [x] Record truthful scope, blockers and artifact index; prepare one logical verified milestone. Final SHA/push state comes from Git/session output, not a self-referential checkpoint.

## Evidence and decisions

Session evidence: workspace artifact root, `_AgentArtifacts/TabDock/sessions/20261113-pi-ux-successor/` (outside Git).

- `baseline-validation.log`, `baseline-resource/`: local Release gate PASS, 1,004 unit tests, no vulnerable packages reported by configured sources.
- Repowise CLI index reports current starting SHA; connected MCP has no Repowise tools, CLI text search provided weak results. Pi-lens graph is stale and C# LSP unavailable; verify exact source and use compiler/tests, not empty diagnostics claims.
- Refero MCP is connected and its Loom screen/image were actually inspected. Keep existing tokens; use task-specific patterns only.

## Completed campaigns

1. **Keyboard conformance:** launcher preview Enter intercepted a nested Open button's row-specific command; container-only fallback now preserves Enter/Space activation. Both capture lists forward focused-row Space to authoritative checked state, leaving checkbox input, modifiers and repeat handling independent of ListBox navigation selection. The shared attached handler owns no external resource.
2. **Minimum-size / recovery containment:** oversized auto chrome and decorative/duplicated empty content consumed results space. Compact inline search/utilities, client-layout height authority, denser launcher header, one launcher action set, and a footer selection count plus compact standalone empty state keep tested content/actions contained. Managed regressions went red before fixes; render review exposed the additional standalone clipping and drove another red/green cycle.
3. **Accessibility / visual consistency:** distinct enabled-control boundary token fixes unchecked contrast (>=3:1 in tested adjacent fills). Inline null-icon rows now use the existing shared fallback. Refero-informed empty headings distinguish no candidates from no matches; recovery guidance stays in view. No guest/native geometry authority, security gate, dependency or persistence behavior changed.
4. **Test/documentation hardening:** 25 new managed cases cover routing, selection/submission/feedback in both motion modes, complete inline rows with tab overflow, full client-layout states, empty-panel containment and control contrast. Updated stale frontend design documentation and documented the new test seam's limits.

## Final validation / adversarial reassessment

- Default `dotnet build TabDock.sln --no-restore`: 0 warnings/errors; Debug unit suite 1,029 passed.
- `scripts/validate.ps1 -Configuration Release -Ci -Publish`: PASS on implementation working tree; 1,029 Release unit tests, audited restore/no vulnerable packages reported by configured sources, all auxiliary builds, synthetic resource gate, native ABI, diagnostic/recovery/privacy smokes, 39 specs, self-contained publish/version. Re-run against final commit for exact-SHA process metadata; final result belongs in session output.
- ValidationDriver `--selftest all --configuration Release --rid none`: 205 passed; deterministic topology/manifest PASS, explicitly synthetic.
- Release-tooling suite: 179 passed, 0 failed.
- Reviewed native WPF-managed renders: launcher 604x460/884x600, picker 544x420/744x610 client sizes, container 420x300/980x640, empty/blocked/long-title cases and minimum inline tab overflow. Dark theme only (the product's existing supported palette); no mobile/web surface.
- Independent initial and final source reviews found no remaining actionable issue after repairs; final diff/whitespace reviewed. C# LSP remained unavailable/inconclusive; compiler/tests provide actual type/build evidence.
- Broader correctness/reliability/security/release seams: no P0 baseline failure or justified new backend/architecture/dependency change identified. Existing storage identity, recovery, native safety, privacy, ABI, resource and release-tooling gates were preserved and exercised. No performance optimization or physical-input/assistive-technology claim is made.
- Further aesthetic churn or unrelated rewrites were rejected for lack of evidence. Native guest capture/split/pop-out, physical keyboard/activation, screen-reader announcements, Windows 10/mixed-DPI and signing remain external qualification, not source fixes justified by synthetic evidence.

## Handoff

**Next action:** qualify the actual native workflows on an exclusively available supervised desktop at the reviewed commit; retain exact-SHA release evidence.
**Blockers:** exclusive supervision, required OS/DPI hardware cells and production signing materials/approval are not supplied by this session. No unattended input or display reconfiguration was attempted. No push/deployment is implied by local validation.
