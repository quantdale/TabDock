# A02 implementation tasks

> **2026-10-21 re-review round (R1–R4) applied.** The re-review
> (`.agent/investigations/validation-safety-rereview-2026-10-21.md`) reproduced
> four residual defects behind the S1–S6 green matrix. All four are repaired
> with genuine red→green fixture evidence (fixtures written first, run red
> against the defective code, then green after repair): R1 cleanup followed a
> replaced ancestor junction and deleted relocated evidence — both C# and
> PowerShell cleanup authorities now apply the bootstrap component-level
> reparse refusal immediately before recursion (and the outer boundary checks
> its own ancestry); R2 a missing/renamed recorded root skipped ownership and
> the parent cleanup deleted the moved evidence — a recorded root must now
> re-prove ownership on every exit, while no-root headless scratch cleanup is
> preserved; R3 the probe timeout rewrite lost the exit-code check so a
> valid-proof/exit-4 child authorized launch — successful exit is required
> again alongside complete proof; R4 PowerShell post-exit stream drains were
> unbounded — one elapsed budget now carries through process exit and both
> drains in probe and recovery paths, refusing on expiry and reaping only the
> proven launched child.
>
> **2026-10-21 earlier review round (S1–S6) applied.** The frontier review
> (`.agent/investigations/validation-safety-review-2026-10-21.md`) reproduced
> six safety gaps behind the earlier green matrix. Affected tasks were
> reopened and corrected, each with red→green mutation evidence: S1 whitespace
> fallback + extended/short-name alias bypass (resolver refusal + verified
> identity), S2 destructive outer cleanup on early failure (single
> ownership-checked cleanup authority on every exit path), S3 RunId-only
> cleanup authorization (complete marker binding), S4 weak proof contract
> (schema/types/IsValidationRoot/Roles enforced), S5 ineffective probe budget
> (concurrent drain under one deadline + kill/reap), S6 process-global test
> state (async-scoped override + nonparallel collection + isolation
> regression). The two xUnit1031 warnings are fixed; a forced
> `--no-incremental` Release rebuild is 0 warnings/0 errors.

## 1. Define bootstrap proof and refusal fixtures

- [x] 1.1 Inventory every default product storage consumer: logger, state/backup/temp files, journal/rescue, pending/sidecar transactions, startup hygiene and diagnostic inspection.
- [x] 1.2 Add failing resolver/probe fixtures for paired-input absence, invalid/mismatched ownership marker, relative/unavailable paths, production overlap/alias and reparse components; use only disposable directories and non-mutating probes. (2026-10-21 S1: reopened; added whitespace-pair refusal and extended/device/short-name alias fixtures with matching alias-spelled markers; red 3+3 → green.)
- [x] 1.3 Define versioned probe/ownership records binding canonical product root, run identity and exact candidate identity; ensure persistent qualification evidence excludes personal paths.

## 2. Resolve storage before side effects

- [x] 2.1 Add the immutable shared storage-path resolver with normal known-folder defaults and the proposed validation root/run inputs; invalid overrides must have no production fallback. (2026-10-21 S1: reopened; explicitly provided blank/whitespace inputs now refuse instead of collapsing to production; namespace spellings refused and overlap/ownership compare verified filesystem identities via NativeMethods.GetLongPathName, failing closed when identity cannot be established.)
- [x] 2.2 Initialize/validate paths before App constructor logging, cleanup, service creation or rescue; make early refusal diagnostic-only and non-mutating.
- [x] 2.3 Implement the read-only `--validation-storage-probe` bootstrap path and tests proving no logs, UI, hooks, mutation lease or recovery operations. (2026-10-21 S4: reopened; probe report/proof verification now require supported ProbeSchema, correct field types, IsValidationRoot=true and the full storage-role set; stable refusals for unknown/malformed proof.)

## 3. Route all default storage through the resolved value

- [x] 3.1 Wire logging and persistence defaults, preserving explicit unit-test path injection and storage-degraded behavior.
- [x] 3.2 Wire journal construction/rescue and every pending discovery/recovery/ledger/hygiene path; preserve SID-scoped lease and generation checks.
- [x] 3.3 Wire diagnostic/log-tail inspection and startup temp cleanup; add a source/call-site check that no default role escapes to a separate known-folder calculation.

## 4. Replace unsafe process isolation

- [x] 4.1 Update the resource-soak launcher to create run-owned roots/markers, verify child root/candidate/run proof, pass identical inputs to startup and revalidate ownership before cleanup. (2026-10-21 S3/S5 + run-ID: reopened; cleanup now revalidates the COMPLETE marker (schema/root/run/candidate) before any recursive deletion and retains ambiguous trees; the probe drains both pipes concurrently under one deadline and reaps only its child on timeout; the validation run ID is captured after `TestRunProvenance.BeginRun()` with a marker/proof/manifest identity guard before any child.) (2026-10-21 R1/R2/R3: reopened again; `TryDeleteTemporaryRoot` refuses a reparse-component boundary and a missing/renamed recorded root (retains the enclosing tree), keeps no-root headless cleanup, and the probe requires successful exit alongside complete proof — fixtures: 2 junction-ancestor, renamed-root, no-root, and 3 exit-code cases.)
- [x] 4.2 Update redirected recovery smoke in `scripts/validate.ps1` with the same proof protocol; remove APPDATA-only isolation claims while preserving guarded process/time bounds and EOF behavior. (2026-10-21 S2/S3/S4: reopened; the smoke/proof/cleanup semantics moved to `scripts/ValidationStorage.ps1` shared with the maintained script tests; cleanup is one ownership-checked authority on EVERY exit path (probe error, child error, timeout, sentinel failure, exception) and the outer finally can no longer bypass a refused product-root cleanup.) (2026-10-21 R1/R2/R4: reopened again; `Remove-RunOwnedTempTree` refuses junction-replaced ancestors and missing recorded roots, and probe + recovery stream drains run under one elapsed budget with bounded post-exit completion.)
- [x] 4.3 Add refusal tests showing mismatched/stale/missing proof prevents mutating launch and lost marker ownership prevents recursive cleanup. (2026-10-21: reopened; matrices extended — schema 999/missing, wrong field types, IsValidationRoot false/missing, missing/partial Roles, incomplete/copied/wrong-root/wrong-candidate/unsupported-schema markers, held-open-stdout and stderr-flood process-budget fixtures; plus actual script-level failure tests with a run-owned fake probe that changes its marker owner and exits 4.) (2026-10-21 R1–R4: reopened again; added replaced-ancestor-junction cases for both cleanup authorities (fixture junction removed at teardown), renamed-recorded-root and no-recorded-root outer-boundary cases, valid-proof/exit-4 + invalid-proof/exit-0 + valid-proof/exit-0 process fixtures, and the exited-parent/inherited-pipe bounded-drain fixture with outcome-explicit assertions.)

## 5. Qualify the storage boundary safely

- [x] 5.1 Run `dotnet build TabDock.sln` and targeted resolver/bootstrap/path-routing tests; verify ordinary no-override defaults without writing real-profile fixtures. (2026-10-21: reopened/requalified — focused resolver/proof/cleanup/discovery/persistence tests 136/136 Debug AND Release; forced `--no-incremental` Release rebuild 0 warnings/0 errors; no-override default asserted without writes.) (2026-10-21 R-round: requalified — focused 136/136 Debug AND Release, forced Release rebuild 0 warnings/0 errors.)
- [x] 5.2 With A10 applied, run actual probe/CLI/recovery-smoke fixtures against verified isolated roots and compare outside sentinel file sets/bytes/last-write times; never plant corruption in the real profile. (2026-10-21: requalified after S1–S6 — isolated CLI fixture 16/16, isolated recover fixture 6/6, script-level safety suite 20/20 including the fake-probe lost-owner retention repro, in-gate redirected recovery smoke PASS under the complete proof contract.) (2026-10-21 R-round: requalified — script safety suite 24/24 (junction, renamed-root, inherited-pipe, fake-probe retention), isolated CLI 16/16 + recover 6/6, in-gate recovery smoke PASS.)
- [x] 5.3 Recheck mutation-lease contention, source/run proof and privacy, then run the canonical Release CI/publish gate; normal UI soak remains opt-in and subject to its separate safety prerequisites. (2026-10-21: requalified — `scripts/validate.ps1 -Ci -Publish` PASS end-to-end (exit 0): OpenSpec 49/49, support-bundle privacy, proof-protected recovery smoke, publish smoke; ownership-proven cleanup removed the run tree. UI soak not run.) (2026-10-21 R-round: requalified — gate PASS exit 0 again; driver self-tests 205/205; full unit 944/944 Debug + Release.)
- [x] 5.4 Update the testing playbook to document effective-root proof, failure behavior and safe cleanup; do not claim physical/native qualification from this storage test.
