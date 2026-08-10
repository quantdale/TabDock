# Shared agent layer

This directory is the durable, harness-neutral support layer for repository work. The root `AGENTS.md` is the compact entrypoint. Harness adapters should point here rather than copy its rules.

## Fresh-session sequence

1. Read `AGENTS.md` and this file.
2. Read `STATE.md`. If it names an active plan, read that plan before acting.
3. Use the repository-exploration workflow for broad discovery and load detailed project references only as needed.
4. Confirm the working tree before editing. Preserve unrelated changes.
5. Implement in small, verifiable stages and checkpoint meaningful progress.

## Durable state

`STATE.md` is a compact current checkpoint, not a transcript. It records the active objective, phase, completed work, important facts, validation, blockers, and next action. Keep historical detail in a linked plan, decision record, or investigation note.

Use:

- `plans/TEMPLATE.md` for multi-step work and the execution source of truth;
- `decisions/TEMPLATE.md` for choices that future agents must not rediscover;
- `investigations/TEMPLATE.md` for bounded research, evidence, failed approaches, and conclusions;
- `workflows/repository-exploration.md` for efficient discovery;
- `workflows/checkpoint.md` for when and how to update state.

Create a task-specific record by copying the relevant template and use a descriptive filename. Do not create a record for trivial one-command work.

## Shared repository intelligence

Repowise is optional infrastructure behind the portable `.mcp.json` registration and Codex’s project-local configuration. Prefer its lean indexed tools for broad questions, then inspect the source files it identifies. The index is a discovery aid, not authority for code changes. Use normal Git/filesystem tools when the index is missing, stale, or not precise enough.

## Portability

The knowledge, state, plans, decisions, and workflows here are vendor-neutral. `CLAUDE.md`, `.clinerules/agent-layer.md`, `.codex/`, and other harness directories are adapters or integrations only. Keep vendor-specific model names, hooks, and command syntax out of the canonical records unless a task explicitly requires them.
