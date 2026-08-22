# Post-Hardening: Durable State & Recovery (2026-08-23)

Objective: close the two verified persistence/recovery follow-ups queued by the
stranded-guest tails campaign — C-1 (state.json.bak durability/atomicity gap)
and C-2 (PendingRecoveryService resolution-ledger growth + orphan `.recovered`
sidecars). No Shepherd/native-window behavior change; this campaign touches
only durable bookkeeping and its tests.

Baseline `9965702908eeccef90b52595f50f48610c80f5e5`
(main == origin/main, clean worktree; builds/tests/tooling/validation green at
campaign start per STATE.md). Both findings re-proven against this baseline's
source before any edit.

## Verified findings

### C-1 — backup overwrite is not a durable transaction
`PersistenceService.CommitJson` (Services/PersistenceService.cs:258-265) runs,
inside `_writeGate`:

```
if File.Exists(state):  File.Copy(state, bak, overwrite:true)   // destructive in-place overwrite
WriteDurableText(tmp); File.Move(tmp, state, overwrite:true)    // primary: durable temp + atomic swap
```

The primary write is already atomic+durable. The backup is not:
`File.Copy(..., overwrite:true)` truncates and rewrites the LIVE `.bak` in
place, so `.bak` is recovery evidence destroyed non-atomically.

Failure windows (classified):

- **W1 crash/power-loss mid-copy**: destination opened with truncate semantics;
  power loss mid-stream leaves `.bak` truncated/partial while the primary still
  holds the previous good state. The last-known-good backup generation is gone.
- **W2 managed exception mid-copy** (disk full, IO error): same partial-`.bak`
  damage as W1 without process death; catch block aborts the save (primary
  untouched) but the old backup is already damaged.
- **W3 crash between backup completion and primary install**: `.bak` ==
  previous primary == current primary. Coherent but the older recovery
  generation was discarded unnecessarily early (acceptable outcome, avoidable
  damage window).
- **W4 failure/crash during primary temp write or install**: with W3 already
  committed, `.bak` holds only the previous primary; primary remains old-or-new
  atomically. No torn primary (MoveFileEx-with-replace is metadata-atomic), so
  the primary side of the transaction is sound today.

Not a problem: concurrent writers (`_writeGate` serializes all disk mutation).

### C-2 — resolution ledger growth + orphan `.recovered` sidecars
`ResolutionLedger.Transactions` is bounded (`CompactRetiredTransactions`,
≤64 retired, non-retired preserved) but `Resolutions` has NO equivalent bound
and nothing ever removes records. Growth vector: pending filenames are reused
across generations (`GetUniqueJournalPendingPath` picks the first free
`hidden-windows.json.pending[.NNN]`; retirement frees the base name), while the
sidecar `<name>.recovered` SURVIVES retirement (`RetireEntry` deletes only the
source, PendingRecoveryService.cs:1876-1880). A later generation with the same
filename reads the leftover sidecar and APPENDS its resolutions to it, so one
sidecar accumulates every generation's history without bound.

Additionally the orphaned sidecar itself is dead bookkeeping that accumulates
one file per retired source. Once its source is permanently retired:

- new-format evidence cannot inherit it (`FindResolution`/`FindTransaction`
  require exact `SourceInstanceId` equality; a recreated filename always gets a
  fresh generation id because `PreservePendingJournal` deletes the main journal
  and the next cache-from-empty mints a new GUID);
- the bounded legacy fingerprint fallback could theoretically consume it for a
  restored byte-identical pre-upgrade copy, but that path exists solely to
  consume legacy evidence once; treating a restored copy as fresh unresolved
  evidence is fail-safe (retention + supervised review), never data loss.

## Invariants preserved (non-negotiables)

One mutating product owner (`--recover-pending` lease); read-only discovery
never mutates ledgers or sources; journal-before-native-mutation ordering;
fail-closed unreadable/unverifiable evidence retention; interrupted/non-retired
transactions survive compaction; generation-scoped `SourceInstanceId` exact
matching; bounded legacy fingerprint fallback; single-writer `_writeGate`;
monotonic latest-wins save generations; corrupt-primary quarantine before
backup authority; no empty-state overwrite after unsafe load; unchanged-save
optimization; missing primary never destroys a valid backup.

## Design decisions

### C-1 final algorithm (CommitJson, inside lock + generation gate)
1. recreate state directory if needed (unchanged);
2. if primary exists: read primary bytes ONCE → durably write candidate to
   `state.json.bak.tmp` (`FileMode.Create` + WriteThrough + Flush(flushToDisk))
   → `File.Move(bak.tmp, .bak, overwrite:true)` atomic install. Previous `.bak`
   stays intact until the atomic swap instant; a failed stage leaves it
   untouched and the leftover `.bak.tmp` is truncated by the next attempt and
   never readable as state;
3. write primary candidate to `state.json.tmp`, flush, atomic install
   (unchanged);
4. advance `_lastSavedJson` only after the primary install succeeded.

Missing primary ⇒ skip the backup stage entirely (existing valid `.bak` is
preserved — matches current behavior and the Load-side classification rules).
Seam: extend the existing internal test constructor with three optional
delegates (readAllBytes / writeDurableBytes / atomicMove) defaulting to real
IO — smallest seam that makes every failure boundary deterministic; no storage
abstraction.

### C-2 rules
- **Liveness compaction** (`CompactUnreachableResolutions`, run wherever the
  ledger is rewritten for a live source): keep a Resolution iff it is reachable
  from the CURRENT source generation — same SourceFileId, same
  SourceInstanceId (`SameSourceInstance`), same SourceFileSha256 as the live
  bytes — plus, when the live source itself is legacy (null id), the
  empty-keyed fingerprint-only markers. Everything else (dead-generation
  records, stale-SHA records after external rewrite, cross-file junk) is
  unreachable bookkeeping and is dropped. Transactions are NOT touched here
  (`CompactRetiredTransactions` keeps its existing non-retired-preserving
  bound). Never a global newest-N rule.
- **Full-retirement sidecar removal**: when `RetireEntry` proves every sibling
  resolved AND no non-retired transaction lacks its own resolution marker (the
  existing checks) and deletes the source, the sidecar's remaining content is
  provably historical (resolutions + Retired transactions) → delete
  `<source>.recovered` best-effort immediately after the source deletion.
- **Crash convergence sweep** (mutating supervised path only): RunInteractive
  gains `SweepOrphanedResolutionSidecars` — for each `*.pending*.recovered`
  whose exact source path no longer exists: unreadable ⇒ retain+report
  (fail-closed); contains any non-Retired transaction ⇒ retain+report
  (possible interrupted-recovery trace left by external source deletion);
  otherwise delete+report. Discover()/FormatDiscovery stay strictly read-only.

Rejected alternatives:
- Sweeping orphan sidecars inside `Discover()` — rejected: discovery backs the
  read-only `--pending-pending` contract; deleting LEDGERS (unlike age-gated
  `.tmp` fragments) is evidence-adjacent destruction and must stay behind the
  product-mutation lease.
- Global newest-N cap on Resolutions — rejected by mission: can drop records
  required by live sources/siblings while retaining unreachable ones.
- Keeping orphan sidecars forever to preserve artificial same-generation
  replay dedup — rejected: replay-after-retirement requires external
  restoration of deleted evidence; re-supervision is fail-safe and the
  marker-and-source-coexist case (crash between MarkResolved and RetireEntry)
  still dedups via the live sidecar.
- Redesigning CommitJson into a generic storage abstraction — rejected;
  three delegates suffice.

## Waves

1. **Wave A — red regression net:** fault-injection persistence tests
   (new `tests/UnitTests/PersistenceBackupDurabilityTests.cs`) covering the ten
   mission boundaries against the injected seams; pending-recovery tests for
   resolution liveness bounding, full-retirement sidecar removal,
   crash-convergence sweep, partial-source retention, unreadable-sidecar
   retention, fresh-generation non-inheritance, legacy fallback preservation.
   Update the two tests that pin the OLD orphan-sidecar behavior
   (`ResolvedEntryRetirement_CanBeRetriedToCompletion`,
   `UnreadablePendingSibling_DoesNotBlockOtherDiskOnlyCleanup`) and re-anchor
   the replay/legacy tests on marker-and-source-coexist states.
2. **Wave B — root cause:** implement the staged backup transaction (C-1) and
   the ledger liveness compaction + sidecar retirement + orphan sweep (C-2),
   preserving every invariant above.
3. **Wave C — qualification:** Debug+Release builds 0w/0e, xUnit both
   configs, release tooling, `validate.ps1 -Ci -Publish`, openspec validate,
   `git diff --check`; OpenSpec change `durable-state-recovery-hardening`
   validated then archived per canonical workflow; STATE.md handoff with the
   next queued campaign recommendation.

Validation requirements: every C-1 boundary proven by a deterministic thrown
exception at the injected seam followed by a successful retry; every C-2 rule
proven by ledger-content assertions, not just exit codes. No real-input
ValidationDriver run needed (no interactive window behavior touched).
