# TabDock agent state

## Objective and status — 2026-09-17

Release-finalize TabDock from the four beta observations using current
Windows evidence. Implementation and automated qualification are complete in
the campaign working tree; exact post-commit Release physical qualification,
documentation closure, and mainline integration remain.
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
  never-reparent.
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
- `repowise update` reports the local index current; `git diff --check` passes
  with only the repository's LF-to-CRLF normalization warnings.

## Next action

Inspect the final diff, commit only campaign files, publish the exact
post-commit Release artifact, physically qualify the available A-D and core
workflow matrix, then update this state with facts before pushing `main`.
