# Production Publication Gates (External Evidence)

**Status: the machine-enforced gate is implemented; the human evidence is NOT
PERFORMED — `BLOCKED_EXTERNAL` until real operators qualify the exact final
artifact.**

This document defines the trust model for TabDock production publication, the
`release-external-evidence.json` schema (v2), and the exact two-stage dispatch
sequence for a production release. The enforcement described here is code, not
intent: `scripts/release-tooling.ps1` (shared by the publication workflow and
`scripts/release-tooling-tests.ps1`) refuses publication when any required
condition fails.

The signing architecture (provider abstraction, the approved non-exportable
HSM/cloud signer, and why local-PFX is not the public-GA signer) is defined in
`docs/release/code-signing.md`.

## Trust model: the CURRENT trusted policy evaluates an inert candidate

Authenticode signing with RFC3161 timestamping mutates the executable and is
not expected to reproduce an identical signed file on a later run, and even
an unsigned publish may not be byte-reproducible across runner/SDK changes.
The production chain therefore NEVER rebuilds or re-signs after the final
artifact exists, and Stage B NEVER executes the candidate: a program under
evaluation is not an independent authority for its own release eligibility.

```
CURRENT TRUSTED POLICY P
        |
        +----------------------------+
        |                            |
        v                            v
candidate source S              candidate artifact H
READ/PARSE ONLY                 READ/HASH/VERIFY ONLY
NO EXECUTION                    NO APPLICATION EXECUTION
        |                            |
        +-------------+--------------+
                      |
                      v
              CURRENT POLICY P
                evaluates H
                      |
              human evidence
                      |
                      v
                 ALLOW/DENY
                      |
                      v
               publish exact H
```

The candidate NEVER supplies the policy that decides whether it may be
published (P0 trust boundary), and Stage B NEVER executes the candidate (P0
trust-boundary closure): candidate source and candidate artifact are DATA
ONLY in both Stage B jobs. In Stage B the three trust domains are physically
separated:

- `$GITHUB_WORKSPACE/policy` — TRUSTED release policy: the revision of
  `publish-release.yml` being executed, checked out explicitly
  (`github.ref == refs/heads/main` and `github.workflow_sha == github.sha`
  are required; for `workflow_dispatch`, `github.sha` is the last commit on
  the dispatched branch = the revision whose workflow file GitHub executes).
  All release-policy code is dot-sourced exclusively from
  `policy/scripts/release-tooling.ps1`.
- `$GITHUB_WORKSPACE/candidate-source` — CANDIDATE SOURCE (DATA ONLY):
  readable for `TabDock.csproj`/product metadata/source identity; its
  scripts are NEVER executed, dot-sourced, or imported, and no path under it
  appears in an execution position.
- `$GITHUB_WORKSPACE/candidate-artifact` — CANDIDATE ARTIFACT (DATA ONLY):
  the downloaded immutable bytes are hashed, parsed,
  certificate-inspected, and Authenticode/RFC3161-verified — NEVER launched
  (no version self-report, no native-ABI self-test, no helper process).

All policy evaluation is performed by CURRENT trusted policy code, and the
release-policy schema contract (`releasePolicySchemaVersion`, minimum 3)
rejects candidates produced under older policy generations (fail closed).

### Why Stage B does not execute the candidate

The identity chain is complete WITHOUT running the candidate:

```
TRUSTED STAGE A POLICY
    -> authoritative TabDock.csproj <Version>
    -> build exact source SHA
    -> execute/qualify the UNSIGNED built executable while Stage A is
       already the trusted build environment
    -> record buildIdentity in the manifest
    -> sign the same executable in place
    -> independent Authenticode verification
    -> RFC3161 timestamp verification
    -> final signed SHA H
    -> manifest + SHA256SUMS describe H
    -> retain immutable H
```

Stage B then validates the manifest contract from TRUSTED RECORDS and data
reads — it never asks the candidate binary to self-report:

- `manifest.sourceCommitSha == run head SHA == candidate-source checkout SHA`;
- candidate-source `TabDock.csproj <Version>` (parsed as XML data) ==
  `manifest.semanticVersion`;
- `manifest.buildIdentity.semanticVersion` and
  `.informationalVersion` == `manifest.semanticVersion` (the `buildIdentity`
  record was generated and verified by TRUSTED Stage A);
- on-disk SHA == `manifest.artifactSha256` == `SHA256SUMS.txt` ==
  `finalSignedSha256`;
- `workflowRunId` and artifact-name bindings;
- independent Authenticode + RFC3161 verification and certificate identity
  of the downloaded bytes.

Every one of these is enforced by the fail-closed gate
(`Test-PublicationEligibility`) in the verify job; the final hash identity
check in the publish job re-proves the byte identity. No candidate code is
executed in either job.

```
SOURCE SHA
-> build once
-> automated qualification
-> APPROVED HSM/CLOUD signer signs once (non-exportable private key)
-> signature verify (provider-independent)
-> RFC3161 timestamp verify
-> certificate identity record (== current publisher policy)
-> FINAL distributed hash
-> immutably retain signed candidate
-> humans download THAT EXACT artifact
-> final Windows smoke on that exact hash
-> physical mixed-DPI on that exact hash
-> Windows 10/11 compatibility evidence on that exact hash
-> external evidence record
-> publish the PREVIOUSLY RETAINED artifact
-> NO build, NO sign, NO binary mutation, NO provider contact
```

The artifact being published must be byte-identical to the artifact whose
hash appears in the external evidence.

## Two-stage release architecture

### STAGE A — `prepare-release-candidate.yml` (prepare production candidate)

A manually dispatched workflow that:

1. FIRST verifies the trusted production dispatch contract (before any
   checkout, credentials, restore, or build): the workflow must be
   dispatched from `main` (`github.ref == refs/heads/main`), the requested
   SHA must equal the trusted dispatch SHA (`inputs.sha == github.sha`, the
   last commit on the dispatched branch), and the workflow-file revision
   must be the dispatch commit (`github.workflow_sha == github.sha`).
   Policy code and candidate source therefore start from the SAME trusted
   release-policy generation; production candidates are never prepared from
   arbitrary historical SHAs (RC qualification in `release.yml` still
   supports arbitrary SHAs);
2. checks out the exact SHA (== the trusted dispatch SHA) and verifies it;
3. runs the canonical hermetic qualification;
4. provider-aware signing preflight — the run requires an APPROVED
   production signer (`SIGNING_PROVIDER=digicert-stm`, key protection class
   `CLOUD_HSM`: non-exportable private key inside the DigiCert
   service/HSM). `not-configured`, `local-pfx`, `mock`, and unknown
   providers fail with `BLOCKED_EXTERNAL` BEFORE any build, as does an
   approved provider with incomplete credentials (only variable NAMES are
   reported) or a missing publisher identity policy
   (`SIGNING_EXPECTED_SUBJECT`, mandatory for production);
5. materializes the DigiCert client-authentication certificate with
   PowerShell/.NET into a random private path under `runner.temp` (never
   printed, never uploaded, removed by an always-run cleanup step), sets up
   the official DigiCert Software Trust Manager tooling (the action is
   PINNED to its full immutable commit SHA
   `fae23a455ba4bde62b64fd7cb2f81ade788f5a95`, v1.2.1 — a mutable major tag
   is never trusted for the signing control plane), and builds/publishes
   exactly once;
6. Authenticode-signs exactly once through `sign-release.ps1`
   (`smctl sign --simple ...`, the official simple-signing invocation);
   the production private key never exists on the runner — the runner only
   holds service-authentication material;
7. RFC3161-timestamps exactly once and VERIFIES the timestamp
   (`timestampStatus=VERIFIED` required; a missing/invalid timestamp fails);
8. verifies the signature (`signtool verify /pa /v /tw`) and records the
   signed certificate identity (subject, thumbprint, issuer, validity, EKU)
   — the subject must equal the CURRENT publisher identity policy — and
   the RFC3161 timestamper identity;
9. computes the final distributed SHA-256;
10. generates `release-manifest.json` + `SHA256SUMS.txt` describing the FINAL
    bytes (file == manifest == checksums), including the signing provenance
    (`signingProvider`, `signingKeyProtection`, `timestampStatus`,
    certificate + timestamper identity, `releasePolicySchemaVersion` =
    current schema);
11. uploads the immutable Actions artifact
    `tabdock-candidate-<sha>-<run-id>` (90-day retention);
12. records/prints: workflow run ID, artifact name, source SHA, semantic
    version, final artifact SHA-256, signing provider/certificate (the
    values the human copies into the evidence record);
13. NEVER creates a GitHub Release.

Signing is mandatory on this path. Without an approved, fully configured
production signer the run fails with `BLOCKED_EXTERNAL` and no candidate is
produced. The legacy local-PFX secrets are NOT exposed to this production
job at all (least privilege: the production HSM job must not receive unused
exportable-PFX secrets). Qualification-only unsigned RC workflows
(`release.yml`) remain separate and continue to work (`not-configured` or
`local-pfx`).

### STAGE B — `publish-release.yml` (publish existing qualified candidate)

A separate, manually dispatched publication workflow split into TWO jobs
(least privilege; see the job description below) that:

1. accepts the Stage A **workflow run ID** (and the schema-v2 evidence
   record) — never a version or tag input;
2. verifies the trusted dispatch contract (both jobs): `github.ref ==
   refs/heads/main` and `github.workflow_sha == github.sha`, then checks out
   the TRUSTED release policy at `github.sha` into `policy/` (the revision
   of this workflow being executed; the candidate can never replace it);
3. **JOB 1 `verify`** (permissions `contents: read`; `actions: write` held
   ONLY to upload the same-run verification handoff — documented deviation,
   no release/tag capability): verifies the source run with the GitHub API
   (exists in THIS repository, is the `prepare-release-candidate` workflow,
   `completed` with `conclusion=success`; its `head_sha` becomes the
   candidate SHA), locates the exact candidate artifact via the artifacts
   API (name `tabdock-candidate-<head-sha>-<run-id>`, not expired, unique),
   checks out the CANDIDATE SOURCE (data only) at that SHA, and downloads
   the existing artifact with the supported cross-run mechanism
   (`actions/download-artifact@v7` with `run-id`, `repository`, and
   `github-token`) — WITHOUT rebuilding, republishing, re-signing, or
   touching `TabDock.exe`;
4. **JOB 1 gate** (exclusively from `policy/scripts/release-tooling.ps1`):
   source SHA (run head SHA == manifest == evidence), semantic version
   (project `<Version>` at the candidate SHA == manifest == recorded binary
   identity `buildIdentity`; the binary is NEVER executed), final artifact
   hash (file == manifest == SHA256SUMS),
   manifest provenance (`releaseMode == PRODUCTION`,
   `workflowRunId == run-id`), CURRENT release-policy schema contract
   (`releasePolicySchemaVersion >= 3`), signing provenance (approved
   production provider + approved non-exportable key protection, certificate
   identity, CURRENT trusted publisher policy == manifest subject == actual
   certificate subject, verified timestamp + timestamper identity),
   Authenticode (`signtool verify /pa /v /tw` + RFC3161 timestamp
   verification of the downloaded bytes, certificate identity cross-checked
   against the manifest), schema-v2 external evidence, and Windows 10/11
   compatibility evidence;
5. **JOB 1 handoff:** writes `publication-verification.json` +
   `RELEASE_NOTES.md` (the verified same-run handoff, uploaded as
   `tabdock-verified-<run-id>`); no candidate code is executed anywhere in
   this job or in Stage B;
6. **JOB 2 `publish`** (`needs: verify`, permissions `contents: write`,
   `actions: read`; NO candidate execution, NO build/sign): downloads the
   handoff and re-downloads the EXACT Stage A bytes, performs the final
   hash identity check (on-disk `TabDock.exe` + `SHA256SUMS.txt` == handoff
   `artifactSha256`), re-derives the tag from the trusted policy module, and
   publishes those exact bytes as the GitHub Release with the DERIVED tag
   `v<semanticVersion>` at the exact candidate SHA, then verifies the
   release assets.

Stage B requires NO signing-provider credentials and never contacts the
signing provider: it validates the already-signed immutable artifact using
Windows signature verification and its retained provenance.

The key invariant:

```
PUBLISHED TabDock.exe SHA
== STAGE A TabDock.exe SHA
== EXTERNAL EVIDENCE artifactSha256
```

No second compilation. No second signing. The publish workflow contains no
build, sign, or qualification invocation at all (a deterministic static test
enforces this), and candidate scripts are never executed or imported for
release approval (the policy module is loaded only from `policy/`).

## Provenance binding (why "artifact-name = something" is never enough)

Stage B fails closed when ANY of these is untrue:

- the source workflow run does not exist (or belongs to another repository);
- the source run did not succeed;
- the source run is not the `prepare-release-candidate` workflow;
- the artifact is expired, missing, or not uniquely named
  `tabdock-candidate-<sha>-<run-id>`;
- the run's `head_sha` differs from the manifest `sourceCommitSha` (the run
  used another source commit);
- the manifest `workflowRunId` differs from the requested run id;
- the manifest `releaseMode` is not `PRODUCTION` (qualification-only/RC
  artifacts are rejected);
- the manifest SHA != requested SHA, the manifest version is malformed, or
  the recorded binary identity disagrees with the manifest version;
- the executable hash mismatch, checksum mismatch, or any triple inconsistency;
- signing was absent, mock, unverified, or fails independent verification;
- the manifest signing provider is missing, unknown, or not an approved
  production provider (local-PFX, mock, and not-configured are rejected);
- the manifest key-protection classification is missing or not the approved
  non-exportable class (`CLOUD_HSM`);
- the manifest lacks the signed-certificate identity (subject, thumbprint,
  issuer, validity window, code-signing EKU), the RFC3161 timestamper
  identity, or `timestampStatus != VERIFIED`;
- the manifest records an absent or stale `releasePolicySchemaVersion`
  (older than the current minimum): an old candidate that its old policy
  would have accepted is NEVER evaluated under its own historical policy;
- the CURRENT trusted publisher policy (`SIGNING_EXPECTED_SUBJECT`) is not
  configured, does not equal the manifest subject, or does not equal the
  signed certificate subject on the actual bytes (a manifest+file that
  consistently record the WRONG publisher still fail);
- the signed certificate on the downloaded file differs from the manifest
  record, lacks the code-signing EKU, or its RFC3161 timestamp cannot be
  verified;
- the evidence names another artifact, another source SHA, another run, or
  another artifact name.

Binding chain: run-id input -> GitHub run (`path`/`status`/`conclusion`/
`headSha`) -> artifact name (embeds SHA + run id) -> manifest (`sourceCommitSha`,
`workflowRunId`, `releaseMode`, version, binary identity,
`releasePolicySchemaVersion`) -> evidence (`candidateWorkflowRunId`,
`candidateArtifactName`, `sourceCommitSha`, `artifactSha256`) -> bytes (hash
triple + `signtool verify /pa /v /tw`) -> CURRENT trusted policy
(`policy/scripts/release-tooling.ps1` at the executing workflow revision) ->
verified same-run handoff (final hash identity check in the publish job).

## Checkout credential hardening

Every `actions/checkout@v7` step in the release workflows
(`publish-release.yml` — both trusted-policy checkouts and the
candidate-source checkout; `prepare-release-candidate.yml`; `release.yml`;
`build.yml`) sets `persist-credentials: false`: none of these workflows
performs an authenticated git push from a checkout, so no credentials are
persisted in `.git/config` on the runner. This is a static, tested property
of every release-sensitive checkout. The explicitly passed `github-token`
inputs used by the cross-run `actions/download-artifact@v7` steps remain —
that mechanism genuinely requires them — and `gh`/artifact operations use
the per-job `github.token` as before.

## Evidence schema (`release-external-evidence.json`, v2)

```json
{
  "schemaVersion": 2,
  "sourceCommitSha": "<exact 40-char candidate SHA>",
  "artifactSha256": "<exact FINAL artifact SHA-256, lowercase>",
  "candidateWorkflowRunId": "<Stage A workflow run ID that produced the candidate>",
  "candidateArtifactName": "<Stage A artifact name, e.g. tabdock-candidate-<sha>-<run-id>>",
  "finalWindowsHumanSmoke": {
    "status": "PASS",
    "completedAt": "<ISO-8601 timestamp>",
    "operator": "<human identity>",
    "evidence": "<summary of what was executed; checklist reference>"
  },
  "physicalMixedDpi": {
    "status": "PASS",
    "completedAt": "<ISO-8601 timestamp>",
    "operator": "<human identity>",
    "evidence": "<summary of scenarios executed; evidence directory reference>"
  },
  "windowsCompatibility": {
    "status": "PASS",
    "windows10": {
      "status": "PASS",
      "build": "10.0.19045.x",
      "operator": "<human identity>",
      "completedAt": "<ISO-8601 timestamp>",
      "nativeAbiEvidence": "<--selftest-native-abi PASS on Windows 10 x64 build ...; environment report attached>",
      "evidence": "<summary of the real Windows 10 qualification>"
    },
    "windows11": {
      "status": "PASS",
      "build": "<Windows 11 build, e.g. 10.0.26200>",
      "operator": "<human identity>",
      "completedAt": "<ISO-8601 timestamp>",
      "nativeAbiEvidence": "<--selftest-native-abi PASS on Windows 11 x64 build ...; environment report attached>",
      "evidence": "<summary of the real Windows 11 qualification>"
    }
  }
}
```

Validation rules (enforced by `Test-ExternalEvidenceFile`):

- `schemaVersion` must be `2`.
- `sourceCommitSha` must be exactly 40 hex characters and MUST equal the
  candidate SHA (the Stage A run `head_sha`).
- `artifactSha256` must be exactly 64 hex characters and MUST equal the FINAL
  distributed artifact hash (manifest `artifactSha256` == `SHA256SUMS.txt` ==
  on-disk `TabDock.exe`).
- `candidateWorkflowRunId` must be numeric and MUST equal the Stage A run id
  passed to the publish workflow.
- `candidateArtifactName` must be non-empty and MUST equal the artifact name
  actually downloaded from the Stage A run.
- Each mandatory gate (`finalWindowsHumanSmoke`, `physicalMixedDpi`) must be
  present with `status == "PASS"`, non-empty `operator` and `evidence`, and a
  `completedAt` that parses as ISO-8601 and is not materially in the future
  (5-minute clock-skew tolerance).
- `windowsCompatibility` must be present with `status == "PASS"` and BOTH
  `windows10` and `windows11` entries, each with `status == "PASS"`, a
  recorded OS `build`, `operator`, ISO-8601 `completedAt`, a recorded
  `nativeAbiEvidence` (the `--selftest-native-abi` environment report), and
  `evidence`. Missing/FAIL/BLOCKED/malformed entries fail closed.
- Missing file, malformed JSON, wrong schema version, `FAIL`,
  `BLOCKED_EXTERNAL`, missing gates, and missing fields all fail closed.
- A caller-controlled boolean is NOT accepted as evidence. Only this
  auditable record (human identity + completion time + evidence detail,
  bound to exact SHA, artifact hash, run, and artifact name) passes.

## RC qualification vs production publication

| Property | RC qualification-only (`release.yml`) | Production (Stage A + Stage B) |
|----------|---------------------------------------|--------------------------------|
| Workflow | `release.yml`, dispatch with `sha` (+ optional `version`, `signing-required`) | Stage A: `prepare-release-candidate.yml`; Stage B: `publish-release.yml` |
| External evidence | never required | required in Stage B: schema-v2 record with all gates PASS, bound to run + artifact + SHA + hash |
| Signing | optional: `not-configured` (unsigned) or `local-pfx` (dev/private cert, never the public-GA signer) | mandatory in Stage A through the APPROVED production provider (`digicert-stm`, non-exportable `CLOUD_HSM` key; run fails `BLOCKED_EXTERNAL` without an approved configured signer); Stage B independently re-verifies `signtool verify /pa` + timestamp + certificate identity with no provider access |
| Build/sign at publication | n/a (nothing is published) | Stage B downloads the Stage A artifact; ZERO rebuild, ZERO re-sign |
| Manifest `productionReleaseEligibility` | `BLOCKED_EXTERNAL` (honest at qualification time) | `BLOCKED_EXTERNAL` in the manifest; `ELIGIBLE` only in `publication-verification.json` after the Stage B gate |
| Fail-closed behavior | n/a | any failed condition in Stage B aborts before `gh release create` |

The manifest is a qualification-time record: it never claims the human gates
are verified. The publish-time verdict is a separate record,
`publication-verification.json`, written by the Stage B job after every check
passes and attached to the release alongside `release-external-evidence.json`.

## Production dispatch walkthrough

1. **Stage A — prepare:** ensure the repository variables/secrets for the
   approved production signer are configured (see
   `docs/release/code-signing.md` section 4: `SIGNING_PROVIDER=digicert-stm`
   plus `SM_HOST`, `SM_API_KEY`, `SM_CLIENT_CERT_FILE_B64`,
   `SM_CLIENT_CERT_PASSWORD`, `SM_KEYPAIR_ALIAS`, and the MANDATORY
   `SIGNING_EXPECTED_SUBJECT` publisher policy), then dispatch
   `prepare-release-candidate.yml` from `main` with
   `sha=<current main HEAD == the trusted dispatch SHA>` (and the expected
   `version`, default `1.0.0`). The run verifies the trusted dispatch
   contract first (ref == main, requested SHA == dispatch SHA,
   workflow SHA == dispatch SHA), builds once, signs once through the
   signing service (non-exportable key, certificate subject == current
   publisher policy), verifies the signature, timestamp, and certificate
   identity, computes the final distributed hash, and retains
   `tabdock-candidate-<sha>-<run-id>` (manifest `artifactSha256` =
   final signed hash, `SHA256SUMS.txt` = final signed hash,
   `releaseMode = PRODUCTION`, `signingProvider = digicert-stm`,
   `signingKeyProtection = CLOUD_HSM`, `releasePolicySchemaVersion` =
   current schema). The run summary prints the run id, artifact name, source
   SHA, semantic version, final SHA-256, and the signing certificate.
   Without an approved, configured production signer (or without the
   publisher policy) the run fails `BLOCKED_EXTERNAL` before any build.
2. **Human gates on the exact artifact:** download the retained artifact,
   verify `TabDock.exe` SHA-256 == manifest `artifactSha256` ==
   `SHA256SUMS.txt`, run the final manual Windows smoke
   (`docs/release/final-smoke.md`), the physical mixed-DPI qualification
   (`docs/release/mixed-dpi-qualification.md`), and the Windows 10/11
   compatibility qualification (`docs/release/compatibility-matrix.md`)
   against THAT executable, and fill in `release-external-evidence.json`
   (schemaVersion 2) with the exact SHA, hash, run id, and artifact name.
3. **Stage B — publish:** dispatch `publish-release.yml` from `main` with
   `run-id=<Stage A run id>` and `external-evidence=<the record>`. Stage B
   verifies the trusted dispatch contract, checks out the trusted policy at
   the executing workflow revision, verifies the run, locates and downloads
   the exact artifact (candidate source checked out as data only),
   re-verifies everything against the downloaded bytes using ONLY the
   trusted policy module (project version at the candidate SHA == manifest
   == recorded binary identity `buildIdentity` — the candidate is never
   executed; file == manifest == SHA256SUMS; CURRENT policy
   schema; publisher identity == manifest == actual certificate; evidence
   bound to SHA, hash, run, and artifact; Authenticode re-proven with
   `signtool verify /pa /v /tw`), writes the verified same-run handoff, and
   the publish job performs the final hash identity check and creates the
   release with the DERIVED tag `v<semanticVersion>` at the exact candidate
   SHA.

There is no "Run 2 rebuild": the artifact published by Stage B is the
artifact retained by Stage A — byte-identical, never rebuilt, never re-signed.

## Version and tag authority

- `TabDock.csproj <Version>` is the single authoritative semantic version.
  Stage A reads it from the exact candidate source; the workflow `version`
  input is only an EXPECTED value and the qualification fails on any
  disagreement (`version=9.9.9` cannot be recorded while the project
  declares `1.0.0`).
- The published executable's reported semantic version and its
  informational version were verified by trusted Stage A and recorded as
  `buildIdentity` in the manifest; the Stage B gate requires manifest ==
  recorded binary identity == project version. Stage B validates this
  contract from the trusted records and NEVER executes the downloaded
  binary to re-ask it.
- The release tag is DERIVED as `v<semanticVersion>`; Stage B accepts no tag
  input, so arbitrary tags (`stable-final`, `v2.0.0` for version `1.0.0`,
  ...) are structurally impossible, and the protected `v*` tag namespace
  (ruleset `release-tags`) applies by construction.

## Windows compatibility gate

v1.0.0 advertises Windows 10 (recent builds) and Windows 11 x64, so the
production evidence schema REQUIRES `windowsCompatibility` with PASS entries
for both:

- **Windows 10 x64** (real machine, build recorded, `--selftest-native-abi`
  PASS recorded) — missing Windows 10 evidence blocks production publication.
- **Windows 11 x64** (real machine, build recorded, `--selftest-native-abi`
  PASS recorded).
- Hosted Windows CI is proven automatically by every qualification run
  (`--selftest-diagnostics` includes the native ABI checks; `build.yml` also
  runs `--selftest-native-abi` on `windows-2022`).

Evidence must NOT be fabricated: if no real Windows 10 environment exists,
the gate stays `BLOCKED_EXTERNAL` and v1.0.0 is not published. Dropping the
Windows 10 support claim would be a product decision (README, release notes,
manifest/support metadata, compatibility docs) — it must never be done
silently merely to pass the gate.

## Enforcement summary

- `docs/release/code-signing.md`: the signing architecture — why public-GA
  signing uses hardware/cloud-backed private keys, why local-PFX is not the
  public-GA path, supported providers, required GitHub configuration per
  provider, Stage A signing/verification, final-hash provenance, Stage B
  provider independence, and `BLOCKED_EXTERNAL`.
- `scripts/release-tooling.ps1`: version/tag authority helpers
  (`Test-SemanticVersion`, `Get-ProjectSemanticVersion`,
  `Get-ReleaseTagFromVersion`, `Assert-ReleaseTagMatchesVersion`),
  `Complete-ReleaseRecords` (final-hash records + triple consistency),
  `Test-ExternalEvidenceFile` (schema v2 + SHA + artifact + run + artifact
  name binding + gates + completedAt quality), the release-policy schema
  contract (`Get-ReleasePolicySchemaVersion` / `Get-MinimumAcceptedProductionPolicySchema` /
  `Test-ReleasePolicySchema`), the current publisher-identity policy
  (`Get-PublisherIdentityPolicy`), signing-provider policy
  (`Get-SigningProvider`, `Test-ApprovedProductionSigningProvider`,
  `Test-ApprovedProductionKeyProtection`, `Test-SigningProviderConfiguration`,
  `Test-ProductionSigningPreflight`), `Test-AuthenticodeSignature` and
  `Test-AuthenticodeTimestamp` (independent `signtool verify /pa /v /tw` +
  RFC3161), `Get-SignerCertificateInfo` (certificate + timestamper
  identity), and `Test-PublicationEligibility` (the full gate — including
  the approved provider class, key-protection class, certificate identity,
  timestamper identity, current publisher policy, policy-schema contract,
  and timestamp policy — bound to the candidate run id and artifact name).
- `.github/workflows/prepare-release-candidate.yml` (Stage A): trusted
  production dispatch contract (ref == main, requested SHA == dispatch SHA,
  workflow SHA == dispatch SHA) as the FIRST step; provider-aware
  `BLOCKED_EXTERNAL` preflight requiring the approved production signer AND
  the mandatory publisher identity policy; official DigiCert tooling setup
  PINNED to the full immutable action SHA; PowerShell-materialized
  client-authentication certificate with always-run cleanup; NO legacy
  local-PFX secrets; single build/sign; immutable retention; candidate
  identity summary; never creates a release.
- `.github/workflows/publish-release.yml` (Stage B): two jobs — `verify`
  (contents: read + documented actions: write for the same-run handoff
  upload; trusted policy checkout at the executing workflow revision,
  candidate-source checkout as data only, cross-run run/artifact resolution
  via the GitHub API, download of the EXACT artifact
  (`download-artifact@v7` `run-id`/`repository`/`github-token`), fail-closed
  gate loaded ONLY from `policy/scripts/release-tooling.ps1`, ZERO candidate
  execution — binary identity is validated from the trusted manifest
  `buildIdentity` record, never by running the candidate; verified same-run
  handoff) and `publish`
  (needs: verify; contents: write; final hash identity check; derived tag;
  release asset verification; no candidate execution, no build, no sign,
  no signing-provider authentication).
- `.github/workflows/release.yml`: RC qualification-only (unsigned or
  `local-pfx`); no publication path.
- `.github/workflows/build.yml`: hosted CI gates the release-control
  regression suite (`scripts/release-tooling-tests.ps1`).
- `scripts/release-tooling-tests.ps1`: 134 deterministic regression cases
  including every adversarial condition (missing/malformed evidence, wrong
  SHA, wrong artifact hash, wrong run, wrong artifact name, `FAIL`,
  `BLOCKED_EXTERNAL`, unsigned artifact under mandatory signing,
  checksum/manifest/tamper mismatches, wrong version, malformed version,
  forged binary identity, RC-mode rejection, Windows 10/11 gate failures,
  future/non-ISO timestamps, signing-provider policy: production rejects
  local-pfx/mock/not-configured/unknown/missing provider metadata and
  key-protection metadata, mock never claims an approved provider,
  cloud-provider configuration and tooling failures fail closed, timestamp
  and certificate identity policy, static workflow guarantees, and the
  release-policy trust boundary: old pre-HSM candidates rejected under the
  CURRENT policy, missing/stale policy schema fails, hostile candidate
  tooling cannot change the Stage B verdict, the policy module is loaded
  only from `policy/`, publisher-identity policy missing/mismatch/wrong-
  consistent-publisher fails, Stage A dispatch contract ordering, Stage B
  run-head-SHA binding, no PFX secrets in production Stage A, DigiCert
  action pin, verify job without contents: write, publish job without
  build/sign/candidate execution, timestamp missing/warned fails,
  Stage B executes ZERO candidate code (no version self-report, no
  native-ABI self-test, no Start-Process, no candidate scripts, no path
  under candidate-source/ or candidate-artifact/ in an execution position;
  identity validated from trusted records; every release-workflow checkout
  sets `persist-credentials: false`)) — none of
  which publishes anything or contacts a signing provider.
