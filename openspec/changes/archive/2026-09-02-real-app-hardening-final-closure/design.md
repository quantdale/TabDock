# Design — real-app-hardening-final-closure

## Context

The archived real-app campaign (38 checkbox rows in `tasks.md`, archived
2026-09-01) produced valid native F11 evidence for Chrome and Brave, an
unexplained first-valid Edge `FAIL_PRODUCT`, explicit Notepad/Terminal
capability blocks, and a privacy policy (`REAL_APP_RESTRICTED`,
`AllowVirtualDesktop=false`) — but closed gaps 1.7, 4.3, and 7.1 without
actual real-browser visual packets, multimodal review, or the canonical final
gates, and reported 26 tasks instead of 38.

This corrective campaign inherits the same Shepherd/no-reparent, identity,
lease, first-failure-authority, and visual-privacy contracts. It performs only
the missing evidence work and the ledger correction.

## Edge characterization protocol (bounded 5×3)

Five independent invocations, each with a fresh isolated Edge user-data-dir,
run against the exact committed Release candidate with the exact Release
ValidationDriver under an exclusive supervised desktop, valid lease, and
topology snapshot. Each invocation performs 3 F11 enter/exit cycles
(15 total bounded cycles — not a soak).

Per cycle, record: run ID, invocation index, cycle index, Edge PID/process
start, HWND, process generation, assigned pane rect, observed rect before F11,
observed fullscreen rect, observed rect after exit, WS_CAPTION/style before/
during/after, IsZoomed, monitor, DPI, `LOCATIONCHANGE`/drift-reconcile event
count, `SHEPHERD[presentation-restore-request]` count, time from F11 exit
request to containment settle, `IsDocked` result, point ownership, foreground
state, tab membership, visual packet/result where enabled, cleanup result.

Every first valid failure is preserved. Classification outcomes:

- `PROVEN_PRODUCT_DEFECT` → freeze first failure, identify first divergence,
  update spec if underspecified, add non-vacuous regression, smallest
  Shepherd-preserving repair, rerun full Edge 5×3, requalify Chrome + Brave,
  retain original `FAIL_PRODUCT` as pre-fix evidence.
- `PROVEN_HARNESS_DEFECT` → fix harness only, add regression, keep raw
  original result, reclassify through a durable investigation proving the
  label was erroneous (e.g., geometry/identity/ownership already proved
  containment before IsDocked was sampled on a stale observation).
- `PROVEN_ENVIRONMENT_FAILURE` → concrete external-environment proof.
- `CHARACTERIZED_PRODUCT_FLAKE` → unresolved product issue; **DO NOT ARCHIVE**.
- `NOT_REPRODUCED_BUT_UNEXPLAINED` → 15 later PASS cycles do not erase the
  valid historical failure; **cannot final-close**.

## Chromium visual evidence protocol

For each installed Chromium browser (Chrome, Edge, Brave) run at least one
accepted exact-candidate physical F11 attempt with
`--visual-evidence checkpoints --visual-review-packet`. Required checkpoints:
baseline captured state, immediately before F11, fullscreen state,
post-containment/restored state (a fifth settled checkpoint where helpful).
Only controlled isolated test content; browser profile is test-owned
(`TEST_OWNED`), no personal history or logged-in accounts. Capture the
smallest approved region; `AllowVirtualDesktop` stays false.

Before rerunning, the prior zero-visual-log root cause must be classified
(target topology binding vs monitor identity vs privacy-scope policy vs
target identity vs scenario ordering vs visual recorder state vs capability
planning vs stale candidate/run/attempt context vs actual capture failure).
If it is a harness defect, fix the visual harness only and add a regression
proving a valid controlled browser F11 checkpoint attempt produces the
required selected visual artifacts. Production TabDock is not modified for a
visual-harness problem.

Packet acceptance requires: raw PNGs exist, visual manifest exists, packet
exists, packet SHA-256 computed from exact packet bytes, selected image
hashes verify, candidate SHA matches, run/scenario/attempt matches,
topology/monitor binding matches, privacy class matches, verifier
`Valid:true`. No "future packet" wording.

## Multimodal review

Per `.agent/workflows/visual-evidence-review.md`, a capable multimodal
reviewer inspects each accepted Chromium packet (contact sheet, every required
raw checkpoint, expected visual state, correlated native evidence) and writes
a hash-bound result. Verdicts: `VISUAL_OK` / `VISUAL_SUSPECT` / `VISUAL_DEFECT`
/ `REVIEW_UNAVAILABLE`. Required browser visual acceptance cannot close with
`REVIEW_UNAVAILABLE` unless this proposal explicitly permits it without
contradicting the inherited 19.1 boundary. `VISUAL_OK` cannot override a
native failure.

Tamper check: copy at least one accepted packet to a temporary validation
root, alter one PNG byte, run the offline verifier, require deterministic
rejection, record the exact rejection reason. The authoritative packet is
never modified.

## Canonical final gates

- `scripts/validate.ps1 -Configuration Release -Ci -Publish` (exact canonical
  invocation; no substitutes). If PowerShell policy requires:
  `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate.ps1
  -Configuration Release -Ci -Publish`. The script is CI-safe; if it blocks on
  stdin, investigate before marking complete.
- Explicit native ABI: `TabDock.exe --selftest-native-abi` — PASS required.
- Deterministic resource gate: `--resource-headless --configuration Release
  --rid none --cycles 32 --profile all --seed 20260824` (all declared
  profiles, canonical seed).
- Privacy/recovery/support: support-bundle privacy, recovery,
  pending-recovery, doctor/version, visual privacy, historical bundle
  compatibility — actually executed, not inferred from unit tests.

## Ledger reconciliation

Record in `.agent/investigations/real-app-hardening-final-closure-2026-09-02.md`
that the archived real-app ledger contains **38 checkbox rows** (0.1–0.4,
1.1–1.8, 2.1–2.4, 3.1–3.5, 4.1–4.4, 5.1–5.4, 6.1–6.4, 7.1–7.5) and that the
earlier "26 tasks" report was incorrect. Do not delete or renumber archived
historical tasks. Provide the corrective mapping table:

| Prior archived task | Historical check state | Review finding | Corrective task | Final evidence |
|---|---|---|---|---|
| 1.7 | [x] | No actual restricted browser visual packet | REOPENED_FOR_CORRECTIVE_CLOSURE | … |
| 4.3 | [x] | No actual multimodal review | REOPENED_FOR_CORRECTIVE_CLOSURE | … |
| 7.1 | [x] | Canonical final gates not actually executed | REOPENED_FOR_CORRECTIVE_CLOSURE | … |
| EDGE_FIRST_VALID_FAIL_PRODUCT_DISPOSITION | — | FLAKE_UNCLASSIFIED, unexplained | (new obligation) | … |

If satisfied: `SATISFIED_POST_ARCHIVE_BY_FINAL_CLOSURE` — never "satisfied
before the original archive".

## Final candidate boundary

After all source/test/harness changes settle they are committed as
`FINAL_CANDIDATE_SHA`; the exact Release executable and driver are built from
that committed SHA (exe SHA-256, driver SHA-256, version, informational
version, signing status, release mode, production eligibility). Physical
Chromium qualification runs against this exact candidate. Later
evidence/archive/STATE commits must not retag the candidate binary; if a
later commit changes production or harness code, the old candidate is invalid
and must be rebuilt/requalified.

## Closure conditions (all must hold)

1. Edge historical first valid `FAIL_PRODUCT` has a defensible final
   disposition; if a product defect, fixed and requalified; no unexplained
   unresolved valid product defect.
2. Real restricted browser visual packets exist for Chrome, Edge, Brave and
   verify `Valid:true`.
3. Capable multimodal review actually occurred.
4. 19.1 truthfully closed; 19.4 truthfully supported.
5. `validate.ps1 -Configuration Release -Ci -Publish` actually passes.
6. Native ABI actually passes.
7. Current resource qualification actually passes.
8. Privacy/recovery/support/historical-compatibility gates actually pass.
9. Ledger correction records 38 historical tasks with mapping.

If any condition fails, the change stays ACTIVE and the remaining gaps are
reported.