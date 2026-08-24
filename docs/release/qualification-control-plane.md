# Qualification Control Plane

This document is the operator contract for the release evidence chain. It
connects the exact candidate bytes, ValidationDriver plans and manifests,
independent-machine handoffs, external evidence, and the trusted publication
workflow. It does not replace the detailed human procedures in
`final-smoke.md`, `mixed-dpi-qualification.md`, or
`compatibility-matrix.md`.

Current status: the deterministic and offline control plane is implemented and
verified on this host. Physical SendInput, real mixed-DPI hardware, Windows 10
qualification, production signing, and the final human smoke remain external
gates. Synthetic topology is never eligible for the physical mixed-DPI gate.

## Evidence chain

```text
source SHA
  -> exact Stage-A candidate bytes
  -> release-manifest.json + SHA256SUMS.txt
  -> catalog-derived qualification plan
  -> direct/shard run-manifest.json files
  -> verified all parent manifest (if an all run)
  -> qualification-bundle.json + artifact index
  -> independent-machine package/report import
  -> schema-3 external evidence + explicit human attestation
  -> trusted Stage-B policy verification
  -> the same published bytes
```

Each arrow is hash-checked where a file exists. Stage B reads the candidate and
evidence as data; it does not build, sign, launch, or execute candidate code.
The policy checkout is the trusted authority for the publication decision.

## Schema and generation policy

| Record | Current version/generation | Accepted policy |
| --- | ---: | --- |
| Scenario catalog | `scenario-catalog-2026-08-24-v1` | Exact generation for new qualification runs |
| Direct/shard/parent run manifest | `2` | New manifests emit v2; older records are diagnostic-only unless a migration explicitly verifies them |
| Qualification bundle | `1` | Exact current schema; future/unsupported versions fail closed |
| Independent-machine handoff package | `1` | Exact current schema; all indexed files are rehashed |
| Independent-machine report | `1` | Exact current schema; source, candidate, package, bundle, and run hashes are checked |
| External release evidence | `3` | Publication requires v3 and a qualification-bundle binding; v2 remains readable for non-publication compatibility tests |

Paths inside records use portable forward-slash relative paths. Absolute paths,
empty segments, `.`/`..`, duplicate artifact entries, duplicate JSON
properties, missing files, modified bytes, malformed timestamps, future
timestamps, and contradictory summaries are failures.

## Catalog and planning

The ValidationDriver catalog is the one source of truth for dispatchable
scenarios. It owns the scenario ID, handler, shard, execution class, guest
family, required applications, input/session/topology requirements, safety
classification, runtime budget, default inclusion, and release-evidence
eligibility. `Program --list`, shard selection, capability preflight, `all`,
and planning use catalog projections; callers must not maintain a second
allowlist or infer capability from a scenario name.

Use planning before any input or application launch:

```powershell
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- --list
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- --plan release --configuration Release --rid none
dotnet run --project tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj -- --plan physicalMixedDpi --configuration Release --rid none
```

The plan reports catalog generation, required scenarios, current capability
classification, topology requirements, and whether a gate is runnable. It
does not send input. A capability skip or environment block remains a
non-pass result in the later manifest.

## Run manifests and `all`

Direct and shard runs write schema-2 `run-manifest.json` records with candidate
and executable hashes, driver identity, catalog generation, timestamps,
capabilities, scenario attempts, first-attempt-authoritative rerun lineage,
outcome counts, and relative JSON/JUnit/timeline artifact links.

An `all` run creates a parent identity and an isolated child directory for each
orchestrated shard. The parent imports the actual child manifest and verifies
the child run kind, shard, source/candidate/driver hashes, timestamps,
scenario ownership, artifact hashes, outcome mapping, and exit-code agreement.
Missing, malformed, stale, duplicated, tampered, contradictory, timed-out, or
unlaunched child evidence is `FAIL_HARNESS`/partial evidence, never an
implicit PASS. The parent records child manifest hashes and flattened
scenario/attempt lineage so an auditor can traverse parent -> shard -> result
without trusting console text.

The first attempt remains authoritative. A later passing investigation rerun
does not turn a valid first-attempt failure into PASS; it is represented as
`FLAKE_UNCLASSIFIED` until the cause is understood.

## Qualification bundles and offline verification

`qualification-bundle.json` is the root record. It binds:

- source commit and semantic version;
- exact candidate executable and release-manifest hashes;
- ValidationDriver hash and catalog generation;
- every direct/shard/parent manifest hash and the primary run;
- outcome/count summaries and environment/capability classifications;
- synthetic/replay flags; and
- a complete relative artifact index for results, JUnit, timelines, manifests,
  candidate, release metadata, and driver.

Verify without launching anything:

```powershell
pwsh -File scripts\verify-qualification-bundle.ps1 -BundlePath <qualification-root> -ExpectedSourceSha <40-char-source-sha> -ExpectedArtifactSha <64-char-candidate-sha>
```

The verifier is deliberately independent of free-form driver output. It
recomputes hashes, checks manifest-derived counts, checks child references,
rejects path traversal and privacy-sensitive fields, and rejects unsupported
or future schemas. A bundle with blocked, skipped, flaky, replay-only, or
synthetic evidence may be useful diagnostic evidence but cannot satisfy a
physical production gate.

## Exact-candidate bridge

For a retained Stage-A candidate, run the bridge from a clean checkout of the
matching source SHA:

```powershell
pwsh -File scripts\qualify-release-candidate.ps1 `
  -CandidateDir <retained-candidate-directory> `
  -TabDock <retained-candidate-directory>\TabDock.exe `
  -SourceRoot <matching-clean-source-checkout> `
  -Tier deterministic
```

The bridge verifies `release-manifest.json`, `SHA256SUMS.txt`, signing state
when applicable, source `HEAD`, and the exact candidate bytes. It may build or
copy matching ValidationDriver/GuineaPig tooling, but it never rebuilds,
replaces, signs, or qualifies a different candidate path. Deterministic output
is explicitly synthetic. Physical/all execution additionally requires
`-AllowPhysical` and `TABDOCK_VALIDATION_DESKTOP_LEASE=exclusive-supervised`;
otherwise the request records `BLOCKED_SUPERVISED` and runs only native-free
evidence.

## Independent-machine handoff

Export a bounded package containing only the retained candidate, release
manifest/checksums, matching ValidationDriver runtime files, optional
GuineaPig runtime files, and instructions:

```powershell
pwsh -File scripts\export-qualification-package.ps1 `
  -CandidateDir <retained-candidate-directory> `
  -OutputDir <portable-package-directory> `
  -ValidationDriver <matching>\ValidationDriver.exe
```

On the Windows 10/11 machine, the operator runs:

```powershell
pwsh -File scripts\run-qualification-package.ps1 `
  -PackageDir <portable-package-directory>
```

The default tier is deterministic and marked `syntheticTopology=true`. A
physical run is opt-in and guarded:

```powershell
pwsh -File scripts\run-qualification-package.ps1 `
  -PackageDir <portable-package-directory> -Tier physical -AllowPhysical
```

The package runner writes `machine-report.json` and
`qualification-bundle.json` beside, rather than inside, the immutable package.
The report includes OS family/build, architecture, candidate and driver
hashes, native ABI result, observed topology classification, scenario outcome
counts, bundle/run hashes, and privacy contract fields. It contains no raw
titles, URLs, document text, arbitrary user paths, or console transcript.

On the originating machine, validate the returned report before merging it:

```powershell
pwsh -File scripts\import-qualification-report.ps1 `
  -ReportPath <machine-results>\machine-report.json `
  -PackagePath <portable-package-directory>
```

For a complete release evidence import, retain the existing human smoke
attestation as the input evidence and merge one validated Windows 10 report,
one Windows 11 report, and one physical mixed-DPI report:

```powershell
pwsh -File scripts\merge-qualification-evidence.ps1 `
  -ArtifactDir <retained-candidate-directory> `
  -EvidencePath <human-attestation-and-template>\release-external-evidence.json `
  -PackagePath <portable-package-directory> `
  -MachineReportPath @('<win10-results>\machine-report.json', '<win11-results>\machine-report.json', '<physical-results>\machine-report.json') `
  -HandoffDir <data-only-evidence-handoff> `
  -Operator '<importing operator identity>'
```

The merge command accepts only reports that pass offline package/report/bundle
verification and match the retained source/candidate. It stages only
bundle-indexed files, refuses duplicate OS records and candidate substitution,
and writes schema-3 evidence. With `-HandoffDir`, it also emits a bounded,
data-only directory containing the merged evidence JSON and the staged
`qualification/external` trees. That directory is the input to the optional
publication workflow evidence-artifact download; it contains no source,
release scripts, or candidate root metadata. It derives structured machine
records and the physical observed topology from the verified reports; it does
not create the human smoke attestation. Missing reports, synthetic/replay
topology, blocked, skipped, or flaky outcomes stop the merge rather than
becoming PASS.

The returned package/report is untrusted input. The import and merge scripts
never execute a returned binary, script, or command. Publication Stage B
re-verifies the staged hashes and bundle references using the trusted policy.

## Virtual topology laboratory

The native-free laboratory generation is
`virtual-topology-lab-2026-08-24-v1` with fixed seed `20260824`. The fixed
matrix covers single 96-DPI, dual horizontal and vertical, left-negative and
above-origin monitors, asymmetric work areas, mixed 96/120/144/192 effective
DPI, odd widths, narrow work areas, large coordinates, and monitor removal or
reordering. It exercises containment, placement/clamp/restore, split
partitioning, projection, drag math, and transition invariants with 256
bounded topology transitions.

Every lab artifact states `syntheticTopology=true`. These tests prove policy
math and transition safety only; they cannot satisfy the physical mixed-DPI,
Windows compatibility, signing, or human-smoke gates.

## Privacy and failure policy

Evidence is bounded and portable. The verifier rejects concrete user/profile
paths, URLs, titles, document text, credentials, and secret-like properties;
the driver may use explicit redaction markers such as `<validation-artifact>`.
Errors identify the violated contract and relative artifact, not desktop
content. A missing or contradictory record is a harness failure, not an
inferred success.
