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

## Trust model: prepare the signed candidate once, publish the SAME bytes

Authenticode signing with RFC3161 timestamping mutates the executable and is
not expected to reproduce an identical signed file on a later run, and even
an unsigned publish may not be byte-reproducible across runner/SDK changes.
The production chain therefore NEVER rebuilds or re-signs after the final
artifact exists:

```
SOURCE SHA
-> build once
-> automated qualification
-> Authenticode sign once
-> signature verify
-> FINAL distributed hash
-> immutably retain signed candidate
-> humans download THAT EXACT artifact
-> final Windows smoke on that exact hash
-> physical mixed-DPI on that exact hash
-> Windows 10/11 compatibility evidence on that exact hash
-> external evidence record
-> publish the PREVIOUSLY RETAINED artifact
-> NO build, NO sign, NO binary mutation
```

The artifact being published must be byte-identical to the artifact whose
hash appears in the external evidence.

## Two-stage release architecture

### STAGE A — `prepare-release-candidate.yml` (prepare production candidate)

A manually dispatched workflow that:

1. accepts the exact source SHA (and an EXPECTED semantic version that must
   agree with `TabDock.csproj <Version>`, which is authoritative);
2. checks out the exact SHA and verifies it;
3. runs the canonical hermetic qualification;
4. builds/publishes exactly once;
5. Authenticode-signs exactly once (mandatory; a preflight step fails with
   `BLOCKED_EXTERNAL` when `SIGNCERT_BASE64`/`SIGNCERT_PASSWORD` are absent);
6. RFC3161-timestamps exactly once;
7. verifies the signature (`signtool verify /pa`);
8. computes the final distributed SHA-256;
9. generates `release-manifest.json` + `SHA256SUMS.txt` describing the FINAL
   bytes (file == manifest == checksums);
10. uploads the immutable Actions artifact
    `tabdock-candidate-<sha>-<run-id>` (90-day retention);
11. records/prints: workflow run ID, artifact name, source SHA, semantic
    version, final artifact SHA-256 (the values the human copies into the
    evidence record);
12. NEVER creates a GitHub Release.

Signing is mandatory on this path. Without credentials the run fails with
`BLOCKED_EXTERNAL` and no candidate is produced. Qualification-only unsigned
RC workflows (`release.yml`) remain separate and continue to work.

### STAGE B — `publish-release.yml` (publish existing qualified candidate)

A separate, manually dispatched publication workflow that:

1. accepts the Stage A **workflow run ID** (and the schema-v2 evidence
   record) — never a version or tag input;
2. verifies the source run with the GitHub API: it must exist in THIS
   repository, must be the `prepare-release-candidate` workflow, must be
   `completed` with `conclusion=success`, and its `head_sha` becomes the
   candidate SHA;
3. locates the exact candidate artifact via the artifacts API (name
   `tabdock-candidate-<head-sha>-<run-id>`, not expired, unique);
4. downloads that existing artifact with the supported cross-run mechanism
   (`actions/download-artifact@v7` with `run-id`, `repository`, and
   `github-token`) — WITHOUT rebuilding, republishing, re-signing, or
   touching `TabDock.exe`;
5. validates: source SHA (run head SHA == manifest == evidence), semantic
   version (project `<Version>` at the candidate SHA == manifest == binary
   `--version`), final artifact hash (file == manifest == SHA256SUMS),
   manifest provenance (`releaseMode == PRODUCTION`,
   `workflowRunId == run-id`), Authenticode (`signtool verify /pa`),
   schema-v2 external evidence, and Windows 10/11 compatibility evidence;
6. publishes those exact existing bytes as the GitHub Release with the
   DERIVED tag `v<semanticVersion>` at the exact candidate SHA.

The key invariant:

```
PUBLISHED TabDock.exe SHA
== STAGE A TabDock.exe SHA
== EXTERNAL EVIDENCE artifactSha256
```

No second compilation. No second signing. The publish workflow contains no
build, sign, or qualification invocation at all (a deterministic static test
enforces this).

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
- the evidence names another artifact, another source SHA, another run, or
  another artifact name.

Binding chain: run-id input -> GitHub run (`path`/`status`/`conclusion`/
`headSha`) -> artifact name (embeds SHA + run id) -> manifest (`sourceCommitSha`,
`workflowRunId`, `releaseMode`, version, binary identity) -> evidence
(`candidateWorkflowRunId`, `candidateArtifactName`, `sourceCommitSha`,
`artifactSha256`) -> bytes (hash triple + `signtool verify /pa`).

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
| Signing | optional (`NOT_CONFIGURED` allowed) | mandatory in Stage A (run fails `BLOCKED_EXTERNAL` without credentials); Stage B independently re-verifies `signtool verify /pa` |
| Build/sign at publication | n/a (nothing is published) | Stage B downloads the Stage A artifact; ZERO rebuild, ZERO re-sign |
| Manifest `productionReleaseEligibility` | `BLOCKED_EXTERNAL` (honest at qualification time) | `BLOCKED_EXTERNAL` in the manifest; `ELIGIBLE` only in `publication-verification.json` after the Stage B gate |
| Fail-closed behavior | n/a | any failed condition in Stage B aborts before `gh release create` |

The manifest is a qualification-time record: it never claims the human gates
are verified. The publish-time verdict is a separate record,
`publication-verification.json`, written by the Stage B job after every check
passes and attached to the release alongside `release-external-evidence.json`.

## Production dispatch walkthrough

1. **Stage A — prepare:** dispatch `prepare-release-candidate.yml` with
   `sha=<candidate>` (and the expected `version`, default `1.0.0`). The run
   builds once, signs once, verifies the signature, computes the final
   distributed hash, and retains
   `tabdock-candidate-<sha>-<run-id>` (manifest `artifactSha256` = final
   signed hash, `SHA256SUMS.txt` = final signed hash, `releaseMode =
   PRODUCTION`). The run summary prints the run id, artifact name, source
   SHA, semantic version, and final SHA-256. Without signing credentials the
   run fails `BLOCKED_EXTERNAL` before any build.
2. **Human gates on the exact artifact:** download the retained artifact,
   verify `TabDock.exe` SHA-256 == manifest `artifactSha256` ==
   `SHA256SUMS.txt`, run the final manual Windows smoke
   (`docs/release/final-smoke.md`), the physical mixed-DPI qualification
   (`docs/release/mixed-dpi-qualification.md`), and the Windows 10/11
   compatibility qualification (`docs/release/compatibility-matrix.md`)
   against THAT executable, and fill in `release-external-evidence.json`
   (schemaVersion 2) with the exact SHA, hash, run id, and artifact name.
3. **Stage B — publish:** dispatch `publish-release.yml` with
   `run-id=<Stage A run id>` and `external-evidence=<the record>`. Stage B
   verifies the run, locates and downloads the exact artifact, re-verifies
   everything against the downloaded bytes (project version at the candidate
   SHA == manifest == binary `--version`; file == manifest == SHA256SUMS;
   evidence bound to SHA, hash, run, and artifact; Authenticode re-proven),
   then creates the release with the DERIVED tag `v<semanticVersion>` at the
   exact candidate SHA.

There is no "Run 2 rebuild": the artifact published by Stage B is the
artifact retained by Stage A — byte-identical, never rebuilt, never re-signed.

## Version and tag authority

- `TabDock.csproj <Version>` is the single authoritative semantic version.
  Stage A reads it from the exact candidate source; the workflow `version`
  input is only an EXPECTED value and the qualification fails on any
  disagreement (`version=9.9.9` cannot be recorded while the project
  declares `1.0.0`).
- The published executable must report the same semantic version, and its
  informational version must carry that semantic version plus the source
  identity. The manifest records the binary identity; the Stage B gate
  requires manifest == binary identity == project version, and Stage B
  re-runs the downloaded `--version` to prove it.
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

- `scripts/release-tooling.ps1`: version/tag authority helpers
  (`Test-SemanticVersion`, `Get-ProjectSemanticVersion`,
  `Get-ReleaseTagFromVersion`, `Assert-ReleaseTagMatchesVersion`),
  `Complete-ReleaseRecords` (final-hash records + triple consistency),
  `Test-ExternalEvidenceFile` (schema v2 + SHA + artifact + run + artifact
  name binding + gates + completedAt quality), `Test-PublicationEligibility`
  (the full gate, bound to the candidate run id and artifact name),
  `Test-AuthenticodeSignature` (independent `signtool verify /pa`).
- `.github/workflows/prepare-release-candidate.yml` (Stage A): mandatory
  signing with explicit `BLOCKED_EXTERNAL` preflight, single build/sign,
  immutable retention, candidate identity summary; never creates a release.
- `.github/workflows/publish-release.yml` (Stage B): cross-run run/artifact
  resolution via the GitHub API, download of the EXACT artifact
  (`download-artifact@v7` `run-id`/`repository`/`github-token`), fail-closed
  gate, derived tag, release asset verification; contains no build, sign, or
  qualification invocation.
- `.github/workflows/release.yml`: RC qualification-only; no publication
  path.
- `scripts/release-tooling-tests.ps1`: 69 deterministic regression cases
  including every adversarial condition (missing/malformed evidence, wrong
  SHA, wrong artifact hash, wrong run, wrong artifact name, `FAIL`,
  `BLOCKED_EXTERNAL`, unsigned artifact under mandatory signing,
  checksum/manifest/tamper mismatches, wrong version, malformed version,
  forged binary identity, RC-mode rejection, Windows 10/11 gate failures,
  future/non-ISO timestamps, static workflow guarantees) — none of which
  publishes anything.
