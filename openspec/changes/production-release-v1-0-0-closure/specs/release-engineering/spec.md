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
SHALL re-verify the manifest (`PASS`, matching SHA, on-disk hash equal to the
manifest) before consuming the preserved artifact, and SHALL verify the
published release assets afterwards. No source modification and no second
compilation SHALL occur at publication time.

#### Scenario: Publication re-verification fails
- **WHEN** the preserved artifact's on-disk hash differs from the manifest
- **THEN** publication is refused

#### Scenario: Tag identity
- **WHEN** a release is published with tag `v<semver>`
- **THEN** the tag resolves to the exact qualified candidate SHA

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
