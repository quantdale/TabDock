# Maintained script-level safety tests for the A02 validation-storage proof
# and cleanup protocol (scripts/ValidationStorage.ps1). Every destructive case
# runs inside a disposable run-owned fake root; no real profile, journal or
# user directory is ever touched. Run with:
#   pwsh -NoProfile -File scripts/tests/ValidationStorageSafety.Tests.ps1
# Exits 0 on PASS, 1 on any failed check.

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..' 'ValidationStorage.ps1')

$script:Failures = 0
$script:Checks = 0

function Assert-True {
    param([bool]$Condition, [string]$Label)
    $script:Checks++
    if ($Condition) {
        Write-Host "PASS $Label"
    }
    else {
        $script:Failures++
        Write-Host "FAIL $Label" -ForegroundColor Red
    }
}

function New-FixtureRoot {
    param([string]$Name)
    $root = Join-Path ([IO.Path]::GetTempPath()) ("TabDock-ps-safety-$Name-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    return $root
}

function Remove-FixtureRoot {
    param([string]$Root)
    try {
        if (Test-Path -LiteralPath $Root) {
            Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    catch { }
}

function New-MarkerFixture {
    param([string]$Name, [string]$MarkerJson)
    $root = New-FixtureRoot $Name
    $productRoot = Join-Path $root 'validation-product'
    New-Item -ItemType Directory -Path $productRoot -Force | Out-Null
    if ($null -ne $MarkerJson) {
        Set-Content -LiteralPath (Join-Path $productRoot 'tabdock-validation-run.marker.json') -Value $MarkerJson -Encoding UTF8
    }
    Set-Content -LiteralPath (Join-Path $productRoot 'evidence.bin') -Value 'keep' -Encoding UTF8
    return @{ Root = $root; ProductRoot = $productRoot }
}

$runId = 'ps-safety-run'
$candidate = Join-Path ([IO.Path]::GetTempPath()) 'candidate.exe'

# ---------------------------------------------------------------------------
# 1. S2: a fake probe that changes its fixture marker to another owner and
#    exits 4 must not authorize deletion — the retention sentinel survives
#    the actual cleanup authority invoked from both the smoke-level and the
#    outer finally call sites.
# ---------------------------------------------------------------------------
$fixture = New-FixtureRoot 'lostoneowner'
try {
    $tamperScript = Join-Path $fixture 'fake-probe-tamper.ps1'
    @'
$root = $env:TABDOCK_VALIDATION_STATE_ROOT
$markerPath = Join-Path $root 'tabdock-validation-run.marker.json'
$marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
$marker.RunId = 'another-owner'
$marker | ConvertTo-Json | Set-Content -LiteralPath $markerPath -Encoding UTF8
Set-Content -LiteralPath (Join-Path $root 'retention-sentinel.txt') -Value 'keep' -Encoding UTF8
exit 4
'@ | Set-Content -LiteralPath $tamperScript -Encoding UTF8

    $powershellExe = (Get-Process -Id $PID).Path
    $fakeProbeArgs = '-NoProfile -ExecutionPolicy Bypass -File "' + $tamperScript + '"'

    $smokeThrew = $false
    try {
        Invoke-RecoveryProcessSmoke -Path $powershellExe -RunRoot $fixture -ProbeArguments $fakeProbeArgs -TimeoutMs 30000
    }
    catch {
        $smokeThrew = $true
    }
    Assert-True $smokeThrew 'fake probe (marker owner changed, exit 4) refuses the mutating launch'

    $productRoot = Join-Path $fixture 'validation-product'
    $sentinel = Join-Path $productRoot 'retention-sentinel.txt'
    $lostRunId = $script:ValidationRunId
    $lostCandidate = $script:ValidationCandidate

    $deletedByFunction = Remove-RunOwnedTempTree -TempRoot $fixture -ProductRoot $productRoot -RunId $lostRunId -CandidatePath $lostCandidate
    Assert-True ((-not $deletedByFunction) -and (Test-Path -LiteralPath $sentinel)) `
        'retention sentinel survives actual function cleanup after ownership was lost inside the probe'

    $deletedByFinally = Remove-RunOwnedTempTree -TempRoot $fixture -ProductRoot $productRoot -RunId $lostRunId -CandidatePath $lostCandidate
    Assert-True ((-not $deletedByFinally) -and (Test-Path -LiteralPath $sentinel)) `
        'retention sentinel survives the outer finally cleanup path as well'

    Assert-True (Test-Path -LiteralPath $productRoot) 'ambiguous run-owned tree is retained for review'
}
finally {
    Remove-FixtureRoot $fixture
}

# ---------------------------------------------------------------------------
# 2. S3: cleanup requires the COMPLETE marker binding root, run and candidate.
# ---------------------------------------------------------------------------
$incomplete = New-MarkerFixture 'incomplete' ('{"RunId":"' + $runId + '"}')
$deleted = Remove-RunOwnedTempTree -TempRoot $incomplete.Root -ProductRoot $incomplete.ProductRoot -RunId $runId -CandidatePath $candidate
Assert-True ((-not $deleted) -and (Test-Path -LiteralPath (Join-Path $incomplete.ProductRoot 'evidence.bin'))) `
    'incomplete marker (RunId only) blocks cleanup and retains evidence'
Remove-FixtureRoot $incomplete.Root

$wrongRoot = New-MarkerFixture 'wrongroot' ('{"SchemaVersion":1,"RunId":"' + $runId + '","ProductRoot":"C:\\elsewhere\\product","CandidatePath":"' + ($candidate -replace '\\', '\\\\') + '"}')
$deleted = Remove-RunOwnedTempTree -TempRoot $wrongRoot.Root -ProductRoot $wrongRoot.ProductRoot -RunId $runId -CandidatePath $candidate
Assert-True ((-not $deleted) -and (Test-Path -LiteralPath (Join-Path $wrongRoot.ProductRoot 'evidence.bin'))) `
    'copied wrong-root marker blocks cleanup and retains evidence'
Remove-FixtureRoot $wrongRoot.Root

$wrongCandidate = New-MarkerFixture 'wrongcandidate' '{"unusable":true}'
$deleted = Remove-RunOwnedTempTree -TempRoot $wrongCandidate.Root -ProductRoot $wrongCandidate.ProductRoot -RunId $runId -CandidatePath $candidate
Assert-True ((-not $deleted) -and (Test-Path -LiteralPath (Join-Path $wrongCandidate.ProductRoot 'evidence.bin'))) `
    'malformed marker blocks cleanup and retains evidence'
Remove-FixtureRoot $wrongCandidate.Root

$schema = New-MarkerFixture 'schema' ('{"SchemaVersion":999,"RunId":"' + $runId + '","ProductRoot":"' + (($schemaProduct = Join-Path ([IO.Path]::GetTempPath()) 'x') -replace '\\', '\\\\') + '","CandidatePath":"' + ($candidate -replace '\\', '\\\\') + '"}')
$deleted = Remove-RunOwnedTempTree -TempRoot $schema.Root -ProductRoot $schema.ProductRoot -RunId $runId -CandidatePath $candidate
Assert-True ((-not $deleted) -and (Test-Path -LiteralPath (Join-Path $schema.ProductRoot 'evidence.bin'))) `
    'unsupported marker schema blocks cleanup and retains evidence'
Remove-FixtureRoot $schema.Root

$missing = New-MarkerFixture 'missingmarker' $null
$deleted = Remove-RunOwnedTempTree -TempRoot $missing.Root -ProductRoot $missing.ProductRoot -RunId $runId -CandidatePath $candidate
Assert-True ((-not $deleted) -and (Test-Path -LiteralPath (Join-Path $missing.ProductRoot 'evidence.bin'))) `
    'missing marker blocks cleanup and retains evidence'
Remove-FixtureRoot $missing.Root

$owned = New-FixtureRoot 'owned'
$ownedProduct = Join-Path $owned 'validation-product'
New-Item -ItemType Directory -Path $ownedProduct -Force | Out-Null
$ownedCandidate = Join-Path ([IO.Path]::GetTempPath()) 'owned-candidate.exe'
$ownedMarker = @{
    SchemaVersion = 1
    RunId         = $runId
    ProductRoot   = [IO.Path]::GetFullPath($ownedProduct)
    CandidatePath = [IO.Path]::GetFullPath($ownedCandidate)
    CreatedUtc    = [DateTimeOffset]::UtcNow.ToString('O')
} | ConvertTo-Json
Set-Content -LiteralPath (Join-Path $ownedProduct 'tabdock-validation-run.marker.json') -Value $ownedMarker -Encoding UTF8
Set-Content -LiteralPath (Join-Path $ownedProduct 'scratch.bin') -Value 'delete' -Encoding UTF8
$sibling = New-FixtureRoot 'sibling'
Set-Content -LiteralPath (Join-Path $sibling 'outside.bin') -Value 'keep' -Encoding UTF8
$deleted = Remove-RunOwnedTempTree -TempRoot $owned -ProductRoot $ownedProduct -RunId $runId -CandidatePath $ownedCandidate
Assert-True ($deleted -and (-not (Test-Path -LiteralPath $owned)) -and (Test-Path -LiteralPath (Join-Path $sibling 'outside.bin'))) `
    'complete proven marker deletes only the run-owned tree'
Remove-FixtureRoot $sibling

# ---------------------------------------------------------------------------
# 3. S4: the preflight verifier requires the COMPLETE proof contract.
# ---------------------------------------------------------------------------
$roles = Get-ValidationRequiredRoles
$goodProof = [pscustomobject]@{
    ProbeSchema      = 1
    Status           = 'ok'
    EffectiveRoot    = 'C:\run-owned'
    RunId            = $runId
    CandidatePath    = $candidate
    IsValidationRoot = $true
    Roles            = $roles
}
Assert-True ($null -eq (Test-ValidationProof -Proof $goodProof -RunOwnedRoot 'C:\run-owned' -RunId $runId -CandidatePath $candidate)) `
    'complete real proof protocol authorizes the launch'

$badSchema = [pscustomobject]@{
    ProbeSchema      = 999
    Status           = 'ok'
    EffectiveRoot    = 'C:\run-owned'
    RunId            = $runId
    CandidatePath    = $candidate
    IsValidationRoot = $true
    Roles            = $roles
}
Assert-True ((Test-ValidationProof -Proof $badSchema -RunOwnedRoot 'C:\run-owned' -RunId $runId -CandidatePath $candidate) -eq 'proof-schema-mismatch') `
    'unsupported probe schema is refused'

$badValidation = [pscustomobject]@{
    ProbeSchema      = 1
    Status           = 'ok'
    EffectiveRoot    = 'C:\run-owned'
    RunId            = $runId
    CandidatePath    = $candidate
    IsValidationRoot = $false
    Roles            = $roles
}
Assert-True ((Test-ValidationProof -Proof $badValidation -RunOwnedRoot 'C:\run-owned' -RunId $runId -CandidatePath $candidate) -eq 'proof-contract-invalid') `
    'IsValidationRoot=false is refused'

$missingValidation = [pscustomobject]@{
    ProbeSchema   = 1
    Status        = 'ok'
    EffectiveRoot = 'C:\run-owned'
    RunId         = $runId
    CandidatePath = $candidate
    Roles         = $roles
}
Assert-True ((Test-ValidationProof -Proof $missingValidation -RunOwnedRoot 'C:\run-owned' -RunId $runId -CandidatePath $candidate) -eq 'proof-contract-invalid') `
    'missing IsValidationRoot is refused'

$missingRoles = [pscustomobject]@{
    ProbeSchema      = 1
    Status           = 'ok'
    EffectiveRoot    = 'C:\run-owned'
    RunId            = $runId
    CandidatePath    = $candidate
    IsValidationRoot = $true
}
Assert-True ((Test-ValidationProof -Proof $missingRoles -RunOwnedRoot 'C:\run-owned' -RunId $runId -CandidatePath $candidate) -eq 'proof-contract-invalid') `
    'missing Roles is refused'

$partialRoles = [pscustomobject]@{
    ProbeSchema      = 1
    Status           = 'ok'
    EffectiveRoot    = 'C:\run-owned'
    RunId            = $runId
    CandidatePath    = $candidate
    IsValidationRoot = $true
    Roles            = @('state', 'logs')
}
Assert-True ((Test-ValidationProof -Proof $partialRoles -RunOwnedRoot 'C:\run-owned' -RunId $runId -CandidatePath $candidate) -eq 'proof-contract-invalid') `
    'partial role set is refused'

$wrongTypes = [pscustomobject]@{
    ProbeSchema      = '1'
    Status           = 'ok'
    EffectiveRoot    = 'C:\run-owned'
    RunId            = $runId
    CandidatePath    = $candidate
    IsValidationRoot = $true
    Roles            = $roles
}
$wrongTypeResult = Test-ValidationProof -Proof $wrongTypes -RunOwnedRoot 'C:\run-owned' -RunId $runId -CandidatePath $candidate
Assert-True (($null -ne $wrongTypeResult) -and ($wrongTypeResult -notin @('root-mismatch', 'run-id-mismatch', 'candidate-mismatch'))) `
    'wrong field types map to a stable non-identity refusal'

$identityMismatch = [pscustomobject]@{
    ProbeSchema      = 1
    Status           = 'ok'
    EffectiveRoot    = 'C:\elsewhere'
    RunId            = $runId
    CandidatePath    = $candidate
    IsValidationRoot = $true
    Roles            = $roles
}
Assert-True ((Test-ValidationProof -Proof $identityMismatch -RunOwnedRoot 'C:\run-owned' -RunId $runId -CandidatePath $candidate) -eq 'root-mismatch') `
    'mismatched effective root is refused'

# ---------------------------------------------------------------------------
# 4. S5: probe process budget and concurrent stream draining.
# ---------------------------------------------------------------------------
$watch = [Diagnostics.Stopwatch]::StartNew()
$hangOutcome = 'not-run'
try {
    Invoke-StorageProbe -Path (Join-Path $env:SystemRoot 'System32\cmd.exe') -ProductRoot 'C:\run-owned' -RunId $runId -CandidatePath $candidate `
        -TimeoutMs 1000 -ProbeArguments '/c ping -n 60 127.0.0.1 >nul' | Out-Null
    $hangOutcome = 'returned'
}
catch {
    $hangOutcome = 'refused'
}
$watch.Stop()
Assert-True (($hangOutcome -eq 'refused') -and ($watch.Elapsed -lt [TimeSpan]::FromSeconds(15))) `
    'live held-open-stdout child is refused at the process budget'

$watch = [Diagnostics.Stopwatch]::StartNew()
$floodOutcome = 'not-run'
try {
    $floodOut = Invoke-StorageProbe -Path (Join-Path $env:SystemRoot 'System32\cmd.exe') -ProductRoot 'C:\run-owned' -RunId $runId -CandidatePath $candidate `
        -TimeoutMs 15000 -ProbeArguments '/c for /L %i in (1,1,2000) do @echo xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx 1>&2'
    $floodOutcome = if ([string]::IsNullOrEmpty($floodOut)) { 'completed-empty' } else { 'completed-output' }
}
catch {
    $floodOutcome = 'refused'
}
$watch.Stop()
Assert-True (($floodOutcome -eq 'completed-empty') -and ($watch.Elapsed -lt [TimeSpan]::FromSeconds(15))) `
    'stderr-flood child drains concurrently without deadlock'

# R4: an exited child whose pipes are held open by an inherited handle must not
# return success after an unbounded post-exit drain. The holder is bounded
# (ping exits by itself) and its execution is proven by the drain stalling past
# the budget before refusal.
$fixtureR4 = New-FixtureRoot 'r4inherited'
try {
    $proofFile = Join-Path $fixtureR4 'proof.json'
    $proofObject = [pscustomobject]@{
        ProbeSchema      = 1
        Status           = 'ok'
        EffectiveRoot    = 'C:\run-owned'
        RunId            = $runId
        CandidatePath    = $candidate
        IsValidationRoot = $true
        Roles            = (Get-ValidationRequiredRoles)
    }
    $proofObject | ConvertTo-Json | Set-Content -LiteralPath $proofFile -Encoding UTF8

    $watch = [Diagnostics.Stopwatch]::StartNew()
    $inheritedOutcome = 'not-run'
    try {
        $out = Invoke-StorageProbe -Path (Join-Path $env:SystemRoot 'System32\cmd.exe') `
            -ProductRoot 'C:\run-owned' -RunId $runId -CandidatePath $candidate -TimeoutMs 5000 `
            -ProbeArguments ('/c start /b ping -n 15 127.0.0.1 & type "' + $proofFile + '" & exit /b 0')
        $inheritedOutcome = if ($out -match 'ProbeSchema') { 'returned-proof' } else { 'returned-other' }
    }
    catch {
        $inheritedOutcome = 'refused'
    }
    $watch.Stop()
    Assert-True (($inheritedOutcome -eq 'refused') -and ($watch.Elapsed -ge [TimeSpan]::FromMilliseconds(4000)) -and ($watch.Elapsed -lt [TimeSpan]::FromSeconds(12))) `
        'exited probe with inherited-pipe holder is refused within the bounded drain (fixture execution proven)'
}
finally {
    Remove-FixtureRoot $fixtureR4
}

# ---------------------------------------------------------------------------
# 5. R1: cleanup must refuse a replaced ancestor junction instead of following
#    it into relocated evidence (same component policy as bootstrap).
# ---------------------------------------------------------------------------
$fixtureR1 = New-FixtureRoot 'r1junction'
try {
    $runTree = Join-Path $fixtureR1 'run'
    $product = Join-Path $runTree 'validation-product'
    New-Item -ItemType Directory -Path $product -Force | Out-Null
    $markerR1 = @{
        SchemaVersion = 1
        RunId         = $runId
        ProductRoot   = [IO.Path]::GetFullPath($product)
        CandidatePath = [IO.Path]::GetFullPath($candidate)
        CreatedUtc    = [DateTimeOffset]::UtcNow.ToString('O')
    } | ConvertTo-Json
    Set-Content -LiteralPath (Join-Path $product 'tabdock-validation-run.marker.json') -Value $markerR1 -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $product 'evidence.bin') -Value 'keep' -Encoding UTF8
    $movedTree = Join-Path $fixtureR1 'moved-run'
    Move-Item -LiteralPath $runTree -Destination $movedTree
    & cmd /c mklink /J "$runTree" "$movedTree" | Out-Null
    $junctionCreated = Test-Path -LiteralPath $runTree
    try {
        $deleted = Remove-RunOwnedTempTree -TempRoot $fixtureR1 -ProductRoot $product -RunId $runId -CandidatePath $candidate
        $relocatedEvidence = Join-Path (Join-Path $movedTree 'validation-product') 'evidence.bin'
        Assert-True ($junctionCreated -and (-not $deleted) `
                -and (Test-Path -LiteralPath $relocatedEvidence) `
                -and ((Get-Content -LiteralPath $relocatedEvidence -Raw).Trim() -eq 'keep') `
                -and (Test-Path -LiteralPath $movedTree)) `
            'cleanup refuses replaced ancestor junction and retains relocated evidence'
    }
    finally {
        # Remove the fixture junction itself before ordinary teardown; the
        # relocated target must survive independently.
        & cmd /c rmdir "$runTree" | Out-Null
    }
}
finally {
    Remove-FixtureRoot $fixtureR1
}

# ---------------------------------------------------------------------------
# 6. R2: a recorded product root that no longer exists is lost ownership —
#    the enclosing tree must be retained, never deleted by parent cleanup.
# ---------------------------------------------------------------------------
$fixtureR2 = New-FixtureRoot 'r2renamed'
try {
    $product = Join-Path $fixtureR2 'validation-product'
    New-Item -ItemType Directory -Path $product -Force | Out-Null
    $markerR2 = @{
        SchemaVersion = 1
        RunId         = $runId
        ProductRoot   = [IO.Path]::GetFullPath($product)
        CandidatePath = [IO.Path]::GetFullPath($candidate)
        CreatedUtc    = [DateTimeOffset]::UtcNow.ToString('O')
    } | ConvertTo-Json
    Set-Content -LiteralPath (Join-Path $product 'tabdock-validation-run.marker.json') -Value $markerR2 -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $product 'evidence.bin') -Value 'keep' -Encoding UTF8
    $renamed = Join-Path $fixtureR2 'renamed-product'
    Move-Item -LiteralPath $product -Destination $renamed
    $deleted = Remove-RunOwnedTempTree -TempRoot $fixtureR2 -ProductRoot $product -RunId $runId -CandidatePath $candidate
    Assert-True ((-not $deleted) `
            -and (Test-Path -LiteralPath $fixtureR2) `
            -and (Test-Path -LiteralPath (Join-Path $renamed 'evidence.bin'))) `
        'missing recorded root refuses cleanup and retains the enclosing tree with renamed evidence'
}
finally {
    Remove-FixtureRoot $fixtureR2
}

# R2 guard: no recorded root (headless/synthetic) keeps plain scratch cleanup.
$fixtureR2b = New-FixtureRoot 'r2scratch'
try {
    Set-Content -LiteralPath (Join-Path $fixtureR2b 'scratch.bin') -Value 'delete' -Encoding UTF8
    $deleted = Remove-RunOwnedTempTree -TempRoot $fixtureR2b -ProductRoot '' -RunId '' -CandidatePath ''
    Assert-True ($deleted -and (-not (Test-Path -LiteralPath $fixtureR2b))) `
        'no recorded root still permits plain owned-scratch cleanup'
}
finally {
    Remove-FixtureRoot $fixtureR2b
}

Write-Host ''
if ($script:Failures -gt 0) {
    Write-Host "PS SAFETY RESULT: FAIL ($script:Failures of $script:Checks checks failed)" -ForegroundColor Red
    exit 1
}
Write-Host "PS SAFETY RESULT: PASS ($script:Checks checks)" -ForegroundColor Green
exit 0
