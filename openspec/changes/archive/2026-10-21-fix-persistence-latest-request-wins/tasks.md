## 1. Reproduce non-monotonic request ordering

- [x] 1.1 Verify A01's protected-load behavior is present and add gate-controlled A→async B→sync A and A→async B→async A tests against actual PersistenceService I/O seams.
- [x] 1.2 Hold B both before admission to the writer and after entering its transaction; release gates from an independent coordinator so the synchronous test cannot deadlock itself.
- [x] 1.3 Add A→B→A→C, latest-write failure/retry, idle-equality write-count and settlement-barrier tests.

## 2. Make coalescing observe admitted intent

- [x] 2.1 Separate immutable snapshot admission/order from completed-content equality; ensure admitted requests equal to old disk content still participate in ordering.
- [x] 2.2 Perform unchanged suppression only at the serialized writer decision where preceding writes are known, retaining stale-generation rejection for unstarted work.
- [x] 2.3 Preserve backup staging, primary atomic install, completion-marker-on-success and latest async barrier bookkeeping; keep unsafe-load/storage refusals protected.

## 3. Validate correctness and retained optimization

- [x] 3.1 Run `dotnet build TabDock.sln` and targeted latest-intent, single-writer, backup-failure and A01 cleanup-preservation tests against explicit temp paths.
- [x] 3.2 Verify every successful settled sequence leaves the latest snapshot on disk and idle unchanged requests avoid replacement I/O; record failure/retry evidence without asserting failed writes succeeded. (2026-10-20 follow-up correction R2: retry regression now reuses one fixed-Id group, asserts byte-identical intended JSON across failure/retry and a real reinstall; mutation-proved to fail on a prematurely advanced saved-content marker.)
- [x] 3.3 After A02/A10 land, run the full unit and Release gate; preserve normal debounce/journal semantics and defer unsafe process/full-suite invocation beforehand. (2026-10-20: A02/A10 landed; full unit Debug 930/930 + Release 930/930 incl. debounce/journal suites, canonical `scripts/validate.ps1 -Ci -Publish` PASS.) (2026-10-21: requalified after the A02 S1–S6 safety corrections — full unit Debug 944/944 + Release 944/944 incl. debounce/journal suites, canonical gate PASS exit 0; A04 fixes unchanged.) (2026-10-21 R-round: requalified again after the A02 R1-R4 repairs - full unit 944/944 Debug + Release, canonical gate PASS exit 0.)
