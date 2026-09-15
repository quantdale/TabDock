# Plan: refresh repository documentation and agent guidance

**Status:** complete
**Owner/session:** Codex
**Updated:** 2026-09-15

## Objective

Update stale agent instructions, documentation, Markdown, and related non-code
guidance against the current repository. Preserve historical evidence and
distinguish it from instructions for current work.

## Scope and constraints

- No application behavior changes, dependency upgrades, commits, or pushes.
- Verify claims against source, scripts, project files, and Git.
- Edit canonical guidance; do not hand-edit generated OpenSpec mirrors.
- Preserve unresolved physical qualification and external release gates.

## Steps

- [x] Resolve Git state and inspect the prior checkpoint and active plan.
- [x] Audit current entrypoints, agent configuration ownership, and workflows.
- [x] Audit user/developer, architecture, testing, and release documentation.
- [x] Check links, commands, historical boundaries, and generated mirror drift.
- [x] Apply verified corrections and validate the documentation/agent-tooling diff.
- [x] Replace the stale checkpoint with a concise handoff and linked evidence.

## Evidence and decisions

- Starting worktree was clean on `main`; HEAD and local `origin/main` both
  resolved to `ea1ad4367e8c14b68c0ec101355dc452e3d0b69d`.
- The preceding repair is committed in `ea1ad43`; the old checkpoint's
  uncommitted status is obsolete. Prior validation remains historical evidence.
- Repowise reports an index at `7fa466805436` and no synthesis provider;
  use direct current-source checks for this documentation audit.
- Completed audit and validation:
  `.agent/investigations/documentation-refresh-2026-09-15.md`.
- Extended the existing synchronization script instead of hand-editing generated
  skills; 142 generated artifacts now have a reproducible read-only drift gate.

## Handoff

**Next action:** hand off the verified, uncommitted documentation/agent updates.
**Blockers:** none for documentation work.
