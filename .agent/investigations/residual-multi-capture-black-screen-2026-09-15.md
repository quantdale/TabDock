# Residual multi-capture black-screen investigation and resolution plan

Date: 2026-09-15  
Status: **implementation applied — confirmed minimized-admission defect addressed; physical recovery sequence remains blocked**  
Scope: investigation findings plus the bounded implementation follow-up recorded in §15.

## 1. Executive Summary

The reported residual defect is that, after selecting two windows in one capture operation, one tab presents and the other is absent until repeated switching eventually repairs it. This is distinct from the previously repaired first-container-render stacking defect.

**CONFIRMED:** existing production logs from the exact inspected SHA contain a concrete two-guest failure: Chrome is captured first; Terminal is captured second while already iconic (minimized). Terminal becomes the selected guest, but remains at `-32000,-32000,199x34`, with `iconic=True`, while the content host is `1920,81,1920x951`. The first guest is deliberately hidden. Several switches back to Terminal log foreground requests but no positioning. Terminal is still iconic when released.

**CONFIRMED, for this recorded failure:** `LayoutShepherdActiveWindow` returns on `IsIconic` before `PositionAndShow`, whose native implementation contains the required restore. That guard assumes an iconic guest is owned by the existing minimize-event recovery path. A guest already minimized before capture need not generate a new captured `MINIMIZESTART` event. Logical selection and controller authority therefore agree on an incoming guest that has never been restored into the content area. This is a missing lifecycle transition, not a lost layout generation or a guest-blind rectangle cache.

**HYPOTHESIS, not a confirmed explanation of the entire user report:** this is the same failure the user saw eventually recover. The retained iconic run does **not** recover before release. A separate Cursor/Chrome run contains repeated switches and earlier mixed-DPI geometry correction, but no timestamped visual bad-to-good observation. It would be false to manufacture a successful switch or claim that a certain number of clicks clears `IsIconic`.

Confidence: high in the minimized-admission defect and source-level mechanism; insufficient to attribute the entire reported eventually-recovering symptom to it. No new physical reproduction was completed in this session. The implementation plan below is concrete for the confirmed defect, with a mandatory evidence gate before claiming closure of the broader residual report.

The three-part explanation is consequently:

1. Chrome is non-iconic, so it reaches the restore/position path (including restore from maximized state) and later positions into the host.
2. Terminal is iconic, so the view returns before the exact same Shepherd path can restore it. Ordinary selection can hide Chrome without establishing usable incoming content.
3. **BLOCKED:** no observed successful Terminal transition exists in the retained run. Repeated switches alone do not remove this guard. Recovery requires an observed change to native iconic state, execution of a restore-capable path, or a different original failure. This document does not certify the anti-premature-completion criterion as satisfied.

## 2. Repository Baseline

| Item | Observed value |
| --- | --- |
| Root | `D:/Documents/tryPython/TabDock` |
| Branch | `main` |
| HEAD / local main | `7fa466805436d1f4d241c38e9da27536a7f46166` |
| `origin/main` | `7fa466805436d1f4d241c38e9da27536a7f46166` |
| Live remote main (`git ls-remote --heads origin`) | Same SHA |
| Equality | local main == origin/main == live remote main |
| Initial worktree | Clean; no staged or untracked changes |
| Local branches | `main` only |
| Remote branches | `origin/main` only; `origin/HEAD -> origin/main` is symbolic |
| Worktrees | One, repository root, main at the inspected SHA |
| Open PRs | `gh pr list --state open --json ...` returned `[]` |
| Previous substantive fix | `17ebbbdfa7674dd4f252f31f44036753b7757513` |
| Previous baseline | `586870739e8cfe8998738a1f492004b568afec21` |
| Closure commit | `7fa4668`, docs only |

Read `.agent/STATE.md`, its completed initial-capture plan, and the previous investigation. They are historical evidence, not proof of today's desktop capability. Repowise was queried first; its index names this HEAD, but synthesis is degraded (`no-llm-provider`), so actual source and Git diffs were inspected. No index refresh was needed for a new investigation document.

The existing log's `BUILD[identity]` lines at 12:28:55.492 and 12:29:38.972 both identify this SHA, Release, win-x64. This binds the two relevant historical runs more strongly than an assumption about the current binary.

Only this document is an authorized repository modification. In particular, `.agent/STATE.md` is deliberately not updated because the user's one-document restriction overrides the normal checkpoint convention.

## 3. Previous Fix Review

Actual `git show 17ebbbd` production delta:

- New `Services/InitialPresentationReconciliationPolicy.cs`: one boolean records whether the first `ContentRendered` was consumed. First call returns `hasActiveGuest`; all later calls return false. An empty first render consumes the boundary too.
- `Views/ContainerWindow.xaml.cs`: creates that policy, subscribes after `InitializeComponent`, unsubscribes on close. The handler checks current `ActiveTab`, calls `SyncShepherdActiveWindow`, then `RequestRelayout(ensureFinalPass: true)`.
- `tests/UnitTests/InitialCapturePresentationTests.cs`: four tests exercise policy consumption and coordinator callback counts. Their fake execution makes a Boolean native-health flag true; they do not construct the production container or invoke its layout gates.

The prior investigation recorded a disposable WPF/HwndHost probe with `Loaded -> Show return -> initial positioning -> ContentRendered`; container stacking could change at first composition without a changed content rectangle. This session did not repeat that removed probe. The user's improvement report and the actual lifecycle hook are consistent with that repair remaining useful.

The hook is **per container**, and its eventual layout reads the **current** active guest, not necessarily the first captured guest. In the ordinary two-successful-capture loop, B is the final active guest. Thus “the previous fix only repairs A” is not supported by source. It cannot repair an iconic B: both its direct sync no-op and its queued layout leave the iconic guard intact. Making this hook run more times would not fix that condition.

The iconic guard originated in `f1dc7ab3` (2026-08-13; actual historical diff inspected), before `17ebbbd`. Its stated purpose is to avoid restoring during minimize-then-hide tray-close classification. Preserve that purpose. Do not delete the old first-render policy or globally remove minimize protection.

Related specifications: `openspec/specs/presentation-integrity/spec.md` distinguishes visible stacking from foreground, requires bounded identity-safe reconciliation, and requires non-vacuous presentation evidence; `capture-picker-identity/spec.md` preserves candidate identity through submission; `ui-ux-hardening/spec.md` preserves split authority and final reconciliation. Any eventual behavior delta must follow the canonical OpenSpec workflow in the implementation session.

## 4. Two-Guest Capture Timeline

### 4.1 Exact successful managed path

References are source line anchors at the inspected SHA; use symbol names if later edits move lines.

| Order | Source boundary | Managed and native effect |
| --- | --- | --- |
| 1 | `Views/CapturePickerWindow.xaml.cs:32`, `OnGroupingRequested` | Enumerates master `Windows.Where(IsSelected)`, converts to identity-bearing targets, materializes a list, sets result and closes the modal picker. Order is candidate-list order, **not checkbox-click order**. |
| 2 | `App.xaml.cs:895`, `ShowCapturePickerCore` | `ShowDialog` returns; resolves or creates group; opens container before iterating targets. |
| 3 | `App.xaml.cs:1094`, `OpenContainer` | Constructs VM and container, registers container before `Show`, then hides launcher. `Loaded` caches native host/container handles and attaches layout notifications. First composition is not a capture completion barrier. |
| 4 | `App.xaml.cs:998` | Synchronous `foreach SelectedTargets`; calls `container.CaptureWindow(target)` for each, collecting failures. No `await`, dispatcher yield, per-guest render wait, or batch presentation transaction. |
| 5 | `ContainerWindow.xaml.cs:2113`, `CaptureWindow` | Admission, target identity, no-nesting and duplicate guards; calls `_shepherd.Capture`; rechecks picker identity after capture. |
| 6 | `WindowShepherdService.cs:470`, `Capture` | Samples original placement/identity; creates token and captured object; binds identity; commits recovery journal; revalidates and installs token before DWM mutation through `TryCompleteCaptureAfterJournal`. Capture preserves original placement; it does not restore/position the guest into the host here. |
| 7 | `GroupViewModel.cs:295`, `AddCapturedWindow` | Builds tab/icon; adds group member (captured-member index and monitor lifecycle update synchronously), adds tab, then calls `SetActiveTab`. |
| 8 | `GroupViewModel.cs:261`, `GroupManager.cs:388` | Writes model `ActiveIndex` unless already equal, then sets VM `ActiveTab`. `SetProperty` raises property change synchronously; the container handler calls `SyncShepherdActiveWindow`. `IsActive` tab flags follow the property notification. |
| 9 | `ContainerWindow.xaml.cs:2272`, sync | If old/new references differ, journal-safely hides outgoing current member. On recovery-pending hide, rolls logical selection back and does not show incoming. Otherwise `SelectGuest(new)` commits controller authority, increments policy generation, disarms split settle, clears containment refusals and dirties size constraints. |
| 10 | Same function | Calls `LayoutShepherdActiveWindow` immediately for valid incoming HWND. If container owns foreground and chrome interaction permits, calls `SetForeground(new)`. This latter operation does **not** position or restore. |
| 11 | VM capture completion | Durably saves tab metadata; container verifies monitor admission is still healthy, records capture success and returns. Next target starts. |
| 12 | Loop completion | One summary dialog only if failures occurred; all-failed new group is discarded. No success-path per-target settle. `_capturePickerOpen` protects workflow reentry, not native convergence. |
| 13 | Dispatcher/render/native feedback | Coalesced render reads current controller guest; first `ContentRendered` requests final pass if active. WinEvent callbacks are posted to UI context and revalidate captured-object identity before lifecycle dispatch. |

Captures are sequential managed calls, **not transactionally isolated from native effects**. Synchronous USER32 operations and WPF `UpdateLayout` may reenter processing; guest processes and DWM proceed independently. The source provides no proof that A was visually composited before B is admitted. Out-of-context WinEvent delivery and its additional UI `Post` are not a rendering fence. Microsoft's hook documentation also warns about reentrancy: [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook).

### 4.2 Case A: first guest

With empty group, controller foreground starts null. A's native capture token is established before member insertion, then A becomes VM selection and controller foreground. No outgoing guest is hidden. For non-iconic A, layout reaches `PositionAndShowCore`: identity check, native restore if zoomed, `SetWindowPos(A, HWND_TOP, rect, SWP_NOACTIVATE | SWP_SHOWWINDOW)`, then guarded container-behind-guest pairing.

That is an issued native presentation attempt, not a verified compositor frame. The capture API returns success after managed admission even if layout exited early. `PositionAndShow` returns void; callers cannot equate its invocation or its log with convergence.

If A itself starts iconic, it also hits the same view guard. The defect is not inherently “second guest only.” If B becomes active before first render, the prior fix reconciles B, and A gets its next direct presentation opportunity on manual selection.

### 4.3 Case B: second guest

A remains presentation authority throughout B's identity/journal capture and until B's `ActiveTab` notification. The old guest hide is verified (visibility postcondition) before B authority is committed. There is no corresponding guarantee that B has usable content before A is hidden.

For ordinary non-iconic B, direct layout samples **B's** native flags and rect. For iconic B, layout returns at line 2591, before reading content geometry, consulting refusal history, or calling Shepherd. The controller nevertheless names B and capture reports success. A queued relayout or the first-render final pass reaches the same return.

### 4.4 Case C: first manual switch to the affected guest

Switching B -> A changes both model and VM selection, hides B, commits A, and shows/positions A. Switching A -> B again hides A, commits B, clears refusals, then returns if B is still iconic. `SetForeground(B)` may log `fg=True`; it contains no `SW_RESTORE` or `PositionAndShow`. A foreground return value is not a presentation proof.

Clicking the already-selected B can be a logical no-op; alternating A/B is **not** suppressed by reference equality. Therefore the equality early return does not explain the alternating failures in the retained log.

### 4.5 Case D: what would materially repair it, and what was actually seen

For an iconic guest, a later successful layout must observe `IsIconic == false`, or a restore-capable operation must occur before the guard. Candidate operations are existing `RestoreMinimized` after a valid active `MINIMIZESTART`, split native restore, or an external/user restore. None is established as the reported repair. The activation timer also excludes iconic guests, so waiting 120 ms or clicking repeatedly is not a guaranteed solution.

For a non-iconic guest with wrong z-order, a later `BringToFront` differs from direct redundant-layout handling: it calls full `PositionAndShowCore` before foreground. For a non-iconic guest with transient geometry refusal, switching invalidates containment history and can permit a fresh positioning attempt. These are **HYPOTHESIS** repair deltas until a bad/good trace identifies them.

The retained Terminal run ends iconic; no successful Case D can honestly be attached to it. The implementation acceptance gate must capture Case D for any separate eventually-recovering run.

## 5. Native-State Evidence

### 5.1 Artifact provenance

Read-only source artifact: `%APPDATA%/TabDock/logs/TabDock.log`, 749,971 bytes at inspection, last modified 2026-09-15 12:33:44 local time. SHA-256:

`1B5691CB05295445C19F6D75666A76F47B800A1843999CFC4475B924D523291E`

Do not commit this personal log. Relevant bounded excerpts below omit titles and personal executable paths. The logger is bounded/asynchronous, so absent lines alone are weaker evidence than a native state snapshot. No retained in-process support-bundle trace was supplied for these runs; the discovered ValidationDriver run directories were dated September 13, not these September 15 observations.

### 5.2 Chrome/Terminal run: actual chronological evidence

| Time on 2026-09-15 | Recorded observation |
| --- | --- |
| 12:29:38.972 | Build identity: inspected SHA, Release, win-x64 |
| 12:29:47.325 | Container `0xBA0A26`, normal; content `1952,112,980x560`; no guest yet |
| 12:29:47.486 | A = Chrome `0x20CDC`, captured visible, non-iconic, zoomed |
| 12:29:47.611 | A position requested at content rect; observed mismatch classified as pane refusal |
| 12:29:47.620 | A durable capture save |
| 12:29:47.628 | B = Terminal `0x790B32`, captured `rect=-32000,-32000,199x34 iconic=True zoomed=False visible=True` |
| 12:29:47.630 | Selected tab index 1 |
| 12:29:47.638 | A hidden |
| 12:29:47.645 | B durable capture save; no B position line |
| 12:29:47.775 | A hide event matched Shepherd provenance; tab retained |
| 12:29:50.112 | Maximized container settled; content `1920,81,1920x951`; B remains `-32000,-32000,199x34 iconic=True visible=True`, `docked=False` |
| 12:29:50.384–.426 | Select A; hide B; position A into host; foreground request for A |
| 12:29:51.200–.222 | Select B; hide A; foreground request for B; no B positioning |
| 12:29:54.034–.078 | Select B again; hide A; foreground request for B; no B positioning |
| 12:30:00.523–.557 | Select A; hide B; position A; foreground request |
| 12:30:01.761–.785 | Select B; hide A; foreground request for B; no B positioning |
| 12:30:06.422 | B released; native description still iconic at `-32000,-32000,199x34` |

This demonstrates an admitted, selected, native-minimized guest rather than simply a repaint failure. The final release restores capture-time placement, so its iconic state alone cannot prove it never restored at an unlogged intermediate instant; the settled failure snapshot and repeated missing position sequence are the stronger evidence.

### 5.3 Healthy/absent comparison and unobserved fields

| Field | Working candidate A | Absent B | Evidence strength |
| --- | --- | --- | --- |
| Native handle | `0x20CDC` | `0x790B32` | CONFIRMED historical handles; do not reuse them as live identities |
| Admission | Journal/token transaction succeeded | Same | CONFIRMED source plus capture logs; actual token values not exported |
| Visibility | Hidden outgoing; shown on selection | True while iconic at settled observation; hidden when inactive | CONFIRMED log/native snapshot |
| Iconic | False at admission | True at admission and failed settled observation | CONFIRMED |
| Position | Requested host rect on A selection | Minimized coordinates, not host | CONFIRMED B snapshot; A request is not pixel evidence |
| Active index | 0 on A switches | 1 on B switches | CONFIRMED logs |
| Controller authority | Source predicts A | Source predicts B | STRONGLY SUPPORTED; no live logical snapshot saved |
| Foreground | `fg=True` request result | `fg=True` request result | CONFIRMED log result; no continuous foreground observation |
| Z-order neighbors / guest vs container | Not retained | Not retained | BLOCKED historical evidence |
| Cloaking / clipping / client pixels | Not retained | Not retained | BLOCKED historical evidence |
| Pending render / final-pass latch | Not exported | Not exported | BLOCKED historical evidence; behavior established from source/tests |
| Hide recovery | No pending-hide log in sequence | Same | Supports ordinary hide path; not an exhaustive recovery snapshot |

`IsWindowVisible` indicates `WS_VISIBLE`, not actual on-screen presentation or lack of occlusion. [Microsoft documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iswindowvisible). `ShowWindow(SW_RESTORE)` restores minimized placement; its Boolean return describes prior visibility. [ShowWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow). Foreground activation has a different contract. [SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow).

### 5.4 Separate Cursor/Chrome run: do not conflate it

At 12:29:06.264 A=Cursor `0x80CB4` is non-iconic/zoomed. At .420 B=Chrome `0x20CDC` is non-iconic/zoomed. Both receive positioning attempts. At .905 the log explicitly records Chrome `GeometryMismatch`: assigned `2048,209,980x559`, observed `2048,208,784x448`. At .920 another position occurs. At 12:29:09.000/.009 full position + `BringToFront` occurs. Subsequent tab switches position both guests. No line marks which frame was black or which switch first became healthy.

The dimensions `784x448` versus `980x560` are consistent with a 1.25 scaling difference; **HYPOTHESIS** that this involved a cross-monitor DPI transition, not proof of its complete cause. Doctor confirms the host currently has primary 120 DPI and secondary 96 DPI displays; this corrects the stale single-monitor limitation in the previous checkpoint. It does not establish that this geometry mismatch explains a full black pane or the later switches.

### 5.5 Diagnostics available for the missing transition

- `--doctor` reports native visibility, iconic/zoomed, rect, owner, prev/next, foreground, topmost, cloaked, monitor/DPI and hit-test probes for discovered product/journal windows. It explicitly has no live in-process logical snapshot.
- `Ctrl+Alt+Shift+D` exports in-process diagnostics: current controller active guest, group/split/member state, expected panes, native snapshot and bounded trace (capacity 1024).
- `CreateDiagnosticSnapshot` uses `ShepherdActiveWindow` for `ActiveGuestHwnd`; independently compare group index/tab UI when testing authority divergence. It does not expose the layout coordinator's two private pending flags or all local refusal state.
- Trace records include `presentation.active-member`, `group.active-tab`, WinEvent callback/dispatch, `repair.visibility`, `repair.pair-z-order` with before/after pairing observation, and foreground result. File `SHEPHERD[position]` logs a requested rectangle, not completion.
- `RuntimeTelemetry` is opt-in and primarily counts/times operations; it is not an automatic native frame history or proof of visible content.

## 6. Hypotheses and Falsification

Verdicts below explicitly distinguish the recorded iconic case from the entire user report.

| Hypothesis | Supporting evidence / falsifiable prediction | Contradicting evidence | Verdict |
| --- | --- | --- | --- |
| B HWND invalid/recycled | Would expect failed identity or removal | Admission succeeds; settled B snapshot is native-iconic; stable tab retained | REJECTED as explanation of recorded settled absence; token not independently sampled |
| B remains minimized and restore owner is missing | Iconic before capture; layout returns; no captured minimize-start required; settled rect is minimized coordinates | Does not itself explain eventual recovery | CONFIRMED for recorded failure; broader symptom link HYPOTHESIS |
| B merely hidden | Later outgoing hides do hide B | Failed initial settled observation says visible=True, iconic=True | REJECTED as sole initial cause; hidden+iconic later is relevant |
| Container covers correctly placed B | Earlier bug and full-position reassert can repair stacking | Recorded B is nowhere near host | REJECTED as sufficient explanation for iconic run; HYPOTHESIS for separate non-iconic report |
| Another guest covers B | Could happen with failed outgoing hide | Hide verifies invisibility or rolls back; recorded outgoing hides succeed | No supporting evidence; REJECTED as primary recorded mechanism |
| Offscreen/zero/stale content coordinates | Offscreen guest is observed | Host is nonzero and valid; offscreen B is iconic before any layout | CONFIRMED offscreen consequence; REJECTED stale host/zero-size as its cause |
| Coalescing drops B | Would require captured A closure or discarded generation | No layout generation exists; closure reads current guest; switches invoke direct layout | REJECTED proposed mechanism |
| Geometry-only suppression treats B as A | LayoutUpdated cache is rect-only | Direct sync bypasses this scheduling filter; native guard samples current guest; refusal store is keyed by object and invalidated on switch | REJECTED as explanation of skipped B in iconic run |
| Same-authority no-op blocks alternating repair | Sync returns on same object | Actual A/B alternation changes reference and logs transitions; iconic return still applies | REJECTED for alternation; CONFIRMED same-tab reselection is a logical no-op |
| Outgoing hide races incoming show | Native/WPF feedback can interleave | Hide is synchronous and verifies state; no ShowWindowAsync/SWP_ASYNCWINDOWPOS in ordinary path; incoming B show is not reached | REJECTED as necessary explanation for recorded run |
| Hide provenance releases/removes affected member | Could lose or misclassify queued hides | Expected hides are token/time-bound and consumed; tab remains; no tray release in sequence | REJECTED for recorded run |
| Foreground refusal alone makes content black | Requests may be denied | B logs fg=True while still never positioned; foreground does not imply non-iconic content | REJECTED as sufficient explanation |
| First render policy only handles first guest | Per-container one-shot exists | Handler and closure read current active guest, commonly B | REJECTED; policy cannot bypass iconic guard |
| DPI mismatch/refusal delays non-iconic guest | Actual 784x448 observation and later position; switching clears refusals | No black/good pixel correlation; iconic Terminal case independent of geometry | HYPOTHESIS for separate run; requires native/visual delta |
| DWM/client repaint failure | User reports black | No captured frame/client liveness data; iconic absence already explained without it | HYPOTHESIS for otherwise healthy native state, not established |
| First vs later tab activation uses a success counter | Could explain N switches | No first-show counter or N-switch threshold exists in these paths | REJECTED |

## 7. Confirmed Root Cause

For the exact-SHA Chrome/Terminal evidence:

> During admission of a guest that is already minimized, `SyncShepherdActiveWindow` hides the outgoing guest and commits the incoming captured object as controller foreground. `LayoutShepherdActiveWindow` then sees `IsIconic == true` and returns before `PositionAndShowCore` can execute its identity-checked native restore and positioning. The code assumes minimized-state recovery belongs to `RestoreMinimizedWindow`, but that path is reached from captured `EVENT_SYSTEM_MINIMIZESTART`, not from admission or ordinary selection. No new minimize event is required for a previously minimized window. The guest therefore remains iconic/offscreen despite being the logical active guest. Render, activation-reassert and drift paths also exclude iconic guests, and ordinary alternating switches repeat the same failed transition.

The incorrect invariant is “an iconic active captured guest necessarily has a live minimize-recovery owner.” It does not. `RestoreMinimizedWindow` also ignores inactive single guests at event time; its comment says they restore on next activation, but ordinary activation has no corresponding restore before the iconic guard.

The broken presentation invariant is **selected current captured guest in a non-minimized single container -> usable non-iconic visible guest at the assigned rect**, subject to explicit failure/recovery handling. The logical equality `ActiveTab == controller.Foreground` is not itself broken in this trace; equating that equality with completed native presentation is the mistake.

**Unconfirmed portion:** the true cause of the user-observed later successful switch. No number of identical A/B transitions provides a source-level escape from a persistently iconic B. The confirmed defect must not be presented as a complete explanation of an unobserved non-iconic black-to-healthy sequence.

## 8. Why Existing Tests Missed It

Executed `dotnet build TabDock.sln -c Release --no-restore`: exit 0, zero warnings/errors. Executed `dotnet test TabDock.sln -c Release --no-build --no-restore`: exit 0, **834 passed, 0 failed, 0 skipped**. This includes initial presentation, capture boundaries/admission, group mutations, split controller/ownership, layout final pass, containment, native restore policy, hide provenance and WinEvent suites.

Inspected coverage limitations:

- Initial-presentation tests simulate native health by setting a Boolean in a callback. They never reach the view's iconic guard and model one guest, not native admission/selection of two.
- `NativePresentationRestorePolicyTests.IconicOrZoomedState_RequiresRestoreRegardlessOfStyle` proves the lower restore decision, not that the production caller reaches it.
- `GroupViewModelMutationTests` uses preconstructed captured members and fake native services; it tests selection/persistence/release parity without a live `ContainerWindow` listener performing native presentation.
- Split-controller fake `PositionAndShow` methods are no-ops; policy/authority assertions do not establish actual native visibility.
- `RequestRelayoutFinalPassTests` and `HardeningRegressionTests` prove queued execution/counts; a callback can execute correctly and still return early in the production layout function.
- `PaneContainmentPolicyTests` prove hidden guests are not refusal-suppressed. The earlier iconic return prevents those tests' decision from being reached.
- Capture-boundary tests cover journal -> token -> DWM safety, ending before group selection and first native show.
- WinEvent routing/provenance coverage verifies events that actually occur. It does not require admission to make progress when the window was minimized before monitoring.
- The physical helper `CaptureIntoGroupCore` (`Scenarios.cs:1699`) accepts a guest that is docked **or hidden**, then waits 800 ms before returning. That is reasonable for inactive membership, but is not proof that the selected guest is usable before any corrective action. Existing broader tab-switch scenarios do not substitute for a first-activation assertion on a pre-minimized candidate.

Missing decisive test: run the actual production selection/layout decision with valid captured A/B, stable nonzero host geometry, B already iconic before its admission, no later minimize-start event, and A/B alternation. Require B's restoration and native presentation on its first intended activation, not eventual success after helper interaction.

Other diagnostics executed: read-only `--doctor` confirmed build, Windows 11 build 26200, standard-user session, 120/96-DPI monitors, empty current group/journal state, and no live captured container. `--selftest-native-abi` returned `placementContract=PASS placementRoundTrip=PASS result=PASS` (44-byte runtime placement contract). App inventory through the computer-use capability succeeded; TabDock was not running. No real-input scenario was executed; the user subsequently confirmed supervision was unavailable and instructed this session to use existing evidence. No source or test instrumentation was added.

## 9. Resolution Architecture

### Confirmed defect: smallest repair direction

Correct the **explicit incoming-guest presentation boundary**, not the frame scheduler. Admission and ordinary user selection must establish or explicitly fail the required non-iconic state before passive layout is allowed to treat the guest as covered by a minimize-recovery owner.

Use the existing `SyncShepherdActiveWindow` / Shepherd restore authority. Separate the reason for a presentation request: explicit admission/selection can intentionally restore its incoming guest; passive geometry, activation echo and drift reconciliation must continue respecting an in-flight self-minimize/tray-close decision. The same captured-object identity and existing controller remain authoritative. Do not introduce a second active HWND, timer, polling subsystem, or per-app branch.

The likely minimal implementation uses the existing identity-checked `RestoreMinimized` for an incoming iconic guest at explicit selection/admission, then continues through existing layout. The exact ordering must be regression-tested: native restore may activate a window and cause synchronous/posted callbacks. Preserve outgoing hide failure rollback; do not pre-show a target when the old hide was refused. A restore failure must have an explicit bounded outcome and must not be silently declared a healthy presentation. Reuse current rollback/recovery semantics rather than inventing a second capture transaction.

A hidden iconic inactive guest must also be eligible for explicit restoration: requiring `IsWindowVisible` here would retain the trap created by TabDock's own inactive hide. Conversely an unmatched guest-initiated hide must still follow existing tray teardown. Use operation provenance/reason, not visibility alone, to distinguish these cases.

**Scope:** per actual incoming-guest transition, including first admission, not per container and not an N-click/permanent first-show flag. Preserve idempotence: ordinary non-iconic selection adds no restore; same healthy selection does no hide/show; passive repeated layout remains coalesced; any deferred completion binds to current captured object/controller epoch and rechecks membership and container lifetime.

### Gate for the broader residual report

Before selecting a different fix, record one failing and one successful activation of the same captured identity. If failing B is non-iconic and positioned, inspect stacking/hit-test/cloaking/client evidence. If the successful transition differs only by full `BringToFront`, trace exactly which guard skipped the earlier work. If a refusal clears, capture requested and observed rects and DPI before/after. Only then amend this design. Do not add per-guest render reconciliation solely because the old fix was per container.

No speculative production pseudocode is necessary: the unresolved restore-failure and reentrancy outcomes deserve behavioral tests rather than a copyable patch that assumes success.

## 10. File-Level Implementation Plan

The bounded changes described below were the implementation target. The
incoming-guest restore rows were applied in this session; the remaining rows
describe deliberately unchanged boundaries or follow-up validation seams.

| File / function | Current behavior | Required behavior / reason | Risks and bounds |
| --- | --- | --- | --- |
| `Views/ContainerWindow.xaml.cs`, `SyncShepherdActiveWindow` | Commits new authority then calls passive layout; iconic target never reaches restore | Establish incoming explicit presentation, including restore of a pre-minimized admitted/selected guest; preserve outgoing hide gate and define restore-failure outcome | Reentrant activation, selection rollback, hidden inactive guest, tray-close conflict; test all before coding broadly |
| Same file, `LayoutShepherdActiveWindow` | Unconditional iconic early return protects passive self-minimize handling | Retain passive protection; make the explicit activation path reach the existing restore safely, without blanket deletion of the guard | Avoid re-showing self-hiding guests or restoring a minimized container |
| Same file, `RestoreMinimizedWindow` | Event-triggered delayed repair; ignores inactive singles | Preserve guest-origin minimize/tray classification; reconcile stale comment about next activation with actual explicit activation implementation | Existing timers remain bounded and identity/lifetime guarded; do not add delays |
| `Services/WindowShepherdService.cs`, `RestoreMinimized`, `RestoreForMutation`, `PositionAndShowCore` | Restore already checks identity and native postconditions; position returns void | Reuse existing operation first. Change only if the chosen failure contract requires an existing native seam/outcome to be exposed | Preserve journal-before-mutation, capture token/process identity and original placement; avoid unrelated native refactor |
| `tests/UnitTests/InitialCapturePresentationTests.cs` or a focused new presentation-transition test file | One-guest abstract post-render success | Cover the actual incoming transition and its pre-layout native-state decision, with injectable native outcomes or a suitable existing seam | Do not merely duplicate policy in test-local code or assert a source substring |
| `tests/UnitTests/GroupViewModelMutationTests.cs` | Model/VM parity without live presentation listener | Add two sequential admissions/selection integration only where it exercises real transition dispatch | Model-only parity alone is insufficient |
| `tests/ValidationDriver/TabDock.ValidationDriver/Scenarios.cs` and appropriate scenario partial/registration | General multi-capture helper permits inactive hidden members and waits before scenario | Add first-presentation scenario, pre-minimized target permutations, native and visual checkpoints before corrective interaction | Preserve desktop lease, process ownership, bounded waits, first-failure evidence and helper semantics for unrelated scenarios |
| Canonical OpenSpec change artifacts | General presentation and lifecycle requirements | Plan/implement a focused scenario for pre-minimized admission and first explicit activation, following existing workflow | Do not hand-edit generated harness mirrors |

**Not planned to change without contrary evidence:** `App.ShowCapturePickerCore` enumeration, `PresentationLayoutCoordinator`, `PaneContainmentCoordinator`, `InitialPresentationReconciliationPolicy`, native declarations, split policy/controller and XAML. There is no evidence supporting batch sleeps, guest-keyed layout queue replacement, or an architectural rewrite.

## 11. Regression-Test Plan

For deterministic coverage, the seam must include the view's actual precondition/transition decision and record native intents/outcomes, not just an isolated downstream restore policy. Run red before implementing. In the implementation session a minimal injectable seam is acceptable only if the existing structure cannot exercise that decision directly; retain one production authority.

| Scenario | Required assertion |
| --- | --- |
| One normal guest | First intended presentation positions/shows; prior first-render final pass remains bounded |
| One initially iconic guest, no captured minimize event | First intended presentation restores then positions; no tab switch needed |
| Two targets in one picker result: normal A, iconic B | B is final selection in actual enumeration order and becomes non-iconic/visible/contained; A inactive-hidden; no event fabricated to unblock B |
| Iconic A, normal B | B first visible; first switch back to A succeeds; confirms state rather than ordinal |
| Both normal / both iconic | Both first activations satisfy native postconditions |
| A -> B -> A, same host rect | Per-object flags used; incoming hidden guest is shown even though geometry did not change |
| Repeat switching | Every first return to each guest succeeds; no N-switch health threshold; bounded restore/position calls |
| Same healthy guest selected repeatedly | No duplicate hide/restore/foreground churn |
| Pending render across A -> B | Queued execution uses B; any final-pass latch remains bounded; no inactive A resurrection |
| Incoming restore fails or identity becomes unverifiable | Explicit failure/pending result, no false healthy claim, no unrelated HWND mutation; outgoing handling matches chosen rollback contract |
| Outgoing hide recovery-pending | Incoming remains unpresented; old selection/authority retained as today |
| Recycled HWND / release during callback | Captured-object/token guard blocks stale native work |
| Guest self-minimize -> self-hide while active | Existing tray teardown wins; no eager passive resurrection |
| Inactive iconic guest hidden by TabDock | Explicit next selection restores despite WS_VISIBLE being false |
| Split creation after both admissions | Existing pair transaction shows both; identity/foreground and exact pane partition preserved |
| Split exit | Survivor usable full-width; partner hidden; no extra split generation/settle path |
| Container minimize/restore | Container minimize never causes guest restore; restore re-presents intended guest(s) |
| Maximize/restore | Existing zoomed normalization, content sizing and original release placement preserved |
| Mixed 120/96 DPI | Observe requested vs actual rect and ensure legitimate geometry transition clears stale refusal; no assertion based only on requested log |

Keep current selfminhide, tabswitch-hidesafety, selfminimize-timer-vs-teardown, split-minrestore and split-exit physical coverage. These specifically guard against an overly broad removal of iconic protection.

## 12. Physical Validation Plan

### Current session disposition

The Windows computer-use package initialized and listed running applications. This is **not** an absent-native-app capability claim. There was no running TabDock container to inspect. The repository's `docs/TESTING.md` requires: “Because it sends real input, the run must be supervised.” The user answered the supervision question: “Not available; use existing evidence.” No input was sent and no scenario was claimed to run. The computer-use skill was used only for app inventory; terminal interaction was not automated.

Repro 1 (one app), Repro 2 (two apps), Repro 3 (first switches), Repro 4 (recovery switch), Repro 5 (reversed order), Repro 6 (different pair): **BLOCKED for new physical execution: supervision unavailable; user directed use of existing evidence**, not PASS. Existing production log evidence supports the separate cases described above, not a new reproduction by this session.

The user also confirmed they do not remember which historical run recovered, which guest was black, its pre-capture minimized state, or the repairing action. Historical run correlation therefore remains unknown; recollection cannot close the recovery-evidence gate. The procedure below is a future validation plan, not an action authorized for this session without supervision.

### Exact next procedure

1. Verify candidate SHA, clean baseline, Release artifact identity, current monitor/DPI topology and no unowned TabDock instance. Arrange a supervised exclusive desktop with the existing qualification lease. Use disposable test-owned windows first; preserve real-app state and do not kill adopted processes.
2. Enable existing bounded diagnostic/visual evidence through the harness. Record capture identities and original native placement before opening the picker. No production instrumentation is required for native snapshots; use an attached debugger only if the unexported pending state becomes decisive.
3. New workspace, one ordinary visible guest: capture, take the first settled native/visual checkpoint before clicking any tab or content. Repeat with the guest minimized **before opening the picker**. A capture of an iconic first guest is a critical negative control.
4. New workspace with two distinct guests A/B. Record candidate-list/tab order separately from click order. Capture both from one submission. Record active index, controller active guest (in-process hotkey bundle), both guest native states, host rect and five representative content hit-test roots immediately after return/first render. Do not first maximize, resize, click the guest or enter split: those can repair other defects.
5. For the confirmed trap, pre-minimize B and record `IsIconic=true` before capture; require no new `MINIMIZESTART` after capture. Repeat with pre-minimized A, then both non-iconic. A black guest that is non-iconic falsifies this mechanism for that attempt.
6. Click A once, checkpoint; click B once, checkpoint. Each checkpoint includes `IsWindow`, visibility, iconic/zoomed, rect/client rect, current capture identity, foreground, z-order neighbors, guest/container relation, hit-test roots, cloak state and actual client pixels. Record exact click times and existing trace events.
7. If black persists, take bounded per-switch checkpoints (up to 20 alternating switches) and freeze the **first** healthy one and preceding failure. Record any external restore, caption click, maximize, split, monitor move or menu interaction. Do not report “timing”: compare branch-enabling state and native operations between those two checkpoints. If no recovery occurs, report persistent failure, not a repaired result.
8. Reverse actual enumeration order, not merely checkbox click order. Verify it via capture logs or tab-strip X order (`TabStripOrder` already exists). Repeat with two different eligible app families and normal/maximized/minimized preconditions. The existing actual logs identify Chrome/Terminal and Cursor/Chrome, but do not prove application independence of eventual recovery.
9. After the first-presentation assertion, exercise split creation/exit, container minimize/restore and maximize/restore, and the existing minimize-then-hide scenarios. Include each current monitor separately and mixed-DPI transfer. Do not alter display configuration as part of this investigation.
10. Release captured guests normally; verify original native placement/visibility and no orphaned identity/journal records. Preserve first failure artifacts even if later runs pass. Inspect visual packets before declaring presentation PASS.

The implementation is accepted only if initially failing first activation now succeeds without extra clicks, and the original reported black-to-healthy sequence has a defensible disposition. A green geometry check or `fg=True` alone is insufficient.

## 13. Non-Goals

- No changes to Shepherd/no-reparent, ownership/style, capture identity, recovery-journal durability, or release safety.
- No Rust rewrite, new presentation subsystem, alternate active-guest field, refactor campaign or application-specific workaround.
- No sleeps, new delays, polling, unconditional relayout or unbounded native retries.
- No removal of existing first-render reconciliation, split settle or legitimate minimize/tray protections without contrary evidence.
- No source/test/build/configuration edits outside the bounded incoming-guest restore repair and its deterministic seam coverage; no commits, PRs, merges, cleanup, branch deletion or worktree changes.
- No claim that passing headless tests proves native visual health, or that confirmed iconic absence proves the separate reported eventual recovery.

## 14. Implementation Handoff

1. Preserve the one-shot initial-presentation fix and the explicit incoming restore boundary already implemented here.
2. Resolve the remaining evidence gate: correlate the user's recovering run with a native bad/good pair. Do not repeat the full source investigation; start with the exact conditions and log timestamps above.
3. Reproduce the independently confirmed pre-minimized-admission trap, preferably with a disposable non-Terminal guest, and verify that first intended presentation succeeds without a subsequent minimize event.
4. Keep the restore-failure/reentrancy rollback and captured identity checks intact; do not widen the passive iconic guard.
5. Add any further deterministic first-admission/first-switch coverage only for a separately demonstrated non-iconic failure mode.
6. Preserve passive minimize/tray handling, per-object containment, split behavior, and current render coalescing; avoid expanding changes into split or picker batching without evidence.
7. Run the regression matrix and normal repository build/tests/OpenSpec validation. Run supervised first-presentation, hide-safety and adjacent split/minimize physical checks on the exact candidate.
8. Close the residual report only after the user-observed eventually-recovering case is explained or separately dispositioned. This document confirms and repairs one real defect but does **not** claim an unexplained successful switch as proven.

## 15. Implementation Follow-up (2026-09-15)

The confirmed minimized-admission repair was implemented after the investigation. The broader user report still has no supervised bad-to-good native trace, so this implementation is intentionally scoped to the mechanically demonstrated defect; it does not claim that every possible non-iconic/z-order failure has been ruled out.

### Production changes

- `Views/ContainerWindow.xaml.cs`: `SyncShepherdActiveWindow` now treats an explicitly incoming iconic guest as a presentation transition that must restore the guest before the outgoing guest is hidden. It uses the existing identity-checked `WindowShepherdService.RestoreMinimized` operation, records the outcome, and fails closed with rollback if restoration or the outgoing hide cannot be completed. The existing passive `LayoutShepherdActiveWindow` iconic guard remains unchanged, so background relayout and guest self-minimize/tray-close handling are not broadened.
- `Services/WindowShepherdService.cs`: the release-native seam now exposes post-restore `IsIconic`/`IsZoomed` state through the same injected API used for `ShowWindow`, allowing the existing restore postcondition to be tested without bypassing the identity/journal boundary. Production still delegates directly to USER32.

### Regression coverage

- `tests/UnitTests/InitialCapturePresentationTests.cs` verifies that an iconic incoming guest requires explicit restoration only for a non-minimized container and verifies that restoring a pre-minimized captured guest issues `SW_RESTORE` and clears the fake native iconic state.
- `tests/UnitTests/TestInfrastructure/ShepherdTestFixtures.cs` models visibility, iconic, zoomed, and restore-command state for the existing Shepherd release seam.
- `tests/UnitTests/CaptureBoundaryTests.cs` supplies the new seam members for its identity-only fake.

### Validation

- Focused initial-presentation tests: **8/8 passed**.
- Full Release solution tests: **838/838 passed**.
- Release solution build: **0 warnings, 0 errors**.
- Read-only `--doctor` and `--selftest-native-abi`: **passed**.
- Supervised physical reproduction: **BLOCKED** because the user was unavailable to supervise real input; no new desktop run is claimed.

The implementation handoff remains relevant for the unresolved portion of the report: a future supervised run must capture a native bad-to-good pair before attributing any additional non-iconic recovery behavior to this change.

**IMPLEMENTATION APPLIED — physical validation and the separate eventually-recovering sequence remain an explicit follow-up gate.**

## 16. Qualification Follow-up (2026-09-15)

This qualification pass reverified the uncommitted implementation at the
working-tree baseline `7fa466805436d1f4d241c38e9da27536a7f46166`.

### Code-review result

**CONFIRMED:** the repair remains scoped to the explicit incoming selection
boundary. It checks the incoming HWND before calling the existing
identity-checked `RestoreMinimized`, restores before attempting the outgoing
hide, rolls back the logical selection on a pending restore/hide, and leaves
the passive `LayoutShepherdActiveWindow` iconic guard and split branches ahead
of the new code unchanged. No timer, sleep, polling loop, application-specific
branch, or second presentation authority was introduced.

### Physical qualification

**PHYSICAL VALIDATION BLOCKED.** The computer-use inventory returned no native
apps and no browsers. The prior user instruction also denied supervision for a
real-input run. Therefore Test A, the two-guest capture, order reversal,
different-pair, tab-switch, minimize, split, and resize workflows were not
executed. No PASS is inferred from the absence of a desktop surface, and no
mouse or keyboard input was sent.

### Automated revalidation

- Focused `InitialCapturePresentationTests`: **8/8 passed**.
- Release solution tests: **838/838 passed**.
- Debug solution tests: **838/838 passed**.
- Release solution build: **0 warnings, 0 errors**.
- `scripts/validate.ps1 -Configuration Release -Ci -Publish`: **passed**,
  including the synthetic resource/visual gate, native ABI, publish smoke, and
  **38/38** OpenSpec checks.

No evidence-driven source correction was identified during this review. The
repair remains preserved and uncommitted pending a supervised physical
first-activation result.
