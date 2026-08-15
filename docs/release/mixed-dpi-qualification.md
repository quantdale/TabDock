# Physical Mixed-DPI Production Qualification Procedure

**Status: NOT PERFORMED — BLOCKED_EXTERNAL until executed on real mixed-DPI
hardware.**

Deterministic repository tests (`--selftest-geometry`, `MonitorDpiSelfTest`)
are **not** equivalent to physical mixed-DPI hardware qualification. This
procedure is the required human/hardware gate. It must be executed against the
**exact release candidate artifact** (same SHA-256 as the artifact in
`release-manifest.json`) on a machine with at least two monitors at different
scaling (e.g. 100% + 150%).

## Rules

- Each scenario records evidence: monitor identity (index, handle, bounds,
  DPI, scale), guest HWND/process, initial geometry, final geometry, expected
  alignment, result, and (where practical) a screenshot artifact.
- Allowed results: `PASS`, `FAIL`, `BLOCKED_NO_MIXED_DPI_HARDWARE`.
- `BLOCKED_NO_MIXED_DPI_HARDWARE` is the only honest result on single-DPI
  machines. Never mark a scenario PASS without executing it.
- A `0/N` scenario run is not a PASS. An unexecuted scenario is not a PASS.

## Setup

1. Windows 10 (recent build) or Windows 11, 64-bit; release candidate
   `TabDock.exe` whose SHA-256 matches the manifest.
2. Monitor A at 100% scaling, monitor B at 150% scaling (both directions of
   the transition are exercised in scenario 1/2).
3. Guests: two normal Windows applications (e.g. Notepad + Windows Terminal)
   plus one DPI-unaware guest when available (see scenario 13).
4. Verify the candidate identity first: `TabDock.exe --version` must report
   the release commit and its self-reported SHA-256 must equal the manifest.

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

## Completion

The physical mixed-DPI gate is PASS only when **every applicable scenario**
records PASS with evidence, the machine configuration is described, and the
candidate artifact SHA-256 is recorded. Until then the gate remains
`BLOCKED_NO_MIXED_DPI_HARDWARE` / `BLOCKED_EXTERNAL` and must be reported as
such in `release-manifest.json` (`externalGates.physicalMixedDpi`).
