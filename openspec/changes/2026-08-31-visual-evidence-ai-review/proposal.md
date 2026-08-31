# Visual evidence and AI-assisted presentation review

## Why

TabDock's ValidationDriver already performs unusually strong physical and native
qualification. It records process/HWND identity, foreground and point ownership,
geometry, visibility, z-order-adjacent facts, UI Automation state, input
timelines, product/guest logs, and selected pixel-derived metrics such as
brightness, frame difference, and dominant color. The existing `Pixels`
helper can capture the DWM-composited host region with `BitBlt` and can capture
window-rendered content with `PrintWindow(PW_RENDERFULLCONTENT)`.

The remaining blind spot is that the captured pixels are normally reduced to
numbers and discarded. A run can therefore retain enough structured evidence to
say that geometry, liveness, ownership, and brightness were plausible while
retaining no image that lets a human or multimodal development agent answer the
simpler question: **what did TabDock actually look like at the moment of the
interaction or failure?**

That gap matters most for presentation defects that are visually obvious but
difficult to encode completely as native invariants: clipping, mis-centering,
unexpected opaque regions, partial occlusion, transient stacking errors,
misplaced popups, visual drift, flicker, stale frames, poor composition, and
brief wrong-window presentation.

The goal of this change is to turn visual state into a first-class, bounded,
privacy-aware qualification artifact and to define an agent-facing review
protocol so a multimodal AI development agent can actually inspect those images
and correlate them with the existing machine evidence.

## What Changes

- Extend ValidationDriver's existing pixel capture into a retained **visual
  evidence recorder** that can encode selected DWM-composited/window captures as
  PNG artifacts with exact run/scenario/checkpoint identity.
- Add event-driven visual checkpoints around important actions and assertions:
  baseline, pre-input, immediate post-input, settled state, suspicious state,
  assertion failure, and pre-cleanup.
- Add a bounded, opt-in **visual flight recorder** for high-risk transitions.
  It keeps only a small in-memory rolling frame history and flushes the history
  when a failure/suspicious event occurs; it does not record the desktop
  continuously.
- Store image metadata and SHA-256 hashes in the scenario/run artifact graph so
  visual evidence is candidate-bound, tamper-detectable, portable, and
  offline-verifiable.
- Generate an **AI visual review packet** that groups raw images, contact sheets,
  checkpoint metadata, expected visual invariants, pixel metrics, UIA/native
  facts, and timeline references for one scenario/attempt.
- Define a harness-neutral **multimodal agent review contract**. The
  ValidationDriver itself does not require an OpenAI/Anthropic/Google API key
  and does not embed a model SDK. A capable development agent opens the retained
  images using its own vision facility, evaluates them, and writes a structured
  review result tied to image hashes and checkpoint IDs.
- Add an agent workflow for reviewing the packet, escalating suspicious frames,
  correlating visual observations with HWND/UIA/log evidence, and classifying
  whether the finding is product, harness/environment, expected variation, or
  unresolved.
- Add optional deterministic/perceptual image comparisons for controlled
  GuineaPig fixtures, while explicitly rejecting universal pixel-perfect
  golden-image testing as the primary release oracle.
- Integrate visual evidence first into presentation-sensitive scenarios such as
  rename, split, inline capture, context-menu/chrome stability, tab switching,
  maximize/fullscreen containment, topmost interactions, monitor transfer, and
  title-centering qualification.

## Capabilities

### New Capabilities

- `visual-qualification-evidence`: bounded screenshot/checkpoint capture,
  failure-ring retention, artifact integrity, privacy scope, AI review packets,
  multimodal review records, and visual-review disposition semantics.

### Modified Capabilities

- `validation-qualification`: scenario/run manifests and release evidence must
  understand retained visual artifacts and, when a gate declares visual review
  required, must not silently claim visual qualification if the images or
  required review record are missing, tampered, or unavailable.
- `qualification-control-plane`: direct/shard/parent manifests, portable
  qualification bundles, offline verification, planning, and independent-machine
  import must hash/index/verify visual artifacts and visual-review records
  without executing a model or trusting returned image/review bytes.

## Design Intent

The AI reviewer is deliberately **outside the product and outside the core
physical-input safety boundary**.

The intended workflow is:

1. ValidationDriver performs the real/synthetic scenario and captures bounded
   visual checkpoints.
2. The run emits a self-contained review packet with image files plus exact
   native/UIA/log/timeline context.
3. A multimodal development agent opens the contact sheet and raw frames with
   its own image-understanding tool.
4. The agent evaluates expected-versus-observed presentation and writes a
   structured `visual-review-result.json` referencing the exact image hashes.
5. A verifier checks the result's schema and evidence bindings.
6. Any visual defect is correlated with existing native evidence before a
   production change is authorized.

This permits Codex/Claude/Kimi/OpenCode/other multimodal agents to participate
without coupling TabDock to one model vendor or requiring secrets in the repo.

AI review SHALL augment, not replace, the current hard evidence. A model's
statement that an image "looks fine" is not by itself proof of process identity,
foreground ownership, point ownership, desktop lease safety, geometry, DPI,
cleanup, or candidate provenance. Conversely, a retained image that visibly
contradicts a native-only PASS is a real signal that the scenario's acceptance
logic is incomplete and must be investigated.
## Current-main reconciliation — 2026-09-01

This change is preserved on current `main` as a **future qualification and
diagnostic infrastructure** plan. The repository-consolidation campaign does
not implement this system, add model/provider dependencies, or alter existing
qualification behavior.

The presentation-integrity physical campaign has now completed its exercised
matrix using hard native, identity, lease, geometry, DPI, z-order, cleanup, and
rendering evidence. This visual plan is not required to retroactively justify
those valid physical results. Existing physical artifacts remain bound to the
exact candidate they exercised; they must not be relabeled or augmented by
future screenshots.

Visual evidence augments hard evidence. It never replaces candidate/process/
HWND identity, lease and foreground proof, `WindowFromPoint`/`GA_ROOT`,
geometry, DPI, local z-order, cleanup, or native assertions. Future visual
review must not convert a blocked or skipped physical cell into a PASS.


## Privacy and Safety Boundary

Visual evidence can contain materially more sensitive information than current
numeric/log evidence. The default capture scope therefore SHALL be restrictive:

- prefer the run-owned/test-owned TabDock container, host/client region,
  controlled GuineaPig, popup, or a tightly bounded context crop;
- do not capture the entire virtual desktop by default;
- do not continuously record normal user activity;
- require an explicit visual-evidence mode for real-app screenshots;
- record the reason/scope/rect for every capture;
- keep capture retention bounded and run-local;
- never commit generated screenshots to Git;
- never include credentials, tokens, browser history, unrelated window content,
  or arbitrary desktop imagery intentionally;
- preserve the existing supervised desktop lease and guarded-input rules.

A full-desktop diagnostic capture, if implemented at all, must be explicit,
supervised, capability-gated, separately labeled, and excluded from default CI
and ordinary physical runs.

## Non-Goals

- No production TabDock UI behavior changes are part of this change.
- No replacement of HWND/UIA/native invariants with visual-model opinion.
- No always-on screen recorder.
- No hidden cloud upload or vendor-specific inference dependency.
- No automatic screenshot-driven production code edits without evidence
  correlation and a normal defect investigation.
- No universal exact-pixel golden baseline across Windows versions, GPUs, DPI,
  themes, ClearType settings, or real applications.
- No weakening of the Shepherd/no-reparent architecture or physical-input
  safety gates.

## Expected Impact

Primary implementation surfaces:

- `tests/ValidationDriver/TabDock.ValidationDriver/Pixels.cs` or a focused
  successor capture layer;
- new ValidationDriver visual-evidence/artifact/review classes;
- scenario context/checkpoint APIs;
- run/qualification manifest schemas and offline verifiers;
- deterministic driver/unit tests;
- `docs/TESTING.md`;
- agent workflow/instruction surfaces under `.agent/` and canonical agent
  guidance only where needed.

Generated runtime artifacts remain outside Git.

## Acceptance Boundary

This change is complete when all of the following are true:

- a controlled scenario can retain raw PNG checkpoints without changing the
  physical-input safety model;
- every retained image is hash-bound to one candidate/run/attempt/scenario/
  checkpoint and can be verified offline;
- an assertion failure automatically retains sufficient before/after visual
  context within configured bounds;
- a multimodal agent can follow a repository-defined workflow, inspect the
  packet's images, and produce a machine-readable review tied to the exact
  evidence;
- the review distinguishes `VISUAL_OK`, `VISUAL_SUSPECT`,
  `VISUAL_DEFECT`, and `REVIEW_UNAVAILABLE` without inventing a PASS from
  missing evidence;
- the system can demonstrate at least one seeded visual defect that the agent
  flags and at least one healthy controlled state it does not falsely flag;
- visual artifacts remain bounded, privacy-aware, portable, and excluded from
  source control;
- existing deterministic/native qualification continues to pass unchanged
  except for intentional schema/artifact extensions.
