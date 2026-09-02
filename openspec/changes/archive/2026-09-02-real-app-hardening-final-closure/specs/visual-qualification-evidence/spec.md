# visual-qualification-evidence

## MODIFIED Requirements

### Requirement: Real-app visual packets SHALL be hash-bound and separately reviewed

A real-app visual packet SHALL bind exact candidate/run/scenario/attempt, HWND/process-start identity, topology/DPI, packet hash, artifact hashes, checkpoint expectations, and privacy scope. A capable multimodal agent SHALL inspect retained `REAL_APP_RESTRICTED` images via the canonical workflow `.agent/workflows/visual-evidence-review.md`; verdicts `VISUAL_OK`/`VISUAL_SUSPECT`/`VISUAL_DEFECT`/`REVIEW_UNAVAILABLE` remain separate from native outcomes and SHALL NOT promote a failed lease/identity/presentation.

For every installed Chromium browser (Chrome, Edge, Brave), at least one accepted exact-candidate physical F11 attempt SHALL actually produce the required raw PNG checkpoints (baseline captured state, immediately before F11, fullscreen state, post-containment/restored state, and a settled fifth where helpful), a visual manifest, a review packet, a packet SHA-256 computed from the exact packet bytes, verified selected image hashes, matching candidate/run/scenario/attempt/topology/monitor/privacy-class bindings, and a verifier result of `Valid:true`. Capability planning, future-packet intent, or "visual implementation exists" wording SHALL NOT substitute for the packet itself. Browser profiles SHALL be test-owned with controlled isolated content only; whole-desktop capture remains disabled and unrelated windows/personal content are never captured.

#### Scenario: Restricted browser packet is reviewed
- **WHEN** a valid `browser-fullscreen-contained` packet retains before/fullscreen/after images with `Valid:true` native and `REAL_APP_RESTRICTED` scope
- **THEN** a reviewer can hash-verify the packet/result, inspect raw PNGs, and record `VISUAL_OK` only when the visual contract is met; `REVIEW_UNAVAILABLE` remains `BLOCKED_CAPABILITY` for a required visual gate

#### Scenario: Installed Chromium browsers produce real packets
- **WHEN** Chrome, Edge, and Brave are installed and a final real-app closure claims visual acceptance
- **THEN** each browser has an existing hash-verified packet with real raw PNGs and verifier `Valid:true`; no browser closes on intent alone

### Requirement: Real-app visual tamper SHALL be rejected

Offline verification SHALL reject any packet/result whose PNG bytes, packet hash, candidate/run binding, checkpoint artifact IDs, or privacy scope was modified, removed, duplicated, path-escaped, or re-bound to another run. At least one accepted real Chromium packet SHALL be tamper-exercised: a copy in a temporary validation root with one altered PNG byte SHALL be deterministically rejected by the offline verifier, and the exact rejection reason SHALL be recorded. The authoritative packet SHALL NOT be modified.

#### Scenario: Real-app packet is tampered
- **WHEN** a retained real-app PNG is modified after capture
- **THEN** verification returns `FAIL_HARNESS`-equivalent and the associated visual gate cannot claim `PASS`

#### Scenario: Tamper rejection is exercised on a real packet
- **WHEN** a final visual closure is claimed
- **THEN** at least one accepted real Chromium packet copy with a one-byte PNG alteration is deterministically rejected and the rejection reason is recorded, while the authoritative packet remains unmodified