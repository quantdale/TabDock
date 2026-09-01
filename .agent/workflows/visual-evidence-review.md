# Visual evidence review workflow

Purpose: let a capable (vision) development agent inspect a TabDock validation run's retained images without modifying native qualification.

## Prerequisites

- The run wrote `run-manifest.json`, per-scenario `*.json`/`*.junit.xml`/`*.timeline.json`, `visual/<scenario>/attempt-NNN/manifest.json`, and when `--visual-review-packet` was enabled a review packet at `visual/<scenario>/attempt-NNN/review/visual-review-manifest.json`.
- The operator selected one attempt: candidate SHA, run ID `TestRunProvenance.RunId`, scenario name, attempt number.

## Steps

1. **Bind packet by hash.** Locate the packet path declared in the scenario result `visualEvidence.reviewPacketArtifact` and the visual manifest `reviewPacketPath`. Verify SHA-256 by re-hashing the file bytes; reject if absent/modified/duplicated/path-escaping or if `candidateSha`/`runId`/`scenario`/`attempt` disagree with the run manifest.

2. **Open overview first.** Open the contact sheet `visual/.../review/contact-sheet.png` (derived, `derived=true`) for the attempted check-points. It contains chronological thumbnails, checkpoint IDs, phase, relative time, scope and a truncated expectation label. Do not treat it as authoritative — hashes of raw PNGs remain authoritative.

3. **Inspect raw frames at full resolution.** Open each raw `visual/.../checkpoints/.../*.png` (or `.../ring/.../*.png`) referenced by the packet. Evaluate expected vs observed presentation for every required checkpoint. Identify visible symptoms before hypothesizing causes; distinguish capture artifacts from presentation defects.

4. **Correlate with native evidence.** Compare with scenario result `visibleHwndSet`, `guestRectangles`, `paneRectangles`, `desktopQualification`, `capabilities`, and `<stem>.timeline.json`. A visually healthy frame never proves process identity, lease continuity, foreground ownership, geometry, cleanup, or candidate provenance.

5. **Write the structured result.** Create the file at the packet `requiredResultPath` (default `visual/.../review/visual-review-result.json`) with schema `tabdock-visual-review-result-v1`:
   - `packetSha256` = hash of the exact packet file inspected
   - `candidateSha`/`runId`/`scenario`/`attempt` = packet identity
   - `verdict`: `VISUAL_OK` | `VISUAL_SUSPECT` | `VISUAL_DEFECT` | `REVIEW_UNAVAILABLE` (non-vision harness)
   - `reviewerKind`/`reviewerId`/`reviewedUtc` provenance (informational; not a trust substitute)
   - `reviewedImages[]` = each `artifactId`/`checkpointId`/`sha256` reviewed (must cover every required checkpoint when a gate requires review)
   - `findings[]` = every concrete finding with `checkpointId`/`artifactId`/`imageSha256`, category (see spec), severity, `expected`/`observed`/`uncertainty`, optional normalized `region`
   - Do not embed absolute machine paths or secrets. Do not reuse file-name cues ("healthy"/"occluded") as evidence.

6. **Verify.** Run `VisualReviewVerifier.VerifyFiles(artifactRoot, packetPath, resultPath)` or the PowerShell `Test-QualificationVisualManifest` path. Re-hash every referenced image. Reject stale packet hashes, unknown artifact IDs, unreviewed required images, invalid verdicts/regions, or hash mismatches.

## Non-vision fallback

If the harness cannot open images, write `REVIEW_UNAVAILABLE` with empty `reviewedImages`/`findings` and a note explaining the limitation. Do not fabricate a verdict from file names, metrics, or logs.

## Outcome boundary

`VISUAL_*` never overrides native `PASS`/`FAIL_PRODUCT`/`FAIL_HARNESS`/`BLOCKED_*`. A `VISUAL_DEFECT` on a gate that declares visual review required blocks visual acceptance; it does not by itself change the native scenario outcome. Human/operator disposition correlates the two evidence streams before authorizing source changes.

## Supplying a packet explicitly

When several runs exist, pass the packet path explicitly (e.g. `--visual-review-packet <relativePath>` or environment selection) so the agent does not guess the wrong attempt.
