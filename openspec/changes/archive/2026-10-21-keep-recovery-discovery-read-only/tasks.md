## 1. Lock down inspection non-mutation

- [x] 1.1 Add a failing explicit-directory default-discovery fixture containing an aged pending-prefix temp fragment and assert unchanged file set, bytes and last-write times.
- [x] 1.2 Extend fixtures with fresh temps, pending sources, ledgers, malformed and unrelated files; use recording native adapters to assert no recovery mutations.
- [x] 1.3 Inventory every Discover/FormatDiscovery/count/attention/doctor/export caller and current hygiene invocation.

## 2. Separate discovery from coordinated hygiene

- [x] 2.1 Make discovery unconditionally read-only and remove the mutating cleanup flag/side effect; migrate all inspection callers without changing parsing or attention classifications.
- [x] 2.2 Expose current eligible stale-temp hygiene only to explicit existing mutating startup/recovery paths after storage resolution and mutation-lease acquisition.
- [x] 2.3 Retain bounded age/name eligibility and sidecar/source retention rules; ensure root/lease refusal performs no hygiene deletion and no fallback cleanup.

## 3. Verify the full inspection surface safely

- [x] 3.1 Run `dotnet build TabDock.sln` and targeted default-discovery, attention, sidecar and hygiene tests with explicit temporary roots.
- [x] 3.2 With A02 present, run actual isolated pending CLI, doctor, support ZIP and in-process inspection tests; compare all fixture artifacts and assert zero native writes. (2026-10-20: isolated CLI fixture PASS 16/16 — probe proof, pending/doctor/ZIP over a run-owned root, product tree bytes+mtimes unchanged, outside sentinels unchanged; zero-native-mutation proven in-process via recording FakePendingApi.)
- [x] 3.3 Verify approved mutating hygiene deletes only eligible temps and no inspection takes the mutation lease; update the playbook's read-only boundary.
- [x] 3.4 After A02 and this change pass their sentinel suites, run the full unit/Release gate; do not reuse APPDATA-only isolation or claim authoritative journals were deleted by the original temp-fragment finding. (2026-10-20: sentinel suites 78/78 + 23/23 + 11/11 driver; full unit Debug 930/930 and Release 930/930; canonical `scripts/validate.ps1 -Ci -Publish` PASS end-to-end; isolated `--recover-pending` fixture proves mutating-path hygiene.) (2026-10-21 R-round: requalified after the A02 R1-R4 repairs - isolated CLI 16/16 + recover 6/6, script safety 24/24, full unit 944/944 both configs, canonical gate PASS exit 0.)
