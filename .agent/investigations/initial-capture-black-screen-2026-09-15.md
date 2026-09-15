# Initial-capture black-screen investigation

Date: 2026-09-15
Repository: `D:/Documents/tryPython/TabDock`

This is the preservation inventory captured before any branch/PR cleanup or
production change for the initial-capture presentation report.

## Git baseline

| Item | Value |
| --- | --- |
| Current branch | `main` |
| Local `main` HEAD | `586870739e8cfe8998738a1f492004b568afec21` |
| `origin/main` | `586870739e8cfe8998738a1f492004b568afec21` |
| Working tree | clean before this inventory file |
| Primary worktree | `D:/Documents/tryPython/TabDock` |
| Secondary worktrees | none |
| Local branches | `main` only |
| Stashes | `stash@{0}` on `fix/remove-window-subclass-pinvoke-signature`: `AGENTS.md doc update for shepherd mode` |
| Untracked files | none before this inventory file |

## Remote branch preservation table

| Remote branch | Tip | Commits not reachable from `origin/main` at baseline | Initial disposition |
| --- | --- | --- | --- |
| `origin/fix/initial-capture-black-presentation` | `5661fce353b54bbc3ee21120ac19fd50aa592cb3` | `5270169` fix, `5661fce` test | Preserve for independent review; neither accepted nor rejected yet |
| `origin/fix/initial-capture-presentation` | `4157f1c40c2607eaed7e090bf0c812da3924bf5c` | `88af039` fix, `af6674d` test, `7d10bfe` noop, `4157f1f` scratch-file removal | Preserve for independent review; neither accepted nor rejected yet |
| `origin/campaign/runtime-perf-and-coverage-2026-09-12` | `0178390b77fa1089bfcdd5061a2d16e2bf56938a` | none — tip is reachable from `origin/main` through merge `136f91c` | Confirmed integrated; retain only until final remote cleanup |
| `origin/ui/refero-frontend-overhaul` | `105cb80cd129151a78c64231884f5e0b9507a35b` | none — tip is reachable from `origin/main` first-parent history | Confirmed integrated; retain only until final remote cleanup |

The remote symbolic `origin/HEAD` points to `origin/main`; it is not an
additional branch. There were no local topic branches, no extra worktrees, and
no unpushed local commits at the baseline. Open PRs at baseline were #16
(draft, `fix/initial-capture-black-presentation`) and #15
(`fix/initial-capture-presentation`), both targeting `main`. Recent relevant
merged PRs #13 and #14 target the campaign/UI branches and were already
represented by the integrated mainline history; they remain part of the review
record but are not cleanup candidates requiring re-integration.

## Safety rule

No remote or local ref is to be deleted until every unique commit above and the
stash has been classified as integrated, intentionally rejected with a durable
reason, or explicitly preserved elsewhere.

## Independent lifecycle evidence

The production path is:

1. `App.ShowCapturePickerCore` creates a new `Group`, calls `OpenContainer`,
   and returns from `Window.Show()` before it enters the selected-target capture
   loop.
2. `ContainerWindow.Loaded` has already created/cached the container and marker
   HWNDs by that return, so a missing HWND is not the explanation.
3. Each `GroupViewModel.AddCapturedWindow` adds the tab and synchronously raises
   `ActiveTab`; `SyncShepherdActiveWindow` can therefore issue the first
   `PositionAndShow` before WPF raises `ContentRendered`.
4. Current `ContainerWindow` has no first-render handler. Its `LayoutUpdated`
   handler is attached from `Loaded` and suppresses a valid, unchanged content
   rectangle, so it does not provide a guaranteed native pass after first
   composition.
5. Single presentation's redundant-glue guard accepts matching geometry only
   after checking z-order; split presentation instead runs the explicit deferred
   guest/guest/container transaction. That explains why split entry repairs a
   stale single-guest native stack and why leaving split remains healthy.

A disposable WPF + `HwndHost` probe on this Windows host observed:

```text
BeforeShow -> SourceInitialized -> Loaded -> AfterShow -> AfterPosition
-> ContentRendered
```

The guest and container HWNDs were both non-zero at `Loaded`/`AfterShow`.
When the pre-render pair was observed at `ContentRendered`, the container was
above the guest in one run; attaching `LayoutUpdated` only from `Loaded` yielded
no intervening layout callback. This establishes the relevant race boundary,
while the nondeterminism of a minimal probe is why the production fix is a
one-shot lifecycle reconciliation rather than an application-specific or timed
repair.

The leading cause is therefore a stale native presentation state created by
performing the first Shepherd glue before WPF's first rendered composition and
never requiring a post-`ContentRendered` reconciliation. Pure missing-HWND,
empty-state popup, and application-specific explanations do not fit the
observed ordering or the shared capture path. The existing `PositionAndShow`
transaction is also shared with ordinary later presentation, while split's
explicit batch is the distinguishing repair operation.

## Existing work review

- PR #15 / `fix/initial-capture-presentation`: its admission hook requests a
  coalesced pass on `Tabs.CollectionChanged`. The hypothesis is directionally
  related, but that callback is before first composition and is not a guaranteed
  post-`ContentRendered` boundary. Its regression is source-text inspection,
  not behavior. Its two unique fix/test commits are superseded by the validated
  lifecycle fix below; no unique useful code is being discarded.
- PR #16 / `fix/initial-capture-black-presentation`: its `OnContentRendered`
  hypothesis matches the independent evidence. The implementation was
  unconditional, lived in the split partial, and its regression only checked
  source ordering. The retained idea is the first-render hook; the final change
  uses a small one-shot policy, conditions work on an active guest, keeps the
  hook with the ordinary container lifecycle, and adds behavioral coalescing
  tests.
- The campaign and UI remote tips are already reachable from current
  `origin/main` (`git log origin/main..branch` and `git cherry -v` are empty),
  so their work is not stranded. Their remote branch refs are cleanup-only.
  The stash contains an older AGENTS.md
  description of an experimental reparent backend that conflicts with the
  current Shepherd canonical instructions, so it is documentation-only and
  intentionally rejected after inspection.

## Final disposition

The validated implementation was committed as `17ebbbdfa7674dd4f252f31f44036753b7757513`
and pushed to `main` before cleanup. The following dispositions preserve the
useful-work decision made from the baseline inventory:

| Surface | Disposition | Unique work reaching `main` |
| --- | --- | --- |
| PR #15 / `fix/initial-capture-presentation` | Closed as superseded | None; its admission-time hook and source-only test were replaced by the post-`ContentRendered` policy and behavioral tests |
| PR #16 / `fix/initial-capture-black-presentation` | Closed as superseded | None verbatim; its first-render boundary hypothesis was retained and hardened in `17ebbbd` |
| PR #14 / campaign branch | Already merged; remote branch deleted after reachability verification | All desired work was already reachable from `main` |
| PR #13 / UI branch | Already merged; remote branch deleted after reachability verification | All desired work was already reachable from `main` |
| `stash@{0}` | Inspected, classified redundant/outdated documentation, then dropped | None |
| Disposable WPF/HwndHost probe | Evidence-only diagnostic artifact, removed from `D:\Temp` | None |

The final cleanup commands verified that the remote has only `refs/heads/main`,
the local branch list has only `main`, the stash list is empty, and the
repository has one primary worktree. PRs #15 and #16 were closed with comments
linking their supersession to the validated mainline fix. Hosted CI run
`34925077671` failed with zero executed steps in both jobs; local validation is
green and this is recorded as an external runner/allocation failure.
