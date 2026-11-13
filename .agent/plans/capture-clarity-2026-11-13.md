# Plan: capture selection clarity and mainline integration

**Status:** local implementation verified; publication receipt is resolved from Git/session output
**Updated:** 2026-11-13

## Objective

Improve the existing capture workflow's state transparency and keyboard destination feedback, then integrate both verified UX commits on main and push normally under the user's explicit authorization.

## Ground truth / constraints

- Starting clean topic branch `ux/capture-keyboard-layout` at historical commit `4cc4c1c`; origin/main `c0b28c7` after fetch. Preserve the prior verified local commit and unrelated worktree.
- Baseline Release unit suite: 1,029 passed. Prior full Release/publish and native-safe gates passed at 4cc4c1c; repeat final gate.
- Keep checked candidates distinct from list navigation, preserve hidden selections and final native admission, retain all existing IDs and the graphite/lavender identity.
- No unattended physical input, UIA actions, production state mutation, display changes or signing. Use managed templates/fixtures; do not promote synthetic results to native qualification.

## Evidence-backed improvements

1. **Hidden selection disclosure:** search intentionally hides checked candidates but the current summary/help does not disclose how many submitted windows are out of view. Add concise hidden counts and explicit primary-action help; update on filtering, bulk selection/clear and identity-safe refresh without changing targets.
2. **Destination keyboard highlight:** the shared ComboBoxItem template suppresses the system focus style without replacing unselected keyboard-highlight feedback. Provide visible highlighted/focused boundaries that coexist with selected feedback without changing geometry.
3. Preserve minimum-size containment and expose full truncated candidate metadata through tooltips where appropriate; avoid unrelated restyling.

Refero search and Loom no-results screen `75d51a33-cdaf-430e-ab02-ec0101509617` reinforce explicit search context and distinct recovery/state hierarchy. Keep established tokens; no branding or layout copying. Hidden-target disclosure is grounded in repository behavior, not claimed as a Refero finding.

## Steps

- [x] Reconcile Git/instructions/current specs; baseline and Refero research.
- [x] Add failing managed regressions for hidden-selection summary and destination highlight.
- [x] Implement focused fixes, update current acceptance specifications and docs.
- [x] Render minimum/default states and selected/hidden/focus cases; verify ordinary flows unchanged.
- [x] Independent review and final Release/publish, Debug, deterministic/release-tooling checks.
- [x] Prepare the verified milestone for authorized normal main integration. Actual final SHA, push and exact-SHA hosted result are recorded in session output/artifacts, not a self-referential state-only commit.

## Evidence

Outside-Git session root: `_AgentArtifacts/TabDock/sessions/20261113-pi-capture-clarity/`.

- `clarity-red.log`: reproduced missing hidden disclosure and unselected destination focus before fixing them; `clarity-layout.log`: focused suite 50 passed with managed renders.
- `debug-build.log`, `debug-tests.log`: zero warnings/errors; all 1,037 Debug tests passed.
- `release-validation.log`: full audited Release/publish gate passed, 1,037 tests, synthetic resources, ABI, diagnostics/recovery/privacy, 39 specs, self-contained publish/version. No dependency vulnerabilities reported by configured sources.
- `release-tooling.log`: 179 passed; `driver-selftests.log`, `deterministic/`: 205 self-tests plus synthetic topology/manifest PASS.
- `renders/`: reviewed hidden-selection disclosure at 544x420/744x610 standalone and 420x300/980x640 inline client sizes, plus keyboard destination feedback in both motion modes and existing state fixtures. No native popup or desktop input used.
- `independent-review.md`: no actionable finding in bounded read-only source review. C# LSP inconclusive; compiler/tests provide actual validation.
- Eight additional cases protect hidden count/help notifications, bulk clear, identity-safe refresh removal, minimum/default clipping, and selected-versus-navigation feedback in both motion modes. Full titles/paths remain inspectable through bound tooltips. Existing AutomationIds, target set, native admission and storage remain unchanged.

## Handoff

Resolve the final branch/HEAD/remote and exact-SHA hosted build independently using Git/GitHub; user explicitly authorized push to main. Do not create another commit merely to record publication. Physical keyboard, screen-reader, actual popup/native capture/split/pop-out, Windows 10/mixed-DPI and signing qualification remain external; managed fixtures are not certification of those workflows.
