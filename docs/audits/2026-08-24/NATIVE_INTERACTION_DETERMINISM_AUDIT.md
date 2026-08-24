# Native Interaction Determinism & Reliability Audit — 2026-08-24

## Scope and authority

This campaign branch starts at PR #12 head
`eca8670759f9bc42aee58ec5f59b33fd0adab3f0` on
`codex/native-interaction-determinism-20260824`, with PR #12 left draft and
unmerged. Git state is authoritative for the final SHA; this file records the
campaign evidence and does not replace `git rev-parse`.

The audit inspected the application services/models/view-models/views,
`NativeMethods.cs`, WinEvent/lifecycle/identity/foreground/Shepherd/split and
containment paths, persistence/recovery, the complete ValidationDriver partial
scenario set, GuineaPig, unit tests, release tooling/CI, OpenSpec specs and
instructions, `README.md`, `docs/ARCHITECTURE.md`, `docs/TESTING.md`, the RC
ledger, historical audit plans, and agent state/checkpoint records.

## Baseline

Before campaign edits the deterministic baseline was:

| Gate | Result |
| --- | --- |
| Debug build | 0 warnings / 0 errors |
| Debug unit tests | 675/675 |
| Release build | 0 warnings / 0 errors |
| Release unit tests | 675/675 |
| ValidationDriver deterministic self-tests | 38/38 |
| Release tooling tests | 150/150 |
| Strict OpenSpec | 30/30 |
| Canonical Release CI/publish validation | PASS |
| `git diff --check` | PASS |

No physical input run was performed during this campaign: the available
desktop was not proven exclusive and safe for guarded SendInput.

## Historical disposition

The requested historical fixes were re-read against current source and tests:

| Finding | Disposition |
| --- | --- |
| staged/atomic `state.json.bak` | ALREADY FIXED |
| pending-recovery resolution compaction | ALREADY FIXED |
| orphan `.recovered` sidecar cleanup | ALREADY FIXED |
| intentional-hide finalization after capture-token removal | ALREADY FIXED |
| diagnostic-suppression cleanup on mismatch | ALREADY FIXED |
| split suspend/resume member liveness | ALREADY FIXED |
| dormant-split tab drag projection | ALREADY FIXED |
| direct `GroupViewModel` mutation regression | ALREADY FIXED |
| real layout dirty-check behavior | ALREADY FIXED |
| drag-reorder H2 physical repeat | NEEDS REPRODUCTION under lease |
| split-drag-release zero-delta polyline | NEEDS REPRODUCTION under lease |
| inline-capture second-tab assertion | NEEDS REPRODUCTION under lease |

No speculative product change was made to Shepherd, split authority, journal
ordering, recovery fail-closed behavior, or native identity gates.

## Campaign implementation

- `ScenarioOutcome` is the single eight-way outcome contract. JSON, JUnit,
  console, root-manifest, rerun, and child-shard exit semantics use it.
- `ScenarioCapabilities` resolves application/topology/session/input/signing/
  Stage-B requirements before destructive setup.
- `DesktopQualificationLease` records privacy-safe environment state and
  permanently rejects foreign coverage, foreign foreground, stale identity,
  or unverifiable observations before input/assertions.
- `TestRunProvenance` makes owned process/window, adopted external, foreign,
  and stale/recycled states explicit. Adopted external processes cannot enter
  the cleanup kill list.
- `NativeInteractionTimeline` is bounded, monotonic, role-based, deterministic,
  and redacts titles, URLs, paths, document contents, and free-form text.
- `QualificationResultWriter` emits per-scenario JSON/JUnit/timeline files and
  a root `run-manifest.json` with candidate/build identity, executable hashes,
  capability matrix, aggregate/attempt counts, ownership, and artifact links.
- `WinEventRoutingPolicy` and `NativeInteractionReplay` isolate native-free
  policy/state behavior. Fixtures cover lifecycle/stale events, H2 drag causes,
  split drag identity phases, and authoritative inline-capture handoff.
- `ScenarioWait` replaces shared generic polling with monotonic bounded waits
  that retain last observed state; UIA window/menu waits and `Util.WaitUntil`
  use the shared seam. Continuous drag/input and product debounce intervals
  remain explicit physical timing requirements.
- Fixed-seed deterministic suites cover outcome/rerun semantics, lease state,
  provenance, replay, split transitions, identity matrices, and stress seeds
  `0x5EED2026`, `0x51172026`, and `20260824`.

The final deterministic corpus is 96/96 green. Debug and Release unit suites
are 686/686, the focused native WinEvent/routing/replay tests are 13/13,
release-tooling is 150/150, strict OpenSpec is 31/31, and canonical Release
validation/publish is green with the native ABI, version, privacy, recovery,
and publish smokes included.

## WinEvent measurement decision

The representative storm sends 20 captured foreground events and 10 irrelevant
child-object notifications. Measured result: 30 callbacks, 20 callback
membership probes, 20 dispatch membership revalidations, 20 posts, 20 lifecycle
callbacks, at least 10 irrelevant rejections, and zero stale dispatches.
Child/object events are rejected before a membership probe. No before/after
optimization comparison is claimed because the second dispatch lookup is the
HWND-generation safety proof; a cross-event cache would risk changing stale
handle behavior. The measurement is retained as a behavioral regression.

## Physical qualification status

The three remaining unclassified physical repeats were not run. Their current
status for this session is `BLOCKED_SUPERVISED`/`BLOCKED_ENVIRONMENT` pending an
exclusive desktop, never `FAIL_PRODUCT`: `dragreorder` H2 flip-back count,
split-drag-release zero-delta polylines, and inline-capture second-tab
assertions. Their non-physical causes and identity/order handoffs are covered
by replay fixtures and deterministic assertions.

## Artifacts

- Campaign plan: `.agent/plans/native-interaction-determinism-reliability-2026-08-24.md`
- OpenSpec change: `openspec/changes/native-interaction-determinism/`
- Replay fixtures: `tests/ValidationDriver/fixtures/native-replay/`
- Per-run root manifest/timelines: `%TEMP%\TabDock-Validation\runs\<run-id>\`
- Historical ledger: `docs/audits/2026-08-23/RC_QUALIFICATION_EVIDENCE.md`
- This audit: `docs/audits/2026-08-24/NATIVE_INTERACTION_DETERMINISM_AUDIT.md`
