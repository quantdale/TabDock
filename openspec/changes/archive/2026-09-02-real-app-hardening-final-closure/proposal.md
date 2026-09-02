# Proposal — real-app-hardening-final-closure

## Why

The previous `real-app-hardening` campaign was archived prematurely. Its
checklist was marked complete, but an independent review of the archived
evidence found four specific closure defects:

1. **Edge first valid failure is unexplained.** The first invocation of Edge
   `browser-fullscreen-contained` produced `FAIL_PRODUCT` on its second F11
   cycle. A later invocation passed. The archive recorded
   `FLAKE_UNCLASSIFIED` — a valid first failure retained but never explained.
   The first valid `FAIL_PRODUCT` remains authoritative until properly
   dispositioned.
2. **No real real-app browser visual packets exist.** Tasks 1.7 and 4.3 were
   checked complete even though no actual restricted browser visual packet
   was produced and no capable multimodal review inspected real Chromium
   imagery. The Chrome checkpoint run produced no visual logs because the
   monitor/topology binding path did not yield a usable binding.
3. **Final gates were not actually executed.** Task 7.1 was checked complete
   even though the canonical
   `scripts/validate.ps1 -Configuration Release -Ci -Publish`, the explicit
   native ABI gate, and the resource/privacy/recovery qualification were not
   all actually executed in the final session.
4. **Ledger count is wrong.** The final report said "26 real-app tasks" while
   the canonical archived `tasks.md` contains **38 checkbox rows**.

## What Changes

This is a correction/closure change. It explicitly carries forward the four
unfulfilled acceptance obligations from the prematurely archived real-app
campaign:

- Characterize Edge with a bounded 5-invocation × 3-cycle (15 F11 cycles)
  matrix against an exact committed candidate with fresh isolated profiles,
  and end with a defensible classification
  (`PROVEN_PRODUCT_DEFECT` → smallest Shepherd-preserving repair and
  requalification, `PROVEN_HARNESS_DEFECT` → harness fix + reclassify with
  proof, `PROVEN_ENVIRONMENT_FAILURE`, `CHARACTERIZED_PRODUCT_FLAKE` —
  unresolved, no archive, or `NOT_REPRODUCED_BUT_UNEXPLAINED` — no closure).
- Produce actual restricted visual packets for every installed Chromium
  browser (Chrome, Edge, Brave): raw PNG checkpoints, manifest, packet,
  SHA-256, verifier `Valid:true`, `TEST_OWNED`/`REAL_APP_RESTRICTED`
  privacy, no whole-desktop capture, no personal content.
- Perform an actual capable multimodal review of each accepted Chromium
  packet per `.agent/workflows/visual-evidence-review.md`, and exercise a
  tamper rejection on at least one real packet.
- Actually execute the canonical final gates: `validate.ps1
  -Configuration Release -Ci -Publish`, `TabDock.exe
  --selftest-native-abi`, the deterministic resource-headless gate, and the
  privacy/recovery/support/doctor gates — recording exact results.
- Reconcile the ledger: 38 historical checkbox rows (not 26), with a
  corrective mapping for tasks 1.7, 4.3, 7.1 and a new corrective obligation
  `EDGE_FIRST_VALID_FAIL_PRODUCT_DISPOSITION`.

## Non-Goals

- No new hardening beyond the four carried-forward obligations.
- **No product repair is authorized merely to obtain closure.** Production
  TabDock source changes only if a valid product defect is proven (Edge
  `PROVEN_PRODUCT_DEFECT` or another valid product defect with frozen first
  evidence).
- No rerunning of already-valid evidence: DPI/topology (archived, accepted),
  Chrome/Brave native F11 PASS, Notepad `ACCEPTED_BLOCKED_ENVIRONMENT`,
  Terminal `ACCEPTED_BLOCKED_CAPABILITY`, Firefox `SKIP_CAPABILITY`.
- No rewriting of history: the premature archive remains as archived; this
  change becomes the authority for the outstanding closure requirements only.
- No timeout-only, retry-only, second-F11, or weakened-assertion "fixes".
- No loosening of the inherited 19.1 acceptance boundary to allow closure.