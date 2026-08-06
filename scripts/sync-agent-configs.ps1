<#
.SYNOPSIS
    Re-mirrors the canonical OpenSpec agent-tool configs into the other tool directories.

.DESCRIPTION
    The `openspec` CLI vendors its 6 opsx workflow skills and command files into all agent-tool
    directories, but its output drifts: .cursor command files are dash-named (/opsx-apply) yet
    their bodies (and the .cursor/.opencode skill copies) still reference colon-form commands
    (/opsx:apply) that do not exist there.

    This script treats .claude/skills/ and .claude/commands/opsx/ as the canonical source and
    re-mirrors them into the other tool directories:

      * Skills (.claude/skills/<name>/SKILL.md) are copied byte-identically into each tool
        directory's skills/ folder, EXCEPT for tools whose slash-command convention is dash-form
        (.cursor, .opencode), where every `/opsx:` reference is rewritten to `/opsx-`.
      * Commands (.claude/commands/opsx/<stem>.md) are mirrored into each tool's command/workflow
        directory as opsx-<stem>.md with that tool's required frontmatter convention:
          - .cursor/commands     : name/id/category/description frontmatter, dash-form body
          - .opencode/commands   : description-only frontmatter, dash-form body
          - .clinerules/workflows: "# OPSX: <Name>" heading + description line, colon-form body
          - .kilocode/workflows  : bare body (no frontmatter, no title), colon-form body
        These per-tool transforms were verified to reproduce the CLI's existing output
        byte-for-byte.

    All files are written as UTF-8 without BOM and with LF line endings. Only .claude is
    canonical: hand-edits to any other copy are overwritten by this script. Run it after every
    `openspec` CLI regeneration (see the 'Spec-driven changes (OpenSpec)' section of AGENTS.md).

.EXAMPLE
    .\scripts\sync-agent-configs.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# Resolve everything relative to the repo root (one level above this script).
$RepoRoot    = Split-Path -Parent $PSScriptRoot
$CanonSkills = Join-Path $RepoRoot '.claude\skills'
$CanonCmds   = Join-Path $RepoRoot '.claude\commands\opsx'

# Tools whose slash-command convention is dash-form (/opsx-apply): their skill copies get the
# /opsx: -> /opsx- rewrite. All other tools keep the canonical colon-form copies byte-identical.
$DashFormTools = @('cursor', 'opencode')

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
    @{ Tool = 'kimi';       Skills = Join-Path $RepoRoot '.kimi\skills';          Commands = $null }
    @{ Tool = 'kimi-code';  Skills = Join-Path $RepoRoot '.kimi-code\skills';     Commands = $null }
)

function Read-Utf8 {
    param([string]$Path)
    return [System.IO.File]::ReadAllText($Path, [System.Text.UTF8Encoding]::new($false))
}

function Write-Utf8 {
    param([string]$Path, [string]$Text)
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
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

    # ---- Skills: byte-identical copy, then the dash-form rewrite where required. ----
    if ($t.Skills) {
        $targetSkills = $t.Skills
        New-Item -ItemType Directory -Path $targetSkills -Force | Out-Null
        foreach ($name in $SkillNames) {
            $src = Join-Path $CanonSkills $name
            $dst = Join-Path $targetSkills $name
            Remove-Item -Path $dst -Recurse -Force -ErrorAction SilentlyContinue
            Copy-Item -Path $src -Destination $targetSkills -Recurse -Force
            if ($DashFormTools -contains $tool) {
                $skillFile = Join-Path $dst 'SKILL.md'
                Write-Utf8 $skillFile ((Read-Utf8 $skillFile) -replace '/opsx:', '/opsx-')
            }
        }
        $summary += if ($DashFormTools -contains $tool) { 'skills (dash-form /opsx-)' } else { 'skills (byte-identical)' }
    }

    # ---- Commands: mirror opsx-<stem>.md with the target's frontmatter convention. ----
    if ($t.Commands) {
        $targetCmds = $t.Commands
        New-Item -ItemType Directory -Path $targetCmds -Force | Out-Null
        Remove-Item -Path (Join-Path $targetCmds 'opsx-*.md') -Force -ErrorAction SilentlyContinue
        foreach ($canon in Get-ChildItem -Path $CanonCmds -Filter '*.md') {
            $stem = [System.IO.Path]::GetFileNameWithoutExtension($canon.Name)
            $parts = Split-CommandFile (Read-Utf8 $canon.FullName)
            $name = $parts.Keys['name']
            $desc = $parts.Keys['description']
            $bodyColon = $parts.AfterFrontmatter.TrimStart("`n", "`r")
            $bodyDash  = $parts.AfterFrontmatter -replace '/opsx:', '/opsx-'

            switch ($t.CommandFormat) {
                'cursor' {
                    # name/id are derived from the filename stem; category is always Workflow.
                    $text = "---`nname: /opsx-$stem`nid: opsx-$stem`ncategory: Workflow`ndescription: $desc`n---`n" + $bodyDash
                }
                'opencode' {
                    $text = "---`ndescription: $(Unquote $desc)`n---`n" + $bodyDash
                }
                'clinerules' {
                    $text = "# $(Unquote $name)`n`n$(Unquote $desc)`n`n" + $bodyColon
                }
                'kilocode' {
                    $text = $bodyColon
                }
                default { throw "Unknown command format: $($t.CommandFormat)" }
            }
            Write-Utf8 (Join-Path $targetCmds "opsx-$stem.md") $text
        }
        $summary += "commands ($($t.CommandFormat) format)"
    }

    Write-Host "==> $tool`t$($summary -join ', ')"
}

Write-Host 'Done. Canonical source: .claude/skills + .claude/commands/opsx; everything else re-mirrored.'
