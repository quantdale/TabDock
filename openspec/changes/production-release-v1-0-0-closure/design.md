## Design

### Release chain

```
SOURCE SHA
  -> exact-SHA + clean-tree verification (release-qualify.ps1)
  -> audited NuGet restore
  -> single-file self-contained publish (win-x64)
  -> execute + qualify THE PUBLISHED EXECUTABLE
       (--version identity must equal the candidate SHA and its own
        self-reported SHA-256; --selftest-geometry; --selftest-diagnostics;
        --selftest-native-abi for placement ABI environment evidence)
  -> UNSIGNED provenance SHA-256 (unsignedQualifiedSha256)
  -> optional Authenticode signing (scripts/sign-release.ps1)
  -> signature verification (signtool verify /pa)
  -> FINAL SHA-256 of the bytes as they will be distributed
  -> release-manifest.json + SHA256SUMS.txt (FINAL distributed hash only;
     file == manifest == checksums enforced by Complete-ReleaseRecords)
  -> immutable GitHub Actions artifact retention
  -> EXTERNAL evidence (release-external-evidence.json: final human Windows
     smoke + physical mixed-DPI performed against the exact signed artifact,
     bound to its SHA and the candidate source SHA)
  -> intentional GitHub Release consuming the preserved artifact
     (publication job independently re-verifies manifest, SHA256SUMS,
     external evidence, and the Authenticode signature before gh release
     create; the publish-time verdict is recorded in
     publication-verification.json)
```

The v1.0.0 trust model is "sign first, then qualify the exact signed
artifact": manual smoke and physical mixed-DPI qualification are performed
against the artifact AFTER signing, and the evidence records that artifact's
final hash. Publication is only possible for bytes whose hash the evidence
describes.

### Exact-SHA qualification

`release-qualify.ps1` refuses to run when the requested SHA does not equal
`HEAD`, when the working tree is dirty (locally), and when the published
executable's embedded source commit does not equal the candidate SHA. The
artifact's self-reported SHA-256 is cross-checked against `Get-FileHash`.
Manifest vocabulary: `PASS` / `FAIL` / `BLOCKED_EXTERNAL` /
`BLOCKED_ENVIRONMENT`; a 0/N scenario run, an unavailable browser, or missing
mixed-DPI hardware can never become `PASS`. External gates are recorded
explicitly (`externalGates.*`) and stay `BLOCKED_EXTERNAL` / `SKIP_*` until
real evidence exists.

### Signing readiness

Material arrives only through CI secrets (`SIGNCERT_BASE64`,
`SIGNCERT_PASSWORD`, optional `SIGNCERT_TIMESTAMP`). The PFX is decoded to a
random temp file, used once, and deleted. Without material the status is
`NOT_CONFIGURED` and RC qualification is unaffected; with
`RELEASE_SIGNING_REQUIRED=true` (or the production gate) a missing/invalid
signature fails the qualification. `unsignedQualifiedSha256` (pre-sign
provenance) and `finalSignedSha256` (post-sign) are both recorded, and
`artifactSha256` / `SHA256SUMS.txt` always describe the FINAL distributed
bytes. The release manifest never describes an unsigned executable as
signed. Git commit signatures are irrelevant to Authenticode; they are never
conflated.

Production signing policy is explicit: the Stage A candidate-preparation
workflow (`prepare-release-candidate.yml`) forces `RELEASE_SIGNING_REQUIRED=true`
and `RELEASE_PRODUCTION_GATE=true`, and the Stage B publication workflow
additionally requires `SIGNED` + `SIGNATURE_VERIFIED` + `finalSignedSha256`
equal to the final artifact hash plus an independent `signtool verify /pa`
on the downloaded executable. A production release is never silently
unsigned; an unsigned GA would require an explicit documented policy change.
RC qualification keeps its `NOT_CONFIGURED` allowance.

Test-only mock signer modes (`-MockSign`, `-MockSignFailure`,
`-MockVerifyFailure`) model the byte-mutation semantics for the regression
suite. They never run while real material is configured, are recorded as
`Mock=true`, are refused under the production gate, and their artifacts can
never pass the publication gate.

### External production evidence

Production publication requires a `release-external-evidence.json` record
(schemaVersion 2) provided through the Stage B workflow's
`external-evidence` input. The schema is `schemaVersion`, `sourceCommitSha`
(exact 40-char candidate SHA), `artifactSha256` (exact FINAL artifact hash),
`candidateWorkflowRunId` and `candidateArtifactName` (the exact Stage A run
and artifact), and the mandatory gates `finalWindowsHumanSmoke`,
`physicalMixedDpi`, and `windowsCompatibility` (with PASS entries for
Windows 10 x64 and Windows 11 x64) — each gate with `status` (only `PASS` is
acceptable), `operator`, ISO-8601 `completedAt` (not materially in the
future), and `evidence` (plus `build`/`nativeAbiEvidence` for the Windows
entries). A caller-controlled boolean is not evidence. The Stage B job
validates the record with the shared `Test-ExternalEvidenceFile`/
`Test-PublicationEligibility` functions: missing, malformed, wrong-SHA,
wrong-hash, wrong-run, wrong-artifact, `FAIL`, or `BLOCKED_EXTERNAL` all fail
closed. The validated record is attached to the release, and
`publication-verification.json` records the publish-time eligibility verdict.

### Fail-closed release workflow

The release chain is two-stage. `release.yml` is the RC qualification-only
workflow: `workflow_dispatch`-only (no stable release on every push),
requires an exact `sha` input, verifies the checkout against it, runs
canonical qualification, then produces and retains the qualified artifact
(`tabdock-rc-<sha>-<run-id>`); it has NO publication path. Production
candidates are prepared by `prepare-release-candidate.yml` (build once, sign
once, immutable retention, no release) and published by `publish-release.yml`
(Stage B), which takes the Stage A run id, downloads the EXACT artifact
cross-run, runs the shared fail-closed gate (manifest `PASS`, exact SHA,
version authority, releaseMode PRODUCTION, run binding, on-disk hash ==
manifest `artifactSha256` == `SHA256SUMS.txt`, schema-v2 evidence bound to
the exact SHA, final hash, run, and artifact, mandatory `SIGNED` +
`SIGNATURE_VERIFIED` + independent `signtool verify /pa`) before
`gh release create` with the downloaded bytes, then verifies the published
assets. No source modification, no second compilation, and no second signing
happens at publication time; the tag is derived as `v<semanticVersion>`.

### Version contract

`TabDock.csproj` `<Version>` is the single authoritative mechanism; assembly
metadata and `BuildIdentity` derive from it. Release tooling takes the
expected version as input and records it in the manifest; the workflow
defaults it to the same `1.0.0`. Historical non-semantic tag names
(`stable`, `split`) do not dictate the semantic contract.

### Reproducibility

`global.json` pins the .NET 8 SDK feature band with `rollForward:
latestFeature` (stays within .NET 8; never silently jumps to .NET 9+). NuGet
restore remains ordinary with a mandatory `NuGetAudit` in CI; strict NuGet
lock mode stays avoided because SDK-generated `Microsoft.NET.ILLink.Tasks`
differences made lock results unstable across supported SDKs. OpenSpec tooling
remains pinned through `package-lock.json` + `npm ci --ignore-scripts`.

### Human gates

`docs/release/mixed-dpi-qualification.md` defines the physical mixed-DPI
procedure (16 scenarios, evidence requirements, `BLOCKED_NO_MIXED_DPI_HARDWARE`
result). `docs/release/final-smoke.md` defines the final manual Windows smoke
(38 checks, operator-signed evidence). Both must be executed against the
exact FINAL artifact (the signed executable, same SHA-256 as
`release-manifest.json`'s `artifactSha256`) and their PASS results recorded
in `release-external-evidence.json`; until then their status stays
`BLOCKED_EXTERNAL` in the manifest and `productionReleaseEligibility` is
`BLOCKED_EXTERNAL`. `docs/release/publication-gates.md` documents the trust
model and the production dispatch walkthrough; `docs/release/compatibility-matrix.md`
tracks which Windows environments have ABI evidence (hosted CI builds and
the local Windows 11 build) versus which remain external (Windows 10 x64).
