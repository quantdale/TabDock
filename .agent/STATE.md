# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session. This file never records a
self-referential current SHA or a hosted-CI result for the commit containing
this text.

## Current checkpoint — performance optimization campaign

- Objective: complete the measurement-first performance, efficiency, and
  maintainability pass without weakening the Shepherd/no-reparent, HWND
  identity, recovery, persistence, input, DPI, or shutdown contracts.
- Repository-content status: early desktop-reorder filtering, one-copy
  diagnostic metadata, post-Loaded HWND reuse, bounded generation-safe picker
  icon resolution, duplicate-build removal, locked OpenSpec/npm tooling, and
  NuGet lock mode are implemented. No speculative logger, persistence,
  min-track, z-order, or large-class refactor was retained.
- Active plan/spec: `.agent/plans/tabdock-performance-optimization-2026-08-14.md`
  plus `openspec/changes/performance-optimization-2026-08-14/`.
- Campaign record: `docs/internal/perf-2026-08-14.md`.
- Current local evidence: final Release validation passes with `193 checks / 0
  failures`, OpenSpec `18 passed / 0 failed`, locked audited restores,
  publish, recovery/support-bundle smoke, and published `--version`. The
  non-gating performance harness has trace, logger, picker, persistence, and
  build/tool timing JSON evidence. Commit/push/hosted-CI status remains
  dynamic and is never persisted as a claim about the commit containing this
  file.

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
  same-user sessions contend, different users do not, and identity, ACL, or
  unexpected-object failure is fail-closed. The protected DACL grants only
  the current SID the required wait/release/read-permissions rights; read-only
  diagnostics remain independent.
- A uniquely provable legacy rewritten-source transaction is rebound in the
  existing durable record even when it is already `TokenRemoved`. One recovery
  token therefore has one ownership record; ambiguity and foreign tokens stay
  untouched, and completed cleanup can retire/delete the source idempotently.
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

1. Read `AGENTS.md`, this file, the active plan, `docs/ARCHITECTURE.md`,
   `docs/TESTING.md`, `README.md`, and the performance OpenSpec artifacts.
2. Resolve Git dynamically and inspect the bounded diff; preserve unrelated
   work and never reset/clean/force-push.
3. If source edits remain, review the final diff, make one coherent commit
   only because the campaign explicitly requests it, push safely, and query
   hosted CI for the exact resulting SHA. Do not create a state-only commit
   merely to record post-CI evidence.
