# Next campaign — ultrathink exploration

**Date:** 2026-09-02
**Mode:** explore (no implementation — thinking only)
**Source campaign:** `2026-08-31-visual-evidence-ai-review` — canonical
ledger reconciled at **93/178 checked**, with 85 rows explicitly classified
and unchecked. Current deterministic evidence: 44/44 visual-filter tests,
successful Debug/Release solution builds, and historical manifests valid;
the current visual implementation and generated run roots remain dirty.
The row-level reconciliation is
`.agent/investigations/visual-evidence-ledger-reconciliation-2026-09-01.md`.
**Verified head at reconciliation:** `68e456ff8500750c32474927ce724292a54f1245`
== `origin/main`, branch `main`, with seven modified files and 24 untracked
paths before subsequent planning edits.

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


**What the visual campaign actually closed:** pure ValidationDriver pipeline
with injectable `IVisualCaptureProvider`, no production change, privacy classes
(`TEST_OWNED/PRODUCT_OWNED/REAL_APP_RESTRICTED/DESKTOP_RESTRICTED`),
`MaxBytes/MaxArtifacts/MaxWidth/Height` budgets, `VISUAL_*` vocabulary isolated
from native `PASS/FAIL_*`, contact sheet as derived convenience, and direct
deterministic tests for the implemented pieces. This is not a claim that all
178 predecessor rows are complete.

**What remains open or blocked:** the exact row-level remainder is 35
implemented-but-not-accepted, 38 not implemented, 3 superseded, 7 supervised
blocked, and 2 capability blocked. It includes contract/verifier hardening,
scenario integration, privacy/tamper/docs work, supervised packets and review,
and archive/provenance gates. The 84/89 wording was an implementation-only
roll-up with no mapping to the canonical rows and must not be reused.

**Health signals from repo:**
- 44/44 visual tests pass, but visual model collections and verifier call paths
  still require strict null/malformed-input closure; Release visual compilation
  has nullable warnings.
- `VisualEvidenceRecorder.TryBuildContactSheet` records a generic unavailable
  entry; successor A requires an authoritative derived-failure record surfaced
  through manifest, scenario result, packet eligibility, and offline verification.
- `qualification-bundle.ps1` indexes visual manifest/image data, but successor A
  still needs review packet/instructions linkage and aggregate visual outcome
  gating.
- Historical `.visual-validation-runs` roots contain native manifests/results
  only; no current exact-candidate visual acceptance packet is present.

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

Visual evidence currently has two concrete contract risks to close before
archive:
- The investigation originally reported a `packetSha256: string.Empty` literal,
  but that literal is not present in the current `VisualReviewVerifier.cs`;
  current code accepts a caller-supplied packet hash. Successor A must trace the
  current data flow and remove any caller-substitution opportunity, rather than
  applying a stale text fix.
- Required collections are nullable in the current model/JSON path and emit
  nullable warnings. A strict current-schema constructor/converter/validation
  path must reject missing or null collections while preserving explicitly
  supported historical non-visual bundles.
- `.visual-validation-runs/` is not currently ignored; generated roots must
  remain on disk only as run-owned evidence and be excluded from Git.

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

- **Verification drift:** the historical `packetSha256: string.Empty` report is
  absent from current source. The current writer/packet builder emits a packet
  path and the verifier now hashes the exact packet bytes on disk; no public
  verifier path accepts a caller-supplied packet hash. `VisualJson` rejects
  missing/null current collections, and manifest/packet/result derived-failure
  bindings are checked by both the offline verifier and bundle validator.
- **Literal classification:** `packetSha256: string.Empty` is not present in the
  current writer, serializer, verifier, or fixture sources; the report was
  stale/test-only evidence. The live weakness was the verifier trusting a
  caller-provided hash, which could weaken packet binding; the API now removes
  that substitute.
- **Dirty tree:** `.visual-validation-runs/` and visual review JSON/instruction
  outputs now have narrow ignore rules. Intended visual source/tests/planning
  records remain modified or untracked; closure still requires an explicit
  allowlist audit without staging generated evidence.
- **Name legality:** `openspec` rejects `2026-08-31-*` (must start with letter).
  The active predecessor remains in progress under that name; the successor is
  the letter-leading `visual-evidence-closure-and-performance-requalification`.
- **No third-party imaging dependency** — the hand-rolled contact sheet remains
  dependency-free and deterministic; adding SkiaSharp/System.Drawing would
  change the reproducibility boundary.

## 5a. Milestone A packet-binding trace

The required `packetSha256` trace is now source-verified:

1. `VisualReviewPacketBuilder` creates the packet and required result path;
   `VisualEvidenceRecorder.WriteReviewPacket` writes immutable packet bytes and
   instructions; `QualificationResultWriter` links packet, instructions,
   result path, and derived-failure IDs in scenario/run manifests and the
   artifact index.
2. `VisualJson.Options` serializes current packet, manifest, and result
   schemas; `StrictCollectionConverter<T>` rejects missing or JSON-null
   required arrays during deserialization.
3. `VisualReviewVerifier.VerifyFiles` reads exact packet bytes and
   `VerifyLoaded` computes their SHA-256 before comparing result, manifest,
   candidate, run, scenario, attempt, image, and derived-failure bindings.
4. `scripts/qualification-bundle.ps1` re-hashes packet/result/manifest/raw
   files, validates strict arrays and packet/result identity, checks scenario
   hierarchy links, and fails closed for missing/non-pass review results.
5. Unit fixtures cover empty and changed packet hashes, missing/null
   collections, stale identity, tampered derived records, intact raw PNG after
   contact failure, and unacknowledged derived failure. The current visual
   filter is 49/49 passing.

## 6. Concrete next steps

1. The canonical predecessor ledger is now reconciled at 93/178 checked with
   85 explicit remainder classifications; preserve the matrix and state
   updates as provenance.
2. Proceed with successor Milestone A: trace/fix packet hash binding, make
   required collections strict, represent derived contact-sheet failures,
   close deterministic verifier/index/tamper gaps, then attempt the supervised
   healthy/defect/flight evidence boundary.
3. Proceed to B only after A's deterministic work and any available supervised
   evidence are recorded: measure none/checkpoints/flight modes, derive budgets
   from repeated samples, and rerun resource-lifecycle qualification.
4. Proceed to E only after A and B acceptance: qualify the exact clean committed
   candidate with fresh executable identity and SHA-256. Repository policy
   still forbids an unrequested commit/push; archive and release remain blocked
   until their explicit authority and evidence boundaries are met.
5. Do not change Shepherd z-order logic, add model SDKs, enable unrestricted
   desktop capture, or retag physical artifacts from `914a259`.

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
- `tests/UnitTests`: historical 776/776 baseline; current visual filter 49/49;
  `scripts/release-tooling-tests.ps1` 177/177 on PowerShell 7; Debug/Release
  solution builds

---
*This investigation is superseded operationally by the successor OpenSpec
change and the reconciled ledger; keep it as the decision record.*
