# Windows Compatibility Matrix (WINDOWPLACEMENT ABI)

**Status: evidence-based, NOT assumed. Windows 10 x64 is NOT YET TESTED.**

Publication uses external-evidence schema 3. Each imported Windows 10/11
record must reference a privacy-safe machine report, its qualification-bundle
hash, its primary run-manifest hash, the exact candidate hash, and a PASS
native-ABI result. A schema-2 record remains historical/diagnostic and cannot
pass the current publication gate.

This matrix records where the 44-byte `WINDOWPLACEMENT` user32 contract (see
`NativeMethods.cs`) has actually been proven against real user32, versus
where it remains an external qualification item. It exists because the
published SDK definition (`sizeof(WINDOWPLACEMENT)` = 60 bytes on x64 with
the trailing `rcDevice`) differs from the empirically accepted runtime
contract (44 bytes), and no single machine's evidence may be extrapolated to
untested Windows versions.

Evidence is produced by the fail-loud native ABI self-test:
`TabDock.exe --selftest-native-abi` runs against real user32 on whatever
machine executes it and prints a per-machine environment report (OS
family/build, accepted length, `GetWindowPlacement` length write-back,
`SetWindowPlacement(44)` result, `SetWindowPlacement(60)` result). Every
qualification run (`scripts/validate.ps1`, `scripts/release-qualify.ps1`
against the published artifact) executes this probe directly.

## Matrix

| Environment | OS build (evidence) | 44-byte contract | Get length write-back | Set(44) | Set(60) rejected | Status |
|-------------|---------------------|------------------|------------------------|---------|------------------|--------|
| Hosted CI `windows-latest` | Server SKU, current image | proven by every qualification run (`--selftest-native-abi`) | proven | proven | proven | PROVEN (per run) |
| Hosted CI `windows-2022` (build.yml `native-abi-evidence` job) | Server 2022 (build 20348 family) | proven by `--selftest-native-abi` | proven | proven | proven | PROVEN (per run) |
| Windows 11 x64 workstation | 10.0.26200 (Windows 11) | proven (native round trip, set/get, 60-rejection) | proven | proven | proven | PROVEN |
| Windows 11 x64 (other builds) | untested | not claimed | not claimed | not claimed | not claimed | EXTERNAL — required |
| Windows 10 x64 (recent build, e.g. 22H2 19045) | untested | not claimed | not claimed | not claimed | not claimed | EXTERNAL — required |
| Other Windows versions | untested | not claimed | not claimed | not claimed | not claimed | EXTERNAL — required |

## Rules

- A row is marked PROVEN only when `--selftest-native-abi` passed on that exact
  machine/build and the environment report was captured.
- The 44-byte contract is the empirically validated runtime behavior on the
  tested builds; the SDK documents 60 bytes. The discrepancy is documented in
  `NativeMethods.cs` and enforced by the self-test — it is not papered over.
- If a Windows build ever accepts `length == 60` or rejects `length == 44`,
  the self-test FAILS LOUDLY. The correct response is a compatibility wrapper
  decision based on that evidence, never a silent global structure-size
  change.
- Hosted CI evidence (Server SKU) is real user32 evidence for the ABI, but it
  is NOT a substitute for physical desktop qualification of the full product
  on Windows 10 x64 and Windows 11 x64.

## Operator procedure (exact, unfakeable)

Each entry must be produced against the **exact FINAL bytes from Stage A**
(byte-identical `TabDock.exe` whose SHA-256 equals `finalSignedSha256` /
`SHA256SUMS.txt`):

1. Download `tabdock-candidate-<sha>-<run-id>` from Stage A run
   `candidateWorkflowRunId` (the same artifact described in
   `release-manifest.json`).
2. Verify the hash (`Get-FileHash` == `finalSignedSha256` == `SHA256SUMS.txt`)
   and that `TabDock.exe --version` reports the candidate commit.
3. On the target Windows machine, run `TabDock.exe --selftest-native-abi` and
   capture the full environment report into `nativeAbiEvidence`. Do NOT
   substitute a different build or a local `dotnet run` result.

Expected evidence is authored **exactly once** as
`release-external-evidence.json` with `schemaVersion: 3` and bindings
`sourceCommitSha` / `artifactSha256` / `candidateWorkflowRunId` /
`candidateArtifactName` equal to the verified artifact, and every
`completedAt` an ISO-8601 timestamp not in the future (5-minute tolerance).
Any stale binding, future timestamp, or wrong schema version fails the
publication gate closed.

## Production qualification requirements

Before v1.0.0 production publication, the external compatibility gate must
record PASS evidence in `release-external-evidence.json`
(`windowsCompatibility`, schemaVersion 3 — see
`docs/release/publication-gates.md`) for at minimum:

1. a supported recent **Windows 10 x64** system (`windows10`: `status` PASS,
   OS build recorded in `build`, `operator`, ISO-8601 `completedAt` (not in
   the future), the `--selftest-native-abi` environment report in
   `nativeAbiEvidence`, and `evidence`);
2. **Windows 11 x64** (`windows11`: same structure; build recorded — the
   10.0.26200 workstation already proven locally may be cited);
3. the hosted Windows CI environment (proven automatically on every run:
   `validate.ps1 -Ci` executes `--selftest-native-abi` directly, and
   `build.yml` runs it independently on windows-2022).

Each external entry needs the machine's OS build and the `--selftest-native-abi`
environment report recorded with the release evidence. The Stage B publication gate fails closed when
`windowsCompatibility` is missing, malformed, `FAIL`, `BLOCKED_EXTERNAL`, or
lacks either the Windows 10 or the Windows 11 entry. Externally visible gate
statuses are exactly `PASS` / `FAIL` / `BLOCKED_EXTERNAL` /
`BLOCKED_ENVIRONMENT`. Windows 10 evidence must NOT be fabricated if no
Windows 10 environment exists — the gate stays `BLOCKED_EXTERNAL` until a real
run. Dropping Windows 10 from the supported OS is a product decision that must
be made explicitly (README, release notes, manifest/support metadata, this
matrix) — never silently to pass the gate.
