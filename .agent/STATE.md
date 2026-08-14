# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session. This file never records a
self-referential current SHA or a hosted-CI result for the commit containing
this text.

## Current checkpoint — final production-readiness closure

- Objective: leave TabDock as a source-complete release candidate with only
  genuine supervised/physical qualification remaining; do not create a
  GitHub Release in this campaign.
- Repository-content status: the requested High and Medium source fixes are
  implemented locally, with deterministic coverage in the diagnostics suite.
  Final Release qualification, guarded console checks, and the two required
  real-input attempts are complete; commit/push and exact hosted-CI inspection
  remain in progress. The real-input attempts were blocked before input by the
  environment's verified-foreground preflight.
- Active plan/spec: `.agent/execplans/tabdock-deep-audit-remediation-2026-08-13.md`
  plus `openspec/changes/final-production-readiness-closure-2026-08-14/`.
- Current local evidence: Debug solution build passes; diagnostics self-test
  passes `181 checks / 0 failures`; `openspec validate --all --no-interactive`
  passes `17 / 17`.

## Closure decisions

- Close-group Yes snapshots immutable HWND/PID/thread/executable/class/
  process-start identity before release, then posts `WM_CLOSE` only after an
  independent released-target match. Destroyed/replaced targets are skipped;
  unverifiable evidence cancels fail-closed.
- Pending recovery keeps the source JSON byte-for-byte immutable while any
  sibling is unresolved. The `.recovered` sidecar is the logical ledger;
  source deletion occurs only after every entry has a durable resolution.
  Legacy rewritten files rebind only on a unique, provable fingerprint;
  ambiguity and foreign tokens remain fail-closed.
- Durable `NativeRecoveryComplete` reconciliation distinguishes exact match,
  destroyed, positive replacement, and unverifiable. Only exact matches may
  have the exact recovery token removed; replaced/destroyed targets receive
  disk-only completion and never native presentation work.
- Persistence salvages valid nested tabs/groups, ignores null/malformed
  records at record granularity, clamps active indexes, and preserves the
  existing unreadable/corrupt/future-state overwrite protection.
- Product mutation uses `Global\\TabDock-<canonical current-user SID>`;
  same-user sessions contend, different users do not, and identity failure
  is fail-closed. Read-only diagnostics remain independent.
- Supervised recovery uses one bounded Unicode-safe terminal sanitizer for
  all externally derived display fields. Sanitized `--support-bundle` and
  `--doctor` output are the primary shareable support artifacts; raw logs are
  explicitly sensitive.
- Shepherd/no-reparent, strong generation identity, journal-before-mutation,
  full placement/DWM restoration, bounded WinEvent handling, HDWP chaining,
  split z-order, and no destructive automated input remain invariants. HDWP
  retains the documented ordinary Win32 check-to-commit residual race.

## Validation and external qualification

- Completed local gates include the Release solution/harness builds,
  `scripts/validate.ps1 -Configuration Release -Ci -Publish`, OpenSpec,
  diff-check, isolated PowerShell/cmd parent-console qualification, and guarded
  attempts of `closegroupprompt`/`exitpopulated`.
- Do not automate shutdown/logoff. Do not claim mixed-DPI hardware,
  unavailable browser, or foreground-policy qualification without evidence.

## Resume

1. Read `AGENTS.md`, this file, the active execplan, `docs/ARCHITECTURE.md`,
   `docs/TESTING.md`, `README.md`, and the active closure OpenSpec artifacts.
2. Resolve Git dynamically and inspect the complete diff; preserve unrelated
   work and never reset/clean/force-push.
3. Finish validation, update OpenSpec task checkboxes and this checkpoint at
   milestones, then commit coherent closure work, fetch, push normally, and
   inspect the Actions run for the exact final SHA. Stop committing once that
   SHA is green.
