# DPI/topology hardening implementation and qualification baseline — 2026-09-01

## Authority and scope

This campaign implements and qualifies `openspec/changes/dpi-topology-hardening/`. The strict pre-edit validation was run from the repository root:

```text
openspec validate dpi-topology-hardening --type change --strict --no-interactive --json
valid: true
summary.total: 1
summary.passed: 1
summary.failed: 0
version: 1.0
```

Dynamic Git authority at campaign start:

- Initial branch: `main`.
- Initial local `HEAD`: `89d4d912a88ca33c8f1ebb3c7dd68b487526d5ac`.
- Initial `origin/main`: `46a1d1bfb0d2c9111ed474f2104d10738d887489`.
- `git fetch origin --prune` showed the remote had advanced by the campaign starter commit `46a1d1b openspec: start dpi topology hardening campaign`.
- The remote change was the expected OpenSpec starter (14 files, +500/-49), not unrelated work; no local worktree changes or extra worktrees were present.
- `git pull --ff-only` fast-forwarded local `main` to `46a1d1bfb0d2c9111ed474f2104d10738d887489`.

No product source change is justified by the baseline. A source change requires either a retained valid `FAIL_PRODUCT` or a deterministic policy regression reproduced by the new qualification coverage. Synthetic topology evidence is never physical qualification evidence.

## Predecessor-row handoff

The following four predecessor rows are explicitly closed as migrated into this campaign, not silently counted as previously accepted:

| predecessor row | prior disposition/evidence | migration intent | required new evidence | synthetic-only allowance |
|---|---|---|---|---|
| `4.8` clipping/negative-coordinate/resize matrix | `IMPLEMENTED_BUT_NOT_ACCEPTED`; full geometry/DPI run absent | exercise negative-X, negative-Y, staggered, asymmetric work areas, odd/narrow/large geometry and containment after movement | physical topology snapshot, candidate/driver/run/scenario/attempt provenance, before/after geometry and restoration proof; deterministic matrix for every unavailable physical cell | synthetic lab may prove algorithmic invariants only |
| `18.6` title centering | `NOT_IMPLEMENTED`; physical title evidence absent | measure short/medium/long titles on narrow/default/wide windows after transfer | physical before/after title captures and numerical midpoint error bound, tied to observed topology and DPI | synthetic measurements cannot satisfy the physical row |
| `19.2` bounded dual-monitor capture | `NOT_IMPLEMENTED`; dual-monitor transfer unrun | qualify source→destination and destination→source transfer on every safely available mixed-DPI pair | native physical capture, exact scoped target, topology/DPI binding, both directions, cleanup/restoration verification | deterministic pair model can cover unavailable cells but cannot mark them physical PASS |
| `19.3` mixed-DPI before/after evidence | `NOT_IMPLEMENTED`; mixed-DPI evidence unrun | capture before/after transfer with actual native DPI probes and scale transitions | per-frame observed DPI, no fallback-to-96 on probe failure, source/destination IDs, strict visual packet verification | synthetic mixed values remain deterministic-only |

Predecessor rows `19.1` and `19.4` remain **REAL-APP HARDENING** scope and are deliberately out of this campaign. They must not be relabeled as completed here.

Source reconciliation was performed against the archived visual-evidence proposal, its task rows, and the reconciliation ledger. The ledger dispositions for rows `4.8`, `18.6`, `19.2`, and `19.3` are the authority for this handoff.

## Baseline deterministic evidence

Before implementation, the existing ValidationDriver topology self-test was run:

```text
dotnet run --project tests/ValidationDriver/TabDock.ValidationDriver/TabDock.ValidationDriver.csproj -c Release --no-restore -- --selftest topology --configuration Release --rid none
runId: f3ed3bca-769c-4ffb-bb92-ddcb4d7bbebe
TOPO01..TOPO07: PASS
summary: 7/7 PASS
lab: PASS
seed: 20260824
generation: virtual-topology-lab-2026-08-24-v1
```

The current lab contains 11 fixed topologies, including single-monitor, horizontal/vertical dual-monitor, negative-left, above-origin, asymmetric work areas, mixed 100/125/150/200%, odd-width, narrow work area, large coordinates, and removal/reorder transition, followed by 256 seeded stress iterations. Its output is explicitly `syntheticTopology=true`; it is not physical proof.

The existing deterministic assertions cover explicit synthetic provenance, fixed-matrix presence, same-seed repeatability, containment/partition invariants, modeled mixed-DPI values, negative/above-origin coordinates, and removal/reorder clamping. The campaign must extend this coverage for pairwise DPI transitions, selection/reorder/removal identity semantics, candidate/provenance validation, and fail-closed negative probes without weakening existing assertions.

## Physical host baseline

The last native preflight was run without starting TabDock or sending input:

```text
dotnet run --project tests/ValidationDriver/TabDock.ValidationDriver/TabDock.ValidationDriver.csproj -c Release -- --plan physicalMixedDpi --configuration Release --rid none --guest pig
```

Observed host topology at that boundary:

- virtual screen: `(0,0)-(3840,1200)`;
- primary monitor: `(0,0)-(1920,1200)`, work area `(0,0)-(1920,1140)`, effective DPI `120` / `125%`;
- right monitor: `(1920,0)-(3840,1080)`, work area `(1920,0)-(3840,1032)`, effective DPI `96` / `100%`;
- mixed DPI: available (`96`, `120`);
- negative-X monitor: unavailable;
- negative-Y/above-origin monitor: unavailable;
- current pair order in the plan was right secondary then primary; monitor handles are not acceptable persistent identity.

The preflight reported the existing mixed-DPI transfer, title-centering, and DPI-awareness scenarios as runnable on this observed host. This is capability evidence only; no TabDock process, GuineaPig process, or SendInput action occurred. No prior evidence is being relabeled as a new physical PASS.

## Implementation inventory and acceptance boundaries

Primary seams to extend:

- `tests/ValidationDriver/TabDock.ValidationDriver/VirtualTopologyLab.cs` and topology self-tests: deterministic topology/pair/transition coverage and explicit synthetic provenance.
- `tests/ValidationDriver/TabDock.ValidationDriver/ScenarioCapabilities.cs` and `Program.cs`: no-input physical cell planning, capability/environment/supervision outcomes, exact candidate/driver identity requirements, and truthful blocked rows.
- `tests/ValidationDriver/TabDock.ValidationDriver/DesktopQualificationLease.cs`: native topology/work-area/primary/DPI observations, run-local monitor identity, before/after comparison, lease invalidation, and restoration proof.
- `tests/ValidationDriver/TabDock.ValidationDriver/QualificationResultWriter.cs`: privacy-safe topology snapshots and provenance in results/manifests.
- `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios*.cs`: physical movement/title/topmost flows, controlled GuineaPig `--topmost`, scoped visual checkpoints, and restoration/cleanup gates.
- `tests/ValidationDriver/TabDock.ValidationDriver/VisualCaptureNative.cs`, visual models, recorder, and review verifier: target-monitor DPI must fail closed when probing fails; physical packets must bind observed topology/DPI and reject synthetic substitution.
- `tests/UnitTests/` and ValidationDriver self-tests: contract tests only for newly observable behavior, with existing source-link conventions preserved.
- `.agent/workflows/visual-evidence-review.md`, `docs/TESTING.md`, OpenSpec canonical/delta specs, and generated qualification records: update only through their canonical workflow and keep privacy-safe artifact rules.

Required final gates: deterministic self-tests and solution build; no-input physical plan with every requested cell classified; supervised physical execution only where the preflight is `RUNNABLE`; strict result/visual packet verification; residual-defect decision; dynamic final Git authority; final `main` clean and pushed. If physical capability is absent, preserve explicit `BLOCKED_CAPABILITY`, `BLOCKED_ENVIRONMENT`, or `BLOCKED_SUPERVISED` rows rather than manufacturing PASS evidence.

## Implementation checkpoint before physical execution

The implementation now provides:

- virtual topology laboratory schema 2, generation
  `virtual-topology-lab-2026-08-24-v2`, seed `20260824`, 12 fixed
  topologies, 12 required bidirectional DPI transitions, seeded removal/reorder,
  and explicit `syntheticTopology=true` gating;
- native topology schema 1, generation
  `physical-topology-snapshot-2026-09-01-v1`, with privacy-safe virtual,
  monitor, work-area, primary, DPI, scale, taskbar, placement, candidate,
  executable, driver, run, scenario, attempt, provenance, and snapshot hash
  fields;
- no-input physical planning with each requested cell classified before launch
  or input, and a ten-step operator-controlled display-state protocol whose
  only input-enabled step is the bounded cell run;
- lease pre-input topology equality and post-cleanup restoration equality;
- topology-bound physical visual manifests, packets, artifacts, and review
  results with strict synthetic/stale/tamper rejection;
- physical scenario guards for restricted checkpoints, exact monitor/DPI
  bindings, controlled GuineaPig `--topmost`, title lengths crossed with
  narrow/default/wide widths, and bidirectional mixed-DPI transfer.

Static evidence after implementation:

```text
dotnet build TabDock.sln -c Release --no-restore
solution build: PASS, 0 warnings, 0 errors
dotnet test TabDock.sln -c Release --no-restore --nologo
Release unit tests: 795/795 PASS
dotnet test TabDock.sln -c Debug --no-restore --nologo
Debug unit tests: 795/795 PASS
ValidationDriver selftest all, runId a52153dd-37e6-44ae-937b-f399790014b5:
173/173 PASS
ValidationDriver visual selftest, runId 7f73a4f3-4f47-4081-a10c-fa8ad7aaa580:
14/14 PASS
ValidationDriver capability selftest, runId 6333e5fb-7bdc-4bab-b8fd-c4355665ba72:
41/41 PASS
```

The exact pre-commit physical plan run was `8ce90f14-c472-48c8-9236-2f7d146d4296`
with `supervisionConfirmed=true`. It observed virtual
`(0,0)-(3840,1200)`, primary `monitor-001` `(0,0)-(1920,1200)` at 120 DPI,
and `monitor-002` `(1920,0)-(3840,1080)` at 96 DPI. Negative-X, negative-Y,
above-origin, odd-dimension, narrow-area, large-coordinate, and 144/168/192
DPI cells were explicitly `BLOCKED_CAPABILITY`; current asymmetric work-area
and title width/length cells were runnable in the no-input plan. No process
or input was started by that plan run.
