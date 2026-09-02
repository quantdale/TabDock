# Real-app hardening final closure — corrective ledger reconciliation

**Date:** 2026-09-02
**Campaign:** `openspec/changes/real-app-hardening-final-closure/` (ACTIVE)
**Archived predecessor:** `openspec/changes/archive/2026-09-01-real-app-hardening/`
**Purpose:** corrective closure record for the four defects found by review of
the prematurely archived real-app hardening campaign.

## Authority

Git is authoritative for `HEAD`, branch, `origin/main`. This file is a durable
provenance record. The historical archive remains evidence; this corrective
campaign becomes the authority for the outstanding closure requirements only.
History is not rewritten — the premature archive stands as archived.

## Historical ledger correction

The archived real-app `tasks.md` contains exactly **38 checkbox rows**
(0.1–0.4, 1.1–1.8, 2.1–2.4, 3.1–3.5, 4.1–4.4, 5.1–5.4, 6.1–6.4, 7.1–7.5),
verified by `grep -c '^- \[[ x]\]' .../tasks.md` = 38.

The earlier final report's **"26 tasks" count was incorrect.** No archived
historical task is deleted or renumbered to make counts match.

## Corrective mapping table

| PRIOR ARCHIVED TASK | HISTORICAL CHECK STATE | REVIEW FINDING | CORRECTIVE TASK | FINAL EVIDENCE |
|---|---|---|---|---|
| 1.7 — Retain restricted before/fullscreen/after visual packets per browser and run verifier `Valid:true` | [x] | Checked complete without actual restricted real-browser visual packets; Chrome checkpoint run produced no visual logs (monitor/topology binding unusable) | REOPENED_FOR_CORRECTIVE_CLOSURE | **SATISFIED_POST_ARCHIVE_BY_FINAL_CLOSURE** — real restricted packets at final candidate `fbc4d92`: Chrome run `6a5bb064` packet `c03de022…dcfe`, Edge run `76fb0a68` packet `1a47df5a…a019`, Brave run `b151a8ce` packet `75c6c51c…c652`; 14 PNGs each (baseline/before/fullscreen/after), `TEST_OWNED`, `AllowVirtualDesktop=false`, topology `92790d2a`; offline verifier `Valid:true` on all three (see 2.x) |
| 4.3 — Review retained real-app visuals via `.agent/workflows/visual-evidence-review.md` with a capable multimodal agent | [x] | Checked complete without any actual multimodal review of real browser imagery | REOPENED_FOR_CORRECTIVE_CLOSURE | **SATISFIED_POST_ARCHIVE_BY_FINAL_CLOSURE** — operator (capable human vision reviewer) inspected contact sheets + all 14 raw checkpoints per browser; hash-bound `visual-review-result.json` for Chrome/Edge/Brave; verdicts SUSPECT (benign CAPTURE_ARTIFACT container-fullscreen frames) / SUSPECT (same, privacy clean) / OK; verifier `Valid:true` (see 3.x) |
| 7.1 — Run after implementation settles: `dotnet build -c Debug/Release`, `dotnet test -c Debug/Release`, build ValidationDriver/GuineaPig Release, `selftest all`, `capability`, `visual`, `catalog`, `plan`/`plan real-app`, deterministic topology/visual/resource/privacy, native ABI, release-tooling, historical bundle compatibility, strict OpenSpec, and `scripts/validate.ps1 -Ci -Publish` — record current counts dynamically | [x] | Checked complete although canonical `validate.ps1 -Configuration Release -Ci -Publish`, the explicit native ABI gate, and resource/privacy/recovery qualification were not all actually executed in the final session | REOPENED_FOR_CORRECTIVE_CLOSURE | **SATISFIED_POST_ARCHIVE_BY_FINAL_CLOSURE** — at final candidate `fbc4d92`: `validate.ps1 -Configuration Release -Ci -Publish` exit 0 (publish smoke sha `747187…2F85`), `--selftest-native-abi` PASS, resource-headless PASS (`--cycles 32 --profile all --seed 20260824`), 812/812 unit Debug+Release, 173/173 selftest, 179/179 release-tooling, doctor/support-bundle (privacy clean)/pending-recovery PASS, strict OpenSpec 39/39, catalog 135, plans emitted (see 4.x) |
| EDGE_FIRST_VALID_FAIL_PRODUCT_DISPOSITION | — (new corrective obligation) | Edge first invocation `FAIL_PRODUCT` on second F11 cycle (run `ba1cedc3`), rerun PASS (run `35525ad6`); current disposition `FLAKE_UNCLASSIFIED` with no explanation | (new obligation) | **SATISFIED_POST_ARCHIVE_BY_FINAL_CLOSURE** — terminal classification `PROVEN_EXTERNAL_BROWSER_INPUT_FLAKE`: 5×3 characterization at `fbc4d92` 15/15 PASS (runs `928524b2`, `f0117322`, `7b8adc23`, `08a14221`, `b3d9464c`; setup-only `FAIL_HARNESS` `8cfbc878` preserved); historical mechanism reconstructed from TabDock log (one posted F11 exit not consumed by Edge before the 3500 ms settle; no product/harness change); no unresolved valid product defect remains (see 1.x) |

When a requirement is satisfied by this corrective campaign it is recorded as
**SATISFIED_POST_ARCHIVE_BY_FINAL_CLOSURE** — never as satisfied before the
original archive.

## Scope rules

- Only the four carried-forward obligations plus minimal source/harness repair
  proven necessary by valid evidence.
- No product repair is authorized merely to obtain closure.
- Prior valid evidence is preserved: DPI/topology archived+accepted, Chrome and
  Brave native F11 PASS, Notepad `ACCEPTED_BLOCKED_ENVIRONMENT`, Terminal
  `ACCEPTED_BLOCKED_CAPABILITY`, Firefox `SKIP_CAPABILITY`.
- Capability blocks are not transformed into failures; valid physical PASS is
  not transformed into synthetic claims.

## Visual binding root cause (task 2.1) — classified 2026-09-02

**Classification: HARNESS observability defect (not production).**

`TryGetVisualMonitorBinding` (`Scenarios.PhysicalCertification.cs`) has four
failure gates — zero HWND, monitor handle not present in
`EnumerateDpiMonitors`, no `capabilities.Topology` monitor matching the
observed bounds/work/DPI, and `VisualTopologyFor` returning null — and every
gate returned `false` silently with no diagnostic. `BrowserFullscreenContained`
treated the result as best-effort and silently skipped all visual checkpoints.
That is why the prior Chrome checkpoint run produced no visual logs: either
the topology snapshot lacked a matching monitor row for the observed handle
(or the enumeration handle differed), and the harness recorded no reason.

**Fix (task 2.2, harness-only, production TabDock unchanged):** each failure
gate now emits `VISUAL_BINDING_UNAVAILABLE: <reason>` with the specific
monitor/handle/topology detail, and the `browser-fullscreen-contained` call
site emits `VISUAL_SKIPPED: <guest> ...` when visual is enabled and no binding
is available. No scenario outcome, signature, capture, or product code
changed. Regression evidence: xUnit 812/812 PASS and ValidationDriver selftest
173/173 PASS on the changed tree; the physical visual runs below now record
the exact binding reason (or produce packets).

## Status log

- 2026-09-02: corrective change created, strict validation `valid=true`
  before implementation; ledger count 38 verified; mapping table above
  initialized with TBD evidence; Edge characterization and Chromium visual
  evidence pending.
- 2026-09-02 (remediation): audit classified readiness as
  CLOSURE_INVALID_EVIDENCE_GAP (0/35 corrective tasks done); remediation
  started — visual binding root cause classified as harness observability
  defect, minimal diagnostic fix applied (driver-only), Debug+Release build 0
  warnings/0 errors, xUnit 812/812, selftest 173/173; commit to follow as
  FINAL_CANDIDATE_SHA.
- 2026-09-02: **Edge visual packet produced and verified** (run
  `25dc6648-bd0d-4675-9689-743b03d2bf1a`, candidate `43360c6`): 3 F11 cycles
  all PASS natively; 21 PNG checkpoints (baseline/before/fullscreen/after per
  cycle, guest+container) all hash-verified; review packet SHA-256 matches;
  topology binding `snapshotId 92790d2a`; `syntheticTopology=false`;
  captures 20/20 succeeded; `derivedArtifactFailures=[]`. **PRIVACY DEFECT**:
  Edge's implicit sign-in to the OS Microsoft account surfaced the device
  account email (`palacamichaeldale16@outlook.com`) and Edge's sync prompt
  text ("passwords, history, credentials") in the captured imagery, which
  violates TEST_OWNED/no-logged-in-accounts. Harness fix: added
  `--disable-sync --disable-features=msImplicitSignin,msEdgeFirstRunExperience`
  to the isolated Chromium launch (Scenarios.Browser.cs); production TabDock
  unchanged. Re-capture pending a clean exclusive desktop.
- 2026-09-02: subsequent Edge run aborted `FAIL_HARNESS` at foreground
  qualification — a non-exclusive desktop (user's real Edge window "Bohu on X"
  and a terminal overlapping the test surface) prevented a verified foreground
  target. The harness correctly refused to send input. Blocked on an
  exclusive supervised desktop before completing the Edge 5×3 and
  Chrome/Brave visual runs.
- 2026-09-02 (headless gates at candidate `fbc4d92`, before physical phase):
  Release build 0 warnings, xUnit 812/812 Debug+Release, selftest 173/173,
  strict OpenSpec valid (change + all specs 39/39), canonical
  `validate.ps1 -Configuration Release -Ci -Publish` PASS (publish smoke
  sha256 `747187…2F85`), `--selftest-native-abi` PASS (placement 44-byte
  contract, round trip OK), deterministic resource-headless PASS (`--cycles 32
  --profile all --seed 20260824`, source SHA `fbc4d92`, visual measurements
  PASS synthetic 900 samples / 30 cells / 720 pairs), `--doctor` exit 0,
  `--support-bundle` exit 0 with privacy scan NO_PRIVACY_HITS (redacted
  %APPDATA% paths, no personal paths/emails), `--pending-recovery` exit 0
  (0 pending), release-tooling 179/179 PASS under PowerShell 7 (the script
  requires pwsh; the earlier 72 failures under Windows PowerShell 5.1 were
  `System.IO.Path.GetRelativePath` absence — environment artifact, not a
  product/gate failure), qualification plans `release`/`all`/`physicalMixedDpi`
  emitted (physical cells honestly `BLOCKED_SUPERVISED`), catalog
  `scenario-catalog-2026-09-01-v2` 135 scenarios listed.
- 2026-09-02: physical phase re-blocked — a fresh probe shows the primary
  display fully covered by user's maximized real Edge ("Verboo Code | AI in
  your terminal…"), Chrome ("Alphaus Sage — GMO AWS Cost Extraction"), and
  Brave (YouTube) windows, plus overlay surfaces. The harness would refuse
  input (BLOCKED_ENVIRONMENT/FAIL_HARNESS at foreground qualification). Next
  action: operator clears the primary desktop and refrains from mouse/keyboard
  during the supervised runs (Edge 5×3 first, then Chromium packets).
- 2026-09-02 (resumed session): operator cleared the desktop for supervised
  runs. **Edge 5×3 characterization completed at candidate `fbc4d92`** — 5
  independent invocations × 3 F11 cycles = **15/15 PASS** (run IDs
  `928524b2`, `f0117322`, `7b8adc23`, `08a14221`, `b3d9464c`; one setup-only
  `FAIL_HARNESS` run `8cfbc878` preserved, not hidden). Every invocation used a
  fresh isolated Edge profile, valid lease, topology snapshot `92790d2a`, and
  the exact Release candidate.
- 2026-09-02 (resumed session): **Edge historical failure mechanism
  reconstructed and terminally classified as
  `PROVEN_EXTERNAL_BROWSER_INPUT_FLAKE`.** Historical run `ba1cedc3`
  (candidate `ab8853f1`): cycle 1 fully contained PASS; cycle 2 F11
  transition observed (style `0x1ECF→0x1E0B`, outer `(0,0)-(1920,1200)`),
  TabDock posted its one-shot identity-checked browser F11 exit
  (`SHEPHERD[presentation-restore-request] method=browser-f11` twice — verified
  in the TabDock log at 01:17:11.327 and 01:17:12.521), Edge remained
  borderless-fullscreen (`observed=0,0,1920x1200` through every poll), and
  `SHEPHERD[size-constraint]` correctly refused to force-resize a fullscreen
  browser; the browser window was then destroyed at 01:17:16.333, ending the
  run `FAIL_PRODUCT` on the contain assertion. Positive evidence the product
  contract was correct: (a) the very same mechanism (one posted `WM_KEYDOWN/
  UP VK_F11` to the guest HWND after strong identity + mutation-generation
  gates, coalesced by `_pendingBrowserFullscreenExits`) succeeded 15/15 in
  the bounded characterization and holds deterministic policy coverage in
  `NativePresentationRestorePolicyTests`; (b) the historical mechanism is NOT
  a retry — no second F11 was issued after the request fired, so no
  output-suppression or timing change occurred; (c) the failure is a
  browser-side transition loss (Edge did not consume/act on the posted F11
  before the 3500 ms `IsDocked` settle window), not a TabDock containment or
  observation defect. **No production TabDock change was made; no timeout/
  retry/assertion change was introduced.**
- 2026-09-02 (resumed session): **real Chromium visual packets produced at
  final candidate `fbc4d92`** — one accepted `browser-fullscreen-contained`
  attempt each for Chrome (run `6a5bb064`, 2 cycles, native PASS), Edge (run
  `76fb0a68`, 2 cycles, native PASS), Brave (run `b151a8ce`, 2 cycles, native
  PASS), all with `--visual-evidence checkpoints --visual-review-packet`.
  Per packet: 14 raw PNG checkpoints (baseline / before / fullscreen / after
  per cycle, guest+container), visual manifest, contact sheet, review packet,
  topology binding `snapshotId 92790d2a`, `syntheticTopology=false`,
  privacy `TEST_OWNED`, `AllowVirtualDesktop=false`. Packet SHA-256:
  Chrome `c03de02255c8a36c948dc71cbbeba31a1837ee88377c2658f30cd379e9f2dcfe`,
  Edge `1a47df5abb05cbe50ce27ff941338e56de50d7af36d14cea499be2d8d844a019`,
  Brave `75c6c51c6f547f636721ec3ad6a488b4d8929af35bc6fbf48376af185905c652`.
- 2026-09-02 (resumed session): **hash-bound multimodal review completed** by
  the operator (capable human vision reviewer) per
  `.agent/workflows/visual-evidence-review.md`, with all 14 raw checkpoints +
  contact sheet inspected per browser. Verdicts: Chrome `VISUAL_SUSPECT` (2
  informational `CAPTURE_ARTIFACT` findings on the container-window frames at
  the fullscreen checkpoints — blank white host region + dark surround from
  the 1920x1200 borderless guest covering the 1225x800 container; guest
  fullscreen frame correct, native PASS authoritative), Edge `VISUAL_SUSPECT`
  (same benign artifact; **no account email / sync prompt present — privacy
  fix confirmed**), Brave `VISUAL_OK`. Offline verifier
  `VisualReviewVerifier.VerifyFiles` returns **`Valid:true` for all three**
  packet+result pairs, with exact packet SHA bindings.
- 2026-09-02 (resumed session): **real-packet tamper check passed** — Edge
  packet copied to a temporary root, exactly one byte of
  `edge-normal-f11-1-fullscreen/0005-GUEST_WINDOW.png` flipped (original sha
  `37201d69…`, tampered `07fa1ab8…`), verifier deterministically rejected
  with `visual evidence hash mismatch for
  'browser-fullscreen-contained-attempt-001-edge-normal-f11-1-fullscreen-0005'`
  (manifest + packet + review bindings). Authoritative packet unchanged;
  tampered copy deleted.