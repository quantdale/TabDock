## 1. Baseline and shared contracts

- [x] 1.1 Record the verified Git/PR/baseline evidence in the campaign plan and state checkpoint.
- [x] 1.2 Add the canonical outcome value object, exit/JUnit mappings, and deterministic aggregation tests.
- [x] 1.3 Add monotonic observable wait results and migrate the shared `Util.WaitUntil` boundary without changing intentional debounce sleeps.

## 2. Capabilities and desktop lease

- [x] 2.1 Add scenario descriptors and centralized capability discovery for applications, topology, session, input, signing, and Stage-B requirements.
- [x] 2.2 Add the bounded desktop qualification lease, privacy-safe snapshots, and pre-input/assertion checkpoint decisions.
- [x] 2.3 Add native-free lease state-machine tests for foreign coverage, owned/adopted targets, identity change, lock/session loss, and unverifiable probes.
- [x] 2.4 Run capability preflight before state isolation/process launch and migrate scattered browser/topology skips to the canonical outcome contract.

## 3. Ownership and evidence

- [x] 3.1 Extend `TestRunProvenance` with explicit ownership kinds and stable identity transitions while preserving adopted-process cleanup exclusion.
- [x] 3.2 Add HWND/PID reuse, ancestry, dynamic popup, modal, broker adoption, and cleanup-ownership regression cases.
- [x] 3.3 Add bounded native interaction timeline recording, deterministic serialization, privacy sanitization, and scenario artifact links.
- [x] 3.4 Add the root run manifest with candidate/build identity, environment/capabilities, scenario outcomes, artifact links, and aggregate counts.

## 4. Replay and native-event measurement

- [x] 4.1 Extract and wire the smallest pure WinEvent routing policy while preserving callback-time membership and dispatch-time identity revalidation.
- [x] 4.2 Add deterministic replay fixtures for relevant WinEvent filtering, stale generations, hide/show/name/destroy ordering, foreground intent, split transitions, and containment decisions.
- [x] 4.3 Add bounded WinEvent storm measurements for irrelevant/captured events, resolver probes, callbacks, lifecycle/layout/repair effects, and document the optimization decision.

## 5. Scenario reliability and stress

- [x] 5.1 Decompose shared scenario input, waits, app/browser launch, guarded point checks, evidence, and cleanup helpers while keeping explicit workflows readable.
- [x] 5.2 Audit the three unclassified physical cases and convert every non-physical step into deterministic replay/model coverage with honest classifications.
- [x] 5.3 Add fixed-seed lifecycle/identity/split/persistence/cleanup/result stress and model suites; print the seed on failure.
- [x] 5.4 Add first-attempt/rerun aggregation semantics and ensure best-of-N cannot produce a release PASS.

## 6. Documentation, qualification, and handoff

- [x] 6.1 Update architecture/testing/evidence documentation with outcome taxonomy, lease, ownership, replay, timeline, reruns, and physical limits.
- [x] 6.2 Validate the OpenSpec change strictly and reconcile canonical specs without hand-editing generated mirrors.
- [ ] 6.3 Run focused and full Debug/Release gates, release tooling, canonical validation/publish, native ABI, privacy checks, and diff check after each wave.
- [ ] 6.4 Update `.agent/STATE.md`, the campaign evidence ledger, final artifacts, commits, branch/PR status, and push the stacked draft branch without merging PR #12.
