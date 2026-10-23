## 1. Add owner-thread and immutable-evidence regressions

- [x] 1.1 Add a no-input STA test using an actual ContainerWindow/provider that reproduces worker access failure and missing logical evidence without reading production storage.
- [x] 1.2 Add held-archive-gate tests for UI yielding, captured DTO immutability, container-close races, partial collection and observed asynchronous failures.
- [x] 1.3 Define additive available/empty/partial/unavailable logical status and same-bundle sanitized issue assertions.

## 2. Separate capture from background export

- [x] 2.1 Collect/deep-copy logical DTOs on the owning dispatcher before archive work; ensure worker/report input contains no live Window, view-model or provider references.
- [x] 2.2 Refactor report/ZIP assembly to consume captured logical data and include collection failures in the same bundle rather than only a later trace.
- [x] 2.3 Make the App hotkey orchestration asynchronous with observed completion/error and shutdown-safe lifetimes; retain a UI-free CLI wrapper and no-live-provider status.
- [x] 2.4 Preserve local atomic archive publication, filename collision handling, privacy sanitization and no-presentation-mutation behavior.

## 3. Verify the actual exported boundary

- [x] 3.1 Run `dotnet build TabDock.sln` and targeted dispatcher/immutable-report tests through injected observation/storage fixtures.
- [x] 3.2 After A02/A10 land, export actual isolated ZIPs for live, partial and no-provider cases; parse logical JSON/status and inspect every entry for privacy regressions.
- [x] 3.3 Run full diagnostics/unit/Release gates only after those safety prerequisites; verify no SaveState, HWND repair or existing-deadlock claim was introduced.
