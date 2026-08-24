<#
.SYNOPSIS
    Export a bounded, exact-byte qualification handoff for another Windows machine.

.DESCRIPTION
    Only the retained candidate executable, its release manifest/checksum file,
    and explicitly supplied matching ValidationDriver/GuineaPig binaries are
    copied. The output contains no source tree, raw logs, desktop titles, URLs,
    or user paths. The returned report is later imported as untrusted data.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CandidateDir,
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [string]$ValidationDriver = '',
    [string]$GuineaPig = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-tooling.ps1')

$candidateRoot = [IO.Path]::GetFullPath($CandidateDir)
$outputRoot = [IO.Path]::GetFullPath($OutputDir)
if (-not (Test-Path -LiteralPath $candidateRoot -PathType Container)) { throw "candidate directory is missing: $candidateRoot" }
if ([string]::Equals($candidateRoot, $outputRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'qualification package output must be different from the candidate directory' }
if (Test-Path -LiteralPath $outputRoot) {
    if (@(Get-ChildItem -LiteralPath $outputRoot -Force).Count -gt 0) { throw "qualification package output directory is not empty: $outputRoot" }
}
else { New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null }

$releasePath = Join-Path $candidateRoot 'release-manifest.json'
$sumsPath = Join-Path $candidateRoot 'SHA256SUMS.txt'
if (-not (Test-Path -LiteralPath $releasePath -PathType Leaf)) { throw "release-manifest.json is missing: $releasePath" }
if (-not (Test-Path -LiteralPath $sumsPath -PathType Leaf)) { throw "SHA256SUMS.txt is missing: $sumsPath" }
$releaseJson = Read-QualificationJsonFile $releasePath
if (@($releaseJson.DuplicateFailures).Count -gt 0) { throw (@($releaseJson.DuplicateFailures) -join '; ') }
$release = $releaseJson.Value
$artifactName = [string](Get-QualificationProperty $release 'artifactFileName')
if ([string]::IsNullOrWhiteSpace($artifactName) -or $artifactName -match '[\\/]') { $artifactName = 'TabDock.exe' }
$candidatePath = Join-Path $candidateRoot $artifactName
if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) { throw "candidate executable is missing: $candidatePath" }
$sourceSha = [string](Get-QualificationProperty $release 'sourceCommitSha')
$candidateSha = Get-QualificationFileSha256 $candidatePath
if (-not [string]::Equals($candidateSha, [string](Get-QualificationProperty $release 'artifactSha256'), [StringComparison]::OrdinalIgnoreCase)) { throw 'release manifest artifactSha256 does not match candidate bytes' }
$sumLine = @(Get-Content -LiteralPath $sumsPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($sumLine.Count -ne 1 -or $sumLine[0] -notmatch '^([0-9a-fA-F]{64})\s+\*?(.+)$') { throw 'SHA256SUMS.txt is not a single strict candidate record' }
if (-not [string]::Equals($Matches[1], $candidateSha, [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals($Matches[2].Trim(), $artifactName, [StringComparison]::OrdinalIgnoreCase)) { throw 'SHA256SUMS.txt does not bind the exact candidate bytes' }

if ([string]::IsNullOrWhiteSpace($ValidationDriver)) {
    $ValidationDriver = Join-Path $candidateRoot 'qualification\tooling\TabDock.ValidationDriver.exe'
    if (-not (Test-Path -LiteralPath $ValidationDriver -PathType Leaf)) { $ValidationDriver = Join-Path $candidateRoot 'ValidationDriver.exe' }
}
$driverPath = [IO.Path]::GetFullPath($ValidationDriver)
if (-not (Test-Path -LiteralPath $driverPath -PathType Leaf)) { throw "matching ValidationDriver executable is missing: $driverPath" }
$pigPath = $null
$pigCompanions = @()
if (-not [string]::IsNullOrWhiteSpace($GuineaPig)) {
    $pigPath = [IO.Path]::GetFullPath($GuineaPig)
    if (-not (Test-Path -LiteralPath $pigPath -PathType Leaf)) { throw "matching GuineaPig executable is missing: $pigPath" }
    $pigStem = [IO.Path]::GetFileNameWithoutExtension($pigPath)
    $pigCompanions = @(Get-ChildItem -LiteralPath ([IO.Path]::GetDirectoryName($pigPath)) -File | Where-Object {
        $_.Name -in @("$pigStem.dll", "$pigStem.deps.json", "$pigStem.runtimeconfig.json")
    })
}

$candidateOut = Join-Path $outputRoot 'candidate'
$toolOut = Join-Path $outputRoot 'tooling'
$instructionsPath = Join-Path $outputRoot 'qualification-instructions.txt'
New-Item -ItemType Directory -Path $candidateOut,$toolOut -Force | Out-Null
Copy-Item -LiteralPath $candidatePath -Destination (Join-Path $candidateOut $artifactName)
Copy-Item -LiteralPath $releasePath -Destination (Join-Path $candidateOut 'release-manifest.json')
Copy-Item -LiteralPath $sumsPath -Destination (Join-Path $candidateOut 'SHA256SUMS.txt')
Copy-Item -LiteralPath $driverPath -Destination (Join-Path $toolOut 'TabDock.ValidationDriver.exe')
$driverStem = [IO.Path]::GetFileNameWithoutExtension($driverPath)
$driverCompanions = @(Get-ChildItem -LiteralPath ([IO.Path]::GetDirectoryName($driverPath)) -File | Where-Object {
    $_.Name -in @("$driverStem.dll", "$driverStem.deps.json", "$driverStem.runtimeconfig.json")
})
foreach ($companion in $driverCompanions) { Copy-Item -LiteralPath $companion.FullName -Destination (Join-Path $toolOut $companion.Name) }
if ($null -ne $pigPath) { Copy-Item -LiteralPath $pigPath -Destination (Join-Path $toolOut 'TabDock.GuineaPig.exe') }
foreach ($companion in $pigCompanions) { Copy-Item -LiteralPath $companion.FullName -Destination (Join-Path $toolOut $companion.Name) }
[IO.File]::WriteAllText($instructionsPath, @"
TabDock independent-machine qualification handoff

Source commit: $sourceSha
Candidate SHA-256: $candidateSha

On the target Windows machine, run the repository-owned command:
  pwsh -File scripts/run-qualification-package.ps1 -PackageDir <this-package>

The default tier is deterministic and synthetic. Physical qualification is
explicit, requires an exclusive supervised desktop lease, and must be run only
when the operator can control the session safely:
  pwsh -File scripts/run-qualification-package.ps1 -PackageDir <this-package> -Tier physical -AllowPhysical

Return only machine-report.json, qualification-bundle.json, run-manifest.json,
and their declared hashes. Do not edit or replace candidate files.
"@, [Text.UTF8Encoding]::new($false))

$files = [System.Collections.Generic.List[object]]::new()
foreach ($file in @(Get-ChildItem -LiteralPath $outputRoot -Recurse -File)) {
    $relative = (Resolve-QualificationRelativePath -Root $outputRoot -RelativePath ([IO.Path]::GetRelativePath($outputRoot, $file.FullName))).RelativePath
    [void]$files.Add([ordered]@{
            relativePath = $relative
            kind = if ($relative -like 'candidate/*') { 'candidate' } elseif ($relative -like 'tooling/*') { 'qualification-tooling' } else { 'instructions' }
            sha256 = Get-QualificationFileSha256 $file.FullName
            sizeBytes = $file.Length
        })
}
$package = [ordered]@{
    schemaVersion = Get-QualificationPackageSchemaVersion
    packageKind = 'qualification-handoff'
    packageId = [Guid]::NewGuid().ToString('D')
    createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
    sourceCommitSha = $sourceSha.ToLowerInvariant()
    candidate = [ordered]@{
        relativePath = "candidate/$artifactName"
        sha256 = $candidateSha
        releaseManifestRelativePath = 'candidate/release-manifest.json'
        releaseManifestSha256 = Get-QualificationFileSha256 (Join-Path $candidateOut 'release-manifest.json')
        artifactName = $artifactName
    }
    driver = [ordered]@{
        relativePath = 'tooling/TabDock.ValidationDriver.exe'
        sha256 = Get-QualificationFileSha256 (Join-Path $toolOut 'TabDock.ValidationDriver.exe')
        fileName = 'TabDock.ValidationDriver.exe'
        runtimeFiles = @($driverCompanions | ForEach-Object {
                [ordered]@{
                    relativePath = "tooling/$($_.Name)"
                    sha256 = Get-QualificationFileSha256 (Join-Path $toolOut $_.Name)
                }
            })
    }
    guineaPig = if ($null -ne $pigPath) {
        [ordered]@{
            relativePath = 'tooling/TabDock.GuineaPig.exe'
            sha256 = Get-QualificationFileSha256 (Join-Path $toolOut 'TabDock.GuineaPig.exe')
            runtimeFiles = @($pigCompanions | ForEach-Object {
                    [ordered]@{
                        relativePath = "tooling/$($_.Name)"
                        sha256 = Get-QualificationFileSha256 (Join-Path $toolOut $_.Name)
                    }
                })
        }
    } else { $null }
    artifactIndex = @($files | Sort-Object relativePath)
    privacy = [ordered]@{ privacySafe = $true; containsRawDesktopData = $false; containsTitles = $false; containsUrls = $false; containsUserPaths = $false }
}
$manifestOut = Join-Path $outputRoot 'qualification-package.json'
[IO.File]::WriteAllText($manifestOut, ($package | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
$verification = Test-QualificationPackage -PackagePath $outputRoot -ExpectedSourceSha $sourceSha -ExpectedCandidateSha $candidateSha
if (-not $verification.Valid) { throw "exported qualification package failed offline verification: $($verification.Failures -join '; ')" }
Write-Host "Qualification package: $outputRoot" -ForegroundColor Green
Write-Host "Candidate SHA-256: $candidateSha" -ForegroundColor Green
Write-Host "Package SHA-256: $(Get-QualificationFileSha256 $manifestOut)" -ForegroundColor Green
