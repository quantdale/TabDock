# Release-Candidate Qualification Evidence Ledger — 2026-08-23/24

Candidate branch: `codex/ship-readiness-overhaul-20260823` (PR #12, draft, base `main`)
Final source SHA: see `.agent/STATE.md` CURRENT STATUS (resolved dynamically; the
canonical `scripts\validate.ps1 -Configuration Release -Ci -Publish` run below was
executed against this exact committed tree, and the binary's embedded
`informationalVersion` binds it).

Status vocabulary: PASS / FAIL_PRODUCT / FAIL_HARNESS / FLAKE /
BLOCKED_SUPERVISED / BLOCKED_ENVIRONMENT / BLOCKED_SIGNING / BLOCKED_STAGE_B /
SKIP_BROWSER_NOT_INSTALLED / NOT_APPLICABLE.

## Deterministic gates (final committed tree)

| Gate | Result |
| --- | --- |
| Debug build | PASS — 0 warnings / 0 errors |
| Debug unit suite | PASS — 675/675 (+4 LauncherStartupBindingTests) |
| Release build | PASS — 0 warnings / 0 errors (inside canonical validate) |
| Release unit suite | PASS — 675/675 (standalone + inside validate) |
| Release tooling suite (`release-tooling-tests.ps1`, pwsh required) | PASS — 150/150 |
| ValidationDriver deterministic self-tests (`--selftest all`) | PASS — 38/38 |
| OpenSpec strict validation | PASS — 30/30 (fixed pre-existing SHALL gap in release-engineering spec) |
| Native ABI self-test (exact published artifact) | PASS — Windows 11 build 26200, length-44 WINDOWPLACEMENT contract |
| Version/provenance smoke | PASS — informationalVersion binds final SHA |
| Doctor smoke | PASS — exit 0, no `%APPDATA%` mutation |
| Pending-recovery read-only discovery smoke | PASS |
| Supervised-recovery redirected-process lifecycle smoke | PASS |
| Support-bundle privacy inspection | PASS |
| Self-contained single-file publish + version smoke | PASS |
| `git diff --check` | PASS — clean |
| Hosted CI (`build`) on prior head db6d3e6 | PASS — run 32639177096 |

## Defects found by qualification (REPRO → root cause → fix → regression)

1. **PRODUCT Critical — launcher cold-start fatal crash** (first supervised run).
   Every launch without persisted state died in `Application_Startup`:
   `InvalidOperationException: A TwoWay or OneWayToSource binding cannot work on
   the read-only property 'Count'`. Root cause: `<Run Text="{Binding Groups.Count}">`
   in `MainWindow.xaml`; WPF defaults `Run.Text` bindings to TwoWay. Fix:
   explicit `Mode=OneWay` on both Run bindings. Regression:
   `LauncherStartupBindingTests` (TwoWay-default metadata pin; STA attach reproduces
   the throw; OneWay attaches/renders/tracks; source contract forbids unmoded
   data-bound Runs in launcher XAML). Commit d60bf7b.
2. **HARNESS — three stale UI contracts vs redesigned UI** (commit 60d01f3):
   capture-order assumption in global-tab-navigation (now derives live strip
   order), two-tab split action used in a three-tab group (now partner submenu),
   `'Add window to group'` lookup (now AutomationId `AddWindowButton`),
   ancestor-checkbox row resolution (now sibling checkbox `"Select <title>"`),
   `'Exit'` button lookup (now `'Exit TabDock'` with fresh-rect clicking),
   empty-state hint `'No groups yet'` (now `'Create your first workspace'`),
   missing parent bounding-rect wait in `ClickTabSubmenuItem`.
3. **HARNESS — environment-provenance gaps**: Windows 11 Notepad single-instance
   broker hands the spawned temp file to an already-running process; the driver
   now ADOPTS such windows (full stable identity pinned, re-verified per input,
   process never tracked/killed). Standalone Chrome scenarios now emit
   SKIP_BROWSER_NOT_INSTALLED instead of Process.Start Win32Exception.
4. **PRODUCT High — ordinary tab switches left OS foreground on container chrome**
   (torture-tabswitch-rapid/random: zero `SHEPHERD[bring-to-front]` across every
   switch while the container stayed active; foreground assertions failed each
   cycle). Split-member switches already granted foreground; ordinary switches
   relied solely on WM_ACTIVATE, which never fires when the container is already
   active. Fix: ordinary switches grant `_shepherd.SetForeground(newWindow)` when
   the container holds foreground and no chrome interaction is active (mirrors
   the documented reassert/suppression contract).

## Supervised physical results (Release artifacts, guarded SendInput)

Three RC-targeted scenarios: **global-tab-navigation PASS (24/24 checks)**,
**split-affordance PASS**, **capture-admission-blocked BLOCKED_ENVIRONMENT**
(journal failure not safely inducible; exact rerun command in docs/TESTING.md).

Broad hermetic suite (11 shards): every shard executed at least twice.
Best-of-N: **dpi-multi-monitor PASS (all scenarios)**; split-core **PASS (11/11)**
on rerun; crash-recovery 7/8; capture-group 13/15; split-focus 5/9;
split-render 12/18; drag-z-order 5/9; keyboard-input 4/15; startup 1/3;
diagnostics 5/8 (plus 6 Chrome scenarios correctly SKIPped). Persistent
non-passes are concentrated where an unregistered foreign window (the operator's
Windows Terminal, repeatedly maximized fullscreen mid-run) held/covers the
foreground — identity-failure-NNNN.json artifacts in
`%TEMP%\TabDock-Validation\runs\<runId>\` prove fail-closed refusals
(`process-not-registered`, `point-obscured-by-unrelated-window`). Per mission
§3 these are classified **BLOCKED_ENVIRONMENT/FLAKE**, not product failures:
identical binaries passed the same scenarios in other runs on this desktop
(e.g. selfhide, hotkey-afterclose, exitpopulated, rename-edge-cases each passed
after earlier interference aborts; directclick-foreground-pairing,
group-dropdown-stability, split-native-move-reassert passed on rerun).
Remaining genuinely-unclassified repeats (dragreorder H2 flip-back count,
split-drag-release zero-delta polylines, inline-capture second-tab asserts)
require an exclusive-desktop rerun on the final SHA before they may be called
PASS or product defects; commands are unchanged.

Real-application interop executed: GuineaPig (deterministic stand-in),
Windows Terminal via maximize-repro family (pig shard coverage), Edge present;
Chrome absent → SKIP. Notepad interop blocked by the broker-adoption gap during
these runs (fix landed; needs exclusive-desktop rerun).

## External gates

- Multi-monitor / mixed-DPI topology: **BLOCKED_ENVIRONMENT** — single 1920×1080
  monitor, 100% scale (ENV logs: monitorCount=1 dpi=96). Headless geometry/DPI
  suites green; physical matrix impossible on this hardware.
- Production signing: **BLOCKED_SIGNING** — no DigiCert STM (CLOUD_HSM)
  credentials configured; policy forbids calling local-pfx production signing.
  Artifact remains unsigned-qualified (`signingStatus=NOT_CONFIGURED`,
  `productionReleaseEligibility=BLOCKED_EXTERNAL` in release-manifest.json).
- Stage-B independent machine: **BLOCKED_STAGE_B** — no second Windows
  machine/VM available to the session.
- Destructive logoff/shutdown cancellation test: NOT_APPLICABLE here (requires
  disposable instrumented supervised host per docs/TESTING.md §B.6).

## Candidate artifact

- Source: final commit on this branch (see STATE.md).
- Framework-dependent-style build output:
  `bin\Release\net8.0-windows\win-x64\TabDock.exe` (self-contained deployment
  reported by --version); sha256 recorded by the final validate run output.
- Qualified RC bundle: `artifacts\rc-candidate\TabDock.exe` +
  `release-manifest.json` + `SHA256SUMS.txt` (sha256
  `66d243bdae0de47a51300b3b12d0009a9997c5351631ec3d0cd1f074854f7a4e`,
  QUALIFICATION_ONLY, produced at intermediate SHA 56cc217 before later defect
  fixes; regenerate with `scripts\release-qualify.ps1` on the final SHA).

## Conclusion

Deterministic gates: fully green. Supervised native qualification: partially
executed with two reproduced-and-fixed product defects and one critical
cold-start crash fixed; completion requires an exclusively available desktop.
Signing, Stage-B, and mixed-DPI topology unavailable. PR #12 therefore REMAINS
DRAFT; classification recorded in the final handoff.

## Native interaction determinism campaign delta — 2026-08-24

The prior entries above are historical PR #12 evidence and retain their
original vocabulary (`FLAKE`, `SKIP_BROWSER_NOT_INSTALLED`, signing/Stage-B
labels). New campaign artifacts use the canonical eight-way contract:
`PASS`, `FAIL_PRODUCT`, `FAIL_HARNESS`, `BLOCKED_ENVIRONMENT`,
`BLOCKED_SUPERVISED`, `BLOCKED_CAPABILITY`, `SKIP_CAPABILITY`, and
`FLAKE_UNCLASSIFIED`.

The campaign branch is stacked from the exact PR #12 head and does not alter
PR #12. It adds a pre-launch capability matrix, a fail-closed
`DesktopQualificationLease`, explicit run ownership categories, bounded
privacy-safe timelines, a root `run-manifest.json`, replay seams/fixtures, and
deterministic rerun aggregation. A valid first-attempt failure followed by a
pass remains `FLAKE_UNCLASSIFIED`; a blocked first attempt followed by a pass
remains blocked.

Final deterministic campaign gate (Release implementation checkpoint):

| Gate | Result |
| --- | --- |
| ValidationDriver native-free self-tests | PASS — 96/96 |
| WinEvent routing/replay focused unit tests | PASS — 13/13 |
| Debug unit suite | PASS — 686/686 |
| Release unit suite | PASS — 686/686 |
| Release tooling suite | PASS — 150/150 |
| Strict OpenSpec validation | PASS — 31/31 |
| Canonical Release CI/publish validation | PASS — native ABI, version, privacy, recovery, publish smoke |
| `git diff --check` | PASS |
| ValidationDriver Release build | PASS — 0 warnings / 0 errors |
| Physical H2 drag, split zero-delta, inline second-tab repeats | BLOCKED_SUPERVISED / BLOCKED_ENVIRONMENT — no exclusive safe desktop |

The WinEvent measurement storm recorded 30 callbacks, 20 callback membership
probes, 20 dispatch revalidations, 20 posts/lifecycle callbacks, at least 10
irrelevant rejections, and zero stale dispatches. The dispatch revalidation was
retained because it is the HWND-generation safety proof; no speculative cache
optimization was accepted.

## Final independent convergence delta — 2026-08-24

The integrated release-closure lineage received one harness-only fix after the
earlier candidate evidence: `Input.ForceForeground` previously required the
requested target to be foreground before attempting the switch. It now proves
an allowed, identity-current source foreground first and re-proves the exact
requested target after the bounded foreground operation. This does not weaken
foreign-window or HWND/PID/process-instance provenance checks.

Deterministic evidence after the fix: ValidationDriver `--selftest all`
127/127; Debug and Release unit suites 686/686; release-tooling 177/177;
strict OpenSpec 34/34; canonical Release validation/publish PASS. A fresh
exact-SHA candidate and qualification bundle must be regenerated after the
fix commit; the prior candidate remains historical diagnostic evidence only.

The three physical repeats remain safe-session blocked rather than product
PASS: `dragreorder` H2 flip-back count, `split-drag-release` zero-delta
polyline, and `capture-inline-ui` second-tab assertion. Their current run
classification is `BLOCKED_SUPERVISED`/`BLOCKED_ENVIRONMENT`; the
product-versus-harness scenario verdict still requires an authoritative exact
candidate run under an exclusive supervised desktop.
