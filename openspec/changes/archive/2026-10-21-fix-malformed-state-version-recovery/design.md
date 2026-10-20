## Context

See proposal.md and audit A01. The state reader distinguishes corrupt, unreadable, and future data, but an unchecked root Version conversion bypasses that classification. Startup and exit save through GroupManager even after restore failure. Doctor has a separate unchecked conversion. The journal and native rescue authorities are not part of this repair.

## Goals / Non-Goals

**Goals:** Total root-version classification; safe continuation through cleanup; recover a valid backup only after preserving corrupt primary evidence.

**Non-Goals:** No format-version bump, new recovery policy, or changes to async request ordering.

## Decisions

1. Inspect JSON kind before numeric conversion. Missing Version retains legacy-v1 semantics; positive supported integers migrate as today; supported-range future integers are unsupported; wrong kinds, fractions, integer overflow, and nonpositive versions are corruption. Prefer explicit classification over catching every exception and returning an ambiguous empty state.
2. Keep quarantine-before-backup as the load transaction. If preservation or backup reading fails, latch unsafe-load protection before returning. Unexpected load exceptions must also prevent later cleanup saves; do not hide them by silently enabling persistence. A successful preserved-corruption/valid-backup restore remains writable.
3. Give doctor equivalent tolerant metadata interpretation without calling mutating Load/quarantine. Share only a pure classifier where that removes drift; do not construct PersistenceService merely to inspect files. Future data is reported unsupported, never rewritten.
4. Exercise the actual manager emergency-save boundary after each unsuccessful load. Parser-only tests would miss the observed backup replacement.

## Risks / Trade-offs

- A broad exception fallback could conceal programming errors → log classified failures and preserve evidence; retain separate unexpected-error diagnostics.
- Treating absent Version as corrupt would break legacy files → retain and test absence-as-v1.
- A01 and A04 touch the same writer → land A01 first and rerun its evidence-preservation tests after A04.

## Migration Plan

No on-disk migration. Add red-capable explicit-path fixtures, repair classification/load protection, then verify valid/legacy/future/read-denied cases and cleanup saves. Rollback must not delete quarantined files or the original backup. Full CLI tests wait for A02/A10 storage safety fixes; no production-profile corruption fixture is permitted.
