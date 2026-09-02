# presentation-integrity

## MODIFIED Requirements

### Requirement: Chromium F11 fullscreen SHALL be contained without false positives

For captured Chromium-family guests (Chrome, Edge, Brave), an application-defined F11 borderless presentation that removes `WS_CAPTION` without changing captured identity SHALL be detected through bounded native observations (outer rect, style, monitor, title) and `SHEPHERD[drift-reconcile]`, then exited through exactly one identity-checked browser-local F11 request (`SHEPHERD[presentation-restore-request]==1` per drift), duplicate-suppressed, and re-laid out via the resulting `LOCATIONCHANGE` to the assigned pane.

Normal maximized Chromium, ordinary borderless geometry, PWA/windowed app mode, kiosk-like, browser-owned popup/devtools, stale style transition, and monitor-sized non-F11 windows SHALL NOT be classified as F11; they SHALL NOT receive a spurious F11. If a deterministic or valid false-positive defect is proven, the minimal safe classifier repair SHALL be applied.

A preserved first valid `FAIL_PRODUCT` for any Chromium browser SHALL receive a final defensible disposition before a real-app closure archives. Characterization SHALL use bounded independent invocations with fresh isolated browser profiles against an exact committed candidate; a production repair requires frozen first evidence, the smallest Shepherd-preserving change, a non-vacuous regression, and full requalification of the failing browser plus adjacent Chromium adjacency. Timeout-only, retry-only, second-F11, tab-switch, or weakened-assertion changes SHALL NOT be treated as a repair.

#### Scenario: Chromium F11 enter is observed and contained
- **WHEN** a captured Chrome/Edge/Brave guest receives a guarded `SendF11To` with proven foreground/lease and enters borderless presentation
- **THEN** the native transition is observed, membership is retained, and after one coalesced F11 exit plus `LOCATIONCHANGE` the same guest is contained in its assigned pane without a tab click, with `SHEPHERD[presentation-restore-request]==1` and no repeated toggles

#### Scenario: Normal maximized Chromium is not mistaken for F11
- **WHEN** the same guest is caption-maximized (`WS_CAPTION` retained, `IsZoomed`) or presents another legitimate non-F11 borderless/monitor-sized state
- **THEN** no F11 is sent, the guest remains contained, and the scenario does not treat maximized geometry as fullscreen

#### Scenario: F11 repeat remains idempotent
- **WHEN** F11 enter/exit is repeated for 2–3 cycles on the same browser
- **THEN** each cycle observes a fresh native transition, requests at most one F11 exit, and returns to the pane with first-attempt authority preserved

#### Scenario: Preserved first failure is dispositioned before closure
- **WHEN** a Chromium browser's first valid F11 attempt failed but a later run passed
- **THEN** the archive-bound campaign characterizes the failure against an exact candidate and records a final disposition; an unexplained valid product failure keeps the closure open