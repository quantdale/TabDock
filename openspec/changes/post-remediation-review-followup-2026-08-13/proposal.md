## Why

The post-remediation review found that normal destructive callbacks did not
enforce the captured process-start identity, two restore paths treated the
`ShowWindow` previous-visibility return value as an operation result, and the
production DPI probe contradicted Microsoft's PerMonitorV2 API contract. The
same follow-up also needs a durable agent-state convention that cannot enter a
commit/CI self-reference loop.

## What Changes

- Add a native process-instance identity probe based on `GetProcessTimes` and
  apply it to hide, release, foreground, delayed restore, recovery, and other
  slow/destructive gates.
- Keep layout re-glue hot paths bounded with HWND/PID/thread/class checks and
  an in-memory captured-object binding; do not perform managed `Process`
  allocations per layout tick.
- Interpret `ShowWindow` through resulting native state, including the
  minimized-window delayed restore path, and add regression seams for benign
  false returns.
- Replace `GetDpiForMonitor` with a hidden PerMonitorV2 helper HWND queried by
  `GetDpiForWindow`, and route production diagnostics and DPI qualification
  through the same contract-correct probe.
- Refactor `.agent/STATE.md` and repository instructions so Git resolves live
  state dynamically and embedded SHAs/CI IDs are historical evidence only.

## Capabilities

### New Capabilities

- `native-window-identity`: process-instance and captured-object gates for
  native guest mutations.
- `showwindow-poststate`: post-state verification for Win32 show/hide/restore
  operations.
- `monitor-dpi-probing`: contract-correct effective-DPI probing for arbitrary
  monitors from a PerMonitorV2 process.

### Modified Capabilities

None. The existing Shepherd, recovery, DPI-unaware acceptance, and lifecycle
capabilities retain their externally visible architecture and safety policy;
these new contracts close their native identity/API seams.

## Impact

Affected files are `NativeMethods.cs`, `Models/CapturedWindow.cs`,
`Services/WindowShepherdService.cs`, `Services/EnvironmentFingerprint.cs`,
`Services/MonitorDpiService.cs`, `Views/ContainerWindow.xaml.cs`, the linked
ValidationDriver identity/DPI helpers, deterministic diagnostics self-tests,
agent-state instructions, and focused OpenSpec artifacts. No NuGet or
runtime dependency is added, and the Shepherd never-reparent invariant remains
unchanged.
