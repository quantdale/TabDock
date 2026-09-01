# validation-qualification

## ADDED Requirements

### Requirement: Real-app qualification SHALL be exact-candidate, supervised, and first-attempt authoritative

Every real-app scenario (Chromium/Notepad/Terminal) SHALL declare capability needs (guest family `browser` or `real-app`, `--guest` value, monitor/DPI, supervision, lease, destructive-state `ExternalBrowser`/`UserOwnedExternal`), SHALL run only after `DesktopQualificationLease` proves candidate/executable/driver/run/scenario/attempt, topology, effective DPI, identity, foreground, and point ownership, and SHALL preserve the first valid attempt across `--reruns` (a valid `FAIL_PRODUCT` followed by `PASS` is `FLAKE_UNCLASSIFIED`, never best-of-N PASS).

Synthetic fixtures SHALL NOT satisfy a real-app gate.

#### Scenario: First valid real-app failure is retained
- **WHEN** attempt 1 of `browser-fullscreen-contained` is `FAIL_PRODUCT` and attempt 2 is `PASS`
- **THEN** the recorded disposition is `FLAKE_UNCLASSIFIED` with both run/packet hashes retained, not ordinary `PASS`

#### Scenario: Real-app cell blocks before input when harness proof is absent
- **WHEN** foreground/point ownership or candidate/lease cannot be proven for a real-app HWND
- **THEN** the scenario records `BLOCKED_ENVIRONMENT`/`BLOCKED_CAPABILITY` without sending input

### Requirement: Real-app qualification SHALL produce a physical acceptance matrix

The campaign SHALL emit a durable matrix at `.agent/investigations/real-app-hardening-acceptance-matrix-2026-09-02.md` (or JSON per convention) with at least: app, executable, process-start identity, HWND/root, run-owned/adopted, scenario, attempt, source/destination monitor/DPI, lease, foreground, point ownership, native outcome, visual outcome, packet hash, cleanup result, final disposition, blocker/reason. Unavailable apps/cells remain visible as capability blocks.

#### Scenario: Matrix contains unavailable browser family
- **WHEN** Brave is not installed
- **THEN** Brave rows remain `SKIP_CAPABILITY`/`BLOCKED_CAPABILITY` with reason `executable not found`, not omitted or fabricated `PASS`

### Requirement: Product repair SHALL be gated by valid real-app failure or deterministic policy defect

Production edits SHALL be permitted only when (a) a valid real-app `FAIL_PRODUCT` is established with frozen first evidence, or (b) deterministic coverage proves a real invariant defect. Forbidden repairs include `SetParent`/reparenting, style stripping, permanent topmost, global z-order polling, blind repeated `SetWindowPos`, killing adopted apps, process-name-only ownership, title-based identity, and relaxed foreground/point checks.
#### Scenario: Harness failure does not authorize a production change
- **WHEN** a real-app run fails as `FAIL_HARNESS` (e.g., picker cannot prove Notepad broker generation)
- **THEN** no production TabDock behavior is edited; the harness is fixed and the cell is requalified
