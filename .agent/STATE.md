# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session (`git rev-parse HEAD`,
`git branch --show-current`, `git status`). This file never records a
self-referential current SHA or a hosted-CI result for the commit containing
this text.

## CURRENT STATUS

## ARCHITECTURE-HARDENING CAMPAIGN (started 2026-08-22, IN PROGRESS)

Objective: act on `docs/audits/2026-08-22/IMPROVEMENT_REVIEW.md` in waves
(0 regression net → 1 dead scaffolding → 2 correctness dedup → 3 presentation
ownership → 4 self-test migration → 5 repo hygiene), preserving every
Shepherd/native-window invariant. Wave definitions are in the session goal.

**STATUS: Waves 0–1 COMPLETE — implemented, verified, committed, pushed.**
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

1. WAVE 2 — low-risk correctness-preserving deduplication: recovery identity
   evaluation, WindowIdentityGate pair, foreground grant sequence, rect
   epsilon comparison helper, single-shot DispatcherTimer pattern. Standard
   wave gate after; per-wave commit + push.
2. Waves 3–5 per the campaign goal ordering.

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
