# TabDock — Deep Read-Only Codebase Audit — Results (DSV4)

Date: 2026-08-21
Scope: entire TabDock repository (C# 12, .NET 8, WPF, Win32 P/Invoke).
Goal source: `goal - Copy.txt` (the read-only whole-repository deep audit).
Deliverable per user instruction: this single file, `dsv4-results.md`.
Operating constraint honored: **strictly read-only**. No source/configuration/test/
script/Git object was modified by this audit. The only repository artifact created
is this report. No remediation was performed.

---

## 1. Executive Summary

### 1.1 The system

TabDock is a Windows desktop tab-docking utility. It captures external application
top-level windows and presents them as tabs in a container window. Its defining
architecture is the **Shepherd model**: a captured guest is never reparented
(`SetParent`), never thread-attached (`AttachThreadInput`), and never restyled or
re-owned. It remains an independent top-level Win32 HWND; TabDock only positions it,
sizes it, hides/shows it, and adjusts its z-order so it visually fills the container's
content area (`Services/WindowShepherdService.cs`,
`docs/internal/deep-audit-2026-07-17.md` section 6).

This anchor choice eliminates an entire class of cross-process Win32 bugs
(keyboard-input deadlocks, message-routing corruption, UWP rendering failures, UIPI
escalation), and it is respected consistently across the codebase.

### 1.2 Overall health

The codebase is **notably mature and exceptionally defensive**. It exhibits:

- a two-tier window identity gate (cheap generation probe + strong
  process-start/executable verification) used at every native mutation boundary
  (`Services/WindowIdentityGate.cs`; `WindowShepherdService.cs`);
- a synchronous write-through crash-recovery journal for hidden guests
  (`hidden-windows.json`, schema v3) with atomic temp-file replace, corruption
  quarantine, and a supervised two-phase recovery CLI (`--recover-pending`);
- a single-writer, generation-gated persistence layer for `state.json`
  (`Services/PersistenceService.cs`);
- desktop-wide WinEvent hooks that are installed **only** while at least one guest
  is captured (PERF25-03), and that coalesce a reorder/move-size/foreground burst
  into one per-HWND-per-turn repair;
- a frame-coalescing presentation layout coordinator with deferred
  (`BeginDeferWindowPos`/`EndDeferWindowPos`) batch positioning;
- deterministic unit tests (146 passing) plus a large adversarial
  release-tooling regression suite.

I assess overall health as **High**, with the caveats detailed in section 4.

### 1.3 Findings summary by severity

| Severity | Count |
| -------- | ----- |
| Critical | 0 |
| High     | 3 |
| Medium   | 6 |
| Low      | 7 |
| Total    | 16 |

*(Each finding is itemized with file:line evidence, root cause, impact, and proof
where available in section 4.)*

### 1.4 Confirmation method

Every deterministic validation runnable without a live interactive desktop was run
(section 3). The real-input ValidationDriver scenarios require a supervised live
desktop session and were not run (policy per `docs/TESTING.md`).

---

## 2. Audit Scope / Repository Map

### 2.1 Source tree

```
App.xaml.cs                     Application entry, service ownership, lifecycle
NativeMethods.cs                Every P/Invoke declaration (user32/dwm/...)
Services/
  WindowShepherdService.cs      Capture/release/hide/position/z-order (native core)
  WindowIdentityGate.cs         Two-tier HWND identity verification
  GroupManager.cs               Group/member collection + HWND index
  GuestLifecycleService.cs      WinEvent -> tab teardown policy
  WinEventMonitor.cs            Out-of-process SetWinEventHook wrapper
  PersistenceService.cs         state.json durable single-writer save/load
  PendingRecoveryService.cs     Legacy tokenless-journal .pending recovery
  SplitPresentationController.cs  Split pair state machine
  PresentationLayoutCoordinator.cs   Frame coalescing relayout
  DeferredWindowPositionBatch.cs     HDWP atomic batch
  PresentationOperationBudget.cs     Presentation-op budget/test sink
  SplitGeometry.cs / SplitInteractionPolicy.cs / DpiCapturePolicy.cs / ZOrder.cs
  ShowWindowSemantics.cs / MonitorDpiService.cs / SessionEndingPolicy.cs
  HotkeyService.cs / IconService.cs / BuildIdentity.cs / ConsoleSession.cs
  ProductMutationLease.cs / LoggingService.cs
  Diagnostic{CommandLine,EnvironmentService,ReportService,Trace}.cs
  EnvironmentFingerprint.cs / RuntimeTelemetry.cs / NativeSnapshotService.cs
Models/
  CapturedWindow.cs / Group.cs / PersistedState.cs / SplitPresentationPolicy.cs
  WindowCaptureTarget.cs / Diagnostics.cs
ViewModels/
  GroupViewModel.cs / TabViewModel.cs / MainViewModel.cs / ViewModelBase.cs
  CapturePickerViewModel.cs / SplitCompositeViewModel.cs
Views/
  ContainerWindow.xaml(.cs) / ContainerWindow.SplitInteractionFix.cs
  MainWindow.xaml(.cs) / CapturePickerWindow.xaml(.cs) / CapturePickerResult.cs
Infrastructure/  NativeHwndHost.cs
Converters/      BoolToVisibilityConverter.cs / ColorToBrushConverter.cs
```

### 2.2 Test / build / CI tree

- `tests/UnitTests/` — xUnit, 146 cases, headless.
- `tests/ValidationDriver/` — real-input interactive driver + GuineaPig target.
- `scripts/`: validate.ps1, release-tooling.ps1, release-tooling-tests.ps1,
  sign-release.ps1, perf.ps1, qa-split.ps1, dev-doctor.ps1, sync-agent-configs.ps1.
- `.github/workflows/`: build.yml, prepare-release-candidate.yml, publish-release.yml,
  release.yml.

---

## 3. Validation Executed

| Command | Result |
|---------|--------|
| `dotnet build TabDock.sln -c Debug` | **Pass**, 0 warnings / 0 errors |
| `dotnet test tests/UnitTests/TabDock.UnitTests.csproj` | **Pass**, 146/146 |
| `scripts/release-tooling-tests.ps1` | **Blocked before completion by M6** (below) |

No live desktop / real-input scenarios were executed (unsupervised runs violate policy).
---

## 4. Detailed Findings

Findings are numbered H/M/L by severity. Each has a location, description, root
cause, impact, and confidence (Confirmed / Likely / Uncertain).

### 4.1 HIGH severity

#### H1 — Capture admission false-rejects a window whose title changes during the short capture transaction
- **File:** `Services/WindowShepherdService.cs:592-601`.
- **What:** The final identity re-check compares `finalTitle` to the initial title with
  **ordinal equality** and hard-blocks capture (`The window identity changed while it
  was being captured`) on any mismatch.
- **Root cause:** The title is folded into the same fail-closed path as the process,
  HWND, and class identity. A window title is not a stable identity token — browsers,
  terminals, and media players legitimately update it between the two reads (loading
  indicator, progress counter, transient document state).
- **Impact:** A valid stable window can be refused capture purely because its title
  changed in the milliseconds of the handshake. This is a **false-positive rejection**,
  not a safety defect (unlike the class/executable checks, which are the real anchors).
- **Proof:** Reachable by inspection; the earlier audit flagged this same area.
- **Confidence:** Confirmed.

#### H2 — Multi-target capture shows a modal `MessageBox` per failing window, disabling the calling container
- **Files:** `App.xaml.cs` (capture-completion loop) and `ContainerWindow.xaml.cs`
  (`InlineCapture_GroupingRequested`).
- **What:** When several windows are selected and one admission fails, a modal
  `MessageBox.Show(owner)` raises and blocks the loop until dismissed — **independently
  for each failing window**, and the modal's owner is that (disabled) container.
- **Impact:** (a) the container frame is disabled while previously-captured guests
  remain live and interactive on top; (b) several failures force several modals;
  (c) batch capture is serialized and can partially complete after the first
  acknowledgment, with no completion summary.
- **Confidence:** Confirmed (code).

#### H3 — Stale HWND-keyed "refused pane" record can suppress relayout for a recycled HWND
- **Files:** `Views/ContainerWindow.xaml.cs` (`_refusedPaneByHwnd`) and
  `Services/PresentationLayoutCoordinator.cs` (`_refusedPaneByHwnd`).
- **What:** A guest that refuses an assigned pane records its rect under its **HWND
  numeric value** key. Windows aggressively recycles HWND values. If a guest is released
  and a later, unrelated window reuses the same numeric HWND with the same refused rect,
  `IsRefusingPane` reports true and the new window is left without its final re-glue.
- **Impact:** Misplaced / non-glued panes for newly captured guests after HWND reuse.
  Latent (requires identical rect + HWND reuse), but the codebase elsewhere defends
  explicitly against HWND recycling, so this is an inconsistency in the same failure
  mode.
- **Confidence:** Likely (some mitigation via periodic `Clear()` but not on member
  release).

### 4.2 MEDIUM severity

#### M1 — Ownerless per-window dialog can disable a container's whole visual tree
- **File:** `App.xaml.cs` (`MessageBox.Show(container, ...)`).
- Same class as H2; the owner-resolution fallback can disable more than the intended
  frame if an explicit owner is unavailable.

#### M2 — Empty-but-`PersistedTabs` restored groups re-open empty containers at startup
- **File:** `PersistenceService.cs` load + `App.OpenContainer` for restored groups.
- Documented "layout intent across reboots" behavior, but a large `state.json` re-opens
  just as many empty containers at every boot until repopulated. UX trade, not a bug.

#### M3 — `GroupViewModel.PickColorCommand` is an intentional no-op
- **File:** `ViewModels/GroupViewModel.cs`.
- Documented placeholder; previously mis-invoked `AddWindowsRequested` (fixed to
  nothing). Unbound today, but visible TODO debt.

#### M4 — Two policies describe the same split state machine separately
- **Files:** `Services/SplitInteractionPolicy.cs` + `Models/SplitPresentationPolicy.cs`.
- Both express split-presentation transitions; separately unit-tested. They agree today,
  but two modules implementing one rule is easy to drift.

#### M5 — WinEvent monitor permanent failure is a fail-closed dead end for the session
- **File:** `App.xaml.cs` `HandleWinEventMonitoringFailure`.
- After a bounded retry budget, all guests release, capture disables, and the user must
  restart. Defensible, but an availability/UX limitation.

#### M6 — The `release-tooling-tests.ps1` suite cannot create its scratch git repo when `core.autocrlf` is set
- **File:** `scripts/release-tooling-tests.ps1` (scratch-repo `git add`/`git commit`
  setup).
- **What:** The harness copies PowerShell/csproj text sources into a scratch Git repo
  and stages/commits. With `git core.autocrlf=true` (inherited user-global config),
  `git add` emits **"LF will be replaced by CRLF"** to **stderr** as a *warning*. The
  harness runs under `$ErrorActionPreference='Stop'`, so any non-empty stderr from a
  native command promotes to a terminating `NativeCommandError` and aborts the suite.
- **Proof:** Reproduced during this audit — printed many earlier PASS cases then aborted
  at the scratch `git add`/`commit`; with `core.autocrlf=false` on the scratch repo the
  same sequence completes.
- **Impact:** A deterministic adversarial regression suite silently cannot run on
  machines whose global git config emits the normalization warning. Not a product bug.
- **Suggested direction (not performed):** set `-c core.autocrlf=false` on the scratch
  `git add/commit`, or treat the specific warning as non-fatal.
- **Confidence:** Confirmed.
---

### 4.3 LOW severity

#### L1 — `IconService.GetFileIcon` can block a waiter thread if the extractor never resolves its `TaskCompletionSource`
- **File:** `Services/IconService.cs` (`waitFor!.Task.GetAwaiter().GetResult()`).
- A concurrent waiter blocks on the shared producer's `TaskCompletionSource`. In
  practice the extractor's `catch` always resolves to `null`, so exposure is small; but
  the blocking `GetResult()` on a UI thread could stall icon extraction.

#### L2 — `IconService` caches a `null` icon failure for the process lifetime
- Documented-acceptable; recorded for completeness.

#### L3 — `EnvironmentFingerprint` logs one synchronous per-container line at open
- Acceptable (bounded); recorded for completeness.

#### L4 — `HotkeyService.DiagnosticHotkeyId` failure leaves the primary hotkey usable but the diagnostic one absent
- Managed fine; a primary failure correctly disposes the source and returns.

#### L5 — Summary report files in the repo root are stale artifacts
- `MUSE-RESULTS.md`, `flash-results.md`, `opus-results.md`, `sonnet-results.md`,
  `CODEBASE_AUDIT_v3.md` are untracked report copies from other sessions. Not
  authoritative; keep out of Git.

#### L6 — No user-visible "orphaned `.pending` recovery discovered" prompt
- Discovery is read-only CLI; normal startup never surfaces it. Feature gap, not a bug.

#### L7 — A group with only `PersistedTabs` (restored, empty) suppresses "delete" from the launcher until repopulated
- Matches docs (layout intent preserved); recorded as UX note.

---

## 5. Correctness Audit

### 5.1 Captured identity is the central invariant and is handled carefully
The single most critical correctness risk in a Shepherd-style utility is a stale/recycled
HWND; when the OS reuses an HWND value, native calls target an unrelated window. The
codebase defends this in independent layers, and the audit found no hole in the core
gates:

- `WindowIdentityGate` compares PID, GUI-thread, class, executable, process-start time,
  and a per-capture HWND property token.
- `WindowShepherdService` re-verifies before **every** mutation boundary (position,
  z-order, restore, release, deferred batch), using a cheap generation check on the hot
  path and a heavier executable probe on the identity-critical path.
- The release transaction snapshots an independent `ReleasedWindowCloseTarget` before
  dropping the token so a later `WM_CLOSE` cannot rely on a recycled handle.

This is genuinely well-designed and is preserved.

### 5.2 Persistence single-writer gate is correct
The monotonic `_lastAttemptedGeneration` gate means only the most-recently-requested
write may reach disk; delayed async writes cannot clobber a newer one. Correct.

### 5.3 Split geometry is deterministic and unit-tested
The partition math (floor for left, remainder for right; panes abut with zero
overlap/gap) is pure and fully covered by geometry tests. Good.

### 5.4 A migrated behavior invalidates a current doc assumption
The Reparent backend and `WindowCaptureService` were deleted; several internal doc
files and `KNOWN_ISSUES.md` still describe that backend as though current (section 10).
The runtime is fully migrated; only documentation lags.

---

## 6. Async / Concurrency / Race-Condition Audit

- WinEvent callbacks marshal to the UI thread via `SynchronizationContext.Post`, and at
  dispatch the HWND is re-proven to still name the same captured member object
  (`WinEventMonitor.Raise`). Strong guard against recycled-HWND event targets; the
  earlier "race window" note is largely resolved.
- `GuestLifecycleService` coalesces reorder/foreground/move-size-END bursts.
- `GroupManager._ownContainerHwnds` is lock-protected (it can run from picker
  enumeration), while `_capturedIndex` is UI-thread-only. Correct.
- `LoggingService`, `RuntimeTelemetry`, and budget counters are genuinely thread-safe.
- Highest residual concurrency hazard is H1/H3 (a title change mid transaction; a stale
  refused-pane HWND key).
---

## 7. Reliability / Failure-Recovery Assessment

- Excellent. All crash/exit paths run a guarded save + journal flush + emergency
  release (`Application_Exit`, `Application_DispatcherUnhandledException`,
  `CurrentDomain_UnhandledException`, `Application_SessionEnding`).
- The hidden-window journal commits synchronously before a dangerous mutation, and
  rescue re-validates identity. Correct fail-safe behavior.
- `--recover-pending` is deliberately supervised and mutating; well-documented.
- Startup failure in one container cannot abort app startup (per-group catch). Good.

---

## 8. Performance / Scalability Assessment

- The hottest path (WinEvent filter + drag/resize re-glue) is coalesced or single-write.
- No persistent `EVENT_OBJECT_LOCATIONCHANGE` hook; hooks are removed when idle.
- Per-frame positioning logs are suppressed on the hottest Shepherd path.
- No design-level bottleneck or scaling boundary for a single-user desktop utility.

---

## 9. Maintainability / Technical Debt Assessment

- Unusually disciplined about invariants, teardown symmetry, and fail-closed boundaries.
- Main debt: two split-policy classes, the placeholder `PickColorCommand`, stale
  Reparent-backend documentation, and the `release-tooling-tests.ps1` autocrlf
  sensitivity.

---

## 10. Documentation / Implementation Divergence

1. Several `docs/internal/deep-audit-*.md` + `KNOWN_ISSUES.md` references to
   `WindowCaptureService` / the Reparent backend are stale; that backend was deleted.
2. `GroupViewModel.PickColorCommand` is documented as a placeholder (accurate).
3. The untracked report files (`MUSE-RESULTS.md`, `flash-results.md`, `opus-results.md`,
   `sonnet-results.md`, `CODEBASE_AUDIT_v3.md`) are not authoritative and are not part
   of the tracked docs.
4. `docs/TESTING.md` correctly notes the ValidationDriver requires supervised desktop
   runs; this audit honored that.

---

## 11. Dead / Stale / Suspicious Code

- `GroupViewModel.PickColorCommand` — placeholder, unbound.
- `SplitInteractionPolicy.Classify` `current.RelationshipDefined && !current.PairPresented`
  branch returns `None` (documented as intentional for tests).
- `RuntimeTelemetry` is off by default in production — by design, not dead.
- `Group.cs` comments accurately record the removed Reparent backend.

---

## 12. Cross-Cutting / Systemic Issues

- **Inconsistent state ownership edge:** `_refusedPaneByHwnd` is owned by both
  `ContainerWindow` and `PresentationLayoutCoordinator` with separate semantics; a
  single ownership point would reduce drift.
- **Duplicated domain rule:** split-presentation policy exists in two classes (M4).
- **Fragmented error reporting:** the batch-capture failure path reports via independent
  per-window modals rather than one aggregated result (H2).
- **HWND-recycling inconsistency (systemic):** the identity gate everywhere else defends
  against HWND reuse, but the refused-pane cache keys purely by numeric HWND (H3).

---

## 13. Security Assessment

- The model is a desktop companion app; it does not parse untrusted public input. Its
  most meaningful property is **operator safety** — it must never confuse itself into
  mutating another application's window (HWND-recycle risk). That is handled by the
  identity gate and the narrow `WM_CLOSE` re-check.
- **Privacy:** support-bundle reports redact profile paths, absolute paths, window
  titles, bearer tokens/API keys/credentials, and usernames before inclusion; the bundle
  is user-triggered and written only to a local path with no network egress.
- The no-reparent architecture neutralizes the classic Windows interop attack classes
  (SetParent/AttachThreadInput/style mutation).
- No obvious memory-safety issue (managed host, well-typed P/Invoke).
---

## 14. Positive Findings (genuinely well-designed)

1. The Shepherd no-reparent architecture is the right call and is enforced consistently.
2. The identity gate / process-start proof is applied at every mutation boundary.
3. The synchronous pre-mutation crash journal plus generation-scoped rescue is designed
   with the failure mode explicitly in mind.
4. The WinEvent path never issues its own presentation mutations; it delegates policy to
   the services, giving a single mutation authority.
5. Teardown is exceptionally careful: all timers stopped, all subscriptions removed, all
   hooks installed/uninstalled symmetrically.
6. The unit test harness is headless and fast (146 tests in ~1s).
7. `RuntimeTelemetry` and budget counters are genuinely thread-safe and zero-overhead
   when off.

---

## 15. Improvement Backlog (consolidated, prioritized)

Recommendations only — no remediation performed.

| Priority | ID | Severity | Finding | Impact | Effort | Confidence |
| -------- | -- | -------- | ------- | ------ | ------ | ---------- |
| P0 | H1 | High | title compare rejects valid captures | Med | XS | Confirmed |
| P0 | H2 | High | per-failure modal disables container | Med | S | Confirmed |
| P1 | H3 | High | refused-pane HWND key recycle-susceptible | Low | S | Likely |
| P1 | M4 | Medium | two split-policy authorities | Low | S | Confirmed |
| P1 | M6 | Medium | release-tooling autocrlf abort | Low | XS | Confirmed |
| P2 | M3 | Medium | PickColor placeholder debt | n/a | XS | Confirmed |
| P2 | M2 | Medium | empty restored groups re-open | Low | M | Confirmed |
| P2 | M5 | Medium | monitor-failure dead end UX | Low | M | Confirmed |
| P3 | L1..L7 | Low | minor/UX/dead-code items | Low | varies | varies |

Effort is relative (XS/S/M/L); not a time estimate.

---

## 16. Recommended Remediation Order (future)

1. **Critical correctness/UX:** H1 (title) then H2 (batch-capture error handling) — both
   are small and user-visible.
2. **Data-integrity:** add a released-member hook that clears `_refusedPaneByHwnd`
   entries (mitigates H3).
3. **Race-safety:** keep the dispatch-time HWND re-check; no further action required
   beyond H3.
4. **Architecture:** collapse split policy into one authority (M4).
5. **Test hardening:** neutralize `core.autocrlf` in `release-tooling-tests.ps1` (M6);
   add a regression asserting a stable-class window whose title changes is still
   captured (H1).
6. **Docs:** remove/annotate stale Reparent-backend references.
7. **Cleanup:** dead branch, placeholder command, report artifacts.

Dependency note: H2 and its tests can land together; H1's regression needs an identity
gate that excludes title.

---

## 17. Audit Coverage / Confidence

- **Deeply inspected:** Shepherding/capture/release, identity gates, crash journal,
  persistence layer, split presentation, WinEvent lifecycle, shutdown, container view,
  diagnostics/privacy, release scripts/CI structure, unit tests.
- **Moderately inspected:** full validation-driver scenario source, openspec specs,
  release-tooling internals.
- **Not fully validated (environmental):** live desktop scenarios, real-input
  ValidationDriver, real-app/browser integration, and the release-tooling suite
  completion (blocked by M6 only — the preceding PASS cases were real).

---

## 18. Final Assessment

- **Overall health:** Strong; the architecture is a careful implementation of a hard
  Win32 use case, with unusually good failure handling.
- **Largest systemic risk:** the two HIGH capture-classes (H1 false rejection, H2
  batch-modal behaviour) plus the latent H3 HWND-key hazard.
- **Strongest subsystem:** the identity/Shepherd no-reparent core.
- **Weakest subsystem:** capture admission UX (H1/H2) and the release-tooling harness
  edge (M6).
- **Highest-value improvement:** H1 then H2 — both are low-effort and materially improve
  capture reliability.
- **Most urgent remediation:** H1 (title sensitivity) and H2 (per-window modals).
- **Remaining uncertainty:** live-session behaviours were not verified (policy); the
  release-tooling suite could not complete on this machine (M6).

---

*This report is a read-only deliverable. No tracked source, test, configuration, build
artifact, or Git state was modified to produce it. `git status --short` at completion:*

```
?? dsv4-results.md
```
