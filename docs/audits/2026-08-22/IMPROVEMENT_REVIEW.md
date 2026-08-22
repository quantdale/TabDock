# TabDock Deep Improvement Review — 2026-08-22

**Scope:** whole-repo, read-only. **No code changes, no commits.** This is a
review artifact only.

**Axis (deliberate):** complexity, maintainability, design, test-ROI, and
repo signal-to-noise. This pass explicitly does **not** hunt for correctness or
security defects — that ground has been scorched by the R21/R22 campaigns and
the 2026-08-21 multi-model audit. Re-reporting those would be noise.

**Method:** six read-only reviewers over disjoint clusters (native/shepherd
core; UI layer; recovery/persistence/diagnostics; split/lifecycle/policy;
test corpus; non-source scaffolding). Every reviewer was held to one rubric:

> For each finding, decide **load-bearing vs. ceremony**. Windows
> window-shepherding without reparenting is *genuinely* hard — z-order races,
> DPI probing, recycled-HWND identity, crash recovery of external HWNDs. Most
> of the 48k lines of C# maps to a real Win32/product hazard and is defensible.
> A finding only counts if it has a concrete `file:line` **and** a reason a
> senior engineer who already read `docs/ARCHITECTURE.md` would *still* change
> it. Generic "extract method / add XML docs / rename" is rejected.

The framing throughout is **"interest you are now paying," not "you over-built
this."** The engineering here is careful and the hard parts are proportionate
to the problem. The findings below are the small, nameable subset where the
code has drifted from its own stated design, or where scaffolding leaked into
places it does not belong.

---

## 1. Executive thesis

TabDock is **structurally sound product code carrying two forms of accumulated
interest**:

1. **Half-realized extractions in the presentation layer.** The R21 refactor
   correctly extracted `SplitPresentationController`,
   `PresentationLayoutCoordinator`, and the pure `Split*Policy` classes. But in
   several places the *advertised* responsibility silently stayed behind in
   `ContainerWindow`/the controller, leaving **duplicated live state** and
   **tested-but-unreached machinery**. The layers exist; the wiring that would
   make them load-bearing does not. This is the single highest-value cluster to
   address because the duplication is a live drift hazard on the most
   correctness-sensitive code in the app.

2. **Test/diagnostic scaffolding compiled into the product assembly.**
   ~5,900 lines of self-test code and a dead "presentation budget" apparatus
   ship inside `TabDock.exe`, reachable only via undocumented `--selftest-*`
   flags or never reachable at all. An idiomatic xUnit host (`tests/UnitTests`)
   already runs in the same CI gate and references the product assembly, so most
   of this has a standard home it isn't using.

Everything else (native core, event pipeline, persistence, the release chain,
the deliberate multi-harness config) is in good health. A file census puts
application code + tests at ~28% of the 423 tracked files; the rest is process
scaffolding, most of which is load-bearing, with a small pocket of genuine
clutter (~0.85 MB of one-time audit dumps and stale root working notes).

---

## 2. Top findings, ranked (with cross-agent corroboration)

The strongest findings are the ones **multiple independent reviewers reached
without coordination**.

| # | Finding | Sev | Corroboration |
|---|---------|-----|---------------|
| 1 | **`PresentationOperationBudget` sink is dead in production.** `WindowShepherdService.BudgetSink` and `ContainerWindow.PresentationBudget` are never assigned by product *or* test; no `PresentationOperationCounter` is constructed outside tests. Every `_budget?.Record*(...)` call on hot native paths is unreachable. `RecordContainerRaise` is declared, implemented, and never called. It is a test double living in the product assembly. | Med | **3 agents** (recovery/diag, split/lifecycle, test-ROI) + AUDIT-005 in the 08-21 dump |
| 2 | **`PresentationLayoutCoordinator` is a half-realized extraction.** ARCHITECTURE §2 says it "owns generations and redundant suppression," but production constructs it with no ops/budget and calls only `RequestRelayout` (1 of ~14 members). Its `_refusedPaneByHwnd` + `IsRefusingPane`/`MarkRefusingPane`/`ClearAllRefusals` are a **field-for-field duplicate** of the same state still living in `ContainerWindow` (cleared at ~13 sites there). Its stale-frame generation guard is **dead**: the only mutator (`InvalidateLayout`) has zero production callers, so `_layoutGeneration` is pinned at 0. Two copies of one suppression concept = live drift hazard; the coordinator's copy is unreached. | High | **2 agents** (UI, split/lifecycle) independently |
| 3 | **~5,900 lines of self-test code ship inside the product binary.** `Services/*SelfTest*.cs` (5,110) + inline self-test classes in `DiagnosticCommandLine.cs` (~792, incl. test doubles `FakeApi`/`EventApi`/`RecordingContext`, a temp-dir dispatcher-pumping picker harness, and a `Parallel.For(0,512)` stress loop) + smaller inline suites (~24). Reachable only via `--selftest-diagnostics`/`--selftest-native-abi`; **never on normal startup**. Cost is *not* bytes (~0.1% of the 180 MB self-contained bundle) — it's maintenance (~1:1 test:product in the recovery subsystem), surface (test harnesses reachable in the customer exe), and comprehension (can't tell product from test by namespace). `tests/UnitTests` already runs against the product assembly in the same CI gate. | High (as a class) | recovery/diag agent, corroborated by test-ROI agent |
| 4 | **`SplitPresentationController` discards its policy's output.** It calls `SplitPresentationPolicy.DefinePair/SelectMember/…` (each has exactly one caller — here), then ignores the returned state and re-derives `_left/_right/_presented/_foreground` by hand; generation math is done *both* ways (`= desired.Generation` in 3 methods, `++` in 2). `SplitPresentationControllerTests` asserts only on the controller's own fields and never cross-checks `ToState()` against a policy result, so the policy's separate tests protect no production path. Two parallel state machines with no reconciliation. **Keep** the `SplitPresentationState` record, `IsCurrentSettle`, and the `Classify` signature — those are used. | Med | **2 agents** (split/lifecycle, test-ROI) |
| 5 | **Split *transition sequencing* still lives in the 3477-line `ContainerWindow`.** The extraction moved split *state* into the controller but left ~600 lines of orchestration (`EnterSplit`/`ExitSplit`/`ResumeSplitPair`/`HandleSplitMemberRemoved`/`FocusSplitMember`/`Suspend…`) in the view, each hand-syncing `_shepherdActiveWindow` to the controller's `Foreground` at ~8 sites with nothing enforcing the equality. This is the concrete betrayal of the view's own docstring claim of "a single runtime authority (no parallel state machine)." Drift desyncs the tab highlight / `Group.ActiveIndex` from the z-top guest. | High | **2 agents** (UI, split/lifecycle) |
| 6 | **`Spike/TabDock.Spike/Program.cs` (904 lines) is orphaned.** An experimental "survival spike" for the **reparent** architecture that the 2026-07 Shepherd migration deleted. Referenced only by `TabDock.sln`; no product/test code touches it. Still costs solution-build compile and reader confusion. | Med | UI-scan + test-ROI agent |
| 7 | **Dead test-only shims in the product assembly.** `PresentationLayoutCoordinator.CoalesceAndExecute` (a no-op that sets a bool and calls `execute()` once — its 3 `CoalescedRelayout_*` tests are tautologies) and `NeedsPanePositionForTest` (zero callers anywhere). `Models/SplitPresentationPolicy.ResolveNativeTransition`/`DescribeTransaction` (no production callers). `SplitInteractionPolicy` dormant-non-member branch returns `None`, identical to the fallthrough, wrapped in self-contradicting commentary; `return ResumeMember` at the top is unreachable. | Low–Med | **2 agents** (split/lifecycle, test-ROI) |

---

## 3. Findings by cluster

### 3.1 Native / Shepherd core — *health: irreducibly cohesive, two clean dedup misses*

`WindowShepherdService` (2903 lines) earns most of its size: mutation-boundary
revalidation, the two-tier identity gate, DWM suppression, and
journal-before-mutation all map to documented Win32 hazards and correctly
resist the "extract a method" critique. The genuine outlier is the ~950-line
crash-recovery **journal store** bolted into the same file as the live
presentation authority.

| file:line | sev | load-bearing | finding |
|---|---|---|---|
| `WindowShepherdService.cs:1455` (`IsContainerBelowGuest`) vs `ZOrder.cs:22` | Med | No — reuse miss | Hand-rolls the exact `GW_HWNDPREV` upward walk that the pure, unit-tested `ZOrder.IsOrderedAbove` already implements. `ZOrder` exists *precisely* to make this invariant testable without real HWNDs; the live pin bypasses it → tested predicate and live predicate can silently drift. (`IsPairingSatisfied:1475` does invisible-helper skipping and is genuinely distinct — leave it.) |
| `WindowShepherdService.cs:2340` & `:2436` (`EvaluateRecoveryIdentity`/`EvaluateRecoveryGeneration`) | Med | No — must stay lockstep | ~90 lines duplicated; the "generation" variant is the "identity" variant minus exe-path/process-start checks. The instance-side `EvaluateCurrentCapturedWindow:855` already parameterizes exactly this strong-vs-cheap split with bools. The static recovery side copy-pasting two methods is an unenforced invariant on the most correctness-sensitive code. |
| `WindowShepherdService.cs:1958-2903` (journal region + static rescue) | Med | Partly — extract the store | The journal store (`RescueOrphanedWindows`, `LoadJournal`, `PreservePendingJournal`, schema migration/quarantine) has zero dependency on live presentation state and already owns its `IRecoveryNativeApi` seam. Extracting a `HiddenWindowJournal` collaborator would shrink the shepherd ~⅓ and make the store testable standalone. The journal-before-mutation *call sites* stay in the shepherd, so the safety ordering is untouched. |
| `WindowShepherdService.cs:1613-1625` & `:1649-1655` (`BringToFront`/`SetForeground`) | Low-Med | No | Both duplicate the `SetForegroundWindow → SendBenignKeyNudge → re-check-generation → retry-once` focus-steal-guard sequence; only difference is `BringToFront` repositions first. Extract `TryGrantForeground` — the retry is subtle enough that one copy diverging is a latent behavioral inconsistency. |
| 3× `SetTransitionsDisabled` DWM forwarders (`:90,:116,:2900`) | Low-Med | Mostly load-bearing | The per-transaction native-seam split is legitimate (tests prove "no mutation attempted" narrowly) — do **not** collapse the interfaces. But the three byte-identical DWM forwarders can share one implementation. |
| `NativeSnapshotService.cs:348` (`SplitEstimated`) | Low | No, diagnostic-only | Re-derives `floor(W/2)` instead of `SplitGeometry.Partition`, the "single deterministic definition." Read-only estimate, so correctness isn't at stake — but single-source-of-truth is cheap. |

**Ruled load-bearing (not flagged):** the repeated
`TryReleaseMutationBoundary(...)` pattern (each is a distinct named
pre-native-write revalidation — DRYing it erases diagnosability); single-caller
pure helpers `DeferredWindowPositionBatch`/`SplitGeometry`/`ShowWindowSemantics`/
`DpiCapturePolicy` (single-caller *is the point* — deterministic test seams);
`NativeMethods.cs` flat declaration table; the WINDOWPLACEMENT 44-byte contract;
DWM suppression; `NativeHwndHost` brush-ownership; `NativeMonitorDpiProbe`
thread-context save/restore.

### 3.2 UI layer (ContainerWindow + Views/ViewModels) — *health: debt concentrated in the two ContainerWindow partials*

The small VMs and converters are healthy MVVM (`MainWindow.xaml` is textbook).
Debt is concentrated in `ContainerWindow`.

| file:line | sev | load-bearing | finding |
|---|---|---|---|
| `ContainerWindow.xaml.cs` split-orchestration + `_shepherdActiveWindow` hand-sync (~8 sites: `:2177,2791,2843,2949,3012,3021` + `SplitInteractionFix.cs:174,191`) | High | Logic yes, *location* no | See Top-Finding #5. The controller seam already exists (`IPresentationOperations`); the sequencing belongs behind it with the controller owning the single active-member value. |
| `ContainerWindow.SplitInteractionFix.cs` (whole file) | Med | Content yes, *partition* is ceremony | Split logic is scattered across two files with no principled seam — a partial named after a historical *fix*, not a concern. Concrete hazard: `DisarmSplitPresentationSettle()` is torn down from *both* `OnClosed` (partial) and `ContainerWindow_Closed` (main) — idempotent today, but two teardown sites for one lifecycle moment is exactly the confusion partial-by-fix produces. Rename/reorganize to a concern-based `ContainerWindow.Split.cs` with one teardown path. |
| `ContainerWindow.xaml.cs:72-81,2205-2325,2417-2443,2697-2726` (pane-containment: `_constraintMin*`, `_refusedPaneByHwnd`, `RefreshSizeConstraint`, `ComputeContainerMinTrack`, refusal tracking) | Med | Policy yes, *location* no | ~150 lines of cohesive, unit-testable policy (pure arithmetic + an HWND-keyed dict) parallels the already-extracted coordinator. Should be a `PaneContainmentCoordinator`. The `WM_GETMINMAXINFO` *hook* stays in `WndProc` (interop-forced); only the min-track *policy* moves. Leaving it inlined is why `_constraintDirty`/refusal bookkeeping is smeared across ~10 unrelated methods. **See §3.4 — this state is duplicated in the coordinator that was supposed to own it.** |
| `ContainerWindow.xaml.cs:438-477,660-683,940-961,1134-1148,3081-3124` (single-shot `DispatcherTimer` idiom) | Med | Op yes, *repetition* is ceremony | The idiom is copy-pasted 5× with the identical `if (!ReferenceEquals(_field, timer)) { stop; return; }` stale-guard — and that guard is the correctness fix behind AUDIT25-05/Q5/Q8. Five copies = five chances to omit the guard. A `SingleShotTimer` helper makes the safe pattern the only pattern (−~60 lines). The *repetition itself* is the defect vector. |
| `ContainerWindow.xaml.cs:714-719,2377-2384,2320-2324,2289-2293,2605-2608` (1px-epsilon rect compare) | Low | Comparison yes, *duplication* no | Reimplemented ~5×; a single `RectsMatchWithin(a,b,1)` removes the risk of one copy silently using a different epsilon/order. |
| `ContainerWindow.xaml.cs:1374-1438` (`ConfigureSplitMenuItems`) | Low-Med | Dynamic menu yes, *stringly-typed contract* no | Menu items tagged with magic strings (`"SPLIT-ACTION"`, `"SPLIT-SUBMENU"`, …) and re-found via `Tag is string s && s.StartsWith("SPLIT-")`. A typo in one string breaks idempotency silently. Replace with a typed marker. |
| `GroupViewModel.cs:59` (`AccentBrush` → `Brush`); `TabViewModel.cs:23`/`SplitCompositeViewModel.cs:36` (hardcoded `AutomationId`) | Low | Load-bearing but a real boundary cost | VMs surface View-layer types (`PresentationCore`/`PresentationFramework`), so they can't be unit-tested headless. Honest rationale exists (PERF25-06 frozen-brush cache; stable UIA ids). Stated as the axis only: if future headless VM tests are wanted, expose the color string + an id enum and convert in XAML. |

### 3.3 Recovery / persistence / diagnostics — *health: sound product code carrying a large parallel test corpus that ships to customers*

`PendingRecoveryService` (2241 lines) is **not** a god-class — it's a cohesive
supervised-recovery module (entry class + `RecoveryPhase` state machine + 15
supporting types + injectable seams). Recorded so a reader doesn't "fix" a
non-problem. The real finding is the self-test-in-binary class (Top-Finding #3).

| file:line | sev | load-bearing | finding |
|---|---|---|---|
| `PendingRecoverySelfTest.cs:1-2007` | High | Ceremony | 2007-line unit-test file in the product assembly, ~1:1 with the service. All 43 checks hermetic (`JsonNode` fixtures, no real HWND). `tests/UnitTests` already runs against the product assembly in CI — this belongs there. |
| `DiagnosticCommandLine.cs:197-988` | High | Ceremony (except native-ABI) | A "small dependency-free parser" (195 lines) carrying **792 lines of 8 inline self-test classes** incl. test doubles and a temp-dir WPF-dispatcher picker harness. This is why the naive "just exclude `Services/*SelfTest*.cs`" fix fails — ~792 lines live inline in a *production* file that `DiagnosticSelfTest.Run()` calls directly. |
| `DiagnosticCommandLine.cs:314-317` | Med | Ceremony | `DiagnosticSelfTest.Run` ends with `Parallel.For(0,512,…)` — a 512-way concurrency stress test on `DiagnosticTrace` shipped inside the exe. Textbook xUnit material. |
| `DiagnosticCommandLine.cs:376-537` (`NativeInteropSelfTest`) | Low | **Load-bearing (narrow)** | The **only** self-test with a real reason to run as a shipped/OS-native binary: it probes real user32's 44-vs-60-byte `WINDOWPLACEMENT` contract, and `build.yml:96-98` runs `--selftest-native-abi` on a *second* OS image for compatibility-matrix evidence. This is a *where-it-runs* concern, not *which-assembly*. Keep — but as a ~160-line standalone probe, not bundled with ~5,760 lines of hermetic tests. (Note: even this runs against a plain Release build, never the single-file R2R artifact — `validate.ps1:326` gives the published artifact only `--version` smoke.) |
| `PresentationOperationBudget.cs` + `WindowShepherdService` budget calls | Med | Ceremony (CI-only scaffolding in prod hot paths) | See Top-Finding #1. |

**Idiomatic alternative:** add one `[assembly: InternalsVisibleTo("TabDock.UnitTests")]`
(none exists today), move the hermetic suites into `tests/UnitTests` as `[Fact]`s,
keep a thin `--selftest-native-abi` probe (the sole on-machine value), and drop
the `--selftest-diagnostics` dispatch. **The naive fix is wrong** — the surface
is not filename-separable because ~792 lines live inline in production files.

**Cleared as load-bearing product (not flagged):** `PendingRecoveryService`,
`PersistenceService`, `DiagnosticReportService`, `DiagnosticTrace`,
`LoggingService`, `EnvironmentFingerprint`, `BuildIdentity`, `ConsoleSession`.

### 3.4 Split / lifecycle / policy — *health: event-side excellent; presentation-trio layering only half-realized*

The event pipeline (`WinEventMonitor → GuestLifecycleService → GroupManager
index → GuestHideProvenance`) is excellent, load-bearing engineering — each
layer removes a concrete hazard, no duplication. `WindowIdentityGate`,
`ProductMutationLease`, `HotkeyService`, `IconService`, all Models are clean.
The debt is entirely in the split-presentation trio.

| file:line | sev | load-bearing | finding |
|---|---|---|---|
| `PresentationLayoutCoordinator.cs:33,112-117` + `ContainerWindow.xaml.cs:81,2285-2308` | High | No — half-realized | See Top-Finding #2 (refusal-state duplicated between view and coordinator; coordinator's copy dead). |
| `PresentationLayoutCoordinator.cs:63-83` | High | No — dead safety branch | The stale-frame generation guard can never fire: `InvalidateLayout` (sole mutator of `_layoutGeneration`) has zero production callers. Heavily-commented, unit-tested machinery guarding a transition the wiring never produces. |
| `SplitPresentationController.cs:83-220` | Med | Partly | See Top-Finding #4 (discards policy output; parallel state machines). |
| `WindowIdentityGate.cs:219-380` | Med | No | `Evaluate` and `EvaluateBeforeCaptureToken` are ~90% identical (~70 lines each) incl. duplicated local `Mismatch`/`Unverifiable` helpers, differing only by a 6-line capture-token block. One method with a `requireCaptureToken` bool is equally testable and removes the mirror-both-sites tax on the most correctness-sensitive code in the cluster. |
| `SplitInteractionPolicy.cs:132-142` (+ unreachable `:113`) | Low | No — dead/confused | Dormant-non-member branch returns `None`, identical to the `:144` fallthrough, wrapped in self-contradicting commentary. `return ResumeMember` at `:113` is unreachable (earlier guards partition every case). Delete. |
| `ContainerWindow.SplitInteractionFix.cs:93-100` | Low | Partly — CI-determinism real | The sole production caller of `Classify` passes 5 of 7 inputs as compile-time constants and re-computes the button guard inline before calling, so in production `Classify` collapses to `if (isStaleIdentity) reject` — the button/member/presented decisions are made *twice*. Don't delete the classifier (earns its keep for CI determinism); route the inline guards *through* it so it's the genuine single source. |
| `Models/SplitPresentationPolicy.cs:137-141,171-179` | Low | No | `ResolveNativeTransition`/`DescribeTransaction` have no production callers (only tests/docs). Dead public API on a class documented as the shared production+CI contract. |
| `PresentationLayoutCoordinator.cs:22,95-103,108-109,119-126` | Low | No | `CoalesceAndExecute` (no-op shim) + `NeedsPanePositionForTest` (name says it) are test-only members in a shipped class; `_layoutSplitCount`/`_layoutSingleCount` incremented but never read. |
| `PersistedState.PersistedTab` vs `Group.PersistedTabMetadata` | Low | Borderline | Structurally identical 8-field classes hand-copied field-by-field (mapper in `GroupManager.ClearCapturedMembersAfterSessionEnding:541`). The domain/wire split is a recognized pattern kept so they *can* diverge; today they're identical, so every schema change touches both plus the copy. |
| `SessionEndingPolicy.cs` | Low | Borderline | A named static "policy" + dedicated self-test wrapping a two-line compare-and-set on a *caller-owned* `ref bool` (it doesn't own the state). The one-way-teardown invariant is genuine and worth a comment; a senior would likely inline it and drop the self-test. |

### 3.5 Test corpus — *health: mostly load-bearing; a concentrated pocket of theater + one 2-way redundancy*

The heavy `ValidationDriver` (real SendInput + UIA + BitBlt + GuineaPig target)
is **not** bloat — a window manager mutating external HWNDs earns it. 13 of 15
xUnit files drive real product types. The `Performance` harness is a legitimate
benchmark. The problems are narrow:

| file:line | sev | load-bearing | finding |
|---|---|---|---|
| `PresentationOperationBudgetTests.cs:60,:89,:359` | Med | No — theater | `NormalTab_*` and `CoalescedRelayout_*` invoke no production type — they script `FakePresentationOps`, then assert the counter echoed the script; `PresentationOperationCounter_ThreadSafeSnapshot` unit-tests a test helper. They pass regardless of product behavior. **Keep** the hybrid `PresentedSplit/DormantPair/SplitMemberFocus` cases and the "no redundant storm / hide-once" checks — those drive the *real* `SplitPresentationController` and catch duplicate native calls the state-only controller tests can't. |
| `DeterministicSelfTests.cs:57-177` vs `SplitPresentationPolicyTests.cs` | Med | Partial — redundancy | Two parallel corpora drive the **same real** `SplitPresentationPolicy` with the same assertions in two harnesses. Not theater (both hit production), but the driver's `--selftest split` adds no realism over xUnit on the same headless data. Consolidate to the xUnit authority. |
| `GeometryTests.cs:134` (`RunSelfTest_ReportsZeroFailures`) | Low | No | Wrapper asserting the in-product `SplitGeometry.RunSelfTest()` reports 0 failures; the direct `Partition`/`MinContentWidth` facts above already cover the math. 3rd copy alongside `--selftest-geometry`. Drop. |
| `RequestRelayoutFinalPassTests.cs:71` (`UnchangedLayoutUpdated_ProducesNoRelayout`) | Low | No | Can't guard its stated contract — the unchanged-rect suppression lives in the WPF caller `ContainerWindow_LayoutUpdated`, not the coordinator. The body calls `RequestRelayout` once and asserts it *did* relayout, contradicting the name. Coverage-gap dressed as coverage. |
| `Spike/TabDock.Spike/Program.cs` + `TabDock.sln:8` | Med | No | See Top-Finding #6. |

**Redundancy map:** split pure policy = xUnit ✕ ValidationDriver, *same
altitude → wasteful, consolidate*. `SplitGeometry` = 3 ways, drop only the
xUnit wrapper. Persistence = 3 ways but *different altitudes → justified*.
Controller transitions = state-tests ✕ op-count hybrid ✕ live scenarios →
*complementary, keep*.

### 3.6 Non-source scaffolding — *health: 61% Markdown; most load-bearing, ~0.85 MB genuine clutter*

**423 tracked files; 258 (61%) Markdown; code+tests ≈120 (28%).** That ratio is
an observation, not a verdict — most of the 72% non-code is load-bearing (live
capability specs, ARCHITECTURE/TESTING, the deliberate multi-harness feature, a
fully-wired release chain, well-justified `.gitignore`/`.gitattributes`).

| path | sev | load-bearing | finding |
|---|---|---|---|
| `docs/audits/2026-08-21/` (14 files, 790 KB) | Med | Only `DISPOSITION.md` | 11+ raw per-model audit dumps (kimi 197 KB, kimis 124 KB, alpha 115 KB, …) are one-time provenance already triaged into the 7 KB `DISPOSITION.md`. This one dated folder is ~2× the size of all application code. Raw dumps belong in git history / a tag, not perpetually in the working tree. |
| `docs/audits/2026-08-21/{CODEBASE_AUDIT_v3,MUSE-RESULTS,sonnet-results}.md` | Med | No | **Byte-identical trio, 66 KB of pure duplication** (`DISPOSITION.md` itself says they're identical). Replace two with a one-line pointer. |
| 6 completed OpenSpec changes still in live `openspec/changes/` | Low-Med | Yes (ledger) | All effectively complete (tasks 20/0, 20/0, 18/0, 28/0, 15/0, 15/**1**) yet unarchived beside the 15 that were. Pollutes "what's in flight" and inflates the live surface by ~54 files. Archive (moves, doesn't delete). |
| root: `tabdock_runtime_audit.md`, `tabdock_runtime_stabilization_agent_prompt.txt` | Med | No (orphaned) | Referenced only by the 08-21 audit that consumed them. Relocate under `docs/audits/2026-08-21/` or drop. |
| root: `investigation_findings.md` (39 KB) | Med | Weakly (8 refs) | Stale (2026-08-06) but referenced by ARCHITECTURE/AGENT_GUIDE/archived specs. **Don't delete** — relocate to `docs/internal/` and update the 8 refs. |
| root: `goal.txt` (45 KB) vs `docs/audits/2026-08-21/real-goal.txt` (44 KB, *differs*) | Low-Med | Weakly | Two divergent "goal" files invite confusion about which is authoritative. Consolidate; move the canonical one into `docs/`. |
| `.github/workflows/release.yml` | Low | Yes | Confusingly named: a workflow called `release` that its own header says "never publishes" (RC-qualification only), beside the real two-stage chain. **Not vestigial** — verified it shares `release-qualify.ps1`/`validate.ps1`/`release-tooling-tests.ps1` with Stage A. Rename to `qualify-candidate.yml`. |
| 8 harness trees + `.clinerules` (81 files, 478 KB) | Low | **Yes — deliberate feature** | 48 near-identical `SKILL.md` copies. Committing is largely *forced*: these tools discover config in place from the working tree, and `tools/openspec` generates them, so gitignore-and-generate would leave a fresh clone's non-`.claude` harnesses empty. `sync-agent-configs.ps1` is the right mitigation. **Do not delete 7 of 8.** Residual: the sync silently overwrites hand-edits to non-`.claude` copies — worth a one-line warning in `AGENTS.md`. |
| `.agent/investigations/digicert-research/*.ts` + `action.yml` | Low | Provenance | Superseded prototype of a signing action (production signing is `scripts/sign-release.ps1`). Acceptable in `investigations/`; add a one-line "superseded by sign-release.ps1" note. |
| `README.md` (29 KB) | Low | Yes | Unusually large for a single-utility README (larger than most source files). Worth a skim to confirm it hasn't absorbed content that belongs in `docs/`. |

**Cleared (not findings):** `KNOWN_ISSUES.md` (17 refs incl. live test source —
root-appropriate); all 9 `scripts/*.ps1` (referenced, no orphans);
`.gitignore`/`.gitattributes`/`.editorconfig`/`global.json`/`.mcp.json` (clean);
`openspec/specs/` (14 live capability specs — the authoritative spec surface).

---

## 4. Coverage map

Meeting the repo's own "genuine nothing-found vs. shallow bail-out" standard.
Every falsifiable "dead code / delete" claim in §2 and §5 was independently
re-verified by `grep` for callers/assignments before signing (all six held);
the design/decomposition findings are recommendations for the reader to assess,
not verified assertions. `App.xaml.cs` (1147 lines, the orchestrator) received
only a partial read on *this* axis (startup/self-test gating); its full
complexity read relies on prior-round coverage, not a fresh pass here.

| Cluster | Fully read | Skimmed / calibration | Skipped (why) |
|---|---|---|---|
| Native core | `WindowShepherdService` (2903), `NativeMethods` (1052), `NativeHwndHost`, `ZOrder`, `DeferredWindowPositionBatch`, `ShowWindowSemantics`, `SplitGeometry`, `DpiCapturePolicy`, `MonitorDpiService`, `NativeSnapshotService` | — | Identity-gate/recovery collaborators (owned by other clusters) |
| UI layer | `ContainerWindow.xaml.cs` (3477) ×3 passes, `.SplitInteractionFix.cs`, `ContainerWindow.xaml`, all 6 ViewModels, both Converters, `CapturePickerWindow.xaml.cs`, `MainWindow.*`, `CapturePickerResult` | `SplitPresentationController` (calibration) | `CapturePickerWindow.xaml` markup (thin fallback modal — no finding hinges on it) |
| Recovery/diag | all 10 `Services/*SelfTest*.cs` (counted; recovery+persistence read for hermeticity), `DiagnosticCommandLine` (988 full), `PresentationOperationBudget` (full), `PendingRecoveryService`/`PersistenceService`/`DiagnosticEnvironmentService` (structural), CI pipeline (`validate.ps1`, `build.yml`) | `DiagnosticReportService`, `DiagnosticTrace`, `LoggingService`, `EnvironmentFingerprint`, `BuildIdentity`, `RuntimeTelemetry`, `ConsoleSession` (confirmed load-bearing) | — |
| Split/lifecycle | `SplitPresentationController`, `SplitPresentationPolicy`, `SplitInteractionPolicy`, `PresentationLayoutCoordinator`, `GuestLifecycleService`, `WinEventMonitor`, `GuestHideProvenance`, `GroupManager`, `WindowIdentityGate`, `ProductMutationLease`, `HotkeyService`, `IconService`, `SessionEndingPolicy`, all Models | `ContainerWindow.*`, `WindowShepherdService` (collaboration) | correctness/native-race behavior (out of axis) |
| Test corpus | all 15 xUnit files, `PresentationOperationBudgetTests`, `SplitPresentationControllerTests`, `DeterministicSelfTests`, `Spike/Program.cs`; ValidationDriver + GuineaPig + Performance assessed as infra | — | deep per-scenario internals of ValidationDriver (assessed at the harness level) |
| Scaffolding | 423-file census; 8 harness trees md5-compared; `openspec/` (141) triaged into archived/live/specs; `docs/` (33) sized; 4 workflows; 9 scripts grepped for orphans; all config files; root working-notes cross-referenced | `docs/release/*`, `docs/internal/*` individual bodies | — |

---

## 5. Suggested prioritization (if any of this is ever acted on)

This is a review, not a work order. But if the interest is ever paid down, the
highest **value-to-risk** order is:

1. **Delete the dead scaffolding** (zero product-behavior risk, immediate
   clarity): `PresentationOperationBudget` sink + `RecordContainerRaise`;
   `PresentationLayoutCoordinator.CoalesceAndExecute`/`NeedsPanePositionForTest`
   + the dead generation guard; `SplitPresentationPolicy.ResolveNativeTransition`/
   `DescribeTransaction`; the `SplitInteractionPolicy` dead branch + unreachable
   return; the tautological budget/geometry-wrapper tests; the `Spike` project.
2. **De-duplicate within the correctness-sensitive code** (high
   drift-protection payoff): the two recovery-identity methods → one
   parameterized method; the two `WindowIdentityGate` methods → one; the
   `BringToFront`/`SetForeground` foreground-grant helper. **Not mechanical:**
   `IsContainerBelowGuest`→`ZOrder.IsOrderedAbove` is a *behavioral-equivalence*
   claim, not a safe textual dedup — it must be proven (the "harmless initial
   self-compare" difference confirmed truly harmless against real z-order walks)
   before the live pin is rerouted, or it risks introducing the very kind of
   z-order bug the R21/R22 campaigns closed.
3. **Resolve the presentation-trio half-extraction** (higher risk, do behind
   the existing tests + ValidationDriver): decide *per responsibility* whether
   the coordinator/controller/policy own it or the view does, then route state
   through one owner — eliminating the `_refusedPaneByHwnd` and
   `_shepherdActiveWindow` shadow copies and the discarded policy output.
4. **Move the hermetic self-tests to xUnit** (mechanical but touches many files;
   needs `InternalsVisibleTo`): keep only the ~160-line native-ABI probe
   in-product.
5. **Repo hygiene** (trivial): archive the 6 completed OpenSpec changes, prune
   the duplicate/raw audit dumps, relocate the root working notes, rename
   `release.yml`.

---

*Generated by a six-cluster read-only review, 2026-08-22. No product code,
tests, or configuration were modified. Findings are improvement opportunities,
not defects — the application's correctness and security posture is governed by
the R21/R22 campaigns and `docs/audits/2026-08-21/DISPOSITION.md`.*
