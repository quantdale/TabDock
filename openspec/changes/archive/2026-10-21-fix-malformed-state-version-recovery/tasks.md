## 1. Lock down the failing load and cleanup boundary

- [x] 1.1 Add explicit-temp-path tests for Version strings, null, booleans, objects, arrays, fractions, overflow, zero and negative values; confirm the current wrong-kind cases go red.
- [x] 1.2 Add a materialized valid backup fixture and invoke actual emergency/exit-style save boundaries after failed restore; assert preserved corruption and recoverable group/tab intent, not just an empty result.
- [x] 1.3 Add preservation-denied, backup-unreadable, missing-Version legacy and representable-future cases with file-set/byte assertions.

## 2. Repair classification and save protection

- [x] 2.1 Guard root JSON kinds/numeric ranges before conversion and route invalid versions through deliberate corruption classification without changing supported schema versions.
- [x] 2.2 Ensure every unsuccessful/unexpected load leaves persistence protected until authoritative restore is established; retain quarantine-before-backup and atomic backup/primary behavior. (2026-10-20 follow-up: added direct outer-catch regression `PersistenceUnexpectedLoadProtectionTests`, mutation-proved to depend on the outer catch.)
- [x] 2.3 Update doctor metadata inspection using a pure tolerant classifier without constructing mutating persistence or quarantining files. (2026-10-20 follow-up correction R1: unsupported future-schema status is now authoritative over Groups-shape checks; matrix tests added, 9 red → green.)

## 3. Verify evidence preservation and compatibility

- [x] 3.1 Run `dotnet build TabDock.sln` and targeted malformed-version, classification, backup-durability and recovery-preservation tests against fixture paths; record red-to-green evidence.
- [x] 3.2 After A02/A10 safety prerequisites land, run actual isolated doctor fixtures and the full unit/Release gate; until then record the process/full-suite gate as deferred, never as an isolated PASS. (2026-10-20: A02/A10 landed and verified; isolated doctor fixture PASS (16/16 CLI fixture), full unit Debug 930/930 + Release 930/930, canonical `scripts/validate.ps1 -Ci -Publish` PASS.) (2026-10-21: requalified after the A02 S1–S6 safety corrections — full unit Debug 944/944 + Release 944/944, isolated CLI fixture 16/16, canonical gate PASS exit 0; A01 fixes unchanged.) (2026-10-21 R-round: requalified again after the A02 R1-R4 repairs - full unit 944/944 Debug + Release, isolated CLI fixture 16/16, canonical gate PASS exit 0.)
- [x] 3.3 Reconcile the persistence delta with A04 and verify this change introduces no schema bump, automatic HWND reattachment or native recovery mutation.
