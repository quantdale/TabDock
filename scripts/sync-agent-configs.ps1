<#
.SYNOPSIS
    Re-mirrors the canonical OpenSpec agent-tool configs into the other tool directories.

.DESCRIPTION
    Synchronizes the six tracked OpenSpec workflows from their canonical source.
    Run with -Check for a read-only drift check (exit 1 when copies differ).
    Requires PowerShell 7. No dependency installation or upgrade is performed.

    This script treats .claude/skills/ and .claude/commands/opsx/ as the canonical source and
    re-mirrors them into the other tool directories:

      * Skills retain canonical content with the target's invocation syntax:
        colon-form, dash-form, or skill-name invocation (Codex/shared agents/Kimi Code).
      * Commands (.claude/commands/opsx/<stem>.md) are mirrored into each tool's command/workflow
        directory as opsx-<stem>.md with that tool's required frontmatter convention:
          - .cursor/commands     : name/id/category/description frontmatter, dash-form body
          - .opencode/commands   : description-only frontmatter, dash-form body
          - .clinerules/workflows: "# OPSX: <Name>" heading + description line, dash-form body
          - .kilocode/workflows  : bare body, dash-form body
        Additional formats, prompt extensions, and argument placeholders are recorded below.

    All files are written as UTF-8 without BOM and with LF line endings. Only .claude is
    canonical: hand-edits to generated copies are overwritten by this script. Run it after
    `openspec` CLI regeneration (see docs/internal/AGENT_GUIDE.md). The target table below
    covers the tracked OpenSpec skill and command surfaces; unrelated files are preserved.
    The small goal and GitHub OpenSpec adapters are generated from the templates below.

.EXAMPLE
    .\scripts\sync-agent-configs.ps1
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'

# Resolve everything relative to the repo root (one level above this script).
$RepoRoot    = Split-Path -Parent $PSScriptRoot
$CanonSkills = Join-Path $RepoRoot '.claude\skills'
$CanonCmds   = Join-Path $RepoRoot '.claude\commands\opsx'

# Tools with dash-form workflow commands. Skill-only surfaces are handled separately.
$DashFormTools = @('agent', 'cline', 'clinerules', 'commandcode', 'cursor', 'github', 'kilocode', 'omp', 'opencode', 'pi')
$script:DriftCount = 0
$script:CheckedCount = 0

$SkillNames = @(
    'openspec-apply-change',
    'openspec-archive-change',
    'openspec-explore',
    'openspec-propose',
    'openspec-sync-specs',
    'openspec-update-change'
)

# Target table: tool name -> skills dir (or $null), commands/workflow dir (or $null), command format.
$Targets = @(
    @{ Tool = 'cursor';     Skills = Join-Path $RepoRoot '.cursor\skills';        Commands = Join-Path $RepoRoot '.cursor\commands';        CommandFormat = 'cursor' }
    @{ Tool = 'opencode';   Skills = Join-Path $RepoRoot '.opencode\skills';      Commands = Join-Path $RepoRoot '.opencode\commands';      CommandFormat = 'opencode' }
    @{ Tool = 'clinerules'; Skills = $null;                                       Commands = Join-Path $RepoRoot '.clinerules\workflows';   CommandFormat = 'clinerules' }
    @{ Tool = 'kilocode';   Skills = Join-Path $RepoRoot '.kilocode\skills';      Commands = Join-Path $RepoRoot '.kilocode\workflows';     CommandFormat = 'kilocode' }
    @{ Tool = 'cline';      Skills = Join-Path $RepoRoot '.cline\skills';         Commands = $null }
    @{ Tool = 'codex';      Skills = Join-Path $RepoRoot '.codex\skills';         Commands = $null }
    @{ Tool = 'kimi-code';  Skills = Join-Path $RepoRoot '.kimi-code\skills';     Commands = $null }
    @{ Tool = 'agent';      Skills = Join-Path $RepoRoot '.agent\skills';         Commands = Join-Path $RepoRoot '.agent\workflows'; CommandFormat = 'description' }
    @{ Tool = 'agents';     Skills = Join-Path $RepoRoot '.agents\skills';        Commands = $null }
    @{ Tool = 'codebuddy';  Skills = Join-Path $RepoRoot '.codebuddy\skills';     Commands = Join-Path $RepoRoot '.codebuddy\commands\opsx'; CommandFormat = 'codebuddy' }
    @{ Tool = 'commandcode'; Skills = Join-Path $RepoRoot '.commandcode\skills'; Commands = Join-Path $RepoRoot '.commandcode\commands'; CommandFormat = 'arguments' }
    @{ Tool = 'github';     Skills = Join-Path $RepoRoot '.github\skills';        Commands = Join-Path $RepoRoot '.github\prompts'; CommandFormat = 'description'; Extension = '.prompt.md' }
    @{ Tool = 'omp';        Skills = Join-Path $RepoRoot '.omp\skills';           Commands = Join-Path $RepoRoot '.omp\commands'; CommandFormat = 'description' }
    @{ Tool = 'pi';         Skills = Join-Path $RepoRoot '.pi\skills';            Commands = Join-Path $RepoRoot '.pi\prompts'; CommandFormat = 'description' }
)

if (Test-Path -LiteralPath (Join-Path $RepoRoot '.kimi') -PathType Container) {
    # Preserve the older optional local harness without creating an unused tree.
    $Targets += @{ Tool = 'kimi'; Skills = Join-Path $RepoRoot '.kimi\skills'; Commands = $null }
}

function Read-Utf8 {
    param([string]$Path)
    return [System.IO.File]::ReadAllText($Path, [System.Text.UTF8Encoding]::new($false))
}

function Write-Utf8 {
    param([string]$Path, [string]$Text)
    $Text = $Text.Replace("`r`n", "`n")
    $script:CheckedCount++
    if ((Test-Path -LiteralPath $Path -PathType Leaf) -and (Read-Utf8 $Path) -ceq $Text) { return }
    $script:DriftCount++
    if ($Check) {
        Write-Host "Drift: $([System.IO.Path]::GetRelativePath($RepoRoot, $Path))"
        return
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

function Convert-Invocation {
    param([string]$Text, [string]$Tool)
    if ($DashFormTools -contains $Tool) { return $Text.Replace('/opsx:', '/opsx-') }
    if ($Tool -in @('codex', 'agents', 'kimi-code')) {
        $prefix = if ($Tool -in @('codex', 'agents')) { '$' } else { '/' }
        $names = @{ apply = 'apply-change'; archive = 'archive-change'; explore = 'explore'; propose = 'propose'; sync = 'sync-specs'; update = 'update-change'; continue = 'continue-change' }
        foreach ($stem in $names.Keys) {
            $Text = $Text.Replace("/opsx:$stem", ($prefix + 'openspec-' + $names[$stem]))
        }
    }
    return $Text
}

function Unquote {
    param([string]$Value)
    # Strip surrounding double quotes from a YAML scalar, if present.
    return [regex]::Replace($Value, '^"(.*)"$', '$1')
}

# Splits a canonical command file into its frontmatter key/value pairs and the content that
# follows the closing `---` (which starts with a blank line). Returns a hashtable.
function Split-CommandFile {
    param([string]$Content)
    $m = [regex]::Match($Content, '(?ms)^---\r?\n(.*?)\r?\n---\r?\n')
    if (-not $m.Success) {
        throw "Malformed command file: expected a leading '---' frontmatter block."
    }
    $keys = @{}
    foreach ($line in $m.Groups[1].Value -split "`n") {
        if ($line -match '^([A-Za-z-]+):\s*(.*)$') {
            $keys[$Matches[1]] = $Matches[2]
        }
    }
    return @{ Keys = $keys; AfterFrontmatter = $Content.Substring($m.Length) }
}

if (-not (Test-Path $CanonSkills)) { throw "Canonical skills dir not found: $CanonSkills" }
if (-not (Test-Path $CanonCmds))   { throw "Canonical commands dir not found: $CanonCmds" }

foreach ($t in $Targets) {
    $tool    = $t.Tool
    $summary = @()

    # ---- Skills: canonical text with the target's invocation syntax. ----
    if ($t.Skills) {
        $targetSkills = $t.Skills
        foreach ($name in $SkillNames) {
            $src = Join-Path $CanonSkills $name
            $dst = Join-Path $targetSkills $name
            foreach ($sourceFile in Get-ChildItem -LiteralPath $src -File -Recurse) {
                $relative = [System.IO.Path]::GetRelativePath($src, $sourceFile.FullName)
                Write-Utf8 (Join-Path $dst $relative) (Convert-Invocation (Read-Utf8 $sourceFile.FullName) $tool)
            }
        }
        $summary += 'skills (target invocation syntax)'
    }

    # ---- Commands: mirror opsx-<stem>.md with the target's frontmatter convention. ----
    if ($t.Commands) {
        $targetCmds = $t.Commands
        foreach ($canon in Get-ChildItem -Path $CanonCmds -Filter '*.md') {
            $stem = [System.IO.Path]::GetFileNameWithoutExtension($canon.Name)
            $parts = Split-CommandFile (Read-Utf8 $canon.FullName)
            $name = $parts.Keys['name']
            $desc = $parts.Keys['description']
            $bodyColon = (Convert-Invocation $parts.AfterFrontmatter $tool).TrimStart("`n", "`r")
            $bodyDash  = $parts.AfterFrontmatter -replace '/opsx:', '/opsx-'
            if ($tool -in @('omp', 'pi')) {
                $bodyColon = $bodyColon -replace '(?m)^(\*\*Input\*\*[^\r\n]*)', ('$1' + "`n**Provided arguments**: " + '$@')
            }

            switch ($t.CommandFormat) {
                'cursor' {
                    # name/id are derived from the filename stem; category is always Workflow.
                    $text = "---`nname: `"/opsx-$stem`"`nid: `"opsx-$stem`"`ncategory: `"Workflow`"`ndescription: $desc`n---`n" + $bodyDash
                }
                'opencode' {
                    $text = "---`ndescription: $desc`n---`n" + $bodyDash
                }
                'clinerules' {
                    $text = "# $(Unquote $name)`n`n$(Unquote $desc)`n`n" + $bodyColon
                }
                'kilocode' {
                    $text = $bodyColon
                }
                'description' {
                    $text = "---`ndescription: $desc`n---`n`n" + $bodyColon
                }
                'codebuddy' {
                    $text = "---`nname: $name`ndescription: $desc`nargument-hint: `"[command arguments]`"`n---`n`n" + $bodyColon
                }
                'arguments' {
                    $text = $bodyColon -replace '(?m)^(\*\*Input\*\*[^\r\n]*)', ('$1' + "`n**Provided arguments**: " + '$ARGUMENTS')
                }
                default { throw "Unknown command format: $($t.CommandFormat)" }
            }
            $fileName = if ($t.CommandFormat -eq 'codebuddy') { "$stem.md" } elseif ($t.Extension) { "opsx-$stem$($t.Extension)" } else { "opsx-$stem.md" }
            Write-Utf8 (Join-Path $targetCmds $fileName) $text
        }
        $summary += "commands ($($t.CommandFormat) format)"
    }

    Write-Host "==> $tool`t$($summary -join ', ')"
}

$goalBody = 'Read `AGENTS.md`, `.agent/STATE.md`, `.agent/PLANNER_HANDOFF.md`, and `.agent/EXECUTION_PROMPT.md` if present. Reconcile the active plan with current Git. Resume the first incomplete requirement of an ACTIVE prompt or the user''s native goal, validate, and update state. Commit or push only with explicit user authorization. Do not require a new planning campaign when the current goal already supplies direction.'
Write-Utf8 (Join-Path $RepoRoot '.agents/skills/goal/SKILL.md') ("---`nname: goal`ndescription: Resume the repository's planner-generated or native development campaign.`n---`n`n" + $goalBody + "`n")
Write-Utf8 (Join-Path $RepoRoot '.kimi-code/AGENTS.md') ("# Goal adapter`n`nFor goal continuation, follow this repository's root instructions.`n`n" + $goalBody + "`n")
Write-Utf8 (Join-Path $RepoRoot '.opencode/commands/goal.md') ("---`ndescription: Resume the planner-generated or native active campaign`n---`n`n" + 'Reconcile `$ARGUMENTS` with the current goal. ' + $goalBody + "`n")

$openSpecAdapter = @'
---
name: OpenSpec
description: "Work on TabDock OpenSpec proposals, implementation, specification updates, and archives using the pinned repository CLI."
tools:
  - "execute"
  - "read"
  - "search"
  - "edit"
---

<!-- Generated by scripts/sync-agent-configs.ps1. -->

# OpenSpec agent

Read `AGENTS.md`, `.agent/STATE.md`, and its active plan first. Use the
matching repository OpenSpec skill under `.github/skills/` and follow the
user's requested scope and existing authorization.

Use `tools/openspec/node_modules/.bin/openspec.cmd` from the repository root
on Windows. If absent, install the pinned dependency with
`npm ci --prefix tools/openspec --ignore-scripts`. Do not install or upgrade a
global CLI as a bootstrap fallback. `tools/openspec/package-lock.json` is the
dependency authority; generated skill metadata records generator provenance.

Discover active changes with `list --json`, inspect a selected change with
`status --change <name> --json`, and obtain its next-step contract with
`instructions <artifact> --change <name> --json`. Follow the relevant skill
for apply, sync, and archive operations. Validate changed specifications with
`validate --all --no-interactive`. Commands here are arguments to the pinned
CLI path above. Archived history is under `openspec/changes/archive/`;
current capability specifications are under `openspec/specs/`.
'@
Write-Utf8 (Join-Path $RepoRoot '.github/agents/openspec.agent.md') ($openSpecAdapter + "`n")

Write-Host "Checked $script:CheckedCount generated files; differences: $script:DriftCount. Sources: canonical .claude OpenSpec workflows and this script's adapter templates."
if ($Check -and $script:DriftCount -gt 0) { exit 1 }
