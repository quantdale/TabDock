# Visual evidence closure and performance re-qualification

## Why

The preceding `2026-08-31-visual-evidence-ai-review` campaign added bounded,
model-free ValidationDriver visual evidence. Its canonical 178-row task ledger
has now been reconciled against current repository evidence: 93 rows are
checked as `COMPLETED_AND_PROVEN`, while 85 remain unchecked with explicit
`IMPLEMENTED_BUT_NOT_ACCEPTED`, `NOT_IMPLEMENTED`, `SUPERSEDED`,
`BLOCKED_SUPERVISED`, or `BLOCKED_CAPABILITY` classifications. The
row-level matrix is
`.agent/investigations/visual-evidence-ledger-reconciliation-2026-09-01.md`.
This campaign closes the applicable visual evidence boundary first, then
measures its disabled/checkpoint/flight cost from representative scenarios and
uses measured results to qualify an exact Release v1.1 candidate.

The current worktree is intentionally not treated as an accepted visual
release: visual implementation files, tests, workflow/investigation records,
and generated run roots are modified or untracked. The predecessor remains
unarchived, and no historical 84/89 implementation roll-up is substituted for
the reconciled canonical ledger. This change must resolve that provenance
before any archive or release claim.

## What Changes

- **Milestone A — visual evidence closure (first milestone):** close the
  applicable remaining predecessor obligations identified by the reconciled
  row-level matrix, not an assumed five-row roll-up. An exclusive supervised
  desktop run must prove exact candidate identity, a healthy packet, a seeded
  defect packet, a transient flight packet, actual multimodal inspection,
  hash-bound review results, honest `REVIEW_UNAVAILABLE` behavior, tamper
  rejection, first-attempt defect preservation, and native/lease precedence.
- Make the intended verifier invariant explicit before implementation:
  trace the current `packetSha256` data flow, determine whether any
  caller-supplied or placeholder value can weaken packet binding, and then fix
  and test the authoritative packet/result hash comparison.
- Tighten required visual collection contracts so null/default collections
  cannot satisfy a required packet, manifest, or review-result field. JSON
  round trips, historical migration behavior, and malformed-input failures
  must be explicit rather than hidden by nullable warnings.
- Define one authoritative machine-verifiable representation for contact-sheet
  generation failure. Raw PNGs remain valid and immutable when the derived
  contact sheet fails, while the derived failure is visible in the manifest,
  scenario result, packet eligibility, and offline verification outcome.
- Resolve all intended visual source, test, specification, investigation, and
  workflow files into explicit Git provenance before archiving the preceding
  change. Generated screenshots, run roots, caches, and secrets remain
  excluded.
- **Milestone B — performance/resource re-baseline (main new work):** measure
  disabled, checkpoint, and flight modes over representative rename, split,
  inline-capture, maximize/fullscreen, title-centering, and one controlled
  topmost/transition scenario. Record sample count, median, p95 where useful,
  maximum, scenario, dimensions, capture method, mode, retained frames/bytes,
  candidate SHA, and machine/topology classification.
- Prove disabled mode performs no screenshot capture, PNG encoding, retained
  image allocation, contact-sheet/packet work, worker/timer activity, or
  artifact growth; report unavoidable branch/check overhead separately.
- Measure capture, PNG encode, filesystem write, manifest/hash, contact-sheet,
  packet, ring-buffer, flush, cleanup/cancellation, CPU, allocation/peak-memory,
  GDI/HBITMAP/HDC, handle, and worker/timer behavior where the platform can
  observe them without changing production behavior.
- Derive conservative artifact, latency, memory, CPU, native-resource, and
  lifecycle budgets from measured distributions. Do not turn guessed defaults
  into release thresholds before measurements exist.
- Re-run the existing resource-lifecycle qualification with visual evidence
  disabled and enabled, compare against the non-visual baseline, and preserve
  synthetic/headless versus supervised physical classification. Ordinary CI
  remains screen-capture-free and model-free.
- **Milestone E — Release v1.1 closure:** after A and B are accepted, build and
  qualify the exact final committed candidate, record its fresh executable
  SHA-256 and embedded source identity, run version/publish and canonical
  CI-safe Release validation, dependency audit, strict OpenSpec validation,
  unit and ValidationDriver deterministic self-tests, visual manifest/review
  gates, and historical non-visual compatibility checks. Do not reuse an older
  artifact SHA.
- Keep visual verdicts separate from native qualification outcomes. A visual
  result cannot promote a failed lease, identity, foreground, cleanup, or
  native assertion, and a required visual-review failure cannot be hidden by a
  native PASS.

No production TabDock behavior, Shepherd authority, HWND identity rule,
physical-input safety rule, or unrestricted desktop capture behavior changes in
this planning scope. No model SDK, network upload, or provider-specific
inference dependency is introduced.

## Capabilities

### New Capabilities

- `visual-performance-requalification`: measured visual evidence overhead,
  disabled-mode zero-work invariant, derived resource budgets, comparison to
  the non-visual baseline, and bounded CI/resource gates.

### Modified Capabilities

- `visual-qualification-evidence`: strict packet/result/manifest collection
  contracts, verifier hash binding, explicit derived contact-sheet failure,
  supervised closure, and first-attempt visual-defect semantics.
- `validation-qualification`: visual closure prerequisites, model-free
  deterministic performance gates, visual/native outcome separation, and
  required-review non-pass behavior.
- `qualification-control-plane`: visual artifact/review indexing, stale and
  tamper rejection, historical bundle compatibility, and exact candidate/run
  continuity.
- `resource-lifecycle-qualification`: visual-enabled/disabled comparison,
  capture/encode/retention resource observations, and worker/timer/handle
  cleanup evidence.
- `release-engineering`: v1.1 release qualification must consume the exact
  post-A/B candidate and retain fresh executable hash, embedded source
  identity, publish smoke, and visual/performance gate evidence.

## Milestones and Acceptance Boundary

### A — Visual evidence closure

A is a prerequisite to archiving the preceding visual-evidence change. A is
not satisfied by deterministic tests alone. The acceptance record must bind one
exact committed candidate and include:

1. exclusive supervised desktop and lease/identity evidence;
2. one healthy visual checkpoint packet that a capable multimodal agent opens
   and classifies without a false defect;
3. one test-owned seeded visual defect packet that the agent detects without
   being told the defective frame by filename or prompt;
4. one transient/flight-recorder failure packet with ordered pre-failure
   frames and the trigger frame;
5. a valid `visual-review-result.json` for each reviewed packet, bound to the
   exact packet/image hashes and identity;
6. an honest non-vision `REVIEW_UNAVAILABLE` result and required-gate behavior;
7. offline rejection after mutating a screenshot, packet hash, path, candidate,
   scenario, or review binding;
8. proof that visual evidence cannot override failed native lease/identity/
   foreground/cleanup/assertion prerequisites;
9. first-attempt visual-defect preservation across investigation reruns;
10. explicit disposition of every remaining prior-campaign task and all
    intended visual files tracked before archive.

### B — Performance/resource re-baseline

B is accepted only when measurements have enough repeated samples to support
reported distribution statistics and the resulting budgets are checked by
hermetic regression tests. Any unavailable native counter remains unavailable
and blocks a resource claim; it is never converted to zero. The gate must show
that healthy flight history is discarded, failure flush is bounded, and no
visual worker/timer/file/native resource survives cleanup.

### E — Release v1.1 closure

E is accepted only after A and B: the final release evidence is generated from
the exact clean committed candidate, its fresh executable bytes, embedded source
identity, and artifact hash agree across release, qualification, and bundle
manifests. All required deterministic and CI-safe gates pass; unavailable
physical/signing/external requirements remain honestly blocked rather than
silently promoted.

## Deferred Follow-up Campaigns

These are explicitly excluded and remain separate future changes:

- **DPI/topology hardening:** negative-coordinate and above-origin monitors,
  150%/175%/200% DPI, deeper mixed-DPI permutations, monitor transfer, and
  broader topmost/topology matrices.
- **Real-app hardening:** broader Chromium/fullscreen coverage, Notepad broker
  behavior, Windows Terminal monarch/hosting behavior, and additional
  real-application lifecycle quirks.

Neither follow-up is required to implement the measured visual overhead
baseline, and neither may be smuggled into A, B, or E through an expanded
capture scope.
