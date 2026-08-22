## ADDED Requirements

### Requirement: Resolution bookkeeping SHALL be bounded by source-generation liveness
The per-source resolution ledger (`<pending>.recovered`) SHALL NOT grow without
bound. Whenever the ledger is durably rewritten for a live pending source,
resolution records that are reachable only from other source generations —
records whose `SourceInstanceId` does not equal the live source's, records
whose `SourceFileSha256` no longer matches the live bytes, and records bound to
other file names — SHALL be compacted away. Resolutions required by the live
source (including every sibling check used to prove full retirement), by a
non-retired/interrupted transaction, and — when the live source itself is
pre-upgrade evidence without an instance id — the empty-keyed fingerprint-only
legacy markers SHALL always survive compaction. Compaction SHALL never remove
a non-retired transaction record.

#### Scenario: Dead-generation resolutions are compacted while the live generation stays intact
- **WHEN** a sidecar ledger holds resolutions from earlier generations of a reused pending filename and a recovery for the current generation rewrites the ledger
- **THEN** only resolutions belonging to the current generation's identity remain, the current entries still classify as already-resolved where marked, and unresolved siblings keep their markers

#### Scenario: An interrupted transaction survives ledger rewrites
- **WHEN** a non-retired transaction record exists in a sidecar and any ledger rewrite occurs
- **THEN** that record is retained unchanged and its entry remains resumable

#### Scenario: Legacy fingerprint migration keeps working for pre-upgrade sources
- **WHEN** a pre-upgrade pending source without a SourceInstanceId is resolved through the bounded fingerprint fallback
- **THEN** its empty-keyed marker survives subsequent rewrites while the source exists

## ADDED Requirements

### Requirement: A fully retired pending source SHALL take its sidecar with it
When supervised disk-only retirement proves every sibling entry resolved and no
non-retired transaction lacks its own durable resolution marker, deleting the
pending source SHALL also remove the now-historical `<source>.recovered`
sidecar; its remaining content is provably completed bookkeeping because new
generations always receive a fresh SourceInstanceId and can never inherit it.
A crash between source deletion and sidecar deletion SHALL converge on a later
supervised invocation: an orphaned readable sidecar whose source no longer
exists and which holds no non-retired transaction MAY be deleted, while an
unreadable orphan or one holding a non-retired transaction SHALL be retained
and reported. The read-only discovery contract SHALL perform no such cleanup.

#### Scenario: Full retirement removes the sidecar when safe
- **WHEN** the last unresolved sibling of a pending file is recovered and retirement deletes the source
- **THEN** `<source>.recovered` is removed as well

#### Scenario: Crash after source deletion converges on the next supervised invocation
- **WHEN** a previous invocation was interrupted after deleting the source but before deleting the sidecar, and a later supervised run executes
- **THEN** the readable, all-retired orphan sidecar is deleted and the run reports the cleanup

#### Scenario: Unreadable orphan ledgers are retained fail-closed
- **WHEN** an orphaned `.recovered` sidecar cannot be parsed during the mutating supervised sweep
- **THEN** it is retained, reported, and never silently destroyed

#### Scenario: An orphan holding an interrupted transaction is retained
- **WHEN** an orphaned sidecar contains a non-retired transaction record
- **THEN** the sidecar is retained and reported so possible interrupted-recovery traces remain reviewable

#### Scenario: Read-only discovery performs no destructive cleanup
- **WHEN** `--pending-recovery` discovery runs against a directory containing orphaned sidecars
- **THEN** discovery output is produced as before and no ledger or evidence file is modified

#### Scenario: Partially resolved sources keep their sidecars
- **WHEN** at least one sibling entry of a pending file remains unresolved
- **THEN** the source file, its sidecar, and all resolution markers stay in place
