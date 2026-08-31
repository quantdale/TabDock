# Tasks — user-reported presentation integrity

## 0. Evidence-first reproduction and diagnosis

- [ ] 0.1 Resolve current Git state dynamically; read `AGENTS.md`,
  `.agent/STATE.md`, `docs/ARCHITECTURE.md`, `docs/TESTING.md`, the current
  canonical OpenSpec specs, and this change before editing production code.
- [ ] 0.2 Reproduce the color-menu disappearance with one controlled guest.
  Record guest/container/content-host rects, visibility, foreground, local
  z-order, popup HWND/root, and point ownership before/open/after-close.
- [ ] 0.3 Reproduce workspace rename disappearance and distinguish keyboard
  foreground from visual guest/container ordering.
- [ ] 0.4 Reproduce split-affordance/menu and "+" inline-capture symptoms in
  single and split presentation; identify whether the guest is hidden, moved,
  below the container, covered only in a region, or otherwise unhealthy.
- [ ] 0.5 Reproduce guest-originated standard maximize, F11/custom fullscreen,
  restore, Win+Up, and monitor-transfer paths where capabilities permit. Prove
  whether logical group membership survives each apparent "escape".
- [ ] 0.6 Reproduce intermittent top-layer/occlusion behavior against ordinary
  external windows and a controlled topmost guest/window where safe.
- [ ] 0.7 Measure the workspace title center against the actual container
  midpoint over representative widths/DPI and long names.
- [ ] 0.8 Write a durable investigation record with accepted/rejected
  hypotheses. If findings differ from this design, update this OpenSpec change
  before implementing the contradicted mechanism.

## 1. Regression harness before/with the fix

- [ ] 1.1 Add stable AutomationIds/selectors needed to reach color, rename,
  split, "+", title, and relevant guest-state actions without title-only
  assumptions.
- [ ] 1.2 Add point-ownership/client-render assertions that can detect "guest
  rect is correct but opaque container is covering it".
- [ ] 1.3 Add focused scenarios for color, rename, split menu and "+" guest
  visibility; require both usable TabDock chrome and live guest content.
- [ ] 1.4 Add guest-originated maximize/fullscreen containment scenarios that
  verify logical membership, assigned geometry, visibility and usability after
  the transition without a corrective tab click.
- [ ] 1.5 Add capability-gated multi-monitor transfer and topmost-band
  scenarios; classify unavailable topology honestly.
- [ ] 1.6 Add title-centering geometry checks across width/DPI/name-length
  cases.
- [ ] 1.7 Demonstrate at least one reported symptom on the pre-fix behavior, or
  document why physical reproduction is blocked and provide the strongest
  deterministic/controlled evidence available.

## 2. Chrome presentation integrity

- [ ] 2.1 Separate transient TabDock interaction/foreground state from guest
  visual-pairing state; remove any broad coupling proven to cause occlusion.
- [ ] 2.2 Replace container-wide elevation where evidence shows it is the cause
  with the smallest safe owned-popup/surface strategy.
- [ ] 2.3 Make nested/overlapping chrome transitions safe; no premature restore,
  lost focus, stranded raised container, or duplicate reconciliation.
- [ ] 2.4 Ensure the inline "+" capture surface intentionally composes with the
  guest instead of accidentally blanking the content region.
- [ ] 2.5 Preserve color/group/split/tab menus, rename keyboard behavior, modal
  dialogs, split focus semantics, accessibility, and strong identity gates.
- [ ] 2.6 Reconcile the guest stack exactly once after the final relevant chrome
  interaction closes; healthy steady state remains mutation-free.

## 3. Guest presentation drift and fullscreen containment

- [ ] 3.1 Identify the minimal reliable native signal(s) for guest-originated
  geometry/state transitions. Do not assume `EVENT_OBJECT_LOCATIONCHANGE` is
  required until measured.
- [ ] 3.2 If a new WinEvent class is added, filter it immediately through the
  captured-member index, coalesce per HWND/dispatcher turn, add metrics, and
  prove unrelated desktop storms remain bounded.
- [ ] 3.3 Add a pure/central presentation-drift decision so state/geometry/event
  entry points do not grow conflicting repair logic.
- [ ] 3.4 Reconcile a strongly identified guest that becomes zoomed/fullscreen/
  moved outside its assigned region through the existing presentation
  authority without reparenting or guest restyling.
- [ ] 3.5 Define and implement bounded fail-closed behavior for a guest that
  persistently refuses containment; never loop indefinitely or silently claim
  a healthy dock.
- [ ] 3.6 Preserve split LEFT/RIGHT identity, dormant/presented semantics,
  monitor/DPI physical-coordinate rules, minimize/self-hide semantics, and
  recovery journal safety.
- [ ] 3.7 Verify no regression in ordinary container move/resize,
  maximize/restore, direct guest click, tab switching, drag-out/pop-out, and
  foreground pairing.

## 4. Z-order reliability

- [ ] 4.1 Audit every production call that raises/pins container or guest and
  classify it by visual, foreground, popup, or recovery intent.
- [ ] 4.2 Consolidate proven duplicate/conflicting paths into the smallest
  authoritative policy; keep no unrelated-window z-order fight.
- [ ] 4.3 Validate normal and topmost guest bands, invisible helper HWNDs,
  owned dialogs/popups, external foreground switches, and nested chrome.
- [ ] 4.4 Add deterministic no-op/idempotence checks so healthy pairing produces
  zero repeated native writes.

## 5. Caption alignment

- [ ] 5.1 Refactor caption layout so the workspace title/editor center is based
  on the container midpoint rather than asymmetric leftover space.
- [ ] 5.2 Preserve hit-testing/WindowChrome behavior, title trimming, rename,
  automation, window controls and narrow-window responsiveness.
- [ ] 5.3 Validate center tolerance and collision behavior at supported DPI/work
  area sizes.

## 6. Qualification and documentation

- [ ] 6.1 Run focused unit tests plus Debug/Release build/test gates after each
  implementation wave.
- [ ] 6.2 Run deterministic ValidationDriver self-tests/catalog checks and
  OpenSpec strict validation.
- [ ] 6.3 Run the new real-input scenarios only under the repository's
  supervised/lease-gated rules; record BLOCKED_* rather than faking a PASS.
- [ ] 6.4 Re-run existing high-risk suites: group dropdown, add-window toggle,
  group rename, context-menu render stability, chrome-click render stability,
  direct-click foreground pairing, guest/container maximize, split
  maximize/restore, drag/reorder, torture, multi-app/browser where available.
- [ ] 6.5 Update `docs/ARCHITECTURE.md`, `docs/TESTING.md`,
  `.agent/STATE.md`, and the investigation/decision records with the proven
  design and remaining limits.
- [ ] 6.6 Reconcile/adjust this OpenSpec change to the actual implementation,
  with no speculative mechanism presented as fact.
- [ ] 6.7 Commit with a detailed evidence summary and push the completed
  implementation so the exact final SHA can be reviewed independently.
