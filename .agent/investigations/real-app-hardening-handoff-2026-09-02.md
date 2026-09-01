# Real-app hardening handoff — rows 19.1 and 19.4

**Date:** 2026-09-02
**Source predecessor:** `openspec/changes/archive/2026-09-01-2026-08-31-visual-evidence-ai-review/` (ledger `.agent/investigations/visual-evidence-ledger-reconciliation-2026-09-01.md`)
**Successor campaign:** `openspec/changes/real-app-hardening/` (ACTIVE, strict validation `valid=true`)
**DPI campaign reference:** `openspec/changes/archive/2026-09-01-dpi-topology-hardening/` (rows 4.8/18.6/19.2/19.3 closed; 19.1/19.4 preserved out of scope)

No row remains floating after this campaign.

## Authority

Git is authoritative for `HEAD`, branch, `origin/main`. This file is a durable provenance record, not a claim about the commit containing this file. The floating-obligation closure is proven by the successor campaign's tasks/specs and by the durable acceptance matrix.

## Row 19.1 — restricted browser F11 visual evidence

| Field | Value |
|---|---|
| **Original wording** | "After privacy gates are proven, add restricted visual packets to real browser F11 qualification." (`tasks.md` 19.1) |
| **Original classification** | `NOT_IMPLEMENTED` — Browser F11 scenarios do not emit restricted visual packets. (ledger 19.1) |
| **Original reason for migration** | Predecessor visual pipeline had proven privacy classes (`TEST_OWNED`/`PRODUCT_OWNED`/`REAL_APP_RESTRICTED`/`DESKTOP_RESTRICTED`) and capture scoping, but no browser F11 scenario was integrated to emit `REAL_APP_RESTRICTED` packets. The privacy gates and `AllowVirtualDesktop=false` default were proven, yet F11 visual was deferred because it requires a separate supervised, privacy-aware, real-browser qualification (not synthetic GuineaPig). Disposition `MIGRATED_TO_REAL_APP_CAMPAIGN`. |
| **Current implementation support** | `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.PhysicalCertification.cs:BrowserFullscreenContained` + `Scenarios.Browser.cs:SpawnBrowserGuest` (fresh isolated profile per Chrome/Edge/Brave, `Chrome_WidgetWin_1`, `--user-data-dir`), `GuestDpiPositionScope`-protected positioning, `WindowShepherdService` F11 classifier (`NeedsNativePresentationRestore` + `RequestBrowserFullscreenExit` one-shot, duplicate suppression, `LOCATIONCHANGE` re-entry), `VisualCaptureScope` with `VisualPrivacyClass.REAL_APP_RESTRICTED`, `VisualEvidenceRecorder`, `VisualReviewVerifier`, and `BrowserFullscreenContained` native snapshot logging. |
| **Current scenario** | `browser-fullscreen-contained` (`scenario-catalog-2026-09-01-v2`, shard `browser`, `ExecutionClass=Browser`, `GuestFamily=browser`, `DestructiveState=ExternalBrowser`, `mayContributeReleaseEvidence=false`, `--guest chrome-normal|edge-normal|brave-normal`, `--cycles 2` cycles (2–3), single-tab `ExternalBrowser` harness never kills adopted process beyond exact run-owned PID/start). Exercises on each available Chromium family separately. |
| **Exact acceptance requirement** | At least the available Chromium family is exercised; browser identity is exact (`HWND`/`PID`/`TID`/`class`/`exe`/`process-start`/`token` generation); F11 transition is physically observed via `NativeGuestSnapshot` before vs transitioned (`outer/style/monitor/zoomed/title`) plus `SHEPHERD[drift-reconcile]`; containment/recovery is proven (`IsDocked` to pane, `WS_CAPTION` restored, `SHEPHERD[presentation-restore-request]==1`, no tab click, no repeated F11, no `Released tab`); repeat cycles preserve first-attempt authority; monitor continuity on 120/96 DPI where runnable; no synthetic fixture substituted for physical cell. |
| **Visual/privacy requirement** | Restricted before/fullscreen/after packets with `privacyClass=REAL_APP_RESTRICTED`, host-client plus bounded context, no whole-desktop, no unrelated windows, packet `SHA-256` hash-valid, `Valid:true` verifier, capable multimodal review where required by predecessor (healthy `VISUAL_OK` vs `VISUAL_DEFECT`/`REVIEW_UNAVAILABLE` separation). Real-app crop minimized to smallest approved region per `visual-qualification-evidence` delta. |
| **Final disposition** | **COMPLETED_AND_PROVEN** when available Chromium exercised with above; otherwise `ACCEPTED_SKIP_CAPABILITY`/`ACCEPTED_BLOCKED_CAPABILITY` with explicit `executable not found`/`BLOCKED_ENVIRONMENT` (not fabricated `PASS`). This handoff is the row-level closure; the durable matrix `.agent/investigations/real-app-hardening-acceptance-matrix-2026-09-02.*` records the per-browser attempt/disp. |

## Row 19.4 — adopted real-app crop/minimization/privacy evidence

| Field | Value |
|---|---|
| **Original wording** | "Minimize/crop adopted real-app imagery and avoid unrelated desktop content." (`tasks.md` 19.4) |
| **Original classification** | `NOT_IMPLEMENTED` — No adopted real-app visual cropping/minimization path is integrated. (ledger 19.4) |
| **Original reason for migration** | Same predecessor privacy boundary as 19.1: the visual pipeline could capture `HOST_CLIENT` and `TARGET_WITH_CONTEXT` for test-owned windows, but adopted real apps (user's Notepad/Terminal or existing browser) had no proven cropped `REAL_APP_RESTRICTED` path. The risk was exposing personal documents, terminal history, or unrelated windows. Disposition `MIGRATED_TO_REAL_APP_CAMPAIGN`. |
| **Current implementation support** | `Scenarios.cs:SpawnNotepad` (unique temp file, `Notepad` class, `FindWindowsByClass`, `isOurProcess` `PID==launcher` check, `DoNotKill` for adopted) and `Scenarios.cs:SpawnGuest wt` (launcher vs monarch inspection), `NativeSnapshotService`/`DesktopQualificationLease` ownership/foreground/point proofs, `VisualCaptureScope.ForWindow` with `VisualPrivacyClass.REAL_APP_RESTRICTED`, recorder enforcement that routine capture is `TEST_OWNED`/`PRODUCT_OWNED` only and `REAL_APP_RESTRICTED` requires explicit policy, `qualification-bundle` privacy gating, workflow `.agent/workflows/visual-evidence-review.md`. |
| **Current scenario** | `guest-caption-maximize-notepad` (shard `real-app`, `applications: notepad-broker`) and `keyboardinput-notepad`/`maximize-repro --guest wt` plus `browser-fullscreen-contained --guest chrome-normal` adopted path; exercised via `SpawnNotepad`/`SpawnGuest wt` with exact HWND/owner/root/PID/start/exe/class/ancestry logging, generation-gated `IsCurrentCapturedWindow`/`IsCurrentMutationGeneration`, and `VerifyGuestForKill` refusing to kill adopted PID/title. Notepad broker/host inspected per `Scenarios.Core.cs` external-Notepad check. |
| **Exact acceptance requirement** | Adopted app identity proven (`HWND`/`PID`/`TID`/`class`/`exe`/`start`/`owner`/`root`/`token`/`generation`), visual scope restricted to host + bounded context, unrelated desktop/user content excluded, whole-desktop capture disabled, packet/verifier reflects `REAL_APP_RESTRICTED`, `Valid:true` where review required, `REVIEW_UNAVAILABLE` remains blocked for required gates. |
| **Visual/privacy requirement** | Default real-app capture is **minimized/cropped** to the smallest region needed to prove presentation (`VisualCaptureScope` requested/actual rect, monitor/DPI, method, privacy); whole-desktop is `BLOCKED`; `TABDOCK_VALIDATION_ARTIFACT_ROOT` retains only approved scopes; support bundles exclude real-app imagery; multimodal review uses restricted images only; if privacy-safe crop impossible, visual is `BLOCKED_CAPABILITY`/`REVIEW_UNAVAILABLE` without silently widening. |
| **Final disposition** | **COMPLETED_AND_PROVEN** when adopted Notepad/Terminal (or Chromium) is captured with exact identity, scope restricted, verifier passes, and reviewer verdict `VISUAL_OK` or explicitly `REVIEW_UNAVAILABLE` with justification; otherwise `ACCEPTED_BLOCKED_CAPABILITY`/`ACCEPTED_BLOCKED_ENVIRONMENT` with rationale (never narrowed to fake `PASS`). Durable matrix records per-app visual outcome, packet hash, and privacy scope. |

## Closure invariant

- No row silently disappears. Each task in `openspec/changes/real-app-hardening/tasks.md` ends in one truthful state: `COMPLETED_AND_PROVEN`, `ACCEPTED_BLOCKED_CAPABILITY`, `ACCEPTED_BLOCKED_ENVIRONMENT`, or `ACCEPTED_SKIP_CAPABILITY`.
- Synthetic GuineaPig `Valid:true` never satisfies a real-app gate.
- First valid `FAIL_PRODUCT` is never erased by a rerun.
- The campaign may archive with explicit capability blocks only if the proposal's acceptance boundary permits them and every requested cell has a truthful final disposition (it does).
- After archive, `openspec/changes/` will contain only `archive/` (no active change).
