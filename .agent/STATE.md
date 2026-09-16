# TabDock agent state

## Objective and status — 2026-09-17

Release-finalize TabDock from the four beta observations using current
Windows evidence. The implementation, automated gates, exact Release
physical qualification, final-tree artifact smoke, mainline integration, and
exact-SHA hosted checks are complete for the available single-monitor desktop.
The remaining items are external qualification limitations only: a mixed-DPI
repeat and the supervised ValidationDriver foreground lease.
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
  120/96-DPI topology is unavailable for final repetition. ValidationDriver
  split foreground qualification was attempted and correctly blocked by its
  occupied-desktop foreground lease; it is not a product pass.

## Completed and validation

- Debug solution, ValidationDriver, and GuineaPig builds: 0 warnings/errors.
- Full Debug xUnit: 847/847. Targeted hide-provenance and interaction source
  contracts: 25/25.
- Canonical pre-integration Release/CI/publish gate: NuGet audit clean,
  Release builds clean, xUnit 847/847, headless resource lifecycle pass,
  native ABI/CLI/support-bundle privacy pass, OpenSpec 38/38, and single-file
  publish/version smoke pass.
- Exact implementation Release artifact: `artifacts/release-final-4f4998ff`;
  version `1.1.0`, SHA-256
  `3225289BF6A3EB28B9941D0B93F5C38694B53C5E5D81A30F3890A1B417F06CE1`.
  Sixteen real switches across four captured guests measured 406–503 ms
  per switch (mean 428 ms); no blank content was observed.
- Evidence-closure tree `3e72a94ce4f79dddd41147f04e2909474f80fd6e` was
  rebuilt and physically smoke-qualified as the exact Release artifact:
  Spotify rendered, split panes remained bright, popup-open panes remained
  bright, overlap foreground/z-order invariants held in both directions, and
  the Release textbox accepted real input without clipping. The application
  implementation is unchanged from source commit `4f4998ff`.
- A self-contained single-file Release artifact built from the final
  integrated tree was launched, captured Spotify and a real GuineaPig guest,
  rendered both, kept Spotify visible under the Group popup, released the
  guest, and exited through the product's accessible Exit control. Its exact
  source SHA and file hash are recorded in the campaign handoff.
- Exact-SHA hosted CI run `35138318079` passed for implementation commit
  `6172595cffcce2e2eee506c3eb9ea1791afa7fa8`; both `build` and
  `native-abi-evidence` check-runs were successful.
- `repowise update` reports the local index current; `git diff --check` passes
  with only the repository's LF-to-CRLF normalization warnings.

## Handoff

Campaign complete for the evidence available on this desktop. `main` and
`origin/main` are aligned at the verified implementation tree, and the
pre-existing `.codex/config.toml` edit remains preserved and excluded.
