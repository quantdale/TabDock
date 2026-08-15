# Decision: two-stage production release (no rebuild/re-sign after human qualification)

**Date:** 2026-08-15
**Status:** accepted

## Context

The release-gate hardening at `a653d3e4` left a structural defect: the
documented production sequence qualified and signed an artifact in Run 1,
then REBUILT and RE-SIGNED the same source in Run 2 and compared the
evidence hash against the rebuilt hash. Authenticode signing with RFC3161
timestamping mutates the executable and is not byte-reproducible across
runs, so Run 2 could normally never equal the human-qualified Run 1 bytes —
publication of the exact qualified artifact was structurally impossible.
Independent review also found that the workflow `version`/`tag` inputs were
operator-controlled rather than derived, and that the documented Windows 10
x64 compatibility requirement was not enforced by the publication gate.

## Decision

Split production publication into two disjoint workflows sharing no build or
signing step:

- **STAGE A `prepare-release-candidate.yml`** — exact SHA -> qualify -> build
  once -> Authenticode sign once (mandatory; `BLOCKED_EXTERNAL` preflight
  without credentials) -> verify -> final SHA-256 -> manifest/checksums ->
  immutable artifact `tabdock-candidate-<sha>-<run-id>`. Never creates a
  GitHub Release.
- **STAGE B `publish-release.yml`** — accepts the Stage A run id + schema-v2
  evidence; resolves the run and artifact via the GitHub API (exists, correct
  workflow, completed/successful, live unique artifact), downloads the EXACT
  artifact cross-run (`download-artifact@v7` `run-id`/`repository`/
  `github-token`), re-verifies every production condition against the
  downloaded bytes, and publishes those exact bytes with the DERIVED tag
  `v<semanticVersion>`. The workflow contains no build, sign, or
  qualification invocation (enforced by a static test).

`TabDock.csproj <Version>` is the single version authority: qualification
reads it from the exact candidate, workflow `version` inputs are
expected-only, the manifest records the project version, and the binary's
semantic + informational versions must carry it; Stage B re-checks project
version at the candidate SHA == manifest == downloaded binary `--version`.

The evidence schema moves to v2: it gains `candidateWorkflowRunId` and
`candidateArtifactName` (evidence is run/artifact-bound, not hash-bound
alone), a mandatory `windowsCompatibility` gate with PASS entries for real
Windows 10 x64 and Windows 11 x64 (build, operator, ISO-8601 completedAt,
`nativeAbiEvidence`, evidence), and ISO-8601/not-materially-future
`completedAt` validation.

## Consequences

- The published bytes are always the human-qualified bytes: published SHA ==
  Stage A SHA == evidence `artifactSha256`; zero rebuild, zero re-sign.
- Publication is impossible when the run/artifact/SHA/hash/evidence disagree,
  when the tag is not `v<semanticVersion>`, when Windows 10/11 compatibility
  evidence is missing, or when signing is absent/invalid/mock.
- RC qualification (`release.yml`) is now qualification-only with no
  publication path; RC artifacts record `releaseMode=QUALIFICATION_ONLY` and
  can never be published.
- Cost: two dispatches instead of one; the operator must copy run id +
  artifact name from the Stage A summary into the evidence record.

## Evidence

- `scripts/release-tooling-tests.ps1`: 96 deterministic cases (authority
  chain, tag derivation, cross-run binding, Windows gate, completedAt
  quality, signing-provider policy, static workflow guarantees) — all PASS
  locally.
- Official `actions/download-artifact` v7 documentation (run-id/repository/
  github-token cross-run inputs) verified from the action README.
- `docs/release/publication-gates.md`, `docs/release/compatibility-matrix.md`,
  `docs/release/final-smoke.md`, `README.md`, OpenSpec change
  `production-release-v1-0-0-closure` (section 12).

## Addendum (2026-08-15): provider-abstracted signing with a non-exportable key

The signing implementation is corrected so the production pipeline no longer
requires the public code-signing private key to exist as an exportable PFX
(a base64 PFX GitHub Secret written to the runner). Modern publicly trusted
code-signing keys must remain inside compliant hardware cryptographic
modules / HSM-backed signing services.

- **Signer abstraction:** `SIGNING_PROVIDER` selects the backend
  (`not-configured`, `local-pfx`, `digicert-stm`, `mock-test`);
  `sign-release.ps1` returns ONE structured contract (Status, Verification,
  FinalSha256, Provider, KeyProtection, TimestampStatus, certificate
  identity) so release-qualify and the workflows never know or care how
  signing happens.
- **Production provider policy:** the allowlist
  (`Get-ApprovedProductionSigningProviders`) contains exactly `digicert-stm`
  (DigiCert Software Trust Manager; official action
  `digicert/code-signing-software-trust-action@v1` setup + the official
  `smctl sign --simple` invocation, verified against the current official
  action source) with key protection class `CLOUD_HSM` (non-exportable).
  Stage A fails `BLOCKED_EXTERNAL` BEFORE any build when the provider is not
  approved or its credentials are incomplete; Stage B rejects
  `local-pfx`, `mock`, `not-configured`, unknown, and missing
  provider/key-protection metadata. Microsoft Artifact Signing is
  deliberately NOT implemented (geography-restricted Public Trust); the
  allowlist makes it a future policy change.
- **local-pfx reclassified:** still supported for development/private/RC
  use, explicitly documented as NOT the approved public-GA signer, and
  rejected by production at every layer.
- **Provider-independent verification:** after signing, Windows tooling
  verifies the actual EXE — `signtool verify /pa`, RFC3161 timestamp
  (mandatory, `timestampStatus=VERIFIED`), and the signed certificate
  identity (subject, thumbprint, issuer, validity, code-signing EKU), which
  is recorded in the manifest and cross-checked at Stage B. Stage B never
  contacts the provider and never re-signs.
- **Mock safety:** mock modes require `SIGNING_PROVIDER=mock-test`, refuse
  real material, and their results always report Provider=mock-test /
  KeyProtection=MOCK_TEST — a mock result can never claim an approved
  provider.
- Evidence: official DigiCert action README/source captured in
  `.agent/investigations/digicert-research/`; 96 deterministic regression
  cases; docs `docs/release/code-signing.md` (new), updated
  `publication-gates.md`/`final-smoke.md`/README, OpenSpec change section 13.
  Real production signing remains `BLOCKED_EXTERNAL` (credentials not
  configured; no candidate exists yet).
