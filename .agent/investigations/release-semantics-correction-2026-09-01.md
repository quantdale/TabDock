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

The predecessor archive boundary is satisfied. Final exact-candidate Release
closure remains pending until the next clean source commit is qualified and
fresh executable/publish hashes are recorded.

## Disposition and archive boundary satisfied

The 85 previously unchecked predecessor rows now each carry exactly one
allowed disposition, with one row per ID in
`.agent/investigations/visual-evidence-ledger-reconciliation-2026-09-01.md`.
The predecessor was strictly validated, its three delta-spec capabilities were
synchronized (`+11/~5`), and it was archived as
`openspec/changes/archive/2026-09-01-2026-08-31-visual-evidence-ai-review/`.
The repository project version is now `1.1.0`; workflow defaults and active
release documentation agree with that authority.

The final exact-candidate source boundary is the clean commit produced after
this archive, spec, version, task, and state settlement. Its SHA is resolved
dynamically immediately before the final Release build. No executable hash is
assigned here; the final build record must bind fresh bytes to that later
source SHA and must not reuse either `ef9fe35` or the b19 Milestone A hash.
No final v1.1 executable hash, push, or clean-tree claim is made by this
correction record yet; those are finalization evidence.

## Final source candidate and E rerun

The final source candidate boundary is the clean committed SHA
`6bb8ecc80b103ec9e2e1bc12cebe241b1ab9519f`. The exact command
`scripts/release-qualify.ps1 -Sha 6bb8ecc80b103ec9e2e1bc12cebe241b1ab9519f -Version 1.1.0 -OutDir .artifacts/release-final -Ci`
passed its source/HEAD, version, native ABI, strict OpenSpec, qualification,
and publish gates. It produced the self-contained Release v1.1 executable
with SHA-256
`cf442e369c56c7c06c23b33c25b3434b079398b479e188c47e03f2d76dfbc291`;
the release manifest SHA-256 is
`8665641b1247e36bdaf9863ba9ac3d2ce49a06cc568ec6b2d93eff6076a4e849`;
and `SHA256SUMS` SHA-256 is
`3414edc07dc528a8fe50a777cb4133e7831d9d1623f330e0aebca1721a19fb81`.
The embedded informational version is
`1.1.0+6bb8ecc80b103ec9e2e1bc12cebe241b1ab9519f`.

Post-boundary E evidence was rerun without relabeling the candidate:
Debug/Release solution builds and unit tests passed (`795/795` each),
ValidationDriver deterministic selftests passed (`153/153`), the catalog
reported 135 scenarios, strict OpenSpec validation passed `38/38`, canonical
Release validation/publish passed, and release-tooling regression passed
`179/179`. The final supervised packets are run
`1ca035cc-b73f-489b-8280-58c5ca713636` (healthy `VISUAL_OK`),
`9ad16a31-52cc-4018-b44b-24c3355901a7` (attempt 1 `VISUAL_DEFECT`, attempt 2
`VISUAL_OK`), `2cb0f55c-6437-4379-9599-f2ab74a26ad6` (flight `VISUAL_OK`),
and `ce628549-9cf9-4c39-a426-0c44195dfb15`
(`REVIEW_UNAVAILABLE`). Packet/result verification passed for all five
review records; screenshot tamper was rejected; native/visual precedence
remained fail-closed.

This record is evidence for the candidate above, not a new binary candidate.
Signing remains `NOT_CONFIGURED`, release mode is `QUALIFICATION_ONLY`, and
production eligibility is `BLOCKED_EXTERNAL`; physical mixed-DPI and external
Windows-human requirements remain blocked. The evidence-record commit and
remote parity are still pending when this section is first written.
