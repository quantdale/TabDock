## Why

A captured guest that enforces a native minimum track size (browsers, Explorer,
terminals via `WM_GETMINMAXINFO`) visibly escapes its assigned pane when the
split pane or the normal-mode content area is narrower than that minimum. The
overflow grows as the container narrows — confirmed with actual `GetWindowRect`
evidence. Previously TabDock requested a pane rect via `SetWindowPos` and never
verified the observed rect, and its shell had no minimum-size constraint, so it
could be resized into the impossible region with no containment.

## What Changes

- TabDock discovers each visible guest's effective native minimum track size at
  runtime (cross-process `WM_GETMINMAXINFO` via `SendMessageTimeout`, fail-closed,
  cached, never per-frame, never hardcoded per application).
- The container enforces a dynamic minimum size (`WM_GETMINMAXINFO`
  `ptMinTrackSize`) computed from the currently visible guests' native minima
  plus chrome, so the user can no longer drag-resize the shell below what the
  guests can physically fit. Normal mode: single guest's minimum. Split mode:
  the exact partition's width/height for both members.
- The layout short-circuits now confirm requested-vs-observed geometry: a guest
  that still refuses its pane after a re-glue is marked non-compliant and not
  re-fought every frame (bounded, no resize war), with a bounded
  `SHEPHERD[size-constraint]` diagnostic on the transition.
- Constraint state is recomputed on every visible-set/window-state transition
  (split enter/exit/replace, active-tab change, survivor promotion, resize end)
  and on a debounced periodic re-probe, so a dynamic native minimum and pair
  replacement never leave a stale minimum.
- Deterministic constraint math (`SplitGeometry.MinContentWidth/MinContentHeight`)
  is added to the `--selftest-geometry` matrix.
- Regression scenarios (`split-guest-does-not-overflow-pane`,
  `split-narrow-container-constraints`, `single-guest-does-not-overflow-content`)
  and a `--min-width`/`--min-height` GuineaPig option reproduce the defect
  deterministically.

## Capabilities

### New Capabilities
- `guest-size-constraint`: the container's dynamic minimum size derived from the
  visible guests' native minima, the bounded requested-vs-observed non-compliance
  guard, and the deterministic constraint math.

### Modified Capabilities
<!-- No existing spec's requirements change in a way that removes or rewrites them;
     this is a new containment capability layered on the existing split/window-state
     reconciliation behavior. -->

## Impact

- `Services/WindowShepherdService.cs`: `GetEffectiveMinTrackSize` probe.
- `Services/SplitGeometry.cs`: `MinContentWidth`/`MinContentHeight` + self-test.
- `Views/ContainerWindow.xaml.cs`: constraint state, `WM_GETMINMAXINFO`
  min-track enforcement, refusal guard, refresh triggers.
- `NativeMethods.cs`: `SendMessageTimeout` + `SMTO_*` constants.
- `tests/ValidationDriver/TabDock.GuineaPig`: `--min-width`/`--min-height`.
- `tests/ValidationDriver/.../Scenarios.Split.cs`: three supervised scenarios.
- No new dependencies; Shepherd/no-reparent architecture preserved.