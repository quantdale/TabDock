# TabDock agent state

## Objective and status — 2026-09-17

Release-finalize TabDock from the four beta observations using current
Windows evidence. The candidate implementation, automated gates, and exact
Release physical qualification are complete for the available single-monitor
desktop. The evidence records are closed; the remaining operations are the
final exact-tree Release rebuild/smoke, authorized mainline push, and
independent remote/CI verification.
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
- Candidate source fixes cover popup-local z-order reconciliation, hide/minimize
  provenance ordering, active-switch foreground ownership, measured split
  stack reassertion, and stale delayed foreground callbacks. Shepherd remains
  never-reparent. The exact candidate Release artifact was exercised with
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
- Exact candidate Release artifact: `artifacts/release-final-4f4998ff`;
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
- `repowise update` reports the local index current; `git diff --check` passes
  with only the repository's LF-to-CRLF normalization warnings.

## Next action

Rebuild and exercise the exact final-tree Release artifact, push `main`
without force, and independently verify remote SHA and CI status. The
pre-existing `.codex/config.toml` edit remains preserved and excluded.
