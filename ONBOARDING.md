# Fresh-machine onboarding

This is the canonical bootstrap entry point for a new workstation or a fresh coding-agent environment. Complete this document before implementation work. The objective is a reproducible machine that can build, test, inspect, and operate this repository without rediscovering tooling mid-campaign.

## 1. Preflight rule

1. Clone the repository and enter its root.
2. Confirm the intended repository/branch and fetch current `origin/main`.
3. Read `AGENTS.md`, `.agent/STATE.md`, and its active plan. Use `README.md` for product guidance and load focused architecture, testing, OpenSpec, or release references as the task needs them. `KNOWN_ISSUES.md` is historical evidence, not the current backlog.
4. Install/verify the machine prerequisites below.
5. Enable the committed agent integrations and repository-local skills.
6. Restore .NET dependencies normally under the SDK pin; install OpenSpec from its npm lockfile. Do not upgrade dependencies during bootstrap.
7. Run the baseline validation commands.
8. Only then begin a development campaign. If a prerequisite cannot be satisfied, record it as an environment blocker rather than weakening a gate.

Credentials, API keys, signing material, account logins, licensed assets, and other secrets are machine/user responsibilities. Never commit them.

## 2. Supported host and prerequisites

**Primary host:** Windows 10/11 only for real product behavior; WPF/.NET desktop application.

**Required machine tools**
- Git
- .NET 8 SDK, feature band 8.0.4xx or later within .NET 8 (`global.json`)
- PowerShell 7+ (`pwsh`; CI and the canonical validation scripts run under pwsh)
- Node.js/npm for pinned OpenSpec tooling

**Task-dependent / optional tools**
- Visual Studio 2022 with Windows desktop/.NET tooling, or another C#/XAML editor
- Repowise CLI for the committed repository-intelligence MCP
- Windows UI Automation/browser installs for qualification lanes
- approved signing provider credentials only for production release Stage A


## 3. Agent setup

- Load repository instructions before acting. Prefer committed repository state over chat history.
- Repository-local skills include `goal` and the OpenSpec workflows; use the skills exposed by your harness for the current task.
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
dotnet test tests\UnitTests\TabDock.UnitTests.csproj -c Debug
.\scripts\validate.ps1 -Configuration Release -Ci -Publish
```

A fresh machine is **development-ready** when all applicable non-external gates pass. Hardware/device/signing/account gates may remain explicitly blocked when repository state already classifies them that way.

## 7. Fresh-agent instruction

> Start with `AGENTS.md`, `.agent/STATE.md`, and its active plan. On a fresh machine, use `ONBOARDING.md` to set up the prerequisites relevant to the task. Read `docs/TESTING.md` before validation. Record unavailable capabilities explicitly; do not replace pinned tooling or weaken gates. Routine documentation work does not require installing optional desktop, signing, or release tooling.
