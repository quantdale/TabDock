## Why

The latest deep audit identified correctness, recovery, lifecycle, privacy,
qualification, multi-monitor, responsiveness, and persistence-hardening risks
that must be confirmed against the current Shepherd implementation before a
production-readiness claim is credible. The campaign provides one coherent
contract for safe native positioning, crash recovery, monitor/session failure,
diagnostic privacy, schema evolution, and bounded validation while preserving
the existing no-reparent architecture.

## What Changes

- Correct deferred Win32 positioning semantics and preserve split-pane
  geometry, z-order, visibility, journal, and no-activation invariants.
- Define complete identity-gated crash recovery for reversible guest
  presentation mutations, including versioned journal/state compatibility and
  evidence-preserving backup recovery.
- Make session-ending and WinEvent-monitor failure policies explicit and
  coherent; never leave active guests unsupported by lifecycle monitoring or
  leave a cancelled session half torn down.
- Sanitize all diagnostic bundle representations so profile, AppData,
  executable, machine-specific, and credential-like data cannot leak.
- Establish safe behavior when durable AppData storage is unavailable and make
  semantic persistence durable without turning high-frequency UI changes into
  synchronous disk I/O.
- Align container sizing with the monitor containing it and bound hung-guest
  min-track probing without resize races.
- Add CI-safe behavioral qualification, configurable Debug/Release/RID-aware
  ValidationDriver discovery, and bounded scenario shards while retaining
  supervised real-input requirements.
- Measure picker icon/executable work and fix it only if realistic profiling
  demonstrates material UI impact.

## Capabilities

### New Capabilities

- `native-window-qualification`: Safe deferred positioning, monitor-specific
  container constraints, and bounded native min-track probing.
- `monitor-health-policy`: Capture admission and recovery behavior when
  WinEvent monitoring fails permanently.
- `diagnostic-privacy`: End-to-end privacy contract for doctor, logs, and
  support-bundle artifacts.
- `validation-qualification`: Hermetic qualification commands and bounded,
  configurable supervised scenario orchestration.

### Modified Capabilities

- `hidden-window-journal`: Full-state, identity-gated recovery and journal
  versioning for abrupt termination, including self-hide semantics.
- `crash-shutdown-coherence`: Explicit session-ending cancellation policy and
  coherent fatal/unclean shutdown behavior.
- `persistence-resilience`: Corrupt-vs-unreadable classification, backup
  fallback, future-version preservation, and older-version migration.
- `diagnostics-logging`: Storage-unavailable degraded mode and privacy-safe
  path handling across diagnostics.
- `test-tooling-safety`: Release/RID-aware executable discovery and bounded
  scenario sharding.
- `e2e-scenario-coverage`: Regression scenarios for native split, recovery,
  persistence, lifecycle, and qualification behavior.
- `capture-picker-icons`: Measured/cancellable picker hydration behavior if
  profiling confirms a material synchronous stall.

## Impact

Affected production code includes `NativeMethods.cs`, `WindowShepherdService`,
`WinEventMonitor`, `GuestLifecycleService`, `App`, `GroupManager`,
`PersistenceService`, diagnostic services, `ContainerWindow`, and picker/icon
services. Affected test infrastructure includes the ValidationDriver,
GuineaPig, self-tests, scripts, CI workflow, and focused fixture/seam tests.
The change adds no third-party runtime dependency and does not alter the
Shepherd/no-reparent model.
