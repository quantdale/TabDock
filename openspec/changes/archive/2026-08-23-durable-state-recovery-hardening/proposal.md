## Why

The two verified persistence/recovery follow-ups from the stranded-guest tails
campaign remain open: `PersistenceService.CommitJson` overwrites the live
`state.json.bak` in place with `File.Copy(overwrite:true)`, so a failure or
power loss while creating the next backup can destroy the previous known-good
backup before the new primary is installed; and the pending-recovery sidecar
ledger grows without bound (`Resolutions` are never compacted, retired sources
leave orphan `<source>.recovered` files that later same-name generations keep
appending to). This change makes the backup a real durable transaction stage
and gives resolution bookkeeping a generation-liveness lifecycle, without
changing any Shepherd/native-window behavior.

## What Changes

- Stage the persistence backup before primary replacement: read the primary
  once, durably write a candidate to `state.json.bak.tmp`, then atomically
  install it over `state.json.bak`; a failure at any backup boundary leaves the
  previous backup and the primary untouched and does not advance the saved-
  content marker. A missing primary never destroys an existing valid backup.
- Bound the per-sidecar resolution ledger by source-generation liveness:
  resolutions reachable only from dead generations (other SourceInstanceId,
  stale source SHA, other filename) are compacted whenever the ledger is
  rewritten for a live source; non-retired transactions remain untouched by
  compaction.
- Retire the `.recovered` sidecar together with its fully-retired pending
  source, and sweep orphaned sidecars (no source file, readable, all
  transactions retired) on the mutating supervised recovery path — unreadable
  sidecars and sidecars holding non-retired transactions are always retained.
- Add deterministic fault-injection regression coverage for every backup
  transaction boundary and a regression matrix for ledger bounding, full
  retirement, crash convergence, partial sources, unreadable ledgers,
  fresh-generation non-inheritance, and legacy fallback preservation.

## Capabilities

### New Capabilities

- None.

### Modified Capabilities

- `persistence-resilience`: backup staging becomes a durable, atomic,
  primary-gating transaction stage.
- `hidden-window-journal`: resolution-ledger compaction and sidecar retirement
  join the supervised recovery lifecycle.

## Impact

Affected areas: `Services/PersistenceService.cs` (CommitJson + internal test
seam), `Services/PendingRecoveryService.cs` (ledger rewrite paths, RetireEntry,
RunInteractive), and their xUnit suites. Read-only discovery output and the
`--pending-recovery` contract are unchanged; no application window behavior
changes; no new dependencies.
