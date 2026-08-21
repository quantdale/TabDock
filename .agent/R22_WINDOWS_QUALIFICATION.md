# R22 — Interactive Windows Qualification & Torture Campaign (FINAL)

Campaign window: 2026-08-21 ~10:15 – 14:10 local. Baseline at start:
`5980e0cf30fa3e53a19c11fa1fd42facc8861a48` (R21 tip, verified == origin/main,
clean tree). Work main-only; no force-push; no release published.

## Classification

**QUALIFIED_WITH_EXTERNAL_BLOCKERS.**

All qualification that was executable in this environment passed. One real
product regression was reproduced and fixed with regression coverage. The
remaining gaps are (a) the long-standing external gates (mixed-DPI hardware,
Windows 10 x64, signing credentials, human smoke) and (b) a subset of
supervised shards that became **BLOCKED_ENVIRONMENT** when the operator's
desktop stopped being exclusively available mid-campaign (user applications
appeared; the driver's fail-closed identity guard correctly refused input).
Those shards have exact rerun commands recorded below and are resumable.

## Environment (evidence `.artifacts/r22/environment.txt`)

Windows 11 Home Single Language 10.0.26200 x64 · local Console session
(TerminalServerSession=False) · 1×1920×1080 @100% DPI, no negative coords ·
AMD Radeon iGPU + RTX 2050 · .NET SDK 8.0.424 / WindowsDesktop 8.0.30 · pwsh
7.6.5 · unelevated.

## Product defect reproduced & fixed

**HIGH — capture did not durably persist first-tab metadata**
(`52cd3ca`). Supervised `persist-kill`, `persist-active-tab-index`, and
`restored-group-survives-member-reclose` all failed identically: capturing
the first tab into a new group wrote nothing to state.json. Root cause:
`CreateGroup`'s durable commit runs before any member exists, and R21 commit
`08fc456` made `GroupManager.SetActiveTab` a no-op when `ActiveIndex ==
index` — always true for index 0 into an empty group — removing the only
save trigger on the capture path. A hard kill in that window lost the group
entirely. Fix: `GroupViewModel.AddCapturedWindow` issues one durable
`tab-captured` save after successful commit. Regression coverage = the three
supervised scenarios above (re-run green); no headless seam exists for this
WPF-bound path (documented disposition).

No other product defect was reproduced. All other findings were harness/
interaction-layer and are listed below. Recon-only theoretical observations
(stale-expectation absorption ≤15 s in hide provenance; show-path lacks
post-set verification; duplicated survivor logic policy/controller) remain
observations — never reproduced live, per campaign operating rule.

## Harness defects found & fixed (qualification-enabling; no app behavior change)

| Commit | Defect | Fix |
|---|---|---|
| `e9ac3dc` | `all` orchestrator executed shard children as raw managed DLLs (CLR bind failure 0xE0434352); path never exercised while supervised runs were blocked | Prefer apphost beside assembly; fall back to dotnet host; never execute DLL |
| `b2bae03` | New soak test flake: mid-burst delete raced in-flight `.bak` handle | Bounded transient-retry on injected delete; 20/20 green |
| `b2bae03` | Stale doc claimed `SPLIT[replace]` log line exists | Corrected to `SPLIT[enter]` |
| `52cd3ca` | Relaunched TabDock processes unregistered → provenance guard refused (`process-not-registered`) at 11 relaunch sites | Register via `TestRunProvenance.RegisterLaunchedProcess` |
| `52cd3ca`+`8427588` | Substring picker-row matching ambiguous for same-process prefix titles (`X` vs `X-W2`) | Exact-match row discovery (`CaptureIntoGroupExact`) incl. retry path |
| `52cd3ca` | `three-app-torture` hard-failed when Chrome absent | `SKIP_BROWSER_NOT_INSTALLED` skip |
| `99747ac` | Activation reassert closed `+ New group` popup between UIA discovery and click (1/5 flake) | `TryClickVerifiedPopupItem` verify-under-cursor + reopen/retry; 10/10 green |
| `3847148` | `Group these` sampled disabled once; selection-only requery is dispatcher-queued | Poll IsEnabled up to 2 s |
| `d3314f8` | Same popup race in `ClickTabSubmenuItem` (torture phase c) | Retry open-hover-click ×3 + verified child click |
| `ef27e4c` | `GetForegroundWindow()=NULL` transient treated as fatal (split-move) | Retry read ≤600 ms before refusing |
| `ef27e4c` | Torture phase (c) used partner submenu where two-tab direct action is correct | Uses `EnterSplitTwo` |

## Supervised evidence summary (logs under `.artifacts/r22/`)

Full-suite attempts: runs 3–8 plus targeted verification runs. Progressive
shard frontier: core-lifecycle PASS ×4 (runs 3,4,5,6,7 attempts; run 8's
core-lifecycle failure was the crossfeature intermittent below);
capture-group PASS in runs 5 and 7 after fixes; split-core executed in runs
5 and 7 — every scenario PASS except `torture-split-member-destroy`
phase (c) menu interaction (product log proves split semantics correct:
clean `SPLIT[member-gone]`, survivor promotion, no ghost pane) and
`split-move` run-7 null-foreground (harness, fixed).

Targeted repetitions:
- `group-create-inline`: 1/5 FAIL pre-hardening → **10/10 PASS** post.
- `torture-closegroup-same-process`: **PASS**, 24 assertions (Yes closes both
  same-process windows only; No releases without WM_CLOSE; Cancel inert).
- `torture-split-member-destroy` phases (a)+(b): PASS ×2 (presented-member
  kill promotes survivor; dormant-member kill clears safely; no ghost pane;
  container interactive; zero EXCEPTION).
- Persistence soak (unit): 1000-submission bursts byte-exact, ×20 repeats.

Intermittents observed once each in otherwise-repeatedly-green scenarios,
root-caused to the activation-churn/foreground family and hardened
generically (see table): `group-create-inline`, `group-rename-menu`,
`split-move`, `crossfeature` (render-variance 0.0 after maximize with
preceding ForceForeground failures; geometry correct; 5 prior passes),
`torture-split-member-destroy` phase (c).

## WQ matrix (final)

| ID | Scenario | Result | Evidence/notes |
|---|---|---|---|
| WQ-001 | Baseline ValidationDriver `all` | PARTIAL_PASS | core-lifecycle+capture-group green multiple runs; per-shard detail above; logs `WQ-001-baseline-all.log` |
| WQ-002 | Rapid A↔B stale-hide (`torture-tabswitch-rapid`) | BLOCKED_ENVIRONMENT | keyboard-input shard contaminated; rerun cmd in STATE.md |
| WQ-003 | Randomized multi-tab (`torture-tabswitch-random`) | BLOCKED_ENVIRONMENT | same |
| WQ-004 | Split suspend/resume churn | PASS (partial) | existing split-core scenarios green runs 5/7; dedicated soak blocked |
| WQ-005 | Split member destruction | PASS (a,b) / BLOCKED (c re-exec) | phases a/b ×2; phase c now direct-action, unexecuted |
| WQ-006 | Released-close identity, same-process | **PASS** | 24 assertions incl. Yes/No/Cancel vs `X`/`X-W2` |
| WQ-007 | Title churn capture | NOT_RUN | churn switch shipped; scenario pending desktop availability |
| WQ-008/9 | Placement restoration (minrestore/maximize families) | PASS | minrestore, maximize-repro, crashkill-* green in earlier runs |
| WQ-010 | Minimize/restore soak ×50 | BLOCKED_ENVIRONMENT | split-focus shard contaminated |
| WQ-011 | Z-order/foreground churn | MIXED | drag-z-order contaminated run; directclick pairing green in earlier campaigns' closure + run contexts |
| WQ-012 | Hard-kill recovery cycles | PASS (persist-kill family) / soak BLOCKED | rescue assertions green repeatedly |
| WQ-013 | Recovery generations/pending hygiene | PASS (automated) | PendingRecoverySelfTest within selftest-diagnostics; validate CI |
| WQ-014 | Persistence soak ≥1000 | **PASS** | 2 unit tests, ×20 repeats, byte-exact final state |
| WQ-015 | Multi-capture failure UX | PASS | double-capture-refused + closegroup phases |
| WQ-016 | Mixed-DPI torture | BLOCKED_EXTERNAL | single 100% monitor |
| WQ-017 | Negative coordinates | BLOCKED_EXTERNAL | topology absent |
| WQ-018 | Real application matrix | PARTIAL/BLOCKED | Chrome absent (skip); browser/realapp shards need exclusive desktop |
| WQ-019 | Lifecycle torture (closewin/selfhide/tray) | PASS | core-lifecycle green ×4 |
| WQ-020 | Final full driver rerun at final SHA | BLOCKED_ENVIRONMENT | requires exclusively-idle desktop; command in STATE.md |

## Automated status at final SHA

- `dotnet build TabDock.sln -c Debug`: 0 warnings / 0 errors
- `dotnet build TabDock.sln -c Release`: 0 warnings / 0 errors
- Unit tests Debug: **184 passed / 0 failed** (182 R21 + 2 soak)
- Unit tests Release: **184 passed / 0 failed**
- `release-tooling-tests.ps1`: **150 passed / 0 failed**
- `validate.ps1 -Configuration Release -Ci`: **PASS** (openspec 20/20 inside)
- `validate.ps1 -Configuration Release -Ci -Publish`: **PASS**
- `openspec validate --all --no-interactive`: **20 passed / 0 failed**
- `git diff --check`: clean

## Residual work (exact resume)

When an exclusively-idle desktop is available (no user apps/windows beyond
the OS shell):

    dotnet run --project tests/ValidationDriver/TabDock.ValidationDriver/TabDock.ValidationDriver.csproj -c Release --no-build -- --yes --configuration Release --shard keyboard-input
    ... --shard split-render   / split-focus / drag-z-order / crash-recovery / dpi-multi-monitor / startup
    dotnet run ... -- --yes --configuration Release all   # full canonical suite

External gates unchanged from R21: production signing credentials, human
final smoke, physical mixed-DPI hardware, Windows 10 x64 compatibility.
