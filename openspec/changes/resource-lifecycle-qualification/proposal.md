## Why

TabDock's functional, recovery, native-determinism, and release qualification
coverage does not currently measure whether repeated capture, split, picker,
diagnostic, persistence, and restart lifecycles accumulate USER objects, GDI
objects, process handles, windows, threads, or private memory. A behaviorally
green run can therefore leave a native/UI lifecycle regression undetected. The
release-stage reliability work needs a bounded, reviewable resource signal now
that the mainline behavior has been qualified.

## What Changes

- Add a validation-side resource snapshot model and Windows probe for process
  identity, handles, USER/GDI objects, memory, threads, and top-level windows.
- Add deterministic series analysis that separates warm-up, transient noise,
  plateau, sustained growth, invalid evidence, and unavailable evidence.
- Add reusable headless lifecycle profiles for group/capture churn, split and
  layout churn, picker/icon generations, WinEvent routing, diagnostics,
  persistence/recovery fixtures, and process-generation cleanup.
- Retain resource-only JSON/JUnit evidence in the existing qualification
  manifest, bound to the source and driver identities and explicitly marked
  synthetic when measurements are synthetic.
- Gate ordinary CI with a short, headless, non-invasive resource regression and
  provide an opt-in longer local resource soak with isolated test state.
- Document the distinction between functional correctness, resource
  stability, synthetic qualification, supervised physical qualification, and
  production release eligibility.

## Capabilities

### New Capabilities

- `resource-lifecycle-qualification`: bounded resource measurement, analysis,
  lifecycle churn qualification, evidence retention, and safe CI boundaries.

### Modified Capabilities

None.

## Impact

- ValidationDriver and unit-test projects gain test-side resource models,
  analyzers, Windows-only probes, lifecycle profiles, and artifact generation.
- `scripts/validate.ps1` and the hosted build workflow gain one bounded
  headless gate; no physical input or production sampler is added.
- Existing qualification manifests gain a resource-only entry without allowing
  synthetic resource evidence to satisfy physical, OS-version, signing, or
  human-smoke requirements.
- Production architecture, Shepherd presentation, GroupManager membership,
  split authority, persistence durability, privacy rules, and release trust
  boundaries remain unchanged. No new runtime dependency is introduced.
