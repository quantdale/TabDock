## Why

Audit finding A05 snapshots an actual ContainerWindow successfully on its UI thread, but worker access throws and a real support ZIP omits the expected logical entry. The current export worker calls the live WPF provider, and swallowed per-container errors can look like an empty session.

## What Changes

- Capture immutable logical presentation data on the owning dispatcher before background report/ZIP work.
- Keep the hotkey path asynchronous without blocking dispatcher round trips, and preserve the UI-free CLI path.
- Distinguish unavailable/partial logical evidence from a genuinely empty session in the same bundle.
- Add actual dispatcher-owned provider, close-race, and sanitized ZIP regressions.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `production-diagnostics`: specify dispatcher-safe logical capture, explicit partial evidence, and nonblocking hotkey export.

## Impact

Affects App diagnostic orchestration, `DiagnosticReportService`, logical snapshot DTOs/provider contracts, and diagnostics tests. Export stays local, privacy-safe, and non-mutating. Source evidence: audit A05.

## Non-goals

No new repair loop, HWND presentation changes, periodic sampling, or telemetry. The existing exporter did not reproduce an async-context deadlock; this change fixes off-thread WPF access and waiting, not a claimed existing deadlock. Planning only.

## Sequencing

Snapshot ownership work is independent of persistence/motion. End-to-end ZIP/process tests depend on `isolate-validation-storage-roots` and `keep-recovery-discovery-read-only` so they cannot inspect/clean the production profile. Async completion/failure must be observed even when the triggering window closes.
