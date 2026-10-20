## Context

See proposal.md and audit A05. App's logical provider reads actual ContainerWindow state, but CreateReport invokes it inside Task.Run. Per-container exceptions are swallowed after the trace snapshot was already taken, so the current ZIP may omit both the data and its failure. The exporter uses ConfigureAwait(false); an existing async-context deadlock was not demonstrated.

## Goals / Non-Goals

**Goals:** Dispatcher-correct immutable snapshots, useful same-bundle partial status, responsive hotkey export, and UI-free CLI diagnostics.

**Non-Goals:** No guest repair, activation, layout, SaveState, extra monitoring, or expanded personal data.

## Decisions

1. Split logical collection from report/native observation and archive writing. The App hotkey orchestrator captures logical DTOs synchronously on the UI dispatcher for one bounded turn, deep-copies mutable collections, then awaits background export with those DTOs. The worker must not hold Window, view-model, DispatcherObject, or live provider references.
2. Make hotkey handling async with observed completion/error; do not call the synchronous ExportBundle wrapper from UI. Keep the CLI synchronous wrapper usable when no live provider exists. A background API caller needing live state must request bounded asynchronous collection before archive work, not synchronously Invoke a blocked dispatcher.
3. Carry explicit available/empty/partial/unavailable logical status and sanitized per-container issues into the same report. Snapshot trace/error metadata after logical collection or merge its bounded failures, so a close-race is not misrepresented as an empty session.
4. Add dispatcher-owned actual ContainerWindow/DTO tests and read the actual ZIP. Assert provider thread ownership, immutable post-capture data, same-bundle partial evidence, and no native presentation calls. Inject report observation/storage seams for fixture-only tests.

## Risks / Trade-offs

- Closing a container during collection → record partial/unavailable status and continue; do not retain or reopen the window.
- Async completion after shutdown → catch/report through a lifetime-safe continuation with no required UI owner or unobserved exception.
- Adding status metadata affects consumers → retain existing entries/fields, add explicit status, and rerun privacy/JSON contracts.

## Migration Plan

Add provider ownership tests, separate collection from export, and update hotkey orchestration. End-to-end ZIP/CLI runs use A02's verified isolated root and A10's read-only discovery. No state migration; rollback must not reintroduce off-thread live-provider calls or silently hide partial evidence.
