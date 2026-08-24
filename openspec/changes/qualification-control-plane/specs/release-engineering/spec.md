## MODIFIED Requirements

### Requirement: Release qualification is exact-SHA and immutable
Release qualification SHALL operate on an exact candidate commit and exact executable bytes: the requested SHA must equal `HEAD`, the working tree must be clean, the published executable's embedded source commit must equal the candidate SHA, and the artifact's computed SHA-256 must be the identity used by the release manifest, qualification plan, ValidationDriver child/parent manifests, and qualification bundle. Physical qualification SHALL accept `--tabdock <path>` and SHALL refuse to rebuild or replace the bytes being qualified. The qualified artifact SHALL be retained without any second compilation.

#### Scenario: Mismatched SHA is refused
- **WHEN** a requested release SHA differs from `HEAD` or a qualification bundle names another source SHA
- **THEN** qualification fails before any build work or evidence publication

#### Scenario: The published executable must be the qualified executable
- **WHEN** a release candidate is qualified
- **THEN** the executable that passes `--version`, native ABI, and permitted qualification checks is the same binary whose final hash is recorded in every downstream evidence record

### Requirement: Qualification results use an explicit vocabulary
Qualification SHALL record exactly one of `PASS`, `FAIL_PRODUCT`, `FAIL_HARNESS`, `BLOCKED_ENVIRONMENT`, `BLOCKED_SUPERVISED`, `BLOCKED_CAPABILITY`, `SKIP_CAPABILITY`, or `FLAKE_UNCLASSIFIED` for scenario evidence, and SHALL map those values consistently into release gates. A 0/N run, unavailable browser, missing mixed-DPI hardware, synthetic topology, replay-only fixture, blocked result, skipped capability, or unclassified flake SHALL NOT become `PASS`. External gates SHALL retain their exact `PASS`, `FAIL`, `BLOCKED_EXTERNAL`, or `BLOCKED_ENVIRONMENT` vocabulary.

#### Scenario: Missing external evidence is recorded, not fabricated
- **WHEN** a release manifest is generated without physical mixed-DPI hardware, a human smoke, an independent-machine report, or required signing material
- **THEN** the corresponding external gate remains blocked and no manifest or builder claims it passed

#### Scenario: A blocked child is included in an all run
- **WHEN** a child shard contains `BLOCKED_*`, `SKIP_CAPABILITY`, or `FLAKE_UNCLASSIFIED`
- **THEN** the parent and qualification bundle preserve that outcome and the release eligibility is not PASS

### Requirement: Windows compatibility is a production evidence gate
Because v1.0.0 advertises Windows 10 and Windows 11 x64, production publication SHALL require structured PASS evidence for both a real supported Windows 10 x64 system and a real Windows 11 x64 system, each recording OS build, architecture, operator, ISO-8601 completion time, native ABI evidence, exact candidate identity, and a verified qualification-bundle reference. Compatibility evidence SHALL state `syntheticTopology=false` and SHALL not be replay-only or deterministic-simulation evidence. Missing, malformed, mismatched, `FAIL`, blocked, synthetic, or non-candidate-bound reports SHALL block production publication.

#### Scenario: Windows 10 remains unproven
- **WHEN** a production release is requested without real Windows 10 x64 PASS evidence, native ABI evidence, and an exact candidate-bound bundle
- **THEN** publication is refused

#### Scenario: Synthetic compatibility evidence is submitted
- **WHEN** a Windows compatibility report contains `syntheticTopology=true`, replay provenance, or a candidate hash different from the final candidate
- **THEN** the compatibility gate is rejected and remains blocked

### Requirement: External production evidence is an auditable record
Production publication SHALL require a `release-external-evidence.json` record with `schemaVersion: 2`, exact `sourceCommitSha`, `artifactSha256`, `candidateWorkflowRunId`, and `candidateArtifactName` bindings, and mandatory human and physical gates. Machine-produced gate records SHALL additionally reference a verified qualification-bundle path and SHA-256 plus the relevant run-manifest hash, scenario IDs, observed topology classification, and outcome evidence. Human smoke SHALL remain an explicit attestation bound to the same source, final artifact, Stage-A identity, operator, completion time, and preferably the verified bundle. Missing, malformed, stale, synthetic, replay-only, blocked, skipped, or flaky evidence SHALL fail closed.

#### Scenario: Evidence from another candidate cannot be reused
- **WHEN** any source SHA, executable hash, Stage-A run/artifact identity, bundle hash, or run-manifest hash differs from the candidate being published
- **THEN** publication is refused

#### Scenario: Machine evidence is not a human attestation
- **WHEN** a machine qualification bundle is present but the final Windows human-smoke attestation is absent
- **THEN** automated gates may remain independently recorded, but production publication remains blocked

## ADDED Requirements

### Requirement: Stage-A candidate qualification SHALL accept verified ValidationDriver evidence
The release qualification tooling SHALL verify the retained Stage-A manifest/checksums/signature state as applicable, build or locate matching tooling from the candidate source without replacing the candidate executable, run only capability-permitted tiers, and emit an exact-candidate qualification bundle. Stage B SHALL consume the verified bundle as data under the existing trusted-policy/no-candidate-execution boundary.

#### Scenario: A retained candidate is qualified in place
- **WHEN** an operator supplies a retained Stage-A artifact directory and `--tabdock` points to its exact executable
- **THEN** tooling verifies the manifest hash and candidate identity, runs the driver against that path, and records the same final executable SHA in the bundle

#### Scenario: Candidate qualification would rebuild the executable
- **WHEN** requested tooling cannot locate a matching ValidationDriver or would need to overwrite the candidate executable
- **THEN** qualification fails closed and retains no PASS evidence for that candidate

### Requirement: External evidence builders SHALL consume verified machine reports
Release tooling SHALL import structured Windows compatibility and independent-machine reports only after schema, timestamp, OS/architecture, candidate hash, bundle hash, run-manifest hash, topology classification, and outcome verification. It SHALL never manufacture PASS from free-form evidence text or from synthetic, replay, skipped, blocked, or flaky results.

#### Scenario: A report claims PASS with blocked scenarios
- **WHEN** an imported report claims a machine gate PASS but its bundle contains a blocked, skipped, replay-only, or unclassified-flake required scenario
- **THEN** import fails closed and the external gate remains blocked
