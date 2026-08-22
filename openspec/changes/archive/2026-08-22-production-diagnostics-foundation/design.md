## Context

The current application has a rotating `LoggingService`, string-based `EnvironmentFingerprint` lines, persisted group metadata, out-of-process WinEvent hooks, and Shepherd-owned HWND placement. The diagnostic foundation must make those streams correlate without making diagnostics a second window-management authority. A friend may run the report with no source checkout, with no TabDock instance, or while a running instance is visibly broken.

## Decisions

### Build identity

Use MSBuild-generated assembly metadata as the product source of truth. Keep the semantic version in the project, expose `AssemblyInformationalVersion`, `AssemblyVersion`, `FileVersion`, `SourceRevisionId`, configuration, and RID through a `BuildIdentity` service. The service parses the informational-version commit suffix once and never shells out to Git. Configuration and RID are generated properties, not path heuristics. No build timestamp is added: commit plus artifact SHA-256 gives a stronger distributed-artifact identity without making deterministic rebuilds differ.

The executable hash is computed only for explicit version/doctor/export commands or an explicit in-product export, never on ordinary startup. Failures are represented as unavailable with a category.

### CLI lifecycle

Argument dispatch happens before mutex acquisition, state construction, hotkeys, hooks, or WPF windows. `--version` and `--doctor` use a small parser; `--doctor --output <path>` writes an explicit report destination, and `--support-bundle [--output <path>]` writes a ZIP using built-in `System.IO.Compression`. Console attachment/output is best effort for a `WinExe`; redirected output remains supported for automation. CLI commands do not instantiate the normal `PersistenceService`, because its constructor creates the application-data directory.

### Report model and privacy

Use serializable record/classes under `Models/Diagnostics` or an equivalent diagnostic namespace. Default titles are represented by length and a short SHA-256, never the raw title. Process identity uses PID, executable name (and a sanitized path where possible), start time, architecture/elevation status, and main HWND; usernames, machine names, command lines, and arbitrary environment variables are excluded. State inspection reads bytes directly and classifies absent, valid, unreadable, malformed, and unsupported schema without calling application load/quarantine/save paths.

### Native observation

Keep all new P/Invoke declarations in `NativeMethods.cs`. A `NativeSnapshotService` performs best-effort observation only: it enumerates top-level windows/processes, queries geometry/DPI/monitor/class/visibility/iconic/zoomed/owner/topmost/cloaked/foreground/z-order neighbors, and takes fixed safe point probes for each TabDock container. It never sends pointer-bearing cross-process messages; in particular it does not probe `WM_GETMINMAXINFO`. Handle death between calls as a per-observation status.

For a live application, `ContainerWindow` exposes a read-only logical snapshot provider and the app registers a `DiagnosticSnapshotProvider` with the export service. The provider reads current fields (`_splitLeft`, `_splitRight`, `_splitForeground`, active member, expected pane rectangles) without calling layout or Shepherd operations. A command-line helper cannot safely reach those private UI objects without IPC, so it uses persisted summary plus native process/window observations; the explicit in-product export captures the richer live logical snapshot.

### Trace and repair instrumentation

`DiagnosticTrace` is a lock-protected fixed-capacity ring (default 1024 significant events). `Record` accepts structured fields and returns a sequence number. It is cheap for selected low-volume events; no global `EVENT_OBJECT_LOCATIONCHANGE` hook and no health timer are added. `WinEventMonitor` records callback observations before dispatch for foreground/reorder/move-size events, and the existing handlers record dispatch/action outcomes. `WindowShepherdService` gets a narrow optional trace sink around important existing operations, preserving it as the only native write authority.

### Export

An explicit `SupportBundleService` gathers `BuildIdentity`, `DoctorReport`, `EnvironmentFingerprint` structured data, persistence summary, native snapshot, logical snapshot when registered, trace JSONL, and a bounded tail of current/rotated logs. It writes a temporary directory and atomically creates a ZIP or a specified directory; it never calls save/release/layout. Failed sections become records in the report. The in-product trigger is a diagnostic-only global hotkey (`Ctrl+Alt+Shift+D`) registered only by the normal process; it invokes export on the UI dispatcher and does not require the header. The hotkey is documented and can be disabled by omitting registration if registration fails.

## Alternatives rejected

- A named-pipe/IPC service was deferred: it would add a second cross-process control surface and elevation/UIPI protocol before the basic evidence model exists.
- Continuous whole-desktop snapshots and global location-change hooks were rejected for performance and signal-to-noise reasons.
- A build timestamp was rejected because it weakens deterministic/reproducible artifact identity without improving source identification beyond commit and SHA-256.
- Reusing `PersistenceService.Load` for doctor was rejected because it may quarantine malformed state and its constructor creates directories, violating read-only doctor semantics.

## Invariants

The implementation must retain the Shepherd/no-reparent model, existing WinEvent hook topology except selected trace calls, current group/split semantics, `asInvoker` execution, no foreground stealing, and no periodic reconciliation. Diagnostic snapshot/export code must not call `ShowWindow`, `SetWindowPos`, `SetForegroundWindow`, capture/release, `SaveState`, or `Layout`.

## Validation

Use deterministic tests for build identity parsing, trace ordering/capacity/concurrency, title redaction, state classification, CLI argument dispatch, and JSON serialization. Use CLI-safe process tests for `--version`, `--doctor`, no-window/no-state mutation, and published artifact identity. Run the repository's full build, validate, geometry, OpenSpec, and diff checks. Manual native snapshot/export validation remains a supervised desktop check, not a reason to add real-input automation to the unit surface.
