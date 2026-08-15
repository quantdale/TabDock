# Production Publication Gates (External Evidence)

**Status: the machine-enforced gate is implemented; the human evidence is NOT
PERFORMED — `BLOCKED_EXTERNAL` until real operators qualify the exact final
artifact.**

This document defines the trust model for TabDock production publication, the
`release-external-evidence.json` schema, and the exact dispatch sequence for
a production release. The enforcement described here is code, not intent:
`scripts/release-tooling.ps1` (shared by the release workflow's publication
job and `scripts/release-tooling-tests.ps1`) refuses publication when any
required condition fails.

## Trust model: sign first, then qualify the exact signed artifact

For TabDock v1.0.0 the intended sequence is:

```
build
-> automated qualification (exact SHA, published executable)
-> Authenticode sign
-> signature verification (signtool verify /pa)
-> compute FINAL distributed hash
-> final human Windows smoke on the EXACT signed artifact
-> physical mixed-DPI qualification on the EXACT signed artifact
-> external evidence references the SIGNED artifact hash
-> publish those exact bytes
```

Authenticode signing mutates the executable, so manual/human qualification
MUST target the artifact after signing. The evidence record binds the human
gates to the exact candidate source SHA and to the exact final artifact hash;
a publication run whose artifact hash differs from the evidence (for example
because a rebuild produced different bytes) is REFUSED. This is deliberate
fail-closed behavior: the bytes being published must be the bytes the humans
qualified.

## Evidence schema (`release-external-evidence.json`)

```json
{
  "schemaVersion": 1,
  "sourceCommitSha": "<exact 40-char candidate SHA>",
  "artifactSha256": "<exact FINAL artifact SHA-256, lowercase>",
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
  }
}
```

Validation rules (enforced by `Test-ExternalEvidenceFile`):

- `schemaVersion` must be `1`.
- `sourceCommitSha` must be exactly 40 lowercase hex characters and MUST
  equal the release candidate SHA requested in the dispatch.
- `artifactSha256` must be exactly 64 lowercase hex characters and MUST equal
  the FINAL distributed artifact hash (manifest `artifactSha256` ==
  `SHA256SUMS.txt` == on-disk `TabDock.exe`).
- Each mandatory gate (`finalWindowsHumanSmoke`, `physicalMixedDpi`) must be
  present with `status == "PASS"` and non-empty `operator`, `completedAt`,
  and `evidence`. `FAIL`, `BLOCKED_EXTERNAL`,
  `BLOCKED_NO_MIXED_DPI_HARDWARE`, missing gates, and missing fields all fail
  closed.
- Missing file, malformed JSON, and wrong schema version fail closed.
- A caller-controlled boolean is NOT accepted as evidence. Only this
  auditable record (human identity + completion time + evidence detail,
  bound to exact SHA and artifact) passes.

## RC qualification vs production publication

| Property | RC qualification-only | Production (`create-release=true`) |
|----------|-----------------------|-------------------------------------|
| External evidence required | NO | YES (schema-valid, gates PASS, bound to exact SHA + final hash) |
| Signing | optional (`NOT_CONFIGURED` allowed) | mandatory: `SIGNED` + `SIGNATURE_VERIFIED` + `finalSignedSha256` + independent `signtool verify /pa` in the publish job |
| Manifest `productionReleaseEligibility` | `BLOCKED_EXTERNAL` (honest at qualification time) | `BLOCKED_EXTERNAL` in the manifest; `ELIGIBLE` only in `publication-verification.json` after the publish job validates everything |
| Fail-closed behavior | n/a (nothing is published) | any failed condition aborts before `gh release create` |

The manifest is a qualification-time record: it never claims the human gates
are verified. The publish-time verdict is a separate record,
`publication-verification.json`, written by the publication job after every
check passes and attached to the release alongside
`release-external-evidence.json`.

## Production dispatch walkthrough

1. **Run 1 — qualification-only (signed):** dispatch `release.yml` with
   `qualification-only=true`, `sha=<candidate>`, `signing-required=true`
   (or rely on the workflow's production rule). This produces the signed
   artifact, `release-manifest.json` (`artifactSha256` = final signed hash),
   and `SHA256SUMS.txt` as a retained Actions artifact.
2. **Human gates on the exact artifact:** download the retained artifact,
   verify `TabDock.exe` SHA-256 == manifest `artifactSha256` ==
   `SHA256SUMS.txt`, run the final manual Windows smoke
   (`docs/release/final-smoke.md`) and the physical mixed-DPI qualification
   (`docs/release/mixed-dpi-qualification.md`) against THAT executable, and
   fill in `release-external-evidence.json` with the exact SHA and hash.
3. **Run 2 — production:** dispatch `release.yml` with
   `qualification-only=false`, `create-release=true`, the SAME `sha` and
   `version`, and `external-evidence=<the record>`. The qualify job
   re-runs (signing forced); the publish job downloads THIS run's artifact
   and the gate verifies: manifest PASS / exact SHA / exact version,
   file == manifest == SHA256SUMS.txt, evidence valid and bound to the
   requested SHA and the FINAL hash, and the Authenticode signature
   re-proven with `signtool verify /pa`. Only then is the release created at
   the exact candidate SHA.

If Run 2's artifact is not byte-identical to the artifact the evidence
describes (non-deterministic build), publication fails closed with the
artifact-hash mismatch listed. That is correct behavior: the release must
contain the exact bytes that were qualified. Investigate build
determinism or re-run the human gates against the actual final artifact.

## Enforcement summary

- `scripts/release-tooling.ps1`: `Complete-ReleaseRecords` (final-hash
  records + triple consistency), `Test-ExternalEvidenceFile` (schema + SHA +
  artifact binding + gates), `Test-PublicationEligibility` (the full gate),
  `Test-AuthenticodeSignature` (independent `signtool verify /pa`).
- `.github/workflows/release.yml`: `external-evidence` input; production runs
  force `RELEASE_SIGNING_REQUIRED` and `RELEASE_PRODUCTION_GATE`; the publish
  job runs the gate before `gh release create` and attaches the evidence and
  the verification record to the release.
- `scripts/release-tooling-tests.ps1`: 37 deterministic regression cases
  including every adversarial condition (missing/malformed evidence, wrong
  SHA, wrong artifact hash, `FAIL`, `BLOCKED_EXTERNAL`, unsigned artifact
  under mandatory signing, checksum/manifest/tamper mismatches, wrong
  version/SHA, dirty tree) — none of which publishes anything.
