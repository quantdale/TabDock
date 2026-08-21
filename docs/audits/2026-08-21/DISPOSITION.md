# August 21, 2026 Audit — Consolidated Remediation Disposition

One canonical record for the whole-codebase audits committed on 2026-08-21.
Raw reports are archived in this directory (`kimi-results.md`,
`kimis-results.md`, `alpha-results.md`, `luna-results.md`, `opus-results.md`,
`dsv4-results.md`, `flash-results.md`, `CODEBASE_AUDIT.md`,
`CODEBASE_AUDIT_v3.md` == `MUSE-RESULTS.md` == `sonnet-results.md`
(byte-identical), `real-goal.txt`). Every substantive raw finding maps to one
R21 workstream below, or to an explicit rejection class.

Verdict classes: FIXED (implemented this campaign), DUPLICATE, SUPERSEDED
(already fixed before or during the campaign), FALSE-POSITIVE,
INTENTIONAL/BY-DESIGN, DEFERRED (with reason).

## Workstreams

| ID | Title | Disposition | Main files | Regression coverage |
|----|-------|-------------|------------|---------------------|
| R21-001 | Release workflow input injection / workflow correctness | FIXED | publish-release.yml, prepare-release-candidate.yml, release.yml, release-tooling-tests.ps1 | static no-interpolation test + adversarial value fixtures + trust-boundary + boolean/provider/permissions tests |
| R21-002 | Stage-B external-evidence asset | FIXED | publish-release.yml, release-tooling.ps1 | Assert-ReleaseAssetsPresent gate + missing-file cases |
| R21-003 | Release-suite false green | FIXED | release-tooling-tests.ps1, sign-release.ps1, qa-split.ps1 | StrictMode + fused-variable fix + binding assertions |
| R21-004 | Released-HWND destructive close identity | FIXED | WindowIdentityGate.cs, WindowShepherdService.cs, CapturedWindow.cs | CaptureBoundarySelfTest, WindowReleaseSelfTest (recycle/nonce matrix) |
| R21-005 | Recovery generation identity | FIXED | PendingRecoveryService.cs, PersistedState.cs, WindowShepherdService.cs | PendingRecoverySelfTest generation/hygiene cases |
| R21-006 | Hide operation provenance | FIXED | GuestHideProvenance.cs (new), GuestLifecycleService.cs, WindowShepherdService.cs, ContainerWindow.xaml.cs | GuestHideProvenanceTests (11), WindowReleaseSelfTest provenance cases |
| R21-007 | Split state-machine single authority | FIXED | SplitPresentationController.cs, ContainerWindow*.cs | SplitPresentationControllerTests incl. dormant-removal divergence |
| R21-008 | Split transition atomicity / DefinePair result | FIXED | SplitPresentationController.cs, ContainerWindow.xaml.cs | RecoveryPending-at-every-boundary controller tests |
| R21-009 | Capture identity authority (title is not identity) | FIXED | WindowShepherdService.cs, ContainerWindow.xaml.cs | existing admission self-tests; title removed from all identity axes |
| R21-010 | Capture→membership transaction | SUPERSEDED (rollback paths verified) + title-veto removal in target revalidation | ContainerWindow.xaml.cs | pre-existing rollback paths exercised by capture flow |
| R21-011 | Release placement fallback | FIXED | WindowShepherdService.cs | fallback uses rcNormalPosition; show command re-applies zoomed state |
| R21-012 | Min-track/DPI composition | FIXED | ContainerWindow.xaml.cs, MonitorDpiService.cs, NativeMethods.cs | max(floor, guest-min) composition; per-monitor DPI cache; DPI/display invalidation |
| R21-013 | Stale z-order reorder repair | DEFERRED (cosmetic residual; foreground snapshot validated at first dispatch, final event re-pairs; no persistent desync demonstrated) | — | — |
| R21-014 | Multi-capture failure UX | FIXED | App.xaml.cs | single post-loop summary replaces per-failure modals |
| R21-015 | Test honesty/determinism | FIXED | PersistenceSingleWriterTests.cs, PersistenceSelfTest.cs, ValidationDriver Scenarios*.cs | barrier-based timing tests; FAIL demotes SKIP; explicit ctx.Skip |
| R21-016 | Recovery/persistence hardening | PARTIAL (generation identity + hygiene via R21-005; remaining sub-items listed below) | — | — |
| R21-017 | Raw-HWND lifetime caches | PARTIAL (context menus fixed; shepherd sets tracked below) | ContainerWindow.xaml.cs, IconService.cs | — |
| R21-018 | Picker/UI responsiveness | FIXED | CapturePickerViewModel.cs, IconService.cs, CapturePickerWindow.xaml | probe containment, selection-only requery, virtualization, bounded icon wait |
| R21-019 | Diagnostics/privacy/logging | FIXED | DiagnosticEnvironmentService.cs, DiagnosticReportService.cs, LoggingService.cs, ConsoleSession.cs, DiagnosticPrivacySelfTest.cs, EnvironmentFingerprint.cs | DiagnosticsSanitizationTests + expanded privacy self-test |
| R21-020 | Dead code / repo / docs cleanup | IN PROGRESS | this move; see below | — |

## Rejected / stale / false-positive findings worth noting

- **Startup restored-container z-order "missing"** — STALE: `App.xaml.cs`
  calls `ReconcileRestoredContainerZOrder()` after reopening restored
  containers.
- **Crash rescue should restore last-docked geometry** — REJECTED:
  pre-TabDock presentation restore is the documented contract.
- **System.Threading.AccessControl 10.x invalid on net8** — FALSE-POSITIVE:
  package ships net8.0 lib; resolves and builds clean.
- **HiddenWindowEntry needs its own [JsonSerializable]** — FALSE-POSITIVE:
  reachable child DTOs are generated from the registered root.
- **_capturedIndex raced from a background WinEvent thread** — REFUTED:
  WINEVENT_OUTOFCONTEXT delivers on the installing UI thread; affinity
  documented.
- **WM_GETMINMAXINFO handled=true disables the XAML floor** — FALSE-POSITIVE
  as stated (the incoming struct already carries the floor); the real defect
  was the unconditional overwrite of that floor with the guest-derived value,
  now composed via max().
- **ConverterTests CS8625 claims** — partially valid (11 warnings exist);
  converter nullability alignment remains open hygiene.
- **Split state should survive process restart** — INTENTIONAL limitation;
  not converted into a requirement.

## Deferred with justification

- R21-013 stale reorder final foreground re-check: narrow cosmetic race;
  dropped intermediate states are corrected by the final foreground event's
  own repair; revisit only with reproduced desync evidence.
- Budget-sink production wiring (PresentationLayoutCoordinator constructed
  without a sink): counters remain test-seam-only; wiring them into
  production is observability work, not correctness.
- Correlation IDs across capture/release/split/recovery logs (optional
  enhancement).
- GuineaPig ParseColor fallback breadth, driver --selftest CI wiring,
  performance-harness budget enforcement (harness scope).

## Validation snapshot (this campaign)

See `.agent/STATE.md` CURRENT STATUS for dated counts; historical counts in
older sections are snapshots, not current truth.
