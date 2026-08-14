## ADDED Requirements

### Requirement: Unresolved pending source bytes remain immutable
Resolving one pending recovery entry SHALL record logical resolution in the
durable sidecar ledger without rewriting or reindexing the source JSON while
any sibling remains unresolved. The source file MAY be deleted only as one
unit after every entry in it has a durable resolution marker.

#### Scenario: Retiring the first of two distinct siblings preserves the second transaction
- **WHEN** sibling B has a durable interrupted transaction and sibling A is resolved
- **THEN** the source SHA, entry indexes, and B transaction binding remain unchanged and B is resumable after restart

#### Scenario: Retiring siblings in reverse order preserves the first transaction
- **WHEN** sibling A has an interrupted transaction and sibling B is resolved first
- **THEN** A remains supported and resumable with no index shift

#### Scenario: Byte-identical duplicates remain logically distinct
- **WHEN** one of two byte-identical entries is resolved
- **THEN** the survivor is not classified as an unverifiable transaction merely because its fingerprint is duplicated, and it remains explicitly recoverable or safely bound by unique durable evidence

#### Scenario: Middle retirement preserves both outer siblings
- **WHEN** the middle entry of a three-entry file is resolved
- **THEN** the first and last entries retain their original indexes/fingerprints and remain recoverable

#### Scenario: Unknown source fields survive ledger-only retirement
- **WHEN** a source contains unknown root or entry fields and one entry is resolved while siblings remain
- **THEN** the source bytes and all unknown fields remain unchanged

### Requirement: Legacy rewritten-source evidence is rebound only when provable
Transactions and resolution markers written by the prior source-rewriting
implementation SHALL be matched to a changed source only with unique,
entry-scoped evidence. Positive ambiguity, foreign ledger records, and
unverifiable ownership SHALL remain pending and SHALL not be removed.

#### Scenario: A unique shifted sibling transaction is safely rebound
- **WHEN** an older transaction references a prior source SHA/index, the source SHA changed after another sibling was retired, and exactly one current entry has its fingerprint
- **THEN** the transaction is rebound to the current index durably before resume and remains recoverable

#### Scenario: Ambiguous duplicate legacy evidence fails closed
- **WHEN** more than one current entry or more than one unresolved ledger transaction could own the same legacy fingerprint
- **THEN** discovery reports unresolved ambiguity and removes neither source evidence nor native tokens

#### Scenario: Foreign transaction evidence is never consumed
- **WHEN** a ledger token or transaction cannot be tied to the current source file and unique entry identity
- **THEN** the evidence remains untouched and no native recovery is attempted

### Requirement: Durable native completion never repeats native recovery
Once a transaction records `NativeRecoveryComplete`, every later retry SHALL
perform only identity classification, exact-token cleanup when matched, ledger
resolution, and source retirement. Placement, visibility, and DWM operations
SHALL not run again.

#### Scenario: Hard-kill phase matrix resumes without duplicate native work
- **WHEN** recovery is interrupted after Prepared, SetProp, Placement, Visibility, DWM, NativeRecoveryComplete, TokenRemoved, resolution marking, or final retirement
- **THEN** restart resumes from the durable phase, preserves evidence on failure, and never repeats a native operation already marked complete
