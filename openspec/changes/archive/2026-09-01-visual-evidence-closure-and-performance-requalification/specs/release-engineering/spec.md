# release-engineering delta — exact Release v1.1 requalification

## MODIFIED Requirements

### Requirement: Release qualification is exact-SHA and immutable

Release qualification SHALL operate on one exact candidate commit and exact
executable bytes after visual closure and measured performance
re-qualification: the requested SHA SHALL equal `HEAD`, the intended source
and test/spec/planning files SHALL have explicit Git provenance, the working
tree SHALL be clean for the release candidate, the published executable's
embedded source commit SHALL equal the candidate SHA, and the executable's
fresh computed SHA-256 SHALL be the identity used by the release manifest,
qualification plan, ValidationDriver child/parent manifests, performance
report, visual evidence, and qualification bundle. Physical qualification
SHALL accept `--tabdock <path>` and SHALL refuse to rebuild or replace bytes
being qualified. The qualified artifact SHALL be retained without a second
compilation.

A historical artifact hash SHALL NOT be reused as evidence for the new
candidate. A release candidate SHALL not claim visual/performance closure when
Milestone A is incomplete, measured budgets lack provenance, visual/review
bindings fail, or a required supervised condition is blocked.

#### Scenario: The final candidate is qualified once

- **WHEN** A and B have accepted evidence and E qualifies a Release v1.1
  candidate
- **THEN** the exact clean candidate SHA, embedded source identity, and fresh
  executable bytes/hash agree across all release, visual, performance, and
  bundle records without a replacement rebuild

#### Scenario: A stale artifact is supplied

- **WHEN** a requested SHA, embedded source identity, executable hash, visual
  evidence, or performance report names another tree or artifact
- **THEN** qualification fails before evidence publication and does not reuse
  the historical artifact as a substitute

 
#### Scenario: Mismatched SHA is refused

- **WHEN** a requested release SHA differs from `HEAD` or a qualification bundle
  names another source SHA
- **THEN** qualification fails before any build work or evidence publication

#### Scenario: The published executable must be the qualified executable

- **WHEN** a release candidate is qualified
- **THEN** the executable that passes `--version`, native ABI, and permitted
  qualification checks is the same binary whose final hash is recorded in
  every downstream evidence record
### Requirement: Stable releases are intentional and fail-closed

The release workflow SHALL remain dispatch-only and exact-commit based. Before
publication it SHALL run the canonical Release validation, dependency audit,
unit tests, ValidationDriver/GuineaPig deterministic self-tests, strict
OpenSpec validation, visual manifest/review compatibility gates, and measured
resource/performance gates. Publication SHALL independently re-verify every
production condition against the preserved on-disk artifact: manifest PASS,
exact source SHA, semantic version, on-disk hash, `SHA256SUMS.txt`, schema-valid
external evidence when required, and signing state when mandatory. It SHALL
also verify that historical non-visual manifests/bundles remain valid under
 their declared schemas. No source modification and no second compilation
shall occur at publication time.

A blocked physical, signing, vision, or external condition SHALL remain in its
canonical blocked vocabulary; it SHALL NOT be converted to PASS merely because
synthetic visual/performance checks passed.

#### Scenario: A required A/B gate is incomplete

- **WHEN** publication is requested before supervised visual closure or
  measured resource budgets are accepted
- **THEN** publication is refused and no Release v1.1 artifact is claimed

#### Scenario: Publication re-verification fails

- **WHEN** the preserved executable, visual/performance evidence, or checksums
  differ from their exact-candidate manifest values
- **THEN** publication is refused and no release/tag is created

#### Scenario: Historical compatibility is checked

- **WHEN** the Release v1.1 validation checks a supported pre-visual bundle
- **THEN** the old bundle verifies under its declared schema without
  synthesizing visual or performance evidence
 
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
