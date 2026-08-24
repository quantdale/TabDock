# Physical Mixed-DPI Production Qualification Procedure

**Status: NOT PERFORMED — BLOCKED_EXTERNAL until executed on real mixed-DPI
hardware against the exact Stage A candidate bytes.**

The current publication evidence contract is schema 3. Schema 2 examples in
older release records are diagnostic history only; a production physical PASS
must include a verified `qualificationBundle`, `runManifestSha256`, exact
candidate hash, and structured `observedTopology` with
`syntheticTopology=false`, `replayOnly=false`, at least two monitors, and
distinct DPI values including a non-default value.

Deterministic repository tests (the headless xUnit geometry and monitor-DPI
suites) are **not** equivalent to physical mixed-DPI hardware qualification.
This procedure is the required human/hardware gate. It must be executed against the
**exact release candidate artifact** (byte-identical to the FINAL distributed
`TabDock.exe` from Stage A — same SHA-256 as `release-manifest.json`
`finalSignedSha256` / `SHA256SUMS.txt`; signing changes the bytes, so
qualifying any other build is NOT acceptable) on a machine with at least two
monitors at different scaling (e.g. 100% + 150%). There is no tooling boolean
shortcut — only the auditable evidence record described below satisfies the
publication gate. See `docs/release/publication-gates.md` for the trust model
and the evidence schema.

## Rules

- Each scenario records evidence: monitor identity (index, handle, bounds,
  DPI, scale), guest HWND/process, initial geometry, final geometry, expected
  alignment, result, and (where practical) a screenshot artifact.
- Allowed results: `PASS`, `FAIL`, `BLOCKED_NO_MIXED_DPI_HARDWARE`.
- `BLOCKED_NO_MIXED_DPI_HARDWARE` is the only honest result on single-DPI
  machines. Never mark a scenario PASS without executing it.
- A `0/N` scenario run is not a PASS. An unexecuted scenario is not a PASS.
- Externally visible gate statuses are exactly `PASS` / `FAIL` /
  `BLOCKED_EXTERNAL` / `BLOCKED_ENVIRONMENT` (see "External gate lifecycle"
  in `docs/release/publication-gates.md`); `BLOCKED_NO_MIXED_DPI_HARDWARE`
  is the hardware-specific reporting of `BLOCKED_ENVIRONMENT` for this gate.

## Operator procedure (exact, unfakeable)

Every scenario must run against the **exact bytes that will be published**,
cryptographically bound to the Stage A run that produced them:

1. **Download the exact candidate** from Stage A: artifact
   `tabdock-candidate-<sha>-<run-id>` from run `candidateWorkflowRunId`
   (the same artifact described in `release-manifest.json`).
2. **Verify byte identity** before any scenario (fail closed if the hash
   differs from `finalSignedSha256` / `SHA256SUMS.txt`):
   ```powershell
   (Get-FileHash -Algorithm SHA256 .\TabDock.exe).Hash.ToLowerInvariant() -eq
     ((Get-Content release-manifest.json | ConvertFrom-Json).finalSignedSha256.ToLowerInvariant())
   Get-Content SHA256SUMS.txt  # same hash
   .\TabDock.exe --version     # reports the candidate commit + matching sha256
   ```
   A smoke/qualification that does not re-prove this hash is not bound to the
   published artifact and will be rejected by the publication gate.
3. Then execute scenarios 1–16 on that verified binary, recording the required
   evidence per scenario. Missing evidence, a wrong hash, a stale run id, or a
   future `completedAt` fails the publication gate closed — see
   `docs/release/publication-gates.md`.

## Setup

1. Windows 10 (recent build) or Windows 11, 64-bit; the verified
   `TabDock.exe` from the procedure above (whose SHA-256 equals
   `finalSignedSha256` / `SHA256SUMS.txt`). Do NOT substitute another build.
2. Monitor A at 100% scaling, monitor B at 150% scaling (both directions of
   the transition are exercised in scenario 1/2).
3. Guests: two normal Windows applications (e.g. Notepad + Windows Terminal)
   plus one DPI-unaware guest when available (see scenario 13).
4. Verify again that `TabDock.exe --version` reports the release commit and
   its self-reported SHA-256 equals the manifest before starting scenarios.

## Scenarios and evidence

| # | Scenario | Steps | Evidence |
|---|----------|-------|----------|
| 1 | 100% -> 150% monitor transition | Move the container from monitor A to B with a guest docked. | Monitor B identity/DPI; guest HWND/process; geometry before/after; alignment (guest fills content area); result |
| 2 | 150% -> 100% monitor transition | Move the container back from B to A. | As scenario 1, reverse direction |
| 3 | Negative-coordinate monitor | Place monitor B (or an additional monitor) with negative coordinates; dock a guest there. | Monitor bounds (negative origin); guest geometry; alignment; result |
| 4 | Unsplit guest | Single guest docked; move container across both monitors. | Geometry + alignment per monitor; result |
| 5 | Active split pair | Create a split pair; move the container across monitors. | Both pane rects; no overlap/no gap; both guests visible; result |
| 6 | Dormant split pair while third guest displayed | A+B split; click third tab C; move container across monitors with C full-width. | C geometry; dormant pair metadata unchanged (per `--doctor` logical snapshot); result |
| 7 | Restoring dormant split | From scenario 6, click a composite half. | Pair returns exact LEFT/RIGHT; no overlap/gap; result |
| 8 | Maximize/restore | Maximize and restore the container on each monitor (split and unsplit). | Bounds match work area of the containing monitor; panes stay partitioned; result |
| 9 | Minimize/restore | Minimize the container, restore it on each monitor. | Guest placement/visibility after restore; result |
| 10 | Container drag across monitors | Drag the container title bar in one continuous motion from A to B. | Intermediate + final geometry; guest re-glue (SHEPHERD[position] log lines); alignment; result |
| 11 | Guest-native title-bar move/re-glue | Drag the docked guest by its own title bar on each monitor. | Guest returns to its pane (re-glue); no pop-out; result |
| 12 | Native resize/re-glue | Resize the docked guest by its own edge on each monitor. | Guest re-glued to pane rect; result |
| 13 | Dynamic minimum-size constraints | With a guest having a min-track size (e.g. 500x320 logical), maximize the container on the 150% monitor. | Effective min-track converted with monitor DPI; no guest larger than work area; result |
| 14 | DPI-unaware guest (when available) | Dock a known DPI-unaware app; move container across monitors. | Accepted at target monitor DPI; geometry physical-exact; content bitmap-scaled (blurry) as standalone; result |
| 15 | Split exit/reconfiguration | Enter split on monitor B, exit split, re-enter on monitor A. | Pane partition correct on each; result |
| 16 | Member removal on mixed DPI | With split active on monitor B, pop out one member. | Survivor takes full width at correct scale; result |

## Recording

Write results into the run evidence directory (`artifacts/qa-split/<run-id>/`
or a release-evidence directory) as JSON, one object per scenario:

```json
{
  "scenario": "monitor-transition-100-to-150",
  "result": "PASS",
  "monitors": [{"index": 0, "handle": "0x10001", "bounds": "0,0,1920x1080", "dpi": 96, "scale": "100%", "primary": true},
               {"index": 1, "handle": "0x10002", "bounds": "1920,0,1920x1080", "dpi": 144, "scale": "150%", "primary": false}],
  "guests": [{"hwnd": "0x…", "pid": 1234, "process": "notepad.exe"}],
  "initialGeometry": "0,0,1920x1080",
  "finalGeometry": "1920,0,1920x1080",
  "expectedAlignment": "guest fills content area",
  "resultDetail": "…",
  "artifact": "path/to/screenshot.png"
}
```

## Completion (exact, once)

The physical mixed-DPI gate is `PASS` only when **every applicable scenario**
records `PASS` with evidence, the machine configuration is described, and the
verified `artifactSha256` (`finalSignedSha256`) is recorded. Until then the
gate remains `BLOCKED_NO_MIXED_DPI_HARDWARE` / `BLOCKED_EXTERNAL` and must be
reported as such in `release-manifest.json`
(`externalGates.physicalMixedDpi`). Externally visible gate statuses are
exactly `PASS` / `FAIL` / `BLOCKED_EXTERNAL` / `BLOCKED_ENVIRONMENT`.

A PASS qualification must additionally be recorded **exactly once** in
`release-external-evidence.json` with `schemaVersion: 3` and the bindings
proven above (any reuse with a different SHA/hash/run/artifact fails; any
`completedAt` in the future fails; any wrong schema version fails — see
`docs/release/publication-gates.md`):

```json
{
  "schemaVersion": 3,
  "sourceCommitSha": "<exact 40-char candidate SHA>",
  "artifactSha256": "<exact FINAL artifact SHA-256>",
  "candidateWorkflowRunId": "<Stage A prepare-release-candidate run ID>",
  "candidateArtifactName": "tabdock-candidate-<sha>-<run-id>",
  "finalWindowsHumanSmoke": {
    "status": "<PASS only after docs/release/final-smoke.md>",
    "completedAt": "...",
    "operator": "...",
    "evidence": "..."
  },
  "physicalMixedDpi": {
    "status": "PASS",
    "completedAt": "<ISO-8601 timestamp>",
    "operator": "<human identity>",
    "evidence": "16 scenarios PASS with per-scenario evidence JSON in <evidence-dir>; monitors 100% + 150%; machine OS build <build>"
  },
  "windowsCompatibility": {
    "status": "PASS",
    "windows10": { "status": "PASS", "build": "<Windows 10 x64 build>", "operator": "...", "completedAt": "...", "nativeAbiEvidence": "...", "evidence": "..." },
    "windows11": { "status": "PASS", "build": "<Windows 11 build>", "operator": "...", "completedAt": "...", "nativeAbiEvidence": "...", "evidence": "..." }
  }
}
```

The mixed-DPI gate can never be marked PASS by a boolean or by an assertion
in tooling: only this schema-validated record, bound to the exact source SHA
and the exact final artifact hash, is accepted by the publication gate.
