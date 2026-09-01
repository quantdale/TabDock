# presentation-integrity

## ADDED Requirements

### Requirement: Chromium F11 fullscreen SHALL be contained without false positives

For captured Chromium-family guests (Chrome, Edge, Brave), an application-defined F11 borderless presentation that removes `WS_CAPTION` without changing captured identity SHALL be detected through bounded native observations (outer rect, style, monitor, title) and `SHEPHERD[drift-reconcile]`, then exited through exactly one identity-checked browser-local F11 request (`SHEPHERD[presentation-restore-request]==1` per drift), duplicate-suppressed, and re-laid out via the resulting `LOCATIONCHANGE` to the assigned pane.

Normal maximized Chromium, ordinary borderless geometry, PWA/windowed app mode, kiosk-like, browser-owned popup/devtools, stale style transition, and monitor-sized non-F11 windows SHALL NOT be classified as F11; they SHALL NOT receive a spurious F11. If a deterministic or valid false-positive defect is proven, the minimal safe classifier repair SHALL be applied.

#### Scenario: Chromium F11 enter is observed and contained
- **WHEN** a captured Chrome/Edge/Brave guest receives a guarded `SendF11To` with proven foreground/lease and enters borderless presentation
- **THEN** the native transition is observed, membership is retained, and after one coalesced F11 exit plus `LOCATIONCHANGE` the same guest is contained in its assigned pane without a tab click, with `SHEPHERD[presentation-restore-request]==1` and no repeated toggles

#### Scenario: Normal maximized Chromium is not mistaken for F11
- **WHEN** the same guest is caption-maximized (`WS_CAPTION` retained, `IsZoomed`) or presents another legitimate non-F11 borderless/monitor-sized state
- **THEN** no F11 is sent, the guest remains contained, and the scenario does not treat maximized geometry as fullscreen

#### Scenario: F11 repeat remains idempotent
- **WHEN** F11 enter/exit is repeated for 2–3 cycles on the same browser
- **THEN** each cycle observes a fresh native transition, requests at most one F11 exit, and returns to the pane with first-attempt authority preserved

### Requirement: Windows 11 Notepad capture SHALL respect the actual broker/host presentation

Windows 11 Notepad may present through a packaged broker/host whose visible HWND, owner/root, PID, process-start, class, and executable differ from simple `notepad.exe`. The harness SHALL discover and record the actual top-level HWND, owner/root, process ancestry, executable identity, and class for the surface being captured, and SHALL keep generation continuity before every mutation.

A capture SHALL prove strong identity before mutation; stale HWND, recycled generation, or broker confusion SHALL fail closed. `BLOCKED_CAPABILITY`/`BLOCKED_ENVIRONMENT` SHALL be retained when safe automation is unavailable, without killing or mis-owning a user Notepad.

#### Scenario: Notepad broker/host is qualified or explicitly blocked
- **WHEN** a supervised cell inspects the live Notepad HWND hierarchy and attempts capture/focus/tab/maximize/transfer/release/re-capture
- **THEN** the record contains HWND, owner, root, PID/start, exe, class, ancestry, and broker flag; lifetime mutations are generation-gated; unrelated user Notepad processes are never terminated

### Requirement: Windows Terminal capture SHALL distinguish launcher from monarch/host

`wt.exe` may be a launcher that exits while the visible terminal belongs to an existing `WindowsTerminal.exe` monarch/host. The harness SHALL record launcher PID, monarch/host PID, visible HWND owner PID, process-start identities, and root, and SHALL distinguish run-owned clean launch from adopted-existing. `wt` launcher exit SHALL NOT be misread as guest disappearance, and cleanup SHALL only target the exact run-owned PID/start.

#### Scenario: Terminal launcher versus host is handled correctly
- **WHEN** a supervised cell spawns `wt` and inspects the resulting visible terminal HWND and its owning process, then exercises capture/tab/transfer/maximize/release
- **THEN** the record distinguishes launcher vs host, proves which PID/start is run-owned, and treats launcher exit without HWND loss as non-disappearance

### Requirement: Real-app containment SHALL remain observable across monitor/DPI transfer

Chromium/Notepad/Terminal guests moved between the available 120-DPI and 96-DPI monitors SHALL keep the same logical member and generation where applicable, and SHALL reconcile to the intended destination pane without monitor jump, opaque-container cover, tab-switch correction, or continuous fight, with lease/foreground/point ownership proven.

#### Scenario: Chromium transfers between 120 DPI and 96 DPI
- **WHEN** a supervised transfer moves a captured Chromium guest between verified 120-DPI and 96-DPI monitors
- **THEN** it remains captured, contained, and locally paired, and visual evidence remains restricted to the host/context with `Valid:true` where review is required
