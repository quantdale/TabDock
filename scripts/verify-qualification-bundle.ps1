<#
.SYNOPSIS
    Verify qualification-bundle.json without launching any candidate code.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BundlePath,
    [string]$ExpectedSourceSha = '',
    [string]$ExpectedArtifactSha = '',
    [switch]$RequirePhysicalTopology
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-tooling.ps1')
$result = Test-QualificationBundle -BundlePath $BundlePath `
    -ExpectedSourceSha $ExpectedSourceSha -ExpectedArtifactSha $ExpectedArtifactSha `
    -RequirePhysicalTopology:$RequirePhysicalTopology
$summary = [ordered]@{
    valid = [bool]$result.Valid
    bundlePath = [IO.Path]::GetFullPath($BundlePath)
    failureCount = @($result.Failures).Count
    failures = @($result.Failures)
    schemaVersion = if ($null -ne $result.Bundle) { Get-QualificationProperty $result.Bundle 'schemaVersion' } else { $null }
    sourceCommitSha = if ($null -ne $result.Bundle) { Get-QualificationProperty $result.Bundle 'sourceCommitSha' } else { $null }
    candidateSha256 = if ($null -ne $result.Bundle) { Get-QualificationProperty (Get-QualificationProperty $result.Bundle 'candidate') 'artifactSha256' } else { $null }
}
Write-Output ($summary | ConvertTo-Json -Depth 12)
if (-not $result.Valid) { exit 1 }
exit 0
