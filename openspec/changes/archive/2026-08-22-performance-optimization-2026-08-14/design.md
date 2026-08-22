## Context

The authoritative live baseline is the Git-resolved `origin/main` at session
start. `docs/internal/perf-2026-07-25.md` records prior code-reviewed wins but
explicitly says they were not stopwatch-measured. The new harness runs real
production methods in an isolated executable and reports medians and tails; it
does not gate CI on noisy timing thresholds.

## Decisions

### Measurement first

`tests/Performance` references the self-contained app on `win-x64` and runs on
an STA thread. It measures `DiagnosticTrace.Record`, the real logger, the real
picker enumeration/icon path, and durable/identical persistence saves. It uses
`Stopwatch`, `GC.GetAllocatedBytesForCurrentThread`, bounded samples, and JSON
percentiles. Temporary state/log directories are outside `%APPDATA%`; output
defaults to ignored `artifacts/perf`.

### Desktop reorder filter

`EVENT_OBJECT_REORDER` from the desktop is a retained correctness signal. The
callback snapshots foreground HWND, resolves the current `CapturedWindow`, and
returns if no member exists. Only then may it allocate trace metadata or post
to the UI context. A captured event carries the exact object reference; the UI
dispatch still requires that reference to resolve from the current index, so a
release or HWND recycle fails closed.

### Diagnostic trace allocation

`DiagnosticEventRecord` retains its public parameterless constructor and
non-null mutable `Data` property. An internal constructor creates exactly one
empty or defensive-copy dictionary, and `DiagnosticTrace.Record`/`Clone` use it
directly. Timestamp representation and snapshot ordering remain unchanged
unless later measurements prove a separately justified benefit.

### Container handle reuse

Only methods reached after `Loaded` use `_containerHwnd`. `OnSourceInitialized`
continues to query the handle because the cache is not established yet, and
`Loaded` continues to call `EnsureHandle`. Teardown still clears/unregisters the
cached values.

### Validation/tooling

The solution graph proves that TabDock and Spike are already built by
`TabDock.sln`; validation keeps explicit builds for ValidationDriver and
GuineaPig. OpenSpec is a private repository tool package with exact `1.8.0`
dependency and lockfile. CI runs `npm ci --ignore-scripts` in that package and
invokes its local `.bin` executable; no global install is required.

### Evidence gates for deferred options

Async icons require a repeatable cold-vs-warm latency gap large enough to be
user-visible and must use generation/cancellation/single-flight protections.
Persistence replacement, z-order epochs, and timer changes require equal or
stronger safety semantics plus failure-path tests. If those conditions are not
met, the implementation remains unchanged and the campaign record documents
the measurement and decision. Large-class extraction is limited to pure policy;
native capture/release ownership stays in `WindowShepherdService`.

## Risks

- Native callback filtering could lose a relevant event if it resolved the
  wrong object; exact callback-time reference capture and dispatch-time
  reference validation preserve the existing identity contract.
- Async icon work could race refresh/close; it will not be introduced without
  generation and bounded-worker tests.
- Tooling lockfiles can expose RID restore fragility; NuGet lock mode is adopted
  only if all supported project restores remain clean.

## Rollback

All changes are source/build-tool changes with no forward-only data migration.
Reverting the change restores the prior callback allocation behavior and global
tool install without affecting state or journal compatibility.
