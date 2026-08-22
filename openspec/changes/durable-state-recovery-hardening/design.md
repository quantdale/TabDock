## Design

### Context

`PersistenceService.CommitJson` already serializes all disk mutation behind
`_writeGate`, gates every attempt by a monotonic latest-wins generation, and
installs the primary via durable-temp + atomic move. Only the backup stage is
non-atomic: `File.Copy(state, bak, overwrite:true)` truncates the live backup
in place. On the recovery side, `PendingRecoveryService` bounds retired
transactions (≤64, non-retired preserved) but nothing bounds `Resolutions`,
and `RetireEntry` deletes the fully-resolved pending source while leaving its
`.recovered` sidecar behind; later same-filename generations append to the
leftover ledger forever.

### Goals / Non-Goals

- Goal: a failure while creating the next backup must not destroy the previous
  known-good backup; the primary must not be replaced unless the backup stage
  succeeded (or was legitimately skipped because no primary exists).
- Goal: resolution bookkeeping bounded by source-generation liveness; orphaned
  sidecars converge away on the mutating supervised path only.
- Non-Goal: any change to Shepherd rescue, journal writes, load-side
  classification/quarantine rules, or the read-only discovery contract.
- Non-Goal: a generic storage abstraction; three injected delegates are enough.

### Decisions

**D1 — staged backup transaction.** Inside the lock and generation gate:
read primary bytes once → `WriteDurableBytes(state.json.bak.tmp, bytes)`
(Create-truncate + WriteThrough + Flush(flushToDisk)) →
`File.Move(bak.tmp, .bak, overwrite:true)`. Windows MoveFileEx-with-replace is
a metadata-atomic replacement: the previous `.bak` remains intact until the
swap instant and a failed move leaves it in place. The leftover `.bak.tmp`
from a crashed attempt is truncated by the next attempt and is never readable
as state. Skip the whole stage when no primary exists so an existing valid
backup survives (missing primary is a documented recoverable state).

**D2 — smallest fault seam.** Extend the internal test constructor with three
optional delegates (`readAllBytes`, `writeDurableBytes`, `atomicMove`)
defaulting to the real operations. Every mission boundary maps to one
delegate+path combination; tests throw deterministically per path.

**D3 — liveness compaction rule.** When rewriting a sidecar for a live source,
keep a Resolution iff: same SourceFileId AND `SameSourceInstance` against the
live source AND SourceFileSha256 equals the live bytes' SHA — plus, when the
live source itself has no instance id (pre-upgrade), the empty-keyed
fingerprint-only legacy markers. Everything else is unreachable bookkeeping.
Transactions keep their existing compaction (never removes non-retired).

**D4 — sidecar retirement + crash convergence.** After `RetireEntry` proves
full resolution and deletes the source, delete `<source>.recovered`
best-effort; remaining content is provably historical at that point because an
existing check already refuses retirement while a non-retired transaction
lacks its own durable resolution marker. The crash window (source deleted,
sidecar left) converges via `SweepOrphanedResolutionSidecars` in RunInteractive:
no source file + readable ledger + zero non-retired transactions ⇒ delete;
unreadable or holds a non-retired transaction ⇒ retain and report.

### Risks / Trade-offs

- Artificial same-generation replay after full retirement (external
  restoration of deleted evidence) loses its dedup memory and is re-offered as
  fresh supervised evidence — accepted: fail-safe direction, requires explicit
  human confirmation before any native work; within-generation replay with
  source+sidecar still present keeps its existing protection.
- Legacy fingerprint markers deleted with a retired legacy source would let a
  restored byte-identical pre-upgrade copy be re-reviewed instead of
  auto-skipped — accepted for the same reason; new pending files always carry a
  fresh SourceInstanceId, so this affects only externally restored copies.
- `.bak.tmp` adds one more artifact shape in the state directory — mitigated:
  never read by Load/classification, self-healing on retry.

## Open Questions

None.
