# Next campaign — ultrathink exploration

**Date:** 2026-09-02
**Mode:** explore (no implementation — thinking only)
**Source campaign:** `2026-08-31-visual-evidence-ai-review` — 84/89 done, 5 blocked awaiting supervised desktop + archival acceptance. Deterministic evidence: `776/776` unit tests (44 visual), Debug/Release builds 0 warnings (driver), historical manifests valid, `scripts/qualification-bundle.ps1` now indexes visual artifacts offline.
**Verified head:** `fdb782e107d0bbf632a88936a876c7f5db35cb4c` == `origin/main`, worktree dirty only with visual-evidence untracked+staged files (expected pre-acceptance).

## 1. Where we stand

```
 ┌──────────────────────────────────────────────────────────────────────┐
 │                    TabDock — current capability map                  │
 │                                                                      │
 │  Production (Shepherd / no-reparent)  ───  BuildIdentity/Doctor      │
 │          │                                      │                    │
 │          ▼                                      ▼                    │
 │   WindowShepherd ──► NativeMethods        DiagnosticTrace (1024)     │
 │          │              │                        │                   │
 │   GroupManager ──► WinEventMonitor ──► GuestLifecycle               │
 │          │                                                        │
 │   Persistence ────────────────────────────────────────────────┐     │
 │                                                              │     │
 │  ValidationDriver (real SendInput, UIA, lease, Pixels)       │     │
 │    ├─ ScenarioCatalog 135 dispatchable                       │     │
 │    ├─ Ctx + Timeline + DesktopQualificationLease             │     │
 │    ├─ Pixels (BitBlt / PrintWindow PW_RENDERFULLCONTENT)     │     │
 │    ├─ ★ NEW VisualEvidenceRecorder / Ring / Manifest / Bundle│     │
 │    │     ├─ PNG atomic + hash + path policy                 │     │
 │    │     ├─ ContactSheet (derived, labeled)                 │     │
 │    │     ├─ ReviewPacket (hash-bound) + Verifier            │     │
 │    │     └─ qualification-bundle.ps1 offline visual check   │     │
 │    ├─ Resource Lifecycle probe (synthetic, headless)         │     │
 │    └─ run-manifest.json schema 2 ──▶ qualification-bundle   │     │
 └──────────────────────────────────────────────────────────────────────┘
```

**What the visual campaign actually closed:** pure ValidationDriver pipeline with injectable `IVisualCaptureProvider`, no prod change, privacy classes (`TEST_OWNED/PRODUCT_OWNED/REAL_APP_RESTRICTED/DESKTOP_RESTRICTED`), `MaxBytes/MaxArtifacts/MaxWidth/Height` budgets, `VISUAL_*` vocabulary isolated from native `PASS/FAIL_*`, contact sheet is derived convenience.

**What remains blocked (not failed):**
- 4 supervised validations: healthy/defective packet multimodal review (non-vacuous), transient flight-evidence review, capable vs non-vision `REVIEW_UNAVAILABLE` path, hash-tamper + native-precedence on a physical run.
- 1 archival gate: `openspec archive` only after operator acceptance + push.

**Health signals from repo:**
- 776/776 unit tests green, but visual model recently made collections nullable (`VisualReviewPacket.Checkpoints?` etc.) to survive JSON round-trip — hides the real contract (required fields should be non-null via required ctor + custom converter, not nullable). Leaves warnings `CS8602` in verifier/packets.
- `VisualEvidenceRecorder.TryBuildContactSheet` records `BEST_EFFORT/SUSPICIOUS` unavailable on byte-count budget exhaustion but manifest still counts it as unavailable — correct, but plan task 10.6 expects explicit derived-failure reporting in scenario result (currently only in `Unavailable[]`, not surfaced in JUnit `visualEvidence`).
- `qualification-bundle.ps1` now has `Test-QualificationVisualManifest` but `Get-QualificationManifestSummary` does not yet surface visual counts to `aggregateCounts` — parent `all` cannot yet fail a gate that declares visual review required.
- Physical matrix last proven at `914a259` (732 tests at that time) — visual branch adds 44 tests, re-baselining needed before next physical `all`.

## 2. Problem space — where could the *next* campaign go?

Five naturally emerging directions, each grounded in a gap the visual work exposed:

```
  Need                         Why now                         Cost
 ─────────────────────────────────────────────────────────────────────────
  A. Close visual evidence     Blocked tasks are pure          Low — 1 supervised session + archive + spec sync
     (supervised + archive)    supervised; no new prod code    Risk: desktop lease flakes, model availability

  B. Visual overhead →         New PNG/contact/packet code     Medium — measure, set budgets, prove disabled=0
     resource re-baseline      adds encode/IO/CPU; resource    overhead, re-run qualification

  C. DPI / topology            Visual clipping tests hit        Medium-High — needs mixed-DPI hardware,
     hardening                 negative coords, above-origin,   125%→100% + topmost + monitor transfer
                              120 vs 96 DPI; verified env     + deterministic Lab already exists
                              only 125%+100%, no negative

  D. Real-app hardening        Real-app capture is policy-     High — privacy, browser fullscreen,
     (browser/Notepad)        gated `REAL_APP_RESTRICTED`;    Notepad broker surface, Wt,
                              chrome-normal is only real-app  foreground-lease nuance
                              scenario exercised

  E. Release v1.1 closure      Need to re-tie artifact SHA     Low-Medium — mostly control-plane
     (v1.0 was 914a259)       to new visual build, publish    + docs, no prod change needed
                              smoke, qualification bundle v1
 ─────────────────────────────────────────────────────────────────────────
```

### How they relate
```
  A ──► B ──► E
  │     │
  │     └─► C ──► D
  │
  └─► (A must precede any archive that claims visual qualification;
       B is prerequisite if C/D will add more capture;
       E is final)
```

## 3. Deep dives on the interesting threads

### Thread A — why "just archive" is not trivial

Visual evidence correct but leaves small verification gaps:
- `VisualReviewPacketBuilder` builds `facts` as `SortedDictionary` then round-trips via `Dictionary` — loses sort determinism in JSON (harmless but `VisualReviewVerifier` does not check key order).
- `VisualEvidenceManifest.ReviewPacketSha256` currently mismatched check in verifier (`packetSha256: string.Empty` literal — copy-paste bug at `VisualReviewVerifier:205`). Deterministic tests pass because that branch is only hit when `ReviewPacketPath != null` with wrong expected — needs fix before archival audit.
- Dirty worktree has `??` files (new visual types) + `M` files — `openspec archive` will reject dirty visual change unless committed. Need a clean `feat(visual-evidence): ...` commit with only visual files, not `.visual-validation-runs/`.

*Open questions:* Who is the accepting operator? Which commit SHA will be the candidate for the supervised run? Will the supervised run use `--yes all --visual checkpoints --visual-review-packet` or per-scenario?

### Thread B — resource/performance re-baseline

Visual path now does: `Capture (15-46ms)` → `Encode (PNG, ~ms)` → `WriteImmutable (tmp+move+hash)` → `ContactSheet (decode all raws + thumbnail + encode)`. On healthy 3-cycle `maximize-repro`, 1 scenario will retain 2-3 raws + 1 contact + 1 manifest + 1 packet + 1 instructions ≈ 6 files. Under `flight` (2 fps × 6s = 12 frames max, 8 MiB ring), worst retained ≈ 13 PNGs.

Risk: `headless` resource profiles currently run with `Enabled=false` (correct — no capture) but have no proof `disabled` path has zero overhead. Plan task 21.4 asks for conservative budgets from *measured* runs, not guesses — we guessed `16 MiB/64 artifacts`. Need measurement: run `--yes deterministic` + `--yes resource-stability` with visual `NONE` vs `CHECKPOINTS` and assert `BytesRetained` delta.

**Comparison — where to measure:**

| Scenario | Visual `NONE` | `CHECKPOINTS` | `FLIGHT` | Expected delta |
|----------|---------------|---------------|----------|----------------|
| deterministic suite | 0 bytes, 0 encode ms | 0 (no UI) | 0 | 0 |
| GuineaPig split | 0 | ~2 raws × 1225×700×4 → PNG ~tens KiB | + ring up to 12 | < 2 MiB |
| browser chrome-normal | 0 | 1 raw | + ring | < 1 MiB |

If delta > budget, either lower `RingMaxFrames` or make contact sheet opt-in.

### Thread C — DPI/topology is the biggest latent risk

Verified env today: primary 125% (120 DPI), secondary 100% (96 DPI), **no negative coords**, **no above-origin**. Visual clipping tests prove `RequestedRect` vs `ActualRect` + monitor/DPI are recorded, but no scenario exercises:
- primary on right (negative X secondary)
- 150% / 175% / 200% mixed
- `TopMost` band interaction with split (policy exists but only synthetic)
- Monitor transfer while TabDock on 125% → guest on 100%

`VirtualTopologyLab` already has deterministic topology matrix for this — it just needs a visual-aware extension in a follow-up campaign, not in the closing visual change.

```
  Monitor A (125%, 1920×1200)          Monitor B (100%, 3840×1080)
 ┌─────────────────────┐            ┌─────────────────────┐
 │ Container(160,260)  │            │ Guest alone?        │
 │ ┌─────────────────┐ │  ──drag──▶│ moved by guest    │
 │ │ Guest clipped?   │ │            │ DPI re-layout?    │
 │ └─────────────────┘ │            └─────────────────────┘
         ▲ DPI 120 → 96 translation
         │ Visual ActualRect must track physical coords
```

*Unknowns:* Does `MonitorDpiService` correctly probe per-monitor effective DPI when container and guest are on different DPIs during split? Does `TargetWithContext` margin correctly clip to work area on negative-coord monitor?

### Thread D — real-app hardening

`VisualPrivacyClass` and `AllowVirtualDesktop=false` by default are correct. But:
- `REAL_APP_RESTRICTED` capture currently uses same `HostClient` path as `TEST_OWNED` — no cropping to necessary region.
- `Scenarios.Browser.cs` `chrome-normal` is the only real-app scenario with deterministic visual path (PrintWindow) — no `MAXIMIZE`, no `TopMost`, no Notepad broker handle edge.
- Bundle privacy gate (`Test-QualificationPrivacyObject`) rejects `C:\Users\...` but visual packet `CorrelatedFacts` now includes `timelineArtifact` filename — safe, but packet instructions could leak `candidateSha` + environment fingerprints to an external model without explicit operator consent (spec says no auto-upload, but no code enforces it — correctly, since model is external).

### Thread E — release closure

Last authoritative Release artifact was `win-x64` at `914a259` `E3C830...BBF06F`. Visual branch will change artifact SHA and add new `qualification-bundle` artifact kind `visual-image` etc. Need to prove `verify-qualification-bundle.ps1` still accepts historical bundles (it does — visual check early-returns) and then cut new `v1.1` candidate with `publish` smoke.

## 4. Options — what *could* a next OpenSpec change be?

| Option | Scope | Tasks | Pro | Con | Verdict |
|--------|-------|-------|-----|-----|---------|
| **1 — Minimal closure** (`visual-evidence-closure`) | Only A+E: supervised runs + archive + spec sync + SHA re-baseline | ~12 | Fastest to green `main`; respects visual as infrastructure | Leaves B/C/D debt; still needs next campaign soon |
| **2 — Visual + perf baseline** (`visual-evidence-closure-and-performance-requalification`) | A+B+E | ~22 | Sets real budgets from measurement, proves disabled=0 | Adds 1-2 days of controlled profiling |
| **3 — Visual + DPI hardening** (`visual-evidence-and-topology-hardening`) | A+C (+ maybe B) | ~30 | Highest risk reduction — DPI is top field risk | Needs physical mixed-DPI matrix (the 9278 build has it, but need above-origin rig) |
| **4 — Visual + real-app** (`visual-evidence-and-real-app-hardening`) | A+D | ~28 | User-visible value (browser/Notepad are top user guests) | Privacy review overhead; biggest scope creep |
| **5 — Grand unification** (1+2+3+4) | A+B+C+D+E | ~60 | One big bang | Violates OpenSpec "one capability per change" and 178-task precedent; review nightmare |

**Lean recommendation:** **Option 2** as the *next* campaign, with **Option 1 as its first milestone**. Rationale:
- Visual evidence is valueless if its overhead silently breaks resource stability or if `disabled` still captures. B is cheap, deterministic, and required before any visual qualification gate can be trusted.
- C is important but deserves its *own* campaign after B establishes budgets — otherwise DPI runs will be blamed for visual overhead.
- D should follow C, because real-app geometry bugs are DPI/topology bugs in disguise.

## 5. Risks that apply to *any* next campaign

- **Verification drift:** `VisualReviewVerifier` literal bug (`packetSha256: string.Empty`) and nullable-collection warnings indicate the verifier is not yet strict enough for a release gate. Archival without fixing it would cement a weak gate.
- **Dirty tree:** `.visual-validation-runs/` and `??` visual files are intentionally gitignored? Check `.gitignore` — they are not ignored, they are just untracked. Archive step `doctor` will complain about untracked visual `??` files if they are not committed.
- **Name legality:** `openspec` rejects `2026-08-31-*` (must start with letter). The active change *is* in-progress under that name but was created outside the CLI. Next change should be letter-leading: `visual-evidence-closure-and-performance-requalification` or similar.
- **No third-party imaging dependency** — watch that `VisualContactSheets` hand-rolled font + nearest-neighbor thumbnail is intentionally dependency-free; a follow-up that adds SkiaSharp/System.Drawing would break determinism/repro.

## 6. Concrete next steps (if you want to proceed)

1. **Fix verifier bug** (`packetSha256` literal) + make `VisualEvidenceModel` collections `required`/non-nullable with a custom `JsonConverter` for strict non-null, remove `CS8602` warnings — 1 task, pre-closure.
2. **Create change** (when ready): 
   ```bash
   openspec new change visual-evidence-closure-and-performance-requalification --type spec-driven
   ```
   Then `status --change <name>`, seed `proposal.md` with Option 2 scope, `design.md` with measurement protocol, `specs/validation-qualification/spec.md` with `disabled overhead = 0` requirement, `tasks.md` with ~22 tasks (measure → budgets → proof → bundle → deterministic CI → supervised `all` with visual enabled → archive).
3. **Or close current first** (fastest): commit visual files as `feat(visual): bounded evidence and review harness`, run supervised `all` with `--visual checkpoints --visual-review-packet` on a single clean desktop, have a vision-capable agent run `.agent/workflows/visual-evidence-review.md` on one healthy + one defective synthetic packet (deterministic already), prove `Test-QualificationVisualManifest` fail-closed on tampered byte, then `openspec archive 2026-08-31-visual-evidence-ai-review --yes` + spec sync.
4. **Do not** in the next campaign: change Shepherd z-order logic, add model SDKs, enable unrestricted desktop capture, or re-tag physical artifacts from `914a259`.

## 7. What would make this stronger (spikes)

- **Spike — measure now:** Run `./scripts/validate.ps1 -Configuration Release -Ci` with and without visual, dump `VisualEvidenceCounterSnapshot.EncodeMilliseconds/BytesRetained` into the investigation.
- **Spike — topology matrix:** Run `VirtualTopologyLab` with `syntheticTopology` + visual packet on one fake negative-coord topology (deterministic, no hardware) to prove path policy holds.
- **Spike — privacy gate on packet upload:** Prove that `visual-review-result.json` write path is local-only and that no code path calls an HTTP client — grep for `HttpClient` in `ValidationDriver`.

## References

- `openspec/changes/2026-08-31-visual-evidence-ai-review/{proposal,design,tasks}.md`
- `openspec/specs/validation-qualification/spec.md`, `presentation-integrity/spec.md`
- `docs/ARCHITECTURE.md`, `docs/TESTING.md`
- `.agent/STATE.md`, `.agent/investigations/visual-evidence-implementation-2026-09-01.md`
- `tests/ValidationDriver/.../{VisualEvidenceModel,VisualPngArtifacts,VisualEvidenceRecorder,VisualRingBuffer,VisualContactSheets,VisualReviewPackets,VisualReviewVerifier}.cs`
- `scripts/qualification-bundle.ps1` + `Test-QualificationVisualManifest`
- `tests/UnitTests` 776/776, `TabDock.sln` Release builds

---
*Next action if you green-light:* say which option (1 Minimal, 2 Perf-baseline, 3 DPI, 4 Real-app) you want as the *next* change, or "close current first" — I will generate the `openspec new change` proposal + tasks skeleton for that option (no prod code).
