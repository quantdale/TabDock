# Windows Compatibility Matrix (WINDOWPLACEMENT ABI)

**Status: evidence-based, NOT assumed. Windows 10 x64 is NOT YET TESTED.**

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
`SetWindowPlacement(44)` result, `SetWindowPlacement(60)` result). The same
checks run inside `--selftest-diagnostics` (used by every qualification run),
so every qualifying machine contributes evidence.

## Matrix

| Environment | OS build (evidence) | 44-byte contract | Get length write-back | Set(44) | Set(60) rejected | Status |
|-------------|---------------------|------------------|------------------------|---------|------------------|--------|
| Hosted CI `windows-latest` | Server SKU, current image | proven by every qualification run (`--selftest-diagnostics`) | proven | proven | proven | PROVEN (per run) |
| Hosted CI `windows-2022` (build.yml `native-abi-evidence` job) | Server 2022 (build 20348 family) | proven by `--selftest-native-abi` | proven | proven | proven | PROVEN (per run) |
| Windows 11 x64 workstation | 10.0.26200 (Windows 11) | proven (native round trip, set/get, 60-rejection) | proven | proven | proven | PROVEN |
| Windows 11 x64 (other builds) | untested | not claimed | not claimed | not claimed | not claimed | EXTERNAL — required |
| Windows 10 x64 (recent build, e.g. 22H2 19045) | untested | not claimed | not claimed | not claimed | not claimed | EXTERNAL — required |
| Other Windows versions | untested | not claimed | not claimed | not claimed | not claimed | EXTERNAL — required |

## Rules

- A row is marked PROVEN only when `--selftest-native-abi` (or the
  equivalent checks inside `--selftest-diagnostics`) passed on that exact
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

## Production qualification requirements

Before v1.0.0 production publication, the external compatibility gate must
record evidence for at minimum:

1. a supported recent **Windows 10 x64** system (build recorded);
2. **Windows 11 x64** (build recorded; 10.0.26200 already proven locally and
   recorded in this matrix);
3. the hosted Windows CI environment (proven automatically on every run).

Each entry needs the machine's OS build and the `--selftest-native-abi`
environment report (or a `--selftest-diagnostics` run log) recorded with the
release evidence. Windows 10 evidence must NOT be fabricated if no Windows 10
environment exists — the gate stays `BLOCKED_EXTERNAL` until a real run.
