## Purpose

Defines the exact-SHA, immutable, honestly-qualified production release chain
for TabDock: the artifact that passes qualification is the artifact that is
hashed, manifested, retained, and (only when every gate passes) published.

## ADDED Requirements

### Requirement: Release qualification is exact-SHA and immutable
Release qualification SHALL operate on an exact candidate commit: the
requested SHA must equal `HEAD`, the working tree must be clean, the
published executable's embedded source commit must equal the candidate SHA,
and the artifact's self-reported SHA-256 must equal the computed file hash.
The qualified artifact SHALL be retained as a GitHub Actions artifact without
any second compilation.

#### Scenario: Mismatched SHA is refused
- **WHEN** a requested release SHA differs from `HEAD`
- **THEN** qualification fails before any build work

#### Scenario: The published executable must be the qualified executable
- **WHEN** a release candidate is qualified
- **THEN** the executable that passes `--version` identity, geometry, and
  diagnostics self-tests is the same binary that is hashed, manifested, and
  available for publication

### Requirement: Qualification results use an explicit vocabulary
Qualification SHALL record exactly one of `PASS`, `FAIL`, `BLOCKED_EXTERNAL`,
`BLOCKED_ENVIRONMENT`, or `SKIP_NOT_APPLICABLE` per gate. A 0/N scenario run,
an unavailable browser, and missing mixed-DPI hardware SHALL NOT become
`PASS`. External gates (final human smoke, physical mixed-DPI, signing
credentials, destructive logoff testing) SHALL be recorded explicitly in the
release manifest and SHALL remain unperformed markers until real evidence
exists.

#### Scenario: Missing external evidence is recorded, not fabricated
- **WHEN** a release manifest is generated without physical mixed-DPI
  hardware or a human smoke
- **THEN** the corresponding external gate is `BLOCKED_EXTERNAL` and the
  manifest never claims the gate passed

### Requirement: Signing readiness with truthful states
Authenticode signing SHALL be optional by default and mandatory only when
policy declares it (`RELEASE_SIGNING_REQUIRED=true`). Signing material SHALL
come only from CI secrets/environment; certificates and passwords SHALL NOT be
committed. Signing states SHALL be exactly `NOT_CONFIGURED`, `SIGNED`,
`SIGNATURE_VERIFIED`, or `SIGNING_FAILED`; an unsigned executable SHALL never
be described as signed; when signing changes the bytes, both the unsigned
qualified hash and the final signed hash SHALL be recorded.

#### Scenario: No material is present
- **WHEN** no signing material is configured and signing is not required
- **THEN** the manifest records `NOT_CONFIGURED` and qualification still passes

#### Scenario: Mandatory signing without material
- **WHEN** `RELEASE_SIGNING_REQUIRED=true` and no material is configured
- **THEN** qualification fails

### Requirement: Stable releases are intentional and fail-closed
The release workflow SHALL be dispatch-only (never triggered by a push),
require an exact commit, run canonical qualification, preserve the qualified
artifact, and publish only through an explicit second decision. Publication
SHALL independently re-verify every production condition against the on-disk
artifact before consuming it: manifest `PASS`, exact source SHA, exact
semantic version, on-disk hash equal to the manifest's final artifact hash,
`SHA256SUMS.txt` equal to the on-disk hash, schema-valid external evidence
bound to the exact SHA and the final artifact hash, and (when production
signing is mandatory) `SIGNED` + `SIGNATURE_VERIFIED` with an independent
`signtool verify /pa` on the final executable. The workflow SHALL verify the
published release assets afterwards. No source modification and no second
compilation SHALL occur at publication time.

#### Scenario: Publication re-verification fails
- **WHEN** the preserved artifact's on-disk hash differs from the manifest
  or from `SHA256SUMS.txt`
- **THEN** publication is refused

#### Scenario: Production publication requires external evidence
- **WHEN** a production release is requested without a valid
  `release-external-evidence.json` record (missing, malformed, wrong source
  SHA, wrong artifact hash, or any mandatory gate not `PASS`)
- **THEN** publication is refused and no release or tag is created

#### Scenario: Qualification-only runs need no external evidence
- **WHEN** a qualification-only run is requested
- **THEN** the qualification succeeds and the manifest records
  `productionReleaseEligibility = BLOCKED_EXTERNAL` without any evidence

#### Scenario: Tag identity
- **WHEN** a release is published with tag `v<semver>`
- **THEN** the tag resolves to the exact qualified candidate SHA

### Requirement: Final distributed hash contract
The release manifest SHALL distinguish `unsignedQualifiedSha256` (the hash of
the executable that passed pre-sign qualification, always retained),
`finalSignedSha256` (the hash after Authenticode signing and verification,
when signing changed the bytes), and `artifactSha256` (ALWAYS the hash of the
final distributed executable). `SHA256SUMS.txt` SHALL describe the final
distributed executable. The manifest and checksums SHALL be written only
after signing, from the hash of the artifact as it exists at finalization
time, and SHALL be cross-checked against the actual file in both release
qualification and publication.

#### Scenario: Signing changes the bytes
- **WHEN** an artifact is signed before finalization
- **THEN** `artifactSha256` and `SHA256SUMS.txt` carry the post-sign hash,
  `unsignedQualifiedSha256` retains the pre-sign provenance hash, and any
  disagreement with the on-disk file fails the qualification

### Requirement: Production signing policy is explicit
RC qualification SHALL permit `NOT_CONFIGURED` signing. Production candidates
(the `prepare-release-candidate` Stage A workflow) SHALL make Authenticode
signing mandatory: the manifest must record `SIGNED` and
`SIGNATURE_VERIFIED`, must carry `finalSignedSha256` equal to the final
artifact hash, must not be a test-only mock-signed artifact, and the final
executable SHALL pass an independent `signtool verify /pa` in the Stage B
publication workflow. An unsigned production release SHALL NOT be silently
defaulted.

#### Scenario: Production publication without a signature
- **WHEN** a production release is requested and the artifact is unsigned or
  its signature is unverified
- **THEN** publication is refused

#### Scenario: RC qualification remains unsigned-capable
- **WHEN** a qualification-only run has no signing material
- **THEN** the qualification still passes with `signingStatus =
  NOT_CONFIGURED` and external gates stay `BLOCKED_EXTERNAL`

### Requirement: Signing is provider-abstracted with an approved production provider class
Release signing SHALL be selected by a `SIGNING_PROVIDER` variable with an
explicit vocabulary (`not-configured`, `local-pfx`, `digicert-stm`,
`mock-test`); an unknown value SHALL fail rather than silently fall back.
Every provider SHALL report one structured signer contract (Status,
Verification, FinalSha256, Provider, KeyProtection, TimestampStatus,
certificate identity). Production candidates (Stage A) SHALL require an
APPROVED production provider — currently `digicert-stm` (DigiCert Software
Trust Manager, non-exportable `CLOUD_HSM` private key held by the signing
service) — with complete provider credentials; anything else
(`not-configured`, `local-pfx`, `mock-test`, unknown) SHALL fail with
`BLOCKED_EXTERNAL` BEFORE any build work, and only variable NAMES SHALL be
reported. The production code-signing private key SHALL NOT exist as an
exportable PFX in GitHub Secrets or on the runner; the runner SHALL hold
only service-authentication material. `local-pfx` SHALL remain available
for development/private/RC use and SHALL be rejected by production policy
at every layer.

#### Scenario: Production candidate with an unapproved provider is refused before build
- **WHEN** a production candidate is requested with `SIGNING_PROVIDER` =
  `local-pfx`, `mock-test`, `not-configured`, or an unknown value
- **THEN** the run fails `BLOCKED_EXTERNAL` before any build and no
  candidate is produced

#### Scenario: An approved provider with incomplete credentials is refused before build
- **WHEN** a production candidate is requested with `SIGNING_PROVIDER` =
  `digicert-stm` but its credentials are incomplete
- **THEN** the run fails `BLOCKED_EXTERNAL` naming the missing variable
  names (never values) before any build

#### Scenario: The mock provider is structurally isolated
- **WHEN** a test-only mock signing mode is invoked
- **THEN** it requires `SIGNING_PROVIDER=mock-test`, refuses to run while
  real provider material is configured, reports `Provider=mock-test` /
  `KeyProtection=MOCK_TEST` / `Mock=true`, and can never be selected by a
  production configuration

### Requirement: Production signing verification is provider-independent
After ANY provider signs, the production chain SHALL verify the actual
executable with provider-independent Windows tooling: `signtool verify /pa`
(Authenticode), RFC3161 timestamp verification (a valid timestamp
certificate on the signature; `timestampStatus=VERIFIED` is mandatory and a
missing/invalid timestamp fails the run), and signed-certificate identity
(subject, thumbprint, issuer, serial number, validity window, and the
code-signing EKU `1.3.6.1.5.5.7.3.3`), with an optional expected-publisher
allowlist. A provider reporting success SHALL NOT be sufficient. The final
SHA-256 SHALL be computed only after signing and verification.

#### Scenario: The provider claims success but the signature is invalid
- **WHEN** a provider reports a successful signing operation but
  independent Authenticode verification of the actual EXE fails
- **THEN** the run fails and no candidate is produced

#### Scenario: The timestamp is absent or invalid
- **WHEN** an artifact is signed but the RFC3161 timestamp cannot be
  verified
- **THEN** production Stage A fails (`timestampStatus != VERIFIED`) and the
  publication gate rejects the artifact

### Requirement: Signing provenance is recorded and required at publication
The release manifest SHALL record `signingProvider`, `signingKeyProtection`,
`timestampStatus`, and the signed-certificate identity (subject, thumbprint,
issuer, validity window, EKU) for a signed artifact; the thumbprint SHALL be
recorded, not hard-coded (certificates rotate). The Stage B publication gate
SHALL require the APPROVED provider class and approved non-exportable key
protection (`CLOUD_HSM`), the recorded certificate identity with the
code-signing EKU, `timestampStatus=VERIFIED`, and SHALL independently
re-verify the signature, timestamp, and certificate identity of the
downloaded bytes. Stage B SHALL NOT contact the signing provider, SHALL NOT
require provider credentials, and SHALL NOT re-sign.

#### Scenario: A locally signed or mock artifact cannot satisfy production
- **WHEN** a manifest records `signingProvider=local-pfx`,
  `signingProvider=mock-test`, `signingProvider=not-configured`, an unknown
  provider, or no provider/key-protection metadata
- **THEN** the publication gate refuses the artifact even when the file is
  genuinely Authenticode-signed

#### Scenario: Publication never depends on the signing provider
- **WHEN** Stage B validates an already-signed candidate
- **THEN** it uses only Windows signature verification and the retained
  provenance, with no provider credentials, no provider contact, and no
  signing operation

### Requirement: External production evidence is an auditable record
Production publication SHALL require a `release-external-evidence.json`
record with `schemaVersion`, `sourceCommitSha` (exact 40-character candidate
SHA), `artifactSha256` (exact final artifact hash), and mandatory gates
`finalWindowsHumanSmoke` and `physicalMixedDpi`, each carrying `status`
(only `PASS` is acceptable), `operator`, `completedAt`, and `evidence`.
A caller-controlled boolean SHALL NOT substitute for the record. Missing
evidence, malformed evidence, wrong schema version, wrong source SHA, wrong
artifact hash, `FAIL`, or `BLOCKED_EXTERNAL` SHALL fail closed. The validated
record SHALL be retained with the release and its publish-time eligibility
verdict recorded in `publication-verification.json`.

#### Scenario: Evidence from another candidate cannot be reused
- **WHEN** the evidence's source SHA or artifact hash does not match the
  candidate and the final artifact being published
- **THEN** publication is refused

### Requirement: Signing-path regression protection without real material
Release tooling SHALL provide deterministic tests that exercise the
"artifact changed after signing" semantics without committing or generating
persistent private key material: a test-only mock signer models the byte
mutation, is marked `Mock=true`, refuses to run while real material is
configured, and can never satisfy the production publication gate. The
regression suite SHALL cover the unsigned path, the signed/mutated path,
publication provenance, signing failure, and signature-verification failure.

#### Scenario: A mock-signed artifact never reaches production
- **WHEN** a manifest records `signingMock = true`
- **THEN** the production publication gate refuses the artifact

### Requirement: Native ABI self-test evidence
The executable SHALL provide a fail-loud native ABI self-test
(`--selftest-native-abi`) that validates the 44-byte `WINDOWPLACEMENT`
contract against real user32 (structure size/offsets, get/set round trip,
zero-length rejection, 60-byte rejection) and prints a per-machine
environment report (OS identity, accepted length, get/set behavior). The
supported-hosted CI SHALL run the self-test on more than one Windows build,
and untested Windows versions SHALL remain documented as unproven rather
than assumed.

#### Scenario: A Windows build changes the placement ABI
- **WHEN** a qualifying machine accepts a 60-byte `WINDOWPLACEMENT` or
  rejects the 44-byte contract
- **THEN** the self-test fails loudly and qualification fails until the
  structure-size decision is revisited with that evidence

### Requirement: Version contract is authoritative
The `TabDock.csproj` `Version` SHALL be the single authoritative version
mechanism. Release tooling SHALL validate that its expected version agrees and
SHALL record the version in the release manifest. Historical release names
SHALL NOT dictate the semantic version contract.

#### Scenario: Tooling version disagreement is recorded, not invented
- **WHEN** release tooling is invoked with a version that differs from the
  project's authoritative `Version`
- **THEN** the invocation fails or the manifest records the disagreement;
  the manifest never claims a version the project does not declare

### Requirement: Reproducibility policy is explicit
The .NET SDK SHALL be pinned via `global.json` within the .NET 8 feature band
with roll-forward that never leaves .NET 8. NuGet restore SHALL remain
ordinary (no strict lock mode) with a mandatory CI vulnerability audit, and
OpenSpec tooling SHALL remain pinned through its lockfile.

#### Scenario: A newer major SDK is not silently selected
- **WHEN** only a .NET 9+ SDK is installed alongside the pinned .NET 8 band
- **THEN** `global.json` keeps the build on a .NET 8 SDK (or the build fails
  with a clear SDK-resolution error) rather than silently compiling with an
  unqualified SDK

### Requirement: Production publication is a two-stage, no-rebuild chain
Production publication SHALL be split into two distinct stages that share no
build or signing step. STAGE A (a manually dispatched candidate-preparation
workflow) SHALL build once, Authenticode-sign once (mandatory; missing
credentials fail with `BLOCKED_EXTERNAL` before any build), verify the
signature, compute the final distributed hash, and retain the immutable
candidate artifact; it SHALL NOT create a GitHub Release. STAGE B (a separate
publication workflow) SHALL accept the Stage A workflow run id and the
external evidence record, download the EXISTING artifact from that run
without rebuilding, republishing, re-signing, or modifying the executable,
re-verify every production condition against the downloaded bytes, and
publish those exact bytes. The published executable SHA SHALL equal the
Stage A executable SHA SHALL equal the evidence `artifactSha256`.

#### Scenario: A second build never happens
- **WHEN** a production candidate exists and humans have qualified it
- **THEN** publication consumes the retained Stage A artifact and performs
  zero compilation and zero signing operations

#### Scenario: Publication without the retained artifact is refused
- **WHEN** the Stage A run does not exist, did not succeed, is not the
  candidate-preparation workflow, has no live uniquely-named candidate
  artifact, or the artifact is expired
- **THEN** publication fails closed and no release or tag is created

### Requirement: Stage B binds to the exact source run and artifact
Stage B publication SHALL bind the downloaded artifact to the exact source
workflow run and exact metadata, never to an artifact name alone: the run's
`head_sha` SHALL equal the manifest `sourceCommitSha`; the manifest
`workflowRunId` SHALL equal the requested run id; the manifest `releaseMode`
SHALL be `PRODUCTION` (qualification-only artifacts SHALL be rejected); the
artifact name SHALL match `tabdock-candidate-<sha>-<run-id>`; and the
external evidence SHALL name the same run id and artifact name.

#### Scenario: Evidence or manifest from another run is refused
- **WHEN** the evidence or the manifest names a different workflow run or
  artifact than the one being published
- **THEN** publication fails closed

### Requirement: The project version is the single authority
`TabDock.csproj <Version>` SHALL be the single authoritative semantic
version. Release qualification SHALL read it from the exact candidate source,
SHALL treat any workflow version input as an EXPECTED value that must agree,
SHALL record the project version in the manifest, and SHALL require the
published executable's reported semantic version and informational version
to carry it. Publication SHALL additionally require the manifest version, the
recorded binary identity, and the project `<Version>` at the candidate SHA to
agree.

#### Scenario: A workflow version cannot override the project
- **WHEN** release tooling is invoked with an expected version that differs
  from the project's authoritative `<Version>`
- **THEN** qualification fails before any build work and the manifest never
  records the operator-supplied version

### Requirement: The production tag is derived, not input
The production release tag SHALL be derived as `v<semanticVersion>` from the
authoritative version; the publication workflow SHALL NOT accept a tag input,
so arbitrary or mismatched tags (`stable-final`, `v2.0.0` for version
`1.0.0`) are structurally impossible and the protected `v*` tag namespace
applies by construction.

#### Scenario: An arbitrary tag cannot be created
- **WHEN** a production release is published
- **THEN** the tag is exactly `v<semanticVersion>` at the exact candidate SHA

### Requirement: Windows compatibility is a production evidence gate
Because v1.0.0 advertises Windows 10 and Windows 11 x64, production
publication SHALL require `windowsCompatibility` evidence with PASS entries
for both a real supported Windows 10 x64 system and a real Windows 11 x64
system, each recording the OS build, operator, ISO-8601 completion time, the
`--selftest-native-abi` evidence, and the qualification summary. Missing,
malformed, `FAIL`, or `BLOCKED_EXTERNAL` Windows 10 or Windows 11 evidence
SHALL block production publication. Windows 10 evidence SHALL NOT be
fabricated; removing the Windows 10 support claim is an explicit product
decision, never a silent gate-simplification.

#### Scenario: Windows 10 remains unproven
- **WHEN** a production release is requested without real Windows 10 x64
  PASS evidence (build and native ABI self-test recorded)
- **THEN** publication is refused

### Requirement: External evidence is traceable and timestamp-quality
External evidence (schemaVersion 2) SHALL record `candidateWorkflowRunId`
(the numeric Stage A run id) and `candidateArtifactName` (the downloaded
Stage A artifact name) in addition to the source SHA and final artifact hash.
Every `completedAt` field SHALL parse as an ISO-8601 timestamp and SHALL not
be materially in the future.

#### Scenario: Future or malformed timestamps are refused
- **WHEN** an evidence gate carries a non-ISO-8601 `completedAt` or one in
  the future beyond the clock-skew tolerance
- **THEN** the evidence is invalid and publication is refused
