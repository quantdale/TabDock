## Why

Audit finding A11 holds a picker icon extraction for 1.2 seconds and calls actual AddCapturedWindow on the UI thread; membership insertion waits roughly 1.2 seconds. The cosmetic API can wait up to five seconds when another worker owns the same executable's extraction.

## What Changes

- Use only completed icon-cache entries synchronously during tab/view-model construction and capture completion.
- Join or schedule bounded, deduplicated asynchronous icon resolution without UI-thread waits or inline extraction.
- Apply frozen results on the owning dispatcher only to current, live tab/view-model generations.
- Add gate-controlled nonblocking/lifetime/failure tests while preserving cached-null and per-executable reuse semantics.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `capture-picker-icons`: extend nonblocking, generation-safe icon delivery to captured-tab creation and existing populated group view models.

## Impact

Affects IconService, GroupViewModel/TabViewModel icon projection and teardown, and picker/tab icon tests. Native capture/identity/journal safety ordering remains authoritative; cosmetic completion does not decide membership. Source evidence: audit A11.

## Non-goals

No cache eviction policy change, persistent icon cache, new image package, or native capture scheduling change. A measured controlled wait is not a claim of a universal speedup. Planning only.

## Sequencing

Independent of motion fixes; preserve A03's UI collectability invariants when adding callbacks. Use explicit-root fixtures for unit tests; full process/ZIP qualification waits for storage-isolation/read-only prerequisites. Existing cache lifetime requirements remain in force.
