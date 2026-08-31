# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session. This file never records a
self-referential SHA or treats an old CI run as evidence for the commit that
contains this text.

## CURRENT CAMPAIGN — PRESENTATION CLOSURE AND REPOSITORY CONSOLIDATION

**Objective:** close the presentation-integrity physical-certification campaign
against an exact current-main build, preserve every valuable side-branch plan
on `main`, and leave only `main` locally and on GitHub.

**Status:** presentation-integrity implementation and supervised physical
qualification are complete for the exercised matrix. The only valid physical
`FAIL_PRODUCT` is the frozen first Chrome F11 run
(`7f5ba57f-af6e-491e-81aa-b33bd8229471`); its bounded Chromium-family F11
repair was requalified by Chrome
(`71656457-b555-42ce-a782-b4947f33f292`), Edge
(`9cb2ad2a-a6bc-41a2-b196-2492702b9331`), and Brave
(`01c74b6f-fb28-4ab6-acef-ab3c2f3ab4d6`). The accepted split-exit PASS
(`03c3aaa2-37d1-41bd-9382-98fcd2d470ad`) remains evidence; later
fail-closed attempts (`58f04b3a-ffbc-4b7b-aa56-b1ee5c32a9fd`,
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

### Exact-mainline qualification

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
The visual-evidence plan is preserved as future qualification/diagnostic
infrastructure. It augments hard identity/lease/native evidence and does not
retroactively justify or relabel physical results.

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

### Next campaign

**COMPLETED:** presentation-integrity implementation and physical certification
for the exercised matrix.

**PLANNED:** visual-evidence and multimodal AI-review infrastructure under
`openspec/changes/2026-08-31-visual-evidence-ai-review/`. Its tasks remain
unchecked and are not implemented by this consolidation.

**PLANNED/OPTIONAL:** repository-local Microsoft Learn and Context7 MCP
integration, subject to a separately authorized upstream/security/scope
revalidation. No global installation is authorized by this state.

Final branch deletion, push identity, clean worktree, and remote topology must
always be re-proven dynamically rather than inferred from this file.
