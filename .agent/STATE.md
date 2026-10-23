# TabDock agent state

## Objective and status — 2026-10-24

Session review/correction is locally complete; publication must be checked against current Git and exact-SHA hosted CI, not this file. Active plan: [session-correction-finalization](plans/session-correction-finalization.md). Full finding/evidence record: [session correction review](investigations/session-correction-review-2026-10-24.md).

All eleven audit proposals are implemented, synced and archived. The final two archives are `2026-10-24-keep-tab-icons-off-ui-thread` and `2026-10-24-fix-dispatcher-safe-diagnostic-snapshots`. No active OpenSpec changes remain.

## Corrections and verified facts

- Motion reattachment initializes current checkmark/chevron/row/button states; entrance disable preserves base opacity. Picker feedback covers the whole row and focus wins over selection.
- The original "hidden" tests actually showed an off-screen HWND. Corrected fixtures never show it, assert native invisibility, and inject only managed lifecycle/focus state. Earlier claims must be read with the correction review.
- Picker/tab icons share two producers and a 256-path queue. Results are frozen, delivered on the owner dispatcher, cancellation stays consumer-local, and closed VMs are weakly referenced. Rollback now checks actual collection membership after throwing observers.
- Diagnostics deep-capture logical DTOs on the UI dispatcher; worker export reads no live UI. Available/empty/partial/unavailable status and sanitized issues are in the same bundle. Filesystem/ZIP work is off-thread; concurrent publication is atomic and non-overwriting.

## Validation

- Debug solution build: 0 warnings/errors.
- Final local Release CI/publish script PASS: 998/998 unit tests, audited restore, synthetic resource/visual, native ABI, isolated redirected recovery lifecycle, support ZIP privacy and self-contained publish smoke.
- Reduced-motion focused tests: 54/54. Actual managed picker renders inspected at 280/600 DIPs, both policies. Strict OpenSpec validation: 39/39; zero active changes.
- Compiler/tests are primary evidence; LSP coverage was inconclusive where servers did not answer. Precommit artifact metadata names historical HEAD `4d0ed7b`; final exact-SHA hosted evidence belongs in the session report, not a state-only follow-up commit.

## Qualification limits

No native keyboard/foreground, SendInput, full physical ValidationDriver catalog, mixed-DPI, Windows 10, signing or deployment certification was established here. The 2026-09-17 release evidence remains historical single-monitor evidence, not extended certification.

## Handoff

Read Git for branch, HEAD, origin/main, cleanliness and exact-SHA CI independently. Do not recreate closed proposals or infer physical readiness from managed/synthetic tests. Further product changes require fresh evidence, not another cosmetic campaign.
