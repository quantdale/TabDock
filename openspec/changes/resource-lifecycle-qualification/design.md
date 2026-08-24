## Context

The existing qualification control plane already owns run identity, manifests,
artifacts, outcome classification, guarded process cleanup, deterministic
policy seams, and the supervised boundary for real input. The resource
capability extends those seams. It does not change the Shepherd presentation
authority or add a production polling loop. See `proposal.md` and the
resource-lifecycle-qualification spec for the motivation and observable
contract.

## Goals / Non-Goals

**Goals:**

- Measure reliable Windows process/UI resource signals for a run-owned target.
- Analyze repeated samples with explicit warm-up, settled checkpoints, tail
  behavior, and strict fail-closed handling of missing evidence.
- Exercise complementary lifecycle ownership seams cheaply in headless CI and
  provide an opt-in process-resource soak for local investigation.
- Retain privacy-safe, source-bound, resource-only evidence in the existing
  manifest hierarchy.
- Make thresholds understandable, deterministic, and covered by unit and
  self-test cases.

**Non-Goals:**

- Adding a continuous resource sampler to TabDock production startup or normal
  operation.
- Replacing Shepherd, GroupManager, SplitPresentationController, persistence,
  WinEvent, picker, or release-control authorities.
- Claiming physical input, mixed-DPI, OS-version, signing, or human-smoke
  qualification from a synthetic resource result.
- Building a generic production resource manager or a cloud telemetry path.

## Decisions

### Keep measurement in the validation boundary

The immutable snapshot/analyzer model is linked into unit tests, while Windows
counter acquisition remains in the ValidationDriver. The probe uses supported
process, GUI-resource, memory-counter, Toolhelp, and top-level-window signals;
it closes native handles per sample and records only privacy-safe failure
categories. This avoids production overhead and prevents instrumentation from
becoming a second HWND mutation authority.

**Alternative considered:** add a production sampler or diagnostic timer.
That would add runtime cost and another long-lived lifecycle owner without
improving the normal functional path, so it is reserved for a future concrete
diagnostic need.

### Analyze series, not final-minus-initial

The analyzer skips only a configured warm-up prefix, requires several settled
samples, and computes final, peak, tail, positive-step, and linear-slope
signals. Small strict budgets apply to handles, USER/GDI objects, threads, and
window count; byte counters receive documented headroom for WPF/CLR noise.
Missing fields, probe errors, invalid ordering, same-generation counter resets,
and identity changes are BLOCKED. A final value that happens to return to the
starting value cannot conceal a sustained or late settled leak.

**Alternative considered:** gate only on `final <= initial` or a very large
absolute ceiling. Both approaches hide transiently recovering leaks and
steady +1-per-cycle native growth, so they are not used.

### Compose small lifecycle profiles over existing authorities

The headless profiles use the existing split policy, geometry, WinEvent routing
policy, and isolated file fixtures. Picker/icon generations and diagnostics use
bounded fake ownership models that reflect their existing cancellation,
generation, ring-buffer, and cleanup contracts. The optional non-headless mode
starts only a run-owned TabDock process with isolated application-data roots and
reads counters without sending input. A physical extension remains governed by
the existing supervised lease and is not part of the CI command.

**Alternative considered:** add another large end-to-end torture scenario.
That would duplicate scenario setup and make failures hard to localize. Small
profiles make ownership residue and the leaking lifecycle explicit while
preserving existing UI torture coverage.

### Extend the existing qualification artifact contract

The resource writer emits JSON and JUnit files and registers one
`resource-stability` entry with `resourceOnly` and `syntheticMeasurements`
capabilities. It reuses the existing candidate/run/driver identity and
manifest hashing path. Resource outcomes are mapped through the existing
PASS/FAIL/BLOCKED contract; no resource result is used to infer unrelated
capabilities.

### Gate with a short CI command and retain optional long evidence

`validate.ps1 -Ci` invokes a fixed 32-cycle headless gate with no desktop lease
or SendInput. The build workflow retains the resource directory as a separate
data-only artifact, including when a later job step fails. Developers can use
the same driver with higher cycles, a duration bound, profile selection, seed,
and an explicit artifact directory for longer safe/test-owned investigation.

## Risks / Trade-offs

- **[Risk]** WPF/CLR byte counters fluctuate between supported Windows images.
  **Mitigation:** byte-specific budgets, warm-up exclusion, tail slope, and
  deterministic analyzer tests; native object counts remain strict.
- **[Risk]** A hosted process probe is unavailable or races process shutdown.
  **Mitigation:** the headless CI gate uses explicit synthetic measurements;
  live probe failures are BLOCKED and never PASS.
- **[Risk]** A fake profile could drift from production ownership semantics.
  **Mitigation:** profiles call existing pure policy seams where available,
  retain scenario names and invariants, and remain supplemental to existing
  supervised/native torture scenarios.
- **[Risk]** Resource evidence is mistaken for release eligibility.
  **Mitigation:** manifest capability flags, explicit synthetic markers, and
  documentation keep physical/manual/signing gates separate.
- **[Risk]** Temporary or spawned state survives a failed run.
  **Mitigation:** isolated roots, bounded guarded spawns, `finally` cleanup,
  process provenance, and residue counts in the artifact.

## Migration Plan

1. Land the validation model, analyzer tests, profiles, probe, artifact writer,
   CI invocation, and documentation on `main`.
2. Run the bounded gate repeatedly and run the opt-in extended safe soak. If a
   real growth pattern appears, reduce it and fix the owning lifecycle with a
   focused regression before declaring the capability green.
3. Retain the new artifact as resource-only evidence in future qualification
   runs. Existing physical and release-control evidence remains unchanged.
4. Rollback, if ever required, is a source-controlled revert of the
   validation-only gate and artifacts; no production persistence or runtime
   migration is introduced.
