# Design — visual closure, measured overhead, and exact Release v1.1 qualification

## Context

The preceding `2026-08-31-visual-evidence-ai-review` change added a
ValidationDriver-only visual pipeline: semantic capture scopes, lossless PNG
artifacts, bounded checkpoints and flight history, contact sheets, review
packets/results, and offline bundle verification. Its canonical 178-row ledger
has now been reconciled against current source and evidence: 93 rows are
checked as `COMPLETED_AND_PROVEN`, while 85 remain explicitly classified and
unchecked in
`.agent/investigations/visual-evidence-ledger-reconciliation-2026-09-01.md`.
The acceptance boundary remains open; this design does not treat deterministic
tests or a syntactically valid verifier as physical or multimodal evidence.

The current repository is not an accepted release of that visual change. The
working tree contains intended visual implementation and test files as
modified or untracked, generated run roots, and planning records. The first
milestone of this change therefore closes the preceding campaign's applicable
acceptance, provenance, and contract obligations; it does not assume an
unmapped 84/89 roll-up or a five-row remainder.

The new work also answers a separate question: what does retaining visual
state cost? Existing resource qualification already measures run-owned process
identity, handles, USER/GDI objects, private bytes, working set, threads, and
TabDock-owned windows. Existing pixel capture measures screen/client and
PrintWindow behavior. The design extends those validation observations and
keeps visual work in the ValidationDriver boundary:

```text
 exact clean candidate
          |
          v
  A: supervised visual closure  -----> archive prior change only after A
          |
          v
  B: paired none/checkpoints/flight measurements
          |
          v
  measured budgets + deterministic regression gates
          |
          v
  E: exact Release v1.1 build -> qualify -> publish/version smoke
```

No production window, Shepherd, HWND identity, foreground/lease, input, or
support-bundle behavior is changed by this design.

## Goals / Non-Goals

**Goals:**

- Close the prior visual-evidence acceptance boundary with exact-candidate,
  supervised, hash-bound, non-vacuous evidence.
- Investigate the reported `packetSha256: string.Empty` literal before editing
  it; establish the intended packet/result/manifest hash invariant and prove
  whether the defect is verifier-only, test-only, or capable of weakening an
  actual gate.
- Make required packet, manifest, and review collections strict. Missing or
  JSON `null` collections must be malformed evidence, not an empty/default
  pass path; supported historical non-visual schemas remain compatible.
- Make derived contact-sheet failure a first-class, machine-verifiable event
  while preserving valid immutable raw PNGs. It must be visible to the
  scenario result, visual manifest, packet eligibility, and offline verifier,
  and must not silently yield `VISUAL_OK`.
- Reconcile all intended visual source/spec/test/workflow/investigation files
  into explicit Git provenance before any preceding-change archive.
- Measure disabled, checkpoint, packet, and flight costs over representative
  presentation scenarios using repeated paired runs and exact candidate,
  machine, topology, mode, dimensions, and capture-method metadata.
- Establish conservative visual latency, memory, bytes, allocation, CPU, native
  resource, artifact-growth, and cleanup budgets from observed distributions,
  not guessed constants.
- Prove disabled mode performs no visual capture/encode/retention/packet work
  and incurs only separately reported unavoidable branch/check overhead.
- Prove ring-buffer and artifact bounds, cleanup/cancellation, and no surviving
  worker/timer/file/native resources after healthy or failed runs.
- Keep ordinary CI model-free, screen-capture-free, deterministic, and
  capable of exercising synthetic visual fixtures and offline verifiers.
- Produce Release v1.1 evidence from the exact final committed tree and exact
  executable bytes, with fresh hash and embedded source identity.

**Non-Goals:**

- No production TabDock UI or Shepherd behavior changes.
- No broad DPI/topology campaign: negative/above-origin layouts, 150%–200%
  matrices, deeper mixed-DPI permutations, and monitor-transfer expansion are
  deferred to a separate **DPI/topology hardening** change.
- No broad real-application campaign: Chromium fullscreen breadth, Notepad
  broker behavior, Windows Terminal hosting, and additional real-app quirks
  are deferred to a separate **real-app hardening** change.
- No universal exact-pixel golden baseline for real applications, Windows
  versions, GPUs, themes, ClearType, DPI, or mixed rendering environments.
- No model SDK, API key, network upload, hidden remote inference, or automatic
  screenshot-driven source edit.
- No unrestricted or always-on desktop recording; VirtualDesktop remains an
  explicit supervised policy, not a default.
- No archive of the prior change before its acceptance and provenance gates.

## Decisions

### 1. Milestone order and authority

A is a hard prerequisite for archival of the preceding visual change. B may
prepare deterministic infrastructure before physical A is available, but B
cannot relabel synthetic evidence as physical visual acceptance. E may start
only after A and B have accepted dispositions.

The active prior change remains authoritative for its own implementation until
A's acceptance record is complete. This change is authoritative only for its
closure, measurement, and release-requalification plan. A candidate SHA,
run ID, and attempt identity are immutable joins across all three milestones.

**Alternative rejected:** archive the preceding change now because deterministic
tests pass. That would convert indirect evidence into a physical/multimodal
claim and would lose the first-attempt boundary.

### 2. Packet hash invariant and `packetSha256` investigation

Before changing the suspicious literal, the implementation must audit every
writer, test fixture, verifier branch, and bundle index that handles
`packetSha256`. The intended invariant is:

```text
packetBytes = exact bytes on disk
packetSha256 = SHA256(packetBytes)
review.packetSha256 == packetSha256
manifest.reviewPacketSha256 == packetSha256   (when a packet is linked)
packet candidate/run/scenario/attempt == review == manifest == run hierarchy
```

A packet/result is invalid when the packet path/hash is absent where a review is
claimed, the hash is malformed, the computed bytes differ, or any identity
field disagrees. The verifier must compare against the computed packet hash,
never an empty placeholder or a caller-provided substitute. Tests must cover a
correct hash, wrong hash, empty literal, changed packet byte, stale packet,
and packet path/manifest disagreement. The investigation result must classify
the original issue as test-only, verifier-only, or gate-impacting; only then is
the minimal fix selected.

**Alternative rejected:** patch the literal immediately without tracing
callers. That could make a test green while leaving another gate comparing a
placeholder or could hide a fixture defect.

### 3. Strict collections with explicit compatibility

New visual packet/result/manifest models use non-nullable concrete collections
(or an equivalent strict converter/constructor) for required arrays and maps.
The serializer/deserializer contract is:

- required collections are present, non-null, and contain valid elements;
- JSON `null`, omitted required fields, duplicate IDs, and invalid element
  values fail validation deterministically;
- an empty collection is valid only where the schema explicitly says empty is
  valid (for example, a `REVIEW_UNAVAILABLE` result may have no reviewed raw
  images, subject to its required note/provenance contract);
- optional collections have an explicit default and are not confused with a
  required collection;
- supported historical non-visual manifests are migrated/validated according
  to their declared schema and do not fabricate visual collections or verdicts.

Round-trip tests must serialize and deserialize every current required model,
then separately remove/null each required collection and assert rejection.
Nullable compiler warnings in the packet/verifier path are treated as design
signals, not suppressed.

**Alternative rejected:** nullable `IReadOnlyList<T>` properties with runtime
`.Count` checks. That was convenient for a failed round trip but permits
missing evidence to travel farther and weakens the schema boundary.

### 4. Authoritative derived-artifact failure representation

A contact sheet is a derived convenience artifact; raw PNGs remain the
authoritative visual evidence. Nevertheless, failure to build a requested
contact sheet is not silently folded into an ordinary unavailable capture.
The visual manifest will carry a dedicated, stable `derivedArtifactFailures`
collection (or an equivalent schema-versioned field) containing at least:

- stable artifact kind/ID (`contact-sheet`);
- checkpoint/scenario/attempt identity;
- failure phase and reason category (budget, decode, encode, IO, validation);
- requiredness and whether raw source artifacts were preserved;
- timestamp and bounded diagnostic detail;
- candidate/run binding through the enclosing manifest.

The scenario result exposes the same failure ID/count, and a packet either
references the successful derived artifact or records the failure explicitly.
Offline verification checks the failure record and confirms raw source hashes
remain valid. A required derived artifact makes the visual gate non-pass. An
optional derived artifact may be reviewed from raw images only when the
structured review result explicitly acknowledges the failure; an unacknowledged
derived failure forbids `VISUAL_OK`. In either case, raw evidence is not
removed or overwritten.

**Alternative rejected:** use only `Unavailable[]` with a checkpoint named
`contact-sheet`. That conflates capture unavailability with a post-capture
build failure and makes it easy for a consumer to treat the packet as healthy.

### 5. Measurement matrix and paired baseline

B uses the same exact candidate and controlled artifact root across paired
runs. Each row is run in both visual-disabled and visual-enabled variants,
with mode order alternated or randomized within the supervised protocol to
avoid one-sided warm-up bias. Every run records candidate SHA, run ID,
scenario, attempt, mode, policy, dimensions, capture method, machine/topology
classification, sample count, and artifact root. Run-owned processes and
isolated state are cleaned between samples.

The minimum presentation matrix is:

| Scenario family | Required measurement purpose |
| --- | --- |
| rename | popup/editor capture, short checkpoint and packet path |
| split | two-guest composition, contact sheet and multiple scopes |
| inline capture | owned chrome/open-close checkpoints and IO |
| maximize/fullscreen | high-risk transition and flight history |
| title centering | geometry-sensitive checkpoint and image dimensions |
| one controlled topmost/transition case | bounded z-order-sensitive flight or failure path |

The sample plan is fixed before collection: at least 30 disabled/headless
observations per comparable profile and at least 20 observations per enabled
scenario/mode cell where physical capture is safe. If fewer samples are
possible, the report must say so and may report median/max only; it may not
present an under-sampled p95 as a stable budget. Checkpoint, checkpoint-plus-
packet, flight-healthy-discard, and flight-failure-flush costs are separate
cells rather than one blended average.

**Alternative rejected:** benchmark only `maximize-repro` or only a trivial
GuineaPig image. That misses popup, split, packet, and artifact-lifecycle
costs and cannot support a campaign-wide budget.

### 6. Measurement boundaries and statistics

Instrumentation is validation-side and surrounds existing operations without
changing their native semantics. Record independently:

- capture duration by method and scope;
- PNG encode duration and output size;
- temporary/final filesystem write and SHA-256 duration;
- manifest, contact-sheet, packet, and instruction generation duration;
- retained frame count/bytes and peak ring bytes;
- peak working set/private bytes and managed allocation deltas where safe;
- CPU cost where the host can measure it without perturbing the operation;
- HBITMAP/HDC, GDI/USER, process handle, file handle, timer, worker/thread,
  and TabDock-owned-window observations before/after cleanup;
- artifact count/bytes and healthy-flight discard residue;
- cancellation/timeout stop time and whether any work continues afterward.

For every metric/cell report `n`, median, p95 when `n >= 20`, maximum, and
measurement units. Keep raw samples and an aggregate JSON/JUnit report under
run-owned output; do not commit generated screenshots, logs, machine paths, or
raw run directories.

Disabled mode has an exact work invariant: visual capture requests,
successful/failed capture counts, PNG encodes, retained bytes, packet/contact
work, visual artifacts, and visual worker/timer activity are all zero. Branch
and policy-check elapsed time may be nonzero but is reported separately as
control overhead. An unavailable native counter is unavailable and blocks a
resource claim; it is never converted to zero.

### 7. Budget derivation and regression gates

No new threshold is accepted before the measurement report exists. After the
report, budgets are derived per operation/mode/scenario from observed
statistics with a written safety margin and explicit rationale. The selected
budget records the source candidate, sample count, distribution, outliers,
margin, and whether it is a hard safety ceiling or a diagnostic warning.

Deterministic gates then verify:

- disabled mode's zero-work counters;
- hard count/byte/dimension/ring limits;
- measured budget comparisons using synthetic/in-memory fixtures;
- healthy flight discard and failure flush bounds;
- no artifact growth beyond the selected policy;
- resource cleanup and cancellation invariants;
- visual-enabled resource deltas against the paired non-visual baseline.

A regression gate fails or blocks rather than accepting a missing metric,
unsupported p95, unbounded allocation, resource leak, or unexplained baseline
increase. These gates supplement, never replace, existing native/resource
qualification.

### 8. Resource and lifecycle safety

Reuse the existing read-only resource probe and run-owned process policy. The
visual recorder must not create a permanent timer, background worker, desktop
subscription, or UI repair loop. Captures and encoding may be synchronous only
inside explicitly bounded ValidationDriver operations; any larger operation
must be measured and remain outside production UI threads.

The safety matrix covers normal success, optional capture failure, required
capture failure, contact-sheet failure, packet failure, cancellation, timeout,
process abort, healthy flight discard, and failure flush. Each path proves:

```text
try { capture/encode/write/flush }
finally {
    stop ring; dispose native objects; close files; remove temp files;
    preserve first-attempt evidence; leave no worker/timer/artifact residue
}
```

**Alternative rejected:** use a process-wide visual worker or periodic desktop
poll. It would violate the bounded, event-driven, privacy-aware capture model
and complicate disabled-mode proof.

### 9. Supervised A acceptance protocol

A's evidence is produced only from an exclusive, supervised Windows desktop
where required. The operator confirms the exact clean candidate and does not
interact with the desktop during each run. The acceptance set contains:

1. healthy controlled packet;
2. test-owned seeded visual defect packet (occlusion, wrong guest, clipping,
   or misalignment) without the defect encoded in the filename or prompt;
3. transient/flight packet containing ordered pre-trigger frames and trigger;
4. capable multimodal agent review of retained contact/raw images and a valid
   result for each packet;
5. non-vision path returning `REVIEW_UNAVAILABLE` honestly;
6. offline verification of packet/result/manifest/image hashes and identity;
7. deliberate screenshot, packet, path, candidate, scenario, and review
   mutations rejected;
8. native lease/identity/foreground/cleanup failure remaining authoritative
   despite `VISUAL_OK`;
9. first-attempt visual defect remaining authoritative across reruns;
10. explicit disposition of every prior-change task and tracked intended file.

The image review is a separate evidence stream. A capable agent describes
visible symptoms before causes and correlates native facts before any product
claim. A non-vision agent does not infer from filenames, metrics, or logs.

### 10. Release v1.1 exact-candidate protocol

After A and B are accepted, perform E from a clean tree. Build the exact final
candidate once for qualification, retain the executable without recompilation,
and prove:

- requested SHA equals `HEAD` and the final tree is clean;
- embedded source identity equals the candidate SHA;
- fresh executable SHA-256 is consistent in release, qualification, and bundle
  artifacts;
- Debug/Release builds and unit tests pass;
- ValidationDriver/GuineaPig Release builds and deterministic self-tests pass;
- canonical CI-safe `scripts/validate.ps1 -Configuration Release -Ci -Publish`
  passes, including dependency audit, resource, privacy, ABI, OpenSpec, and
  publish/version smokes;
- visual manifest/packet/result and historical non-visual compatibility gates
  pass offline;
- unavailable signing, physical, or external evidence remains explicitly
  blocked and is not relabeled as release PASS.

The old Release artifact hash is historical only. It cannot be reused as E
proof for the new tree.

### 11. Provenance and archive cutover

Before archiving the preceding change, run a tracked-file audit against the
intended allowlist. The audit must distinguish:

- intended source/test/spec/workflow/investigation files to add and commit;
- generated `.visual-validation-runs/`, build output, caches, logs, and machine
  artifacts to ignore or remove only when run-owned and authorized;
- unrelated user changes to preserve and report.

The preceding change is archived only after every remaining predecessor row has
an accepted, superseded, or explicitly blocked disposition, the
verifier/collection/contact contracts are fixed and verified, supervised A
evidence exists, canonical specs are synchronized, and
`HEAD == origin/main` has been independently proven for the committed result.
The current reconciliation is 93/178 checked with 85 rows still open; it is a
ledger repair, not an archive claim. This change remains open until B and E are
independently accepted.

### 12. Deferred campaign boundaries

The following are named follow-ups, not hidden tasks in this change:

- **DPI/topology hardening:** negative-coordinate and above-origin monitor
  layouts, 150%/175%/200% DPI, broader mixed-DPI combinations, monitor
  transfer, and deeper topmost/topology permutations.
- **Real-app hardening:** broader Chromium/fullscreen matrix, Notepad broker
  behavior, Windows Terminal monarch/hosting behavior, and additional adopted
  real-app lifecycle/privacy quirks.

This campaign may record a measured limitation or a handoff link for either
follow-up, but it must not expand visual scopes or acceptance claims to absorb
them.

## Risks / Trade-offs

- **Supervised availability:** A and parts of B require an exclusive desktop
  and a capable vision facility. Deterministic fixtures can prove schema and
  hash behavior but cannot substitute for those observations; blocked remains
  an honest outcome.
- **Measurement perturbation:** timers, allocation probes, and file hashing can
  affect latency. Instrumentation is recorded as part of the measurement
  method, paired baseline runs use the same instrumentation, and production
  code remains untouched.
- **Outliers:** a single slow capture or OS counter spike can distort a budget.
  Raw samples, p95/max, outlier notes, and explicit safety margins are retained;
  no silent trimming is allowed.
- **Schema migration:** strict current collections can reject malformed or
  older evidence. Migration is limited to declared supported historical
  schemas; absence in a historical non-visual bundle never synthesizes visual
  PASS.
- **Derived artifact availability:** contact-sheet failure reduces review
  convenience but must not destroy raw evidence. Requiredness and explicit
  acknowledgement determine whether a visual gate is non-pass.
- **Dirty tree/provenance:** the prior visual implementation is currently
  modified/untracked. This plan refuses archive until intended files are
  tracked and unrelated changes are separated; that delays closure but avoids
  an ambiguous candidate.
- **Release timing:** exact-candidate E cannot reuse historical hashes and may
  be blocked by signing or unavailable external evidence. The release record
  must retain those blocked states rather than weakening the gate.
