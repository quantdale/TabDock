# Wave 4 — Self-Test Migration Out of the Shipping Product Assembly

Objective: hermetic test code leaves `TabDock.exe` for `tests/UnitTests`; only
probes whose evidence requires a real built Windows process remain executable.
No product behavior change. Baseline SHA d3116ef (main == origin/main, clean).

## Baseline (2026-08-22, pre-change)

- Builds Debug/Release 0w/0e; tests 302/302 both configs; release-tooling
  150/150; validate.ps1 -Ci PASS; OpenSpec 20/20; git diff --check clean.
- Executable self-tests: `--selftest-diagnostics` checks=229 failures=0;
  `--selftest-geometry` checks=14,719,158 failures=0 (seed 20260810);
  `--selftest-native-abi` placementContract=PASS placementRoundTrip=PASS.
- Production self-test LOC: 5,110 standalone (`Services/*SelfTest*.cs`) +
  ~792 embedded in `Services/DiagnosticCommandLine.cs` (L197–988) +
  ~13 `ConsoleSessionSelfTest` + ~11 `SessionEndingPolicySelfTest` +
  ~141 `SplitGeometry.RunSelfTest` ≈ **6,067 lines**.

## Complete inventory & disposition

| Suite (file) | Lines | Real OS dep? | Target | Rationale |
|---|---|---|---|---|
| DiagnosticSelfTest aggregator (DiagnosticCommandLine.cs L197) | 124 | no | DELETE_REDUNDANT | pure wrapper counting sub-suite checks |
| BuildIdentity parse checks | 4 | no | MOVE_TO_XUNIT | pure parsing |
| Windows product normalization checks | 10 | no | MOVE_TO_XUNIT | pure string policy |
| DiagnosticCommandLine.TryParse checks | 5 | no | MOVE_TO_XUNIT | pure parser |
| DiagnosticTrace ring/defensive-copy/Clear | 18 | no | MOVE_TO_XUNIT | in-memory ring |
| DiagnosticTrace Parallel.For(512) stress | 3 | no | MOVE_TO_XUNIT | concurrency on in-memory type |
| ConsoleSessionSelfTest (ConsoleSession.cs L160) | 13 | no | MOVE_TO_XUNIT | StringReader/StringWriter scoped streams |
| SessionEndingPolicySelfTest (SessionEndingPolicy.cs L19) | 11 | no | MOVE_TO_XUNIT | two-line CAS policy |
| ProductMutationLeaseSelfTest | 271 | named mutexes only (no HWND) | MOVE_TO_XUNIT | ACL/exclusivity semantics via injected platform seam |
| DeferredWindowPositionSelfTest (embedded L896) | 93 | no (IDeferredWindowPositionApi fake) | MOVE_TO_XUNIT | pure chaining policy |
| CapturePicker SelectGroupAfterRefresh checks | 2 | no | MOVE_TO_XUNIT | pure selection policy |
| CapturePickerSelfTest (embedded L589) | 126 | temp dir + WPF dispatcher pump | MOVE_TO_XUNIT | generation-safe icon refresh; runs headless in xUnit like today |
| WinEventMonitorSelfTest (embedded L716) | 179 | no (IWinEventHookApi fakes) | MOVE_TO_XUNIT | install-unwind + desktop-reorder dispatch via recording SynchronizationContext |
| MinTrackProbeSelfTest (embedded L539) | 27 | no (poisoned HGlobal buffer) | MOVE_TO_XUNIT | buffer-initialization invariant |
| MinTrackProbeTimeoutMilliseconds <= 100 | 1 | no | MOVE_TO_XUNIT | constant pin |
| WindowIdentitySelfTest | 197 | no (fake native api) | SPLIT | ~80% ALREADY_COVERED by WindowIdentityGateTests (Wave 2); migrate unique: IsCaptureTokenAvailable refusal, probe-exception→Unverifiable, WindowIdentityBinding lifecycle; delete rest |
| CaptureBoundarySelfTest | 223 | no (FakeCaptureApi) | MOVE_TO_XUNIT | JournalCapture→SetProp→DWM boundary |
| WindowReleaseSelfTest | 835 | no (counting fakes) | MOVE_TO_XUNIT | release/hide transaction identity boundaries; TestFixture becomes shared test infra |
| MonitorDpiSelfTest | 233 | no (IMonitorDpiNativeApi fake) | MOVE_TO_XUNIT | probe lifecycle + conversion seam |
| ShowWindowSemanticsSelfTest (embedded L322) | 40 | no | MOVE_TO_XUNIT | pure post-state policy |
| NativeInteropSelfTest.PlacementContractIsStable | 15 | marshal-only | RETAIN_NATIVE | part of the ABI probe's evidence triple; not duplicated |
| NativeInteropSelfTest.PlacementRoundTripThroughUser32 / PlacementEnvironmentReport | 145 | REAL user32 CreateWindowEx/Get/SetWindowPlacement | KEEP_EXECUTABLE_PROBE | genuine real-process ABI evidence; windows-2022 CI job depends on it |
| ContainerGeometrySelfTest (embedded L567) | 21 | no | MOVE_TO_XUNIT | pure maximize-bounds math |
| RecoveryJournalSelfTest | 589 | no (FakeRecoveryApi) | MOVE_TO_XUNIT | schema compat + identity-safe rescue |
| PersistenceSelfTest | 428 | temp dir; ACL fixture is INJECTED UnauthorizedAccessException delegates (deterministic, no real ACEs) | MOVE_TO_XUNIT | classification/quarantine/backup/migration; LastAccessDeniedFixtureStatus reporting dropped (fixture always runs deterministically under xUnit, so SKIP classification is obsolete) |
| DiagnosticPrivacySelfTest | 154 | temp dir + real exported ZIP | MOVE_TO_XUNIT | credential-form/title-marker/username-word checks ALREADY_COVERED by DiagnosticsSanitizationTests; migrate path-redaction matrix, SanitizeLogTail, SanitizeJsonText, pending-report privacy, exported-ZIP privacy |
| PendingRecoverySelfTest | 2007 | no (IPendingRecoveryNativeApi fake + fault injector) | MOVE_TO_XUNIT | 43 supervised-workflow case groups |
| RuntimeStabilizationSelfTest | 173 | no | MOVE_TO_XUNIT | zero-commit hides, intentional-hide invalidation, active-tab sync write policy, ZOrder relative order |
| SplitGeometry.RunSelfTest | 141 | no | MOVE_TO_XUNIT + REMOVE command | full matrix/fuzz moves into GeometryTests authority; `RunSelfTest_ReportsZeroFailures` wrapper upgraded to own the matrix; delete `SplitGeometry.RunSelfTest` + `--selftest-geometry` |

## Executable probe decisions

- `--selftest-diagnostics`: REMOVE (§7). Everything it covered is hermetic.
- `--selftest-geometry`: REMOVE (§6). Pure deterministic math → xUnit owns it
  (existing direct facts + new full-matrix fact).
- `--selftest-native-abi`: RETAIN in TabDock.exe (Option A, §5). Small (~160
  lines), isolated class used solely by this command; real user32 evidence on
  windows-latest AND windows-2022 hosted jobs.

## Commit plan

1. `test: migrate small hermetic helper self-tests to xunit` — 4A
2. `test: migrate identity, capture, release, and lease self-tests to xunit` — 4B
3. `test: migrate recovery-journal, persistence, and pending-recovery self-tests to xunit` — 4C
4. `test: migrate privacy, stabilization, monitor-dpi, picker, and winevent self-tests to xunit` — 4D
5. `cleanup: remove diagnostics and geometry self-test commands` — 4E (aggregator,
   parser kind, app branch, RunSelfTest, validate.ps1, workflow comment, docs refs)
6. `state: wave 4 self-test migration complete` — STATE.md/TESTING/ARCHITECTURE

Each commit gates: Debug build 0w/0e + Debug tests green + git diff --check;
final gate runs the full §23 battery before push.
