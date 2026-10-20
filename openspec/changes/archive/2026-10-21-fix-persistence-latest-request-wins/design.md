## Context

See proposal.md and audit A04. The writer already serializes disk transactions, but BuildStateJson skips content equal to the last completed JSON before claiming a generation. A newer request therefore cannot supersede an older writer that is queued or already committing.

## Goals / Non-Goals

**Goals:** Final disk content follows the latest admitted request, synchronous saves remain barriers, and genuinely unchanged idle saves stay cheap.

**Non-Goals:** No debounce tuning, new schema, parallel disk writes, or weakening A01's unsafe-load protection.

## Decisions

1. Serialize and validate the caller-owned snapshot first; every admitted snapshot then claims request order, including one equal to the previously completed content. Storage-unavailable or unsafe-load rejections remain rejected, not replacement save requests.
2. Move unchanged-content suppression into the serialized writer decision, against the completed content visible after preceding writes settle. Do not skip based solely on the caller's earlier view of disk. A writer already past its generation check may finish; the later admitted request must still run/check afterwards and restore the latest content if needed.
3. Retain latest-generation rejection for work that has not begun, the backup-before-primary atomic transaction, and advancing completed-content markers only after successful install. Failed latest writes remain retryable; do not mark desired content as durably saved on enqueue.
4. Keep synchronous Save inside the writer barrier, and ensure WhenWritesSettledAsync observes the latest admitted async request. No task bookkeeping may replace a newer barrier with an older caller's task.
5. Use deterministic gates around actual I/O stages for A→B→A and A→B→A→C tests. A generation bump with an unchanged early return is explicitly rejected: it does not undo B that is already in flight.

## Risks / Trade-offs

- More snapshots reach the lock → retain no-I/O idle equality checks inside the gate and measure write counts.
- Latest-write failure cannot promise disk convergence → preserve old complete files, report failure, and test retry; never fake successful durability.
- Synchronous tests can deadlock their own held writer → release injected gates from an independent coordinator, not the blocked saving thread.

## Migration Plan

Land after A01. Add failing interleavings, adjust admission/dedup ordering, then rerun single-writer/backup/failure tests with explicit paths. No file conversion; a rollback must preserve atomicity and should not silently reintroduce caller-side equality suppression.
