# TabDock — Canonical Agent Instructions

This is the repository’s compact, harness-neutral agent entrypoint. Harness-specific files must point here instead of duplicating these rules. Start with `.agent/STATE.md`; read the detailed reference only when the task needs it.

## Orient quickly

TabDock is a Windows desktop utility built with C# 12, .NET 8, WPF, and P/Invoke. It is a single-project repository (the solution also contains an experimental Spike); `tests/ValidationDriver/` is a separate real-input validation harness. The application uses the Shepherd model: captured windows remain independent top-level windows and are positioned over the container rather than reparented.

Important areas:

- `Services/`, `Models/`, `ViewModels/`, `Views/`, `Infrastructure/`, and `NativeMethods.cs` contain the application.
- `docs/ARCHITECTURE.md` and `docs/TESTING.md` provide focused architecture and validation references.
- `docs/internal/AGENT_GUIDE.md` is the detailed project guide preserved for progressive disclosure.
- `openspec/specs/` contains behavior specifications; active behavior changes should check it first.
- `.agent/STATE.md` is the current checkpoint; `.agent/plans/`, `.agent/decisions/`, and `.agent/investigations/` hold durable supporting records.

## Startup and execution state

For a fresh session, read this file, `.agent/STATE.md`, and the referenced active plan (if any). Keep `STATE.md` concise: objective, status, current phase, completed work, important facts, validation, blockers, and next action. Update it at milestones, after investigations or decisions, after meaningful validation, on blockers/strategy changes, before handoff or likely compaction, and before ending incomplete work. Record durable facts, not a command transcript.

Use the templates in `.agent/` for plans, decisions, and investigations. Make the plan the source of truth for multi-step work and link it from `STATE.md`.

## Repository exploration

For architecture, ownership, callers, dependencies, history, implementations, and broad discovery, use the project-local Repowise MCP/index first when available. Its lean profile exposes six core tools: `search_codebase`, `get_answer`, `get_risk`, `get_context`, `get_symbol`, and `get_why`. Treat retrieval as a map, then read and verify the actual source before editing. Fall back to `rg`, Git, and focused file reads when the index is unavailable, stale, incomplete, or the question requires exact current text. Avoid repeated repository-wide crawls and giant command output; expand focused output only when needed.

## Project rules and validation

- Preserve the Shepherd/no-reparent architecture and existing developer workflows.
- Keep P/Invoke declarations in `NativeMethods.cs`; use explicit `using` directives, nullable annotations, file-scoped namespaces, and normal .NET naming.
- Do not hand-edit generated OpenSpec mirrors outside their canonical source; follow the existing OpenSpec workflow when changing behavior.
- For normal source changes, build with `dotnet build TabDock.sln`. Read `docs/TESTING.md` before validation; the ValidationDriver sends real input and requires an interactive, carefully controlled run.
- Agent-infrastructure work must not change application behavior, introduce unrelated dependencies, or discard working-tree changes. Never reset, clean, or revert user work. Do not commit unless explicitly requested.
- Keep generated indexes, caches, logs, machine paths, credentials, and secrets out of Git. `.repowise/` is local and ignored; portable MCP configuration may be committed only when it contains no machine-specific paths.

## Harness map

Codex uses `.codex/config.toml` and `.codex/hooks.json`; Claude Code uses the small root `CLAUDE.md` adapter; Cline uses `.clinerules/agent-layer.md`. Kimi Code, OpenCode, and other AGENTS-aware harnesses consume this file directly. Shared MCP registration is in `.mcp.json`; harness-specific settings remain outside this canonical file.

When a detailed procedure is needed, load the relevant `.agent/workflows/` file and then the project reference it names. Use the smallest capable model/worker for reconnaissance and routine verification; reserve deeper reasoning for architecture, risky changes, ambiguous requirements, and final decisions.
