# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session. This file never records a
self-referential current SHA or a hosted-CI result for the commit containing
this text.

## Current checkpoint — user-reported split interaction closure

- Objective: close the real-world three-tab split activation defect and the
  initial split presentation-settle defect without weakening the
  Shepherd/no-reparent, HWND identity, recovery, persistence, input, DPI, or
  shutdown contracts.
- Ordinary LEFT-click on a captured non-member tab while a pair is split is an
  explicit presentation switch: both split members are journal-safely hidden,
  split mode ends, the clicked tab becomes the ordinary active full-width
  guest, and no captured member is released. Hover/right-click on a non-member
  remains presentation-only and leaves the pair intact; Ctrl+Tab remains
  pair-scoped while split is active.
- The third-tab switch fails closed if hiding a split member becomes
  recovery-pending. The third tab is not selected and the authoritative pair is
  re-presented through the existing split layout path so a partial hide cannot
  leave a blank pane.
- Split creation from TabDock chrome now receives one bounded post-popup settle:
  after TabDock-owned chrome is no longer active, the existing split layout is
  re-run and the focused split member receives a real foreground request through
  the existing identity-checked Shepherd API. No synthetic `WM_SIZE`, guest
  style mutation, reparenting, `AttachThreadInput`, or browser-specific native
  workaround is used.
- Regression coverage is encoded in the existing ValidationDriver split
  scenarios: `split-click-third` now requires the clicked third tab to open,
  the historical `split-third-tab-click-persists` CLI alias repeatedly exercises
  split -> third-tab -> normal-tab recovery, `split-composite` uses the corrected
  contract, split entry asserts `SPLIT[settled]` plus real foreground, and the
  hover-persistence regression remains unchanged.
- Canonical and change-level OpenSpec sources describe the corrected behavior in
  `openspec/specs/ui-ux-hardening/spec.md` and
  `openspec/changes/split-tab-switch-and-settle-fix-2026-08-14/`.
- The completed performance optimization campaign remains the retained baseline:
  early desktop-reorder filtering, lower diagnostic allocation, bounded
  generation-safe picker icons, compile-qualified non-gating performance
  tooling, locked OpenSpec/npm tooling, and audited ordinary NuGet restore.

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

- Canonical repository qualification is
  `scripts/validate.ps1 -Configuration Release -Ci -Publish`, including audited
  restores, Release solution/ValidationDriver/GuineaPig/Performance builds,
  deterministic geometry/diagnostic/persistence/privacy checks, OpenSpec,
  recovery smoke, support-bundle privacy, publish, and exact build identity.
  Hosted Actions evidence is always resolved dynamically for the exact SHA; it
  is not persisted here.
- Because the two defects were observed in real interactive browser use, final
  qualification also requires an interactive Windows re-test of: three captured
  apps -> split two -> click the third -> normal switching, and split creation
  with a Chromium-family guest without a corrective first pane click.
- Do not automate shutdown/logoff. Do not claim mixed-DPI hardware,
  unavailable-browser, or foreground-policy qualification without evidence.

## Resume

1. Read `AGENTS.md`, this file, `docs/ARCHITECTURE.md`, `docs/TESTING.md`, the
   canonical `ui-ux-hardening` spec, and the active split-fix OpenSpec change.
2. Resolve Git and hosted CI dynamically. Preserve unrelated work and never
   reset/clean/force-push published `main`.
3. If the focused real-input split qualification exposes another concrete
   defect, unfreeze only that bounded behavior, add regression coverage, run
   canonical qualification, and verify exact-SHA hosted CI before re-freezing.
4. Do not create a state-only commit merely to record a SHA or CI run.
