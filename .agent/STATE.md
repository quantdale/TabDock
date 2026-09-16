# TabDock agent state

## Objective and status — 2026-09-17

Release-finalize TabDock from the four beta observations using current
Windows evidence. The implementation, automated gates, exact Release
physical qualification, supervised targeted ValidationDriver scenarios,
final-tree artifact smoke, mainline integration, and exact-SHA hosted checks
are complete for the available single-monitor desktop. The remaining items
are external or unperformed qualification limits: mixed-DPI and Windows 10
physical repeats, the full physical ValidationDriver catalog, and production
signing.
Active plan: `.agent/plans/release-finalization-beta-feedback-2026-09-16.md`.
Investigation: `.agent/investigations/beta-feedback-baseline-2026-09-16.md`.

## Important facts

- Starting Git state was clean on `main` at
  `9b84a2a3f16b8cccf3bf18f6cc8fe045e0967229`, equal to `origin/main`; the
  pre-existing `.codex/config.toml` change is preserved and excluded from the
  campaign implementation.
- Baseline physical evidence confirmed popup-open guest coverage by the opaque
  container. Spotify capture/release and the reported clipping were not
  reproduced. A fresh Debug overlap sequence passed native foreground,
  z-order, point-ownership, and inverse always-on-top checks.
- The implementation commit `6172595cffcce2e2eee506c3eb9ea1791afa7fa8` covers
  popup-local z-order reconciliation, hide/minimize provenance ordering,
  active-switch foreground ownership, measured split stack reassertion, and
  stale delayed foreground callbacks. Shepherd remains never-reparent. Exact
  self-contained Release artifacts built from the integrated implementation
  were exercised with
  real GuineaPig guests, the installed Spotify window, popup paths, overlap
  transitions, textbox input, minimize/restore, maximize/restore, and a
  four-guest switching soak.
- Current desktop state is one 1920x1080 monitor at 96 DPI. The earlier mixed
  120/96-DPI topology is unavailable for final repetition. An earlier
  ValidationDriver attempt was correctly fail-closed by the occupied-desktop
  lease, but the exact final Release artifact later passed the targeted
  split-workspace foreground-pairing, persistence, drag-reorder, and
  crash/relaunch scenarios.

## Completed and validation

- Debug solution, ValidationDriver, and GuineaPig builds: 0 warnings/errors.
- Full Debug xUnit: 847/847. Targeted hide-provenance and interaction source
  contracts: 25/25.
- Canonical pre-integration Release/CI/publish gate: NuGet audit clean,
  Release builds clean, xUnit 847/847, headless resource lifecycle pass,
  native ABI/CLI/support-bundle privacy pass, OpenSpec 38/38, and single-file
  publish/version smoke pass.
- Final exact-tree Release artifact: `artifacts/release-final-37536b2`;
  version `1.1.0`, source SHA
  `37536b289273cb0eb78b17c8c7ca277bcfd6cc69`, 180,310,748 bytes, SHA-256
  `05729E907B47CCD8B394200CD8DC4AE9F6B84FD27134C8756EBA697B60646F37`.
  It passed the final supervised persistence, drag/reorder, crash/relaunch,
  and split foreground-pairing scenarios; the four-guest soak measured
  sixteen switches at 406–503 ms (mean 428 ms) with no blank content.
- The final artifact was launched with the installed Spotify window and a
  Release GuineaPig textbox; Spotify rendered through capture/switch/
  release, and the textbox accepted real input without clipping. Popup-open
  split panes and the bidirectional overlap/z-order matrix also remained
  correct.
- Exact-SHA hosted CI run `35142807839` passed for source commit
  `37536b289273cb0eb78b17c8c7ca277bcfd6cc69`; both `build` and
  `native-abi-evidence` check-runs were successful.
- `repowise update` reports the local index current; `git diff --check` passes
  with only the repository's LF-to-CRLF normalization warnings.

## Handoff

Campaign complete for the evidence available on this desktop. `main` and
`origin/main` are aligned at the verified final tree, and the pre-existing
`.codex/config.toml` edit remains preserved and excluded. Mixed-DPI and
Windows 10 physical repeats, the full physical ValidationDriver catalog, and
production Authenticode signing remain unverified or external qualification
items.
