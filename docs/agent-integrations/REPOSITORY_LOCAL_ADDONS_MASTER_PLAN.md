# Repository-Local Add-ons Master Plan — TabDock

## Status

**PRESERVED ON MAIN / PLANNING ONLY**

This document is retained as a future repository-local integration plan. The
2026-09-01 consolidation audited current main before preserving it; it did not
authorize implementation, package installation, or configuration changes.
The original planning branch is historical provenance only.

Current main already protects the root `.mcp.json` Repowise registration,
`.vscode/mcp.json`, the committed harness adapter/skill surfaces, `AGENTS.md`,
`ONBOARDING.md`, validation scripts, CI workflows, and `.agent/` state. No
Microsoft Learn or Context7 registration/package was found in those inspected
surfaces. Revalidate the recommendation, upstream identity, advisories,
version/toolchain compatibility, and repository-local scope in a separately
authorized implementation campaign. Do not retroactively treat this
consolidation as add-on acceptance.

## Repository assessment

.NET 8 WPF Windows application with heavy P/Invoke/Win32 behavior and a canonical PowerShell validation harness.

**Decision:** `RECOMMEND_MICROSOFT_DOCS_STACK`

Keep scripts/validate.ps1 as the qualification authority.

## Recommended additions

### 1. Microsoft Learn MCP — RECOMMEND

**Type:** MCP

**Why it fits:** Excellent fit for current .NET 8, WPF and Win32/P/Invoke documentation.

**Constraints:** Docs only; repository-scoped MCP registration.

### 2. Context7 MCP — RECOMMEND

**Type:** MCP

**Why it fits:** Supplemental version-aware library documentation where Learn coverage is not package-specific.

**Constraints:** Docs only and optional; do not duplicate Learn queries unnecessarily.

## Explicitly not recommended

- Generic desktop-control MCPs
- Retargeting .NET or changing SDK policy to satisfy a tool
- Global dotnet tools solely for agent integration

## Non-negotiable preservation rule

Implementation is **additive only**. Do not remove, disable, rename, replace, migrate, or silently rewrite any existing MCP, plugin, skill, agent configuration, test harness, editor integration, project-local command, or durable agent-state mechanism.

Before any implementation, inventory the complete tracked tree for integration surfaces, including:

- `.mcp.json`, `mcp.json`, `.vscode/mcp.json`, `.cursor/**`, `.claude/**`, `.opencode/**`, `opencode.json*`, `.pi/**`;
- `AGENTS.md`, `.agent/**`, project-local skills/plugins and their manifests;
- package manifests, lockfiles, workspace files and postinstall hooks;
- browser/mobile/editor automation config;
- CI workflows and scripts that launch external tools;
- documentation naming MCPs, skills, plugins, agent servers or credentials.

Search the whole repository rather than only these common paths. Record each discovered integration, scope, command, permissions, dependency source and current use.

### Merge-only law

If an existing config must be changed, merge into it. Never regenerate a minimal config that drops unknown keys. Existing entries are protected even when they look redundant. Removal requires a separate creator-approved task.

## Repository-local scope law

Use the narrowest supported scope:

1. repository-tracked configuration;
2. repository-local dev dependency/package;
3. repository-owned wrapper or launcher;
4. pinned ephemeral execution from repository cwd;
5. user/global installation only after separate explicit creator approval.

Do **not** automatically modify home-directory MCP registries, user-wide editor settings, global npm/pip/cargo packages, shell profiles, PATH, global browser profiles, or machine-wide agent settings.

If an add-on fundamentally cannot be made repository-local, stop that item and record `GLOBAL_SCOPE_BLOCKED`. Do not bypass this rule.

For remote documentation MCPs, the repository-local config entry itself is the scope boundary; do not add global registration merely because the endpoint is remote.

## Secrets and data

Never commit credentials, API keys, tokens, cookies, auth state, private user data, device secrets or production evidence. Use environment-variable names only. If a tool needs stronger secrets/authority than the repository currently grants, stop and request a separately governed integration decision.

## Upstream verification requirement

Before pinning any package/server:

1. verify the current official/upstream documentation;
2. confirm the project is still maintained and the package/server identity is correct;
3. inspect current security advisories;
4. confirm minimum runtime/toolchain compatibility;
5. confirm a repository-local launch/configuration path;
6. pin a specific compatible version when local packages are used;
7. record why the selected tool is preferable to existing repository tooling.

Do not copy a stale install command from old notes.

## Implementation sequence

### Phase 0 — Reconcile repository truth

- Fetch current target branch without discarding legitimate newer work.
- Record exact HEAD and working-tree state.
- Read repository governance, active specs/OpenSpec and agent-state files.
- Re-evaluate this recommendation if architecture materially changed after this plan.

### Phase 1 — Existing integration inventory

- Perform the exhaustive inventory described above.
- Mark every pre-existing integration **PROTECTED**.
- Detect whether any recommended add-on already exists.
- If it exists, validate/improve it rather than installing a duplicate.

### Phase 2 — Feasibility gate

For every recommended item answer:

- Does it solve a current repository problem?
- Can it be project-scoped?
- Does it duplicate a stronger existing harness?
- Does it create new write/network/device authority?
- Can its version be pinned or otherwise made reproducible?
- Can it be validated without production/private data?

If the answer makes the integration net-negative, classify it `NOT_RECOMMENDED_AFTER_REVALIDATION` and do not install it.

### Phase 3 — Minimal repository-local implementation

- Add only the integrations still approved by Phase 2.
- Prefer one existing repo-local config over parallel harness-specific copies.
- Keep launchers small and fail-closed.
- Keep credentials external.
- Do not modify unrelated dependencies or architecture.

### Phase 4 — Preflight and safety checks

Where practical, add a fast repository-owned preflight that detects:

- missing local dependency;
- duplicate integration ID;
- accidental global executable resolution when local resolution is required;
- unsafe target/permission configuration;
- embedded secret-like values;
- configuration drift.

The preflight must not mutate production data or contact protected environments.

### Phase 5 — Functional validation

Validate each new integration on the smallest safe local/synthetic target. Demonstrate its intended value and its explicit authority boundary. Then run the repository's existing relevant tests/build/validation gates.

An add-on's own successful demo is never a substitute for the repository's existing acceptance evidence.

### Phase 6 — Preservation audit

Diff the pre/post inventory and prove:

- zero existing integrations removed;
- zero existing capabilities silently disabled;
- zero global configuration changes;
- zero secrets committed;
- zero unrelated dependency churn;
- only approved repository-local additions remain.

### Phase 7 — Handoff

Record:

- exact files changed;
- selected versions/endpoints;
- activation commands;
- scope mechanism;
- environment-variable names only;
- validation results;
- existing-integration preservation proof;
- blocked/not-recommended items and rationale.

## Acceptance criteria

- The final integration set matches the repository-specific recommendation above after current-state revalidation.
- Every new tool is repository-scoped or truthfully blocked.
- Existing MCPs/plugins/skills remain intact.
- No global install or user-wide config mutation occurs automatically.
- No secret/private data enters Git.
- Existing tests, safety boundaries and qualification semantics remain authoritative.
- The change is removable by deleting only the newly introduced repo-local integration files/dependencies, without disturbing unrelated tooling.

## Next-session execution prompt

> Implement this `REPOSITORY_LOCAL_ADDONS_MASTER_PLAN.md` on `quantdale/TabDock`. Start with a complete integration inventory and protect every existing MCP/plugin/skill. Revalidate the recommendation against current code and current upstream documentation/security advisories. Install only integrations that remain net-positive and can be repository-scoped. Never silently fall back to global installation; use `GLOBAL_SCOPE_BLOCKED` instead. Keep secrets external, preserve all existing test/agent/governance authority, validate each addition on a safe local/synthetic target, run the repository's existing relevant gates, perform a before/after preservation audit, and commit only the bounded integration changes and evidence.
