## 1. Native probe

- [x] 1.1 Add `SendMessageTimeout` import and `SMTO_NORMAL`/`SMTO_ABORTIFHUNG` constants to `NativeMethods.cs`
- [x] 1.2 Add `WindowShepherdService.GetEffectiveMinTrackSize(CapturedWindow)` (fail-closed cross-process `WM_GETMINMAXINFO` probe)

## 2. Deterministic constraint math

- [x] 2.1 Add `SplitGeometry.MinContentWidth`/`MinContentHeight` (exact-partition minima for normal and split)
- [x] 2.2 Extend `SplitGeometry.RunSelfTest` with the constraint math matrix (minimality + fit checks)

## 3. Production constraint enforcement

- [x] 3.1 Add container constraint state and refusal-guard fields in `ContainerWindow`
- [x] 3.2 Add `RefreshSizeConstraint`, `ComputeContainerMinTrack`, refusal helpers, and `ObservedMatches`
- [x] 3.3 Enforce `ptMinTrackSize` in the container `WM_GETMINMAXINFO` handler
- [x] 3.4 Wire `RefreshSizeConstraint` into `RequestRelayout` and add refusal guards to `LayoutShepherdActiveWindow`/`LayoutSplitPanes`
- [x] 3.5 Set constraint-dirty + clear refusals on split enter/exit/replace, survivor promotion, active-tab change, `WM_EXITSIZEMOVE`
- [x] 3.6 Add the debounced 5 s periodic re-probe timer (start on Loaded, stop on Closed)

## 4. Regression coverage

- [x] 4.1 Add `--min-width`/`--min-height` to the GuineaPig (enforce `WM_GETMINMAXINFO`)
- [x] 4.2 Add `split-guest-does-not-overflow-pane`, `split-narrow-container-constraints`, `single-guest-does-not-overflow-content` scenarios and register them in `AllOrder` + runner switch

## 5. Validation

- [x] 5.1 Build all four projects (main, solution, ValidationDriver, GuineaPig) with zero warnings
- [x] 5.2 Run `TabDock.exe --selftest-geometry` (constraint matrix included) and `scripts/validate.ps1`
- [x] 5.3 Run `openspec validate --all --no-interactive`
- [x] 5.4 Supervised real-input run of the three containment scenarios (requires a human operator; not run unattended in this session)