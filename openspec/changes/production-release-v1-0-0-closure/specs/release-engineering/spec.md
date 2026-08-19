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
External evidence SHALL be exactly `schemaVersion: 2` (no other value accepted)
and SHALL record `candidateWorkflowRunId` (the numeric Stage A run id) and
`candidateArtifactName` (the downloaded Stage A artifact name) in addition to
the source SHA and final artifact hash. Every `completedAt` field SHALL parse
as an ISO-8601 timestamp and SHALL not be materially in the future (5-minute
clock-skew tolerance). Any wrong schema version, stale SHA/hash/run/artifact
binding, or future `completedAt` SHALL fail publication closed; missing evidence
SHALL remain `BLOCKED_EXTERNAL` and no workflow SHALL mark an unavailable gate
`PASS`.

#### Scenario: Future or malformed timestamps are refused
- **WHEN** an evidence gate carries a non-ISO-8601 `completedAt` or one in
  the future beyond the clock-skew tolerance
- **THEN** the evidence is invalid and publication is refused

#### Scenario: Stale or wrong-schema evidence is refused
- **WHEN** the evidence carries `schemaVersion != 2` or a `sourceCommitSha` /
  `artifactSha256` / `candidateWorkflowRunId` / `candidateArtifactName` that
  does not equal the Stage A run/artifact/hash being published
- **THEN** publication is refused and the evidence cannot be reused for another candidate

#### Scenario: Unavailable gates cannot be marked PASS
- **WHEN** external evidence is absent or a mandatory gate is unavailable
- **THEN** the gate remains `BLOCKED_EXTERNAL` / `BLOCKED_ENVIRONMENT` and the
  publication gate refuses; no workflow path marks it `PASS` by availability

### Requirement: External gates have an exact vocabulary and lifecycle
Every externally visible gate status SHALL be exactly one of `PASS`, `FAIL`,
`BLOCKED_EXTERNAL`, or `BLOCKED_ENVIRONMENT` (the only allowed
`BLOCKED_ENVIRONMENT` refinement is `BLOCKED_NO_MIXED_DPI_HARDWARE` for the
mixed-DPI gate). `PASS` SHALL require all mandatory gates present with
`status == "PASS"`, a non-empty `operator` and `evidence`, and an ISO-8601
`completedAt` not in the future; `BLOCKED_EXTERNAL` SHALL mean the prerequisite
(credentials/hardware/real Windows environment) does not exist. Every gate SHALL
have an exact prerequisite, exact command/procedure, expected evidence format,
and exact artifact SHA + workflow run + artifact name binding (see
`docs/release/publication-gates.md` external gate table). Publication tooling
SHALL fail closed when evidence says `PASS` but prerequisites are unmet, and
missing evidence SHALL fail closed (`BLOCKED_EXTERNAL`).

#### Scenario: Ambiguous manual steps are rejected
- **WHEN** external gate documentation lacks any of: exact prerequisite, exact
  command/procedure, expected evidence format, or exact SHA/run/artifact binding
- **THEN** the gate is not considered precise and must be repaired before production publication

### Requirement: External evidence is authored once and bound to provenance
The operator SHALL author `release-external-evidence.json` exactly once per
candidate with `schemaVersion: 2` and exact bindings `sourceCommitSha` /
`artifactSha256` (`finalSignedSha256`) / `candidateWorkflowRunId` /
`candidateArtifactName` equal to the Stage A bytes just verified by hash, and
every `completedAt` at actual completion time (ISO-8601, not in the future).
Any reuse with a different SHA/hash/run/artifact, any future `completedAt`, or
any wrong schema version SHALL fail the publication gate.

#### Scenario: Reused evidence for another candidate is refused
- **WHEN** evidence authored for one candidate is presented for a different
  source commit, artifact hash, or Stage A run
- **THEN** the publication gate rejects it as stale provenance

### Requirement: Publication policy is trusted-policy isolated from the candidate
Stage B publication SHALL evaluate the candidate exclusively with release-policy
code loaded from a TRUSTED policy checkout of the workflow revision being
executed (`policy/scripts/release-tooling.ps1`), never from the candidate
source, the candidate artifact, the candidate manifest, or old Stage A policy
code. The trusted policy checkout SHALL be the revision of the executing
`publish-release.yml`: the workflow SHALL require `github.ref ==
refs/heads/main` and `github.workflow_sha == github.sha` (for
`workflow_dispatch`, `github.sha` is the last commit on the dispatched
branch — the revision whose workflow file GitHub executes). The candidate
source SHALL be checked out into a separate `candidate-source/` tree used
only as data (project version, product metadata, source identity), and its
scripts SHALL NEVER be executed, dot-sourced, or imported. The candidate
executable SHALL NOT be executed by Stage B in either job: candidate source
and candidate artifact SHALL be handled strictly as data (read, parse, hash,
certificate/signature verification, asset upload), no path under them SHALL
occur in an execution position, and binary identity SHALL be validated from
the trusted manifest `buildIdentity` record produced by trusted Stage A and
from independent Authenticode/RFC3161 verification of the bytes — never by
running the candidate (no version self-report, no native-ABI self-test, no
candidate helper).

#### Scenario: An old candidate is not evaluated under its own policy
- **WHEN** a candidate was produced by an older release-policy generation
- **THEN** it is evaluated exclusively under the CURRENT trusted policy and
  is rejected whenever it no longer satisfies the CURRENT requirements

#### Scenario: Hostile candidate tooling cannot change the verdict
- **WHEN** the candidate tree contains scripts that would redefine the
  publication gate
- **THEN** the verdict is unchanged because the candidate files are never
  loaded by Stage B

#### Scenario: Stage B never executes the candidate
- **WHEN** the publish workflow runs
- **THEN** no candidate process is launched in either job; identity is
  validated from trusted records (Stage A run head SHA, candidate-source
  checkout SHA, the manifest `buildIdentity` record), on-disk hashes, and
  signature verification — never by executing the candidate

#### Scenario: Candidate paths never occur in an execution position
- **WHEN** the publish workflow is statically inspected
- **THEN** no path under `candidate-source/` or `candidate-artifact/` occurs
  in a `run:` step except as data (hash, parse, certificate inspection,
  signature verification, asset upload)

### Requirement: Release-policy schema contract rejects old policy generations
Stage A production manifests SHALL record `releasePolicySchemaVersion` (the
CURRENT trusted policy schema under which the candidate was produced). The
CURRENT Stage B policy SHALL require a manifest schema at least the current
minimum (`Get-MinimumAcceptedProductionPolicySchema`, currently 3) and SHALL
reject candidates with an absent or stale schema (fail closed). An old
candidate SHALL NOT become valid merely because its old policy would have
accepted itself. Current production policy SHALL additionally require all
mandatory production fields: `signingProvider`, `signingKeyProtection`,
`timestampStatus`, `signingCertificateSubject`, `signingCertificateThumbprint`,
`signingCertificateIssuer`, `signingCertificateValidFrom`,
`signingCertificateValidTo`, `signingCertificateEku`, `releaseMode ==
PRODUCTION`, `finalSignedSha256`, and `workflowRunId`.

#### Scenario: Missing or stale schema is rejected
- **WHEN** a manifest has no `releasePolicySchemaVersion` or a schema below
  the current minimum
- **THEN** the publication gate rejects the candidate

### Requirement: Stage A production preparation uses the trusted dispatch contract
Production candidate preparation SHALL be dispatched from `main` with the
requested SHA equal to the trusted dispatch SHA (`github.ref ==
refs/heads/main`, `inputs.sha == github.sha`) and the workflow-file revision
equal to the dispatch commit (`github.workflow_sha == github.sha`); any
disagreement SHALL fail BEFORE any credentials are materialized and BEFORE
any restore/build, so release-policy code and candidate source start from
the same trusted release-policy generation. RC qualification SHALL continue
to support arbitrary SHAs.

#### Scenario: A mismatched production SHA is refused before build
- **WHEN** `inputs.sha != github.sha` (or the workflow is not dispatched
  from main, or the workflow-file revision differs)
- **THEN** the run fails before any signing credentials, restore, or build

### Requirement: Production publisher identity policy is mandatory
Production Stage A SHALL require the CURRENT expected publisher identity
(`SIGNING_EXPECTED_SUBJECT`, a stable subject/publisher identity — never a
hard-coded rotating thumbprint): the preflight SHALL block without it, the
signer SHALL fail on mismatch, and the signed certificate subject SHALL
equal it. Stage B SHALL independently require CURRENT trusted publisher
policy == manifest `signingCertificateSubject` == the certificate subject
read from the actual downloaded bytes; matching manifest and file that
consistently record the WRONG publisher SHALL fail.

#### Scenario: Wrong publisher fails against current policy
- **WHEN** the manifest and the file agree on a publisher the CURRENT
  policy does not approve
- **THEN** publication is refused

### Requirement: Production Stage A receives no exportable-PFX secrets
The production Stage A workflow SHALL NOT expose the legacy local-PFX
secrets to its job (least privilege: the production HSM job must not receive
unused exportable-PFX secrets); local-PFX support SHALL remain only in RC
qualification and local/private development workflows and tests.

#### Scenario: The production HSM job has no PFX secrets
- **WHEN** a production Stage A run executes
- **THEN** the legacy local-PFX secrets are not among the environment
  variables of any production job step (asserted statically), while RC and
  local workflows keep them

### Requirement: The signing control-plane action is pinned to an immutable SHA
The DigiCert signing action (`digicert/code-signing-software-trust-action`)
SHALL be pinned to its full immutable commit SHA
(`fae23a455ba4bde62b64fd7cb2f81ade788f5a95`, v1.2.1) and SHALL NOT use a
mutable major tag. Updates SHALL be intentional: review the new version, pin
the new full SHA, and run the release-tooling regression suite.

#### Scenario: The signing action is never floated on a mutable tag
- **WHEN** the production Stage A workflow is inspected
- **THEN** the DigiCert action reference is the full 40-character immutable
  commit SHA, never `@v1` or another mutable ref, and the human-readable
  release version is recorded beside the pin

### Requirement: Stage B publication is least-privilege job-split
Stage B SHALL separate verification from publication: JOB 1 (`verify`,
permissions `contents: read`) SHALL perform all release gates (candidate
files handled strictly as data — NO candidate execution) and SHALL NOT hold
`contents: write`;
JOB 2 (`publish`, `needs: verify`, permissions `contents: write`) SHALL
obtain the verified same-run handoff, re-download the exact Stage A bytes,
perform the final hash identity check, and create the release, and SHALL NOT
execute candidate code, build, sign, or contact a signing provider. No
candidate execution SHALL occur in Stage B; the verify job holds no
write-capable credential while it evaluates the candidate.

#### Scenario: The write-capable job cannot execute candidate code
- **WHEN** the publication job runs
- **THEN** it performs only the final hash identity check and the release
  mutation; the static workflow tests prove it contains no build, sign,
  candidate-script execution, or provider contact

### Requirement: Release checkouts do not persist credentials
Every `actions/checkout` step in the release workflows
(`publish-release.yml` — the trusted policy checkouts and the
candidate-source checkout — `prepare-release-candidate.yml`, `release.yml`,
and `build.yml`) SHALL set `persist-credentials: false`, because none of
these workflows performs an authenticated git push from a checkout and no
credentials SHALL be persisted in `.git/config` on the runner. The cross-run
`actions/download-artifact@v7` steps SHALL keep their explicitly passed
`github-token` inputs (that mechanism genuinely requires them).

#### Scenario: A release checkout never persists credentials
- **WHEN** a release workflow checkout step is statically inspected
- **THEN** it sets `persist-credentials: false` and no workflow sets
  `persist-credentials: true`

### Requirement: The release-tooling regression suite is a hosted-CI gate
The build workflow SHALL invoke `scripts/release-tooling-tests.ps1`; the
suite SHALL be deterministic, require no network beyond ordinary restore,
require no production credentials, perform no publication and no real
signing, and SHALL block the build workflow on any failure, so the
release-control suite is an exact-SHA hosted-CI gate.

#### Scenario: A release-policy regression blocks the build
- **WHEN** any release-tooling regression case fails on a hosted CI run
- **THEN** the build workflow fails at that exact commit, making the
  release-control suite a machine-enforced gate

### Requirement: Timestamp verification is explicit and provenance-bound
Signature verification SHALL use `signtool verify /pa /v /tw` (an
untimestamped file yields a warning result — non-zero — and fails closed).
The RFC3161 timestamper identity (subject, thumbprint) SHALL be recorded in
the production manifest and SHALL be cross-checked against the actual bytes
at Stage B; `timestampStatus == VERIFIED` remains mandatory.

#### Scenario: An untimestamped or warned timestamp never passes
- **WHEN** the signature has no valid RFC3161 timestamp, the manifest
  records a non-VERIFIED timestamp status, or the timestamper identity is
  absent or disagrees with the actual bytes
- **THEN** Stage A fails and the Stage B publication gate rejects the
  artifact
