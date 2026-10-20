## Why

Audit finding A01 reproduces a wrong-type JSON `Version` escaping state classification and leaving saves enabled. Startup emergency cleanup then replaces the useful backup with malformed bytes and writes an empty primary; doctor also throws instead of classifying the same input.

## What Changes

- Classify missing, invalid-type, non-integral, out-of-range, supported, and future schema versions explicitly.
- Preserve corrupt input before backup recovery and prevent cleanup saves from replacing unresolved evidence with empty state.
- Make doctor report invalid schema metadata without throwing or mutating files.
- Add adversarial root-type and complete load-failure/cleanup-save regression coverage.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `persistence-resilience`: define total schema-version classification and evidence-preserving behavior across cleanup saves.
- `production-diagnostics`: require tolerant, read-only reporting of malformed root schema metadata.

## Impact

Affects `PersistenceService`, `DiagnosticEnvironmentService`, startup/exit safety-path tests, and isolated persistence fixtures. No state-schema migration, new dependency, or native-window mutation is introduced. Source evidence: `.agent/investigations/repository-audit-2026-10-20.md`, A01.

## Non-goals

Do not change tab-save scheduling (A04), add automatic reattachment, or recover tokenless native windows. This proposal does not implement a fix.

## Sequencing

Independent first-priority data-safety fix. Coordinate shared `PersistenceService` edits with `fix-persistence-latest-request-wins`; apply this change first. Full process/ZIP validation must wait for `isolate-validation-storage-roots` and `keep-recovery-discovery-read-only`; explicit-path parser/cleanup tests do not need those prerequisites.
