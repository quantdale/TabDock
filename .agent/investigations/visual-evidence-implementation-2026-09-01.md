# Investigation: Visual evidence implementation checkpoint

**Date:** 2026-09-01
**Status:** closed; final-source evidence and release-boundary correction are
recorded below.

## Question

Where can bounded visual evidence and agent-mediated review be added without weakening the existing ValidationDriver identity, lease, native-evidence, cleanup, or release-bundle contracts?

## Findings

- The active OpenSpec change is `2026-08-31-visual-evidence-ai-review`, schema
  `spec-driven`, with 178 canonical tasks. The 2026-09-01 reconciliation
  checked 93 rows as `COMPLETED_AND_PROVEN` and left 85 rows explicitly
  classified and unchecked; implementation exists in the current dirty
  ValidationDriver/test tree, but the campaign is not accepted or archived.
- The ValidationDriver is the correct implementation boundary. It already owns
  `Pixels`, `Ctx`, `NativeInteractionTimeline`, `QualificationResultWriter`,
  `QualificationManifest`, `TestRunProvenance`, `ScenarioCatalog`, and the
  guarded cleanup lifecycle. Production TabDock behavior must remain unchanged.
- `Pixels.CaptureHostScreenArea` captures the DWM-composited client area with `BitBlt`; `CaptureWindowViaPrintWindow` captures an outer window rectangle. Existing callers use brightness, frame difference, dominant channel, and null-on-failure semantics. These callers must retain their current `int[]?` behavior.
- Scenario artifacts are written under the run-owned `TestRunProvenance.ArtifactDirectory`; scenario JSON/JUnit/timeline files are linked by schema-2 `run-manifest.json`, whose artifact index is independently hashed by `QualificationManifest` and the PowerShell qualification-bundle verifier.
- `Ctx.Check` maps failed live assertions to native outcome codes, while cleanup runs in `RunScenario` finally with the desktop lease still installed. Visual capture must be supplemental, bounded, and finalized before cleanup; recorder shutdown must remain in finally even when capture or encoding fails.
- Strong target identity is already represented by `WindowIdentity` and validated through `TestRunProvenance.TryValidateWindow`; visual metadata must use that identity contract rather than titles or bare HWNDs.
- `scripts/qualification-bundle.ps1` rejects absolute/traversal paths, rehashes every indexed byte, validates historical schema-2 run manifests, and applies a recursive privacy gate. Visual records must be optional for historical non-visual bundles and fully hash/path/privacy checked when present.
- No existing unit tests cover `Pixels`; tests must be added before shared pixel code is refactored. The unit-test project can link pure ValidationDriver source files without changing production project behavior.
- Controlled Debug `maximize-repro --cycles 1 --guest pig` completed `PASS` under the exclusive supervised desktop lease. Screen-composited client captures were `1225x700` at `(160,260)-(1385,960)` with `3,430,000` raw bytes and measured capture latency `15–46 ms`; maximized captures were `1920x1040` at `(0,100)-(1920,1140)` with `7,987,200` raw bytes and measured latency `28–46 ms`. Brightness and frame-diff metrics remained the existing values and all native assertions passed.
- Controlled Debug `browser-tabswitch-hidesafety --guest chrome-normal` completed `PASS`. Direct `PrintWindow(PW_RENDERFULLCONTENT)` captures were `1225x700` at the observed client/window rectangle, `3,430,000` raw bytes, with measured latency `14–27 ms`; brightness stayed above the existing liveness floor and the blinking page produced the existing frame-diff signal.
- Two initial characterization attempts exposed only environment/tooling facts: the checked-in win-x64 GuineaPig apphost was stale until the current non-RID fixture was rebuilt, and one later run was correctly `BLOCKED_ENVIRONMENT` after foreground qualification lost the lease. Neither path weakened cleanup; state snapshots were restored and no guest windows remained.
- The shared `Pixels` methods now expose detailed metadata through compatibility-preserving projections. Existing callers still receive the same `int[]?` buffers and metric semantics; invalid/zero handles remain null.

## Scope decision

Implement in bounded vertical slices:

1. characterize and regression-test current pixel semantics;
2. add pure visual evidence types, path policy, deterministic PNG encoding, and atomic artifact storage;
3. add capture scopes, checkpoint API, bounded recorder/ring buffer, manifests, contact sheets, review packets/results, and native-outcome aggregation;
4. integrate policy/CLI/catalog/scenario lifecycle and offline bundle verification;
5. add deterministic fixtures/tests, docs, canonical workflow, and supervised evidence where the workstation can safely provide it.

Do not add third-party imaging/model SDKs, upload images to external services, capture the unrestricted desktop by default, or change application behavior.

## Approaches tried

- Repository-owned `tools/openspec/node_modules/.bin/openspec.cmd` is required for status/instructions because the globally resolved CLI rejects the date-prefixed repository change name.
- Existing source and verifier paths were inspected directly after Repowise/source inventory; no existing visual evidence implementation was found.

## Conclusion

The campaign has a safe seam: a ValidationDriver-only visual pipeline with an
injectable native capture adapter and pure artifact/verification core. Existing
pixel helper projections remain compatibility wrappers. The current
reconciliation proves only 93 of 178 canonical rows; contract hardening,
scenario integration, acceptance evidence, documentation, and provenance
remain open or blocked as recorded in the matrix. Visual results are linked
into, but never allowed to override, native scenario outcomes. Physical and
multimodal acceptance remains an explicit supervised gate, and no archive
claim is valid before that boundary is satisfied.


## Post-checkpoint contract closure

The successor's deterministic Milestone A slice is now implemented in the
ValidationDriver-only seam: exact packet-byte hashing, strict current-schema
collection deserialization, explicit derived-artifact failures that preserve
raw sources, scenario/run hierarchy links, and fail-closed offline bundle
verification. The packet-hash trace and stale-literal classification are
recorded in `next-campaign-ultrathink-2026-09-02.md`. Current verification is
49/49 visual-filter unit tests, 177/177 PowerShell 7 release-tooling tests, a
valid visual bundle smoke fixture, and Debug/Release solution builds. This
does not claim supervised multimodal acceptance, physical qualification,
predecessor archival, or release provenance.
## References

- `openspec/changes/2026-08-31-visual-evidence-ai-review/proposal.md`
- `openspec/changes/2026-08-31-visual-evidence-ai-review/design.md`
- `openspec/changes/2026-08-31-visual-evidence-ai-review/tasks.md`
- `tests/ValidationDriver/TabDock.ValidationDriver/Pixels.cs`
- `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.cs`
- `tests/ValidationDriver/TabDock.ValidationDriver/QualificationResultWriter.cs`
- `tests/ValidationDriver/TabDock.ValidationDriver/QualificationManifest.cs`
- `tests/ValidationDriver/TabDock.ValidationDriver/TestRunProvenance.cs`
- `tests/ValidationDriver/TabDock.ValidationDriver/ScenarioCatalog.cs`
- `scripts/qualification-bundle.ps1`
- `docs/release/qualification-control-plane.md`

## Final-source closure addendum

The predecessor's 178-row ledger was fully dispositioned and archived at
`openspec/changes/archive/2026-09-01-2026-08-31-visual-evidence-ai-review/`.
The final source candidate is
`6bb8ecc80b103ec9e2e1bc12cebe241b1ab9519f`; it produced Release v1.1
executable SHA-256
`cf442e369c56c7c06c23b33c25b3434b079398b479e188c47e03f2d76dfbc291`.
The final-source supervised packets are healthy `VISUAL_OK`, preserved
seeded attempt-1 `VISUAL_DEFECT` plus attempt-2 `VISUAL_OK`, flight
`VISUAL_OK`, and unavailable `REVIEW_UNAVAILABLE`; packet/result verification,
visual tamper rejection, and native/visual precedence all passed.

Final deterministic evidence passed Debug/Release solution builds and
`795/795` unit tests, `153/153` ValidationDriver selftests, 135 catalog
scenarios, `179/179` release-tooling tests, canonical Release validation and
publish, strict OpenSpec `38/38`, native ABI, resource stability, recovery,
support, privacy, and version smokes. Signing is `NOT_CONFIGURED`, release
mode is `QUALIFICATION_ONLY`, and physical/external requirements remain
`BLOCKED_EXTERNAL`. Evidence records are bound to the candidate above;
later record commits do not claim new executable bytes.
