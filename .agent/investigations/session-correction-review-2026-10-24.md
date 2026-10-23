# Session correction and finalization — 2026-10-24

## Scope and starting evidence

Reviewed the session changes from `df5785e` through `4d0ed7b`, starting from clean `main` equal to origin. Hosted build 39999556313 succeeded for `4d0ed7b`; that historical success did not prove the newly discovered defects absent. The plan is [session-correction-finalization](../plans/session-correction-finalization.md).

## Review findings and resolutions

| Finding | Correction | Regression evidence |
| --- | --- | --- |
| Checkmark/chevron/row/button state not restored after unload/reload or re-enable | Initialize every feedback channel from current semantic state; checkmark underlying opacity/scale stays at its visible endpoint | `MotionReattachmentRegressionTests`, both policies, actual shared templates |
| Entrance disable exposed underlying opacity zero | Animate from zero without changing authoritative terminal opacity | Mid-animation and settled disable tests preserve a non-default base opacity |
| Picker highlight was inset by the outer content margin | Move spacing to inner content; leave fill/edge at row bounds; explicit focus edge wins over selection | Actual standalone/inline template bounds and combined checked/focus tests |
| Headless fixture actually showed an off-screen HWND | Remove WS_VISIBLE and ShowWindow; assert native invisibility; use managed lifetime/focus signals | All managed lifecycle suites use never-shown host; no new P/Invoke |
| Fixture timeout disposed a signal still used by its thread | Task completion signal, bounded shutdown/join, preserved exception stack | Short-timeout cooperative cleanup regression |
| Color tests accepted half-complete opacity | Require full opacity endpoints as well as color channels | Color round trips and interrupted reversals in both policies |
| Archived acceptance exceeded original tests | Add production split template, actual picker command/search/refresh/rebinding, current-state and obsolete-part collection tests | `ProductionTemplateContinuityTests`, `ButtonFeedbackTimingTests` |
| Partial icon fix had unbounded producers and strong UI callbacks | Shared case-insensitive queue: two producers, 256 queued paths; cancel only consumers; freeze before publication; weak per-tab delivery and dispatcher ownership | Saturation/retry/cancel/cache tests; external-controller membership gate; notification-thread assertions; removal/replacement/Detach/queued-dispatch/weak-reference tests |
| Collection Add observer could throw after insertion while rollback flags were still false | Roll back actual collection membership, not whether Add returned | Throwing observer test checks model, tabs, strip and zero cosmetic work |
| Export worker read live WPF controls and omitted failures | Owner-dispatcher deep capture into immutable serialized evidence; explicit available/empty/partial/unavailable status in same ZIP | Actual never-shown ContainerWindow provider, cross-thread rejection, held worker, deep DTO mutation and container close |
| Hotkey still did filesystem work before its first await | Desktop/path/directory work moved entirely to worker stage; async completion/failure remains observed | Held archive yields dispatcher; isolated output/failure tests; source audit of hotkey |
| Same-target concurrent exports could race final naming | Retry only destination-exists collisions with atomic non-overwriting move | Eight concurrent same-target archives, valid JSON/ZIPs, no temporary residue |

Independent read-only reviews initially blocked the motion/test-safety changes and found remaining diagnostic I/O plus test-evidence gaps. Those issues were corrected. Final motion review reported no blockers. Icon review found no production concurrency defect; its test notes were addressed with external gate control, notification-thread checks and a queued-delivery race.

## Validation

- `dotnet build TabDock.sln`: 0 warnings/errors.
- Final local `scripts/validate.ps1 -Configuration Release -Ci -Publish`: PASS, including **998/998 xUnit**, audited restore with no reported vulnerable packages, app/driver/GuineaPig/Performance builds, synthetic resource/visual gate, native ABI, isolated redirected recovery lifecycle, CLI/support ZIP privacy and self-contained publish smoke.
- Reduced-motion focused run: **54/54**, including normal/reduced policy overrides, motion/picker/button/icon and diagnostic tests.
- Main specs after syncing and archiving both remaining changes: strict **39/39**, zero active changes. Delta requirement/scenario bodies were compared against the main specs before archive.
- `git diff --check`: PASS (line-ending normalization notices only).
- LSP probes were inconclusive where servers did not answer; they are not claimed clean. Compiler and executable tests are the primary source evidence.
- Local build metadata during precommit gates still named historical HEAD `4d0ed7b` because corrections were uncommitted. Exact final commit identity/hosted CI must be verified after publication; no state-only follow-up commit is required.

## Visual evidence and reference scope

Actual production picker templates rendered off-screen at 280 and 600 DIPs under both motion policies. Inspected PNGs show full-row outline/fill, checked mark and coherent spacing. These are managed template renders, not desktop screenshots or native input proof. Refero search and the Square country-selection reference (`b36a6bd5-1f3c-4055-a7b6-23cae59ba2e8`) were used only to compare task-level checked-list conventions; palette and desktop identity were retained, not copied.

## Correction to earlier session claims

Earlier archived task checkboxes and the three motion investigation records should be read with this correction. Original split/picker brush fixtures were substitutes, not complete actual-template coverage. Original hidden-host tests used a shown off-screen HWND, so their prior PASS did not establish non-visible execution. Original pressed/focus injection never established physical keyboard activation. The new tests close the managed correctness/evidence gaps; they do not retroactively upgrade the old runs.

## Remaining external qualification

No SendInput, native keyboard/foreground certification, physical mixed-DPI or Windows 10 qualification, full physical scenario catalog, signing or deployment certification is claimed. No guest positioning, reparenting, journal identity authority, telemetry, dependency or cache-eviction policy change was introduced. No universal performance improvement is inferred from controlled nonblocking gates.
