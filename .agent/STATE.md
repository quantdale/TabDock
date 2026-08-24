# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session. This file never records a
self-referential SHA or treats an old CI run as evidence for the commit that
contains this text.

## CURRENT CAMPAIGN — MAINLINE RELEASE-CANDIDATE CLOSURE

**Objective:** consolidate current mainline with the completed ship-readiness,
native-interaction-determinism, and qualification-control-plane campaigns;
remove branch/specification/release-state drift; and leave one exact,
deterministically qualified integration lineage ready for remaining external
gates.

**Plan:** `.agent/plans/mainline-release-closure-2026-08-24.md`

**Status:** integration and repository reconciliation are complete at the
current checkpoint. The next final handoff step is to qualify the final
committed integration SHA and bind a fresh candidate/bundle/package lineage;
the historical candidate for `159d094...` is diagnostic only and must not be
reused.

### Authoritative topology

- Session start `origin/main` was
  `c2c480707f9e9fdfa753b4885f53bca270c775bc`.
- The common base of main and the campaign stack was
  `ba3115a138eed81e4a56c023aa3381f2c14a20cd`.
- The normal history-preserving merge checkpoint was `7ed8769` (main plus
  `origin/codex/qualification-control-plane-20260824`); resolve the current
  `HEAD` dynamically after the reconciliation commit.
- The campaign stack remains historically traceable through
  ship-readiness `eca8670...`, native determinism `09a204d...`, and
  qualification control `b0101a3...`. No historical campaign branch was
  rebased, force-pushed, rewritten, or merged into `main`.
- The integration branch is `codex/mainline-release-closure-20260824` and
  targets `main`. Main’s planner/executor adapter files are present, and
  `scripts/sync-agent-configs.ps1` completed with no generated-copy drift.

### Completed reconciliation

- `main` is documented as the sole integration/release authority. Temporary
  topic/development/draft-review branches are allowed as finite provenance and
  review surfaces; no permanent staging hierarchy or promotion workflow is
  allowed.
- Canonical specs now contain the final hardening, native-interaction,
  qualification-control-plane, topology, and release-evidence behavior.
  Completed changes were archived under
  `openspec/changes/archive/2026-08-24-*`; `openspec list` has no active
  changes.
- Strict OpenSpec validation is green at 34/34 canonical specs after archive.

### Deterministic validation at the merge checkpoint

- Debug/Release solution builds: 0 warnings / 0 errors.
- Debug/Release unit suites: 686 / 686.
- ValidationDriver Debug/Release deterministic self-tests: 125 / 125.
- Release-tooling regression suite: 177 / 177.
- Canonical `scripts/validate.ps1 -Configuration Release -Ci -Publish`: PASS,
  including audited restore, native ABI, diagnostics/recovery/privacy,
  OpenSpec 37/37 before archival, single-file publish, and version smoke.
- No integration conflict or evidence-backed Critical/High regression was
  found. No guarded physical `SendInput` was issued.

### External gates and known classifications

- Physical qualification remains `BLOCKED_SUPERVISED` /
  `BLOCKED_ENVIRONMENT`: this host has no proven exclusive supervised desktop
  lease. The three known repeats (`dragreorder` H2 flip-back,
  `split-drag-release` zero-delta polyline, and `capture-inline-ui` second-tab
  assertion) remain unclassified and require authoritative exact-candidate
  runs; synthetic replay does not close them.
- Mixed-DPI hardware, real Windows 10 x64 evidence, independent Windows 11
  evidence, approved production signing credentials, and final human smoke
  remain blocked external gates. Synthetic topology remains synthetic-only.
- GitHub CLI is unauthenticated in this environment. PR #12 was not changed;
  recheck its live state after the final push and create a new main-targeting
  draft PR only when authenticated access permits. Do not merge to `main` or
  publish.

### Next action

Commit the reconciled plan/state/docs/spec/archive changes, rerun the complete
deterministic matrix against that final committed SHA, generate and verify a
new exact qualification-only candidate plus portable independent-machine
round-trip package, then push and attempt exact-head hosted CI/PR operations.
