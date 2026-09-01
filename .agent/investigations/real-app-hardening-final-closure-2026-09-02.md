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
| 1.7 — Retain restricted before/fullscreen/after visual packets per browser and run verifier `Valid:true` | [x] | Checked complete without actual restricted real-browser visual packets; Chrome checkpoint run produced no visual logs (monitor/topology binding unusable) | REOPENED_FOR_CORRECTIVE_CLOSURE | TBD — actual Chrome/Edge/Brave packets, verifier `Valid:true` (see 2.x) |
| 4.3 — Review retained real-app visuals via `.agent/workflows/visual-evidence-review.md` with a capable multimodal agent | [x] | Checked complete without any actual multimodal review of real browser imagery | REOPENED_FOR_CORRECTIVE_CLOSURE | TBD — actual hash-bound multimodal review (see 3.x) |
| 7.1 — Run after implementation settles: `dotnet build -c Debug/Release`, `dotnet test -c Debug/Release`, build ValidationDriver/GuineaPig Release, `selftest all`, `capability`, `visual`, `catalog`, `plan`/`plan real-app`, deterministic topology/visual/resource/privacy, native ABI, release-tooling, historical bundle compatibility, strict OpenSpec, and `scripts/validate.ps1 -Ci -Publish` — record current counts dynamically | [x] | Checked complete although canonical `validate.ps1 -Configuration Release -Ci -Publish`, the explicit native ABI gate, and resource/privacy/recovery qualification were not all actually executed in the final session | REOPENED_FOR_CORRECTIVE_CLOSURE | TBD — actual canonical gates (see 4.x) |
| EDGE_FIRST_VALID_FAIL_PRODUCT_DISPOSITION | — (new corrective obligation) | Edge first invocation `FAIL_PRODUCT` on second F11 cycle (run `ba1cedc3`), rerun PASS (run `35525ad6`); current disposition `FLAKE_UNCLASSIFIED` with no explanation | (new obligation) | TBD — 5×3 characterization + classification (see 1.x) |

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