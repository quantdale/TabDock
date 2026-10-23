## Context

See proposal.md and audit A11. IconService deduplicates extraction but its synchronous consumer waits up to five seconds for an in-flight producer. GroupViewModel calls it during tab creation and construction. Picker icon delivery already has useful generation/cancellation patterns, and per-process cached failures are an existing contract.

## Goals / Non-Goals

**Goals:** No cosmetic extraction/wait in UI membership operations; bounded shared producers and stale-result rejection.

**Non-Goals:** No cache eviction, new image library, persisted icons, or delayed native capture admission.

## Decisions

1. Keep completed-cache lookup as the only synchronous icon operation in GroupViewModel construction/AddCapturedWindow. Assign cached frozen images or a null placeholder, complete authoritative membership/presentation work, then request cosmetic completion asynchronously. Never call the synchronous extractor or wait adapter on the UI path.
2. Expose a shared async single-flight lookup per case-insensitive executable path. Bound native producer concurrency to two and pending unique-path work to a finite policy cap (initially 256). Queue saturation returns a noncached no-result for that consumer, not a cached extraction failure; successful/failed actual extraction keeps existing cache semantics. Consumer cancellation does not cancel a producer shared by another current consumer.
3. Dispatch only frozen results to current live tab references. Use VM lifetime plus per-tab request identity, not a positional tab index. Removal, rollback of insertion, Detach, or model replacement invalidates the consumer and drops retained callbacks. A canceled consumer must not retain a closed container while native extraction continues.
4. Reuse the shared service pipeline for picker requests without regressing its batch/generation semantics. Background-only synchronous compatibility callers, if retained, must not be reachable from UI membership paths.
5. In tests, hold the real injected producer on a gate and prove membership completion before opening that gate. This is the correctness signal; elapsed milliseconds are supplementary. Test completion dispatch, removal, closure, cached-null reuse, concurrency bounds and saturation.

## Risks / Trade-offs

- Native extraction itself is not forcibly cancelable → bounded workers prevent unlimited active extraction; do not claim cancellation kills a native call.
- New async closures can retain VMs → cancel/drop consumer ownership on teardown and rerun weak-reference tests.
- Result order can race tab removal/reorder → reference/lifetime revalidation, never index-based delivery.

## Migration Plan

Add the gate-controlled red test, introduce shared async lookup, then switch tab creation/existing-member construction and revalidate picker batching. No cache migration. Preserve journal/capture durability tests; optional icon failure must never roll back an otherwise valid capture.
