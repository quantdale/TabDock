## Context

The current ValidationDriver has a common outcome writer and root `run-manifest.json`, but `Program` and `Scenarios` still carry parallel allowlists, shard projections, a dispatch switch, and name-based capability inference. The current `all` path starts bounded child processes and reduces exit codes without importing child artifacts. Release tooling already owns exact-SHA, signing, Stage-A/Stage-B, and external-evidence policy; this design adds qualification records as verified data flowing into that policy instead of creating a second release policy.

The implementation remains native-free for the new verifier and topology laboratory. Physical scenarios continue to use `DesktopQualificationLease`, guarded input, provenance-aware cleanup, and the existing Shepherd architecture. No new runtime package is required.

## Goals / Non-Goals

**Goals:**

- Make the scenario catalog, child/parent manifests, qualification bundle, offline verifier, and release evidence use explicit versioned contracts.
- Preserve backward readability of existing direct run artifacts while emitting the new schema for new runs.
- Bind all machine evidence to the exact candidate executable bytes and source identity.
- Make independent-machine reports portable and untrusted-input safe.
- Exercise monitor/DPI/placement policy with deterministic models and never misclassify it as physical qualification.
- Retain actionable diagnostics without recording titles, URLs, document text, or arbitrary user paths.

**Non-Goals:**

- Reparenting guests, changing native capture/Shepherd ownership, weakening HWND identity checks, or changing recovery/persistence authorities.
- Running physical SendInput scenarios on a shared or unproven desktop.
- Replacing the existing Stage-B trusted-policy boundary or executing candidate-controlled code.
- Claiming Windows 10, mixed-DPI, signing, independent-machine, or human-smoke PASS from headless simulation.

## Decisions

### One catalog in the ValidationDriver assembly

Add a partial `Scenarios` catalog registry containing one `ScenarioDefinition` per dispatchable handler. Each entry stores the complete typed metadata and a delegate to the existing private scenario body. Shards are typed `ScenarioShardDefinition` records with declared inclusion and budgets. All legacy views (`--list`, explicit-only sets, default `all` order, shard lookup, and guest validation) become projections over the registry. A startup self-test checks unique IDs, handler coverage, compatible execution class, shard ownership, budgets, and catalog generation.

This keeps the existing scenario bodies and the C# compile boundary while removing the fragile name heuristics. An alternative of generating metadata from filenames was rejected because filenames do not express guest ownership, release eligibility, or destructive-state policy. A second external YAML catalog was rejected because it would reintroduce synchronization drift.

### Child artifacts are immutable inputs to parent aggregation

`QualificationResultWriter` will emit a versioned child manifest and expose the artifact directory through an environment/argument contract. `RunAllShards` creates a unique parent artifact root, passes an isolated child root to each process, and reads only the child manifest declared by the child. A verifier parses JSON with duplicate-property rejection, normalizes relative paths, hashes every linked artifact, and compares manifest identity with the expected candidate, driver, catalog generation, and shard. Exit code is treated as a cross-check, never the source of truth.

The parent records every shard even after a failure or cancellation. It may stop launching later shards at the bounded safety boundary, but unlaunched declared shards are represented as harness failures/partial evidence rather than omitted. Rerun aggregation reuses the existing first-attempt-authoritative `ScenarioAggregate` contract.

### Separate typed bundle verifier from release policy

Add a small portable verifier model that accepts a bundle directory and JSON root record, returns structured failures, and never starts a process. The bundle artifact index uses forward-slash relative paths and SHA-256. The verifier rejects absolute paths, `..`, duplicate entries, missing files, modified bytes, unsupported schemas, inconsistent counts, future timestamps, candidate/source disagreement, and synthetic physical claims. The release PowerShell module calls this verifier through a deterministic JSON contract or its equivalent PowerShell validation helpers; Stage B continues to use only trusted policy code.

Existing schema-1 direct manifests remain readable for diagnostics and migration tests, but only new schema-2 child/parent manifests and bundle schema-1 records are eligible for release evidence. The accepted-version policy is explicit in code and documented.

### Evidence bridge is exact-byte and additive

`qualify-release-candidate.ps1` takes a retained artifact directory, verifies `release-manifest.json`, checksums, and applicable signature state, and requires the candidate executable hash to equal `artifactSha256`. It passes that explicit path to ValidationDriver and sets a bounded artifact root. It never invokes build output over the candidate or copies another executable into its place. It writes a bundle containing the release manifest hash, child/parent manifest hashes, driver hash, and physical/synthetic classification.

The external-evidence builder gains import/merge functions for bundles and independent-machine reports. Existing schemaVersion 2 remains the release evidence version; new machine-bound fields are required for machine gates but old records remain distinguishable and fail closed where those fields are absent. Human smoke remains a separate attestation record.

### Topology laboratory uses pure policy models

Add `VirtualTopology`, `VirtualMonitor`, and pure placement/partition helpers in the ValidationDriver/test assembly, reusing existing `SplitGeometry`/`PaneContainmentPolicy` math where their signatures permit. Fixed matrices cover 96/120/144/192 DPI, negative and above-origin rectangles, work-area offsets, odd/narrow dimensions, large coordinates, and monitor removal/reordering. A fixed seed drives bounded transition cases. Every output contains `syntheticTopology=true`; release import explicitly rejects that marker for physical gates.

### Workflow changes retain immutable and least-privilege boundaries

The build/qualification workflows retain the new deterministic bundle artifacts and summaries. Candidate qualification may run in Stage A/RC jobs, but publication only downloads and verifies records. No workflow change introduces a candidate execution step in `publish-release.yml`; static release-tooling tests continue to scan for that class of regression.

## Risks / Trade-offs

- [Risk] The existing scenario body list is large and a catalog migration can omit a rarely used explicit scenario. → Keep a dispatch self-test that compares every handler/catalog entry, preserve compatibility names, and add a catalog count/ID snapshot test.
- [Risk] Child artifact paths can leak machine-specific data or escape the bundle root. → Normalize only relative paths, reject traversal/absolute paths, use privacy-safe manifest projections, and test adversarial paths.
- [Risk] A parent verifier can accidentally trust a child's summary instead of underlying artifacts. → Recompute hashes and outcome counts from imported scenario entries and use exit codes only as disagreement evidence.
- [Risk] Existing consumers expect schema-1 manifests or release evidence v2. → Keep a backwards-conscious reader, emit explicit schema generations, and reject unsupported versions with a documented migration message.
- [Risk] A synthetic topology model may be mistaken for physical qualification. → Make `syntheticTopology` mandatory in the bundle and require `false` plus observed real topology for physical gates.
- [Risk] Large parent manifests may become unwieldy. → Keep bounded per-scenario evidence, hash linked files rather than inlining them, and use relative links.

## Migration Plan

1. Add the catalog and schema models with self-tests while preserving current direct scenario behavior.
2. Migrate the writer and `all` orchestrator to child/parent manifests; keep legacy root output as a compatibility projection where needed.
3. Add bundle creation/verification and deterministic fixtures; then wire release tooling and workflows to retain/verify them.
4. Add independent-machine export/import and topology laboratory reports.
5. Update docs/specs and run all deterministic gates. Rollback is a branch-level revert of the campaign commits; no application state or release artifact is mutated by the verifier.

