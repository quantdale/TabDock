## 1. Catalog foundation

- [x] 1.1 Add typed scenario, execution-class, guest-family, input, topology, safety, shard-budget, and release-evidence metadata models plus a catalog generation identifier.
- [x] 1.2 Migrate every current ValidationDriver dispatchable scenario into one catalog registry with its existing handler, compatibility name, shard, inclusion policy, and explicit capability metadata.
- [x] 1.3 Replace CLI allowlists, shard projections, guest validation, startup coverage checks, and dispatch selection with catalog projections; remove name-based capability heuristics and duplicated scenario arrays.
- [x] 1.4 Add catalog self-tests for unique IDs, handler coverage, incompatible shards, budget limits, explicit-only browser/real-app policy, and documentation/catalog drift.
- [x] 1.5 Commit the catalog wave after focused driver build and catalog/self-test validation.

## 2. Versioned child and parent manifests

- [x] 2.1 Define versioned child/shard/parent manifest records and a backwards-conscious reader that preserves the existing direct scenario artifact contract.
- [x] 2.2 Give each direct scenario/shard run an isolated artifact root and emit child-manifest location, candidate identity, driver identity/hash, catalog generation, shard identity, timestamps, outcomes, capabilities, attempts, and relative artifact links.
- [x] 2.3 Update `all` orchestration to create a parent run identity, pass isolated child roots, import the actual child manifest, verify exit/manifest agreement, and record partial/cancelled/unlaunched shards as harness evidence.
- [x] 2.4 Implement deterministic parent aggregation for duplicate/missing/malformed/stale/tampered child manifests, wrong candidate/shard/driver, artifact absence, timeout, blocked/skipped/flake outcomes, and first-attempt-authoritative reruns.
- [x] 2.5 Add child/parent fixture tests and commit the hierarchical-manifest wave after focused deterministic validation.

## 3. Qualification bundle and offline verifier

- [ ] 3.1 Define the versioned qualification-bundle root record and portable artifact index with source/candidate/driver/catalog/run-manifest bindings and privacy-safe environment fields.
- [ ] 3.2 Implement canonical relative-path normalization, duplicate-property/entry handling, hash computation, timestamp policy, schema acceptance policy, and bounded privacy validation.
- [ ] 3.3 Implement bundle creation from verified direct/parent manifests and offline verification that never launches TabDock, a guest, a script, or a returned binary.
- [ ] 3.4 Add adversarial fixtures for modified/missing artifacts, path traversal/absolute paths, duplicate entries/properties, hash/source/candidate mismatches, inconsistent counts, stale/future/unsupported schemas, and privacy violations.
- [ ] 3.5 Commit the bundle/verifier wave after focused verifier and self-test validation.

## 4. Release-candidate evidence bridge

- [ ] 4.1 Extend release-tooling data contracts to accept verified qualification bundles while preserving Stage-A exact-byte, signing, checksum, and Stage-B trusted-policy boundaries.
- [ ] 4.2 Add `scripts/qualify-release-candidate.ps1` to verify a retained candidate directory, require `--tabdock` exact bytes and matching `artifactSha256`, locate matching tooling, run permitted tiers, and emit a bound bundle without rebuilding/replacing the candidate.
- [ ] 4.3 Add structured external-evidence machine gate fields for bundle/run-manifest hashes, required scenarios, observed topology, and explicit synthetic/replay/blocked/flake classification; keep human smoke as a separate attestation.
- [ ] 4.4 Add deterministic release-tooling tests for candidate/source/run/artifact substitution, bundle tampering, missing results/timelines, schema/timestamp/OS errors, synthetic/replay/blocked/flake PASS claims, signature hash disagreement, and publication-verification disagreement.
- [ ] 4.5 Commit the candidate/evidence bridge wave after release-tooling regression validation.

## 5. Independent-machine handoff

- [ ] 5.1 Add portable export/package commands and operator documentation for copying an exact Stage-A candidate plus matching qualification tooling to Windows 10/11 machines.
- [ ] 5.2 Emit privacy-safe machine reports containing OS/build/architecture, executable identity/hash, native ABI result, observed monitor/DPI classification, scenario outcomes, bundle/run-manifest hashes, and timestamps.
- [ ] 5.3 Add defensive report import/merge validation for untrusted files and ensure imports never execute returned scripts or binaries and never elevate non-pass outcomes.
- [ ] 5.4 Add adversarial independent-machine fixtures for wrong OS, missing operator/timestamps, candidate substitution, bundle/hash mismatch, synthetic topology, replay evidence, and malformed reports.

## 6. Virtual topology laboratory

- [ ] 6.1 Add native-free monitor/work-area/DPI/virtual-screen models and deterministic placement, containment, split partition, clamp/restore, and drag projection helpers using existing pure policy seams.
- [ ] 6.2 Add fixed boundary matrices for 96/120/144/192 DPI, horizontal/vertical/negative/above-origin/asymmetric/odd/narrow/large-coordinate topologies and monitor removal/reordering transitions.
- [ ] 6.3 Add fixed-seed stress/replay artifacts with `syntheticTopology=true`, stable normalized hashes, and explicit no-physical-qualification classification.
- [ ] 6.4 Add unit/self-tests for laboratory invariants, seed reproducibility, transition safety, and rejection of synthetic topology in physical release gates.
- [ ] 6.5 Commit the topology wave after focused laboratory and policy validation.

## 7. Workflow and operator integration

- [ ] 7.1 Audit and minimally update `build.yml`, `qualify-candidate.yml`, `prepare-release-candidate.yml`, and `publish-release.yml` to retain bundle/manifests, expose identities/schema generation in summaries, and record hosted physical capability blocks.
- [ ] 7.2 Preserve immutable action pins, least privilege, exact-SHA checkout, `persist-credentials: false`, Stage-B no-candidate-execution, and production signing policy with static regression tests.
- [ ] 7.3 Add catalog inspection, qualification planning, bundle verification, and bundle/report merge/export help while preserving compatible driver commands.

## 8. Documentation, specifications, and final qualification

- [ ] 8.1 Update architecture/testing/release/mixed-DPI/compatibility/final-smoke documentation with schema migrations, trust boundaries, exact-candidate commands, and independent-machine handoff.
- [ ] 8.2 Generate or validate catalog/documentation projections and update README/known issues only where user-facing release or diagnostic instructions changed.
- [ ] 8.3 Update `.agent/STATE.md`, campaign plan/checkpoints, audit evidence, and OpenSpec task/spec status with physical gates honestly blocked where applicable.
- [ ] 8.4 Run focused tests after each wave, then Debug/Release zero-warning builds and complete suites, all ValidationDriver deterministic tests, catalog/manifest/bundle/topology tests, release-tooling tests, strict OpenSpec, native ABI, privacy/recovery smokes, publish/version checks, canonical validation/publish, and `git diff --check`.
- [ ] 8.5 Commit final documentation/qualification wave, inspect the complete diff for scope creep, push the stacked branch, and create a draft PR against the campaign branch when authenticated access is available without changing PR #12.
