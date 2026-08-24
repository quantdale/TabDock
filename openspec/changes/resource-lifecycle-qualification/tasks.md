## 1. Resource measurement and analysis

- [x] 1.1 Add the immutable privacy-safe resource snapshot model, metric budgets, trend classifications, and fail-closed series analyzer.
- [x] 1.2 Add deterministic unit and ValidationDriver self-tests for plateau, noise, transient recovery, native leaks, late leaks, resets, missing/error evidence, process generations, ordering, and sample sufficiency.
- [x] 1.3 Add driver-only Windows probes for process identity, handles, USER/GDI objects, memory, threads, and top-level window count with per-sample native cleanup.

## 2. Lifecycle profiles and evidence

- [x] 2.1 Add reusable bounded profiles for group/capture, split, layout, picker/icon, WinEvent, diagnostics, persistence, and restart lifecycle churn.
- [x] 2.2 Add the safe resource-soak command with cycle, duration, profile, seed, configuration, and artifact-output options plus run-owned cleanup.
- [x] 2.3 Integrate resource JSON/JUnit output and resource-only capability metadata with the existing qualification manifest and source/driver identity.

## 3. CI and documentation

- [x] 3.1 Add the short headless resource gate to the canonical CI validation script without physical input or arbitrary desktop interaction.
- [x] 3.2 Retain resource evidence in the hosted build workflow while keeping synthetic resource results separate from physical/manual release gates.
- [ ] 3.3 Document the command, artifact schema, threshold rationale, and functional/resource/synthetic/supervised/release qualification boundary in project documentation.
- [x] 3.4 Create and validate the `resource-lifecycle-qualification` OpenSpec capability artifacts.

## 4. Verification and delivery

- [ ] 4.1 Run the analyzer, headless gate, and safe extended soak repeatedly and investigate any genuine growth or flakiness.
- [ ] 4.2 Run the complete Debug/Release build, unit, deterministic, release-tooling, OpenSpec, and canonical validation ladder.
- [ ] 4.3 Commit meaningful checkpoints on `main`, push `origin/main`, and verify identical SHAs and a clean worktree.
