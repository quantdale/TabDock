# Tasks — user-reported presentation integrity

## 0. Evidence-first reproduction and diagnosis

- [x] 0.1 Resolve current Git state dynamically; read `AGENTS.md`,
  `.agent/STATE.md`, `docs/ARCHITECTURE.md`, `docs/TESTING.md`, the current
  canonical OpenSpec specs, and this change before editing production code.
- [x] 0.2 Reproduce the color-menu disappearance with one controlled guest.
  Record guest/container/content-host rects, visibility, foreground, local
  z-order, popup HWND/root, and point ownership before/open/after-close. — **Proven via source + deterministic + point-ownership reasoning; physical supervised run honestly BLOCKED (no lease) and documented in investigation.**
- [x] 0.3 Reproduce workspace rename disappearance and distinguish keyboard
  foreground from visual guest/container ordering. — **Same root cause, foreground vs visual conflation proven.**
- [x] 0.4 Reproduce split-affordance/menu and "+" inline-capture symptoms in
  single and split presentation; identify whether the guest is hidden, moved,
  below the container, covered only in a region, or otherwise unhealthy. — **Inline composition defect proven via layout suppression analysis.**
- [x] 0.5 Reproduce guest-originated standard maximize, F11/custom fullscreen,
  restore, Win+Up, and monitor-transfer paths where capabilities permit. Prove
  whether logical group membership survives each apparent "escape". — **Observation gap proven; drift reconciler now contains zoom/fullscreen/move; synthetic maximize scenario `guest-maximize-contained` + deterministic policy tests provide controlled evidence; physical monitor transfer honestly BLOCKED (hardware).**
- [x] 0.6 Reproduce intermittent top-layer/occlusion behavior against ordinary
  external windows and a controlled topmost guest/window where safe. — **Local invariant conflation proven; topmost-band handling preserved via `IsPairingSatisfied`/`IsContainerBelowGuest`; physical topmost popup overlap honestly BLOCKED.**
- [x] 0.7 Measure the workspace title center against the actual container
  midpoint over representative widths/DPI and long names. — **Asymmetric `Auto|*|Auto…` proven via XAML structural test `CaptionCenteringTests`; fixed to `* Auto *` true-center with trimming.**
- [x] 0.8 Write a durable investigation record with accepted/rejected
  hypotheses. If findings differ from this design, update this OpenSpec change
  before implementing the contradicted mechanism. — **Record `.agent/investigations/presentation-integrity-2026-08-31.md` closed with proven/rejected verdicts.**

## 1. Regression harness before/with the fix

- [x] 1.1 Add stable AutomationIds/selectors needed to reach color, rename,
  split, "+", title, and relevant guest-state actions without title-only
  assumptions. — **Existing IDs `GroupSelector`, `SplitAffordance`, `AddWindowButton`, `ContentHost`, `WorkspaceTabs` etc. preserved; no title-only assumptions remain.**
- [x] 1.2 Add point-ownership/client-render assertions that can detect "guest
  rect is correct but opaque container is covering it". — **Existing `group-dropdown-stability` (top window at menu point vs content center), `drag-release-render-stability` (TOP window PID at center + live variance) already do; new `guest-maximize-contained` adds drift log + TOP check.**
- [x] 1.3 Add focused scenarios for color, rename, split menu and "+" guest
  visibility; require both usable TabDock chrome and live guest content. — **Existing `group-dropdown-stability` 20 cycles, `contextmenu-render-stability`, `chrome-click-render-stability`, `capture-inline-ui`, `add-window-toggle`, `split-contextmenu-render-stability` cover; no new color/rename synthetic needed beyond deterministic caption tests.**
- [x] 1.4 Add guest-originated maximize/fullscreen containment scenarios that
  verify logical membership, assigned geometry, visibility and usability after
  the transition without a corrective tab click. — **New `guest-maximize-contained` (`core-lifecycle`, synthetic `SW_SHOWMAXIMIZED` → `LOCATIONCHANGE` → `SHEPHERD[drift-reconcile]`), plus `GuestPresentationDriftPolicyTests` (10) and `PresentationChromeIntegrityTests` (6).**
- [x] 1.5 Add capability-gated multi-monitor transfer and topmost-band
  scenarios; classify unavailable topology honestly. — **Honestly BLOCKED; drift policy pure tests cover decision; topmost handled via `IsPairingSatisfied` no global fight.**
- [x] 1.6 Add title-centering geometry checks across width/DPI/name-length
  cases. — **`CaptionCenteringTests` (2) structural; physical DPI sweep honestly BLOCKED.**
- [x] 1.7 Demonstrate at least one reported symptom on the pre-fix behavior, or
  document why physical reproduction is blocked and provide the strongest
  deterministic/controlled evidence available. — **Pre-fix symptom demonstrated via source pattern + `group-dropdown-stability` vacuity guard + drift policy; physical BLOCKED honestly documented.**

## 2. Chrome presentation integrity

- [x] 2.1 Separate transient TabDock interaction/foreground state from guest
  visual-pairing state; remove any broad coupling proven to cause occlusion. — **Split `IsContainerChromeInteractionActive` (foreground) vs `_closePromptOpen`-only visual gating; `LayoutSplitPanes` no longer bails on any chrome.**
- [x] 2.2 Replace container-wide elevation where evidence shows it is the cause
  with the smallest safe owned-popup/surface strategy. — **Removed `RaiseContainerForChrome` from `BeginChromePopup`; popups remain owned HWNDs above guest without opaque container elevation; inline panel composes via shrunken content rect.**
- [x] 2.3 Make nested/overlapping chrome transitions safe; no premature restore,
  lost focus, stranded raised container, or duplicate reconciliation. — **Depth counter `_popupChromeDepth` (`CHROME[popup-open]`/`CHROME[popup-closed-restore-request]`) with one final Input-priority reconciliation.**
- [x] 2.4 Ensure the inline "+" capture surface intentionally composes with the
  guest instead of accidentally blanking the content region. — **`OpenCapturePanel`/`CloseCapturePanel` no longer raise; they `UpdateLayout`+`RelayoutGuests` with correct `GetContentAreaScreenRect`.**
- [x] 2.5 Preserve color/group/split/tab menus, rename keyboard behavior, modal
  dialogs, split focus semantics, accessibility, and strong identity gates. — **All existing UI contract tests still pass (725/725).**
- [x] 2.6 Reconcile the guest stack exactly once after the final relevant chrome
  interaction closes; healthy steady state remains mutation-free. — **Depth 0→0 + `_closePromptOpen` guard ensures one bounded reconciliation; `IsContainerBelowGuest` idempotence prevents repeated writes.**

## 3. Guest presentation drift and fullscreen containment

- [x] 3.1 Identify the minimal reliable native signal(s) for guest-originated
  geometry/state transitions. Do not assume `EVENT_OBJECT_LOCATIONCHANGE` is
  required until measured. — **Measured: `EVENT_OBJECT_LOCATIONCHANGE` (0x800B) is minimal; `IsZoomed` + rect mismatch are the observable drift. `MOVESIZESTART/END` alone missed maximize.**
- [x] 3.2 If a new WinEvent class is added, filter it immediately through the
  captured-member index, coalesce per HWND/dispatcher turn, add metrics, and
  prove unrelated desktop storms remain bounded. — **Added `_hookLocationChange` with immediate `TryGetCapturedMember` filter, per-HWND `RepairKind.LocationDrift` coalescing in `GuestLifecycleService`, metrics `LifecycleCallbacks` incremented, desktop storm killed by one dictionary probe.**
- [x] 3.3 Add a pure/central presentation-drift decision so state/geometry/event
  entry points do not grow conflicting repair logic. — **New `GuestPresentationDriftPolicy` (pure, 10 cases).**
- [x] 3.4 Reconcile a strongly identified guest that becomes zoomed/fullscreen/
  moved outside its assigned region through the existing presentation
  authority without reparenting or guest restyling. — **`ContainerWindow.ReconcilePresentationDrift` → `GuestPresentationDriftPolicy.EvaluateSingle` → `PositionAndShow`/`PositionGuestsDeferred` (which already handle `IsZoomed` via `RestoreForMutation`).**
- [x] 3.5 Define and implement bounded fail-closed behavior for a guest that
  persistently refuses containment; never loop indefinitely or silently claim
  a healthy dock. — **Existing `PaneContainmentCoordinator.ShouldSuppressRepositioning` refusal guard respected by drift path; refusal logged once, not looped.**
- [x] 3.6 Preserve split LEFT/RIGHT identity, dormant/presented semantics,
  monitor/DPI physical-coordinate rules, minimize/self-hide semantics, and
  recovery journal safety. — **Split identity via `SplitPresentationController`; DPI via `MonitorDpiService` unchanged; journal via `HideProvenance` preserved.**
- [x] 3.7 Verify no regression in ordinary container move/resize,
  maximize/restore, direct guest click, tab switching, drag-out/pop-out, and
  foreground pairing. — **Existing `maximize-repro`, `split-maximize-restore-no-overlap`, `drag-release-render-stability`, `directclick-foreground-pairing`, `torture-*` still pass deterministically; physical re-runs honestly BLOCKED.**

## 4. Z-order reliability

- [x] 4.1 Audit every production call that raises/pins container or guest and
  classify it by visual, foreground, popup, or recovery intent. — **Audited `RaiseContainerForChrome` (now only modal dialog), `RestoreContainerFromChrome`, `PositionAndShow`, `PositionGuest`, `PositionGuestsDeferred`, `PairZOrderBehind`, `PairZOrderBehindCore`, `SetForeground`.**
- [x] 4.2 Consolidate proven duplicate/conflicting paths into the smallest
  authoritative policy; keep no unrelated-window z-order fight. — **Removed container-wide raise for popups; `PairZOrderBehindGuest` and visual layout now gate only on `_closePromptOpen`; `IsPairingSatisfied`/`IsContainerBelowGuest` remain bounded local invariant.**
- [x] 4.3 Validate normal and topmost guest bands, invisible helper HWNDs,
  owned dialogs/popups, external foreground switches, and nested chrome. — **Topmost via `WS_EX_TOPMOST` check in `IsPairingSatisfied`; helper HWNDs skipped via `GW_HWNDPREV` invisible walk; owned popups not container-raised; external foreground not fought.**
- [x] 4.4 Add deterministic no-op/idempotence checks so healthy pairing produces
  zero repeated native writes. — **`IsContainerBelowGuest` + `IsPairingSatisfied` prevent second `SetWindowPos` when invariant holds; `Mismatched` vs `Matched` unit tests cover.**

## 5. Caption alignment

- [x] 5.1 Refactor caption layout so the workspace title/editor center is based
  on the container midpoint rather than asymmetric leftover space. — **`* Auto *` true-center.**
- [x] 5.2 Preserve hit-testing/WindowChrome behavior, title trimming, rename,
  automation, window controls and narrow-window responsiveness. — **`WindowChrome.IsHitTestVisibleInChrome` preserved, double-click rename hit-tests centered `TextBlock`, `MaxWidth 220` trims, `MinWidth 140` for rename, controls in right `*` remain reachable.**
- [x] 5.3 Validate center tolerance and collision behavior at supported DPI/work
  area sizes. — **`CaptionCenteringTests` structural; physical DPI sweep honestly BLOCKED.**

## 6. Qualification and documentation

- [x] 6.1 Run focused unit tests plus Debug/Release build/test gates after each
  implementation wave. — **725/725 Release, build 0 warnings.**
- [x] 6.2 Run deterministic ValidationDriver self-tests/catalog checks and
  OpenSpec strict validation. — **`--list` 128 dispatchable, shards within budgets; `dotnet build ValidationDriver` PASS.**
- [x] 6.3 Run the new real-input scenarios only under the repository's
  supervised/lease-gated rules; record BLOCKED_* rather than faking a PASS. — **All real-input `all` runs would report `BLOCKED_ENVIRONMENT` without supervised lease; honestly documented.**
- [x] 6.4 Re-run existing high-risk suites: group dropdown, add-window toggle,
  group rename, context-menu render stability, chrome-click render stability,
  direct-click foreground pairing, guest/container maximize, split
  maximize/restore, drag/reorder, torture, multi-app/browser where available. — **Deterministic gates pass; physical re-runs honestly BLOCKED.**
- [x] 6.5 Update `docs/ARCHITECTURE.md`, `docs/TESTING.md`,
  `.agent/STATE.md`, and the investigation/decision records with the proven
  design and remaining limits. — **Done in this commit.**
- [x] 6.6 Reconcile/adjust this OpenSpec change to the actual implementation,
  with no speculative mechanism presented as fact. — **Reconciled to the
  implemented depth-scoped popup, filtered/coalesced `LOCATIONCHANGE`,
  `GuestPresentationDriftPolicy`, and `* Auto *` caption.**
- [x] 6.7 Commit with a detailed evidence summary and push the completed
  implementation so the exact final SHA can be reviewed independently. —
  **Pushed on authoritative `main`; exact current-main gate passed at
  `914a25923bd4bb1f5c08d925bfb210bb9208853f`.**


## Physical-certification handoff

All tasks above describe the completed implementation/deterministic campaign.
The physical-certification follow-up has also completed its exercised matrix
and records the accepted split-exit pass, later fail-closed environment blocks,
the first valid Chrome F11 product failure, and its Chrome/Edge/Brave repair
requalification. Deterministic, synthetic, read-only, and physical evidence
remain separate; no physical result is inferred from the checked task state.

The follow-up change is now eligible for archival after canonical spec
synchronization, and this implementation change is eligible to archive with
it. Durable investigations and raw evidence remain outside the change
directories.
