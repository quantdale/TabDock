## 1. Native and geometry safety

- [x] 1.1 Complete chained HDWP implementation and deterministic native seam.
- [x] 1.2 Remove primary-monitor WPF max-size clamp and retain monitor-specific native work-area handling.
- [x] 1.3 Bound min-track probing, initialize the synthetic native probe buffer, retain identity-scoped last-known values, and add non-pumping guest timing coverage.
- [x] 1.4 Run the targeted split creation, move, resize, minimize/restore, odd-width, focus, drag-release, and torture matrix in bounded sub-shards. `split-core` (10/10) and `split-focus` (7/7) passed, including the H1 focus/drag/maximize evidence; `split-render` passed 12/13 and one `split-third-tab-click-persists` retry was blocked by Windows foreground activation before keyboard input, with the guard correctly refusing to continue. Multi-monitor cases remain hardware qualification.

## 2. Recovery and persistence

- [x] 2.1 Version and expand the recovery journal with full reversible presentation state and conservative identity gates.
- [x] 2.2 Preserve journal-before-hide/capture guarantees and add intentional self-hide no-rescue handling.
- [x] 2.3 Enforce state schema versions, migrate v1, preserve future files, and classify unreadable versus corrupt primary files.
- [x] 2.4 Recover valid backups only for missing/proven-corrupt primary files and preserve all corrupt evidence.
- [x] 2.5 Add durable semantic-save paths while keeping drag/reorder debounce and add fixture/self-test coverage.
- [x] 2.6 Prevent unmaterialized empty groups from accumulating across saves, reloads, failed picker captures, and isolated validation runs.

## 3. Lifecycle and diagnostics

- [x] 3.1 Make session-ending teardown one-way and idempotently exit after normalization.
- [x] 3.2 Add WinEvent hook injection, unhealthy admission state, bounded retry, fail-closed release, and self-tests.
- [x] 3.3 Centralize path/credential sanitization across reports and ZIP entries; add adversarial fixtures and actual archive inspection.
- [x] 3.4 Add storage-unavailable degraded startup/logging and disable capture without a durable journal.

## 4. Qualification infrastructure

- [x] 4.1 Add Release CI self-tests, OpenSpec validation, doctor/version/bundle/publish smoke, and explicit NuGet audit policy.
- [x] 4.2 Add configurable Debug/Release/RID/path discovery and named bounded ValidationDriver shards.
- [x] 4.3 Align `scripts/validate.ps1`, `docs/TESTING.md`, and runner help with the actual qualification surface.

## 5. Measurement and documentation

- [x] 5.1 Measure picker refresh/icon work under realistic desktop candidates; document evidence disproving materiality when the measured bound is not material.
- [x] 5.2 Update architecture, testing, recovery/privacy, and OpenSpec documentation.
- [x] 5.3 Run final builds, self-tests, fixtures, CI-safe checks, supervised scenarios, and repository-state review; final external qualification remains explicitly tracked in the remediation plan.
