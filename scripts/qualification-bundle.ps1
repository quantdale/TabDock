<#
.SYNOPSIS
    Versioned, offline qualification-bundle creation and verification.

.DESCRIPTION
    This file is dot-sourced by release-tooling.ps1 and is intentionally a
    data-only policy surface. It never launches TabDock, ValidationDriver,
    GuineaPig, a script, or a returned binary. Bundle paths are portable
    relative paths and every referenced file is hashed again during offline
    verification.

    A bundle is a qualification evidence record, not a release policy. The
    release policy remains in release-tooling.ps1 and may use this verifier as
    an input. Synthetic/replay evidence can be valid evidence of deterministic
    coverage, but it is explicitly ineligible for physical release gates.
#>

function Get-QualificationBundleSchemaVersion {
    return 1
}

function Get-QualificationOutcomeCodes {
    return @(
        'PASS'
        'FAIL_PRODUCT'
        'FAIL_HARNESS'
        'BLOCKED_ENVIRONMENT'
        'BLOCKED_SUPERVISED'
        'BLOCKED_CAPABILITY'
        'SKIP_CAPABILITY'
        'FLAKE_UNCLASSIFIED'
    )
}

function Get-QualificationProperty {
    param(
        [AllowNull()][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Find-QualificationDuplicateProperties {
    param(
        [Parameter(Mandatory = $true)][System.Text.Json.JsonElement]$Element,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Failures
    )
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $names = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) {
                [void]$Failures.Add("duplicate JSON property '$Path.$($property.Name)'")
            }
            Find-QualificationDuplicateProperties -Element $property.Value -Path "$Path.$($property.Name)" -Failures $Failures
        }
    }
    elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        $index = 0
        foreach ($child in $Element.EnumerateArray()) {
            Find-QualificationDuplicateProperties -Element $child -Path "$Path[$index]" -Failures $Failures
            $index++
        }
    }
}

function Read-QualificationJsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "qualification JSON artifact is missing: $Path"
    }
    $raw = [IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($raw)) {
        throw "qualification JSON artifact is empty: $Path"
    }
    $options = [System.Text.Json.JsonDocumentOptions]::new()
    $options.AllowTrailingCommas = $false
    $options.CommentHandling = [System.Text.Json.JsonCommentHandling]::Disallow
    $document = $null
    try {
        $document = [System.Text.Json.JsonDocument]::Parse($raw, $options)
        $duplicateFailures = [System.Collections.Generic.List[string]]::new()
        Find-QualificationDuplicateProperties -Element $document.RootElement -Path '$' -Failures $duplicateFailures
        $value = $raw | ConvertFrom-Json
        return [pscustomobject]@{
            Value              = $value
            Raw                = $raw
            DuplicateFailures  = @($duplicateFailures)
        }
    }
    finally {
        if ($null -ne $document) { $document.Dispose() }
    }
}

function ConvertTo-QualificationRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw 'qualification artifact path is empty'
    }
    $candidate = $Path.Replace('\', '/')
    if (($candidate.StartsWith('/', [StringComparison]::Ordinal)) -or ($candidate -match '^[A-Za-z]:')) {
        throw "qualification artifact path is absolute: '$Path'"
    }
    $segments = $candidate.Split('/', [StringSplitOptions]::None)
    if (($segments.Count -eq 0) -or (@($segments | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' }).Count -gt 0)) {
        throw "qualification artifact path contains an empty or traversal segment: '$Path'"
    }
    return ($segments -join '/')
}

function Resolve-QualificationRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )
    $normalized = ConvertTo-QualificationRelativePath $RelativePath
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $candidate = [IO.Path]::GetFullPath(
        [IO.Path]::Combine($rootFull, $normalized.Replace('/', [string][IO.Path]::DirectorySeparatorChar)))
    if (-not $candidate.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "qualification artifact path escapes bundle root: '$RelativePath'"
    }
    return [pscustomobject]@{ RelativePath = $normalized; FullPath = $candidate }
}

function Get-QualificationFileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "qualification artifact is missing for hashing: $Path"
    }
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Get-QualificationAggregateCode {
    param([Parameter(Mandatory = $true)][string[]]$Codes)
    if ($Codes.Count -eq 0) { return 'PASS' }
    $failureCodes = @('FAIL_PRODUCT', 'FAIL_HARNESS', 'FLAKE_UNCLASSIFIED')
    for ($i = 0; $i -lt $Codes.Count; $i++) {
        if ($Codes[$i] -in $failureCodes) {
            if ($Codes[$i] -in @('FAIL_PRODUCT', 'FAIL_HARNESS') -and
                @($Codes | Select-Object -Skip ($i + 1)) -contains 'PASS') {
                return 'FLAKE_UNCLASSIFIED'
            }
            return $Codes[$i]
        }
    }
    foreach ($code in @('BLOCKED_ENVIRONMENT', 'BLOCKED_SUPERVISED', 'BLOCKED_CAPABILITY', 'SKIP_CAPABILITY')) {
        if ($Codes -contains $code) { return $code }
    }
    return 'PASS'
}

function Get-QualificationManifestSummary {
    <# Returns a semantic summary and detects contradictory run-manifest data. #>
    param([Parameter(Mandatory = $true)][string]$Path)
    $failures = [System.Collections.Generic.List[string]]::new()
    try { $json = Read-QualificationJsonFile $Path }
    catch {
        [void]$failures.Add($_.Exception.Message)
        return [pscustomobject]@{ Valid = $false; Failures = @($failures); Manifest = $null }
    }
    foreach ($failure in @($json.DuplicateFailures)) { [void]$failures.Add($failure) }
    $manifest = $json.Value
    $schema = Get-QualificationProperty $manifest 'schemaVersion'
    if ($schema -ne 2) { [void]$failures.Add("run manifest schemaVersion=$schema (expected 2)") }
    $runKind = [string](Get-QualificationProperty $manifest 'runKind')
    if ($runKind -notin @('direct', 'shard', 'all', 'deterministic')) {
        [void]$failures.Add("run manifest runKind='$runKind' is unsupported")
    }
    $outcomeCodes = Get-QualificationOutcomeCodes
    $scenarioEntries = @(Get-QualificationProperty $manifest 'scenarios')
    $attempts = [System.Collections.Generic.List[object]]::new()
    $attemptKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $scenarioEntries) {
        $scenario = [string](Get-QualificationProperty $entry 'scenario')
        $attemptValue = Get-QualificationProperty $entry 'attempt'
        $attempt = 0
        if ($null -ne $attemptValue) { $attempt = [int]$attemptValue }
        $code = [string](Get-QualificationProperty $entry 'result')
        if ([string]::IsNullOrWhiteSpace($scenario) -or $attempt -lt 1) {
            [void]$failures.Add('run manifest contains an invalid scenario ID or attempt')
            continue
        }
        if ($code -notin $outcomeCodes) {
            [void]$failures.Add("scenario '$scenario' has unsupported outcome '$code'")
            continue
        }
        $key = "$scenario`#$attempt"
        if (-not $attemptKeys.Add($key)) { [void]$failures.Add("duplicate scenario attempt '$key'") }
        [void]$attempts.Add([pscustomobject]@{ Scenario = $scenario; Attempt = $attempt; Code = $code })
    }

    $aggregates = [System.Collections.Generic.List[object]]::new()
    foreach ($group in @($attempts | Group-Object Scenario)) {
        $ordered = @($group.Group | Sort-Object Attempt)
        $codes = @($ordered | ForEach-Object { [string]$_.Code })
        [void]$aggregates.Add([pscustomobject]@{
                Scenario = [string]$group.Name
                First    = $codes[0]
                Final    = Get-QualificationAggregateCode -Codes $codes
                Attempts = $ordered.Count
            })
    }
    $declaredAggregates = @(Get-QualificationProperty $manifest 'scenarioAggregates')
    $aggregateByScenario = @{}
    foreach ($aggregate in $declaredAggregates) {
        $id = [string](Get-QualificationProperty $aggregate 'scenario')
        if ([string]::IsNullOrWhiteSpace($id) -or $aggregateByScenario.ContainsKey($id)) {
            [void]$failures.Add("run manifest contains duplicate or empty scenario aggregate '$id'")
            continue
        }
        $aggregateByScenario[$id] = $aggregate
    }
    foreach ($aggregate in $aggregates) {
        if (-not $aggregateByScenario.ContainsKey($aggregate.Scenario)) {
            [void]$failures.Add("run manifest is missing scenario aggregate '$($aggregate.Scenario)'")
            continue
        }
        $declared = $aggregateByScenario[$aggregate.Scenario]
        if ([string](Get-QualificationProperty $declared 'first') -ne $aggregate.First) {
            [void]$failures.Add("scenario '$($aggregate.Scenario)' first-attempt outcome disagrees")
        }
        if ([string](Get-QualificationProperty $declared 'final') -ne $aggregate.Final) {
            [void]$failures.Add("scenario '$($aggregate.Scenario)' final outcome disagrees")
        }
    }
    foreach ($id in @($aggregateByScenario.Keys)) {
        if (-not @($aggregates | Where-Object Scenario -eq $id)) {
            [void]$failures.Add("run manifest contains an aggregate without scenario attempts '$id'")
        }
    }

    $counts = [ordered]@{}
    foreach ($code in $outcomeCodes) { $counts[$code] = @($aggregates | Where-Object Final -eq $code).Count }
    $declaredCounts = Get-QualificationProperty $manifest 'aggregateCounts'
    foreach ($code in $outcomeCodes) {
        $declared = Get-QualificationProperty $declaredCounts $code
        if ($null -eq $declared) { $declared = 0 }
        if ([int]$declared -ne [int]$counts[$code]) {
            [void]$failures.Add("run manifest aggregateCounts.$code=$declared, derived $($counts[$code])")
        }
    }
    $expectedOutcome = if ($aggregates.Count -eq 0) { 'FAIL_HARNESS' } else {
        Get-QualificationAggregateCode -Codes @($aggregates | ForEach-Object { $_.Final })
    }
    $childRecordsValue = Get-QualificationProperty $manifest 'childManifests'
    $childRecords = if ($null -eq $childRecordsValue) { @() } else { @($childRecordsValue) }
    if ($runKind -eq 'all' -and @($childRecords | Where-Object { -not [bool](Get-QualificationProperty $_ 'verified') }).Count -gt 0) {
        $expectedOutcome = 'FAIL_HARNESS'
    }
    $declaredOutcome = [string](Get-QualificationProperty $manifest 'outcome')
    if ($declaredOutcome -ne $expectedOutcome) {
        [void]$failures.Add("run manifest outcome=$declaredOutcome, derived $expectedOutcome")
    }

    $attemptCounts = Get-QualificationProperty $manifest 'attemptCounts'
    foreach ($code in $outcomeCodes) {
        $actualAttempts = @($attempts | Where-Object Code -eq $code).Count
        $declared = Get-QualificationProperty $attemptCounts $code
        if ($null -ne $declared -and [int]$declared -ne $actualAttempts) {
            [void]$failures.Add("run manifest attemptCounts.$code=$declared, derived $actualAttempts")
        }
    }
    $executable = Get-QualificationProperty $manifest 'executableSha256'
    $driver = Get-QualificationProperty $manifest 'driverIdentity'
    $started = [string](Get-QualificationProperty $manifest 'startedUtc')
    $ended = [string](Get-QualificationProperty $manifest 'endedUtc')
    $startedAt = [DateTimeOffset]::MinValue
    $endedAt = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($started, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$startedAt)) {
        [void]$failures.Add('run manifest startedUtc is malformed')
    }
    if (-not [DateTimeOffset]::TryParse($ended, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$endedAt)) {
        [void]$failures.Add('run manifest endedUtc is malformed')
    }
    if ($endedAt -lt $startedAt) { [void]$failures.Add('run manifest endedUtc precedes startedUtc') }
    if ($startedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5) -or $endedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
        [void]$failures.Add('run manifest timestamp is materially in the future')
    }
    return [pscustomobject]@{
        Valid                 = $failures.Count -eq 0
        Failures              = @($failures)
        Manifest              = $manifest
        RunKind               = $runKind
        RunId                 = [string](Get-QualificationProperty $manifest 'runId')
        SourceCommitSha       = [string](Get-QualificationProperty $manifest 'candidateSha')
        CatalogGeneration     = [string](Get-QualificationProperty $manifest 'catalogGeneration')
        CandidateExecutableSha = [string](Get-QualificationProperty $executable 'candidate')
        DriverSha             = [string](Get-QualificationProperty $driver 'sha256')
        Outcome               = $declaredOutcome
        Counts                = $counts
        ScenarioCount         = $aggregates.Count
        AttemptCount          = $attempts.Count
    }
}

function Test-QualificationPrivacyObject {
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Failures
    )
    if ($null -eq $Value) { return }
    if ($Value -is [string]) {
        if ($Value -match '(?i)(https?://|[A-Za-z]:[\\/]|(?:^|[\\/])Users(?:[\\/]|$)|%USERPROFILE%|%APPDATA%)') {
            [void]$Failures.Add("privacy-sensitive value at '$Path' is not permitted")
        }
        return
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $index = 0
        foreach ($item in $Value) {
            Test-QualificationPrivacyObject -Value $item -Path "$Path[$index]" -Failures $Failures
            $index++
        }
        return
    }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            $propertyName = [string]$key
            if ($propertyName -match '(?i)(title|url|document(text)?|raw(window|desktop)|user(path|name)|absolute(path)?|password|secret|token)') {
                [void]$Failures.Add("privacy-sensitive property '$Path.$propertyName' is not permitted")
            }
            Test-QualificationPrivacyObject -Value $Value[$key] -Path "$Path.$propertyName" -Failures $Failures
        }
        return
    }
    if ($Value -is [pscustomobject]) {
        foreach ($property in $Value.PSObject.Properties) {
            if ($property.Name -match '(?i)(title|url|document(text)?|raw(window|desktop)|user(path|name)|absolute(path)?|password|secret|token)') {
                [void]$Failures.Add("privacy-sensitive property '$Path.$($property.Name)' is not permitted")
            }
            Test-QualificationPrivacyObject -Value $property.Value -Path "$Path.$($property.Name)" -Failures $Failures
        }
        return
    }
}

function Test-QualificationBundle {
    <#
    .SYNOPSIS
        Verifies qualification-bundle.json and every referenced artifact using
        only offline parsing and hashing.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$BundlePath,
        [string]$ExpectedSourceSha = '',
        [string]$ExpectedArtifactSha = '',
        [switch]$RequirePhysicalTopology
    )
    $failures = [System.Collections.Generic.List[string]]::new()
    $bundleFile = if (Test-Path -LiteralPath $BundlePath -PathType Container) {
        Join-Path $BundlePath 'qualification-bundle.json'
    }
    else { $BundlePath }
    $root = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($bundleFile))
    try { $json = Read-QualificationJsonFile $bundleFile }
    catch {
        [void]$failures.Add($_.Exception.Message)
        return [pscustomobject]@{ Valid = $false; Failures = @($failures); Bundle = $null }
    }
    foreach ($failure in @($json.DuplicateFailures)) { [void]$failures.Add($failure) }
    $bundle = $json.Value
    if ((Get-QualificationProperty $bundle 'schemaVersion') -ne (Get-QualificationBundleSchemaVersion)) {
        [void]$failures.Add("qualification bundle schemaVersion=$((Get-QualificationProperty $bundle 'schemaVersion')) (expected $(Get-QualificationBundleSchemaVersion))")
    }
    if ([string](Get-QualificationProperty $bundle 'bundleKind') -ne 'qualification') {
        [void]$failures.Add('qualification bundle bundleKind is not qualification')
    }
    $sourceSha = [string](Get-QualificationProperty $bundle 'sourceCommitSha')
    if ($sourceSha -notmatch '^[0-9a-fA-F]{40}$') { [void]$failures.Add('qualification bundle sourceCommitSha is malformed') }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSourceSha) -and
        -not [string]::Equals($sourceSha, $ExpectedSourceSha, [StringComparison]::OrdinalIgnoreCase)) {
        [void]$failures.Add('qualification bundle sourceCommitSha does not match the expected candidate')
    }
    $created = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string](Get-QualificationProperty $bundle 'createdUtc'), [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$created)) {
        [void]$failures.Add('qualification bundle createdUtc is malformed')
    }
    elseif ($created -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
        [void]$failures.Add('qualification bundle createdUtc is materially in the future')
    }
    $semanticVersion = [string](Get-QualificationProperty $bundle 'semanticVersion')
    if ($semanticVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$') {
        [void]$failures.Add("qualification bundle semanticVersion is malformed: '$semanticVersion'")
    }

    $artifactMap = @{}
    $artifactEntries = @(Get-QualificationProperty $bundle 'artifactIndex')
    if ($artifactEntries.Count -eq 0) { [void]$failures.Add('qualification bundle artifactIndex is empty or missing') }
    foreach ($entry in $artifactEntries) {
        try { $resolved = Resolve-QualificationRelativePath -Root $root -RelativePath ([string](Get-QualificationProperty $entry 'relativePath')) }
        catch { [void]$failures.Add($_.Exception.Message); continue }
        $relative = $resolved.RelativePath
        if ($artifactMap.ContainsKey($relative)) { [void]$failures.Add("qualification bundle artifactIndex duplicates '$relative'"); continue }
        $hash = [string](Get-QualificationProperty $entry 'sha256')
        if ($hash -notmatch '^[0-9a-fA-F]{64}$') { [void]$failures.Add("artifact '$relative' has a malformed SHA-256") }
        if (-not [bool](Get-QualificationProperty $entry 'exists')) { [void]$failures.Add("artifact '$relative' is recorded as missing") }
        if (-not (Test-Path -LiteralPath $resolved.FullPath -PathType Leaf)) {
            [void]$failures.Add("qualification bundle artifact is missing: '$relative'")
        }
        else {
            $actual = Get-QualificationFileSha256 $resolved.FullPath
            if (-not [string]::Equals($actual, $hash, [StringComparison]::OrdinalIgnoreCase)) {
                [void]$failures.Add("qualification bundle artifact hash mismatch: '$relative'")
            }
        }
        $artifactMap[$relative] = [pscustomobject]@{ Entry = $entry; FullPath = $resolved.FullPath; Hash = $hash }
    }

    $candidate = Get-QualificationProperty $bundle 'candidate'
    $candidatePath = $null
    try { $candidatePath = (Resolve-QualificationRelativePath -Root $root -RelativePath ([string](Get-QualificationProperty $candidate 'artifactRelativePath'))).RelativePath }
    catch { [void]$failures.Add($_.Exception.Message) }
    $candidateSha = [string](Get-QualificationProperty $candidate 'artifactSha256')
    if ($candidateSha -notmatch '^[0-9a-fA-F]{64}$') { [void]$failures.Add('qualification bundle candidate artifactSha256 is malformed') }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedArtifactSha) -and
        -not [string]::Equals($candidateSha, $ExpectedArtifactSha, [StringComparison]::OrdinalIgnoreCase)) {
        [void]$failures.Add('qualification bundle candidate hash does not match the expected final artifact')
    }
    if ($null -ne $candidatePath -and (-not $artifactMap.ContainsKey($candidatePath))) {
        [void]$failures.Add("candidate artifact '$candidatePath' is not in artifactIndex")
    }
    elseif ($null -ne $candidatePath -and -not [string]::Equals($artifactMap[$candidatePath].Hash, $candidateSha, [StringComparison]::OrdinalIgnoreCase)) {
        [void]$failures.Add('candidate artifact index hash disagrees with bundle candidate hash')
    }

    $releaseManifestPath = $null
    try { $releaseManifestPath = (Resolve-QualificationRelativePath -Root $root -RelativePath ([string](Get-QualificationProperty $candidate 'releaseManifestRelativePath'))).RelativePath }
    catch { [void]$failures.Add($_.Exception.Message) }
    if ($null -ne $releaseManifestPath -and -not $artifactMap.ContainsKey($releaseManifestPath)) {
        [void]$failures.Add("release manifest '$releaseManifestPath' is not in artifactIndex")
    }
    $recordedReleaseSha = [string](Get-QualificationProperty $candidate 'releaseManifestSha256')
    if ($null -ne $releaseManifestPath -and $artifactMap.ContainsKey($releaseManifestPath) -and
        -not [string]::Equals($recordedReleaseSha, $artifactMap[$releaseManifestPath].Hash, [StringComparison]::OrdinalIgnoreCase)) {
        [void]$failures.Add('qualification bundle releaseManifestSha256 disagrees with the indexed release manifest')
    }
    $release = $null
    if ($null -ne $releaseManifestPath -and $artifactMap.ContainsKey($releaseManifestPath)) {
        try { $releaseJson = Read-QualificationJsonFile $artifactMap[$releaseManifestPath].FullPath; $release = $releaseJson.Value }
        catch { [void]$failures.Add($_.Exception.Message) }
        if ($null -ne $release) {
            if (-not [string]::Equals([string](Get-QualificationProperty $release 'sourceCommitSha'), $sourceSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('release manifest sourceCommitSha disagrees with qualification bundle') }
            if (-not [string]::Equals([string](Get-QualificationProperty $release 'artifactSha256'), $candidateSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('release manifest artifactSha256 disagrees with qualification bundle candidate') }
            if (-not [string]::Equals([string](Get-QualificationProperty $release 'semanticVersion'), $semanticVersion, [StringComparison]::Ordinal)) { [void]$failures.Add('release manifest semanticVersion disagrees with qualification bundle') }
        }
    }

    $driver = Get-QualificationProperty $bundle 'driver'
    $driverPath = $null
    try { $driverPath = (Resolve-QualificationRelativePath -Root $root -RelativePath ([string](Get-QualificationProperty $driver 'relativePath'))).RelativePath }
    catch { [void]$failures.Add($_.Exception.Message) }
    $driverSha = [string](Get-QualificationProperty $driver 'sha256')
    if ($driverSha -notmatch '^[0-9a-fA-F]{64}$') { [void]$failures.Add('qualification bundle driver SHA-256 is malformed') }
    if ($null -ne $driverPath -and (-not $artifactMap.ContainsKey($driverPath))) { [void]$failures.Add("driver '$driverPath' is not in artifactIndex") }
    elseif ($null -ne $driverPath -and -not [string]::Equals($artifactMap[$driverPath].Hash, $driverSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('driver artifact hash disagrees with bundle driver identity') }

    $catalogGeneration = [string](Get-QualificationProperty $bundle 'catalogGeneration')
    if ([string]::IsNullOrWhiteSpace($catalogGeneration)) { [void]$failures.Add('qualification bundle catalogGeneration is empty') }
    $schemaGeneration = Get-QualificationProperty $bundle 'schemaGeneration'
    if ([int](Get-QualificationProperty $schemaGeneration 'qualificationBundle') -ne (Get-QualificationBundleSchemaVersion)) { [void]$failures.Add('qualification bundle schemaGeneration.qualificationBundle is unsupported') }
    if ([int](Get-QualificationProperty $schemaGeneration 'runManifest') -ne 2) { [void]$failures.Add('qualification bundle schemaGeneration.runManifest is unsupported') }
    if (-not [string]::Equals([string](Get-QualificationProperty $schemaGeneration 'scenarioCatalog'), $catalogGeneration, [StringComparison]::Ordinal)) { [void]$failures.Add('qualification bundle schemaGeneration.scenarioCatalog disagrees') }
    $runEntries = @(Get-QualificationProperty $bundle 'runManifests')
    if ($runEntries.Count -eq 0) { [void]$failures.Add('qualification bundle runManifests is empty') }
    $summaries = @{}
    $runPaths = @{}
    foreach ($runEntry in $runEntries) {
        try { $relative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([string](Get-QualificationProperty $runEntry 'relativePath'))).RelativePath }
        catch { [void]$failures.Add($_.Exception.Message); continue }
        if ($runPaths.ContainsKey($relative)) { [void]$failures.Add("qualification bundle runManifests duplicates '$relative'"); continue }
        $runPaths[$relative] = $true
        if (-not $artifactMap.ContainsKey($relative)) { [void]$failures.Add("run manifest '$relative' is not in artifactIndex"); continue }
        $recordedHash = [string](Get-QualificationProperty $runEntry 'sha256')
        if (-not [string]::Equals($recordedHash, $artifactMap[$relative].Hash, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add("run manifest hash disagrees for '$relative'") }
        $summary = Get-QualificationManifestSummary $artifactMap[$relative].FullPath
        $summaries[$relative] = $summary
        foreach ($failure in @($summary.Failures)) { [void]$failures.Add("run manifest '$relative': $failure") }
        if (-not [string]::Equals($summary.SourceCommitSha, $sourceSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add("run manifest '$relative' candidate SHA disagrees") }
        if (-not [string]::Equals($summary.CatalogGeneration, $catalogGeneration, [StringComparison]::Ordinal)) { [void]$failures.Add("run manifest '$relative' catalog generation disagrees") }
        if (-not [string]::Equals($summary.CandidateExecutableSha, $candidateSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add("run manifest '$relative' candidate executable hash disagrees") }
        if (-not [string]::Equals($summary.DriverSha, $driverSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add("run manifest '$relative' driver hash disagrees") }
        if (-not [string]::Equals([string](Get-QualificationProperty $runEntry 'runKind'), $summary.RunKind, [StringComparison]::Ordinal)) { [void]$failures.Add("run manifest '$relative' runKind disagrees with bundle index") }
        if (-not [string]::Equals([string](Get-QualificationProperty $runEntry 'outcome'), $summary.Outcome, [StringComparison]::Ordinal)) { [void]$failures.Add("run manifest '$relative' outcome disagrees with bundle index") }
        if ([int](Get-QualificationProperty $runEntry 'scenarioCount') -ne $summary.ScenarioCount) { [void]$failures.Add("run manifest '$relative' scenarioCount disagrees with bundle index") }
        if ([int](Get-QualificationProperty $runEntry 'attemptCount') -ne $summary.AttemptCount) { [void]$failures.Add("run manifest '$relative' attemptCount disagrees with bundle index") }

        $manifestRoot = [IO.Path]::GetDirectoryName($artifactMap[$relative].FullPath)
        $innerIndex = @(Get-QualificationProperty $summary.Manifest 'artifactIndex')
        foreach ($inner in $innerIndex) {
            try {
                $innerResolved = Resolve-QualificationRelativePath -Root $manifestRoot -RelativePath ([string](Get-QualificationProperty $inner 'relativePath'))
                $bundleRelative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([IO.Path]::GetRelativePath($root, $innerResolved.FullPath))).RelativePath
            }
            catch { [void]$failures.Add("run manifest '$relative' contains an invalid artifact path: $($_.Exception.Message)"); continue }
            if (-not $artifactMap.ContainsKey($bundleRelative)) { [void]$failures.Add("run manifest '$relative' artifact '$bundleRelative' is absent from bundle artifactIndex") }
            elseif (-not [string]::Equals([string](Get-QualificationProperty $inner 'sha256'), $artifactMap[$bundleRelative].Hash, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add("run manifest '$relative' artifact hash disagrees for '$bundleRelative'") }
        }
    }

    $primaryPath = $null
    try { $primaryPath = (Resolve-QualificationRelativePath -Root $root -RelativePath ([string](Get-QualificationProperty $bundle 'primaryRunManifest'))).RelativePath }
    catch { [void]$failures.Add($_.Exception.Message) }
    if ($null -eq $primaryPath -or -not $summaries.ContainsKey($primaryPath)) {
        [void]$failures.Add('qualification bundle primaryRunManifest is not one of the verified run manifests')
    }
    else {
        $primary = $summaries[$primaryPath]
        $bundleOutcome = Get-QualificationProperty $bundle 'outcome'
        if ([string](Get-QualificationProperty $bundleOutcome 'overall') -ne $primary.Outcome) { [void]$failures.Add('qualification bundle outcome.overall disagrees with primary run manifest') }
        if ([int](Get-QualificationProperty $bundleOutcome 'scenarioCount') -ne $primary.ScenarioCount) { [void]$failures.Add('qualification bundle scenarioCount disagrees with primary run manifest') }
        if ([int](Get-QualificationProperty $bundleOutcome 'attemptCount') -ne $primary.AttemptCount) { [void]$failures.Add('qualification bundle attemptCount disagrees with primary run manifest') }
        $bundleCounts = Get-QualificationProperty $bundleOutcome 'scenarioCounts'
        $primaryCounts = Get-QualificationProperty $primary 'Counts'
        if ($null -eq $primaryCounts) {
            [void]$failures.Add('primary run manifest summary has no derived scenario counts')
            $primaryCounts = @{}
        }
        foreach ($code in (Get-QualificationOutcomeCodes)) {
            $declared = Get-QualificationProperty $bundleCounts $code
            if ($null -eq $declared) { $declared = 0 }
            $derived = if ($primaryCounts.Contains($code)) { $primaryCounts[$code] } else { 0 }
            if ([int]$declared -ne [int]$derived) { [void]$failures.Add("qualification bundle outcome.scenarioCounts.$code disagrees") }
        }

        $primaryManifest = $primary.Manifest
        $primaryChildrenValue = Get-QualificationProperty $primaryManifest 'childManifests'
        $primaryChildren = if ($null -eq $primaryChildrenValue) { @() } else { @($primaryChildrenValue) }
        foreach ($child in $primaryChildren) {
            $childPath = [string](Get-QualificationProperty $child 'manifestPath')
            if ([string]::IsNullOrWhiteSpace($childPath)) { [void]$failures.Add('primary run manifest child record has no manifestPath'); continue }
            try { $childRelative = (Resolve-QualificationRelativePath -Root ([IO.Path]::GetDirectoryName($artifactMap[$primaryPath].FullPath)) -RelativePath $childPath).RelativePath }
            catch { [void]$failures.Add($_.Exception.Message); continue }
            if (-not $artifactMap.ContainsKey($childRelative)) { [void]$failures.Add("primary child manifest '$childRelative' is absent from bundle artifactIndex"); continue }
            $childHash = [string](Get-QualificationProperty $child 'manifestSha256')
            if (-not [string]::Equals($childHash, $artifactMap[$childRelative].Hash, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add("primary child manifest hash disagrees for '$childRelative'") }
        }
    }

    $synthetic = [bool](Get-QualificationProperty $bundle 'syntheticTopology')
    $environment = Get-QualificationProperty $bundle 'environment'
    if ([bool](Get-QualificationProperty $environment 'syntheticTopology') -ne $synthetic) { [void]$failures.Add('bundle syntheticTopology disagrees with environment classification') }
    if ($RequirePhysicalTopology -and $synthetic) { [void]$failures.Add('syntheticTopology=true cannot satisfy a physical qualification gate') }
    $privacy = Get-QualificationProperty $bundle 'privacy'
    if ([bool](Get-QualificationProperty $privacy 'privacySafe') -ne $true -or
        [bool](Get-QualificationProperty $privacy 'containsRawDesktopData') -ne $false) {
        [void]$failures.Add('qualification bundle privacy contract is not explicitly safe')
    }
    foreach ($relative in @($artifactMap.Keys)) {
        if ($relative -notmatch '(?i)\.json$') { continue }
        try {
            $artifactJson = Read-QualificationJsonFile $artifactMap[$relative].FullPath
            $privacyFailures = [System.Collections.Generic.List[string]]::new()
            Test-QualificationPrivacyObject -Value $artifactJson.Value -Path $relative -Failures $privacyFailures
            foreach ($failure in @($privacyFailures)) { [void]$failures.Add($failure) }
        }
        catch { [void]$failures.Add("JSON artifact '$relative' is not strict JSON: $($_.Exception.Message)") }
    }
    return [pscustomobject]@{ Valid = $failures.Count -eq 0; Failures = @($failures); Bundle = $bundle }
}

function Test-QualificationEvidenceBinding {
    <# Validates a machine-produced evidence reference against the retained bundle bytes. #>
    param(
        [Parameter(Mandatory = $true)][object]$Evidence,
        [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
        [Parameter(Mandatory = $true)][string]$ExpectedSourceSha,
        [Parameter(Mandatory = $true)][string]$ExpectedArtifactSha,
        [switch]$RequirePhysicalTopology
    )
    $failures = [System.Collections.Generic.List[string]]::new()
    $reference = Get-QualificationProperty $Evidence 'qualificationBundle'
    if ($null -eq $reference) {
        [void]$failures.Add('external evidence is missing qualificationBundle binding')
        return [pscustomobject]@{ Valid = $false; Failures = @($failures); Verification = $null }
    }
    $relative = [string](Get-QualificationProperty $reference 'relativePath')
    try { $resolved = Resolve-QualificationRelativePath -Root $EvidenceDirectory -RelativePath $relative }
    catch { [void]$failures.Add($_.Exception.Message); return [pscustomobject]@{ Valid = $false; Failures = @($failures); Verification = $null } }
    if (-not (Test-Path -LiteralPath $resolved.FullPath -PathType Leaf)) {
        [void]$failures.Add("qualification bundle referenced by external evidence is missing: '$relative'")
        return [pscustomobject]@{ Valid = $false; Failures = @($failures); Verification = $null }
    }
    $actualBundleSha = Get-QualificationFileSha256 $resolved.FullPath
    $recordedBundleSha = [string](Get-QualificationProperty $reference 'sha256')
    if (-not [string]::Equals($actualBundleSha, $recordedBundleSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('qualification bundle SHA-256 disagrees with external evidence') }
    $verification = Test-QualificationBundle -BundlePath $resolved.FullPath -ExpectedSourceSha $ExpectedSourceSha -ExpectedArtifactSha $ExpectedArtifactSha -RequirePhysicalTopology:$RequirePhysicalTopology
    foreach ($failure in @($verification.Failures)) { [void]$failures.Add([string]$failure) }
    $bundle = $verification.Bundle
    if ($null -ne $bundle) {
        if (-not [string]::Equals([string](Get-QualificationProperty $reference 'sourceCommitSha'), $ExpectedSourceSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('qualification bundle evidence sourceCommitSha disagrees') }
        if (-not [string]::Equals([string](Get-QualificationProperty $reference 'candidateSha256'), $ExpectedArtifactSha, [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('qualification bundle evidence candidateSha256 disagrees') }
        if ([bool](Get-QualificationProperty $reference 'syntheticTopology') -ne [bool](Get-QualificationProperty $bundle 'syntheticTopology')) { [void]$failures.Add('qualification bundle evidence syntheticTopology disagrees') }
        if ([bool](Get-QualificationProperty $reference 'replayOnly') -ne [bool](Get-QualificationProperty $bundle 'replayOnly')) { [void]$failures.Add('qualification bundle evidence replayOnly disagrees') }
        $primaryPath = [string](Get-QualificationProperty $bundle 'primaryRunManifest')
        try {
            $primaryResolved = Resolve-QualificationRelativePath -Root ([IO.Path]::GetDirectoryName($resolved.FullPath)) -RelativePath $primaryPath
            $primarySha = Get-QualificationFileSha256 $primaryResolved.FullPath
            if (-not [string]::Equals($primarySha, [string](Get-QualificationProperty $reference 'primaryRunManifestSha256'), [StringComparison]::OrdinalIgnoreCase)) { [void]$failures.Add('qualification bundle evidence primaryRunManifestSha256 disagrees') }
        }
        catch { [void]$failures.Add("qualification bundle primary run manifest cannot be bound: $($_.Exception.Message)") }
        if ([string](Get-QualificationProperty $reference 'automatedOutcome') -ne [string](Get-QualificationProperty (Get-QualificationProperty $bundle 'outcome') 'overall')) { [void]$failures.Add('qualification bundle evidence automatedOutcome disagrees') }
    }
    if ($RequirePhysicalTopology -and ([bool](Get-QualificationProperty $reference 'syntheticTopology') -or [bool](Get-QualificationProperty $reference 'replayOnly'))) {
        [void]$failures.Add('synthetic or replay qualification evidence cannot satisfy a physical release gate')
    }
    return [pscustomobject]@{ Valid = $failures.Count -eq 0; Failures = @($failures); Verification = $verification }
}

function New-QualificationBundle {
    <# Creates a bundle from already-retained candidate and qualification files. #>
    param(
        [Parameter(Mandatory = $true)][string]$BundleRoot,
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][string]$SourceCommitSha,
        [Parameter(Mandatory = $true)][string]$SemanticVersion,
        [Parameter(Mandatory = $true)][string]$CandidateArtifactPath,
        [Parameter(Mandatory = $true)][string]$ReleaseManifestPath,
        [Parameter(Mandatory = $true)][string]$DriverPath,
        [Parameter(Mandatory = $true)][string[]]$QualificationManifestPaths,
        [string]$PrimaryRunManifestPath = '',
        [string]$StageARunId = '',
        [string]$CandidateArtifactName = '',
        [hashtable]$EnvironmentClassification = @{},
        [hashtable]$CapabilityObservations = @{},
        [switch]$SyntheticTopology,
        [switch]$ReplayOnly
    )
    if ($SourceCommitSha -notmatch '^[0-9a-fA-F]{40}$') { throw "source commit SHA is malformed: '$SourceCommitSha'" }
    if ($SemanticVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$') { throw "semantic version is malformed: '$SemanticVersion'" }
    $root = [IO.Path]::GetFullPath($BundleRoot)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "qualification bundle root is missing: $root" }
    $candidateFull = [IO.Path]::GetFullPath($CandidateArtifactPath)
    $releaseFull = [IO.Path]::GetFullPath($ReleaseManifestPath)
    $driverFull = [IO.Path]::GetFullPath($DriverPath)
    $candidateRelative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([IO.Path]::GetRelativePath($root, $candidateFull))).RelativePath
    $releaseRelative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([IO.Path]::GetRelativePath($root, $releaseFull))).RelativePath
    $driverRelative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([IO.Path]::GetRelativePath($root, $driverFull))).RelativePath
    $candidateSha = Get-QualificationFileSha256 $candidateFull
    $releaseSha = Get-QualificationFileSha256 $releaseFull
    $driverSha = Get-QualificationFileSha256 $driverFull
    $releaseJson = Read-QualificationJsonFile $releaseFull
    if (@($releaseJson.DuplicateFailures).Count -gt 0) { throw (@($releaseJson.DuplicateFailures) -join '; ') }
    $release = $releaseJson.Value
    if (-not [string]::Equals([string](Get-QualificationProperty $release 'sourceCommitSha'), $SourceCommitSha, [StringComparison]::OrdinalIgnoreCase)) { throw 'release manifest sourceCommitSha does not match bundle sourceCommitSha' }
    if (-not [string]::Equals([string](Get-QualificationProperty $release 'artifactSha256'), $candidateSha, [StringComparison]::OrdinalIgnoreCase)) { throw 'release manifest artifactSha256 does not match candidate bytes' }
    if (-not [string]::Equals([string](Get-QualificationProperty $release 'semanticVersion'), $SemanticVersion, [StringComparison]::Ordinal)) { throw 'release manifest semanticVersion does not match bundle semanticVersion' }

    $manifestFullPaths = [System.Collections.Generic.List[string]]::new()
    $seenManifests = @{}
    foreach ($inputPath in $QualificationManifestPaths) {
        $fullInput = [IO.Path]::GetFullPath($inputPath)
        $found = if (Test-Path -LiteralPath $fullInput -PathType Container) {
            @(Get-ChildItem -LiteralPath $fullInput -Recurse -File -Filter 'run-manifest.json' | ForEach-Object { $_.FullName })
        }
        else { @($fullInput) }
        foreach ($path in $found) {
            $relative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([IO.Path]::GetRelativePath($root, $path))).RelativePath
            if (-not $seenManifests.ContainsKey($relative)) {
                $seenManifests[$relative] = $true
                [void]$manifestFullPaths.Add([IO.Path]::GetFullPath($path))
            }
        }
    }
    if ($manifestFullPaths.Count -eq 0) { throw 'no run-manifest.json files were supplied for qualification bundle creation' }
    $manifestRecords = [System.Collections.Generic.List[object]]::new()
    $primaryFull = $null
    $primarySummary = $null
    foreach ($path in $manifestFullPaths) {
        $summary = Get-QualificationManifestSummary $path
        if (-not $summary.Valid) { throw "cannot create a verified bundle from '$path': $($summary.Failures -join '; ')" }
        if (-not [string]::Equals($summary.SourceCommitSha, $SourceCommitSha, [StringComparison]::OrdinalIgnoreCase)) { throw "run manifest '$path' candidate SHA disagrees" }
        $manifestRelative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([IO.Path]::GetRelativePath($root, $path))).RelativePath
        [void]$manifestRecords.Add([ordered]@{
                relativePath = $manifestRelative
                sha256 = Get-QualificationFileSha256 $path
                schemaVersion = 2
                runKind = $summary.RunKind
                runId = $summary.RunId
                candidateSha = $summary.SourceCommitSha
                candidateExecutableSha256 = $summary.CandidateExecutableSha
                driverSha256 = $summary.DriverSha
                outcome = $summary.Outcome
                scenarioCount = $summary.ScenarioCount
                attemptCount = $summary.AttemptCount
            })
        if ($summary.RunKind -eq 'all' -and $null -eq $primaryFull) { $primaryFull = $path; $primarySummary = $summary }
    }
    if ([string]::IsNullOrWhiteSpace($PrimaryRunManifestPath)) {
        if ($null -eq $primaryFull) { $primaryFull = $manifestFullPaths[0]; $primarySummary = Get-QualificationManifestSummary $primaryFull }
    }
    else {
        $primaryFull = [IO.Path]::GetFullPath($PrimaryRunManifestPath)
        $primaryRelative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([IO.Path]::GetRelativePath($root, $primaryFull))).RelativePath
        $primaryRecord = @($manifestRecords | Where-Object { $_.relativePath -eq $primaryRelative })
        if ($primaryRecord.Count -ne 1) { throw "primary run manifest is not in the supplied manifest set: $PrimaryRunManifestPath" }
        $primarySummary = Get-QualificationManifestSummary $primaryFull
    }
    $primaryRelativeFinal = (Resolve-QualificationRelativePath -Root $root -RelativePath ([IO.Path]::GetRelativePath($root, $primaryFull))).RelativePath
    $catalogGeneration = [string]$primarySummary.CatalogGeneration
    if ([string]::IsNullOrWhiteSpace($catalogGeneration)) { throw 'primary run manifest has no catalog generation' }

    $artifactPaths = @{}
    $artifactPaths[$candidateRelative] = $candidateFull
    $artifactPaths[$releaseRelative] = $releaseFull
    $artifactPaths[$driverRelative] = $driverFull
    foreach ($path in $manifestFullPaths) {
        $manifestDirectory = [IO.Path]::GetDirectoryName($path)
        foreach ($file in @(Get-ChildItem -LiteralPath $manifestDirectory -Recurse -File)) {
            $relative = (Resolve-QualificationRelativePath -Root $root -RelativePath ([IO.Path]::GetRelativePath($root, $file.FullName))).RelativePath
            $artifactPaths[$relative] = $file.FullName
        }
    }
    $artifactIndex = [System.Collections.Generic.List[object]]::new()
    foreach ($relative in @($artifactPaths.Keys | Sort-Object)) {
        $path = $artifactPaths[$relative]
        $kind = if ($relative -eq $candidateRelative) { 'candidate-executable' }
                elseif ($relative -eq $releaseRelative) { 'release-manifest' }
                elseif ($relative -eq $driverRelative) { 'validation-driver' }
                elseif ($relative -match '(?i)\.junit\.xml$') { 'junit' }
                elseif ($relative -match '(?i)\.timeline\.json$') { 'timeline' }
                elseif ($relative -match '(?i)\.json$') { 'qualification-json' }
                else { 'qualification-artifact' }
        [void]$artifactIndex.Add([ordered]@{
                relativePath = $relative
                kind = $kind
                sha256 = Get-QualificationFileSha256 $path
                exists = $true
                sizeBytes = ([IO.FileInfo]$path).Length
            })
    }
    $environment = [ordered]@{
        os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        syntheticTopology = [bool]$SyntheticTopology
        replayOnly = [bool]$ReplayOnly
    }
    foreach ($key in $EnvironmentClassification.Keys) { $environment[$key] = $EnvironmentClassification[$key] }
    $bundle = [ordered]@{
        schemaVersion = Get-QualificationBundleSchemaVersion
        bundleKind = 'qualification'
        bundleId = [Guid]::NewGuid().ToString('D')
        createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
        sourceCommitSha = $SourceCommitSha.ToLowerInvariant()
        semanticVersion = $SemanticVersion
        catalogGeneration = $catalogGeneration
        schemaGeneration = [ordered]@{ qualificationBundle = Get-QualificationBundleSchemaVersion; runManifest = 2; scenarioCatalog = $catalogGeneration }
        candidate = [ordered]@{
            artifactRelativePath = $candidateRelative
            artifactSha256 = $candidateSha
            releaseManifestRelativePath = $releaseRelative
            releaseManifestSha256 = $releaseSha
            stageARunId = $StageARunId
            candidateArtifactName = $CandidateArtifactName
        }
        driver = [ordered]@{ relativePath = $driverRelative; sha256 = $driverSha; fileName = [IO.Path]::GetFileName($driverFull) }
        primaryRunManifest = $primaryRelativeFinal
        runManifests = @($manifestRecords)
        artifactIndex = @($artifactIndex)
        outcome = [ordered]@{
            overall = $primarySummary.Outcome
            scenarioCounts = $primarySummary.Counts
            scenarioCount = $primarySummary.ScenarioCount
            attemptCount = $primarySummary.AttemptCount
        }
        capabilityObservations = $CapabilityObservations
        environment = $environment
        syntheticTopology = [bool]$SyntheticTopology
        replayOnly = [bool]$ReplayOnly
        evidenceClass = if ($ReplayOnly) { 'replay-only' } elseif ($SyntheticTopology) { 'synthetic-deterministic' } else { 'candidate-qualification' }
        privacy = [ordered]@{
            privacySafe = $true
            containsRawDesktopData = $false
            containsTitles = $false
            containsUrls = $false
            containsUserPaths = $false
        }
    }
    $outputFull = [IO.Path]::GetFullPath($OutputPath)
    $outputRelative = [IO.Path]::GetRelativePath($root, $outputFull)
    if ($outputRelative -match '^[.][.](?:[\\/]|$)' -or $outputRelative.Contains([IO.Path]::DirectorySeparatorChar) -or $outputRelative.Contains([IO.Path]::AltDirectorySeparatorChar)) {
        throw 'qualification-bundle.json must be written at the BundleRoot top level so all artifact paths remain portable and traversal-free'
    }
    $outputDirectory = [IO.Path]::GetDirectoryName($outputFull)
    if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) { New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null }
    [IO.File]::WriteAllText($outputFull, ($bundle | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
    $verification = Test-QualificationBundle -BundlePath $outputFull -ExpectedSourceSha $SourceCommitSha -ExpectedArtifactSha $candidateSha
    if (-not $verification.Valid) { throw "created qualification bundle failed offline verification: $($verification.Failures -join '; ')" }
    return [pscustomobject]@{
        BundlePath = $outputFull
        BundleSha256 = Get-QualificationFileSha256 $outputFull
        CandidateSha256 = $candidateSha
        PrimaryRunManifest = $primaryRelativeFinal
        Verification = $verification
    }
}
