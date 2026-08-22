# Wave 4 self-test migration — tasks

## 1. Inventory and baseline

- [x] 1.1 Re-inventory all self-test code (standalone files, embedded classes,
      `SplitGeometry.RunSelfTest`) against post-Wave-3 source; record
      classification/disposition per suite in
      `.agent/plans/wave4-selftest-migration.md`.
- [x] 1.2 Record green baseline: builds/tests/tooling/validate/OpenSpec,
      executable check counts (diagnostics=229, geometry=14,719,158).

## 2. Migration clusters

- [x] 2.1 Cluster 4A: small hermetic helpers (BuildIdentity, product naming,
      parser, DiagnosticTrace incl. concurrency stress, ShowWindowSemantics,
      SessionEndingPolicy, ConsoleSession, min-track buffer, maximize bounds,
      DeferredWindowPositionBatch).
- [x] 2.2 Cluster 4B: identity/capture/release/lease/stabilization with shared
      fixtures; delete migrated `Services/*SelfTest*.cs`.
- [x] 2.3 Cluster 4C: recovery journal, persistence classification (incl.
      deterministic injected access-denied fixture), pending-recovery
      discovery/execution/ledger.
- [x] 2.4 Cluster 4D: support-bundle privacy, monitor-DPI seam, capture-picker
      selection + generation-safe icons, WinEventMonitor install/reorder.

## 3. Executable command removal

- [x] 3.1 Delete `DiagnosticSelfTest`, `DiagnosticCommandKind.SelfTest`, the
      `--selftest-diagnostics` parse entry, and its dispatch case.
- [x] 3.2 Move the full partition matrix/fuzz/constraint qualification into
      `GeometryTests`; delete `SplitGeometry.RunSelfTest` and the
      `--selftest-geometry` startup branch.
- [x] 3.3 Update `scripts/validate.ps1` (drop both app launches; keep native
      ABI) and `scripts/release-qualify.ps1` (published artifact runs native
      ABI instead of removed commands).
- [x] 3.4 Update `.github/workflows/build.yml` hermetic-gate comment.
- [x] 3.5 Update README/TESTING/ARCHITECTURE references to the removed modes;
      leave historical audit/spec archives untouched.

## 4. Gates

- [ ] 4.1 Debug+Release builds 0w/0e; Debug+Release xUnit green; release
      tooling 150/150; validate.ps1 -Ci PASS; git diff --check clean.
