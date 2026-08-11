# Agent state

## Current checkpoint — POST-AUDIT DPI COMPATIBILITY FINDING resolved (2026-08-11)

Objective: determine whether TabDock can safely shepherd DPI-unaware top-level
HWNDs at non-100% display scaling while preserving physical-pixel geometry and
the Shepherd/no-reparent architecture; if safe, implement it; otherwise prove the
limitation. The user hit the fail-closed refusal "This window is not DPI-aware and
can only be captured reliably at 100% display scaling."

Status: **RESOLVED — DPI-unaware guests are supported safely.** The blanket
refusal was the bug; native evidence proves a PerMonitorV2 caller's physical
`SetWindowPos` glues an unaware top-level OUTER rect exactly. Implemented, all
CLI-safe gates green. Final assessment: RESOLVED PENDING SUPERVISED VISUAL
CONFIRMATION at non-100% scaling (remaining steps are supervised by policy).

## Root cause (proven)

The guard's premise — an unaware guest "would be stretched and misplaced no
matter what rect we hand it" — is false for OUTER-rect positioning. DPI
virtualization of geometry APIs is keyed to the CALLING thread's awareness, so a
PMv2 caller's `SetWindowPos`/`GetWindowRect` are physical against any target. A
native experiment on this machine: `SetWindowPos(200,150,1440,900)` on an unaware
top-level window from a PMv2 thread → `GetWindowRect (200,150,1440x900)` exactly;
an unaware caller of the same window saw `(160,120,1152x720)` = physical ÷ 1.25
(virtualized). The unaware guest's content is DWM-blurred exactly as standalone —
not a TabDock geometry defect. The gate also used `GetDpiForSystem()` (primary),
misclassifying targets on differently-scaled secondaries.

## Fix (Shepherd-compatible)

- `WindowShepherdService.Capture`: a KNOWN DPI-unaware guest is captured normally
  (`dpi::unaware-accepted`); refusal reserved for a probe that FAILS or returns an
  UNKNOWN context (`dpi::probe-failed`), with a precise message. Scale source is
  the TARGET monitor's effective DPI (`GetDpiForMonitor(hmon, MDT_EFFECTIVE_DPI)`
  via shcore, `GetMonitorEffectiveDpi`), not the primary.
- Centralized logical→physical min-track boundary:
  `SplitGeometry.ScaleUnawareLogicalToPhysical` (ceil, never under-estimates),
  used by `WindowShepherdService.ToPhysicalScaleForGuest` for unaware guests, so
  the size-constraint/pane-overflow containment stays correct for unaware guests
  on scaled monitors; aware guests and 100% scaling are no-ops.
- `NativeMethods`: `GetDpiForMonitor`, `MDT_EFFECTIVE_DPI`, `USER_DEFAULT_SCREEN_DPI`.
- GuineaPig `--dpi unaw|system|per-monitor|per-monitor-v2` launcher modes.
- `Scenarios.Dpi.cs`: `capture-dpi-unaware-guest`, `capture-dpi-system-guest`
  (self-skip at 100% with explicit reason).
- New OpenSpec change `dpi-unaware-acceptance`.

## Validation (CLI-safe)

- Builds: TabDock, solution, ValidationDriver, GuineaPig, Spike — PASS, 0 warnings.
- `scripts\validate.ps1`: PASS.
- `TabDock.exe --selftest-geometry`: PASS, 14,719,158 checks, 0 failures (new
  scaling-math coverage included).
- `openspec validate --all --no-interactive`: PASS, 14/14.
- `git diff --check`: clean (expected LF/CRLF conversion notes only).
- Native capture harness (CLI-safe, no SendInput) exercised `Capture` against
  DPI-unaware, system-aware, and per-monitor-v2 pigs on a mixed-DPI host: all
  ACCEPTED (no refusal).

## Outstanding (supervised-only, by policy)

- The two DPI ValidationDriver scenarios (`capture-dpi-unaware-guest`,
  `capture-dpi-system-guest`) send real input and require a human operator.
- Live visual acceptance of a DPI-unaware guest docked at 125%/150% on real
  hardware (no drift, overflow, or blanking).

## Next action

Run the supervised ValidationDriver DPI scenarios and the manual visual acceptance
of an unaware guest at non-100% scaling; then, if green, review the accumulated
diff for a coherent milestone commit (do not push without explicit request).