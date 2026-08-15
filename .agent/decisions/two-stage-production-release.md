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

## Addendum (2026-08-15): release-policy trust-boundary hardening

Independent review (round 3) found a HIGH-severity trust-boundary defect: the
candidate being evaluated supplied the policy code that decided whether it
may be published. Stage B checked out the candidate SHA into the default
workspace and dot-sourced `scripts/release-tooling.ps1` from that candidate
checkout, so an old candidate could be evaluated under its own (old) release
policy. This addendum records the hardening.

- **Trusted policy isolation (P0):** Stage B now physically separates three
  trust domains — `policy/` (TRUSTED release policy), `candidate-source/`
  (candidate source, DATA ONLY), `candidate-artifact/` (candidate bytes, DATA
  ONLY). All release-policy code is dot-sourced EXCLUSIVELY from
  `policy/scripts/release-tooling.ps1`. The policy checkout is the revision
  of `publish-release.yml` being executed: for `workflow_dispatch` on main,
  `github.sha` is the last commit on main (the revision whose workflow file
  GitHub executes) and `github.workflow_sha` is the commit SHA of the
  workflow file; Stage B FAILS unless `github.ref == refs/heads/main` AND
  `github.workflow_sha == github.sha`, so the policy revision can never be
  replaced by the candidate. Candidate scripts are never executed,
  dot-sourced, or imported for release approval (static tests assert the
  trusted module paths).
- **Policy schema contract (P0):** `releasePolicySchemaVersion` (current = 3)
  is recorded by Stage A in the production manifest;
  `Get-MinimumAcceptedProductionPolicySchema` (3) makes the CURRENT policy
  reject candidates with an absent or older schema. Schema generations: 1 =
  pre-provider two-stage era (51f7001: candidate-controlled policy), 2 =
  provider-allowlist era (no schema/publisher/timestamper contract), 3 =
  current. An old candidate that its old policy would have accepted is
  rejected whenever it no longer satisfies the current policy (fail closed).
- **Stage A policy trust boundary (P0):** production candidate preparation
  requires `github.ref == refs/heads/main`, `inputs.sha == github.sha`, and
  `github.workflow_sha == github.sha` — the first step, BEFORE any checkout,
  credentials, restore, or build. Policy code and candidate source therefore
  start from the same trusted release-policy generation. RC qualification
  still supports arbitrary SHAs.
- **Mandatory publisher identity (P1):** `SIGNING_EXPECTED_SUBJECT` (current
  publisher policy, repository variable; stable subject identity, never a
  rotating thumbprint) is REQUIRED for production: Stage A preflight blocks
  without it, `sign-release.ps1` fails without/on mismatch under the
  production gate, and the Stage B gate requires CURRENT policy == manifest
  subject == actual certificate subject ("actual == manifest" alone is never
  sufficient).
- **Least privilege (P1):** Stage B splits into JOB 1 `verify` (contents:
  read; all gates + all read-only candidate identity execution; documented
  deviation: actions: write solely to upload the same-run verification
  handoff artifact) and JOB 2 `publish` (needs: verify; contents: write; no
  candidate execution, no build/sign; only the final hash identity check and
  the release mutation). All untrusted/candidate execution happens before
  write credentials exist.
- **DigiCert action pin (P1):** `digicert/code-signing-software-trust-action`
  pinned to the full immutable SHA `fae23a455ba4bde62b64fd7cb2f81ade788f5a95`
  (v1.2.1; verified via the GitHub API that v1.2.1 and v1 both resolve to
  it). Updates are intentional: review, pin the new full SHA, run the
  release-tooling regression suite.
- **Hosted release-tooling gate (P1):** `build.yml` now invokes
  `scripts/release-tooling-tests.ps1`, making the release-control suite an
  exact-SHA hosted-CI gate (118 cases, deterministic, no credentials, no
  publication).
- **Legacy PFX removal (P1):** production Stage A no longer receives the
  legacy local-PFX secrets; least-privilege invariant: the production HSM
  job must not receive unused exportable-PFX secrets.
- **Timestamp hardening (P2):** signtool verification now uses
  `verify /pa /v /tw` (untimestamped => warning result => non-zero => fail
  closed); the RFC3161 timestamper identity (subject/thumbprint) is recorded
  in the manifest and cross-checked against the bytes at Stage B.
- **Client-auth hygiene (P2):** the DigiCert client-authentication P12 is
  materialized with PowerShell/.NET into a random private path under
  runner.temp (never bash base64), never printed/uploaded/logged, and
  deleted by an always-run cleanup step.
- **Documentation:** `docs/release/publication-gates.md` and
  `docs/release/code-signing.md` updated; OpenSpec change section 14;
  `scripts/release-tooling-tests.ps1` extended from 96 to 118 cases
  (old-pre-HSM candidate rejection, policy-schema contract, candidate-policy
  isolation, publisher policy, Stage A/B dispatch contracts, action pin, job
  split, hosted gate, timestamp policy) — all PASS locally.
