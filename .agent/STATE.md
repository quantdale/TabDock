# TabDock agent state

## Git authority

Git is authoritative for `HEAD`, branch, `origin/main`, and worktree state.
Resolve them dynamically at every fresh session. This file never records a
self-referential SHA or treats an old CI run as evidence for the commit that
contains this text.

## CURRENT CAMPAIGN — PRESENTATION-INTEGRITY PHYSICAL CERTIFICATION

**Objective:** physically certify the original chrome-occlusion, title-centering,
guest-escape, and z-order reports on an exclusive supervised Windows desktop;
repair only a defect actually reproduced under that lease.

**Plan:** `openspec/changes/2026-08-31-presentation-integrity-physical-certification/`

**Status:** physical certification is honestly blocked before input; read-only
environment evidence and deterministic gates are captured; a smallest test-only
GuineaPig topmost switch and one stale self-test expectation were corrected.
Final candidate identity is captured at the last substantive campaign commit
`a131a9f8ec9810a4015db2bd935cdda749f9f278`; push verification and final
reconciliation remain.

### Current phase

- Orientation: complete — current start `HEAD`/`origin/main` was
  `4aaf3fcaa72edf48865030db43bccf7bd50e21b8` on `main` with a clean worktree;
  required guidance, implementation change, canonical specs, and testing
  procedures were read before edits.
- Safety/evidence: complete — the native probes report an interactive unlocked
  session, but this agent cannot prove human-exclusive desktop ownership or a
  supervised operator lease. No `SendInput`, capture, window mutation, or
  physical scenario was issued. Record:
  `.agent/investigations/presentation-integrity-physical-certification-2026-08-31.md`.
- Fixture: complete — `tests/ValidationDriver/TabDock.GuineaPig` now accepts
  `--topmost`, maps it to `Form.TopMost`, carries it to extra windows, and logs
  the setting. This is qualification-only; production behavior is unchanged.
- Validation: deterministic gates currently pass — Debug/Release solution builds,
  725/725 Debug and Release unit tests, Release ValidationDriver/GuineaPig
  builds, catalog listing, and ValidationDriver selftests 143/143. The first
  selftest run was retained as `FAIL_HARNESS` because `CAT01` still expected
  127 instead of the catalog's 128; the test-only expectation was corrected.
- Strict OpenSpec validation currently passes 37/37; rerun after final edits.

### Deterministic gate evidence

- Corrected selftest runId:
  `b9c42048-872a-4799-b8ae-442e9a57bb89`, 143/143, deterministic-all PASS,
  run-manifest PASS.
- Preserved first harness failure runId:
  `e66a69b1-c5a8-4664-92c1-92a60f9ca2a3`, CAT01 failure, 142/143, exit 21.
- ValidationDriver catalog: `scenario-catalog-2026-08-24-v1`, 128 dispatchable
  scenarios. No physical scenario was launched.

- CI-safe Release pipeline passed NuGet audit, resource stability, native ABI,
  recovery, support-bundle privacy, and publish smoke without a scenario
  argument or desktop input.

### Verified environment facts

- Final campaign candidate: commit
  `a131a9f8ec9810a4015db2bd935cdda749f9f278`; Release executable rebuilt after
  commit; embedded SHA matches; executable SHA-256
  `4E5EF396EE585FC02C5C5632F854B78DA3BF37AACA4C8000F72EABC92F1B2103`.
- Pre-commit starting candidate identity was
  `4aaf3fcaa72edf48865030db43bccf7bd50e21b8` with executable hash
  `3C542DC37BE449923539AE169E646A741B781A510E811DA7CCE966BBBCF7D786`.
- `--doctor`: Windows 11 family, raw product label Windows 10 Pro, 25H2,
  build 26200 revision 9278, .NET 8.0.30, standard-user, session 1.
- Two physical monitors: primary `(0,0)-(1920,1200)`, work
  `(0,0)-(1920,1140)`, 120 DPI/125%; secondary
  `(1920,0)-(3840,1080)`, work `(1920,0)-(3840,1032)`, 96 DPI/100%.
- Chrome, Edge, Brave, Windows Terminal, and Notepad are available by
  capability/path probes; Firefox is unavailable.
- No campaign-owned guest/container/popup HWND exists. Prior doctor/log
  observations are not physical scenario evidence.

### Campaign disposition

- Color menu, rename, split, inline `+`, guest caption maximize, Win+Up, real
  F11, dual-monitor transfer, mixed DPI, unrelated foreground, LOCATIONCHANGE
  load, and physical title centering are `BLOCKED_SUPERVISED` before input.
- Topmost guest coverage is now fixture-capable but remains
  `BLOCKED_SUPERVISED` before input.
- No valid `FAIL_PRODUCT` occurred; no production repair was justified.
- Deterministic and synthetic evidence remains separate from physical
  certification. The original implementation change must not be described as
  physically certified.

### Validation and safety rules

- No guarded `SendInput` or blind desktop automation without a proven
  exclusive supervised lease.
- Preserve `WindowFromPoint` → `GA_ROOT`, foreground, process-start, HWND
  generation/identity, local z-order, and cleanup protections.
- A valid first physical failure remains authoritative; a later pass cannot
  become best-of-N PASS.
- Keep generated artifacts, logs, caches, machine paths, credentials, and
  secrets out of Git.

### Next action

Rerun strict OpenSpec validation after these final evidence edits, commit the
final candidate evidence update, push `main`, verify `HEAD == origin/main`,
then mark this campaign complete only as a physically blocked certification.
