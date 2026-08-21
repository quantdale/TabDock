# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session (`git rev-parse HEAD`,
`git branch --show-current`, `git status`). This file never records a
self-referential current SHA or a hosted-CI result for the commit containing
this text.

## CURRENT STATUS

The August 21 audit remediation campaign (R21) is COMPLETE on the repository
side: all confirmed P0/P1 findings are fixed at root cause, regression
coverage is in place, the full automated validation matrix passes, and the
work is committed and pushed to `origin/main` (main-only; no force-push).
Remaining work is exclusively external: supervised live-desktop validation
(BLOCKED_SUPERVISED below) and the pre-existing BLOCKED_EXTERNAL release
evidence (signing credentials, human smoke, mixed-DPI hardware, Windows 10
x64 compatibility). Verdict stays GO FOR RELEASE CANDIDATE / BETA ONLY;
v1.0.0 remains PREPARED BUT INTENTIONALLY NOT PUBLISHED.

## WHAT WAS IMPLEMENTED (R21 campaign, 2026-08-21/22)

Canonical finding-by-finding disposition: `docs/audits/2026-08-21/DISPOSITION.md`
(raw reports archived in the same directory; `CODEBASE_AUDIT_v3.md` ==
`MUSE-RESULTS.md` == `sonnet-results.md`, byte-identical).

- R21-001/002/003 release trust boundary: dispatch inputs travel as env data
  (static test fails on `${{ inputs.` inside any run: block; adversarial
  value fixtures), Stage-B evidence rides verified-handoff with a fail-closed
  required-assets gate before `gh release create`, fused-variable/false-green
  suite bugs fixed under Set-StrictMode, signing-required boolean fixed,
  digicert-stm removed from RC choices, job-level actions:write for uploads,
  GITHUB_ENV appended not overwritten, sign-release failure status honest.
- R21-004 released-close identity: one-shot released-close nonce installed at
  capture (strongly-proven moment), survives release, consumed one-shot by
  `WindowIdentityGate.VerifyReleasedCloseTarget`; close-group Yes cannot
  WM_CLOSE a same-process recycled same-class HWND.
- R21-005 recovery generation identity: per-journal-generation
  `SourceInstanceId` GUID; exact-generation matching for new-format pending
  evidence; bounded legacy fingerprint fallback; .tmp sweep, retired-ledger
  compaction (<=64), unreadable-file skip, supervised abandon path.
  Unresolved/foreign evidence is never deleted.
- R21-006 hide provenance: `GuestHideProvenance` ledger — every shepherd
  SW_HIDE registers an expected-hide bound to the capture token; lifecycle
  consumes matching EVENT_OBJECT_HIDE before classification. Replaces
  active-tab inference, IsSuspendingSplitPair, and container-minimize
  expectation maps.
- R21-007/008 split authority: controller computes desired state via
  `SplitPresentationPolicy` and commits only after ALL guarded native work
  succeeds; DefinePair returns SplitTransitionResult; dormant-active
  non-member preserved on member removal (policy semantics); duplicate
  settle fields/coordinator instance/dead ExplicitExit+ClassifyInteraction
  removed.
- R21-009/010/011 capture/release identity: title removed from every
  identity axis (admission via WindowIdentityGate.EvaluateBeforeCaptureToken;
  picker target revalidation); membership rollback paths verified; release
  fallback restores OriginalPlacement.rcNormalPosition.
- R21-012 min-track/DPI: WM_GETMINMAXINFO composes max(XAML floor, guest
  minimum); split minima scale by the presentation monitor; per-monitor DPI
  cache invalidated on WM_DPICHANGED/WM_DISPLAYCHANGE.
- R21-014 multi-capture UX: one post-loop summary instead of per-failure
  owner-modals.
- R21-015 test honesty: WhenWritesSettledAsync barriers replace timing races;
  PersistenceSelfTest exceptions diagnosable; ValidationDriver Check demotes
  SKIP to FAIL; DPI self-skips record explicit ctx.Skip.
- R21-016 persistence hardening: empty-AppData fails closed; CommitJson
  recreates deleted state directory; volatile _lastSavedJson; emergency
  release writes ONE durable save per sweep.
- R21-017 raw-HWND caches: diagnostic suppression sets evicted on release;
  closed context menus leave tracking sets immediately; DeleteGroupRequested
  unsubscribed; rejected rename raises PropertyChanged.
- R21-018 responsiveness: picker probe failures contained; selection-only
  command requery; virtualization re-enabled; icon in-flight wait bounded.
- R21-019 diagnostics/privacy/logging: marker-based title redaction,
  expanded secret coverage, whole-token username redaction, exception-line
  retention, compact trace.jsonl, logging tail/cap/dispose hardening,
  collision-resistant non-overwriting bundles, ExportBundleAsync.
- R21-020 cleanup/docs: verified dead code removed (0-warning build),
  audit evidence archived under docs/audits/2026-08-21/, ARCHITECTURE/
  TESTING/README/repository-protection reconciled, OpenSpec changes
  startup-group-visibility and production-release-v1-0-0-closure archived
  via the canonical CLI (external-blocker items left unchecked).

## WHAT REMAINS

- Supervised live-desktop acceptance (BLOCKED_SUPERVISED below).
- External release gates (unchanged): production signing credentials, human
  final smoke, physical mixed-DPI qualification, Windows 10 x64
  compatibility — all BLOCKED_EXTERNAL per docs/release/*.
- Deferred-by-design items listed in DISPOSITION.md (stale-reorder cosmetic
  race, budget-sink production wiring, correlation IDs, harness scope).

## VALIDATION RESULTS (2026-08-22, this campaign)

- `dotnet build TabDock.sln -c Debug`: 0 errors, 0 warnings.
- `dotnet build TabDock.sln -c Release`: 0 errors, 0 warnings.
- `dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Debug`: 182 passed / 0 failed.
- `dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Release`: 182 passed / 0 failed.
- `pwsh -NoProfile -File scripts/release-tooling-tests.ps1`: 150 passed / 0 failed.
- `pwsh -NoProfile -File scripts/validate.ps1 -Configuration Release -Ci`: PASS (see git-hosted CI for the exact-SHA run).
- `pwsh -NoProfile -File scripts/validate.ps1 -Configuration Release -Ci -Publish`: PASS.
- `tools/openspec/.../openspec validate --all --no-interactive`: 20 passed / 0 failed.
- `git diff --check`: clean.
- Historical counts elsewhere in this file are dated snapshots, not current truth.

## SUPERVISED TESTS STILL REQUIRED

BLOCKED_SUPERVISED — real SendInput scenarios need an interactive Windows
desktop with no mouse/keyboard input during runs; they were NOT executed in
this environment and must not be reported as passed. Run:

    dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -c Release -- --yes --configuration Release all

Required live coverage: rapid Ctrl+Tab / A→B→A round-trips; split A|B → C/D →
resume; recycled/same-process HWND behavior; multi-capture partial failures;
maximize/minimize/release restoration; mixed-DPI split; guest self-hide/tray
close; container minimize/restore; hard-kill + relaunch recovery; lifecycle
torture; browser title churn during capture; z-order churn during Alt-Tab and
direct clicks; >=1000 SaveAsync stress with the synchronous barrier.

## LAST KNOWN GOOD COMMIT

Resolve dynamically: `git rev-parse origin/main`. The campaign's final state
is the tip of `origin/main` after the R21 push; hosted CI qualifies every
pushed SHA via build.yml.

## NEXT AGENT INSTRUCTIONS

1. Read AGENTS.md, this file, docs/audits/2026-08-21/DISPOSITION.md, and
   docs/ARCHITECTURE.md.
2. Resolve Git and CI state dynamically; never reset/clean/force-push; the
   repo is main-only.
3. The open work is exactly WHAT REMAINS above; do not reopen dispositioned
   findings without new reproduced evidence.
4. Do not create a state-only commit merely to record a SHA or CI run.

## Resume pointers (historical context)

- Two-stage exact-byte release chain and its threat model:
  docs/release/publication-gates.md, code-signing.md, repository-protection.md.
- Runtime stabilization history (background writer, journal dedupe,
  ensureFinalPass latch, relative z-order): earlier sections of this file and
  docs/runtime-stabilization-2026-08.md.
- The ValidationDriver/GuineaPig/Performance projects live outside
  TabDock.sln; build them explicitly (validate.ps1 does).
