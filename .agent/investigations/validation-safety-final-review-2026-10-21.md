# Final review: A02 R1–R4 corrections

**Date:** 2026-10-21
**Status:** complete — approved for the reviewed safety scope; review only
**Plan:** `.agent/plans/validation-safety-final-review-2026-10-21.md`

## Scope and verdict

Reviewed the current uncommitted R1–R4 implementation against `.agent/investigations/validation-safety-rereview-2026-10-21.md` and its follow-up prompt. Git independently reports `main`, with HEAD and origin/main both `eafe922d925b54001d35886f3807e1312cdb16cd`. The review compared current source, maintained tests and fresh execution results rather than relying on the completion summary. Existing unrelated motion/configuration work was preserved.

**No material standards or spec findings remain for the reviewed accidental-mutation boundary. The four previously reproduced defects are repaired in both cleanup authorities, and the previously requested failure-path coverage is present.** This does not establish physical/native, mixed-DPI, Windows 10, signing or UI-soak qualification, and it does not authenticate against a same-user process deliberately rewriting fixture contents.

## Standards

No material manually assessed standards violation. P/Invoke remains centralized, the shared PowerShell protocol remains one source, and the new cleanup/probe code follows the repository's existing style. Fresh Debug and forced Release solution builds both succeed with **0 warnings and 0 errors**.

## Spec review

### R1 — repaired

`ValidationStorageProof.TryCleanupRunOwnedRoot` and `Remove-RunOwnedTempTree` now apply component-level reparse refusal to the deletion path and enclosing cleanup boundary before recursion. The maintained ancestor-junction cases pass for the direct driver helper, outer driver cleanup and PowerShell cleanup.

An additional owned fixture moves the product directory itself and replaces its original path with a junction. Both actual driver cleanup boundaries and the actual PowerShell authority refuse it and retain the relocated evidence. A file replacing the recorded directory also refuses outer cleanup in both implementations. Fixture junctions were removed before ordinary teardown.

### R2 — repaired

Once a product root is recorded, both outer cleanup authorities require current ownership even when that directory is missing, renamed or replaced. The maintained renamed-root cases retain the enclosing tree and evidence. A genuinely absent recorded root still permits ordinary owned-scratch cleanup, preserving the headless/synthetic path.

### R3 — repaired

The driver again requires `ExitCode == 0` after bounded stream draining and before authorization. Maintained process fixtures cover valid proof/exit 4 (refuse), invalid proof/exit 0 (refuse) and valid proof/exit 0 (authorize). The concurrent drain and process-budget behavior remain intact.

### R4 — repaired

Both PowerShell process helpers now carry one elapsed budget through process exit and completion of both stream drains. The maintained inherited-pipe fixture proves execution by stalling at least four seconds, then refuses before twelve seconds against its five-second budget. Expiry disposes only the proven launched child. Live-child and stderr-flood assertions are outcome-explicit.

## Evidence qualification

The completion summary's full-gate counts were not accepted from the checkpoint alone. The retained implementation log at `artifacts/cla-fixture-2026-10-20/validate-ci-publish-r.log` is a successful **Debug** gate, not the claimed Release configuration. This review therefore ran the actual Release gate itself.

The historical 2026-10-20 Release failure remains correctly qualified: a concrete global-state race was demonstrated and fixed, but its identity with that historical failure is unproven because the original stack/error was not retained.

## Fresh validation

| Check | Result |
| --- | --- |
| Debug solution build | PASS, 0 warnings/errors |
| Forced Release solution rebuild | PASS, 0 warnings/errors |
| Focused correction/storage/inspection tests, Debug | **136/136 PASS** |
| Same focused tests, Release | **136/136 PASS** |
| Driver storage self-tests | **32/32 PASS** |
| Full driver deterministic self-tests | **205/205 PASS** |
| Shared PowerShell safety suite | **24/24 PASS** |
| Additional substituted-root and replaced-file probes | **5/5 PASS** across driver direct/outer and PowerShell boundaries |
| Isolated CLI fixture | **16/16 PASS** |
| Isolated recover fixture | **6/6 PASS** |
| Actual `scripts/validate.ps1 -Configuration Release -Ci -Publish` | PASS, exit 0; unit **944/944**, synthetic resource/visual PASS, recovery smoke PASS, privacy PASS, OpenSpec **49/49**, publish smoke PASS |
| Reviewed input stability | All **14** implementation/test/spec/doc hashes unchanged |

The first actual Release gate result was retained. No best-of-N rerun was used. No UI soak, desktop input, mixed-DPI, Windows 10 or signing qualification was performed.

## Handoff

No application/test/helper source, OpenSpec task checkbox, commit, push or archive was changed by this review. Only durable review records/checkpoint and ignored/temporary review fixtures were written. Local evidence is ignored under `artifacts/validation-safety-final-review-2026-10-21/`.

**Summary:** Standards: 0 findings. Spec: 0 findings for the reviewed scope. The seven unrelated product changes remain planning-only and were not reviewed as implementations.
