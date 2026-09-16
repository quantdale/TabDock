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
- The last substantive implementation commit is
  `6172595cffcce2e2eee506c3eb9ea1791afa7fa8`; subsequent campaign commits are
  documentation-only and do not change application behavior. The current
  final-tree Release artifact was rebuilt from the exact tree, hash-verified,
  launched, and exercised through the final supervised persistence,
  drag/reorder, crash/relaunch, split foreground-pairing, Spotify, textbox,
  popup, overlap, and four-guest soak scenarios. Exact artifact identity is
  reported at campaign handoff rather than embedded here as a self-referential
  current-tree SHA.
- The final-tree four-guest soak measured sixteen switches at 406–503 ms
  (mean 428 ms) with no blank content. Popup-open split panes and the
  bidirectional overlap/z-order matrix remained correct. Exact-SHA hosted CI
  for the final tree passed both `build` and `native-abi-evidence`.
- `repowise update` reports the local index current; `git diff --check` passes
  with only the repository's LF-to-CRLF normalization warnings.

## Handoff

Campaign complete for the evidence available on this desktop. `main` and
`origin/main` are aligned at the verified final tree, and the pre-existing
`.codex/config.toml` edit remains preserved and excluded. Mixed-DPI and
Windows 10 physical repeats, the full physical ValidationDriver catalog, and
production Authenticode signing remain unverified or external qualification
items.
