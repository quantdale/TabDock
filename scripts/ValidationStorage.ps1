# A02 validation-storage proof and cleanup semantics for process qualification.
# Dot-sourced by scripts/validate.ps1 and by the maintained script-level safety
# tests (scripts/tests/ValidationStorageSafety.Tests.ps1). All helpers are fail
# closed: a mutating launch requires the COMPLETE supported proof contract, and
# recursive deletion of a run-owned tree requires the complete ownership marker
# binding root, run and candidate identity at the moment of cleanup. When
# ownership cannot be re-established, the enclosing tree is retained for review
# and the cleanup scope is never broadened.

function Test-SameValidationPath {
    param([string]$Left, [string]$Right)

    if ([string]::IsNullOrWhiteSpace($Left) -or [string]::IsNullOrWhiteSpace($Right)) {
        return $false
    }
    $leftTrim = $Left.Trim()
    $rightTrim = $Right.Trim()
    # Extended/device namespaces are unsupported spellings; treat them as a
    # non-match so they can never alias another identity into agreement.
    if ($leftTrim -match '^(\\\\\?\\|\\\\\.\\)' -or $rightTrim -match '^(\\\\\?\\|\\\\\.\\)') {
        return $false
    }
    try {
        $l = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($leftTrim))
        $r = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($rightTrim))
        return [string]::Equals($l, $r, [StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        return $false
    }
}

function Test-ReparsePointComponent {
    param([string]$Path)

    # Component-level reparse policy shared with the bootstrap refusal: a
    # replaced ancestor junction would carry a delete outside the previously
    # verified tree, so any reparse component (or unverifiable component)
    # fails closed.
    $current = $Path
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        try {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                return $true
            }
        }
        catch {
            return $true
        }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or ($parent -eq $current)) {
            return $false
        }
        $current = $parent
    }
    return $false
}

function Get-ValidationRequiredRoles {
    return @(
        'state', 'backup', 'temp', 'journal', 'pending', 'sidecar-ledger',
        'logs', 'startup-hygiene', 'diagnostic-inspection')
}

function Test-ValidationProof {
    param(
        [object]$Proof,
        [string]$RunOwnedRoot,
        [string]$RunId,
        [string]$CandidatePath)

    # Verifies the COMPLETE supported proof contract: probe schema, correct
    # field types, explicit validation-root status, the required storage-role
    # set, and the exact canonical root/run/candidate binding. Returns $null
    # when the proof authorizes the mutating launch, otherwise a stable
    # refusal code. Malformed values map to stable refusals, never exceptions.
    if ($null -eq $Proof) {
        return 'proof-missing'
    }

    $schemaProp = $Proof.PSObject.Properties['ProbeSchema']
    if (($null -eq $schemaProp) -or (($schemaProp.Value -isnot [int]) -and ($schemaProp.Value -isnot [long]))) {
        return 'proof-schema-mismatch'
    }
    if ([int]$schemaProp.Value -ne 1) {
        return 'proof-schema-mismatch'
    }

    $statusProp = $Proof.PSObject.Properties['Status']
    if ($null -eq $statusProp -or $statusProp.Value -isnot [string]) {
        return 'proof-malformed'
    }
    if ($statusProp.Value -ne 'ok') {
        return 'proof-refused'
    }

    $validationProp = $Proof.PSObject.Properties['IsValidationRoot']
    if ($null -eq $validationProp -or $validationProp.Value -isnot [bool] -or -not $validationProp.Value) {
        return 'proof-contract-invalid'
    }

    $rolesProp = $Proof.PSObject.Properties['Roles']
    if ($null -eq $rolesProp -or $rolesProp.Value -isnot [System.Collections.IEnumerable] -or $rolesProp.Value -is [string]) {
        return 'proof-contract-invalid'
    }
    $reportedRoles = @()
    foreach ($role in $rolesProp.Value) {
        if ($role -isnot [string]) {
            return 'proof-contract-invalid'
        }
        $reportedRoles += [string]$role
    }
    foreach ($required in Get-ValidationRequiredRoles) {
        if ($reportedRoles -notcontains $required) {
            return 'proof-contract-invalid'
        }
    }

    foreach ($field in @('EffectiveRoot', 'RunId', 'CandidatePath')) {
        $prop = $Proof.PSObject.Properties[$field]
        if ($null -eq $prop -or $prop.Value -isnot [string]) {
            return 'proof-malformed'
        }
    }
    if (-not (Test-SameValidationPath $Proof.EffectiveRoot $RunOwnedRoot)) {
        return 'root-mismatch'
    }
    if (-not [string]::Equals([string]$Proof.RunId, [string]$RunId, [StringComparison]::Ordinal)) {
        return 'run-id-mismatch'
    }
    if (-not (Test-SameValidationPath $Proof.CandidatePath $CandidatePath)) {
        return 'candidate-mismatch'
    }
    return $null
}

function Invoke-StorageProbe {
    param(
        [string]$Path,
        [string]$ProductRoot,
        [string]$RunId,
        [string]$CandidatePath,
        [int]$TimeoutMs = 30000,
        [string]$ProbeArguments = '--validation-storage-probe')

    # Bounded read-only child probe: both pipes drain concurrently under one
    # deadline; on timeout or error only the launched child is killed/reaped.
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Path
    # Use the string Arguments property (available on both .NET Framework 4.8
    # / Windows PowerShell 5.1 and .NET / PowerShell 7). ProcessStartInfo.
    # ArgumentList exists only on .NET and is null under Windows PowerShell 5.1.
    $psi.Arguments = $ProbeArguments
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = Split-Path -Parent $Path
    $psi.Environment['TABDOCK_VALIDATION_STATE_ROOT'] = $ProductRoot
    $psi.Environment['TABDOCK_VALIDATION_RUN_ID'] = $RunId
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $psi
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $budgetMs = [Math]::Max(1, $TimeoutMs)
    try {
        if (-not $process.Start()) {
            throw 'Could not start the validation storage probe.'
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $remaining = [Math]::Max(1, $budgetMs - [int]$stopwatch.Elapsed.TotalMilliseconds)
        if (-not $process.WaitForExit($remaining)) {
            try { $process.Kill() } catch { }
            try { $process.WaitForExit(1000) } catch { }
            throw "Validation storage probe exceeded its $budgetMs ms budget and was terminated."
        }
        # One elapsed-time budget carries through process exit AND completion
        # of both stream drains: an exited child whose pipes are held open by
        # an inherited handle must never return success after an unbounded
        # post-exit wait.
        $remaining = [Math]::Max(1, $budgetMs - [int]$stopwatch.Elapsed.TotalMilliseconds)
        $drained = [Threading.Tasks.Task]::WaitAll(
            [Threading.Tasks.Task[]]@($stdoutTask, $stderrTask), $remaining)
        if (-not $drained) {
            throw "Validation storage probe exceeded its $budgetMs ms stream-drain budget after the child exited."
        }
        $stdout = $stdoutTask.Result
        $stderr = $stderrTask.Result
        if ($process.ExitCode -ne 0) {
            throw "Validation storage probe refused (exit code $($process.ExitCode)). stdout=$stdout stderr=$stderr"
        }
        return $stdout
    }
    finally {
        # Dispose/reap only the proven launched child; never kill arbitrary
        # process names or unproven descendants to close pipes.
        if ($process -and -not $process.HasExited) {
            try { $process.Kill() } catch { }
        }
        $process.Dispose()
    }
}

function Remove-RunOwnedTempTree {
    param(
        [string]$TempRoot,
        [string]$ProductRoot,
        [string]$RunId,
        [string]$CandidatePath)

    # The SINGLE cleanup authority used by every exit path (including the
    # outer script finally). Recursive deletion of the enclosing temporary
    # tree is authorized only while the COMPLETE ownership marker still binds
    # this exact root, run and candidate identity. Any incomplete, unsupported,
    # copied or otherwise unproven marker retains the whole tree for review;
    # a local refusal is never bypassed by a broader parent deletion.
    if (-not [string]::IsNullOrWhiteSpace($ProductRoot)) {
        $owned = $false
        # R2: a RECORDED root must re-prove current ownership even when it no
        # longer exists (missing, renamed, replaced). The enclosing tree may
        # hold the moved evidence; it is retained, never deleted by parent
        # cleanup.
        if (-not (Test-Path -LiteralPath $ProductRoot -PathType Container)) {
            Write-Host "Validation storage ownership was not proven at cleanup (recorded root is missing); retaining $TempRoot for review (not deleted)." -ForegroundColor Yellow
            return $false
        }
        $markerPath = Join-Path $ProductRoot 'tabdock-validation-run.marker.json'
        if ((Test-Path -LiteralPath $markerPath -PathType Leaf) -and
            -not (Test-ReparsePointComponent $ProductRoot) -and
            -not (Test-ReparsePointComponent $markerPath)) {
            try {
                $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
                $schemaOk = ($null -ne $marker.PSObject.Properties['SchemaVersion']) `
                    -and (($marker.SchemaVersion -is [int]) -or ($marker.SchemaVersion -is [long])) `
                    -and ([int]$marker.SchemaVersion -eq 1)
                $runOk = ($null -ne $marker.PSObject.Properties['RunId']) `
                    -and ($marker.RunId -is [string]) `
                    -and ([string]::Equals([string]$marker.RunId, [string]$RunId, [StringComparison]::Ordinal))
                $rootOk = ($null -ne $marker.PSObject.Properties['ProductRoot']) `
                    -and ($marker.ProductRoot -is [string]) `
                    -and (Test-SameValidationPath ([string]$marker.ProductRoot) $ProductRoot)
                $candidateOk = ($null -ne $marker.PSObject.Properties['CandidatePath']) `
                    -and ($marker.CandidatePath -is [string]) `
                    -and (Test-SameValidationPath ([string]$marker.CandidatePath) $CandidatePath)
                if ($schemaOk -and $runOk -and $rootOk -and $candidateOk) {
                    $owned = $true
                }
            }
            catch { }
        }
        if (-not $owned) {
            Write-Host "Validation storage ownership was not proven at cleanup; retaining $TempRoot for review (not deleted)." -ForegroundColor Yellow
            return $false
        }
    }
    # R1: never delete through a changed boundary — the enclosing cleanup path
    # must still be free of reparse components immediately before recursion.
    if (Test-ReparsePointComponent $TempRoot) {
        Write-Host "Validation storage cleanup boundary is a reparse point; retaining $TempRoot for review (not deleted)." -ForegroundColor Yellow
        return $false
    }
    if (Test-Path -LiteralPath $TempRoot) {
        Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    return $true
}

function Invoke-RecoveryProcessSmoke {
    param(
        [string]$Path,
        [string]$RunRoot,
        [string]$ProbeArguments = '--validation-storage-probe',
        [int]$TimeoutMs = 30000)

    Write-Host ''
    Write-Host '==> Supervised recovery redirected-process smoke' -ForegroundColor Cyan

    # A02: run-owned, marker-bound validation storage root with child-observed
    # read-only proof before the mutating recovery command. The paired
    # TABDOCK_VALIDATION_STATE_ROOT / TABDOCK_VALIDATION_RUN_ID inputs are the
    # authoritative isolation; APPDATA-only overrides are never accepted as
    # proof and are only defense in depth below. No cleanup happens here: the
    # single ownership-checked cleanup runs on every exit path afterwards.
    $runId = [Guid]::NewGuid().ToString('N')
    $candidate = [IO.Path]::GetFullPath($Path)
    $productRoot = [IO.Path]::GetFullPath((Join-Path $RunRoot 'validation-product'))
    New-Item -ItemType Directory -Path $productRoot -Force | Out-Null
    $marker = @{
        SchemaVersion = 1
        RunId         = $runId
        ProductRoot   = $productRoot
        CandidatePath = $candidate
        CreatedUtc    = [DateTimeOffset]::UtcNow.ToString('O')
    } | ConvertTo-Json
    Set-Content -LiteralPath (Join-Path $productRoot 'tabdock-validation-run.marker.json') -Value $marker -Encoding UTF8

    # Recorded BEFORE anything can fail so the outer cleanup always knows the
    # run-owned tree and can recheck its ownership on failure paths too.
    $script:ValidationProductRoot = $productRoot
    $script:ValidationRunId = $runId
    $script:ValidationCandidate = $candidate

    # Production-like pending/ledger sentinel evidence OUTSIDE the verified
    # root must stay byte-for-byte untouched: the child may only observe its
    # own run-owned directory.
    $outside = Join-Path $RunRoot 'outside-sentinels'
    New-Item -ItemType Directory -Path $outside -Force | Out-Null
    $sentinelPaths = @(
        (Join-Path $outside 'hidden-windows.json.pending'),
        (Join-Path $outside 'hidden-windows.json.pending.recovered'),
        (Join-Path $outside 'state.json'))
    Set-Content -LiteralPath $sentinelPaths[0] -Value '{"Version":2,"Entries":[]}' -Encoding UTF8
    Set-Content -LiteralPath $sentinelPaths[1] -Value '{"Resolutions":[],"Transactions":[]}' -Encoding UTF8
    Set-Content -LiteralPath $sentinelPaths[2] -Value '{"Version":2,"Groups":[]}' -Encoding UTF8
    $sentinelBefore = @{}
    foreach ($sentinel in $sentinelPaths) {
        $sentinelBefore[$sentinel] = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
    }

    # Child-observed proof: the exact candidate must report exactly this
    # canonical run-owned root and run identity before any mutating run. The
    # complete proof contract (schema, types, validation-root status, roles,
    # identity binding) must hold or the launch is refused.
    $proofText = Invoke-StorageProbe $Path $productRoot $runId $candidate -TimeoutMs $TimeoutMs -ProbeArguments $ProbeArguments
    $proof = $null
    try { $proof = $proofText | ConvertFrom-Json } catch { }
    $refusal = Test-ValidationProof -Proof $proof -RunOwnedRoot $productRoot -RunId $runId -CandidatePath $candidate
    if ($null -ne $refusal) {
        throw "Storage isolation preflight refused the mutating launch ($refusal). proof=$proofText"
    }

    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Path
    $psi.Arguments = '--recover-pending'
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = Split-Path -Parent $Path
    $psi.Environment['TABDOCK_VALIDATION_STATE_ROOT'] = $productRoot
    $psi.Environment['TABDOCK_VALIDATION_RUN_ID'] = $runId
    $psi.Environment['APPDATA'] = Join-Path $RunRoot 'isolated-appdata\Roaming'
    $psi.Environment['LOCALAPPDATA'] = Join-Path $RunRoot 'isolated-appdata\Local'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $psi
    try {
        if (-not $process.Start()) {
            throw 'Could not start the recovery executable.'
        }
        # EOF is intentional: the run-owned directory is empty, so the real
        # WinExe must print its no-pending result and exit without WPF startup
        # or an interactive ReadLine hang.
        $process.StandardInput.Close()
        $recoverStopwatch = [Diagnostics.Stopwatch]::StartNew()
        $recoverBudgetMs = [Math]::Max(1, $TimeoutMs)
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $remaining = [Math]::Max(1, $recoverBudgetMs - [int]$recoverStopwatch.Elapsed.TotalMilliseconds)
        if (-not $process.WaitForExit($remaining)) {
            $process.Kill()
            throw 'Recovery process did not exit within the guarded time budget.'
        }
        # Same single-deadline drain as the probe: bounded post-exit stream
        # completion, no unbounded .Result waits.
        $remaining = [Math]::Max(1, $recoverBudgetMs - [int]$recoverStopwatch.Elapsed.TotalMilliseconds)
        $drained = [Threading.Tasks.Task]::WaitAll(
            [Threading.Tasks.Task[]]@($stdoutTask, $stderrTask), $remaining)
        if (-not $drained) {
            throw "Recovery process exceeded its $recoverBudgetMs ms stream-drain budget after exit."
        }
        $stdout = $stdoutTask.Result
        $stderr = $stderrTask.Result
        if ($process.ExitCode -ne 0) {
            throw "Recovery process exited with code $($process.ExitCode). stderr=$stderr"
        }
        if ($stdout -notmatch '(?i)no unresolved pending recovery entries') {
            throw "Recovery process did not emit the expected no-pending result. stdout=$stdout"
        }
    }
    finally {
        # Never leave a launched copy of the executable holding a lock on the
        # build output (which would break a subsequent publish). Kill it if it
        # did not already exit.
        if ($process -and -not $process.HasExited) {
            try { $process.Kill() } catch { }
        }
        $process.Dispose()
    }

    # Outside sentinels prove the child never touched evidence beyond its
    # verified run-owned directory.
    foreach ($sentinel in $sentinelPaths) {
        $after = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
        if ($after -ne $sentinelBefore[$sentinel]) {
            throw "Outside sentinel evidence was modified during the recovery smoke: $sentinel"
        }
    }

    Write-Host 'Redirected WinExe lifecycle, verified validation storage root, EOF input, output, and exit code: PASS' -ForegroundColor Green
}
