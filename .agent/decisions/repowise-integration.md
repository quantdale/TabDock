# Decision: shared lean Repowise integration

**Date:** 2026-08-10
**Status:** accepted

## Context

TabDock is a moderately complex single-project WPF application with native interop, a separate real-input harness, substantial architecture/history documentation, and repeated cross-service navigation needs. Multiple coding-agent harnesses may work in the repository, so the index must not be tied to one vendor or expose unnecessary MCP schema.

## Decision

Use one local Repowise 0.39.0 index, ignore `.repowise/`, and expose the lean six-tool MCP profile (`search_codebase`, `get_answer`, `get_risk`, `get_context`, `get_symbol`, `get_why`) through portable root `.mcp.json` and Codex’s project-local `.codex/config.toml`. Keep Codex refresh hooks in its adapter only. Broad retrieval must be followed by direct source verification.

## Consequences

- Codex and Claude can discover the same repository intelligence without duplicate indexes or absolute checkout paths.
- Kimi, OpenCode, and other AGENTS-aware harnesses still receive the shared exploration policy even when they do not consume `.mcp.json` directly.
- The lean profile omits convenience tools such as overview/synthesis breadth; normal filesystem and Git tools remain the fallback.
- The index is machine-local and must be refreshed when source changes make it stale.

## Evidence

- `repowise status --no-workspace` and `repowise doctor --no-workspace --format table`: 115 database pages (112 rendered wiki pages), zero stale pages, SQL/vector/FTS in sync. The status view counts internal page artifacts differently from its rendered-page field.
- Live MCP initialization exposed exactly the six selected tools; representative symbol, risk, search, and history queries returned repository locations and source-backed results.
