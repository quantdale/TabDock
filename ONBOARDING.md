# Fresh-machine onboarding

This is the canonical bootstrap entry point for a new workstation or a fresh coding-agent environment. Complete this document before implementation work. The objective is a reproducible machine that can build, test, inspect, and operate this repository without rediscovering tooling mid-campaign.

## 1. Preflight rule

1. Clone the repository and enter its root.
2. Confirm the intended repository/branch and fetch current `origin/main`.
3. Read the repository control-plane documents before changing code: `AGENTS.md`, `CLAUDE.md`, `README.md`, `KNOWN_ISSUES.md`, `.agent/`, active OpenSpec/release state.
4. Install/verify the machine prerequisites below.
5. Enable the committed agent integrations and repository-local skills.
6. Restore dependencies from lockfiles/pins; do not casually upgrade them during bootstrap.
7. Run the baseline validation commands.
8. Only then begin a development campaign. If a prerequisite cannot be satisfied, record it as an environment blocker rather than weakening a gate.

Credentials, API keys, signing material, account logins, licensed assets, and other secrets are machine/user responsibilities. Never commit them.

## 2. Supported host and prerequisites

**Primary host:** Windows 10/11 only for real product behavior; WPF/.NET desktop application.

**Required machine tools**
- Git
- .NET SDK 8.0.400 feature band (`global.json`)
- Visual Studio/Build Tools with Windows desktop/.NET tooling
- PowerShell
- Node.js/npm for pinned OpenSpec tooling

**Task-dependent / optional tools**
- Repowise CLI for the committed repository-intelligence MCP
- Windows UI Automation/browser installs for qualification lanes
- approved signing provider credentials only for production release Stage A


## 3. Agent setup

- Load repository instructions before acting. Prefer committed repository state over chat history.
- Repository-local skills: `goal`.
- Discover and use committed agent adapter/config directories in-place; do not duplicate them globally unless the harness cannot load repository-local configuration.
- Relevant committed agent surfaces: `.agent/`, `.agents/`, `.claude/`, `.cline/`, `.codex/`, `.cursor/`, `.kilocode/`, `.kimi*/`, `.opencode/`.
- MCP policy: Use committed `.mcp.json`: `repowise mcp . --transport stdio`. Install the Repowise CLI if absent; it is codebase intelligence only and does not replace builds/tests/release evidence.
- Keep diagnostic/documentation MCPs narrow. An MCP does not grant architecture, publishing, production, or gate-bypass authority.
- Authenticate GitHub and coding-agent CLIs separately on the machine. Never store tokens in tracked files.

## 4. Bootstrap

```powershell
dotnet --info
dotnet restore TabDock.sln
# If using the committed MCP:
repowise --version
```

Do not weaken Authenticode, exact-SHA, mixed-DPI, Windows-version, or human-smoke release gates on a machine that cannot perform them.


## 5. Editor/LSP baseline

Use Roslyn/C# tooling with WPF/XAML support. The SDK pin in `global.json` is authoritative; avoid silent roll-forward to a different major SDK.

The editor is optional; reliable language diagnostics are not.

## 6. Baseline verification

```powershell
dotnet build TabDock.sln -c Debug
dotnet test tests\UnitTests\UnitTests.csproj -c Debug
powershell -ExecutionPolicy Bypass -File scripts\validate.ps1
```

A fresh machine is **development-ready** when all applicable non-external gates pass. Hardware/device/signing/account gates may remain explicitly blocked when repository state already classifies them that way.

## 7. Fresh-agent instruction

> Read `ONBOARDING.md` first. Set up every applicable prerequisite, repository-local skill, MCP/plugin, dependency, browser/device/runtime tool, and validation gate described there. Then read the repository's durable agent state and only start implementation after preflight is green or a genuine environment blocker is recorded. Do not replace pinned tooling, skip gates, or invent work to compensate for a missing machine capability.
