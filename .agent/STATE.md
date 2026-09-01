# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session. This file never records a
self-referential SHA or treats an old CI run as evidence for the commit that
contains this text.

# CURRENT CAMPAIGN — VISUAL EVIDENCE AND AI REVIEW

**Objective:** implement and validate the active
`2026-08-31-visual-evidence-ai-review` OpenSpec change as bounded
ValidationDriver evidence infrastructure. Preserve native identity, lease,
timeline, cleanup, and release-gate semantics; do not change production
TabDock behavior.

**Status:** the predecessor `2026-08-31-visual-evidence-ai-review` campaign
remains active and unarchived. Its canonical ledger contains **178** rows:
**93** are checked as `COMPLETED_AND_PROVEN` after the 2026-09-01
reconciliation, and **85** remain unchecked with one explicit classification
each. The complete row-level evidence is in
`.agent/investigations/visual-evidence-ledger-reconciliation-2026-09-01.md`.
Those 85 classifications are not yet the required final dispositions; every
row still needs exactly one allowed disposition before predecessor archive.
The historical 84/89 implementation roll-up was not a mapping to the
canonical rows and is not a valid checkbox count. Remaining implementation,
acceptance, supervised, capability, documentation, archive, and provenance
obligations remain open; the prior presentation-integrity physical
qualification remains historical evidence and is not relabeled.
Foundation inventory, implementation status, and the Release candidate
correction are recorded in
`.agent/investigations/visual-evidence-implementation-2026-09-01.md`,
`.agent/investigations/visual-evidence-ledger-reconciliation-2026-09-01.md`,
and `.agent/investigations/release-semantics-correction-2026-09-01.md`.
No archive, final v1.1 candidate, final hash, push, or clean-tree claim is
made by this status update.

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

The visual-evidence and multimodal AI-review change is active under
`openspec/changes/2026-08-31-visual-evidence-ai-review/`. Its canonical source
checklist is the reconciled 178-row ledger: 93 proven rows are checked and 85
remain explicitly classified and unchecked pending final row-level
disposition. The implementation checkpoint, reconciliation matrix, and
Release-semantics correction are in
`.agent/investigations/visual-evidence-implementation-2026-09-01.md`,
`.agent/investigations/visual-evidence-ledger-reconciliation-2026-09-01.md`,
and `.agent/investigations/release-semantics-correction-2026-09-01.md`.
Current deterministic evidence includes 57/57 visual-filter unit tests,
789/789 total unit tests, 179/179 PowerShell 7 release-tooling tests, and
successful Debug/Release solution builds and ValidationDriver deterministic
self-tests (153/153). This is retained pre-final qualification evidence, not
final v1.1 closure. The successor Milestone A deterministic closure is
implemented: exact packet-byte hashing, strict current collections,
derived-failure propagation, scenario/run hierarchy links, and offline bundle
verification. Milestone B is implemented: paired
disabled/checkpoints/flight measurement harness, portable report with
per-cell median/p95/max, conservative budget derivation (25% margin,
provenance), disabled/enabled/paired resource gates, and healthy-discard/flush
lifecycle proof. Milestone E visual-manifest/packet/budget integration into
the qualification hierarchy and bundle index is implemented with historical
non-visual compatibility. Successor E tasks 6.2–6.5 retain the `ef9fe35`
execution evidence only as pre-final evidence; their final rerun is pending.
In particular, 6.3 requires a fresh exact-candidate build after supervised A,
all 85 predecessor dispositions, predecessor archive if justified, and every
final metadata/spec commit. Supervised healthy/defect/flight visual acceptance
(A §3) and predecessor disposition are still open. The canonical workflow is
`.agent/workflows/visual-evidence-review.md`.

## Chosen next campaign — visual evidence closure and performance requalification

**Status:** implementation is in progress and deterministic closure is proven
only as pre-final evidence. The planning-only skeleton was strictly validated,
predecessor reconciliation is complete at 93/178 checked with 85 explicit
remainder classifications, and successor tasks 1.1–1.4, 2.1–2.5, 4.1–4.6,
5.1–5.5, 6.1, and 7.1 are checked from source/tests. Successor E tasks 6.2–6.5
are reopened in the ledger for final rerun; the recorded `ef9fe35` candidate
and hashes are historical/pre-final only, and `7f4b9df` has no claimed exact
binary candidate. Milestone A supervised evidence tasks (3.1–3.7) remain
pending until an exclusive supervised desktop and capable multimodal review
produce retained healthy, defect, and flight packets with hash-bound results,
tamper rejection, native/lease precedence, and first-attempt preservation.
Milestone B synthetic headless qualification is proven; physical
visual-evidence measurements remain separate and are not relabeled as
synthetic PASS. Milestone E final exact-candidate Release closure remains
pending until A, predecessor disposition/archive, final metadata/spec commits,
and the fresh final v1.1 build/gates settle.
The skeleton contains 34 implementation tasks and six required spec deltas.
The campaign explicitly defers DPI/topology hardening and real-app hardening
to separate future campaign candidates; neither is hidden in this scope.
The planning artifacts preserve the packet-hash, strict-collection,
provenance, and archive-boundary decisions from the reconciliation and
successor investigations.

**PLANNED/OPTIONAL:** repository-local Microsoft Learn and Context7 MCP
integration, subject to a separately authorized upstream/security/scope
revalidation. No global installation is authorized by this state.

Final branch deletion, push identity, clean worktree, and remote topology must
always be re-proven dynamically rather than inferred from this file.
