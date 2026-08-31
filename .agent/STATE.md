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

**Status:** partial physical certification concluded for this desktop session.
The user supplied exclusive supervision and the driver accepted valid native
leases for bounded runs. Several supported paths passed. The physical phase
stopped after two consecutive generic foreground-setup failures; no valid
physical `FAIL_PRODUCT` occurred and no production repair was justified.
Detailed rerun evidence:
`.agent/investigations/presentation-integrity-physical-certification-rerun-2026-08-31.md`.

### Current phase

- Orientation: complete — campaign candidate at orientation was
  `dd5f819484498b1e74678710bde58d55fbdcf8fa` on `main`; required guidance,
  architecture/testing references, active OpenSpec artifacts, and prior
  blocked evidence were read before edits.
- Lease: complete for accepted runs — native probes reported interactive,
  unlocked, `SendInput`-available desktop; accepted timelines proved candidate
  process/HWND identity, owned/adopted provenance, `WindowFromPoint` →
  `GA_ROOT`, foreground continuity, point ownership, and cleanup identity.
  The user statement is the supervision evidence; no guard was bypassed.
- Physical rerun: stopped safely — after valid runs, two fresh `dragreorder`
  attempts (`93118e80-2faf-45ed-aa69-81d32847088a` and
  `c31afe50-d5dd-4a26-b20f-336b71b96f64`) failed at the unchanged generic
  `StartScenario` foreground gate before setup/input. The known
  Windows/sandbox foreground-arrangement limitation applies. No further
  physical input was sent.
- Harness correction: complete — only ValidationDriver scenario guards/live
  UIA reacquisition were changed in `Scenarios.Core.cs`, `Scenarios.Split.cs`,
  and `Scenarios.Drag.cs`; production TabDock source is unchanged. Old raw
  failure artifacts remain preserved and are analytically classified in the
  rerun report.
- Disposition: complete — supported rename, tabswitch, split, inline capture,
  menu, Chrome-click, direct foreground, resize, and synthetic/container
  maximize paths passed. Color selector, physical guest caption/Win+Up, F11,
  transfer/mixed-DPI measurement, topmost, load, and title-centering cells
  remain capability/environment blocked; synthetic evidence is not promoted.

### Verified environment

- Windows 11 Pro family, raw product label Windows 10 Pro, 25H2 build 26200
  revision 9278; .NET 8.0.30; standard-user session 1.
- Primary `(0,0)-(1920,1200)`, work `(0,0)-(1920,1140)`, 120 DPI/125%;
  secondary `(1920,0)-(3840,1080)`, work `(1920,0)-(3840,1032)`, 96 DPI/100%;
  no negative-coordinate monitor.
- Chrome, Edge, Brave, Windows Terminal, and Notepad available; Firefox
  unavailable. `stageBAvailable=false`; candidate signing not configured.
- Candidate Release executable hash at orientation:
  `D2BC99361705240FD1EAB14784D7AA3807AFB1F6F00F870B3C982EE2C3E106A9`.

### Deterministic validation

- Debug/Release solution builds and Debug/Release unit tests passed;
  unit tests were 725/725 in each configuration.
- Release ValidationDriver/GuineaPig builds passed with zero warnings/errors.
- Catalog is `scenario-catalog-2026-08-24-v1` with 128 dispatchable scenarios.
- Latest recorded ValidationDriver selftest:
  runId `e7b8e777-be37-4396-bdcd-67b79cd80639`, 143/143, deterministic-all
  PASS and run manifest PASS.
- CI-safe `scripts/validate.ps1 -Configuration Release -Ci -Publish` passed
  NuGet audit, resource stability, native ABI, recovery, privacy, Release
  tests/builds, OpenSpec validation 37/37, and publish version smoke without
  scenario input.

### Safety and evidence rules

- Preserve raw first failures; never best-of-N a valid failure into PASS.
- Never weaken `WindowFromPoint`/`GA_ROOT`, foreground, process-start, HWND
  generation, provenance, local z-order, or cleanup protections.
- Generated artifacts, logs, caches, machine paths, credentials, and secrets
  stay out of Git.
- The active OpenSpec change is not safe to archive while major requested
  physical cells remain blocked.

### Continuation checkpoint

- Supervised physical evidence through `b0975b2a724f0cf9551c4e106dfc6449c8643002`
  is retained as partial certification; supported rename/split/inline-capture/
  context-menu/direct-foreground paths passed.
- No valid physical `FAIL_PRODUCT` exists and production TabDock remains
  unchanged.
- Remaining work is qualification capability completion, not architecture
  redesign: stable generic foreground qualification, guest-caption maximize,
  Win+Up, real F11, dual-monitor/mixed-DPI transfer, topmost dispatch,
  LOCATIONCHANGE load and title-centering measurement.
- The active OpenSpec task ledger now tracks those cells as unchecked
  continuation work rather than incorrectly treating the initial blocked
  campaign as the final state.

### Next action

Continue the active physical-certification OpenSpec from the latest mainline.
First stabilize the shared fail-closed foreground qualification path using
deterministic harness tests; then add and run the missing physical scenarios.
Do not change production code unless a newly executable physical cell yields a
valid, retained `FAIL_PRODUCT`.
