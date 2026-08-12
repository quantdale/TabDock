## Why

TabDock can currently log useful fragments of environment and window state, but a distributed executable does not expose one authoritative build identity and there is no supported, local, read-only way to capture enough evidence when a guest or container diverges on another machine. This milestone closes that supportability gap without changing Shepherd ownership, grouping, or split semantics.

## What Changes

- Add one authoritative build identity model backed by generated assembly metadata and MSBuild values, including commit, configuration, runtime, artifact path, and optional artifact hash.
- Add UI-free `--version` and read-only `--doctor` command modes, with an explicit support-bundle export mode using only local data.
- Extend existing environment fingerprinting into structured, privacy-safe machine, monitor/DPI, GPU, persistence, process, and native HWND observations.
- Add reusable observed-native and desired/logical presentation snapshot models, including safe `WindowFromPoint` probes for active container surfaces.
- Add a bounded, sequenced diagnostic event/repair trace that records selected WinEvents, logical transitions, and important native repair outcomes without global location-change monitoring or polling.
- Add a local in-process snapshot/export trigger that does not depend on a visible header, while keeping the command-line report useful when no TabDock instance is running.
- Add deterministic/unit-style coverage through the existing CLI/self-test infrastructure, update support and testing documentation, and record privacy/performance decisions.

## Capabilities

### New Capabilities

- `production-diagnostics`: exact build identity, UI-free versioning, read-only doctor reports, sanitized snapshots, bounded trace, and local support export.

### Modified Capabilities

- None.

## Impact

Affected areas are `App.xaml.cs`, `TabDock.csproj`, `NativeMethods.cs`, existing environment/logging/persistence/WinEvent/Shepherd services, container diagnostics, new diagnostic models/services, and focused validation/documentation. No third-party dependency, telemetry, IPC service, reparenting, periodic health loop, or split/group behavior change is introduced.
