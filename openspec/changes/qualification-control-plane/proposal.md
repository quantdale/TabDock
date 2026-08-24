## Why

TabDock already emits deterministic scenario results, shard manifests, release manifests, and conservative physical outcomes, but those records are not yet one verifiable evidence chain. The `all` run, candidate artifact, independent-machine reports, external release gates, and topology qualification can therefore drift or be confused without a shared machine-enforced contract.

This change connects those existing authorities into a fail-closed qualification control plane while expanding native-free monitor/DPI coverage. It is needed now because the current release process has enough evidence primitives to bind exact bytes and outcomes, but still relies on duplicated scenario metadata and manually interpreted boundaries.

## What Changes

- Add a canonical typed scenario catalog and derive dispatch, shard, capability, planning, documentation, and `all` orchestration projections from it.
- Make shard manifests and the `all` parent manifest versioned, hierarchical, hash-bound, rerun-aware, and fail-closed on missing or contradictory child evidence.
- Add a portable qualification bundle and offline verifier with relative artifact paths, manifest/result/timeline hashes, catalog generation, candidate identity, and privacy-safe metadata.
- Add exact-candidate release qualification and independent-machine handoff/import tooling that bridges ValidationDriver evidence into the existing Stage-A/Stage-B release policy.
- Strengthen external evidence builders so only verified, candidate-bound, non-synthetic machine evidence and explicit human attestations can satisfy their respective gates.
- Add a deterministic virtual monitor/DPI/topology laboratory with fixed seeds and boundary matrices; mark all such evidence synthetic and ineligible for physical mixed-DPI release gates.
- Expand ValidationDriver, release-tooling, workflow, and adversarial regression coverage, preserving immutable actions, least privilege, and the Stage-B no-candidate-execution boundary.
- Add operator planning/verification commands and update release, testing, architecture, OpenSpec, and durable state documentation.

## Capabilities

### New Capabilities

- `qualification-control-plane`: canonical scenario metadata, hierarchical manifests, qualification bundles, offline verification, planning, and merge/export contracts.
- `virtual-topology-laboratory`: deterministic monitor/DPI/virtual-screen models and synthetic evidence classification for headless policy coverage.

### Modified Capabilities

- `validation-qualification`: extend scenario execution, shard aggregation, result schemas, capability classification, rerun lineage, and evidence portability contracts.
- `release-engineering`: bind exact candidate bytes and Stage-A provenance to qualification bundles, structured machine evidence, external gates, and publication verification.

## Impact

- ValidationDriver scenario dispatch, result writing, manifests, deterministic self-tests, and new catalog/topology/evidence models.
- `scripts/release-tooling.ps1`, `release-qualify.ps1`, new candidate qualification/import tooling, release-tooling tests, and the four existing release workflows.
- New offline verification/fixture helpers and unit/self-test coverage; no new runtime dependency or change to Shepherd/native capture architecture.
- `openspec/specs/`, architecture/testing/release documentation, workflow summaries, and `.agent/STATE.md`.
