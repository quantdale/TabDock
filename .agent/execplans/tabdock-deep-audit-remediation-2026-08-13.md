# TabDock Deep Audit Remediation

**Status:** READY WITH EXTERNAL QUALIFICATION REMAINING
**Owner/session:** Codex remediation campaign
**Updated:** 2026-08-13

## Post-remediation review follow-up — active narrow pass

This follow-up is separate from the completed deep-audit remediation above.
Its starting canonical handoff was observed dynamically at
`c6b119a028cc83c3a955b15a488f609976404f6e`; the prior remediation baseline
for substantive code is `f1dc7ab3ac616d6f1517efafdced5eb6418d3462`.

Scope and decisions:

- R3: Git is authoritative for current refs/status. State records campaign
  facts and substantive baselines only; it never embeds the SHA of the commit
  containing itself, and it never requires a post-push state-only CI record.
- R4: production uses a two-tier gate. Hot layout/re-glue checks retain
  HWND/PID/thread/class, live object binding, and a reversible per-capture HWND
  token. Slow/destructive/delayed/foreground/recovery paths add executable
  path and native `GetProcessTimes` process-start identity. The token closes
  the same-process HWND-recycling case that PID/start checks cannot distinguish;
  recovery requires a nonzero journaled token, and journal cleanup also matches
  that identity tuple instead of HWND alone.
- R5: every production restore/show path uses the `ShowWindow` post-state;
  its BOOL is treated only as previous visibility. Hidden, minimized, visible,
  hide, release/show, and intentional-hide semantics have deterministic tests.
- R6/M8: `GetDpiForMonitor` is removed from production and driver code. A
  controlled PMv2 helper HWND on the target monitor is queried with
  `GetDpiForWindow`; the helper is destroyed and the thread context restored.
  The DPI probe/conversion seam is deterministic; physical mixed-DPI hardware
  remains external qualification.

The active implementation/specification checklist is
`openspec/changes/post-remediation-review-followup-2026-08-13/tasks.md`.
Final pushed SHA and push-triggered CI values belong in the session output,
not in a follow-up state-only commit.

### Local follow-up validation checkpoint

- Debug and Release solution builds and the Release ValidationDriver build pass
  with 0 warnings/errors; the final canonical Release gate also passes.
- `--selftest-diagnostics` passes 89 checks / 0 failures; geometry self-test
  passes.
- `.\scripts\validate.ps1 -Configuration Release -Ci -Publish` passes audited
  restore, no-vulnerability report, all Release builds, diagnostics/privacy,
  doctor, support-bundle inspection, 16/16 OpenSpec validation, and publish/
  version smoke.
- Physical mixed-DPI, supervised M2 session-ending, Chrome, and any
  foreground-activation-limited scenario remain external qualification items.

## Final Qualification / Integration Pass

### Final durable checkpoint push and CI verified (2026-08-13 22:18 +08:00)

- The durable push/CI record was committed as
  `a48121c7f91d3643096d1b9ec79da681af9633e8` (`docs: record pushed TabDock
  qualification state`) and pushed fast-forward from
  `15c340be52b8b240fb38b536c9c0f1ae028a1eff`. No force push or history rewrite
  occurred.
- After `git fetch origin`, local `HEAD`, `origin/main`, and GitHub
  `refs/heads/main` all matched `a48121c7f91d3643096d1b9ec79da681af9633e8`;
  `main` was clean.
- Push-triggered GitHub Actions `build` run 16 (`31708992953`) completed
  `success` for `a48121c7`. Its Windows job (`94476731303`) passed every
  step, including `scripts\\validate.ps1 -Configuration Release -Ci -Publish`.
  Run 15 for the preceding remediation/handoff SHA `15c340be` was also green.
  M6 is `RESOLVED`.
- The overall result remains **READY WITH EXTERNAL QUALIFICATION REMAINING**:
  M2 session-ending cancellation and M8 multi-monitor/mixed-DPI hardware
  remain environment-qualified items; Chrome-dependent coverage and the
  documented split-render foreground-activation limitation remain external
  desktop qualifications.

### Canonical push and hosted CI verified (2026-08-13 22:10 +08:00)

- After `git fetch origin`, canonical `origin/main` was still
  `d0cea29fd1b8b60008eb3d7021b3c6859951583a`; no remote integration was
  required. Local `main` was clean and contained exactly the expected three
  non-merge descendants, with no unrelated or unreviewed local commits.
- `git push origin main` completed as a fast-forward from `d0cea29` to
  `15c340be52b8b240fb38b536c9c0f1ae028a1eff`. No force push, force-with-lease,
  reset, or history rewrite was used.
- Post-push `git fetch origin` verified local `HEAD`, `origin/main`, and the
  public GitHub `refs/heads/main` all equal
  `15c340be52b8b240fb38b536c9c0f1ae028a1eff`; `git status` reports a clean
  worktree.
- GitHub Actions `build` workflow run 15 (`31708377398`), triggered by this
  push, completed `success` for this SHA. Its Windows job (`94474626224`) and
  every step passed, including
  `scripts\\validate.ps1 -Configuration Release -Ci -Publish`. M6 is now
  `RESOLVED`.
- The final result remains **READY WITH EXTERNAL QUALIFICATION REMAINING**:
  M2 session-ending cancellation and M8 multi-monitor/mixed-DPI hardware
  remain environment-qualified items; Chrome-dependent coverage and the
  documented split-render foreground-activation limitation remain external
  desktop qualifications.

### Durable record committed; clean handoff verified (2026-08-13 20:28 +08:00)

- The final qualification record was committed locally as
  `65002f6e4ee852e1c1f151d3e38e612e8b7bb973` (`docs: record final TabDock
  qualification state`) after committed-tree verification.
- The three local commits are `f1dc7ab` (complete remediation), `65002f6`
  (durable qualification record), and this final handoff-state record (use
  `git log` for its exact SHA). `main` is clean and ahead of unchanged
  `origin/main`; no push, PR, reset, stash, cleanup, or published-history
  rewrite was performed.
- Final clean check: no TabDock/ValidationDriver/GuineaPig processes, no
  repository-local generated artifacts, `state.json`/`.bak` unchanged, and
  `git diff --check` clean. The final result remains READY WITH EXTERNAL
  QUALIFICATION REMAINING with M2/M6/M8 and the documented Chrome and
  split-render desktop limitations.

### Final committed-tree verification and disposition (2026-08-13 20:26 +08:00)

- Committed remediation: `f1dc7ab3ac616d6f1517efafdced5eb6418d3462`,
  `fix: complete TabDock deep audit remediation`; starting HEAD was
  `d0cea29fd1b8b60008eb3d7021b3c6859951583a`; branch is `main`; `origin/main`
  remains the starting commit; no push was performed.
- Post-commit Release solution build passed with 0 warnings/errors; diagnostics
  self-test passed `84/0`; OpenSpec passed `15/15`; and
  `.\scripts\validate.ps1 -Configuration Release -Ci -Publish` passed audited
  restore/no-vulnerability report, all Release builds, geometry, diagnostics,
  version, doctor, actual support-bundle privacy, OpenSpec, and self-contained
  publish/version smoke. The committed SHA appeared in version output.
- Final local disposition is **READY WITH EXTERNAL QUALIFICATION REMAINING**.
  Resolved: H1, M1, M3, M4, M5, M7, M9, L1, L2, L3, L5, R1, R2. Disproved /
  not material: L4. Blocked environment: M2 and M8. Blocked external: M6.
  Chrome-dependent cases are unavailable because `chrome.exe` is not
  installed. Split-render has 12/13 available scenarios passing; the remaining
  scenario safely refuses input when Windows will not prove the exact
  container foreground. H1 is nevertheless resolved by `split-focus` 7/7 and
  dedicated focus/drag evidence.
- No test processes or repository-local generated artifacts remain. The final
  state record is the only pending local change and will receive a separate
  documentation/state commit after this checkpoint.

#### External qualification procedures

- M2: on a disposable/instrumented Release Windows desktop, capture two
  GuineaPig windows, create a split, initiate logoff/shutdown, cancel it from a
  separate controlled application, verify TabDock deliberately exits after
  releasing guests, verify guests are standalone/alive, relaunch TabDock,
  confirm no stale journal and coherent persisted groups, and confirm repeated
  session-ending callbacks cause no duplicate release/prompt. Do not perform
  this against a live user session without supervised control.
- M6: run the configured GitHub Actions workflow on the committed tree via an
  authorized push or equivalent hosted event; this session must not push, so
  hosted execution is not claimed.
- M8: repeat the native maximize/restore, minimize/maximize, move, split, and
  pane/work-area matrix on larger/smaller secondary monitors, negative
  coordinates, and 100/125/150/200% mixed-DPI displays. Current local doctor
  saw only primary `1920x1080`, work area `1920x1032`, 96x96 DPI / 100%.
- Chrome/split-render: install/use a disposable supported Chrome target for
  the Chrome scenarios; on a supervised interactive desktop rerun
  `split-third-tab-click-persists` after ensuring Windows can grant exact
  container foreground. The driver must continue to fail closed on refusal.

### Remediation commit created; post-commit verification pending (2026-08-13 20:21 +08:00)

- The complete verified remediation was committed locally on `main` as
  `f1dc7ab` (`fix: complete TabDock deep audit remediation`). Starting HEAD
  was `d0cea29fd1b8b60008eb3d7021b3c6859951583a`; `origin/main` remains at
  that starting commit and was not pushed.
- The tree is temporarily dirty only for this post-commit durable checkpoint.
  Pre-commit acceptance was green: Release builds, diagnostics `84/0`,
  geometry, driver help/list, audited canonical validation, actual support-ZIP
  privacy, OpenSpec `15/15`, publish smoke, state preservation, process
  hygiene, and artifact hygiene.
- Next action is canonical verification against `f1dc7ab`, followed by the
  final durable-state commit and clean-tree check.

### Pre-commit acceptance gate (2026-08-13 20:18 +08:00)

- Release solution, ValidationDriver, and GuineaPig builds passed with 0
  warnings/errors; geometry, diagnostics (`84/0`), `--help`, and `--list`
  passed, with 65 scenarios and 11 bounded orchestrated shards.
- `.\scripts\validate.ps1 -Configuration Release -Ci -Publish` passed audited
  restore, no vulnerable packages, Release builds, doctor, actual support ZIP
  privacy, OpenSpec (`15 passed / 0 failed`), and self-contained publish
  version smoke. A separately generated support ZIP had 9 entries and zero
  personal-path hits.
- OpenSpec status reports `deep-audit-remediation-2026-08-13` complete. No
  test processes or repository-local generated artifacts remain. The user's
  `state.json` and `.bak` were preserved with their recorded hashes and
  lengths. The privacy `api_key` normalization hardening is included.
- Acceptance gate is green for the local candidate. External qualification
  remains M2 supervised Windows shutdown/logoff cancellation, M6 hosted GitHub
  CI, M8 multi-monitor/mixed-DPI hardware, unavailable Chrome scenarios, and
  the split-render foreground-activation limitation.
- Next action is to stage this reconciled candidate, create the authorized
  local remediation commit, then rerun verification against the committed
  tree.

### Privacy hardening before final gate (2026-08-13 20:12 +08:00)

- The final source review found one narrow diagnostic privacy gap: JSON
  property matching recognized `apiKey` but not punctuation variants such as
  `api_key` or `api-key`.
- `DiagnosticEnvironmentService.IsSensitiveKey` now normalizes non-alphanumeric
  separators before matching, and `DiagnosticPrivacySelfTest` includes an
  adversarial `api_key` value. This is a fail-closed redaction strengthening;
  it does not alter JSON parseability.
- Branch is `main`; HEAD and `origin/main` remain
  `d0cea29fd1b8b60008eb3d7021b3c6859951583a`; the tree is dirty and no commit
  has been created. `git diff --check` passed with only expected line-ending
  warnings.
- Next validation must rebuild Release artifacts and rerun the privacy/self-
  test and canonical local acceptance gate before staging.

### Final bounded-shard qualification and safe input handoff (2026-08-13 20:04 +08:00)

- A fresh Release `hung-guest-mintrack` run passed the complete M9 scenario:
  resize remained bounded while GuineaPig blocked `WM_GETMINMAXINFO` for 800
  ms, containment settled, `SHEPHERD[sizemin]` was observed, the pig recorded
  the deliberate block, and no exception occurred. The prior isolated miss
  was timing-sensitive; no M9 source change was required.
- Release `drag-z-order` passed all available scenarios, including the repaired
  direct-click pairing, reorder, drag-out, immediate pop-out, and held-drag
  identity paths. `chrometabdrag` alone was unavailable because `chrome.exe`
  is not installed; the shard cleaned up and restored both user state files.
- Release `split-render` passed 12 of 13 scenarios. The only remaining case,
  `split-third-tab-click-persists`, intermittently loses Windows foreground
  activation during its Ctrl+Tab step. The driver now sends Ctrl+Tab only as a
  single real-input batch after proving the exact container HWND, PID, class,
  executable, and process-start identity is foreground; it refuses input when
  that proof fails. Focused runs passed many complete cycles but still ended
  with a safe refusal when Windows would not grant the container foreground.
  This is retained as an environment qualification limitation, not claimed as
  a product pass or solved by weakening identity checks.
- No tracked validation processes remain and `git diff --check` exits 0.

Next: finish the subsystem/source and CI workflow review, execute the complete
canonical local acceptance gate, then create the authorized local commit and
verify the committed tree. Do not redo the completed H1, split-focus/core,
M9, direct-click, drag, diagnostics, or lifecycle runs unless source review
finds a regression.

### Drag/z-order qualification and harness hardening (2026-08-13 19:21 +08:00)

- The focused direct-click scenario exposed a real pairing defect in the
  existing predicate: it accepted any visible window above the container,
  leaving an intentionally inserted unrelated window between the guest and
  container. `WindowShepherdService` now requires the first visible window
  above an ordinary guest to be that guest, while retaining the broad
  cross-band check for topmost guests and skipping invisible IME helpers.
  `directclick-foreground-pairing` passes foreground transfer, immediate
  pairing (221 ms), keyboard delivery, liveness, and no-exception checks.
- `dragreorder` initially failed only because post-reorder drag-out began
  after Windows foregrounded the guest; the driver now uses the existing
  point-validated `EnsureClickable` fallback before that real drag. The
  focused scenario passes reorder, bounded H2 churn, drag-out release,
  liveness, and orphan checks.
- `dragreorder-then-immediate-popout` initially failed on the same legitimate
  foreground-lock condition and on a stale expectation of a legacy top-level
  picker. It now uses point-validated tab clicks and checks the current inline
  `Add selected` surface; the focused scenario passes.
- `dragprobe` initially failed because a real held-button drag can foreground
  the registered guest while the gesture is in progress. The driver now
  permits only an exact registered-window/process-start transition while its
  own left button is held; all other identity checks remain fail-closed. The
  focused scenario passes reorder, drag-out, liveness, and cleanup.
- Builds after these changes: TabDock Release and ValidationDriver Release
  both pass with 0 warnings/errors. The tree remains dirty at baseline HEAD
  `d0cea29fd1b8b60008eb3d7021b3c6859951583a`; no commit has been created.
- Current open items are the known `split-render` desktop activation
  limitation, missing Chrome executable coverage, M2 supervised OS
  shutdown/logoff cancellation, hosted M6 CI, and M8 multi-monitor/DPI
  hardware. Next: rerun affected bounded shards and the complete canonical
  local gate, then finish diff review and commit.

### Lifecycle race and post-close input qualification (2026-08-13 18:43 +08:00)

- `core-lifecycle` exposed a reproducible `selfminhide` race: the guest
  correctly logged `WM_SHOWWINDOW 0`, but a concurrent relayout could re-show
  an iconic guest before the queued hide decision ran. The fix keeps iconic
  guests out of relayout, preserves native callback visibility for hide events,
  and adds a 250 ms lifecycle probe that routes a still-captured hidden guest
  through the existing fail-safe hidden-release path. The focused scenario now
  passes all assertions.
- `hotkey-afterclose` initially attempted to click the hidden launcher and was
  correctly rejected by the identity guard. The scenario now accepts the
  documented hidden-launcher state, refreshes the verified container target
  between picker cycles, and uses a point-validated real caption click only as
  the Windows foreground-lock fallback. Three hotkey cycles and inline picker
  activation pass.
- Builds after these changes: `dotnet build TabDock.sln -c Release --nologo`
  and ValidationDriver Release build both pass with 0 warnings/errors.
- Current open items are the prior split-render desktop activation limitation,
  supervised M2 shutdown/logoff behavior, hosted M6 CI, and M8 hardware/DPI.
  Next: remaining bounded shards, canonical validation, final diff review,
  local commits, and committed-tree verification.

### Split sub-shards and H1 completion (2026-08-13 18:08 +08:00)

- The oversized `split` family was decomposed into bounded `split-core` (10),
  `split-render` (13), and `split-focus` (7) shards. `split-core` passed all
  10 scenarios. `split-focus` passed all 7 scenarios, including the H1
  bidirectional focus, drag-release rendering, partner permutation, and
  maximize/restore cases. H1 is therefore RESOLVED with safe real-input
  evidence.
- `split-render` passed 12 scenarios. Its full run failed only at
  `split-third-tab-click-persists` when Windows refused to foreground the
  verified container before Ctrl+Tab at cycle 8. An isolated retry had passed
  earlier and a later isolated retry reproduced the same foreground refusal.
  The driver correctly refused keyboard input; no safety guard was weakened
  and no product assertion failed. Keep this as a supervised desktop
  activation limitation rather than claiming the shard green.
- After reviewing the H1 identity fix, `TryRefreshStableIdentity` now also
  compares the captured process-start timestamp when available, preserving
  recycled-PID protection during cleanup/native operations.
- No TabDock, ValidationDriver, or GuineaPig processes remained after the
  runs. Next: qualify M2/M8 safely, complete deterministic acceptance and
  full diff review, then commit only after the gate passes.

### M2/M8 qualification analysis (2026-08-13 18:12 +08:00)

- M2 source review confirms `Application_SessionEnding` is a one-way,
  idempotent lifecycle transition. It marks shutdown, saves/flushed the
  journal, releases guests, stops WinEvent dispatch/retry, normalizes
  container/group layout intent, saves again, and calls `Shutdown(0)`.
  `Application_Exit`, `WinEventMonitor.Stop`, and release paths tolerate the
  subsequent repeated cleanup. The policy self-test is included in the
  diagnostics self-test. No real logoff/shutdown was initiated because it
  could terminate the agent session or disrupt the user's desktop. M2 stays
  `BLOCKED_ENVIRONMENT`; the exact supervised procedure is documented in
  `docs/TESTING.md`.
- The current doctor report found one usable primary monitor at
  `0,0,1920x1080`, work area `0,0,1920x1032`, 96 DPI/100% scale. No secondary,
  negative-coordinate, or mixed-DPI matrix can be run here. Source confirms
  there is no primary-monitor WPF max-size clamp and `WM_GETMINMAXINFO` uses
  the containing monitor work area. M8 stays `BLOCKED_ENVIRONMENT`; run the
  deterministic geometry check and retain the external hardware matrix.
- The doctor output was written to the system temporary directory, outside
  the repository; no repository artifact was generated.

### Additional harness qualification fixes (2026-08-13 18:24 +08:00)

- The first complete `diagnostics` shard run exposed a harness target-transition
  defect in `hotkey-hold-single-picker`: after the first hotkey opened the
  legitimate TabDock picker, later simulated repeats still expected the old
  launcher. The identity guard correctly refused the mismatch. The scenario
  now re-verifies and targets the current visible registered TabDock picker for
  subsequent repeat taps.
- Two picker-owner scenarios expected a top-level legacy `Capture windows`
  window from the container `+` button, but current behavior intentionally uses
  an inline `Add selected` surface. They now verify the inline surface is
  attached to the requesting container, absent from the other container, and
  still works after launcher closure. Focused reruns of all three scenarios
  pass.
- The driver’s stable identity refresh also checks process-start identity on
  cleanup/native operations. No guard was weakened; the initial diagnostics
  shard must be rerun after these harness-only fixes.

Checkpoint timestamp: 2026-08-13 17:17:24 +08:00.

- Session start branch: `main`.
- Session start HEAD and `main`: `d0cea29fd1b8b60008eb3d7021b3c6859951583a`.
- `origin/main`: `d0cea29fd1b8b60008eb3d7021b3c6859951583a`.
- Working tree: dirty; 42 paths changed/untracked (35 tracked modifications,
  7 untracked), matching the expected uncommitted remediation shape. No
  destructive Git operation has been used.
- Reconciliation so far: durable campaign state, OpenSpec artifacts, Git
  baseline, and source diff agree that this is the prior remediation campaign,
  not a clean checkout or an unrelated change. The OpenSpec change reports
  18/20 tasks complete; tasks 1.4 and 5.3 are the final qualification/review
  work.
- Remaining qualification items at checkpoint: H1 focus/drag real-input
  identity rejection, M2 Windows session-ending cancellation, M6 hosted CI
  (cannot be run without pushing), and M8 multi-monitor/mixed-DPI hardware.
- Last validation before this pass: Release CI-equivalent script with publish,
  reported PASS, diagnostics `84/0`, OpenSpec `15/15`, and focused
  `split-resize` PASS after the initialized probe-buffer fix.
- Current open finding: no known implementation defect; H1 remains an
  unqualified supervised-input path pending root-cause investigation.
- Exact blocker: the ValidationDriver rejects the live target before input in
  focus/drag scenarios; M2/M8 require safe external desktop capabilities; M6
  requires a hosted run from committed code and this session must not push.
- Next three actions: (1) review the complete remediation diff and reproduce
  the H1 guard rejection, (2) complete safe M2/M8 environment analysis and
  hermetic revalidation, (3) commit only after acceptance, then re-run the
  committed-tree checks and update this plan/state with SHAs.
- Commits created in this session: none.
- A fresh session must not redo the prior audit or discard/recreate the
  uncommitted remediation; it should resume from this checkpoint and inspect
  current source/Git state before any edit.

### H1 guard investigation (2026-08-13 17:19 +08:00)

- Focused command: `dotnet run --project
  tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj
  -- --configuration Release --yes split-focus-bidirectional`.
- Launch identity evidence: TabDock PID `25512`, main HWND `0x281094`;
  GuineaPig PIDs `22860`/`3336`, HWNDs `0x1D10C6`/`0x1D10D8`; container
  `0x3210EA`, content host `0x241106`. The driver verified and spawned all
  targets from the Release artifacts recorded in the run output.
- Product evidence before the blocked action: split entry geometry and both
  visible panes passed. No product assertion ran for focus because the driver
  refused the first pane click at `(368,178)`.
- Root cause: `Input.VerifyPointTarget` treated the previous `_activeTarget`
  HWND as a required live anchor. The preceding real click targeted a transient
  WPF context-menu popup; after split entry the popup was gone and the next
  pane point was independently within the registered TabDock/GuineaPig scope,
  but the stale popup identity check failed first. The guard therefore rejected
  a safe target transition before input. The related foreground warning showed
  the container was foreground after popup dismissal, not an unverified user
  process.
- Safety conclusion: the fix must retain an independently re-read current
  point root plus registered process/executable/class identity checks. It must
  not permit an arbitrary unregistered root merely because the prior popup
  disappeared, and it must not remove HWND validation.
- Run result: `BLOCKED_ENVIRONMENT`/driver precondition failure; cleanup passed,
  and both user state snapshots were restored. No user application was
  targeted and no guard was weakened during reproduction.

### H1 guard fix and focused requalification (2026-08-13 17:23 +08:00)

- Qualification-infrastructure fix: `Discover.WindowIdentity` now carries an
  optional process-start timestamp; the driver records per-process executable
  and start identity, and `Input.IsScoped`/stable checks validate it when
  available. `VerifyPointTarget` still requires the current point root to be a
  live registered TabDock/guest/test-process identity, but no longer requires a
  destroyed transient popup to remain the previous live anchor.
- The OpenSpec test-tooling contract now explicitly covers safe transitions
  after transient popup destruction.
- Build: ValidationDriver Release, 0 warnings / 0 errors.
- Focused command rerun: `... --configuration Release --yes
  split-focus-bidirectional`.
- Result: PASS, four alternating right/left cycles; both panes remained visible
  and exact, the correct GuineaPig became foreground on every cycle, split
  stayed active, the composite tab count remained one, no exception log was
  observed, and cleanup restored both state snapshots with no guest windows.
- The prior driver block is resolved. Remaining H1 evidence is the drag-release
  scenario and adjacent/core split coverage; M8 hardware remains independent.

### New qualification-infrastructure finding: oversized split shard (2026-08-13 17:35 +08:00)

- Command: `... --configuration Release --yes --shard split`.
- The shard registered 29 scenarios and consumed its single 10-minute guarded
  driver budget. Twenty-seven scenarios completed with passing output; the
  final `split-maximize-restore-no-overlap` also passed, but the run then
  aborted before the already-focused `split-partner-permutation` could finish.
  The exit was the driver's bounded-budget code path, not a product assertion.
- No timeout was inflated and no process was left running. The shard is
  oversized for the stated qualification contract and must be decomposed into
  separately bounded named sub-shards (or an equivalent explicit grouping)
  before final CI/all qualification. This is a new finding, not an H1 product
  regression.
- Next action: make shard assignment and `all` orchestration enumerate bounded
  split sub-shards, retain the existing 10-minute/12-spawn limits, update help,
  docs, and OpenSpec coverage, then rerun each sub-shard.

## Objective

Complete an evidence-driven remediation and production-hardening pass for the
latest deep repository audit findings supplied in the campaign brief. Every
finding must be resolved with source and validation evidence, disproved with
conclusive evidence, or explicitly blocked/deferred with a durable reason.
Preserve the Shepherd architecture, add regression coverage for materially
testable defects, synchronize OpenSpec and documentation, and leave the
repository in an understandable state without pushing or rewriting unrelated
history.

## Baseline

- Starting branch: `main`
- Starting HEAD: `d0cea29fd1b8b60008eb3d7021b3c6859951583a`
- `origin/main` HEAD: `d0cea29fd1b8b60008eb3d7021b3c6859951583a`
- Starting worktree: clean (`git status --short --branch` reported only the
  branch tracking line)
- Environment: Windows 11 Home Single Language, build `10.0.26200`, x64;
  current identity `LAPTOP-8T16MFM8\\Michael Roy`; process is not elevated
- .NET: SDK `8.0.424`, runtime `8.0.30`, RID `win-x64`
- Repository shape: main WPF project plus Spike in the solution; ValidationDriver
  and GuineaPig are separate projects
- Existing active OpenSpec changes: `production-diagnostics-foundation`
  (complete) and `startup-group-visibility` (in progress); neither is assumed
  to represent this campaign. Historical state is preserved in `.agent/STATE.md`.
- Baseline validation was executed before implementation; exact outcomes and
  later qualification results are recorded in the Validation Ledger below.

## Ground Rules

- Confirm each finding against current source before editing; audit prose is not
  authority over current code.
- Preserve the Shepherd/no-reparent model, identity gating, no duplicate
  capture, fail-closed privilege boundaries, split invariants, and no global
  z-order damage.
- Do not weaken tests, paper over failures, increase timeouts without evidence,
  suppress diagnostics, or overwrite unreadable persistence data.
- Keep all P/Invoke declarations in `NativeMethods.cs`; use nullable annotations,
  explicit usings, file-scoped namespaces, and normal .NET naming.
- Durable recovery state must be written before dangerous guest mutation.
- Do not push, merge remote branches, rewrite unrelated history, or commit
  unless explicitly authorized by the user or repository workflow.
- Read `docs/TESTING.md` before supervised validation. Real-input runs are
  separately classified from hermetic CI-safe checks.
- Update this plan and `.agent/STATE.md` after reconnaissance, every finding
  confirmation, substantial implementation/validation batch, blockers, and
  before handoff.

## Findings Matrix

| ID | Original severity | Subsystem | Status | Confirmation status | Root cause | Planned fix | Files/symbols | Tests required | Validation status | Commit if applicable | Notes/blockers |
|---|---|---|---|---|---|---|---|---|---|---|---|
| H1 | HIGH | Native interop / split | VALIDATING | CONFIRMED | Current declaration returns `bool`, while Win32 returns an updated `HDWP` that may differ. The caller reused the original handle, called `EndDeferWindowPos` even after a middle failure, and used short-circuiting that hid which entry failed. | Changed the P/Invoke return to `IntPtr`; chain every nonzero handle; abandon without `EndDeferWindowPos` after any failed `DeferWindowPos`; retain the existing per-guest fallback and local z-order semantics. | `NativeMethods.cs`; `Services/DeferredWindowPositionBatch.cs`; `WindowShepherdService.PositionGuestsDeferred` | Native seam/changed-HDWP simulation; split creation/focus/move/resize/maximize/minimize/drag/odd-width/torture scenarios. | Deterministic seam PASS; build/self-tests PASS; core supervised split batch PASS; focus/drag input blocked by driver identity precondition | — | Microsoft Learn contract verified: updated handle must feed the next call/End; failed Defer means abandon and do not call End. |
| M1 | MEDIUM | Crash recovery / journal | VALIDATING | CONFIRMED | The journal only records inactive hidden members, while capture/positioning also mutates placement, visibility, z-order pairing, and DWM transition suppression. Rescue only shows/restores a visible state and lacks class/start-time/full-placement gating. | Replace the hidden-only entry with a versioned capture-session journal written before the first dangerous mutation; restore original placement/show state and DWM attribute with HWND+PID+exe+class+process-start identity checks; explicitly preserve intentional self-hide semantics and leave unrelated z-order untouched. | `WindowShepherdService`; `HiddenWindowEntry`; `CapturedWindow`; rescue paths; `PersistenceSelfTest` | Active/inactive/split/maximized/normal/minimized/rapid-switch/self-hide/drag kill/recycled HWND/partial restore. | v2 journal, durable write-through commit, identity/retry seam, storage gate, and deterministic self-tests PASS; supervised normal/rapid/self-hide/drag/max/min/split crash matrix PASS | — | Final release-order review and clean rerun remain; intentional self-hide uses a durable no-rescue marker. |
| M2 | MEDIUM | Session lifecycle | VALIDATING | CONFIRMED | `Application_SessionEnding` set the global shutdown flag, saved, released, stopped hooks, cleared model/container state, and returned without deliberately ending the process; a later OS cancellation could therefore leave a half-running app. | Adopt the explicit exit-after-teardown policy; make teardown idempotent, normalize state, then call `Shutdown` so cancellation by another application cannot return TabDock to an operational-looking but hookless state. | `App.Application_SessionEnding`; `GroupManager`; lifecycle flags; monitor | Deterministic lifecycle-policy test plus supervised OS cancellation checklist. | One-way teardown/idempotence helper and explicit `Shutdown(0)` implemented; deterministic self-test pending final build; supervised Windows cancellation remains external | — | Policy is explicit: cancellation does not resume TabDock. |
| M3 | MEDIUM | WinEvent lifecycle | VALIDATING | CONFIRMED | `WinEventMonitor.Start` could exhaust bounded native-install attempts and `App.SyncWinEventMonitor` could exhaust retry attempts while captured guests remained active; the only result was a log message. | Add injectable hook API/health result; block new capture while unhealthy, retry boundedly, and on permanent failure release and normalize all captured members, retain metadata, show a visible warning, and keep capture disabled until restart. | `WinEventMonitor`; `App.SyncWinEventMonitor`; `GroupManager`; `ContainerWindow`; `DiagnosticCommandLine` | Injected hook failure; guest destroy/hide/minimize/move/foreground/title/stale cleanup coverage. | Deterministic injected failure self-test PASS; post-admission rollback and full app failure-injection/supervised lifecycle validation pending | — | No ad-hoc high-frequency polling. |
| M4 | MEDIUM | Diagnostics / privacy | VALIDATING | CONFIRMED | `RedactPath` only replaced a string-prefix match, yet it was applied to whole timestamped lines and serialized report content; embedded, mixed-case, slash-variant, quoted, and error paths could survive into bundle entries. | Centralize path/token sanitization, apply it at every text/serialization boundary, and inspect actual ZIP entries plus adversarial fixtures. | `DiagnosticEnvironmentService`; `ReadSanitizedRecentLogText`; `RedactPath`; `DiagnosticReportService.ExportBundle`; `DiagnosticPrivacySelfTest` | Quoted/unquoted/mixed-case/slash paths, timestamps/tags, exe/error/JSON/doctor/env/log/bundle. | Adversarial fixtures plus actual ZIP entry inspection self-test PASS; Release script ZIP inspection PASS; final external audit pending | — | Structured JSON sanitization preserves parseability. |
| M5 | MEDIUM | Persistence / recovery | VALIDATING | CONFIRMED | `Save` wrote `.bak`, but `Load` returned an empty model after quarantining malformed primary without reading the backup; read/access failures were not distinguished through a structured load classification. | Classify primary as missing, corrupt, unsupported, or unreadable; quarantine/preserve only proven corrupt data; recover a valid backup only for missing/corrupt primary; preserve unreadable/future primary and block overwrite. | `PersistenceService.Save/Load`; quarantine/schema handling; `PersistenceSelfTest` | Malformed/null primary, valid/invalid backup, missing primary, access denied, future version, older valid primary, post-recovery save. | Hermetic matrix plus an injected UnauthorizedAccessException filesystem seam PASS; primary path uses `File.GetAttributes` so access-denied cannot become missing. A direct non-elevated NTFS deny fixture was rejected because Windows left the deny ACE effective after attempted restoration, so it was not retained in the self-test. | — | Directory-at-primary fixture and injected access-denied fixture both exercise unreadable/no-fallback protection. |
| M6 | MEDIUM | CI / qualification | VALIDATING | CONFIRMED | CI builds the solution and harness projects only; it does not run Release self-tests, persistence/privacy/native checks, OpenSpec, publish smoke, doctor/version/bundle checks, or dependency policy. | Add CI-safe Release qualification and make the validation script run the same hermetic checks; leave SendInput/hardware/session tests supervised and separate. | `.github/workflows/build.yml`; `scripts/validate.ps1`; self-tests | CI-safe full matrix and failure propagation. | Local `scripts\validate.ps1 -Configuration Release -Ci` PASS: audited restore, 0 build warnings/errors, 69-check self-test, doctor/version/bundle privacy, NuGet vulnerability report, OpenSpec 15/15; publish smoke pending | — | Do not put unsafe SendInput on hosted CI. |
| M7 | MEDIUM | Validation harness | VALIDATING | CONFIRMED | Driver paths are fixed to Debug and mixed RID conventions; `all` runs the entire growing `AllOrder` inside one 90-spawn/10-minute process budget, so comprehensive execution is structurally bounded out. | Add configuration/RID/executable arguments and deterministic artifact discovery; define named categories/shards and make `all` launch each bounded shard as a separate guarded process. | `tests/ValidationDriver` Program/Scenarios/GuardedProc; docs/scripts | Debug/Release, shard execution, bounded all orchestration, known-category checks. | Debug build and `--list` PASS with 65 registered scenarios assigned to 11 named shards; Release supervised execution and parent all orchestration pending | — | Preserve safety caps. |
| M8 | MEDIUM | Multi-monitor geometry | VALIDATING | CONFIRMED | `ContainerWindow.xaml` set `MaxWidth/MaxHeight` from `SystemParameters.MaximizedPrimaryScreenWidth/Height`, while its native `WM_GETMINMAXINFO` handler correctly uses the containing monitor's work area. WPF could therefore clamp a secondary-monitor maximize before the native monitor-specific result applied. | Remove the primary-monitor WPF max clamp and keep the monitor-specific native work-area contract as the sole maximize bound; add deterministic contract checks and supervised hardware classification. | `Views/ContainerWindow.xaml`; maximize/WM_GETMINMAXINFO | Deterministic checks plus real multi-monitor/DPI supervised matrix. | Primary-monitor clamp removed; geometry self-test and Release qualification PASS; real multi-monitor hardware matrix unavailable on the single-monitor desktop | — | Hardware qualification remains external. |
| M9 | MEDIUM | UI responsiveness / native | VALIDATING | CONFIRMED | `RefreshSizeConstraint` probes each dirty visible guest synchronously on the WPF dispatcher; each `SendMessageTimeout` permitted 500 ms, so a non-pumping split guest could block up to roughly 1 s during a dirty refresh. | Bound the native probe to a justified short limit, cache last-known results per captured identity, and use the cache on timeout/failure; add a deliberate non-pumping guest scenario/timing evidence. | `WindowShepherdService.GetEffectiveMinTrackSize`; `ContainerWindow.RefreshSizeConstraint`; GuineaPig | Non-pumping GuineaPig, timing/dispatcher responsiveness, split two-guest probes, resize correctness. | 100 ms bound/cache implemented; Release build and supervised `hung-guest-mintrack` passed with 10/11 ms resize response while guest blocked 800 ms | — | No unjustified timeout increase. |
| L1 | LOW | Startup/storage safety | VALIDATING | CONFIRMED | `LoggingService` and `PersistenceService` constructors called `Directory.CreateDirectory` without a degraded-mode boundary; startup retried the logger and capture had no durable-journal capability gate. | Make logging memory-only and persistence disabled when AppData is unavailable; show a clear warning and refuse capture before any guest mutation unless the durable recovery journal can be written. | `LoggingService`; `PersistenceService`; startup; journal | Denied/unavailable AppData; no unsafe hide/capture; in-memory logging/persistence behavior. | Memory-only logger, disabled persistence, journal-path safety gate, warning path, and deterministic storage fixtures PASS in the 77-check diagnostics self-test | — | Safety-critical journal cannot be best-effort. |
| L2 | LOW | Persistence schema | VALIDATING | CONFIRMED | `PersistedState.Version` defaulted to 1 but `PersistenceService.Load` never checked it, so future files could be accepted with silent field loss and later rewritten. | Move to an explicit current version with v1 migration, reject/preserve future versions, and emit meaningful diagnostics; version the recovery journal in the same campaign. | `PersistedState`; `PersistenceService.Load`; journal DTOs; `PersistenceSelfTest` | Current/old/future fixtures, no downgrade field loss, preservation. | State/journal v2 with v1 migration, future preservation, and Release diagnostics self-test PASS | — | Coordinated with M1/M5. |
| L3 | LOW | Dependency supply chain | VALIDATING | CONFIRMED | `TabDock.csproj` globally sets `<NuGetAudit>false</NuGetAudit>` because local network access was unavailable, making the suppression persist into future dependency/release contexts. | Keep reliable offline local builds but require explicit CI restore/audit properties and document the policy; verify the current zero-package surface. | `TabDock.csproj`; CI/docs | Online CI audit and offline/local behavior. | Release audited restore and `dotnet list TabDock.sln package --vulnerable --include-transitive` PASS with no vulnerable packages; local default remains offline-friendly and policy is documented | — | Hardening, not emergency. |
| L4 | LOW | Picker performance | DISPROVED | NOT_MATERIAL | `CapturePickerViewModel.Refresh` performs process-path lookup and first-time icon extraction inline for every candidate, but the current cache/cheap-first ordering keeps the synchronous work bounded. | Keep the simple synchronous path and retain measured timing logging; do not add async hydration complexity without a material stall. | `CapturePickerViewModel.Refresh`; `IconService` | Timing/profiling and disposed-picker/cancellation coverage if changed. | Supervised `capture-inline-ui` PASS; real desktop enumerated 467 windows/5 candidates in 12 ms, then 473/5 in 0 ms with cache. No material UI stall observed. | — | Reopen if a realistic stress run exceeds the documented bound. |
| L5 | LOW | Persistence durability | VALIDATING | CONFIRMED | All state changes used the same 1-second debounce; capture/release/group/active/rename/accent semantics were not uniformly durable immediately, while drag reorder is high-frequency and should remain coalesced. | Add an explicit durable-save path for discrete semantic mutations and a drag-end commit, retaining debounce for intermediate reorder/geometry churn. | `GroupManager.RequestSave`; `GroupViewModel`; `ContainerWindow.EndDrag`; persistence semantics | Kill at offsets after create/delete/capture/release/rename/accent/active/reorder and high-frequency drag. | Durable semantic-save path and drag-end commit implemented; Release persistence fixtures and supervised `persist-kill`/active-index evidence remain in final targeted batch | — | Coordinate with M1/M5/L2. |
| R1 | LOW | Group lifecycle / persistence / validation isolation | RESOLVED | CONFIRMED | Fresh empty group shells were durably saved and restored; picker refresh reset destinations to `<New group>` and all-fail picker attempts retained a provisional shell. The ValidationDriver also isolated `state.json` but left `state.json.bak`, so correct missing-primary recovery repopulated runs from stale materialized groups. | Add `Group.HasMaterializedTabs`; omit unmaterialized shells from saves; skip legacy zero-tab records on load; preserve picker destination across refresh; close/remove a picker-created shell when all captures fail; isolate and restore both primary and backup state files in the driver. | `Models/Group.cs`; `Services/PersistenceService`; `App.xaml.cs`; `ViewModels/CapturePickerViewModel.cs`; `Services/PersistenceSelfTest`; `tests/ValidationDriver/.../Scenarios.cs` | Save/load empty-group fixtures; selection-preservation fixture; populated restored-group preservation; Release launcher-empty-state/group-create/persist-kill scenarios. | Debug diagnostics `83/0`; Release `scripts\validate.ps1 -Configuration Release -Ci` PASS; Release `launcher-empty-state-hint` PASS with both state files isolated/restored; `group-create-inline` and `persist-kill` passed in the earlier mixed run (launcher failure was the stale-backup isolation defect). | — | User's AppData state was only inspected and preserved. The stale `.bak` observed during diagnosis was not deleted or rewritten. |

## Final Status Reconciliation

The detailed matrix above records the original confirmation and implementation
state. Its `VALIDATING` values are retained as historical phase evidence. The
following table is the authoritative final status after the validation ledger;
there are no remaining findings whose final status is `VALIDATING`.

| ID | Final status | Evidence / remaining qualification |
|---|---|---|
| H1 | RESOLVED | P/Invoke and HDWP chain fixed; deterministic seam and core Release split/move/resize/minimize/torture coverage pass. The bounded split-focus shard and dedicated focus/drag evidence pass without weakening the identity guard. |
| M1 | RESOLVED | Versioned full-state journal, identity gates, deterministic recovery fixtures, and Release active/rapid/self-hide/drag/maximized/minimized/split crash matrix pass. |
| M2 | BLOCKED_ENVIRONMENT | One-way idempotent teardown and explicit `Shutdown(0)` policy plus self-test pass; OS logoff/shutdown cancellation remains a supervised desktop check. |
| M3 | RESOLVED | Injected hook-install failure self-test, bounded retry, capture admission gate, and fail-closed release/normalization implementation pass source/self-test review. |
| M4 | RESOLVED | Adversarial sanitizer self-test and actual Release support ZIP entry inspection pass; no profile/AppData path or credential-like material leaked. |
| M5 | RESOLVED | Corrupt/missing/valid-backup/future/unreadable/injected-access-denied matrix passes; corrupt evidence and unreadable-primary no-overwrite rules hold. |
| M6 | RESOLVED | Push-triggered GitHub Actions `build` run 15 (`31708377398`) completed successfully for `15c340be`; the Windows job passed the Release qualification workflow, including audited restore, self-tests, privacy, OpenSpec, and publish smoke. |
| M7 | RESOLVED | Release/Debug/RID/path configuration, 65-scenario/11-shard coverage, bounded shard orchestration, help, and list validation pass. |
| M8 | BLOCKED_ENVIRONMENT | Primary-monitor clamp removed and containing-monitor deterministic contract passes; secondary/mixed-DPI hardware matrix is unavailable on this one-monitor desktop. |
| M9 | RESOLVED | 100ms bounded probe, identity-scoped cache, initialized probe buffer, poisoned-buffer self-test, and non-pumping 800ms guest run with ~9ms resize response pass. |
| L1 | RESOLVED | Memory-only logging, disabled persistence/capture safety gate, warning path, and unavailable-storage fixtures pass. |
| L2 | RESOLVED | State/journal schema v2, v1 migration, future-version preservation, and no-downgrade fixtures pass. |
| L3 | RESOLVED | Explicit CI NuGet audit policy and audited restore/vulnerability report pass with no vulnerable packages. |
| L4 | DISPROVED | Real picker measurement found 467 windows/5 candidates in 12ms and 0ms cached refresh with no material stall. |
| L5 | RESOLVED | Durable semantic saves, drag-end commit, persistence fixtures, and Release persistence/active-index kill scenarios pass. |
| R1 | RESOLVED | Empty shells are session-only; saves/load, picker failure cleanup/selection, and two-file driver isolation are fixed and qualified. |
| R2 | RESOLVED | Newly discovered uninitialized min-track probe buffer was fixed and `split-resize` rerun passed. |

## Architectural Decisions

1. The Shepherd model remains the only production capture architecture:
   captured guests stay independent top-level windows; no `SetParent`, `WS_CHILD`,
   `AttachThreadInput`, or global z-order strategy may be introduced.
2. H1 is the first code-changing priority and will be isolated from later
   native changes until targeted split validation passes.
3. M1, M5, L2, and L5 will be designed as one persistence/recovery contract;
   journal/state versioning and evidence preservation must not be implemented as
   incompatible isolated patches.
4. Supervised SendInput and hardware-dependent multi-monitor/session-cancel
   validation will be reported separately and never represented as green based
   only on compilation or source inspection.
5. Existing OpenSpec changes remain untouched unless this campaign explicitly
   updates their canonical specs. This campaign will use a dedicated OpenSpec
   change for behavior-level contracts where required.
6. Session-ending policy is explicit exit-after-teardown: once TabDock starts
   releasing guests for Windows shutdown/logoff, it normalizes its model and
   calls `Shutdown`; it does not attempt to resume after an external
   cancellation.
7. WinEvent health is a safety capability, not an optional optimization. A
   failed monitor blocks new capture immediately, retries only on the existing
   bounded dispatcher cadence, and permanently releases/normalizes guests if
   health cannot be restored. No polling fallback is introduced.
8. The recovery journal is a capture-session journal despite retaining the
   existing filename for compatibility. It is durable before the first guest
   mutation and remains until a release is durably complete; intentional
   self-hide is represented as a no-rescue transition before the hide.
9. Persistence uses explicit state version 2: version 1 is migrated in memory,
   future versions are preserved and never rewritten, and only a missing or
   proven-corrupt primary may fall back to a valid backup. Unreadable primary
   data remains fail-safe.
10. Native minimum probing remains synchronous only at dirty, discrete
    constraint refreshes; its bound is reduced and last-known identity-scoped
    values are retained on timeout. Container moves/resizes do not trigger a
    probe unless the constraint is dirty.
11. A freshly created group with neither live members nor persisted tab
    metadata is a session-only shell. Persistence must not accumulate or
    resurrect such shells; restored groups with persisted tab metadata remain
    valid layout intent until the user explicitly deletes them.

## Validation Ledger

- [PASS] Repository reconnaissance: `AGENTS.md`, `.agent/STATE.md`, agent
  workflows/templates, architecture/testing docs, audit records, OpenSpec
  tree, CI, and validation script read.
- [PASS] `git status --short --branch`: clean `main` tracking `origin/main`.
- [PASS] `git rev-parse HEAD`: `d0cea29fd1b8b60008eb3d7021b3c6859951583a`.
- [PASS] `git rev-parse --verify origin/main`: same starting HEAD.
- [PASS] `dotnet --info`: SDK `8.0.424`, Windows RID `win-x64`.
- [PASS] Environment probe: Windows 11 build `26200`, non-elevated.
- [PASS] H1 source/API confirmation: `BeginDeferWindowPos`/`DeferWindowPos`/
  `EndDeferWindowPos` occur only in `NativeMethods.cs` and
  `WindowShepherdService.PositionGuestsDeferred`; current `DeferWindowPos`
  return type is `bool`, the original HDWP is reused for all entries, and
  `EndDeferWindowPos` runs even after a failed entry.
- [PASS] H1 official contract confirmation: Microsoft Learn documents
  `DeferWindowPos` as returning `HDWP`, possibly a different handle that must
  be passed to the next call or `EndDeferWindowPos`, and says not to call End
  after a failed Defer. Sources: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-deferwindowpos
  and https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enddeferwindowpos.
- [PASS] H1 implementation seam: diagnostics self-test increased from 15 to
  17 checks and proves changed-handle chaining plus no End call after a failed
  middle Defer.
- [PASS] H1 build/static checks: `dotnet build TabDock.csproj --nologo`
  (0 warnings/0 errors), `--selftest-diagnostics` (17/17),
  `--selftest-geometry` (exit 0), and `git diff --check`.
- [PASS] H1 supervised `TabDock.ValidationDriver.exe --yes split-two-auto`:
  two guests entered split, exact left/right geometry and visibility held, and
  cleanup completed.
- [PASS] H1 supervised
  `--yes split-move split-resize split-minrestore split-repeat-cycles`:
  container move, maximize/restore resize, minimize/restore, and five split
  entry/exit cycles preserved the pane partition, visibility, tab identity, and
  cleanup invariants.
- [BLOCKED_ENVIRONMENT] H1 supervised
  `--yes split-focus-bidirectional`: the driver refused the first focus input
  at `(315,126)` after live-target identity verification failed; split entry
  geometry and cleanup passed, but focus behavior was not qualified.
- [BLOCKED_ENVIRONMENT] H1 supervised
  `--yes split-drag-release-render-stability`: split entry passed, then the
  driver refused the first pane focus probe at `(396,204)` after live-target
  identity verification failed; cleanup passed. This is a driver/session
  precondition failure, not a native assertion failure, and no retry or guard
  weakening was used.
- [PASS] H1 supervised
  `--yes split-move split-resize split-minrestore split-repeat-cycles
  split-drag-release-render-stability` process cleanup: no spawned guest
  top-level windows remained after the mixed pass/fail batch.
- [BLOCKED_ENVIRONMENT] `dotnet run ... --yes split-focus-bidirectional`
  stopped before the focus action because the ValidationDriver refused real
  input at `(315,126)` after its live-target identity verification failed;
  enter geometry and cleanup passed, but the scenario did not qualify native
  focus behavior. No test was weakened or retried blindly.
- [PASS] Baseline `dotnet build TabDock.csproj --nologo`: Debug build, 0
  warnings, 0 errors.
- [PASS] Baseline `dotnet build TabDock.sln --nologo`: TabDock + Spike, 0
  warnings, 0 errors.
- [PASS] Baseline `scripts\\validate.ps1`: Debug TabDock/Spike/
  ValidationDriver/GuineaPig builds, diagnostics self-test, and doctor smoke;
  exit 0.
- [PASS] Baseline `TabDock.exe --selftest-geometry`: exit 0.
- [PASS] Baseline `TabDock.exe --selftest-diagnostics`: 15 checks, 0 failures.
- [PASS] Baseline `openspec validate --all --no-interactive`: 14/14 passed.
- [PASS] Supervised `crashkill-split-rescue`: two captured guests entered an
  exact split, TabDock was force-killed, both guest processes survived, both
  received full-state rescue, both were visible, and cleanup left no spawned
  guest windows.
- [PASS] ValidationDriver registration hardening: after adding explicit
  Debug/Release/RID/path options and shard categorization, `--list` reports all
  65 AllOrder/extra dispatch scenarios assigned to 11 named shards; an initial
  uncategorized `tab-closebutton-popout`/`add-window-toggle` check failed closed
  and was corrected before the passing list run.
- [PASS] `scripts\validate.ps1` default Debug run: solution/app/Spike/
  ValidationDriver/GuineaPig builds, geometry, diagnostics/persistence/privacy
  self-tests, version, doctor, actual support ZIP inspection, and OpenSpec
  validation; exit 0.
- [PASS] `scripts\validate.ps1 -Configuration Release -Ci`: audited restores
  with vulnerability warnings treated as errors, Release builds with 0
  warnings/0 errors, geometry self-test, diagnostics self-test (69 checks/0
  failures), version/doctor, actual support ZIP privacy inspection, NuGet
  vulnerability report, and OpenSpec 15/15; exit 0.
- [PASS] Persistence durability hardening build: recovery-journal and state
  temp writes now use write-through streams plus `Flush(true)` before atomic
  replacement; `dotnet build TabDock.csproj --nologo` remains 0 warnings/0
  errors.
- [PASS] Storage-degraded hermetic fixtures: diagnostics self-test covers a
  file-at-log-directory logger fallback, file-at-state-parent persistence
  disablement, and recovery-journal path-as-directory capture safety; the
  bounded logger retains its memory line and no safety-critical write proceeds.
- [PASS] Supervised `capture-inline-ui`: picker/inline capture behavior passed
  and cleanup left no spawned guest windows. The application log measured
  `PICKER[refresh] windowsSeen=467 candidates=5 elapsedMs=12` and a subsequent
  cached refresh at `windowsSeen=473 candidates=5 elapsedMs=0`.
- [PASS] Persistence source hardening: `File.GetAttributes`-based primary and
  backup path classification now distinguishes missing, directory, file, and
  unreadable paths before backup fallback; this closes the `File.Exists` access
  denied -> missing ambiguity. Direct ACL fixture execution remains pending.
- [PASS] Persistence access-denied regression fixture: the deterministic
  filesystem seam throws `UnauthorizedAccessException` only for the primary;
  `Load` preserves the primary, refuses the valid backup, sets the fail-safe
  flag, and diagnostics self-test exits 0. A direct non-elevated NTFS ACL
  experiment was discarded because the deny ACE could not be reliably removed
  by the same token; no ACL-mutated fixture is retained.
- [PASS] Release CI-safe qualification after the access-denied fixture repair:
  `scripts\validate.ps1 -Configuration Release -Ci` completed audited restores,
  Release builds for solution/app/Spike/ValidationDriver/GuineaPig, geometry,
  diagnostics (77/0), version/doctor, support ZIP privacy inspection, NuGet
  vulnerability report, and OpenSpec 15/15.
- [PASS] Release publish qualification:
  `scripts\validate.ps1 -Configuration Release -Ci -Publish` completed the
  same matrix plus self-contained single-file publish and published
  `--version` smoke.
- [PASS] ValidationDriver Release contract: `TabDock.ValidationDriver.exe
  --help` and `--configuration Release --list` succeeded; all 65 AllOrder/extra
  scenarios were assigned to named shards. Supervised Release
  `--configuration Release --yes split-two-auto` passed with clean guest
  cleanup and explicit Release artifact paths.
- [PASS] Documentation synchronization: architecture, testing, README,
  internal agent guide, OpenSpec task status, and this campaign's recovery,
  session-ending, monitor-health, privacy, storage, and runner contracts were
  updated; stale fixed-Debug/all-budget/session-cancellation claims were
  removed.
- [CONFIRMED] User-reported zero-tab accumulation: `GroupManager.CreateGroup`
  immediately requested a save, `PersistenceService.Save` serialized fresh
  groups with no members or persisted tabs, and `RestoreGroups` reopened those
  records on every launch. `ShowCapturePickerCore` also left a newly created
  group alive when all selected captures failed. The existing
  `OnContainerClosed` cleanup only covered interactive container closure and
  did not protect the persistence path.
- [PASS] Empty-group regression fix: fresh shells are filtered from saves,
  legacy zero-tab records are skipped on load, picker destination selection is
  preserved across refresh, and a picker-created shell is discarded when every
  selected capture fails. `PersistenceSelfTest` now covers save filtering and
  legacy sibling preservation; diagnostics reports `83/0`.
- [CONFIRMED][FIXED] ValidationDriver isolation defect found while qualifying
  the user report: it deleted only `state.json` while leaving `state.json.bak`,
  allowing the deliberately correct missing-primary backup recovery to restore
  17 stale materialized groups. The driver now snapshots, clears, and restores
  both files; Release `launcher-empty-state-hint` passed with both snapshots.
- [PASS] Empty-group targeted supervised batch: `group-create-inline` passed
  its populated/new-shell invariants and `persist-kill` passed durable metadata
  survival; the mixed batch's `launcher-empty-state-hint` failure was the
  stale-backup isolation defect and was rerun successfully after the driver
  fix.
- [PASS] Release `launcher-empty-state-hint` after the backup-isolation fix:
  the driver snapshotted/cleared/restored both `state.json` (37 bytes) and
  `state.json.bak` (13708 bytes); the empty hint was found uniquely and visible,
  then disappeared after a populated capture.
- [BLOCKED_ENVIRONMENT] Release `group-create-inline` rerun after isolation:
  capture and existing-guest invariants passed, but the second-container menu
  action did not qualify because the supervised input guard observed the
  container was no longer the verified foreground target. This is the same
  live foreground/input precondition class as the earlier blocked focus/drag
  probes; no guard was weakened or blindly retried.
- [PASS] Release `persist-kill` after backup isolation: durable capture/rename,
  force-kill, relaunch, restored group, and persisted empty-runtime tab
  metadata all passed; both state files were restored afterward.
- [PASS] ValidationDriver state isolation build: Debug and Release driver
  builds passed after adding atomic write-ahead snapshots for both primary and
  backup state files. OpenSpec test-tooling contract now requires this.
- [PASS] Release persistence/recovery batch with corrected two-file isolation:
  `persist-kill`, `persist-active-tab-index`, `crashkill-rescue`,
  `crashkill-rapidswitch-rescue`, and `crashkill-selfhide-not-rescued` all
  passed; every scenario restored both user state files and left no spawned
  guest windows.
- [PASS] Release original-state recovery batch: `crashkill-during-active-drag`,
  `crashkill-maximized-recovery`, `crashkill-minimized-recovery`, and
  `crashkill-split-rescue` all passed, including full placement/show-state
  rescue for maximized/minimized guests and both split guests.
- [FAIL] Final Release split batch: `split-two-auto`, `split-move`,
  `split-minrestore`, `split-native-move-reassert`,
  `split-native-resize-reassert`, and `split-repeat-cycles` passed, but
  `split-resize` failed both post-maximize left/right guest geometry
  assertions. The failure is a current native/geometry blocker for H1/M8;
  no status is being marked resolved until the maximize path is reproduced and
  corrected or the assertion is disproven with source/runtime evidence.
- [CONFIRMED][FIXED] The split failure was caused by the M9 cross-process
  `WM_GETMINMAXINFO` probe allocating an uninitialized native buffer. A
  GuineaPig window left an indeterminate `ptMinTrackSize.y` value untouched;
  the value became a 65,535px minimum height and expanded the container past
  the work area. `WindowShepherdService` now initializes every field before
  `SendMessageTimeout`, and a poisoned-buffer self-test covers the contract.
- [PASS] Focused Release rerun after the probe fix: `split-resize` passed both
  maximize and restore geometry assertions. `split-maximize-restore-no-overlap`
  then passed its first maximize/restore/minimize transitions but stopped at a
  later maximize click when the ValidationDriver foreground identity guard
  rejected the live target; it is classified as supervised input blocked, not
  as a product failure.
- [PASS] Final Release CI-safe qualification after the min-track probe fix:
  `scripts\validate.ps1 -Configuration Release -Ci -Publish` completed
  audited restore, solution/app/Spike/ValidationDriver/GuineaPig Release
  builds with 0 warnings/errors, geometry, diagnostics `84/0`, version,
  doctor, support-bundle privacy inspection, OpenSpec `15/15`, and published
  single-file `--version` smoke.

## Current Checkpoint

- Current HEAD: `d0cea29fd1b8b60008eb3d7021b3c6859951583a` (uncommitted
  remediation changes are present in the working tree)
- Current branch: `main`
- Current phase: empty-group and min-track/maximize regressions are fixed;
  final matrix, static review, and honest external-qualification handoff remain.
- Findings resolved/disproved: M1, M3-M5, M7, M9, L1-L3, L5, and R1 are
  `RESOLVED`; L4 is `DISPROVED` / `NOT_MATERIAL` based on a real picker run
  (467 windows, 5 candidates, 12 ms first refresh; 0 ms cached refresh); R2
  is resolved by the poisoned-buffer self-test and focused split rerun.
- Findings blocked externally: H1 focus/drag input, M2 OS cancellation, M6
  hosted workflow execution, and M8 secondary/mixed-DPI hardware. The
  focused `split-resize` product regression is fixed and passes after the
  probe-buffer change. Full supervised shard coverage remains intentionally
  unclaimed.
- Files currently modified: source/services/models/views/view-models,
  ValidationDriver/GuineaPig, `.github/workflows/build.yml`,
  `scripts/validate.ps1`, synchronized docs, `.agent/STATE.md`,
  this execplan, and the dedicated OpenSpec change under
  `openspec/changes/deep-audit-remediation-2026-08-13/`.
- Exact last successful validation: `scripts\validate.ps1 -Configuration
  Release -Ci -Publish`; Release builds were warning/error-free, diagnostics
  was `84/0`, OpenSpec was `15/15`, privacy inspection passed, and published
  `--version` smoke passed. Focused Release `split-resize` also passed after
  the min-track probe fix.
- Exact latest failure/blocker: `split-maximize-restore-no-overlap` stopped at
  a later maximize click because the supervised foreground identity guard
  rejected the live target; this is an input-environment blocker. Remaining
  external blockers are the one-monitor M8 matrix, H1 focus/drag input, M2
  session cancellation, and hosted CI execution.
  Existing external blockers remain:
  one-monitor desktop for M8,
  ValidationDriver live-target identity rejection on H1 focus/drag probes, and
  no safe supervised Windows logoff-cancellation run yet.
- Next 3 concrete actions:
  1. Run the final static/repository-state review and verify no processes or
     user-state mutations remain from supervised runs.
  2. Update the final matrix/checkpoint and complete OpenSpec/docs consistency
     validation, including the new initialized-probe contract.
  3. Hand off the exact supervised follow-up checklist and report
     `READY WITH EXTERNAL QUALIFICATION REMAINING` without claiming blocked
     input, hardware, or hosted-CI cases as green.
- Important architectural constraints: Shepherd/no-reparent; identity-gated
  HWND operations; durable journal-before-hide; exact split partition and
  local z-order; unreadable persistence is not empty; no unsafe real-input CI.
- MUST NOT be forgotten after compaction: actual source/Git state supersedes
  stale historical state; do not mark a finding RESOLVED before validation; do
  not push or merge; keep this plan as campaign source of truth.

## Final Verification

- [PASS] Release audited qualification: `scripts\validate.ps1 -Configuration
  Release -Ci -Publish`; solution/application/Spike/
  ValidationDriver/GuineaPig builds completed with zero warnings/errors,
  diagnostics self-test reported `84/0`, audited NuGet report was clean,
  support-bundle privacy inspection passed, OpenSpec reported `15/15`, and
  self-contained publish `--version` smoke passed.
- [PASS] Debug application build: `dotnet build TabDock.csproj --nologo`.
- [PASS] Deterministic geometry, diagnostics, persistence/recovery, privacy,
  monitor-failure, session-ending, native HDWP, and min-track poisoned-buffer
  self-tests.
- [PASS] Release crash matrix: active, rapid-switch, self-hide, drag,
  maximized, minimized, and split rescue scenarios.
- [PASS] Release split regression rerun: `split-resize` passed after initializing
  the synthetic `WM_GETMINMAXINFO` probe buffer; split move/resize/minimize/
  5-cycle coverage also passed.
- [PASS] `openspec validate --all --no-interactive` and `git diff --check`.
- [PASS] Static Shepherd review: no production `SetParent`, `WS_CHILD`,
  `AttachThreadInput`, `HWND_BOTTOM`, or duplicate deferred-position loop was
  introduced; all deferred calls use the chained HDWP helper.
- [BLOCKED_ENVIRONMENT] H1 focus/drag-release input coverage and the later
  maximize steps of `split-maximize-restore-no-overlap` were stopped by the
  ValidationDriver live-target identity guard before the action; no guard was
  weakened.
- [BLOCKED_ENVIRONMENT] M2 Windows logoff/shutdown cancellation and M8
  secondary/mixed-DPI monitor hardware require a supervised desktop with those
  capabilities.
- [BLOCKED_EXTERNAL] M6 hosted GitHub workflow execution was not performed
  because this campaign did not push changes.
- [NOT_CLAIMED] The monolithic `all` run and the full real-application,
  browser, DPI, and hardware scenario inventory were not represented as green;
  use the named bounded shards and the follow-up checklist in `docs/TESTING.md`.
- [PASS] Final repository review: `git diff --check` returned exit code 0
  (only Git line-ending normalization warnings); production searches found no
  `SetParent`/`AttachThreadInput` call or `HWND_BOTTOM`; deferred positioning
  has one `Begin`/chained `Defer`/`End` implementation.
- [PASS] Release ValidationDriver `--help` and `--list` reflect the configured
  Release/RID discovery and 11 bounded shards; no TabDock, ValidationDriver, or
  GuineaPig process remained after validation.
- [PASS] User-state preservation check: `%APPDATA%\TabDock\state.json` remained
  37 bytes with SHA-256
  `ACB57C6A5FB9001C1EAC82738E9DB6D82A8EC6D7DE4849C1798DB08F8D078F45`, and
  `state.json.bak` remained 13,708 bytes with SHA-256
  `15DD48589157598CF791158D8045897F7DFB7CA2E43E3605EC75DED498117140`.

## Final Assessment

**READY WITH EXTERNAL QUALIFICATION REMAINING.** All confirmed findings have
an implementation and validation result, or an explicit environment/external
classification in the final matrix. The empty-group regression is resolved:
unmaterialized groups are session-only, picker destinations survive refresh,
all-failed provisional captures clean up their shell, and the ValidationDriver
isolates/restores both state snapshots. Hosted GitHub CI is green for the
canonical pushed SHA. No unresolved implementation defect is being marked
green. Remaining work is supervised session-ending cancellation, multi-monitor
qualification, unavailable Chrome coverage, and the documented split-render
desktop activation qualification; these cannot be completed safely or
honestly on the current desktop.

## Post-remediation safety closure

This section tracks the narrow follow-up against the verified pre-closure
baseline. Git remains authoritative for live refs and worktree state; the
embedded baseline is historical evidence only and is not a claim about the
commit containing this record.

### Initial reconciliation checkpoint

- Verified dynamically after `git fetch origin`: branch `main` was clean and
  local `HEAD` equaled `origin/main` at the pre-closure baseline
  `f4f953c962549726c8362b94e93538b9ea85b750`; no remote advancement or
  unrelated worktree changes were present.
- Active OpenSpec change
  `post-remediation-review-followup-2026-08-13` is planning-complete with
  its prior 11 tasks already checked off; this campaign extends the source
  safety boundary discovered in independent review rather than reopening the
  completed audit.
- R7 confirmed: the strong identity bool currently merges positive mismatch
  with probe unavailability, and `Release` clears the journal after refusing
  native work. `GroupManager` removes the logical member before that call.
- R8 confirmed from Git history: c6b119a/f1dc7ab v2 contains the full
  presentation/process-start journal but not thread or per-capture token;
  f4f953c adds those fields without bumping the journal schema.
- R9 confirmed: `RestoreSucceeded` checks only iconic/zoomed post-state and
  omits post-restore visibility. R10 remains operationally stale after the
  prior push. R11 requires current official action-runtime guidance review.
- R4 token ordering/lifetime and R6 PMv2 helper cleanup are under focused
  regression review while R7/R8 are implemented; no invariant is being
  intentionally redesigned.

### Safety-closure design checkpoint

- Identity checks will return explicit `Match`, `Mismatch`, or
  `Unverifiable`. Exceptions and unavailable executable/process-start/class
  probes are `Unverifiable`; positive identity differences and a gone HWND
  are `Mismatch`. Neither non-match permits native mutation.
- Release will return a structured outcome. It verifies before mutation,
  clears a journal only for a positive stale/recycled identity or a proven
  complete restoration, and retains both journal and logical member for an
  unverifiable/partial release so retry remains possible. Group collections
  will mutate only after a safe release outcome.
- Journal schema semantics will be explicit: historical v1 is the minimal
  no-version HWND/PID/executable journal; historical v2 is the full
  process-start/presentation journal from c6b119a; v3 is the current
  thread/token schema. Tokenless v1/v2 and malformed v3 entries will be
  preserved in named pending evidence without automatic native mutation or
  unbounded startup retries. Future versions remain untouched.

### Next three actions

1. Implement and self-test the tri-state identity/release transaction,
   including journal preservation and emergency/close-group behavior.
2. Implement v3 journal classification/migration with literal historical
   fixtures, then complete the ShowWindow post-state and R4/R6 reviews.
3. Run targeted and canonical gates, update the compact state protocol,
   commit once, push a normal fast-forward to `origin/main`, and verify the
   hosted workflow for the resulting SHA.

### R7 implementation and validation checkpoint

- Implemented `WindowIdentityResult.Match`, `.Mismatch`, and
  `.Unverifiable`; strong probe failures now remain uncertainty rather than
  becoming stale-identity proof. All callers still use the bool wrapper only
  where a mutation gate needs the `Match` result.
- `WindowShepherdService.Release` now verifies before mutation and returns a
  structured outcome. `RecoveryPending` performs no native mutation, clears no
  journal, and keeps the captured binding. Positive mismatch performs no native
  mutation, clears only the complete old journal identity tuple, and unbinds
  the old object. GroupManager mutates its member collection only after a safe
  release outcome; close/emergency paths continue cleaning independent members.
- Deterministic R7 coverage includes valid release, PID/thread/token/class/
  executable/process-start mismatches, unavailable/throwing strong probes,
  same-HWND replacement journal isolation, hidden/visible pending release,
  emergency continuation, and later retry. The diagnostic command currently
  passes 105 checks with 0 failures.

### R8 schema design checkpoint

- Historical source inspection confirms v1 was the original minimal
  no-explicit-version HWND/PID/executable journal, while c6b119a/f1dc7ab v2
  added class, process-start, presentation, transition, and no-rescue fields.
  The new thread ID and capture-generation token therefore require v3.
- The compatibility policy is conservative: v1/v2 tokenless evidence is
  classified as legacy/manual-recovery pending, written durably to a unique
  `.pending` sidecar before the active path is removed, and never auto-mutated.
  Incomplete v3 entries use the same explicit pending classification. Future
  v4+ data stays untouched. Current v3 rescue uses tri-state identity and
  retains unverifiable entries for bounded retry.
- Literal historical v1/v2 JSON fixtures and v3/future/malformed/mixed-entry
  cases are implemented; migration/recovery validation is the next checkpoint.

### R8 migration/recovery validation checkpoint

- Schema v3 is implemented with explicit v1/v2/v3 semantics. Legacy v1/v2
  bytes are durably moved to unique `hidden-windows.json.pending*` evidence
  for supervised recovery; incomplete v3 entries use the same path, future
  versions remain untouched, and current v3 uncertainty remains retryable.
- Literal fixtures cover historical v1, historical v2 without the new fields,
  valid v3, future v4, malformed JSON, missing-token v3, mixed v3 entries,
  positive token mismatch, unavailable process-start retry, and a legacy read
  that cannot be downgraded or overwritten by release cleanup.
- Exact validation: diagnostics self-test `109 checks / 0 failures`; Release
  solution build and ValidationDriver build both passed with zero warnings or
  errors; `openspec validate --all --no-interactive` passed `16/16`.
- R9 visible/non-iconic/non-zoomed restore semantics and deterministic failure
  fixtures are implemented. R10 now describes repository-content status and
  dynamic Git/CI handoff; R11 uses current stable Node-24 action runtimes while
  retaining the pinned OpenSpec Node >=20.19-compatible CLI runtime. Focused
  R4/R6 source review found no architecture regression.
- Next three actions: run the canonical `validate.ps1 -Configuration Release
  -Ci -Publish` gate and publish/self-test smokes; perform final diff,
  hygiene, and durable-state checkpoint; commit/push once and verify the exact
  hosted workflow run.

### R9 and durable-state checkpoint

- R9 is complete: `SW_RESTORE` success requires visible, non-iconic, and
  non-zoomed post-state; all restore/show/hide/release callers verify observed
  state, and deterministic fixtures cover benign false returns and genuine
  hidden/iconic/zoomed failures.
- The R7 boundary is complete across hide, release, foreground/bring-to-front,
  delayed restore, min-track, lifecycle, journal cleanup, group release, close,
  emergency release, session-ending normalization, and WinEvent teardown.
  Unverifiable hide now returns `RecoveryPending`; active-tab transitions roll
  back and split transitions retain their prior logical mode.
- R6/M8 review is complete: the PMv2 helper uses a predefined hidden STATIC
  popup with `WS_EX_NOACTIVATE`, overflow-safe monitor-center math, monitor
  identity verification, `GetDpiForWindow`, and `finally` cleanup. The native
  lifecycle seam covers context/helper cleanup paths; the available runtime
  smoke reports monitor DPI status `ok` at 96 DPI. No `GetDpiForMonitor` call,
  guest reparenting, `AttachThreadInput`, or `HWND_BOTTOM` regression exists.
- R10 is now future-proof: it records repository-content status and conditional
  Git/CI handoff rules, with no self-SHA or guaranteed-stale post-push command.
  R11 safely moved checkout/setup-dotnet/setup-node to current Node-24 action
  runtimes while retaining the pinned OpenSpec CLI's Node >=20.19 contract.
- Exact latest validation: canonical Release `validate.ps1 -Configuration
  Release -Ci -Publish` passed; diagnostics `111/0`; OpenSpec `16/16`;
  solution and ValidationDriver Release builds passed with zero warnings/errors;
  NuGet audit and support-bundle privacy passed; published version smoke passed.
- Next three actions (conditional on Git state): inspect complete diff and
  hygiene; record the final pre-commit checkpoint and mark the closure task
  complete; create/push one substantive commit only if content is uncommitted,
  then verify the exact hosted workflow run without a state-only follow-up.

### Pre-commit acceptance checkpoint

- R7 acceptance: explicit `Match`/`Mismatch`/`Unverifiable` outcomes are used;
  only `Match` permits native mutation; uncertain hide/release retains the
  captured member and durable journal; positive mismatch cleanup is scoped to
  the complete old identity tuple; deterministic release, emergency, and
  retry tests pass. Group close retains a pending member, continues releasing
  safe members, and does not activate the pending guest while reporting the
  incomplete transaction.
- R8 acceptance: current schema is v3; literal historical v1/v2 evidence is
  preserved as exact-byte pending manual-recovery data; incomplete v3 data is
  not silently consumed; future versions remain untouched; no legacy read can
  downgrade a write.
- R9 acceptance: restore success requires visible, non-iconic, and non-zoomed
  post-state; restore, split, and delayed-minimize paths use that contract;
  deterministic benign-return and genuine-failure fixtures pass.
- R10/R11 acceptance: `.agent/STATE.md` describes repository-content status
  and dynamic Git/CI handoff; the workflow uses current Node-24 action hosts
  while the pinned OpenSpec CLI remains Node >=20.19 compatible.
- R4/R6 acceptance: token ordering/lifetime, exact-token cleanup, hot/slow
  identity tiers, and PMv2 helper/context cleanup are covered; the available
  monitor smoke reports a nonzero 96-DPI result; no Shepherd or z-order
  architecture invariant regressed.
- Exact latest local validation after the final focused cleanup: Debug app
  build passed with 0 warnings/errors; diagnostics self-test passed `111/0`;
  the canonical Release/publish/NuGet/privacy/version/doctor gate passed;
  OpenSpec passed `16/16`; and `git diff --check` passed. No test processes
  remain and no generated artifacts or personal state are in the worktree.
- Repository-content status is complete. The only remaining handoff action is
  dynamic Git verification, one normal fast-forward substantive commit/push
  if the content is still uncommitted, and hosted CI inspection for that exact
  SHA. Do not create a state-only commit afterward.
- Next 3 concrete actions:
  1. Inspect the complete staged-equivalent diff and hygiene.
  2. Create the single coherent substantive commit and push `main` normally.
  3. Fetch refs and verify the exact hosted workflow run; fix only a real
     hosted defect, then stop committing once green.

## R12–R15 final source-hardening pass

### Implementation and focused validation checkpoint — 2026-08-14

- R12 implementation is complete. Capture now performs strong pre-token
  revalidation after durable `JournalCapture`, refuses to install `SetProp` on
  a changed PID/thread/class/executable/process instance, verifies the token,
  and performs a cheap generation gate immediately before DWM mutation. Hide
  performs the durable `JournalHide` write before a cheap gate and
  `SW_HIDE`. Release, rescue, restore, foreground, z-order, and deferred
  positioning paths use cheap generation gates before later distinct native
  writes; token removal is exact-token scoped. The final check-to-call Win32
  race remains explicitly documented as unavoidable.
- R12 deterministic coverage now includes capture journal-to-token races,
  token-to-DWM races, hide commit/boundary mismatch and uncertainty, release
  changes before placement/between placement and visibility/before DWM/before
  token removal, and rescue changes after placement/visibility/transitions.
  Hot-tier identity checks continue to perform zero process-start probes.
- R13 implementation is complete. `--pending-recovery` is read-only and
  reports sanitized stable session IDs, schema/entry counts, historical fields,
  and status. `--recover-pending` requires selecting one entry, selecting a
  live top-level candidate, matching all available historical fields, and
  typing exact `YES`. It refuses existing capture/recovery properties, installs
  a distinct ephemeral token, revalidates before each native mutation, uses
  visibility-only v1 semantics and full recorded v2 presentation, retains
  failed/unverifiable evidence, writes a durable resolution sidecar, and
  atomically retires only the resolved entry while preserving siblings/unknown
  JSON fields. If retirement is interrupted after the marker is durable, a
  later supervised invocation retries disk-only retirement without repeating
  native recovery. Doctor guidance and privacy tests are included.
- R14 implementation is complete. Source now classifies awareness with
  `GetAwarenessFromDpiAwarenessContext`, accepts known unaware/system/per-monitor
  contexts only with a valid target-monitor DPI, and fails closed for unknown
  awareness or unavailable monitor DPI. The authoritative docs/specs explain
  physical outer geometry, possible DWM content blur, 96-DPI min-track
  conversion, and external mixed-DPI hardware qualification. Deterministic DPI
  tests cover known unaware/aware acceptance, unknown/failure refusal, 500 at
  144 DPI, 96-DPI no-op, aware no-scaling, helper cleanup, and negative
  coordinates.
- R15 implementation is complete. The exact 1.8.0 tarball was inspected: its
  postinstall only prints an opt-in completion hint, skips in CI, and is not
  required for validation. CI now installs the pinned package with
  `npm install --global --ignore-scripts`; exact Node 24/npm 11 local install,
  `openspec --version`, and `validate --all --no-interactive` passed.
- Focused validation: Debug app build passed with 0 warnings/errors;
  diagnostics self-test passed `153 checks / 0 failures`; local OpenSpec passed
  `16/16`; and `git diff --check` passed. Full Release/ValidationDriver/
  publish/NuGet/privacy gate and hosted CI remain to be run for this dirty
  worktree.
- Canonical validation now passes on the staged tree: Release solution,
  ValidationDriver, GuineaPig, geometry, diagnostics (`153/0`), version/doctor,
  read-only pending-recovery discovery, support-bundle privacy, OpenSpec
  (`16/16`), NuGet audit, and self-contained publish/version smokes. The full
  staged diff and architecture/invariant review are complete; task 7.5 is
  marked complete in the substantive change record. Hosted CI remains to be
  inspected after the normal commit/push.
- Next three actions: create the substantive commit; push `main` fast-forward;
  inspect and, only if necessary, repair the exact hosted workflow run.

### Reconciliation checkpoint — 2026-08-14

- Dynamic Git reconciliation is clean: branch `main`, local `HEAD`, and
  `origin/main` all resolve to the supplied `3f2462f3dd62c86e46adef759d73ff5a15c9e348`
  baseline after `git fetch origin`; no unrelated worktree changes exist.
- R12 is confirmed in current source. Capture commits `JournalCapture`, then
  installs the per-HWND token and immediately proceeds to DWM without a strong
  pre-token recheck. Hide commits `JournalHide`, then calls `SW_HIDE` without a
  post-I/O generation gate. Release and v3 rescue perform several distinct
  native writes after one strong check without cheap generation revalidation
  between them. Positioning hot paths use the intended bounded HWND/PID/thread/
  class/token tier, but `PositionAndShow` must recheck before its post-restore
  position and before its subsequent z-order pin.
- R12 design: add a pre-token strong gate after capture journal commit; use a
  cheap binding/`IsWindow`/token/PID/thread/class gate immediately before each
  later distinct mutation; retain strong executable/process-start checks only at
  slow transaction entry and capture's post-journal gate. Token removal remains
  exact-token and generation-scoped. The final check-to-native-call interval is
  the unavoidable residual Win32 race, not an atomicity claim.
- R12 outcome policy: positive mismatch stops native work and may clear only the
  exact old journal identity; unverifiable evidence stops native work and keeps
  the journal/member retryable. Rescue uses the same distinction after partial
  placement; a replacement after an already-completed old-window mutation is
  classified as stale old evidence, never as permission to touch the replacement.
- R13 is confirmed: pending legacy sidecars are only counted and logged; no
  product operation discovers candidates, asks for confirmation, establishes a
  temporary token, or retires one entry. Historical source proves v1 only
  recorded HWND/PID/executable for a guest hidden by TabDock, so supervised v1
  recovery is visibility-only; v2 may restore its recorded presentation after
  explicit confirmation. The selected candidate must match every historical
  field available, then receive a distinct ephemeral recovery property token.
  Entry retirement will use durable sidecar resolution markers plus atomic
  version-aware removal of one JSON entry, preserving siblings and unknown
  fields; unresolved or failed entries remain pending.
- R14 is confirmed: current capture source and the archived DPI-acceptance
  decision intentionally accept known DPI-unaware guests and fail closed only
  for unknown/unreadable awareness or monitor DPI. The current README and the
  main `ui-ux-hardening` spec still contain the obsolete non-100% refusal claim.
  The authoritative contract will distinguish physical outer geometry,
  DWM-scaled/blurry content, centralized 96-DPI min-track conversion, and
  externally unqualified physical mixed-DPI hardware.
- R15 is confirmed from the exact `@fission-ai/openspec@1.8.0` tarball: its
  postinstall only prints an opt-in shell-completion hint, skips in CI, and
  never provides validation functionality. A local `--ignore-scripts` install
  ran `openspec --version` (`1.8.0`) and the full repository validation (`16/16`)
  successfully. CI will use the pinned package with lifecycle scripts disabled,
  without broad npm approval or stderr suppression.

### Implementation checkpoint

- Planned source seams: an injectable capture token/DWM adapter and journal/
  mutation sequencing hooks for deterministic race tests; shared cheap recovery
  generation evaluation; a supervised `--pending-recovery` read-only catalog and
  `--recover-pending` explicit console workflow; separate temporary recovery
  property name; durable pending-entry resolution markers; and diagnostics/privacy
  coverage for pending summaries.
- Planned OpenSpec updates: extend native identity, recovery compatibility, and
  monitor-DPI deltas with R12–R14 scenarios; add a CI-tooling delta for R15;
  synchronize affected main capabilities, including replacing the stale DPI
  acceptance requirement. Documentation updates cover README, architecture,
  testing, and doctor guidance.
- Validation status at this checkpoint: no R12–R15 repository edits yet; the
  historical baseline's last reported local gate remains diagnostics `111/0`,
  OpenSpec `16/16`, and canonical Release/publish/privacy/NuGet success. These
  are historical evidence only until the new changes are tested.
- Next 3 actions:
  1. Update active OpenSpec/state/docs with this transaction model, then
     implement capture/hide/release/rescue guards and deterministic race seams.
  2. Implement and test supervised pending recovery, then reconcile DPI docs/spec
     and the pinned npm lifecycle policy.
  3. Run targeted and canonical gates, review/commit/push `main`, and inspect the
     exact hosted workflow until the final SHA is green.

### Canonical local qualification / pre-commit checkpoint — 2026-08-14

- The final staged tree passed `dotnet build TabDock.sln -c Release --nologo`,
  the Release ValidationDriver build, and
  `.\scripts\validate.ps1 -Configuration Release -Ci -Publish`.
- The canonical script reported zero build warnings/errors, no vulnerable
  NuGet packages, diagnostics `153 checks / 0 failures`, pending-recovery
  discovery, support-bundle privacy, OpenSpec `16 passed / 0 failed`, and
  self-contained publish/version smokes all passed.
- The staged source review covered every changed file, destructive native call,
  identity outcome, pending-evidence lifecycle, DPI statement, CI install
  policy, architecture invariant, generated-artifact/secret scan, and
  `git diff --cached --check`. No real-input ValidationDriver scenario was run
  in this pass; unsafe or unavailable M2/M8/Chrome/foreground qualifications
  remain external items.
- Next three actions: create the substantive commit; push `main` normally and
  verify local/origin parity; inspect the exact hosted workflow run and repair
  only a real CI defect before stopping repository modifications.

## R16–R22 recovery/concurrency closure

### Reconciliation

The supplied review baseline was independently verified after `git fetch
origin`: branch `main`, local `HEAD`, and `origin/main` all resolve to
`f76e5ac332bef7c408254f3709eb35ef13856808`, with a clean worktree. The current
source confirms that supervised recovery installs
`TabDock.PendingRecoveryToken` from a process-local counter before any durable
recovery-transaction record, refuses every pre-existing recovery token, runs
outside the normal product mutex, defaults directly to `Console.In`/`Console.Out`,
and has no resumable native-progress marker. Current split positioning validates
both guests before constructing an HDWP but does not revalidate each guest at
its `DeferWindowPos` boundary. `DoNotRescue` is honored by automatic v3 rescue
but not by supervised v2 recovery. `JournalClear` is synchronous in production;
the canonical hidden-window-journal spec still contains the obsolete debounce
requirements. Console title output only normalizes CR/LF.

Historical v2 source at `c6b119a028cc83c3a955b15a488f609976404f6e` confirms that
`DoNotRescue` is written before the intentional `SW_HIDE` transition. Its rescue
branch performs `RestoreJournaledTransitions`, never shows or repositions the
guest, and only then consumes the journal marker. The current supervised v2
path will use that exact intentional-hide meaning.

### R16 transaction decision

The existing `<pending-file>.recovered` sidecar remains the single recovery
ledger. It will retain old `Resolutions` records and gain versioned transaction
records keyed by safe source-file ID + source SHA-256 + entry fingerprint.
Each transaction stores the selected HWND, PID, GUI thread, executable, class,
historical process-start identity when available, random nonzero recovery
token, recovery mode (`v1-visible`, `v2-presentation`, or
`v2-intentional-hide`), phase, and timestamps. Phases are `Prepared`,
`TokenInstalled`, `PlacementComplete`, `VisibilityComplete`,
`NativeRecoveryComplete`, `TokenRemoved`, and `Retired`.

The durable order is:

1. Acquire product mutation ownership and explicitly select/confirm entry and
   target.
2. Write `Prepared` with the complete ownership proof and cryptographically
   random token before `SetProp`.
3. Revalidate identity, install/verify the exact token, and record progress
   after each idempotent native presentation boundary.
4. Record `NativeRecoveryComplete` before removing the token. A hard kill at
   any earlier boundary leaves the token/evidence and permits explicit
   same-transaction resume; a hard kill after this marker skips presentation
   work and performs cleanup only.
5. Remove only the exact owned token, write the existing resolution marker, and
   atomically retire only the matching pending entry. Siblings and unknown JSON
   fields remain intact; every disk step is retryable.

Unknown recovery properties are never removed. A prepared transaction with no
property may be re-confirmed and restarted; an exact property with a matching
transaction is an interrupted transaction; a property without an exact durable
transaction is foreign/unverifiable and fails closed.

The deterministic resume matrix is:

| Durable phase | External token | Native state | Pending JSON | Next supervised behavior |
| --- | --- | --- | --- | --- |
| Before `Prepared` | none | unchanged | unresolved | normal explicit recovery may prepare a new transaction |
| `Prepared` | none | unchanged | unresolved | revalidate and reconfirm, then install the owned token |
| `Prepared`/`TokenInstalled` | exact or none | unchanged | unresolved | recognize only the exact transaction; resume after confirmation |
| `TokenInstalled` | exact | unchanged | unresolved | resume presentation from the first missing phase |
| `PlacementComplete` | exact | placement partial | unresolved | resume visibility/DWM without assuming retirement |
| `VisibilityComplete` | exact | visibility partial | unresolved | resume DWM, then durably mark native complete |
| `NativeRecoveryComplete` | exact | restored | unresolved | cleanup-only: remove the exact token, mark resolved, retire disk evidence |
| `NativeRecoveryComplete`/`TokenRemoved` | none | restored | unresolved | disk-only marker/retirement; never repeat native presentation |
| `Retired` | none | restored | absent or changed | no-op when exact; changed-source evidence is unverifiable and retained |
| foreign token | other | unknown | unresolved | refuse and leave the property and evidence untouched |
| changed target | exact/unknown | unknown | unresolved | refuse identity mutation and retain evidence |

### R17/R18 ownership and console decision

Normal TabDock and supervised recovery will share one reusable
`Global\\TabDock` product-mutation lease held for the entire process lifetime
or interactive recovery transaction. Read-only diagnostics do not acquire it.
The WinExe recovery command will use one scoped console session: redirected
stdio is used as-is; otherwise it attaches once to `ATTACH_PARENT_PROCESS`,
rebinds .NET streams, flushes prompts, and calls `FreeConsole` only when this
scope attached it. No console/stdio returns a clear nonzero error without
blocking. The Release validation script will launch the actual WinExe with
isolated `APPDATA`, redirected stdio, EOF input, and a no-pending state.

### R20 Win32 decision

Microsoft Learn's current [`BeginDeferWindowPos`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-begindeferwindowpos), [`DeferWindowPos`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-deferwindowpos), and
[`EndDeferWindowPos`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enddeferwindowpos)
`EndDeferWindowPos` documentation was reviewed. `BeginDeferWindowPos` allocates
an internal structure; `DeferWindowPos` only queues updates and returns the
next HDWP; if it returns `NULL`, the documented action is to abandon the
operation and not call `EndDeferWindowPos`; `EndDeferWindowPos` sends the
window-position messages and performs the commit. The documentation does not
define a cancellation/disposal API for a still-valid HDWP. Therefore the
implementation will not add a final validator followed by an unsupported
silent abandon. After `BeginDeferWindowPos` allocates the valid HDWP, it will
validate each guest immediately before its `DeferWindowPos`. If a later guest
fails validation, `EndDeferWindowPos` closes the valid batch containing only
the already-validated earlier entries, and the service will not run the
stale-guest fallback. A failed native `DeferWindowPos` still follows the
documented no-`End` path. The remaining race between the final check/call and
the compositor commit is ordinary Win32 behavior and is explicitly not claimed
to be atomic.

### R21/R22 decision

History shows the debounce wording came from the earlier performance audit;
the current synchronous implementation was deliberately introduced in
`f4f953c` as the crash-safety contract. `JournalHide`, intentional-hide writes,
and actual-entry `JournalClear` are synchronous; an empty clear returns before
scanning/writing, so normal frame positioning does not create per-frame disk
writes. The canonical spec will remove all pending-300ms requirements and
state this modern contract. Supervised titles will use a dedicated single-line,
bounded sanitizer that removes/neutralizes C0, DEL, C1, ESC, and Unicode line
separators while preserving ordinary Unicode; diagnostic/support privacy
redaction remains unchanged.

### Targeted validation plan

Add deterministic transaction fault points for every native/disk boundary,
foreign-token refusal, normal-capture refusal, random-token injection,
DoNotRescue true/false behavior, lease races, console stream/no-console
behavior, HDWP validation order, and terminal controls. Add a real WinExe
redirected-process smoke under isolated `APPDATA`, then run the canonical
Release/OpenSpec/publish/privacy/audit gate and review the complete diff.

### R16–R22 local implementation checkpoint — 2026-08-14

- Implemented the `.recovered` transaction ledger, cryptographically random
  nonzero token seam, phase-aware resume/cleanup, exact foreign-token refusal,
  normal-capture pending-token refusal, and deterministic fault/state tests.
- Unified normal startup and supervised recovery under `Global\\TabDock`,
  added abandoned-owner/exclusivity tests, and added one scoped WinExe console
  session with redirected process qualification under isolated `APPDATA`.
- Implemented historical v2 `DoNotRescue` intentional-hide cleanup, safe HDWP
  per-guest queue validation per the Microsoft contract above, terminal-safe
  local title sanitization, and canonical synchronous journal requirements.
- Local targeted diagnostics pass: `166 checks / 0 failures`; OpenSpec pass:
  `16 passed / 0 failed`. The Release `-Ci -Publish` gate passed including
  NuGet audit, support-bundle privacy, redirected recovery process smoke, and
  self-contained publish/version smoke. Hosted CI and Git refs remain dynamic;
  M2/M8/Chrome/foreground qualification remains external.

### Final production-readiness closure checkpoint — 2026-08-14

The final closure work is tracked in the focused OpenSpec change
`openspec/changes/final-production-readiness-closure-2026-08-14/`. It supersedes
the earlier R16 transaction decision where the source array was physically
rewritten on each retirement.

- Close-group Yes now snapshots a released-window target from the strong
  captured identity tuple before `GroupManager.CloseGroup` unregisters the
  live capture token. After release, an explicit Match/Destroyed/Replaced/
  Unverifiable verifier is the only gate before `WM_CLOSE`.
- Pending source files remain byte-for-byte immutable while any sibling is
  unresolved. The `.recovered` ledger carries logical resolution and the
  source is deleted only as an all-resolved unit. Old rewritten ledgers may
  rebind only when the current fingerprint is unique and one transaction is
  provably identified; the existing transaction record is updated even at
  `TokenRemoved`, so one recovery token cannot strand a parallel old binding.
  Duplicate ambiguity and foreign tokens remain retained fail-closed.
- Native-complete transactions use an explicit target classifier. Exact
  targets may lose only their exact recovery token; destroyed or positively
  replaced HWNDs receive disk-only cleanup; unavailable identity retains the
  evidence. No presentation mutation is repeated after the durable marker.
- Persistence now classifies `Version`/`Groups` case-insensitively and parses
  groups and nested tabs independently. Null/malformed records are logged by
  bounded index, valid siblings are salvaged, active indexes are clamped, and
  unreadable/corrupt/future overwrite protection remains unchanged.
- The mutation lease is `Global\\TabDock-<canonical Windows user SID>` and
  fails closed if the SID, protected DACL, or expected existing-object
  security cannot be proven. Stable Microsoft `System.Threading.AccessControl`
  `10.0.11` supplies `MutexAcl`; the protected DACL grants only the current
  SID `Synchronize | Modify | ReadPermissions`, with no broad principal or
  inherited grant. The `Global` namespace and abandoned-owner semantics remain
  unchanged.
- Supervised recovery uses one bounded `Rune`-based terminal display
  sanitizer for title, executable, class, candidate labels, filenames,
  statuses, and errors. README support guidance makes sanitized bundle/doctor
  output primary and labels raw logs sensitive. The HDWP implementation is
  unchanged; docs now state the ordinary Win32 check-to-commit race rather than
  claiming atomic identity.

Current repository-content evidence after this checkpoint: Release solution,
ValidationDriver, and GuineaPig builds; diagnostics self-test (`188/0`);
`scripts/validate.ps1 -Configuration Release -Ci -Publish`; OpenSpec
validation (`17/17`); NuGet vulnerability audit; support-bundle privacy;
publish/version smoke; and the focused TokenRemoved/ACL fixtures all pass.
The guarded `closegroupprompt` and `exitpopulated` attempts were blocked before
input by the verified-foreground preflight and cleaned up safely. Git refs and
hosted-CI results are dynamic session handoff evidence and are not recorded as
post-CI plan state; do not add a state-only commit for them.
