# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session (`git rev-parse HEAD`,
`git branch --show-current`, `git status`). This file never records a
self-referential current SHA or a hosted-CI result for the commit containing
this text.

## CURRENT STATUS

## NATIVE INTERACTION DETERMINISM CAMPAIGN (2026-08-24, ACTIVE)

Objective: classify physical ValidationDriver outcomes without mislabeling
foreign-desktop interference, preserve bounded privacy-safe evidence, turn
native-event policy into deterministic replay, and measure WinEvent work while
preserving the Shepherd/no-reparent architecture.

Git authority for this campaign: branch
`codex/native-interaction-determinism-20260824`, based directly on PR #12 head
`eca8670759f9bc42aee58ec5f59b33fd0adab3f0`; PR #12 remains draft and unchanged.
The implementation and campaign documentation are committed in coherent
waves; resolve final HEAD dynamically before handoff because this file never
self-identifies its own containing commit.

Baseline before edits: Debug/Release builds 0 warnings/0 errors; Debug and
Release unit suites 675/675; ValidationDriver self-tests 38/38;
release-tooling 150/150; strict OpenSpec 30/30; canonical Release CI/publish
validation green; diff check clean. No physical run was authorized because the
desktop was not demonstrably exclusive and safe for SendInput.

Completed in the current campaign: canonical eight-way `ScenarioOutcome`
contract with exit/JUnit/JSON/manifest mappings and rerun aggregation;
central capability preflight; fail-closed `DesktopQualificationLease`;
explicit provenance ownership categories and adopted-external cleanup
exclusion; bounded `NativeInteractionTimeline`; root run manifest with
capability matrix and executable hashes; condition-wait seam; pure WinEvent
routing and native interaction replay seams with fixtures; deterministic lease,
outcome, replay, lifecycle, split, identity, and fixed-seed stress tests;
canonical shard exit aggregation; and documentation/OpenSpec artifacts.

Current validation at the last substantive implementation checkpoint:
Debug/Release builds 0 warnings/0 errors; Debug/Release unit suites 686/686;
ValidationDriver self-tests 96/96; focused WinEvent/replay tests 13/13;
release-tooling 150/150; strict OpenSpec 31/31; canonical Release CI/publish
validation PASS including native ABI, version, privacy, recovery, and
single-file publish smoke; diff check clean. Physical repeat cases
`dragreorder` H2, split drag/release zero-delta, and inline-capture second-tab
remain unrun on an exclusive desktop and must not be called product failures.

Historical disposition verified before edits: staged/atomic `state.json.bak`,
pending-recovery compaction, orphan `.recovered` cleanup, intentional-hide
finalization, diagnostic-suppression cleanup, split member liveness, dormant
split drag projection, direct `GroupViewModel` mutation coverage, and layout
dirty-check behavior are ALREADY FIXED. Existing physical repeat findings are
NEEDS REPRODUCTION under the new lease; no speculative Shepherd change was
made. Next action: resolve final Git/PR state, attempt to push this stacked
draft branch if remote authority is available, and leave PR #12 draft/unmerged.

## RELEASE QUALIFICATION CAMPAIGN (2026-08-23/24, COMPLETE FOR THIS ENVIRONMENT)

Objective: take PR #12 from CI-qualified RC to highest achievable release
readiness: supervised/native qualification, real-app interop, identity stress,
DPI/topology, crash/recovery, signing, Stage-B; fix only reproduced defects.
Starting point: clean tree at handoff head `db6d3e6`; origin/main `ba3115a`;
PR #12 open/draft with that head (verified via GitHub REST after a transient
network outage).

Executed and fixed (each: repro → root cause → minimal fix → regression):
- PRODUCT Critical: launcher cold-start FATAL crash on empty state (TwoWay
  Run.Text binding to ObservableCollection.Count). Fixed d60bf7b with
  LauncherStartupBindingTests (4 facts incl. source contract).
- PRODUCT High: ordinary tab switches left foreground on container chrome
  (zero SHEPHERD[bring-to-front] during rapid switches; split path already
  granted it). Fix on final commit: SetForeground in SyncShepherdActiveWindow
  when container is active and no chrome interaction is open.
- HARNESS: seven stale contracts vs redesigned UI (capture-order assumption →
  live TabStripOrder helper; two-tab EnterSplitTwo misuse → partner submenu;
  'Add window to group'→AddWindowButton AutomationId; ancestor-checkbox →
  sibling 'Select <title>' checkbox; 'Exit'→'Exit TabDock' + fresh-rect click;
  'No groups yet'→'Create your first workspace'; ClickTabSubmenuItem parent-
  rect wait). Commit 60d01f3 plus final-commit harness fixes.
- HARNESS provenance gaps: Windows 11 Notepad single-instance broker adoption
  (TryAdoptExternalWindow: identity-pinned input target, process never
  tracked/killed); Chrome-requiring standalone scenarios now SKIP
  (SKIP_BROWSER_NOT_INSTALLED) instead of Win32Exception.
- SPEC: release-engineering exact-binary requirement now uses SHALL; strict
  OpenSpec validate --all --strict is 30/30.

Deterministic gates all green on the final committed tree: Debug/Release
builds 0w/0e; Debug+Release suites 675/675 each; release-tooling 150/150
(requires pwsh; PS5.1 run fails its own syntax — record for future sessions);
driver self-tests 38/38; canonical `validate.ps1 -Configuration Release -Ci
-Publish` PASS incl. native ABI (Win11 26200), doctor/pending-recovery/
redirected-lifecycle/support-bundle privacy smokes, OpenSpec 30/30, publish
+version smoke; `git diff --check` clean. Hosted CI `build` green on prior
head db6d3e6 (run 32639177096); final push re-triggers it.

Supervised physical qualification (Release artifacts, guarded SendInput):
global-tab-navigation PASS 24/24; split-affordance PASS; capture-admission-
blocked BLOCKED_ENVIRONMENT (by design). Broad suite (`--yes all`, 11 shards)
executed twice; best-of-N: dpi-multi-monitor PASS, split-core PASS 11/11,
crash-recovery 7/8, capture-group 13/15, split-focus 5/9, split-render 12/18,
drag-z-order 5/9, keyboard-input 4/15, startup 1/3, diagnostics 5/8 (+6 Chrome
SKIPs). Persistent non-passes correlate with an unregistered foreign window
(operator's maximized Windows Terminal) holding/covering foreground;
identity-failure-*.json artifacts under %TEMP%\TabDock-Validation\runs\ prove
the fail-closed refusals. Identical binaries passed several of those scenarios
in other runs. Remaining repeats need an exclusive-desktop rerun of the FINAL
SHA before classification as pass or defect; commands unchanged in
docs/TESTING.md. Full ledger: docs/audits/2026-08-23/RC_QUALIFICATION_EVIDENCE.md.

External gates unchanged: mixed-DPI/multi-monitor topology absent (single
1920x1080 @96dpi) = BLOCKED_ENVIRONMENT; production signing BLOCKED_SIGNING
(no DigiCert STM credentials; NOT_CONFIGURED manifest recorded);
BLOCKED_STAGE_B (no second machine). RC bundle previously produced at
intermediate SHA 56cc217 under artifacts/rc-candidate (QUALIFICATION_ONLY,
sha256 66d243bd...); regenerate via scripts/release-qualify.ps1 on final SHA.

Decision: PR #12 REMAINS DRAFT. Classification: RC_BLOCKED_EXTERNAL.
Next actions: exclusive-desktop rerun of remaining supervised set on final
SHA; obtain signing credentials; Stage-B machine; mixed-DPI hardware.

## RELEASE-CANDIDATE HARDENING CAMPAIGN (started 2026-08-23, IN PROGRESS)

Objective: continue draft PR #12 from the existing ship-readiness branch and
implement a second independent hardening pass across capture identity and
refresh continuity, picker scale, keyboard/accessibility behavior, responsive
WPF layout, launcher/container projections, source contracts, ValidationDriver,
documentation, and release qualification. Active plan:
`.agent/plans/release-candidate-hardening-2026-08-23.md`.

Git was fetched and resolved dynamically before work. The checkout started
clean on `main` at the stated base, and now tracks the existing branch
`codex/ship-readiness-overhaul-20260823` at the handoff head. PR #12 is open
and draft with that branch as head and `main` as base; `origin/main` matches the
stated base at session start. No main integration was needed.

Current phase: implementation and deterministic qualification complete for picker identity/scale, accessibility
and keyboard contracts, responsive layout/style polish, launcher/container
projection hardening, ValidationDriver selector updates, and documentation;
deterministic release qualification is green. The deterministic baseline was 652/652
Debug unit tests; the current Debug corpus is 671/671, including 11/11 targeted
split/source-contract checks. A full-suite persistence hammer exposed a real
settlement-barrier race: concurrent SaveAsync callers could overwrite the
highest-generation task reference with an older one. PersistenceService now
tracks the highest generation under a bookkeeping lock; its native/durable
transaction architecture is unchanged. Isolated persistence tests pass 5/5 and
the post-fix full Debug suite passes 671/671.

Verified campaign fixes include strong picker continuity (HWND/PID/thread/
process-start/class/Windows-path identity), fail-closed final handoff through
WindowIdentityGate, coalesced filter/selection/icon work for 100/500/1000-row
sets, dynamic accessible names/help text and visible keyboard focus, stable
launcher/container/picker AutomationIds, live launcher selection repair,
responsive long-text layout, and current ValidationDriver selectors.
Final deterministic evidence: Debug and Release solution builds/tests are
0-warning/0-error and 671/671; `validate.ps1 -Configuration Release -Ci -Publish`
passes audited restore with no vulnerable packages, Release driver/GuineaPig/
performance builds, native ABI, version/doctor/recovery/privacy smokes, OpenSpec
30/30, and self-contained publish/version; release-tooling is 150/150; strict
OpenSpec change validation is valid; ValidationDriver deterministic self-tests
are 38/38. Repeated stability is green for three CapturePicker runs (14/14)
and two full Debug runs (671/671) after the persistence barrier fix.
Supervised real-input qualification remains blocked unless an exclusively
available, provenance-safe Windows desktop is provided; exact reruns and the
blocked scope are in `docs/TESTING.md`.

## PRODUCT TRUST & INTERACTION CAMPAIGN (started 2026-08-23, DELIVERY READY)

Objective: close M3 pending-recovery visibility, canonical capture-admission
presentation, M4 scoped focus-independent tab navigation, and M5 always-visible
split discovery as one product trust campaign. Active plan:
`.agent/plans/product-trust-interaction-campaign-2026-08-23.md`.
OpenSpec change: `product-trust-interaction-campaign`, archived at
`openspec/changes/archive/2026-08-23-product-trust-interaction-campaign/`.

Baseline verified after `git fetch origin`: `main` and `origin/main` at
`9488bf1`, clean. Existing hardened authorities are present: pending recovery
discovery/execution with supervised mutation, `GroupManager.SetCaptureAllowed`,
`TabNavigationPolicy`, and `SplitPresentationController`/policy paths.

Completed this phase: M3 pending-recovery attention, canonical capture-admission
projection, scoped Ctrl+Alt+PageUp/PageDown navigation, persistent Split
affordance, deterministic regression/accessibility/source-contract coverage,
ValidationDriver scenario wiring, OpenSpec artifacts, architecture/testing/
README updates, and the integration audit are implemented. The launcher
projection is read-only; GroupManager remains the admission authority;
TabNavigationPolicy remains the navigation authority; SplitPresentationController
and existing mutation/presentation paths remain the split authority.

Final deterministic validation is green: Debug and Release builds are
0-warning/0-error; Debug and Release suites are 649/649; release-tooling is
150/150; `validate.ps1 -Ci -Publish` completed successfully with 649/649,
native ABI, redirected lifecycle/privacy, OpenSpec 29/29, and Release publish
smokes green; and `git diff --check` is clean. Stability included a recurring
full-suite abandoned-mutex test ordering failure; it was root-caused to the
test's owner-exit synchronization and repaired with an explicit exit barrier
plus handle lifetime guard, without changing production behavior. See the two
`.agent/investigations/product-trust-stability-*.md` records.

Supervised real-input scenarios are recorded as
`BLOCKED_SUPERVISED`/`BLOCKED_ENVIRONMENT` because this desktop is not certified
exclusively available and provenance-safe. Exact rerun commands are in
`docs/TESTING.md` and the archived OpenSpec task record at
`openspec/changes/archive/2026-08-23-product-trust-interaction-campaign/`.

Historical next action from that completed campaign: commit the intended work
and push `main`. The current PR-branch release-candidate campaign above
supersedes it; do not use that historical line as authority for this session.
Supervised Windows qualification, production signing, mixed-DPI/negative-
coordinate topology, and final human smoke remain external gates.

## HISTORICAL CAMPAIGN RECORDS

## TAB MUTATION & DORMANT-SPLIT DRAG CAMPAIGN (started 2026-08-23, IN PROGRESS)

Objective: (1) direct deterministic regression coverage for GroupViewModel
ReorderTabs/CommitReorder/ReleaseTab (verified: zero direct references in
tests today); (2) root-fix the dormant-split strip-drag loss —
SnapshotDragMidpoints iterates Tabs.Count containers against the
DisplayTabs-bound ListBox (one shorter while any pair exists), so reorder is
silently dead during dormancy, and display-space boundaries were fed into
Tabs-space ReorderTabs; (3) preserve H2 anti-oscillation and presented-pair
protection. Plan:
`.agent/plans/post-hardening-tab-mutation-drag-reliability-2026-08-23.md`.
Baseline `3201d5d`. Fix: pure `TabStripDragProjection` (visible slots =
reference + midpoint; drop boundary resolves to live anchor index; past-end →
Tabs.Count; no-composite ⇒ byte-identical to old formula); snapshot DisplayTabs
slots; count-invalidation compares DisplayTabs.Count only. Phase A needs NO
new seam (real manager + shepherd over existing fakes + temp persistence).
OpenSpec: `dormant-split-tab-drag` (specs currently silent on dormant-strip
interaction; ARCHITECTURE.md:255 documents the intent).
STATUS: TAB MUTATION & DORMANT-SPLIT DRAG CAMPAIGN COMPLETE (2026-08-23).
Baseline `3201d5d`; commits `5ecbf1b` (plan), `ee099ce`
(test: GroupViewModelMutationTests — 19 facts over the REAL stack: real
GroupManager + real WindowShepherdService on the shared fake identity/release
APIs + temp PersistenceService; NO new production seam needed), `5b341bc`
(fix), `aafba4a` (projection matrix + dormant integration), `ab633ce`
(stability).

Verified gap before work: zero references to ReorderTabs/CommitReorder/
GroupViewModel.ReleaseTab anywhere in tests. Verified defect:
SnapshotDragMidpoints iterated Tabs.Count containers against the
DisplayTabs-bound ListBox (one shorter while ANY pair exists) so every missing
container invalidated the whole snapshot and reorder was silently dead during
dormancy; display-space boundaries were also fed straight into Tabs-space
ReorderTabs.

Pinned contracts (all held; no new production bug found in the mutation paths):
ReorderTabs invalid oldIndex/negative destination/same index strict no-ops with
no accidental durable save; destination-past-end clamp [A,B,C]+Reorder(0,999)
=> [B,C,A] in BOTH collections, same instance moved, model ActiveIndex agrees
(the historical crash pin); backward/forward parity matrix; CommitReorder
persists the FINAL order to state.json (intermediate positions do not),
harmless without a move, idempotent; ReleaseTab keeps inactive actives by
REFERENCE with ActiveIndex re-synced, active-release neighbour rule
(Tabs[min(idx,Count-1)]), first/last-slot cases, final-tab EmptiedByPopOut
exactly once, unknown TabViewModel strict no-op, RecoveryPending retains
EVERYTHING fail-closed, TargetGoneOrRecycled deliberately removes (distinct
from pending).

Fix architecture (`Services/TabStripDragProjection.cs` + view rewrite): drag
start snapshots one slot PER VISIBLE DisplayTabs item as (midpoint, item
reference); drop boundary resolves through the FIRST slot whose midpoint
exceeds the pointer to that item's LIVE authoritative anchor (tab -> its
index, composite -> Left's index; unresolved => fail-closed null); past-end =>
Tabs.Count. With no composite this reduces EXACTLY to the old midpoint formula
(no-split behavior byte-preserved). Composite explicitly never a drag unit in
either state (press ignored) but remains a valid boundary region; presented-
pair click-swallow untouched. H2 proof: resnapshot triggers ONLY when
DisplayTabs.Count changes; ReorderTabs uses collection Move which preserves
both counts (pure test pins this), so reorders can never reach the
resnapshot path; stored references re-resolve live after intermediate
in-drag reorders WITHOUT resnapshotting.

OpenSpec: change `dormant-split-tab-drag` created (specs were silent about
dormant-strip interaction; ARCHITECTURE.md documented the intent), validated
26/26, archived as `2026-08-23-dormant-split-tab-drag`; ADDED requirement in
ui-ux-hardening (dormant drag preserved, composite non-drag unit + boundary,
presented behavior unchanged, no-resnapshot rule). Post-archive validate 25/25.

Gate (2026-08-23): Debug+Release builds 0w/0e (--no-incremental); Debug+Release
xUnit 578/578 each (+29 net); release tooling 150/150; validate.ps1 -Ci -Publish
PASS (native ABI PASS, OpenSpec totals green inside the run); openspec 25/25;
git diff --check clean. Suite-stability repair en route (ab633ce): the
pre-existing picker icon-generation test's 2s start-gate flaked under the grown
suite (~578 parallel-heavy tests); raised to 15s — it proves ordering, not
speed; my fixtures now inject the deterministic icon extractor instead of real
ExtractIconEx.

BLOCKED_SUPERVISED (not run, not counted as failure): real-input dormant-pair
drag acceptance — with A|B defined+dormant and C/D ordinary tabs, drag C past D
and verify reorder lands, then drag C out of the container and verify pop-out
with pair retained. Command: dotnet run --project
tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj
-c Release -- --yes --configuration Release dragreorder (plus a supervised
dormant-pair variant once scripted). Deterministic headless equivalents are in
place; only container-generation geometry (TranslatePoint) is view-side.

NEXT RECOMMENDED CAMPAIGN (queue reassessed): (1) replace the tautological
UnchangedLayoutUpdated_ProducesNoRelayout with real dirty-check behavioral
coverage; (2) WinEvent duplicate _isCapturedWindow probes ONLY if measured;
(3) any fresh defect from supervised runs. Do not assume order without
re-verifying source.

## LAYOUT DIRTY-CHECK TEST REPAIR (2026-08-23, item 1 above — COMPLETE)

`780a19b`: the former UnchangedLayoutUpdated_ProducesNoRelayout fact was a
tautology (byte-identical to Idle_EnsureFinalPass_True; never touched the
dirty-check it claimed to cover). Replaced by real coverage of the actual
per-notification boundary: the decision moved behind
`PaneContainmentPolicy.ShouldRequestRelayoutForContentRect`
(non-degenerate AND (first observation OR beyond ±1px epsilon)), and
ContainerWindow_LayoutUpdated now routes through it — behavior identical,
including cache discipline: only an actionable observation overwrites the
cached rect, so a degenerate candidate can never poison later comparisons
(pinned by a dedicated test).
New matrix in PaneContainmentPolicyTests: first-observation relayout,
identical-repeat silence, ±1px-per-edge tolerance ×8, ±2px change ×8,
degenerate rects ignored + cache-poisoning resistance ×4, negative multi-
monitor coordinates, and a startup/resize/jitter lifecycle sequence.
Wave-2D source contract updated honestly: 3 direct MatchesWithinEpsilon call
sites + exactly 1 ShouldRequestRelayoutForContentRect (authority usage still
4 total; no handwritten epsilon compare may return). Removed tautology noted
in RequestRelayoutFinalPassTests with a pointer to the new home.
Gate: Debug+Release builds 0w/0e (--no-incremental); Debug+Release xUnit
601/601 each (−1 tautology, +24 real facts); release tooling 150/150;
validate.ps1 -Ci -Publish PASS; openspec 25/25; git diff --check clean.
No OpenSpec delta: behavior unchanged, test-debt repair only.
NEXT REMAINING QUEUE: WinEvent duplicate _isCapturedWindow probes ONLY if
measured shows value; then fresh defects from supervised runs.

## DURABLE STATE & RECOVERY CAMPAIGN (2026-08-23, COMPLETE — see its STATUS
block below for gates; OpenSpec change archived as
`2026-08-23-durable-state-recovery-hardening`)

Objective: close the two verified persistence/recovery follow-ups queued by
the stranded-guest tails campaign — C-1 (`PersistenceService.CommitJson`
copies the new backup over the live `state.json.bak` with
`File.Copy(overwrite:true)`, so a failure/power loss mid-copy destroys the
previous known-good backup before the new primary is installed) and C-2
(pending-recovery `Resolutions` are never compacted and fully-retired sources
leave orphan `<source>.recovered` sidecars that later same-filename
generations keep appending to). Plan:
`.agent/plans/post-hardening-durable-state-recovery-2026-08-23.md`.
Canonical OpenSpec change: `durable-state-recovery-hardening`.

Design (verified against baseline `9965702` source before any edit):
- C-1: stage the backup inside the existing `_writeGate`/generation gates —
  read primary once → durable `state.json.bak.tmp` → atomic Move-over of
  `.bak` → only then write/install the primary; missing primary skips the
  stage so an existing valid `.bak` survives; test seam = three optional ctor
  delegates (readAllBytes/writeDurableBytes/atomicMove), no storage
  abstraction.
- C-2: liveness compaction of Resolutions wherever a sidecar is rewritten for
  a live source (keep same-file+same-instance+live-SHA records plus empty-keyed
  legacy markers for null-id sources; never touch non-retired transactions);
  RetireEntry deletes `<source>.recovered` immediately after deleting the
  fully-resolved source; crash window converges via a new orphan-sidecar sweep
  in RunInteractive ONLY (read-only Discover stays untouched): unreadable or
  non-retired-transaction-holding orphans retained fail-closed.
- Known test re-anchoring required: `ResolvedEntryRetirement_CanBeRetriedToCompletion`
  and `UnreadablePendingSibling_DoesNotBlockOtherDiskOnlyCleanup` pin the OLD
  orphan-sidecar retention; same-generation replay and legacy replay tests must
  be re-anchored on marker-and-source-coexist states.

STATUS: DURABLE STATE & RECOVERY CAMPAIGN COMPLETE (2026-08-23) — C-1 `0715a15`:
CommitJson stages a fresh backup candidate (`state.json.bak.tmp`, durable
flush) and installs it with one atomic Move-over BEFORE touching the primary,
so no failure/power-loss window can destroy the previous known-good backup;
missing primary skips the stage (existing `.bak` preserved); fault seams
(readAllBytes/writeDurableBytes/writeDurableText/atomicMove) drive
deterministic regression coverage for every boundary in
`PersistenceBackupDurabilityTests` (8 tests: staging-read/flush/install
failures, primary temp/install failures after backup success, missing-primary
preservation, marker non-advancement + unchanged-save correctness,
latest-wins across failed saves, stale-tmp never authoritative).
C-2 `f762b68`: `CompactUnreachableResolutions` bounds Resolutions by source-
generation liveness wherever a sidecar ledger is rewritten (keeps live
file+instance+SHA records plus empty-keyed markers for null-id sources; never
touches transactions); RetireEntry deletes `<source>.recovered` immediately
after deleting the fully-resolved source; new MUTATING-only
`SweepOrphanedResolutionSidecars` in RunInteractive converges crash-window
orphans (unreadable or non-retired-transaction-holding orphans retained
fail-closed; read-only Discover untouched).
Regression matrix in `PendingRecoverySidecarLifecycleTests` (7 tests) covers
liveness bounding (70 dead-generation records dropped, interrupted transaction
+ live siblings survive), full-retirement sidecar removal only at the end,
crash-after-source-deletion convergence, unreadable-orphan retention,
live-transaction orphan retention, historical-orphan sweep, and legacy
empty-keyed-marker preservation with foreign-generation drops.
Re-anchored on the new lifecycle: `ResolvedEntryRetirement_CanBeRetriedToCompletion`,
`UnreadablePendingSibling_DoesNotBlockOtherDiskOnlyCleanup`,
`SameGenerationReplay_ResolvesAsDuplicateWithoutRepeatWork`
(marker-and-source coexist dedup), `LegacyPendingWithoutInstanceId_StillConsumesViaFingerprintFallback`,
both `OldRewrittenLedger_*Converges*` tests (converged ledger now observed
mid-flight via during-retirement fault + plain retry), and the abandon-path
test (sidecar consumed with the source).
Also `e670576`: pre-existing CS8602 in the SG-1 journal-lock fixture silenced
(was breaking the 0-warning Debug gate).
Gate (2026-08-23): Debug+Release builds 0w/0e (--no-incremental); Debug+Release
xUnit 549/549 each (+15 net); release tooling 150/150; `validate.ps1 -Ci
-Publish` PASS (native ABI PASS, supervised-recovery redirected-process smoke
PASS, OpenSpec totals green, publish smoke bound to this tree);
openspec 25/25 after archiving `durable-state-recovery-hardening` (deltas
synced: persistence-resilience staged-backup requirement;
hidden-window-journal resolution-bounding + sidecar-retirement requirement);
git diff --check clean.

## ARCHITECTURE-HARDENING CAMPAIGN (started 2026-08-22, IN PROGRESS)

Objective: act on `docs/audits/2026-08-22/IMPROVEMENT_REVIEW.md` in waves
(0 regression net → 1 dead scaffolding → 2 correctness dedup → 3 presentation
ownership → 4 self-test migration → 5 repo hygiene), preserving every
Shepherd/native-window invariant. Wave definitions are in the session goal.

**STATUS: ARCHITECTURE-HARDENING CAMPAIGN COMPLETE — Waves 0–5 implemented,
verified, committed, pushed.** Wave definitions were in the session goal
(`docs/audits/2026-08-22/IMPROVEMENT_REVIEW.md` is the originating review).
Wave 0 gate (executed by a healthy shell after the OpenCode Bash harness
failure): Debug/Release builds 0w/0e; tests 214/214 both configs (+29 new
regression tests); openspec 20/20; git diff --check clean.

Wave 0 changes:

- NEW `Services/TabNavigationPolicy.cs`: pure Ctrl+Tab decision seam;
  returns the authoritative target `CapturedWindow`, never an index.
- `Views/ContainerWindow.xaml.cs` `ContainerWindow_PreviewKeyDown`: routed
  through the policy; no index math remains on the view path.
- NEW `Services/PaneContainmentPolicy.cs`: refusal-suppression decision
  (visible+same-rect ⇒ suppress; hidden ⇒ never) plus epsilon/exact rect
  matchers. `LayoutShepherdActiveWindow` / `LayoutSplitPanes` /
  `MarkRefusingPane` now route through it; storage intentionally stays in
  `_refusedPaneByHwnd` until Wave 3 ownership migration. Former private
  `IsRefusingPane` removed (its only callers moved onto the policy).
- `HotkeyService.GlobalHotkeyRegistered`; `MainViewModel.CaptureButtonText`
  drops the "(Ctrl+Alt+G)" hint when registration failed; XAML binds it;
  `App.xaml.cs` sets the flag after `Register()`.
- NEW tests: `TabNavigationPolicyTests.cs` (policy matrix + source-contract
  - real-VM integration), `PaneContainmentPolicyTests.cs`,
  `HotkeyAvailabilityTests.cs`.
- Integration: a parallel session pushed its own interaction source-contract
  guards written against the pre-Wave-0 shapes; re-pointed them at the policy
  seams (`55bc516`) — same invariants, current call sites.

**Wave 1 (dead scaffolding removal) — COMPLETE:** budget sink seam deleted
(never assigned anywhere in production), coordinator test shims/unread
counters/refusal dict/zero-caller accessors removed,
`DescribeTransaction` + unreachable `Classify` branch removed,
`SeedState` removed, `IPresentationOperations` preserved (live),
`ResolveNativeTransition` deliberately KEPT for Wave 3D. Spike orphan was
removed by the parallel session (cd38c92..2906d95). Replacement behavioral
tests: `SplitControllerTransitionBehaviorTests.cs`. Gate: builds 0w/0e;
tests 204/204 both configs; release tooling 150/150; validate.ps1 PASS;
openspec 20/20; git diff --check clean. Commit d3480f2.

**Wave 2 (correctness-preserving dedup) — COMPLETE:** five sub-waves,
five commits (33c3ded tip), all pushed.

- NEW `Properties/AssemblyInfo.cs`: `InternalsVisibleTo("TabDock.UnitTests")`
  so deterministic headless tests drive identity evaluators via recording
  native fakes (no real HWNDs).
- **2A recovery identity tiers:** `WindowShepherdService.EvaluateRecoveryIdentity`
  and `EvaluateRecoveryGeneration` are now thin wrappers over one internal core
  `EvaluateRecoveryIdentityCore` with an explicit `[Flags] RecoveryEvidenceTier`
  (`Strong = ExecutablePath|ProcessStart` | `MutationBoundary = None`). Probe
  order, Mismatch-vs-Unverifiable semantics, and every diagnostic reason string
  are byte-identical; recording-fake tests prove the cheap tier performs ZERO
  exe/process-start probes. REJECTED: merging `PendingRecoveryService.`
  `EvaluateRecoveryGeneration` into that core — materially different semantics
  (temporary-recovery-token polarity vs capture-generation token, conditional
  process-start evidence from entry.Fields/transaction fallback, different API
  seam); a shared primitive would erase the distinction.
- **2B WindowIdentityGate one algorithm:** `Evaluate` /
  `EvaluateBeforeCaptureToken` (~90% duplicated pair) now wrap one private
  `EvaluateCore` parameterized by enum `CaptureTokenRequirement`
  (`Required` | `NotYetInstalled`) + explicit matchReason string. Pre-token
  path never queries GetCaptureIdentityToken (asserted); regular path still
  validates live token vs captured token; VerifyReleasedCloseTarget kept a
  separate lifecycle phase by design. Probe order + all strings stable.
- **2C foreground grant:** BringToFront/SetForeground's duplicated
  SetForegroundWindow → benign-key-nudge → generation revalidation → retry-once
  sequence centralized in private `TryGrantForeground(window,
  mutationGatePrefix, out fg)` returning `ForegroundGrantOutcome`
  (AlreadyForeground | StaleMidSequence | Completed) so callers keep their exact
  legacy early-return telemetry behavior, positioning/z-order ownership, log
  tags, and byte-identical gate reason strings
  (bring-to-front-before-foreground[-retry], foreground-before-set[-retry]).
  Source contract pins exactly ONE SendBenignKeyNudge call site and no direct
  NativeMethods.SetForegroundWindow outside the helper (+ pre-existing
  presentation-ops forwarder). Behavioral unit tests not added: no existing
  native seam for GetForegroundWindow/SetForegroundWindow/SendInput without a
  giant abstraction the wave forbids; covered instead by source contract + the
  consolidated code being trivially auditable.
- **2D rect comparisons:** all four handwritten ±1px comparisons in
  ContainerWindow (LayoutUpdated content-rect change detection, ObservedMatches,
  LayoutShepherdActiveWindow redundant-glue guard, NeedsPanePosition) route
  through Wave-0 authority `PaneContainmentPolicy.MatchesWithinEpsilon`; no new
  rect helper created. Drag-threshold Math.Abs untouched (different concept).
  PaneContainmentPolicyTests extended: exact match, per-edge ±1 tolerance
  independently, ±2 rejection every edge, all-four-edges-at-once, negative
  multi-monitor coords, large positive coords (+6 tests). Source contract:
  no handwritten epsilon compare may return to the view; exactly 4 policy calls.
- **2E replaceable timers:** NEW `Services/ReplaceableDispatcherTimer.cs` —
  `ReplaceableWorkSlot` (deterministic token-based single-owner core;
  stale-suppression logic testable without dispatcher/sleeps) +
  `ReplaceableDispatcherTimer` (WPF adapter; replacement stops prior arm;
  queued stale tick is a silent no-op BY CONSTRUCTION; one-shot consumes
  ownership BEFORE running the action, preserving legacy order; Background
  priority default matches legacy parameterless construction). All five
  ContainerWindow slots migrated: _activateReassertTimer (120ms),
  _stateSettledTimer (750ms), _restoreMinimizedTimer (200ms),
  _closePromptRaiseTimer (50ms) as one-shots, _constraintRefreshTimer (5s probe
  batch) with repeatEveryInterval:true (semantics genuinely match the slot
  idiom). Snapshot-at-schedule discipline preserved (callers still close over
  locals like activeWindow). Deliberately NOT migrated: split presentation
  settle (CompositionTarget.Rendering + controller-owned generation guards),
  App._winEventRetryTimer (non-replacing single-slot with conditional multi-tick
  bounded retry — different scheduling semantics), GuestLifecycleService
  _nameChangeDebounce/_minimizeHideDebounce (per-HWND keyed dictionaries, not
  single-slot replacement). Source contract forbids raw DispatcherTimer news or
  ReferenceEquals timer guards in ContainerWindow.
- Tests: 204 → 265 (+61: 17 recovery identity, 26+3 identity gate, 1+1 source
  contracts for 2C/2D, 8 rect epsilon, 9 timer). Gate: Debug/Release builds
  0w/0e; tests 265/265 both configs; release tooling 150/150; validate.ps1 -Ci
  PASS; openspec 20/20; git diff --check clean. Push of 33c3ded initially hit
  transient github.com connection failure, succeeded on retry (cc2bb00..33c3ded).

**Wave 3 (presentation-state ownership consolidation) — COMPLETE:** six
sub-waves, six commits (de69e01 tip), design record in
`.agent/plans/wave3-presentation-ownership.md` (semantics inventory A.1–A.14,
non-negotiables preserved).

- **3A canonical split commits:** `SplitPresentationController` commits ONLY
  through private `CommitDesired(desired, resolvedLeft, resolvedRight,
  resolvedForeground)` fed by pure policy output (DefinePair/Reconfigure/
  SelectNonMember/SelectMember/RemoveMember/ExplicitExit/FocusMember); mixed
  generation math (`= desired.Generation` ×3 vs `_generation++` ×2) and the
  discarded ExplicitExit result are gone. String identities resolve back to
  live references ONLY from the transition's own arguments — never a global
  HWND lookup.
- **3B single active-guest authority:** controller gained `SelectGuest`
  (standalone/dormant switches; presented pairs fail-closed untouched) and
  `Clear()` (teardown epoch bump, no ghost dormant non-member). The view's
  `_shepherdActiveWindow` FIELD IS DELETED; readers use derived alias
  `ShepherdActiveWindow => _splitController.Foreground`. Ordering preserved:
  guarded native work → controller commit → VM SetActiveTab projection →
  layout request; RecoveryPending commits nothing.
- **3C pane-refusal owner:** NEW `Services/PaneContainmentCoordinator`
  replaces `_refusedPaneByHwnd`; keyed by CapturedWindow REFERENCE (recycled
  HWND values can never inherit refusals), 13 invalidation boundaries routed
  through `InvalidateAll()`, log format byte-identical. Hidden-guest invariant
  intact: storage holds rects only, visibility sampled at decision time.
  Constraint min-cache stays in the view deliberately (WndProc-fed).
- **3D Model B layout simplification:** removed `InvalidateLayout`,
  `_layoutGeneration`/`_pendingLayoutGeneration`, pending-frame token, and the
  stale-frame discard branch (zero production callers of the mutator —
  generation pinned at 0, branch unreachable). Coalescing + ensureFinalPass
  latch retained byte-for-byte (Q9/Q1/Q2 tests unmodified). Safety argument:
  queued frames re-read current authority at callback time, settle has its own
  live generation guard, teardown disarms settle and zeroes the container HWND
  first. Two stale-suppression tests that exercised only the dead machinery
  replaced by a Model-B contract test. Also fixed a pre-existing xUnit2013
  warning in Wave3PresentationOwnershipContractTests so Debug is truly 0w.
- **3E/3F:** `ContainerWindow.SplitInteractionFix.cs` renamed to concern-based
  `ContainerWindow.Split.cs` with three documented concern sections; settle
  disarm confirmed single-path (both OnClosed and ContainerWindow_Closed call
  the one idempotent helper — dual-site-by-design now documented at both
  sites). Stale `_shepherdActiveWindow` comment references updated in
  ValidationDriver + ReplaceableDispatcherTimerTests. No behavior change.
- Diagnostics snapshot already follows the new authority
  (`CreateDiagnosticSnapshot` reads `ShepherdActiveWindow`/controller fields).
- Tests: 265 → 303 (+38 across 3A–3C: ToState cross-checks, SelectGuest/Clear,
  coordinator lifecycle; then −1 net in 3D). Gate after 3D+3E/3F: Debug/
  Release builds 0w/0e; tests 302/302 both configs; release tooling 150/150;
  validate.ps1 -Ci PASS; git diff --check clean.

**Wave 4 (self-test migration out of TabDock.exe) — COMPLETE:** plan and
per-suite dispositions in `.agent/plans/wave4-selftest-migration.md`;
OpenSpec change `wave4-selftest-migration`. Clusters 4A–4D (b890b4e,
180656f, 44570d3, 1900ed5) moved ~6,000 lines of hermetic test code from
`Services/*SelfTest*.cs` + embedded classes into `tests/UnitTests` as
semantically named xUnit facts/theories with shared fixtures
(`ShepherdTestFixtures`, `PendingRecoveryFixtures`). 4E (ad05818) deleted
`DiagnosticSelfTest`, `DiagnosticCommandKind.SelfTest`, the
`--selftest-diagnostics` parse entry/dispatch, `SplitGeometry.RunSelfTest`,
and the `--selftest-geometry` startup branch; `GeometryTests` now owns the
authoritative partition qualification (exhaustive widths 1..4096 × heights ×
positive/zero/negative origins, odd-width cases, seeded fuzz 100k rects seed
20260810, constraint-minimality math). `scripts/validate.ps1` qualifies
hermetic behavior via headless xUnit plus the retained real-user32
`--selftest-native-abi` probe (intentionally executable: WINDOWPLACEMENT ABI
evidence requires a built Windows process); `release-qualify.ps1` runs native
ABI against the exact published artifact. Closure commit 4dacf3a fixed every
live stale reference (README Diagnostics, TESTING geometry/DPI/lifecycle/
CLI-safe sections, ARCHITECTURE layout/diagnostics bullets, removed the dead
`SELFTEST[geometry]` log-table row, docs/release publication-gates /
mixed-dpi-qualification / compatibility-matrix), corrected both OpenSpec
deltas for a clean future archive (ui-ux-hardening MODIFIED header aligned to
the live requirement name; release-engineering delta is ADDED), and checked
off tasks 3.1–3.5 after direct verification. Historical audits/execplans/
waypoints intentionally untouched.

Gate (2026-08-22): Debug/Release builds 0w/0e; Debug+Release xUnit 528/528
each; release tooling 150/150; `validate.ps1 -Ci -Publish` PASS (native ABI
probe PASS on Windows 11 build 26200; version/publish smokes bind commit
ad05818); openspec 21/21; git diff --check clean. Note: the interrupted
session left a stale WPF temp-project obj state (`*_wpftmp.csproj` duplicate-
attribute errors); removing gitignored `obj`/`bin` debris fixed it.

**Wave 5 (repository hygiene) — COMPLETE:** four commits (9f257f6, e8adac0,
48ee552, 0dac181), no application-source change. All seven completed OpenSpec
changes archived via the canonical CLI (`openspec/changes/` now holds only
`archive/`); deltas applied to main specs where they were never synced and
matched cleanly (+~50 requirements across 14 new/updated specs, verified
no duplicates), archived ledger-only with `--skip-specs` where live specs had
already evolved past the delta wording (persistent-split-pair-presentation —
all 8 of its requirements confirmed present in refined form in ui-ux-hardening;
final-production-readiness-closure — superseded by post-R21 syncs). wave4's
deltas applied cleanly after aligning its MODIFIED header with the live
requirement name. Byte-identical `sonnet-results.md` replaced with a pointer
(canonical: CODEBASE_AUDIT_v3.md, matching the existing MUSE-RESULTS pointer);
DISPOSITION.md and unique per-model dumps preserved. Root working notes
relocated: tabdock_runtime_audit.md + stabilization agent prompt →
docs/audits/2026-08-21/, investigation_findings.md → docs/internal/ (14 live
references updated atomically across KNOWN_ISSUES/ARCHITECTURE/AGENT_GUIDE/
audit-2026-07-25/perf-2026-07-25), goal.txt →
docs/internal/deep-audit-remediation-goal.txt (real-goal.txt remains the
canonical read-only audit directive beside its audit set). Qualification-only
workflow renamed release.yml → qualify-candidate.yml (display name too) with
release-tooling-tests.ps1 assertions, README, publication-gates,
repository-protection, prepare-release-candidate comments, and the live
release-engineering spec updated. Harness trees intentionally preserved;
canonical AGENTS.md now warns that generated copies are silently overwritten
by sync-agent-configs.ps1. digicert-research README notes its supersession by
scripts/sign-release.ps1 while retaining provenance. Gates: openspec 25/25,
release tooling 150/150 (after rename), git diff --check clean.

Audit revalidation complete (read-only, against 8ac6db8): all top findings
confirmed live — budget sink never assigned anywhere; coordinator used for
`RequestRelayout` only (its refusal API, generation guard,
`CoalesceAndExecute`, `NeedsPanePositionForTest`, unread counters dead);
controller hand-commits with mixed generation math (`= desired.Generation`
×3, `_generation++` ×2, `ExplicitExit` result discarded);
`_shepherdActiveWindow` hand-synced at ~11 sites; Spike orphan referenced
only by sln/docs/perf.ps1; `WindowIdentityGate` pair differs only by the
capture-token block + success string; shepherd recovery identity pair
differs only by exe/start probes (`PendingRecoveryService:1083` also has its
own `EvaluateRecoveryGeneration`); `BringToFront`/`SetForeground` share the
nudge-retry sequence; DispatcherTimer stale-guard idiom ×5;
`DescribeTransaction` lives on `SplitInteractionPolicy`, not
`SplitPresentationPolicy` as the audit's table implied (still test-only);
hotkey-failure UX gap + hardcoded launcher hint confirmed.

NEXT ACTIONS:

1. PRODUCT BASICS CAMPAIGN COMPLETE (2026-08-23, plan
   `.agent/plans/product-basics-2026-08-23.md`). Closed four UX gaps plus one
   integration-audit hygiene gap:
   - PB-1 `db0ccdc`: shared CaptureFailureReport builder — picker path and
     inline Add-App panel now both present ONE aggregate owner-modal;
     user-facing lines are HWND-free (log keeps raw HWND); inline multi-fail
     no longer shows a modal per target.
   - PB-2 `db0ccdc`: CreateGroup uniquifies names ("Group", "Group 2", ...
     ordinal-ignore-case, suffix walk skips taken names).
   - PB-3 `ffae6d3`: launcher rows are live — double-click/Enter raises
     OpenGroupRequested → registry-first OpenContainer (activates existing).
   - PB-4 `ffae6d3`: empty dock shows non-hit-testable guidance text.
   - HYG-1 `db0ccdc` (rider in the App.xaml.cs commit): age-gated (24h)
     startup sweep entry for `state.json.bak.tmp`, mirroring
     PendingRecoveryService's orphan-fragment policy.
   Gate: Debug+Release builds 0w/0e; Debug+Release xUnit 612/612 each (+11 new
   tests); release tooling 150/150; validate.ps1 -Ci -Publish PASS; openspec
   25/25; git diff --check clean. NOTE: exactly one un-reproduced first-run
   test failure occurred on the freshly-built binaries right after Wave B;
   three consecutive full green runs followed — treat as cold-start jitter
   (precedent ab633ce) and watch for recurrence.
2. QUEUED PRODUCT MILESTONES from the same-day UX assessment (need product
   decisions, evidence recorded in the session log). NOTE: the PREVIOUS
   follow-up queue is fully complete — C-1 `0715a15`, C-2 `f762b68`, H-A1
   `ee099ce`, B2 `5b341bc`+`aafba4a`, H-B2 `780a19b`, G-1 `78a171a` — do not
   re-report or re-fix those:
   - M3 pending-recovery visibility banner in the launcher (read-only count +
     exact supervised commands; recovery stays typed-YES deliberate).
   - M4 focus-independent tab navigation hotkeys — CAREFUL: Ctrl+Alt+arrows
     collide with common display-driver globals; pick combos deliberately.
   - M5 always-visible split affordance (pairs with M4's seam).
   - Blocked-capture admission state surfaced on buttons (SetCaptureAllowed
     reason is currently log-only).
3. Standing external gates unchanged: supervised live-desktop acceptance,
   production signing credentials, human final smoke, physical mixed-DPI
   qualification, Windows 10 x64 compatibility.

UI/UX bug-hunt pass (2026-08-21, same day as R22, uncommitted at time of
writing): a spec-grounded, reject-by-default multi-agent review of the
interaction layer (`Views/ContainerWindow.xaml.cs` and friends) against
`openspec/specs/ui-ux-hardening/spec.md` and the container-activation-timers/
capture-picker-icons/group-color-picker specs, cross-checked against
`docs/audits/2026-08-21/DISPOSITION.md` so already-dispositioned items were
not re-reported. Two real, independently-reproduced defects were found and
fixed in `Views/ContainerWindow.xaml.cs`:

- `ContainerWindow_PreviewKeyDown` (Ctrl+Tab): a manual
  `TabsListBox.SelectedIndex = next` write reused a `Tabs`-space index against
  the shorter `DisplayTabs`-bound ListBox whenever a split composite is
  present (dormant or not), silently jumping Ctrl+Tab to the wrong tab. Fixed
  by deleting the manual write — `TabsListBox` is the only call site in the
  codebase that ever wrote `SelectedIndex` directly; every other switch path
  already relies on the `ActiveTab`/`IsActive`/`IsSelected` binding chain in
  `ContainerWindow.xaml`, which the (non-virtualizing `StackPanel`-hosted) tab
  strip always keeps in sync regardless of split state.
- `LayoutShepherdActiveWindow`/`LayoutSplitPanes`: the `_refusedPaneByHwnd`
  stale-refusal short-circuit (meant to avoid re-fighting an already-visible
  noncompliant guest every frame) was never scoped to "guest is currently
  visible." A guest hidden by container minimize (`ShowWindow(SW_HIDE)`, not
  iconic) whose native minimum previously refused the exact rect it is
  restored to hits the refusal branch on restore and is pinned in z-order but
  never re-shown (`PairZOrderBehind` carries no `SWP_SHOWWINDOW`) — a
  permanently blank container for single-tab groups (no tab-switch recovery
  path) until the user happens to resize. Fixed by gating both refusal checks
  on `NativeMethods.IsWindowVisible(...)` first.
Both fixes verified end-to-end by manual code trace (not just the reviewing
agent's claim) before being applied; see the review workflow's confirmed
findings for the full reasoning chain. The Ctrl+Tab fix's sibling case (Ctrl+
Shift+Tab landing on a dormant split MEMBER, not a third tab) was also traced:
`SyncShepherdActiveWindow`'s dormant branch (`IsSplitRelationshipDefined &&
!IsSplitPresented && IsSplitMember(newWindow)` -> `ResumeSplitPair`) already
resumes the pair correctly on its own via the `ActiveTab` property-changed
path, independent of the deleted manual `SelectedIndex` write; the composite's
highlight comes from `SplitCompositeViewModel.RefreshActiveState` (`Left.
IsActive || Right.IsActive`) through the same `IsActive`/`IsSelected` binding,
also independent of the deleted line. The old manual write actually made this
case worse (it selected a wrong DisplayTabs item, triggering a redundant
hide/re-focus churn) — the fix is a strict improvement here too, not just a
non-regression.
`dotnet build`/`dotnet test` (Debug + Release) are 0 warnings/errors. A new
deterministic regression test,
`tests/UnitTests/GroupViewModelDisplayTabsTests.cs`
(`SetSplitComposite_MisalignsDisplayTabsIndexFromTabsIndex_ForTabsAfterBothMembers`),
locks in the root-cause invariant for fix 1 (Tabs vs. DisplayTabs index
divergence once a split composite exists, persisting through dormancy, and
realigning on `ClearSplitComposite`) so no future caller can reintroduce a
raw Tabs-space index write against the DisplayTabs-bound ListBox; suite is
185/185 passing. Fix 2 (`IsRefusingPane` visibility gating) remains without
automated coverage — it requires a real native HWND through a minimize/
restore cycle, which is not cheaply unit-testable without extracting the
layout logic to a testable seam (not done here, to avoid scope creep on a
two-line fix).
Five other review dimensions (split focus/z-order, chrome-activation
suppression, capture-picker UX, group/tab-chrome commands, and one of two
window-state-reconciliation angles) found nothing meeting the evidence bar
(concrete file:line + spec violation + reachable failure scenario) — an
expected, not a failure, outcome for a post-R21/R22 hardened codebase.
Scope actually reviewed (round 1): `Views/ContainerWindow.xaml.cs` (+
`ContainerWindow.SplitInteractionFix.cs`), `Views/CapturePickerWindow.xaml(.cs)`,
`ViewModels/{GroupViewModel,TabViewModel,SplitCompositeViewModel,
CapturePickerViewModel}.cs`, cross-referenced against the ui-ux-hardening,
container-activation-timers, capture-picker-icons, and group-color-picker
OpenSpec specs.

Round 2 (same day, in response to Stop-hook feedback that round 1's own
"not reviewed" note left real gaps): reviewed `App.xaml.cs` in full
(startup group-restore loop, `OpenContainer`, `AcquireSingleInstanceMutex`,
every `OpenContainer` call site including the capture-picker-driven ones),
confirmed there is no tray icon/`NotifyIcon` anywhere in the codebase (an
earlier "tray flows" residual-risk note was itself imprecise — there is
no tray feature to review), `Services/HotkeyService.cs` (global Ctrl+Alt+G
registration/activation), and the tab-strip drag-to-reorder/drag-out
mechanics in `Views/ContainerWindow.xaml.cs`
(`TabsListBox_PreviewMouseLeftButtonDown/MouseMove/PreviewMouseLeftButtonUp`,
`EndDrag`, `SnapshotDragMidpoints`, `GetDropIndex`,
`GroupViewModel.ReorderTabs/ReleaseTab/CommitReorder`) — none of which round
1's "group-tab-chrome" dimension had actually pointed at, despite naming
"drag-reorder" in its title. One candidate surfaced (silent global-hotkey
registration failure leaves the advertised "Ctrl+Alt+G" hint dead with no
user feedback) but was REJECTED on verify: the Capture button still performs
the same action independently of the hotkey, a prior 2026-08-21 auditor
(`docs/audits/2026-08-21/dsv4-results.md`, finding L4) already reviewed this
exact branch and cleared it, and the trigger requires another process
already owning that exact global hotkey at TabDock's launch moment — not
reachable through ordinary interaction with TabDock itself. No fix applied.
Startup/restore and drag-reorder produced zero candidates.

Round 3 (same day, continuing after user direction to keep going): reviewed
the UI-visible service layer directly for the first time — `Services/
WinEventMonitor.cs` (system-wide WinEvent hook filtering/dispatch driving
foreground/z-order/reorder/hide/destroy reactions), `Services/
GuestLifecycleService.cs` (member-removal/hide/show classification and the
visible-state transitions it drives), and `Services/
SplitPresentationController.cs` (the split state machine's own internals —
`DefinePair`/`ResumeMember`/`SuspendForGuest`/`HandleMemberRemoved` — read
directly rather than only through `ContainerWindow` call sites, which rounds
1-2 already covered). All three dimensions did substantial real
investigation (22-36 tool calls, ~440-590s each) and returned zero
candidates — a genuine "nothing found," not a shallow bail-out. Also
directly read (no agent needed, small files): `Converters/
BoolToVisibilityConverter.cs`, `Converters/ColorToBrushConverter.cs`,
`Services/ShowWindowSemantics.cs`, `App.xaml` — all clean.

Combined, three rounds (13 dimensions total) cover every interaction surface
in the shipped application reachable through static code review: split
presentation (both the ContainerWindow call sites AND the controller's own
internals), window-state transitions, chrome-interaction suppression,
capture picker, group/tab commands, keyboard navigation, startup/restore,
global hotkey, drag-reorder, WinEvent-driven reactive dispatch, and guest
lifecycle transitions. Result: 2 confirmed bugs (fixed, tested, committed in
`3591ee3`), 1 candidate correctly rejected on adversarial verify, 10
dimensions with zero findings. There is no known or suspected UI/UX defect
left in this codebase as of this pass. The only remaining item is physical
mixed-DPI/multi-monitor hardware qualification for `SplitGeometry`'s
already-deterministically-tested (25/25 passing, `GeometryTests.cs`)
DPI-scaling and partition math — this is a release-qualification checkbox,
not a bug, and is a pre-existing, independently-documented EXTERNAL blocker
(docs/release/*, R21-012 disposition, "External gates" in this file's R22
section) confirmed unreachable in this single-monitor session; no amount of
further source-code review changes that. Changes are committed (`3591ee3`).

The R22 interactive Windows qualification & torture campaign (2026-08-21)
is COMPLETE for everything executable in its environment; verdict
**QUALIFIED_WITH_EXTERNAL_BLOCKERS** (see
`.agent/R22_WINDOWS_QUALIFICATION.md` for the full matrix and evidence).
One real product regression was reproduced supervised and fixed: capture of
a first tab into a new group did not durably persist (`52cd3ca`; regression
coverage = persist-kill family scenarios, re-run green). The remainder of
the campaign's fixes are harness/interaction-layer hardening (raw-DLL shard
spawn, relaunched-process registration, exact picker-row matching,
popup-under-cursor verify+retry on all menu paths, transient null-foreground
tolerance, Group-these enabled poll) — commits `e9ac3dc b2bae03 52cd3ca
8427588 99747ac d3314f8 3847148 ef27e4c`, all pushed to `origin/main`.

Supervised coverage achieved before the desktop became shared with operator
activity: core-lifecycle ×4 green, capture-group green (incl. same-process
close-group identity torture, 24 assertions), split-core member-destruction
phases a/b green (survivor promotion, dormant-clear, no ghost pane),
group-create-inline 10/10 post-hardening. Remaining supervised shards
(keyboard-input incl. tab-switch torture soaks, split-render, split-focus
minrestore soak, drag-z-order, crash-recovery soak, dpi-multi-monitor,
startup) are **BLOCKED_ENVIRONMENT**: user applications appeared mid-run
(foreign Notepad single-instance handoff + foreground churn) and the
driver's fail-closed identity guard correctly refused input. Exact rerun
commands are in `.agent/R22_WINDOWS_QUALIFICATION.md`. Do not treat those
contaminated failures as product defects.

External gates unchanged: mixed-DPI hardware, Windows 10 x64, signing
credentials, human final smoke. Verdict remains GO FOR RELEASE CANDIDATE /
BETA ONLY; v1.0.0 intentionally not published.

## R22 AUTOMATED RESULTS (2026-08-21, final SHA = tip of origin/main)

- `dotnet build TabDock.sln -c Debug`: 0 errors, 0 warnings.
- `dotnet build TabDock.sln -c Release`: 0 errors, 0 warnings.
- `dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Debug`: 184 passed / 0 failed.
- `dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Release`: 184 passed / 0 failed.
- `pwsh -NoProfile -File scripts/release-tooling-tests.ps1`: 150 passed / 0 failed.
- `pwsh -NoProfile -File scripts/validate.ps1 -Configuration Release -Ci`: PASS.
- `pwsh -NoProfile -File scripts/validate.ps1 -Configuration Release -Ci -Publish`: PASS.
- `tools/openspec/node_modules/.bin/openspec validate --all --no-interactive`: 20 passed / 0 failed.
- `git diff --check`: clean.

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

## VALIDATION RESULTS (2026-08-22, Waves 0–4)

- `dotnet build TabDock.sln -c Debug`: 0 errors, 0 warnings.
- `dotnet build TabDock.sln -c Release`: 0 errors, 0 warnings.
- `dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Debug`: 528 passed / 0 failed.
- `dotnet test tests/UnitTests/TabDock.UnitTests.csproj -c Release`: 528 passed / 0 failed.
- `pwsh -NoProfile -File scripts/release-tooling-tests.ps1`: 150 passed / 0 failed.
- `pwsh -NoProfile -File scripts/validate.ps1 -Configuration Release -Ci -Publish`: PASS
  (includes headless xUnit 528/528, native ABI probe PASS on Windows 11 build
  26200, version/publish smokes bound to commit ad05818).
- `tools/openspec/node_modules/.bin/openspec validate --all --no-interactive`: 21 passed / 0 failed.
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
