## Design

### Release chain

```
SOURCE SHA
  -> exact-SHA + clean-tree verification (release-qualify.ps1)
  -> audited NuGet restore
  -> single-file self-contained publish (win-x64)
  -> execute + qualify THE PUBLISHED EXECUTABLE
       (--version identity must equal the candidate SHA and its own
        self-reported SHA-256; --selftest-geometry; --selftest-diagnostics)
  -> optional Authenticode signing (scripts/sign-release.ps1)
  -> signature verification (signtool verify /pa)
  -> SHA-256 (Get-FileHash must equal the executable's self-report)
  -> release-manifest.json + SHA256SUMS.txt
  -> immutable GitHub Actions artifact retention
  -> human/external gates (final smoke, physical mixed-DPI, signing policy)
  -> intentional GitHub Release consuming the preserved artifact
```

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
`NOT_CONFIGURED` and local development is unaffected; with
`RELEASE_SIGNING_REQUIRED=true` a missing/invalid signature fails the
qualification. `finalSignedSha256` (when signing changes the bytes) and
`unsignedQualifiedSha256` are both recorded, and the release manifest never
describes an unsigned executable as signed. Git commit signatures are
irrelevant to Authenticode; they are never conflated.

### Fail-closed release workflow

`release.yml` is `workflow_dispatch`-only (no stable release on every push),
requires an exact `sha` input, verifies the checkout against it, runs
canonical qualification, then produces and retains the qualified artifact.
Publication is an explicit second decision (`qualification-only=false` +
`create-release=true`); the publish job re-verifies the manifest
(`qualificationStatus == PASS`, matching source SHA, on-disk hash equal to the
manifest) before `gh release create` with the preserved artifact, then
verifies the published assets. No source modification and no second
compilation happens at publication time.

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
(38 checks, operator-signed evidence). Both remain explicitly unperformed
until executed against the exact artifact; their status is recorded in the
release manifest.
