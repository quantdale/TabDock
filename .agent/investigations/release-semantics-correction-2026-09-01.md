# Release and archival semantics correction

**Date:** 2026-09-01  
**Campaign:** `visual-evidence-closure-and-performance-requalification`

## Authority

Git is authoritative for the current branch, `HEAD`, `origin/main`, and worktree. Resolve those values dynamically; this record does not identify the commit that contains this record as a candidate.

## Recorded pre-final deterministic candidate

The currently recorded pre-final deterministic Release candidate is the exact source commit:

`ef9fe35088abe8c12c53cc4335ae22f30ab3fc75`

The existing qualification record in the `ef9fe35` campaign commit reports:

- unsigned qualified executable SHA-256: `E38E9930546CDBD77218F5F9B59BCDDCEC27AB67177B3DDCA712DE6288DA5896`;
- publish executable SHA-256: `ECE1D32DC409ECBA48B6AF0633AA98623356C695B15AF48A330201C97E222564`;
- source identity: `ef9fe35088abe8c12c53cc4335ae22f30ab3fc75`.

These hashes and the `ef9fe35` source identity are preserved as **pre-final deterministic qualification evidence**. They are not the final v1.1 candidate because supervised Milestone A, predecessor disposition/archive, and final metadata/spec changes were not yet complete.

The later `7f4b9df0521cd81b28a3f82311dfabfa948ff1c1` commit only changed the successor task ledger. No exact Release executable built from that SHA is claimed by this campaign. The presence of generated build metadata or a source-tree informational version is not binary candidate evidence.

## Successor E status

Successor tasks 6.2–6.5 retain their prior `ef9fe35` execution evidence as pre-final evidence only. They are reopened for final closure where the final-candidate boundary requires a rerun. In particular, task 6.3 is not complete: its phrase “final clean committed tree” requires a new run after all of the following settle:

1. supervised Milestone A;
2. disposition of all 85 unchecked predecessor rows;
3. predecessor synchronization/archive, if justified;
4. all final metadata/spec/task/state changes and their commits.

The final v1.1 candidate must be built once from that later clean committed SHA. Its executable/publish hashes must be freshly recorded and must not reuse either historical hash above.

## Archive boundary

The predecessor remains unarchived. The existing 85-row reconciliation is classification evidence, not final disposition. Before archive, every currently unchecked predecessor row must receive exactly one allowed row-level disposition:

- `COMPLETED_AND_PROVEN`
- `ACCEPTED_SUPERSEDED`
- `MIGRATED_TO_SUCCESSOR`
- `MIGRATED_TO_DPI_TOPOLOGY_CAMPAIGN`
- `MIGRATED_TO_REAL_APP_CAMPAIGN`
- `ACCEPTED_BLOCKED_CAPABILITY`
- `ACCEPTED_BLOCKED_SUPERVISED`

The final disposition matrix will remain durable after archive and will identify each of the 85 task IDs individually. No successor task range is treated as closing an unrelated predecessor remainder without that mapping.

## Current closure truth

Final exact-candidate Release closure remains pending. No archive, final v1.1 candidate, final hash, push, or clean-tree claim is made by this correction record.
