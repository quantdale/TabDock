# visual-qualification-evidence

## ADDED Requirements

### Requirement: Adopted real-app visuals SHALL use REAL_APP_RESTRICTED capture

Routine real-app capture SHALL prefer the minimal approved region needed to prove presentation: host client plus bounded context around the guest, never whole virtual desktop. `REAL_APP_RESTRICTED` SHALL be the privacy class for adopted-browser/Notepad/Terminal imagery; `TEST_OWNED`/`PRODUCT_OWNED` remain for GuineaPigs and chrome.

Every frame SHALL record requested/actual rectangle, monitor/DPI, method (`BitBlt` screen composition vs `PrintWindow`), target identity, privacy, and duration. Whole-desktop capture SHALL be disabled by default and SHALL NOT be added to support bundles, ordinary CI artifacts, source control, or model uploads implicitly.

#### Scenario: Real-app default capture is minimized
- **WHEN** `browser-fullscreen-contained` requests visual evidence for an adopted Chromium window
- **THEN** only the host client and bounded guest/context regions are retained with `privacyClass=REAL_APP_RESTRICTED`; no unrelated desktop or URL content beyond the controlled test page is recorded

#### Scenario: Adopted app visual privacy is enforced
- **WHEN** a scenario would require capturing personal documents, terminal history, or unrelated windows to prove a real-app cell
- **THEN** capture is refused or constrained to the controlled blank/test content; whole-desktop capture remains disabled and the result is `BLOCKED_CAPABILITY` or `REVIEW_UNAVAILABLE` as appropriate

### Requirement: Real-app visual packets SHALL be hash-bound and separately reviewed

A real-app visual packet SHALL bind exact candidate/run/scenario/attempt, HWND/process-start identity, topology/DPI, packet hash, artifact hashes, checkpoint expectations, and privacy scope. A capable multimodal agent SHALL inspect retained `REAL_APP_RESTRICTED` images via the canonical workflow `.agent/workflows/visual-evidence-review.md`; verdicts `VISUAL_OK`/`VISUAL_SUSPECT`/`VISUAL_DEFECT`/`REVIEW_UNAVAILABLE` remain separate from native outcomes and SHALL NOT promote a failed lease/identity/presentation.

#### Scenario: Restricted browser packet is reviewed
- **WHEN** a valid `browser-fullscreen-contained` packet retains before/fullscreen/after images with `Valid:true` native and `REAL_APP_RESTRICTED` scope
- **THEN** a reviewer can hash-verify the packet/result, inspect raw PNGs, and record `VISUAL_OK` only when the visual contract is met; `REVIEW_UNAVAILABLE` remains `BLOCKED_CAPABILITY` for a required visual gate

### Requirement: Real-app visual tamper SHALL be rejected

Offline verification SHALL reject any packet/result whose PNG bytes, packet hash, candidate/run binding, checkpoint artifact IDs, or privacy scope was modified, removed, duplicated, path-escaped, or re-bound to another run.

#### Scenario: Real-app packet is tampered
- **WHEN** a retained real-app PNG is modified after capture
- **THEN** verification returns `FAIL_HARNESS`-equivalent and the associated visual gate cannot claim `PASS`
