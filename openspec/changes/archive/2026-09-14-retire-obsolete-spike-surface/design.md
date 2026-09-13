## Context

See `proposal.md` for motivation. Current HEAD has no `Spike/` tree and
`TabDock.sln` contains only `TabDock` and `TabDock.UnitTests`.
`scripts/validate.ps1` already builds the solution, ValidationDriver,
GuineaPig, and Performance by path; its synopsis still says it covers Spike.
`scripts/perf.ps1` still has an explicit `build-spike-*` step. Main spec
`test-tooling-safety` still has three Spike requirements. Thirteen other main
specs still have archive-stub Purpose lines (`TBD - created by archiving…`).
OpenSpec 1.8 copies Purpose from a **new** capability delta only; existing
capabilities' Purpose MUST be edited on `openspec/specs/<capability>/spec.md`
directly.

`PresentationOperationBudget` was removed from the product assembly in the
Wave 1 presentation-ownership work. `README.md` still claims hosted CI gates
split presentation through that type.

## Goals / Non-Goals

**Goals:**

- Make live specs, scripts, and canonical agent/onboarding docs match HEAD.
- Fail closed if someone re-lists a deleted project in the performance matrix.
- Replace archive-stub Purpose text with durable capability summaries.

**Non-Goals:**

- Recreating Spike, even as a disabled project.
- Rewriting historical audits (`docs/audits/**`, dated `docs/internal/*audit*`,
  archived OpenSpec changes).
- Changing ValidationDriver behavior, guarded-spawn mechanics, or hosted CI
  workflow files beyond documentation accuracy.
- Upgrading the pinned OpenSpec CLI (1.8.0 remains canonical). Uncommitted
  1.9 harness copies in the working tree are out of scope.

## Decisions

1. **Remove Spike requirements rather than mark them vacuous.**
   A requirement that no code can satisfy (or that invites recreating
   reparent tooling) is more dangerous than a missing historical note.
   ValidationDriver already owns live spawn/HWND safety.

2. **Add a positive "do not present Spike as live" requirement** so a later
   doc-only PR that reintroduces the old solution map is a spec miss, not
   merely a wording preference.

3. **Edit Purpose lines on the main specs, not in deltas.**
   OpenSpec 1.8 ignores Purpose on deltas for existing capabilities. Tasks
   will patch `openspec/specs/*/spec.md` Purpose only, without rewriting
   requirement bodies.

4. **Keep historical Spike mentions in provenance docs.**
   `docs/internal/guarded-spawn-pattern.md` and the 2026-07 Shepherd audit
   explain why reparenting was abandoned. Rewrite them only if they instruct
   the reader to build Spike today (`AGENT_GUIDE.md` does; that one is live).

5. **Performance matrix: delete the Spike build step; do not add a skip.**
   A skip would hide the missing project. Canonical CI does not run that
   matrix; `validate.ps1` already compile-qualifies Performance without Spike.

6. **README budget sentence: name the live tests.**
   Replace `PresentationOperationBudget` with the live split-presentation
   unit surfaces (`SplitInteractionPolicy`, `SplitPresentationController`,
   `SplitControllerTransitionBehaviorTests` / related Wave 3 tests). Do not
   resurrect the budget sink.

## Risks / Trade-offs

- **[Risk] Agents keep following stale AGENT_GUIDE copies in chat history.**
  → Mitigation: `AGENTS.md` is the compact entrypoint and MUST be corrected
  in the same change.

- **[Risk] Purpose edits accidentally rewrite requirement text.**
  → Mitigation: tasks constrain the edit to the `## Purpose` section (one or
  two sentences, 50+ characters) and require `openspec validate --all
  --strict` afterward.

- **[Risk] perf.ps1 callers depend on a `build-spike-*` measurement name.**
  → Mitigation: that step cannot succeed on current HEAD, so no healthy
  caller exists. Drop the measurement; do not emit a fake duration.

## Migration Plan

Documentation and script-only. No data migration. Apply after or independently
of `align-native-content-host-void` (no source overlap). Rollback is reverting
the doc/script/spec commit.

## Purpose replacements (main specs, Purpose section only)

Edit `openspec/specs/<capability>/spec.md` `## Purpose` to these summaries
(each is 50+ characters). Do not change requirement bodies.

| Capability | Purpose |
| --- | --- |
| `test-tooling-safety` | ValidationDriver ownership, spawn, isolation, shard, and input-identity safety for real-input qualification. The experimental reparent Spike is not a live target. |
| `ci-tooling` | Hosted and local qualification tooling: pinned OpenSpec CLI lifecycle policy, repository-owned OpenSpec install, and compile-only Performance harness restore/build of in-repo projects. |
| `diagnostic-privacy` | Redaction of window titles, profile paths, and credential-like values in logs, doctor output, and support bundles. |
| `monitor-dpi-probing` | Per-monitor effective DPI probing through the contract-correct Per-Monitor v2 helper, with fail-closed behavior on unknown or zero values. |
| `monitor-health-policy` | WinEvent hook health, capture-admission blocking, and fail-closed guest release after retry exhaustion when monitoring cannot be established. |
| `native-window-identity` | Per-capture HWND generation tokens that reject recycled same-process HWNDs on delayed callbacks and crash rescue. |
| `native-window-qualification` | Capture admission qualifications (elevation, identity, DPI context) before Shepherd installs a capture token. |
| `production-diagnostics` | Read-only doctor, support-bundle, build identity, native snapshot, and diagnostic-trace supportability without Shepherd write authority. |
| `recovery-concurrency-closure` | Product-mutation lease exclusion between live TabDock and supervised recover-pending, plus concurrent recovery refusal. |
| `recovery-journal-compatibility` | Hidden-window journal version compatibility and pending-recovery source-instance identity across journal generations. |
| `showwindow-poststate` | ShowWindow hide/show post-state classification for shepherded guests, including iconic and recovery-pending outcomes. |
| `startup-group-visibility` | Restored groups open their containers on startup; a container-open failure is skipped rather than aborting the session. |
| `visual-performance-requalification` | Visual-evidence resource budgets and disabled-path overhead so resource-lifecycle qualification remains honest. |
| `visual-qualification-evidence` | Visual evidence capture, privacy classes, packets, hashing, and review vocabulary without treating synthetic captures as physical PASS. |

## Open Questions

None.
