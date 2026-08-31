# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session. This file never records a
self-referential SHA or treats an old CI run as evidence for the commit that
contains this text.

## CURRENT CAMPAIGN — PRESENTATION-INTEGRITY PHYSICAL CERTIFICATION

**Objective:** physically certify the original chrome-occlusion, title-centering,
guest-escape, and z-order reports on the user-granted exclusive supervised
Windows desktop; repair only a defect actually reproduced under that lease.

**Plan:** `openspec/changes/2026-08-31-presentation-integrity-physical-certification/`

**Status:** supervised physical certification is complete for the exercised
matrix with one honest adjacent `split-exit` environment block. The
continuation proved one valid product defect in browser F11 presentation,
froze its first failure, applied the smallest Shepherd-preserving repair, and
requalified Chrome, Edge, and Brave. Four new physical cells and nine fresh
adjacent cells passed; three `split-exit` attempts failed closed on foreground
or foreign-point qualification and remain `BLOCKED_ENVIRONMENT`.
Detailed evidence:
`.agent/investigations/presentation-integrity-physical-certification-rerun-2026-08-31.md`.

### Current phase

- Orientation: complete — the continuation started from dynamically verified
  `main` at `b0975b2a724f0cf9551c4e106dfc6449c8643002`; this state file is not
  evidence of that commit containing itself.
- Lease: complete for accepted runs — native probes reported interactive,
  unlocked, `SendInput`-available desktop; accepted timelines proved
  candidate process/HWND identity, owned/adopted provenance, `WindowFromPoint`
  → `GA_ROOT`, foreground continuity, point ownership, and cleanup identity.
  The user statement is the supervision evidence; no guard was bypassed.
- Foreground harness: complete — shared fail-closed qualification and 10
  deterministic seam tests are in place. The accepted physical runs use the
  active desktop lease and bounded guarded input.
- Residual product repair: complete and requalified — Chrome F11's valid
  `FAIL_PRODUCT` (`7f5ba57f-af6e-491e-81aa-b33bd8229471`) showed a
  borderless browser refusing the assigned pane because of its native
  full-monitor minimum. Shepherd now posts one identity-checked browser F11
  repair, suppresses duplicates until `LOCATIONCHANGE`, then reapplies the
  pane. Chrome `71656457-b555-42ce-a782-b4947f33f292`, Edge
  `9cb2ad2a-a6bc-41a2-b196-2492702b9331`, and Brave
  `01c74b6f-fb28-4ab6-acef-ab3c2f3ab4d6` passed after the repair. The Edge
  qualifier failure `0e559627-46f5-463c-9560-ec64913fb3d0` and intermediate
  timing failure `443624df-51bb-4801-a8ad-249b498302b5` remain raw evidence.
- Physical matrix: complete for the newly added cells — GuineaPig caption
  maximize `01d148a1-f381-4023-87f4-a6c2e6e2371f`, Win+Up
  `708fafaa-5da3-48ca-87c1-4daa0d4d77e5`, dual-monitor mixed-DPI
  `f423509e-f869-4097-b938-964355bd9101`, topmost interaction
  `f3ee2adb-2d4b-46ce-973f-ccbf789e5aca`, controlled LOCATIONCHANGE load
  `345e33f8-8086-4819-9e5f-72acbdec45ed`, and title centering
  `1fbc4b0c-f8a8-4dd5-adf4-f547509d9b19` all passed. The physical environment
- Adjacent regression: complete for the requested subset — rename,
  group-menu, add-window, inline-capture, bidirectional split-focus,
  context-menu, Chrome-click, direct foreground-pairing, and drag-reorder
  passed in fresh Release runs. `split-exit` was retried three times and
  remained `BLOCKED_ENVIRONMENT` only when the fail-closed foreground/point
  guard refused input; the earlier accepted split-exit pass remains evidence.
- Documentation/gates: evidence and OpenSpec records are reconciled. Final
  Debug/Release builds, 732/732 unit tests in each configuration, Release
  driver/fixture builds, 153/153 deterministic selftests, 135-entry catalog
  listing/plans, strict OpenSpec, and CI-safe validation all passed.

### Verified environment

- Windows 11 Pro family, raw product label Windows 10 Pro, 25H2 build 26200
  revision 9278; .NET 8.0.30; standard-user session 1.
- Primary `(0,0)-(1920,1200)`, work `(0,0)-(1920,1140)`, 120 DPI/125%;
  secondary `(1920,0)-(3840,1080)`, work `(1920,0)-(3840,1032)`, 96
  DPI/100%; no negative-coordinate monitor.
- Chrome, Edge, Brave, Windows Terminal, and Notepad available; Firefox
  unavailable. `stageBAvailable=false`; candidate signing not configured.
- Pre-integration Release executable SHA-256:
  `4614471803119C6D23308A20F3386A9C83B49969247F6717419E8A442737217D`;
  its embedded source identity is the verified pre-commit
  `b0975b2a724f0cf9551c4e106dfc6449c8643002`. A post-integration
  build/smoke is required after the authorized commit.

- Debug and Release solution builds: PASS, 0 warnings/0 errors.
- Debug and Release unit tests: PASS, 732/732 each.
- Release ValidationDriver/GuineaPig builds: PASS, 0 warnings/0 errors.
- Selftest `4588e75b-f822-45b3-b300-31ac6abb1100`: PASS, 153/153;
  catalog listing/plans: PASS, 135 dispatchable scenarios.
- Strict OpenSpec validation: PASS, 37/37. CI-safe Release validation:
  PASS, including no-vulnerability audit, resource, ABI, privacy, recovery,
  publish, and version smokes; no desktop input.

### Safety and evidence rules

- Preserve raw first failures; never best-of-N a valid failure into PASS.
- Never weaken `WindowFromPoint`/`GA_ROOT`, foreground, process-start, HWND
  generation, provenance, local z-order, or cleanup protections.
- Generated artifacts, logs, caches, machine paths, credentials, and secrets
  stay out of Git.
- The active OpenSpec change is not safe to archive while major requested
  physical cells remain blocked.


### Next action
Commit the authorized working-tree changes, push authoritative `main`, then
rebuild and smoke the pushed candidate so its embedded SHA is current. Verify
clean `HEAD == origin/main`; report the honest split-exit environment block
and preserve all raw artifacts. Do not archive the active change.
