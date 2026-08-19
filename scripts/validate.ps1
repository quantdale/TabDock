<#
.SYNOPSIS
    One-command validation entry point for TabDock.
.DESCRIPTION
    Builds the solution once (covering the main app and Spike) plus the
    ValidationDriver, GuineaPig, and non-gating Performance projects, then runs
    the hermetic diagnostics/geometry/persistence/privacy qualification.
    Real-input ValidationDriver execution is opt-in and remains supervised.
.PARAMETER Configuration
    Build configuration. Debug is the local default; CI uses Release.
.PARAMETER Ci
    Enable the CI qualification policy, including NuGet vulnerability audit,
    OpenSpec validation, support-bundle privacy inspection, and no-restore
    builds after the audited restore.
.PARAMETER Publish
    Also run the self-contained single-file publish smoke test (Release,
    win-x64) and execute the published executable's --version command.
.PARAMETER Scenario
    After building, run the ValidationDriver with --yes and the named scenario
    or shard. The harness drives the desktop with real SendInput input; do not
    touch the mouse or keyboard during a run.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Ci,
    [switch]$Publish,
    [string]$Scenario
)

$ErrorActionPreference = 'Stop'

$RepoRoot       = Split-Path -Parent $PSScriptRoot
$MainProject    = Join-Path $RepoRoot 'TabDock.csproj'
$Solution       = Join-Path $RepoRoot 'TabDock.sln'
$AppExe         = Join-Path $RepoRoot "bin\$Configuration\net8.0-windows\win-x64\TabDock.exe"
$DriverProject  = Join-Path $RepoRoot 'tests\ValidationDriver\TabDock.ValidationDriver\TabDock.ValidationDriver.csproj'
$PigProject     = Join-Path $RepoRoot 'tests\ValidationDriver\TabDock.GuineaPig\TabDock.GuineaPig.csproj'
$PerfProject    = Join-Path $RepoRoot 'tests\Performance\TabDock.Performance.csproj'
$OpenSpecRoot   = Join-Path $RepoRoot 'tools\openspec'
$OpenSpecLocal  = Join-Path $OpenSpecRoot 'node_modules\.bin\openspec.cmd'
$TempRoot       = Join-Path ([IO.Path]::GetTempPath()) "TabDock-validation-$PID-$([Guid]::NewGuid().ToString('N'))"

function Invoke-Step {
    param([string]$Name, [scriptblock]$Body)

    Write-Host ''
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Body
    if ($LASTEXITCODE -ne 0) {
        throw "FAILED: $Name (exit code $LASTEXITCODE)"
    }
}

function ConvertTo-ProcessArgumentLine {
    param([string[]]$Arguments)

    # Start-Process receives one command-line string on Windows. Quote every
    # value so repo paths containing spaces remain one argument.
    return (($Arguments | ForEach-Object {
        '"' + $_.Replace('"', '\"') + '"'
    }) -join ' ')
}

function Invoke-Executable {
    param([string]$Name, [string]$Path, [string[]]$Arguments)

    Write-Host ''
    Write-Host "==> $Name" -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Executable not found: $Path"
    }
    $argumentLine = ConvertTo-ProcessArgumentLine $Arguments
    $process = Start-Process -FilePath $Path -ArgumentList $argumentLine -NoNewWindow -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "FAILED: $Name (exit code $($process.ExitCode))"
    }
}

function Invoke-RecoveryProcessSmoke {
    param([string]$Path, [string]$IsolatedAppData)

    Write-Host ''
    Write-Host '==> Supervised recovery redirected-process smoke' -ForegroundColor Cyan
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Path
    # Use the string Arguments property (available on both .NET Framework 4.8
    # / Windows PowerShell 5.1 and .NET / PowerShell 7). ProcessStartInfo.
    # ArgumentList exists only on .NET and is null under Windows PowerShell 5.1.
    $psi.Arguments = '--recover-pending'
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = $RepoRoot
    $psi.Environment['APPDATA'] = $IsolatedAppData
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $psi
    try {
        if (-not $process.Start()) {
            throw 'Could not start the recovery executable.'
        }
        # EOF is intentional: the isolated directory is empty, so the real
        # WinExe must print its no-pending result and exit without WPF startup
        # or an interactive ReadLine hang.
        $process.StandardInput.Close()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill()
            throw 'Recovery process did not exit within 30 seconds.'
        }
        # .Result is an instance property available on both Windows PowerShell
        # 5.1 and PowerShell 7; the extension-method GetAwaiter() used earlier
        # is not bound by Windows PowerShell 5.1 and returns null there.
        $stdout = $stdoutTask.Result
        $stderr = $stderrTask.Result
        if ($process.ExitCode -ne 0) {
            throw "Recovery process exited with code $($process.ExitCode). stderr=$stderr"
        }
        if ($stdout -notmatch '(?i)no unresolved pending recovery entries') {
            throw "Recovery process did not emit the expected no-pending result. stdout=$stdout"
        }
        Write-Host 'Redirected WinExe lifecycle, isolated APPDATA, EOF input, output, and exit code: PASS' -ForegroundColor Green
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
}

function Assert-SupportBundlePrivacy {
    param([string]$BundlePath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($BundlePath)
    try {
        $requiredEntries = @(
            'version.txt', 'doctor.txt', 'environment.json', 'environment.txt',
            'state-summary.json', 'hwnd-snapshot.json', 'logical-snapshot.json',
            'trace.jsonl', 'recent-log.txt'
        )
        $entryNames = @($archive.Entries | ForEach-Object { $_.FullName })
        foreach ($entryName in $requiredEntries) {
            if ($entryNames -notcontains $entryName) {
                throw "Support bundle is missing required entry '$entryName'."
            }
        }
        if ($archive.Entries.Count -lt $requiredEntries.Count) {
            throw "Support bundle has too few entries."
        }

        $forbiddenPaths = @(
            $env:USERPROFILE,
            $env:APPDATA,
            $env:LOCALAPPDATA,
            (Join-Path $env:USERPROFILE 'AppData'),
            (Join-Path $env:USERPROFILE 'AppData\Roaming'),
            (Join-Path $env:USERPROFILE 'AppData\Local')
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

        foreach ($entry in $archive.Entries) {
            $reader = [IO.StreamReader]::new($entry.Open())
            try {
                $content = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
            foreach ($needle in $forbiddenPaths) {
                if ($content.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    throw "Support bundle entry '$($entry.FullName)' contains a personal path."
                }
            }
            $hasUsername = -not [string]::IsNullOrWhiteSpace($env:USERNAME) -and
                $content.IndexOf($env:USERNAME, [StringComparison]::OrdinalIgnoreCase) -ge 0
            if ($hasUsername) {
                throw "Support bundle entry '$($entry.FullName)' contains the current username."
            }
            if ($content -match '(?im)\bbearer\s+(?!<redacted>)[A-Za-z0-9._~+/=-]{12,}') {
                throw "Support bundle entry '$($entry.FullName)' contains a bearer-like secret."
            }
            if ($content -match '(?im)\b(password|token|secret|authorization)\s*[:=]\s*(?!<redacted>)[A-Za-z0-9._~+/=-]{12,}') {
                throw "Support bundle entry '$($entry.FullName)' contains a credential-like value."
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

New-Item -ItemType Directory -Path $TempRoot -Force | Out-Null
Push-Location $RepoRoot
try {
    $restoreProjects = @($Solution, $DriverProject, $PigProject, $PerfProject)
    if ($Ci) {
        foreach ($project in $restoreProjects) {
            Invoke-Step "Restore with NuGet audit: $(Split-Path -Leaf $project)" {
            dotnet restore $project -p:NuGetAudit=true -p:NuGetAuditMode=all '-warnaserror:NU1900;NU1901;NU1902;NU1903;NU1904' --nologo
            }
        }
        $noRestore = @('--no-restore')
    }
    else {
        $noRestore = @()
    }

    if ($Ci) {
        Invoke-Step 'NuGet vulnerability report' {
            dotnet list $Solution package --vulnerable --include-transitive
        }
    }

    Invoke-Step "Build TabDock.sln ($Configuration)" {
        dotnet build $Solution -c $Configuration --nologo @noRestore
    }
    Invoke-Step "Build ValidationDriver ($Configuration)" {
        dotnet build $DriverProject -c $Configuration --nologo @noRestore
    }
    Invoke-Step "Build GuineaPig ($Configuration, no RID)" {
        dotnet build $PigProject -c $Configuration --nologo @noRestore
    }
    Invoke-Step "Build Performance runner ($Configuration, compile-only)" {
        dotnet build $PerfProject -c $Configuration --nologo @noRestore
    }

    Invoke-Executable "Geometry self-test ($Configuration)" $AppExe @('--selftest-geometry')
    Invoke-Executable "Diagnostics/persistence/privacy self-tests ($Configuration)" $AppExe @('--selftest-diagnostics')
    Invoke-Executable "Version smoke ($Configuration)" $AppExe @('--version')

    $doctorPath = Join-Path $TempRoot 'doctor.txt'
    Invoke-Executable "Doctor smoke ($Configuration)" $AppExe @('--doctor', '--output', $doctorPath)
    if (-not (Test-Path -LiteralPath $doctorPath -PathType Leaf)) {
        throw 'Doctor did not create its requested output file.'
    }

    $pendingRecoveryPath = Join-Path $TempRoot 'pending-recovery.txt'
    Invoke-Executable "Pending-recovery discovery smoke ($Configuration)" $AppExe @('--pending-recovery', '--output', $pendingRecoveryPath)
    if (-not (Test-Path -LiteralPath $pendingRecoveryPath -PathType Leaf)) {
        throw 'Pending-recovery discovery did not create its requested output file.'
    }

    $isolatedAppData = Join-Path $TempRoot 'isolated-appdata\Roaming'
    New-Item -ItemType Directory -Path $isolatedAppData -Force | Out-Null
    Invoke-RecoveryProcessSmoke $AppExe $isolatedAppData

    $bundlePath = Join-Path $TempRoot 'support-bundle.zip'
    Invoke-Executable "Support-bundle privacy smoke ($Configuration)" $AppExe @('--support-bundle', '--output', $bundlePath)
    if (-not (Test-Path -LiteralPath $bundlePath -PathType Leaf)) {
        throw 'Support-bundle command did not create its requested ZIP.'
    }
    Assert-SupportBundlePrivacy $bundlePath
    Write-Host 'Support-bundle ZIP entries and privacy contract: PASS' -ForegroundColor Green

    if ($Ci) {
        $env:OPENSPEC_NO_UPDATE_CHECK = '1'
        $env:OPENSPEC_TELEMETRY = '0'
        if (-not (Test-Path -LiteralPath $OpenSpecLocal -PathType Leaf)) {
            if ($null -eq (Get-Command npm -ErrorAction SilentlyContinue)) {
                throw 'Node/npm is required by -Ci to install repository-owned OpenSpec tooling.'
            }
            Invoke-Step 'Install repository-owned OpenSpec tooling' {
                Push-Location $OpenSpecRoot
                try {
                    npm ci --ignore-scripts
                }
                finally {
                    Pop-Location
                }
            }
        }
        if (-not (Test-Path -LiteralPath $OpenSpecLocal -PathType Leaf)) {
            throw "Repository-owned OpenSpec CLI was not installed: $OpenSpecLocal"
        }
        Invoke-Step 'OpenSpec validation' {
            & $OpenSpecLocal validate --all --no-interactive
        }
    }
    else {
        $openSpec = if (Test-Path -LiteralPath $OpenSpecLocal -PathType Leaf) {
            $OpenSpecLocal
        }
        else {
            $globalOpenSpec = Get-Command openspec -ErrorAction SilentlyContinue
            if ($null -ne $globalOpenSpec) { $globalOpenSpec.Source } else { $null }
        }
        if ($null -ne $openSpec) {
            Invoke-Step 'OpenSpec validation' {
                & $openSpec validate --all --no-interactive
            }
        }
        else {
            Write-Host 'OpenSpec CLI not found; skipping local spec validation.' -ForegroundColor Yellow
        }
    }

    if ($Publish) {
        $publishRoot = Join-Path $TempRoot 'publish'
        $publishArgs = @(
            'publish', $MainProject,
            '-c', 'Release',
            '-r', 'win-x64',
            '--self-contained', 'true',
            '--no-restore',
            '-o', $publishRoot,
            '-p:PublishSingleFile=true',
            '-p:PublishReadyToRun=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true'
        )
        Invoke-Step 'Publish single-file executable (Release, win-x64)' {
            dotnet @publishArgs
        }
        $publishedExe = Join-Path $publishRoot 'TabDock.exe'
        Invoke-Executable 'Published executable --version smoke' $publishedExe @('--version')
    }

    if ($Scenario) {
        Write-Host ''
        Write-Host "==> Running supervised ValidationDriver: $Scenario" -ForegroundColor Cyan
        Write-Host 'WARNING: this harness sends REAL mouse/keyboard input (SendInput).' -ForegroundColor Yellow
        Write-Host 'Do not touch the mouse or keyboard during the run.' -ForegroundColor Yellow
        & dotnet run --project $DriverProject -c $Configuration -- --yes --configuration $Configuration --rid auto $Scenario
        if ($LASTEXITCODE -ne 0) {
            throw "FAILED: ValidationDriver '$Scenario' (exit code $LASTEXITCODE)"
        }
    }

    Write-Host ''
    Write-Host 'All requested validation steps completed successfully.' -ForegroundColor Green
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $TempRoot) {
        Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
