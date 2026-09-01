# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session. This file never records a
self-referential SHA or treats an old CI run as evidence for the commit that
contains this text.

# CURRENT CAMPAIGN — VISUAL EVIDENCE AND AI REVIEW

**Objective:** complete and qualify the active
`visual-evidence-closure-and-performance-requalification` OpenSpec change as
bounded ValidationDriver evidence infrastructure. Preserve native identity,
lease, timeline, cleanup, and release-gate semantics; do not change production
TabDock behavior.

**Status:** the predecessor `2026-08-31-visual-evidence-ai-review` campaign
has been dispositioned and archived at
`openspec/changes/archive/2026-09-01-2026-08-31-visual-evidence-ai-review/`.
Its canonical ledger contained **178** unique rows: **93** direct evidence
rows plus **85** rows with explicit final dispositions. The archive operation
passed strict validation, synchronized canonical specs (`+11/~5`), and moved
the complete predecessor artifacts. Final disposition totals were **41
`COMPLETED_AND_PROVEN`**, **3 `ACCEPTED_SUPERSEDED`**, **35
`MIGRATED_TO_SUCCESSOR`**, **4 `MIGRATED_TO_DPI_TOPOLOGY_CAMPAIGN`**, and
**2 `MIGRATED_TO_REAL_APP_CAMPAIGN`**. Migrated rows remain obligations of
their named destination; they were not represented as completed implementation.
The historical 84/89 implementation roll-up was not a mapping to the
canonical rows and is not a valid checkbox count. The prior
presentation-integrity physical qualification remains historical evidence and
is not relabeled.

The final exact source candidate is the committed SHA
`6bb8ecc80b103ec9e2e1bc12cebe241b1ab9519f`, version `1.1.0`, with
self-contained Release executable SHA-256
`cf442e369c56c7c06c23b33c25b3434b079398b479e188c47e03f2d76dfbc291`.
Its release mode is `QUALIFICATION_ONLY`; production eligibility is
`BLOCKED_EXTERNAL`; signing is `NOT_CONFIGURED`. The final candidate and all
supervised visual packets are bound to that SHA. Later task/state/investigation
commits are evidence records and do not redefine the binary candidate boundary.
Evidence record `de306d39722b9b67548b8e4878f228a97ef4e706` was pushed before
the successor archive. Archive/spec commit
`496b0c1e498c06b5d86211c9e24ec1ae62349eef` was then created and its
post-archive strict validation passed `38/38`. Final E6.6 closure was
recorded and pushed through `main`; post-push parity and clean-tree proof
were independently verified.

Foundation inventory, implementation status, exact pre-final candidate
correction, row-level dispositions, archive result, and final evidence are
recorded in
`.agent/investigations/visual-evidence-implementation-2026-09-01.md`,
`.agent/investigations/visual-evidence-ledger-reconciliation-2026-09-01.md`,
and `.agent/investigations/release-semantics-correction-2026-09-01.md`.

### Prior campaign evidence

The presentation-integrity implementation and supervised physical qualification
are complete for the exercised matrix. The only valid physical
`FAIL_PRODUCT` is the frozen first Chrome F11 run
(`7f5ba57f-af6e-491e-81aa-b33bd8229471`); its bounded Chromium-family F11
repair was requalified by Chrome
(`71656457-b555-42ce-a782-b4947f33f292`), Edge
(`9cb2ad2a-a6bc-41a2-b196-2492702b9331`), and Brave
(`01c74b6f-fb28-4ab6-acef-ab3c2f3ab4d6`). The accepted split-exit PASS
(`03c3aaa2-37d1-41bd-9382-98fcd2d470ad`) remains evidence; later fail-closed
attempts (`58f04b3a-ffbc-4b7b-aa56-b1ee5c32a9fd`,
`de3a5cc7-e7b0-4e2c-ad2a-a7c6ce1aa33c`, and
`f768ff27-2931-4705-8130-89b0bf23a95d`) remain
`BLOCKED_ENVIRONMENT`. No valid split-exit product failure is established.

The two completed presentation changes are archived under
`openspec/changes/archive/2026-08-31-2026-08-31-user-reported-presentation-integrity/`
and
`openspec/changes/archive/2026-08-31-2026-08-31-presentation-integrity-physical-certification/`.
Canonical `presentation-integrity`, `ui-ux-hardening`, and
`validation-qualification` specs were synchronized before archival. Durable
investigations and raw external validation artifacts remain preserved.

#### Exact-mainline qualification

The pre-consolidation authoritative tree was
`914a25923bd4bb1f5c08d925bfb210bb9208853f`, with a clean worktree and
`HEAD == origin/main`. Its exact Release gate passed:

- clean Debug/Release solution builds, 0 warnings/errors;
- Debug/Release unit tests, `732/732` each;
- Release ValidationDriver/GuineaPig builds, 0 warnings/errors;
- ValidationDriver selftest `c100fc7c-3665-47a2-882d-821eca156a56`,
  `153/153` PASS;
- catalog `scenario-catalog-2026-08-24-v1`, 135 dispatchable scenarios;
- release and `physicalMixedDpi` plans PASS;
- strict OpenSpec `37/37` PASS before canonical spec synchronization;
- CI-safe Release validation/publish PASS, including dependency audit,
  resource, recovery, privacy, ABI, and publish smokes.

The exact Release artifact from that tree was `win-x64`, self-contained,
version `1.0.0`, SHA-256
`E3C830202F07C522B8B0A210B4181D96D92158D84F86576CF23DDEDEA9BBF06F`, with
embedded source identity `914a25923bd4bb1f5c08d925bfb210bb9208853f`.
Physical run artifacts remain bound to their recorded pre-integration
candidate; they are not retagged as evidence for another SHA.

### Branch consolidation

The orientation inventory found only two non-main remote branches, both
valuable planning content: `plan/repo-local-addons-2026-08-28` and
`plan/visual-evidence-ai-review-20260831`. No local non-main branch, auxiliary
worktree, or open pull request was found. The branch audit and rationale are
recorded in
`.agent/investigations/repository-consolidation-2026-09-01.md`.

The repository-local add-ons plan is preserved on `main` as planning-only
content. Existing Repowise MCP, harness adapters, skills, CI, and onboarding
surfaces remain protected; Microsoft Learn and Context7 were not installed.
The active visual-evidence plan is being implemented as supplemental
qualification/diagnostic infrastructure. It never retroactively justifies or
relabels physical results and never replaces hard native evidence.

### Verified environment

- Windows 11 Pro family, raw product label Windows 10 Pro, 25H2 build 26200
  revision 9278; .NET 8.0.30; standard-user session 1.
- Primary `(0,0)-(1920,1200)`, work `(0,0)-(1920,1140)`, 120 DPI/125%;
  secondary `(1920,0)-(3840,1080)`, work `(1920,0)-(3840,1032)`, 96
  DPI/100%; no negative-coordinate monitor.
- Chrome, Edge, Brave, Windows Terminal, and Notepad available; Firefox
  unavailable. `stageBAvailable=false`; production signing is not configured.

### Safety and evidence rules

- Preserve raw first failures; never best-of-N a valid failure into PASS.
- Never weaken `WindowFromPoint`/`GA_ROOT`, foreground, process-start, HWND
  generation, provenance, local z-order, or cleanup protections.
- Keep generated artifacts, logs, caches, machine paths, credentials, and
  secrets out of Git.
- Visual evidence is supplemental; it never replaces hard native evidence.

## Previous campaign

**COMPLETED:** presentation-integrity implementation and physical certification
for the exercised matrix. Its changes are archived and remain bound to their
recorded candidate identities.

## Active implementation

The visual-evidence and multimodal AI-review closure is archived at
`openspec/changes/archive/2026-09-01-visual-evidence-closure-and-performance-requalification/`.
The predecessor's 178 rows are archived with all 85 former unchecked rows
explicitly dispositioned. The implementation checkpoint, reconciliation
matrix, Release-semantics correction, exact Milestone A records, and archived
successor task ledger are the durable planning sources.

The archive synchronized the six successor delta capabilities into canonical
specs (`+8/~15`); post-archive strict repository validation passed `38/38`.

The deterministic closure is implemented in ValidationDriver-only seams:
exact packet-byte hashing, strict current collections, derived-failure
propagation, scenario/run hierarchy links, offline bundle verification, paired
visual measurement/resource gates, and native/visual outcome separation.
Final deterministic evidence includes `153/153` selftests from run
`a8771ecf-9ee9-4199-bc7a-686545503531`, 135 catalog scenarios,
`179/179` release-tooling tests, Debug/Release solution builds, and
`795/795` Debug/Release unit tests.

The final source candidate is `6bb8ecc80b103ec9e2e1bc12cebe241b1ab9519f`.
Its exact self-contained Release v1.1 executable is
`cf442e369c56c7c06c23b33c25b3434b079398b479e188c47e03f2d76dfbc291`.
Canonical Release validation/publish passed with no vulnerable packages,
resource stability PASS (`900` samples, `30` cells, `720` pairs, flat
counters), native ABI PASS, recovery/support/privacy/version/publish PASS,
and strict OpenSpec `38/38`. Final supervised visual packets are healthy
`VISUAL_OK`, seeded defect attempt 1 `VISUAL_DEFECT` followed by attempt 2
`VISUAL_OK`, flight `VISUAL_OK`, and required-review unavailable
`REVIEW_UNAVAILABLE`; all packet/result checks passed and screenshot tamper
was rejected. These packets and the executable remain bound to `6bb8`; later
evidence-record commits do not retag them.
The canonical workflow is `.agent/workflows/visual-evidence-review.md`.

### Milestone A attempt — supervised desktop preflight

An exact pre-final candidate was built from clean source commit
`ef49d9a60201a55fa34ae5653a76a0b73cd5a2f6`, project version `1.0.0`,
embedded identity matching that SHA, and executable SHA-256
`e5ce77d5098952245586ed3e091426b8069f95ff43f522e5932fc3860ccb701b`.
This is an A-run candidate only, not the final v1.1 candidate. The native ABI,
strict OpenSpec `38/38`, and release qualification passed.

The first supervised healthy-packet attempt was run with the exact executable
and Release driver on `2026-09-01`, run ID
`87470fb8-50c6-47cc-98eb-e0055363493b`. It failed closed as
`FAIL_HARNESS` before scenario setup because the lease could not establish a
verified TabDock foreground target: a Chrome window and an unrelated
`nightwatch` window covered every tested monitor point. The driver killed only
its own TabDock process and restored both user state snapshots. No healthy,
defect, flight, or review claim is made from this attempt. A rerun requires the
operator to clear/minimize unrelated desktop windows and leave the desktop
untouched during each supervised scenario.

### Milestone A — exact supervised qualification

Milestone A was completed on the exact current pre-final candidate
`b19e33e926d751c5be26a4684265a5cd368cef34`. The Release qualification build
passed with project version `1.0.0`, embedded source identity matching that
SHA, and executable SHA-256
`b6418a949c08ac3d50e6460aaeb6ce3d01df545b95553efc4d98ca1a9cb031c7`.
Signing remains `NOT_CONFIGURED`; this is qualification evidence, not a
production-release authorization.

After supervised desktop clearing, the exact healthy packet passed: run
`96000be6-32c5-4fd4-860f-fc31bca5cae6`, packet
`visual-a8-healthy/.../visual-review-manifest.json`, packet SHA-256
`daf8a966725e0fdd429d13e495fcb9d26c0139b8d9f2ec1430fb0901dea75633`.
Its native outcome was `PASS`, desktop lease was `true`, and the capable
multimodal review returned `VISUAL_OK` after inspecting the contact sheet and
all five raw checkpoint images.

The seeded first-attempt defect and rerun were preserved in one exact run
`c41e4302-b336-40f3-9006-9b500da8d010`. Attempt 1 packet SHA-256
`f76ea3a8382bb23d792ac0065e48c094cd23a3a54d45502c0540417ca8a4d964` was
native `PASS`, lease `true`, and visually `VISUAL_DEFECT` because every
retained frame showed the deliberate red GuineaPig instead of the expected
white guest. Attempt 2 packet SHA-256
`688f052e1523fde529e632dd170c4e2fc5ac5e9d53ddeb32654be63785741958` was
native `PASS`, lease `true`, and `VISUAL_OK`; its five raw frames were
inspected. Both result files passed strict packet/result verification.

The exact flight packet passed with run
`3071f78b-7988-4c35-95e0-b4c96dd23a52`, packet SHA-256
`2b54a390acd9d2b621f2a9f4ef7c2c2de42d89e53100d15f9264fc13d871681c`.
Native outcome and lease were `PASS`/`true`; the driver retained three
failure artifacts, flushed two pre-trigger frames, stopped with ring count
zero, and the capable multimodal review inspected all eight raw images and
returned `VISUAL_OK`.

The exact unavailable-capability packet passed native qualification with run
`d07e2795-9f05-4cbd-91ca-b8dd9d628e1b`, packet SHA-256
`442ebd7e626500fd68bbf85bd35cc549a2b37d78eb8a51803fcc1ab4ba6f43f2`. Its
result is explicitly `REVIEW_UNAVAILABLE` with empty review collections and
passed strict verification; the gate reflection check maps required review
to `BLOCKED_CAPABILITY`. A copied packet with one PNG byte tampered was
rejected by the verifier with visual evidence hash mismatches.

The native/visual precedence checks were exercised against the Release
driver: native `FAIL_HARNESS` and `BLOCKED_SUPERVISED` remained effective
under `VISUAL_OK`; required `VISUAL_DEFECT` mapped `PASS` to `FAIL_PRODUCT`;
required `REVIEW_UNAVAILABLE` mapped `PASS` to `BLOCKED_CAPABILITY`; optional
defect left native `PASS` effective while `visualPass=false`; and required
`VISUAL_OK` produced release `PASS`. Focused visual unit tests passed `35/35`
after the flight-ring capacity correction.

The preceding section closes the supervised A evidence for the b19 pre-final
candidate only. The final-source rerun below is separately bound to
`6bb8ecc80b103ec9e2e1bc12cebe241b1ab9519f`; no packet was retagged.

### Milestone A — final-source supervised requalification

The exact final-source packets were run against the self-contained
`.artifacts/release-final/TabDock.exe` and Release GuineaPig. Healthy run
`1ca035cc-b73f-489b-8280-58c5ca713636` produced packet
`98ebd8186c44325449b72cb4042701d001df9620e9c3568bce8b8e6a3d555bd0` and
`VISUAL_OK`. Seeded defect run
`9ad16a31-52cc-4018-b44b-24c3355901a7` preserved attempt 1 packet
`ae2bd8143d78694cd4631636ad8170cfa2aa482a0accdcb5d6920261cd5ac6ac` as
`VISUAL_DEFECT`; attempt 2 packet
`12385900ee0770c0faae03b98f168a25812da2ff11d4f729b4fb49e03d859c37` was
`VISUAL_OK`. Flight run
`2cb0f55c-6437-4379-9599-f2ab74a26ad6` produced packet
`51c4b244a5766735440ef5ce1410d3e5f469b690688e7dfbc331f7c163349a3c` and
`VISUAL_OK`. Unavailable-review run
`ce628549-9cf9-4c39-a426-0c44195dfb15` produced packet
`b22fcaf135284605bc346ba85000e76339be065ac1b3478a4e70ddfa91198e25` with
`REVIEW_UNAVAILABLE`, empty review collections, and required-gate
`BLOCKED_CAPABILITY`. All final packet/result records passed strict
verification. The tampered healthy packet was rejected by a visual evidence
hash mismatch. Native `FAIL_HARNESS`/`BLOCKED_SUPERVISED` remained effective
under `VISUAL_OK`; required defect/unavailable remained non-pass.

Final candidate evidence is complete for the exact source boundary. Evidence
record `de306d39722b9b67548b8e4878f228a97ef4e706` was pushed and matched
`origin/main` before the current-change archive. Archive/spec commit
`496b0c1e498c06b5d86211c9e24ec1ae62349eef` is now created; final E6.6 task
closure and final parity proof remain open.



## Chosen next campaign — visual evidence closure and performance requalification

**Status:** A and B evidence are complete, both the predecessor and successor
changes are dispositioned and archived, and final E 6.2–6.5 evidence is
recorded against source candidate
`6bb8ecc80b103ec9e2e1bc12cebe241b1ab9519f`. The final self-contained Release
v1.1 artifact is
`cf442e369c56c7c06c23b33c25b3434b079398b479e188c47e03f2d76dfbc291`.
The candidate is `QUALIFICATION_ONLY`; signing is `NOT_CONFIGURED` and
production eligibility is `BLOCKED_EXTERNAL`. Physical mixed-DPI, external
Windows-human, real-app, and any unavailable capability gates remain blocked
and are not relabeled as PASS.

Final E evidence includes Debug/Release solution builds with 0 warnings/errors,
`795/795` Debug/Release unit tests, ValidationDriver selftests `153/153`,
catalog `scenario-catalog-2026-08-24-v1` with 135 scenarios, `179/179`
release-tooling tests, canonical Release validation/publish PASS, strict
OpenSpec `38/38` before archive and `38/38` after archive, resource stability
PASS with 900 samples/30 cells/720 pairs and flat counters, native ABI PASS,
and recovery/support/privacy/version smoke PASS. The final visual packets
preserve healthy PASS, first-attempt seeded defect, successful rerun, flight
flush, unavailable review, packet tamper rejection, and native/visual
precedence.

Successor E6.6 is complete in the archived task ledger: predecessor and
successor changes are synchronized/archived, archive/spec commit
`496b0c1e498c06b5d86211c9e24ec1ae62349eef` is recorded, and the final closure
record was pushed through `main`. The final binary candidate boundary remains
the exact source SHA above; later evidence/archive records do not retag its
packet or executable hash. Post-push `HEAD == origin/main` and clean-tree
status were independently verified.

Final branch deletion, push identity, clean worktree, and remote topology must
always be re-proven dynamically rather than inferred from this file.
