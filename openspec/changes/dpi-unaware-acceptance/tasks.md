## Implementation Tasks

### `dpi-acceptance` capability

- [x] **Capture gate accepts KNOWN DPI-unaware guests.** `WindowShepherdService.Capture` no longer
      refuses a guest whose awareness context equals `DpiAwarenessContextUnaware`; it logs a
      `dpi::unaware-accepted` diagnostic and proceeds. Refusal is reserved for a zero/unknown
      awareness context or a thrown probe (`dpi::probe-failed`), with a precise actionable message.
- [x] **Per-target-monitor scale source.** Replace `GetDpiForSystem()` with
      `GetDpiForMonitor(MonitorFromWindow(hwnd), MDT_EFFECTIVE_DPI)` via the new
      `GetMonitorEffectiveDpi` helper, so mixed-DPI secondary monitors are classified correctly.
- [x] **Centralized logical→physical min-track conversion.** Add
      `SplitGeometry.ScaleUnawareLogicalToPhysical` (pure, deterministic) and use it in
      `WindowShepherdService.ToPhysicalScaleForGuest` for DPI-unaware guests, so the
      size-constraint/pane-overflow hardening stays correct for unaware guests on scaled monitors;
      aware guests and 100% scaling are no-ops.
- [x] **Native surface.** `NativeMethods`: `GetDpiForMonitor` (shcore), `MDT_EFFECTIVE_DPI`,
      `USER_DEFAULT_SCREEN_DPI`.
- [x] **GuineaPig `--dpi` launcher modes.** `--dpi unaw|system|per-monitor|per-monitor-v2` sets the
      thread DPI-awareness context before form creation; logs the effective window DPI/awareness.
- [x] **Deterministic self-test.** `--selftest-geometry` covers `ScaleUnawareLogicalToPhysical`
      (no-ops at 100%, scaling at 125/150/200%, never-underestimate, round-up).
- [x] **Supervised DPI scenarios.** `Scenarios.Dpi.cs` adds `capture-dpi-unaware-guest` and
      `capture-dpi-system-guest`, self-skipping at 100% scaling with an explicit reason.

## Validation Notes

- Builds green: TabDock, solution, ValidationDriver, GuineaPig, Spike (0 warnings).
- `scripts/validate.ps1`: PASS.
- `TabDock.exe --selftest-geometry`: PASS (14,719,158 checks, 0 failures).
- `openspec validate --all --no-interactive`: PASS (14 items).
- `git diff --check`: clean.
- Native capture harness (CLI-safe, no SendInput) exercised `WindowShepherdService.Capture` against
  DPI-unaware, system-aware, and per-monitor-v2 pigs on a mixed-DPI host: all ACCEPTED.
- Native `SetWindowPos` experiment: a PMv2 caller's physical rect round-trips exactly on a
  DPI-unaware top-level window (GetWindowRect physical; unaware caller sees it virtualized ÷1.25).
- Remaining (supervised-only): the DPI scenarios and live visual acceptance at non-100% scaling.