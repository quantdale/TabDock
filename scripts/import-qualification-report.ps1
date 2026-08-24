<#
.SYNOPSIS
    Validate an independent-machine qualification report without executing it.

.DESCRIPTION
    The report, bundle, and package are untrusted returned data. This command
    verifies schemas, hashes, source/candidate identity, native ABI result,
    topology classification, outcome codes, and privacy contracts. It never
    launches the candidate, ValidationDriver, GuineaPig, or any returned script.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ReportPath,
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [switch]$RequirePhysicalMixedDpi,
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-tooling.ps1')
$result = Test-QualificationMachineReport -ReportPath $ReportPath -PackagePath $PackagePath -RequirePhysicalMixedDpi:$RequirePhysicalMixedDpi
$report = $result.Report
$summary = [ordered]@{
    schemaVersion = 1
    importKind = 'qualification-report-import'
    importedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    valid = $result.Valid
    sourceCommitSha = if ($null -ne $report) { [string](Get-QualificationProperty $report 'sourceCommitSha') } else { $null }
    candidateSha256 = if ($null -ne $report) { [string](Get-QualificationProperty $report 'candidateSha256') } else { $null }
    machineReportSha256 = if (Test-Path -LiteralPath $ReportPath -PathType Leaf) { Get-QualificationFileSha256 ([IO.Path]::GetFullPath($ReportPath)) } else { $null }
    qualificationBundleSha256 = if ($null -ne $report) { [string](Get-QualificationProperty $report 'qualificationBundleSha256') } else { $null }
    physicalMixedDpiRequired = [bool]$RequirePhysicalMixedDpi
    failures = @($result.Failures)
    trust = 'validated-data-only; no returned executable or script was executed'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ReportPath))) 'qualification-report-import.json'
}
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), ($summary | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
if (-not $result.Valid) {
    Write-Host 'Qualification report import: FAIL' -ForegroundColor Red
    $result.Failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "Qualification report import: PASS (candidate $($summary.candidateSha256))" -ForegroundColor Green
Write-Host "Validated import record: $OutputPath" -ForegroundColor Green
