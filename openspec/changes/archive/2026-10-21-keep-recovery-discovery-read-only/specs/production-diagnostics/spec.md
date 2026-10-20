## ADDED Requirements

### Requirement: Read-only recovery inspection SHALL preserve all storage artifacts

Pending-evidence discovery SHALL be non-mutating by default and for pending-recovery listing, doctor, support export, launcher attention and count projections. Apart from explicitly requested report output, inspection SHALL preserve the product directory's file set, file bytes and last-write times, including aged pending-prefix temporary fragments, sources, sidecars and transaction records. It SHALL perform no native recovery mutation and remain usable without the product mutation lease. Eligible hygiene SHALL occur only on an explicitly mutating path after both storage-root proof and mutation-lease acquisition.

#### Scenario: Aged temp fragments survive default discovery

- **WHEN** default discovery inspects a directory containing pending-prefix temp fragments older than the existing hygiene age limit
- **THEN** it leaves those fragments and all other evidence artifacts unchanged while reporting inspection as read-only

#### Scenario: CLI and in-process inspection preserve the same fixture tree

- **WHEN** listing, doctor, support export and launcher/count inspection run against verified isolated fixtures containing sources, ledgers, old/new temps, malformed and unrelated files
- **THEN** fixture file sets, bytes and last-write times are unchanged and no native recovery mutation is recorded

#### Scenario: Explicit hygiene retains its safety boundary

- **WHEN** an existing mutating startup or supervised recovery session performs eligible temp hygiene
- **THEN** it has already validated its storage root and acquired the existing mutation lease, deletes only policy-eligible temp fragments, and preserves authoritative pending/ledger and unrelated evidence

#### Scenario: Refused coordination does not authorize cleanup

- **WHEN** the root is unverified or the mutation lease is unavailable
- **THEN** no hygiene deletion occurs and read-only inspection does not attempt to bypass the refusal
