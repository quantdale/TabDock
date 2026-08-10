# Repository exploration workflow

Use this for architecture, ownership, callers, dependencies, history, implementation locations, and other broad questions.

1. Read `.agent/STATE.md` and the relevant plan before starting. Check `git status --short` and identify the task’s scope.
2. Query the project Repowise MCP with the smallest useful question. Prefer `search_codebase` or `get_symbol` for locations, `get_context` for relationships, `get_risk` for change impact, and `get_why` for history. Use `get_answer` only when a synthesized explanation is useful; deterministic indexes may return retrieval targets without prose.
3. Open the returned source files and verify symbols, callers, configuration, tests, and current behavior directly. Retrieval results are navigation aids, not authorization to edit.
4. If Repowise is unavailable or insufficient, use focused `rg` queries, `git log`/`git blame`, and narrow file ranges. Do not dump the whole tree or repeatedly reread unchanged files.
5. For cross-component questions, record the verified relationship in the plan or an investigation note so the next session does not rediscover it.

Keep command output narrow: target a project, symbol, test, log range, or failure. Expand progressively when the evidence is incomplete.
