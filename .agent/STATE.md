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

**Status:** internally qualified and ready for final exact-SHA artifact
generation and delivery. Independent convergence found one ValidationDriver
harness defect: foreground arrangement required the new target to be foreground
before attempting the switch. The committed fix and two deterministic lease
regressions are green; generate the fresh ignored candidate/evidence lineage
under `.artifacts/mainline-release-closure-<final-head>/` and push the
history-preserving lineage to the authorized PR surface. Historical candidates
for `159d094...` and `1cc529f...` are diagnostic only and must not be reused.

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

### Prior integration and evidence checkpoint

- The integration branch was pushed without rewriting any historical campaign
  branch. Its remote head matches the local final head; resolve both
  dynamically with Git.
- The prior fresh qualification-only candidate was bound to the then-final
  committed source SHA, semantic version 1.0.0, release manifest,
  ValidationDriver, primary run manifest, qualification bundle schema 1, and
  portable package schema 1. It is invalidated as final evidence by the
  foreground-transition harness fix. A new candidate must be generated from
  the final committed tree before handoff.
- Candidate bundle verification, portable-package verification, deterministic
  returned bundle verification, and data-only returned-report import all
  passed with zero failures. The returned evidence is synthetic-deterministic
  and does not establish physical qualification.
- No candidate binary, returned executable, or returned script was executed by
  the report importer; imported evidence was hash-verified as data only.

### Deterministic validation at the final source checkpoint

- Debug/Release solution builds: 0 warnings / 0 errors.
- Debug/Release unit suites: 686 / 686.
- ValidationDriver Debug/Release deterministic self-tests: 127 / 127 after
  the foreground-transition lease regressions.
- Release-tooling regression suite: 177 / 177.
- Canonical `scripts/validate.ps1 -Configuration Release -Ci -Publish`: PASS,
  including audited restore, native ABI, diagnostics/recovery/privacy,
  strict OpenSpec 34/34, single-file publish, and version smoke carrying the
  committed foreground-transition fix identity.
- No integration conflict or evidence-backed Critical/High regression was
  found. No guarded physical `SendInput` was issued.

### External gates and known classifications

- Physical qualification remains `BLOCKED_SUPERVISED` /
  `BLOCKED_ENVIRONMENT`: this host has no proven exclusive supervised desktop
  lease. The three known repeats (`dragreorder` H2 flip-back,
  `split-drag-release` zero-delta polyline, and `capture-inline-ui` second-tab
  assertion) have an explicit safe-session classification of
  `BLOCKED_SUPERVISED`/`BLOCKED_ENVIRONMENT`; their scenario-specific
  product-versus-harness verdicts remain pending authoritative exact-candidate
  runs. Synthetic replay does not close them.
- Mixed-DPI hardware, real Windows 10 x64 evidence, independent Windows 11
  evidence, approved production signing credentials, and final human smoke
  remain blocked external gates. Synthetic topology remains synthetic-only.
- GitHub CLI is unauthenticated in this environment, so creation of the new
  main-targeting draft PR and its pull-request CI run is blocked pending
  `gh auth login` or `GH_TOKEN`. PR #12 remains open/draft and unchanged; its
  last live public metadata identified head `eca8670...` targeting `main`.
  Do not merge to `main` or publish.

### Handoff action

Generate the exact-candidate bundle/package verification against the final
committed tree, then push the history-preserving lineage to the authorized PR
surface. If authenticated GitHub access remains unavailable, record that
infrastructure blocker. The remaining release work after exact-SHA
requalification is external:
supervised physical repetitions, mixed-DPI hardware, Windows 10 and independent
Windows 11 evidence, approved production signing, and human smoke. Do not
convert those missing prerequisites to PASS and do not merge or publish.
