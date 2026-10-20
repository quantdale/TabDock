## Why

Audit finding A04 deterministically saves A, holds an async B write in flight, then requests A again; both final sync and async requests incorrectly leave B on disk. Unchanged detection compares completed disk content before registering the latest request.

## What Changes

- Define latest-request ordering independently of the last completed content marker.
- Make unchanged-save suppression aware of in-flight writes, including writers already inside the serialized commit boundary.
- Keep synchronous safety saves durable and latest async requests observable through the settlement barrier.
- Add gated A→B→A regressions and failure/retry coverage while retaining atomic backup/primary transactions.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `persistence-resilience`: require latest-request convergence even when the latest snapshot equals the earlier completed snapshot.

## Impact

Affects `PersistenceService` request bookkeeping, single-writer coordination, and deterministic persistence tests. No schema change or new user-facing setting. Source evidence: audit A04.

## Non-goals

Do not alter group metadata semantics, tab-switch debounce duration, journal durability, or replace synchronous safety saves with fire-and-forget work. Malformed-input recovery belongs to A01. Planning only.

## Sequencing

Apply after `fix-malformed-state-version-recovery` because both touch persistence safety boundaries. A generation bump alone is insufficient when B already passed its gate; the design must guarantee final on-disk convergence. Use explicit temporary paths for regressions; postpone the complete gate until storage-isolation/read-only prerequisites land.
