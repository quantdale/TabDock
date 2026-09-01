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
        # Environment placeholders and the bounded validation-artifact token
        # are intentional redactions emitted by the driver, not personal paths.
        # Reject concrete drive/user paths and URLs while allowing those
        # portable markers to survive in evidence.
        $hasConcretePath = $Value -match '(?i)(https?://|[A-Za-z]:[\\/]|(?:^|[\\/])Users(?:[\\/]|$))'
        $hasRedactionMarker = $Value -match '(?i)(%USERPROFILE%|%APPDATA%|%LOCALAPPDATA%|<validation-artifact>)'
        if ($hasConcretePath -and -not $hasRedactionMarker) {
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
            $privacyContractProperty = $propertyName -in @('privacySafe', 'containsRawDesktopData', 'containsTitles', 'containsUrls', 'containsUserPaths')
            if (-not $privacyContractProperty -and $propertyName -match '(?i)(title|url|document(text)?|raw(window|desktop)|user(path|name)|absolute(path)?|password|secret|token)') {
                [void]$Failures.Add("privacy-sensitive property '$Path.$propertyName' is not permitted")
            }
            Test-QualificationPrivacyObject -Value $Value[$key] -Path "$Path.$propertyName" -Failures $Failures
        }
        return
    }
    if ($Value -is [pscustomobject]) {
        foreach ($property in $Value.PSObject.Properties) {
            $privacyContractProperty = $property.Name -in @('privacySafe', 'containsRawDesktopData', 'containsTitles', 'containsUrls', 'containsUserPaths')
            if (-not $privacyContractProperty -and $property.Name -match '(?i)(title|url|document(text)?|raw(window|desktop)|user(path|name)|absolute(path)?|password|secret|token)') {
                [void]$Failures.Add("privacy-sensitive property '$Path.$($property.Name)' is not permitted")
            }
            Test-QualificationPrivacyObject -Value $property.Value -Path "$Path.$($property.Name)" -Failures $Failures
        }
        return
    }
}


function Get-QualificationRequiredVisualArray {
    param(
        [AllowNull()][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Failures
    )
    if ($null -eq $Object) {
        [void]$Failures.Add("$Path is missing because its parent object is null")
        return @()
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        [void]$Failures.Add("$Path is missing")
        return @()
    }
    if ($null -eq $property.Value) {
        [void]$Failures.Add("$Path is explicitly null")
        return @()
    }
    if ($property.Value -is [string] -or -not ($property.Value -is [System.Collections.IEnumerable])) {
        [void]$Failures.Add("$Path must be a JSON array")
        return @()
    }
    return @($property.Value)
}

function Test-QualificationVisualSha256 {
    param([AllowNull()][object]$Value)
    return ([string]$Value) -match '^[0-9a-fA-F]{64}$'
}
function Test-QualificationVisualManifest {
    <# Verifies declared visual evidence without invoking a model or trusting metadata alone. #>
    param(
        [Parameter(Mandatory = $true)][object]$RunManifest,
        [Parameter(Mandatory = $true)][string]$RunManifestPath,
        [Parameter(Mandatory = $true)][string]$BundleRoot,
        [Parameter(Mandatory = $true)][hashtable]$ArtifactMap,
        [Parameter(Mandatory = $true)][object]$ScenarioEntry,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Failures
    )
    $visualPath = [string](Get-QualificationProperty $ScenarioEntry 'visualManifestArtifact')
    $visualHash = [string](Get-QualificationProperty $ScenarioEntry 'visualManifestSha256')
    if ([string]::IsNullOrWhiteSpace($visualPath) -and [string]::IsNullOrWhiteSpace($visualHash)) {
        return
    }
    if ([string]::IsNullOrWhiteSpace($visualPath) -or -not (Test-QualificationVisualSha256 $visualHash)) {
        [void]$Failures.Add('visual manifest link is missing a normalized path or SHA-256')
        return
    }
    $manifestRoot = [IO.Path]::GetDirectoryName($RunManifestPath)
    $resolved = $null
    $bundleRelative = $null
    try {
        $resolved = Resolve-QualificationRelativePath -Root $manifestRoot -RelativePath $visualPath
        $bundleRelative = (Resolve-QualificationRelativePath -Root $BundleRoot -RelativePath ([IO.Path]::GetRelativePath($BundleRoot, $resolved.FullPath))).RelativePath
    }
    catch {
        [void]$Failures.Add("visual manifest path is invalid: $($_.Exception.Message)")
        return
    }
    if (-not $ArtifactMap.ContainsKey($bundleRelative)) {
        [void]$Failures.Add("visual manifest '$visualPath' is absent from bundle artifactIndex")
        return
    }
    if (-not [string]::Equals($ArtifactMap[$bundleRelative].Hash, $visualHash, [StringComparison]::OrdinalIgnoreCase)) {
        [void]$Failures.Add("visual manifest hash disagrees with scenario link: '$visualPath'")
    }
    if (-not (Test-Path -LiteralPath $resolved.FullPath -PathType Leaf)) {
        [void]$Failures.Add("visual manifest is missing: '$visualPath'")
        return
    }
    try {
        $json = Read-QualificationJsonFile $resolved.FullPath
        foreach ($failure in @($json.DuplicateFailures)) { [void]$Failures.Add("visual manifest '$visualPath': $failure") }
        $manifest = $json.Value
        if ([string](Get-QualificationProperty $manifest 'schema') -ne 'tabdock-visual-manifest-v1') {
            [void]$Failures.Add("visual manifest '$visualPath' has an unsupported schema")
        }
        if ([int](Get-QualificationProperty $manifest 'schemaVersion') -ne 1) {
            [void]$Failures.Add("visual manifest '$visualPath' has an unsupported schemaVersion")
        }
        $expectedCandidate = [string](Get-QualificationProperty $RunManifest 'candidateSha')
        $expectedRunId = [string](Get-QualificationProperty $RunManifest 'runId')
        $expectedScenario = [string](Get-QualificationProperty $ScenarioEntry 'scenario')
        $expectedAttempt = [int](Get-QualificationProperty $ScenarioEntry 'attempt')
        if (-not [string]::Equals([string](Get-QualificationProperty $manifest 'candidateSha'), $expectedCandidate, [StringComparison]::Ordinal)) {
            [void]$Failures.Add("visual manifest '$visualPath' candidate binding disagrees")
        }
        if (-not [string]::Equals([string](Get-QualificationProperty $manifest 'runId'), $expectedRunId, [StringComparison]::Ordinal) `
            -or -not [string]::Equals([string](Get-QualificationProperty $manifest 'scenario'), $expectedScenario, [StringComparison]::Ordinal) `
            -or [int](Get-QualificationProperty $manifest 'attempt') -ne $expectedAttempt) {
            [void]$Failures.Add("visual manifest '$visualPath' run/scenario/attempt binding disagrees")
        }

        $manifestArtifacts = @(Get-QualificationRequiredVisualArray `
                -Object $manifest `
                -Name 'artifacts' `
                -Path "visual manifest '$visualPath'.artifacts" `
                -Failures $Failures)
        $manifestUnavailable = @(Get-QualificationRequiredVisualArray `
                -Object $manifest `
                -Name 'unavailable' `
                -Path "visual manifest '$visualPath'.unavailable" `
                -Failures $Failures)
        $manifestFailures = @(Get-QualificationRequiredVisualArray `
                -Object $manifest `
                -Name 'derivedArtifactFailures' `
                -Path "visual manifest '$visualPath'.derivedArtifactFailures" `
                -Failures $Failures)
        $artifactIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $artifactPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $artifactById = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        foreach ($artifact in $manifestArtifacts) {
            $artifactId = [string](Get-QualificationProperty $artifact 'artifactId')
            $artifactRelative = [string](Get-QualificationProperty $artifact 'relativePath')
            $artifactHash = [string](Get-QualificationProperty $artifact 'sha256')
            if ([string]::IsNullOrWhiteSpace($artifactId) -or -not $artifactIds.Add($artifactId)) {
                [void]$Failures.Add("visual manifest '$visualPath' contains a duplicate or empty artifact ID")
            }
            if ([string]::IsNullOrWhiteSpace($artifactRelative) -or -not $artifactPaths.Add($artifactRelative)) {
                [void]$Failures.Add("visual manifest '$visualPath' contains a duplicate or empty artifact path")
                continue
            }
            if (-not [string]::IsNullOrWhiteSpace($artifactId) -and -not $artifactById.ContainsKey($artifactId)) {
                $artifactById[$artifactId] = $artifact
            }
            if (-not (Test-QualificationVisualSha256 $artifactHash)) {
                [void]$Failures.Add("visual artifact '$artifactId' has a malformed SHA-256")
                continue
            }
            $artifactResolved = $null
            $artifactBundleRelative = $null
            try {
                $artifactResolved = Resolve-QualificationRelativePath -Root $manifestRoot -RelativePath $artifactRelative
                $artifactBundleRelative = (Resolve-QualificationRelativePath -Root $BundleRoot -RelativePath ([IO.Path]::GetRelativePath($BundleRoot, $artifactResolved.FullPath))).RelativePath
            }
            catch {
                [void]$Failures.Add("visual artifact '$artifactId' path is invalid: $($_.Exception.Message)")
                continue
            }
            if (-not $ArtifactMap.ContainsKey($artifactBundleRelative)) {
                [void]$Failures.Add("visual artifact '$artifactId' is absent from bundle artifactIndex")
            }
            elseif (-not [string]::Equals($ArtifactMap[$artifactBundleRelative].Hash, $artifactHash, [StringComparison]::OrdinalIgnoreCase)) {
                [void]$Failures.Add("visual artifact '$artifactId' hash disagrees with bundle artifactIndex")
            }
            if (-not (Test-Path -LiteralPath $artifactResolved.FullPath -PathType Leaf)) {
                [void]$Failures.Add("visual artifact '$artifactId' is missing")
            }
            else {
                $actualArtifactHash = Get-QualificationFileSha256 $artifactResolved.FullPath
                if (-not [string]::Equals($actualArtifactHash, $artifactHash, [StringComparison]::OrdinalIgnoreCase)) {
                    [void]$Failures.Add("visual artifact '$artifactId' bytes do not match declared SHA-256")
                }
            }
            if ([string](Get-QualificationProperty $artifact 'mimeType') -ne 'image/png') {
                [void]$Failures.Add("visual artifact '$artifactId' is not declared as image/png")
            }
            if ([int](Get-QualificationProperty $artifact 'width') -le 0 -or [int](Get-QualificationProperty $artifact 'height') -le 0) {
                [void]$Failures.Add("visual artifact '$artifactId' has invalid dimensions")
            }
        }
        foreach ($artifact in $manifestArtifacts) {
            if (-not [bool](Get-QualificationProperty $artifact 'derived')) { continue }
            $sourceArtifactId = [string](Get-QualificationProperty $artifact 'sourceArtifactId')
            if ([string]::IsNullOrWhiteSpace($sourceArtifactId)) {
                [void]$Failures.Add("derived visual artifact '$([string](Get-QualificationProperty $artifact 'artifactId'))' has no sourceArtifactId")
            }
            elseif (-not $artifactById.ContainsKey($sourceArtifactId)) {
                [void]$Failures.Add("derived visual artifact '$([string](Get-QualificationProperty $artifact 'artifactId'))' references unknown source '$sourceArtifactId'")
            }
        }
        $scenarioResultPath = [string](Get-QualificationProperty $ScenarioEntry 'jsonArtifact')
        if ([string]::IsNullOrWhiteSpace($scenarioResultPath)) {
            [void]$Failures.Add("visual scenario '$expectedScenario' has no scenario-result artifact")
        }
        else {
            try {
                $scenarioResultResolved = Resolve-QualificationRelativePath -Root $manifestRoot -RelativePath $scenarioResultPath
                $scenarioResultBundleRelative = (Resolve-QualificationRelativePath -Root $BundleRoot -RelativePath ([IO.Path]::GetRelativePath($BundleRoot, $scenarioResultResolved.FullPath))).RelativePath
                if (-not $ArtifactMap.ContainsKey($scenarioResultBundleRelative)) {
                    [void]$Failures.Add("visual scenario result '$scenarioResultPath' is absent from bundle artifactIndex")
                }
                if (-not (Test-Path -LiteralPath $scenarioResultResolved.FullPath -PathType Leaf)) {
                    [void]$Failures.Add("visual scenario result '$scenarioResultPath' is missing")
                }
                else {
                    $scenarioResultJson = Read-QualificationJsonFile $scenarioResultResolved.FullPath
                    foreach ($failure in @($scenarioResultJson.DuplicateFailures)) { [void]$Failures.Add("visual scenario result '$scenarioResultPath': $failure") }
                    $scenarioResult = $scenarioResultJson.Value
                    $scenarioVisualEvidence = Get-QualificationProperty $scenarioResult 'visualEvidence'
                    if ($null -eq $scenarioVisualEvidence) {
                        [void]$Failures.Add("visual scenario result '$scenarioResultPath' has no visualEvidence hierarchy")
                    }
                    else {
                        $scenarioVisualManifestPath = [string](Get-QualificationProperty $scenarioVisualEvidence 'manifestArtifact')
                        $scenarioVisualManifestHash = [string](Get-QualificationProperty $scenarioVisualEvidence 'manifestSha256')
                        if (-not [string]::Equals($scenarioVisualManifestPath, $visualPath, [StringComparison]::Ordinal) `
                            -or -not [string]::Equals($scenarioVisualManifestHash, $visualHash, [StringComparison]::OrdinalIgnoreCase)) {
                            [void]$Failures.Add("visual scenario result '$scenarioResultPath' manifest binding disagrees")
                        }
                        $scenarioFailureCountProperty = $scenarioVisualEvidence.PSObject.Properties['derivedArtifactFailureCount']
                        if ($null -eq $scenarioFailureCountProperty) {
                            [void]$Failures.Add("visual scenario result '$scenarioResultPath' has no derived-failure count")
                        }
                        elseif ([int]$scenarioFailureCountProperty.Value -ne $manifestFailures.Count) {
                            [void]$Failures.Add("visual scenario result '$scenarioResultPath' derived-failure count disagrees")
                        }
                        $scenarioFailureIds = @(Get-QualificationRequiredVisualArray `
                                -Object $scenarioVisualEvidence `
                                -Name 'derivedArtifactFailureIds' `
                                -Path "visual scenario result '$scenarioResultPath'.visualEvidence.derivedArtifactFailureIds" `
                                -Failures $Failures)
                        $manifestFailureIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                        foreach ($failure in $manifestFailures) {
                            [void]$manifestFailureIds.Add([string](Get-QualificationProperty $failure 'failureId'))
                        }
                        $scenarioFailureIdSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                        foreach ($failureId in $scenarioFailureIds) {
                            $failureText = [string]$failureId
                            if (-not $scenarioFailureIdSet.Add($failureText) -or -not $manifestFailureIds.Contains($failureText)) {
                                [void]$Failures.Add("visual scenario result '$scenarioResultPath' derived-failure IDs disagree")
                            }
                        }
                        foreach ($failureId in $manifestFailureIds) {
                            if (-not $scenarioFailureIdSet.Contains($failureId)) {
                                [void]$Failures.Add("visual scenario result '$scenarioResultPath' omits derived-failure '$failureId'")
                            }
                        }
                    }
                }
            }
            catch {
                [void]$Failures.Add("visual scenario result '$scenarioResultPath' could not be verified: $($_.Exception.Message)")
            }
        }

        $manifestFailureById = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        foreach ($failure in $manifestFailures) {
            $failurePath = "visual manifest '$visualPath'.derivedArtifactFailures"
            $failureId = [string](Get-QualificationProperty $failure 'failureId')
            $artifactKind = [string](Get-QualificationProperty $failure 'artifactKind')
            $artifactId = [string](Get-QualificationProperty $failure 'artifactId')
            $checkpointId = [string](Get-QualificationProperty $failure 'checkpointId')
            $failureScenario = [string](Get-QualificationProperty $failure 'scenario')
            $failureAttemptValue = Get-QualificationProperty $failure 'attempt'
            $failureAttempt = 0
            try { $failureAttempt = [int]$failureAttemptValue } catch { $failureAttempt = 0 }
            $failureReason = [string](Get-QualificationProperty $failure 'reason')
            $requiredness = [string](Get-QualificationProperty $failure 'requiredness')
            $rawProperty = $failure.PSObject.Properties['rawArtifactsPreserved']
            $recordedUtc = [string](Get-QualificationProperty $failure 'recordedUtc')
            if ([string]::IsNullOrWhiteSpace($failureId) -or $failureId -notmatch '^[a-z][a-z0-9._-]{1,127}$') {
                [void]$Failures.Add("$failurePath contains an empty or unstable failureId")
            }
            elseif ($manifestFailureById.ContainsKey($failureId)) {
                [void]$Failures.Add("$failurePath contains duplicate failureId '$failureId'")
            }
            if ([string]::IsNullOrWhiteSpace($artifactKind) -or $artifactKind.Length -gt 100) {
                [void]$Failures.Add("derived artifact failure '$failureId' has an invalid artifactKind")
            }
            if ([string]::IsNullOrWhiteSpace($artifactId) -or $artifactId.Length -gt 160) {
                [void]$Failures.Add("derived artifact failure '$failureId' has an invalid artifactId")
            }
            if ([string]::IsNullOrWhiteSpace($checkpointId) -or $checkpointId.Length -gt 100) {
                [void]$Failures.Add("derived artifact failure '$failureId' has an invalid checkpointId")
            }
            if (-not [string]::Equals($failureScenario, $expectedScenario, [StringComparison]::Ordinal) -or $failureAttempt -ne $expectedAttempt) {
                [void]$Failures.Add("derived artifact failure '$failureId' identity disagrees")
            }
            if ([string]::IsNullOrWhiteSpace($failureReason) -or $failureReason.Length -gt 500) {
                [void]$Failures.Add("derived artifact failure '$failureId' has an empty or oversized reason")
            }
            if ($requiredness -notin @('REQUIRED', 'BEST_EFFORT')) {
                [void]$Failures.Add("derived artifact failure '$failureId' has unsupported requiredness '$requiredness'")
            }
            if ($null -eq $rawProperty -or $rawProperty.Value -isnot [bool]) {
                [void]$Failures.Add("derived artifact failure '$failureId' has no boolean raw-preservation state")
            }
            $sourceIds = @(Get-QualificationRequiredVisualArray `
                    -Object $failure `
                    -Name 'sourceArtifactIds' `
                    -Path "$failurePath[$failureId].sourceArtifactIds" `
                    -Failures $Failures)
            $sourceSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($sourceId in $sourceIds) {
                $sourceText = [string]$sourceId
                if ([string]::IsNullOrWhiteSpace($sourceText) -or -not $sourceSet.Add($sourceText)) {
                    [void]$Failures.Add("derived artifact failure '$failureId' has duplicate or empty source artifact IDs")
                }
                elseif (-not $artifactById.ContainsKey($sourceText)) {
                    [void]$Failures.Add("derived artifact failure '$failureId' references unknown source '$sourceText'")
                }
                elseif ([bool](Get-QualificationProperty $artifactById[$sourceText] 'derived')) {
                    [void]$Failures.Add("derived artifact failure '$failureId' references a derived artifact instead of raw evidence")
                }
            }
            if ($rawProperty -isnot $null -and [bool]$rawProperty.Value -and $sourceIds.Count -eq 0) {
                [void]$Failures.Add("derived artifact failure '$failureId' claims raw preservation without source artifacts")
            }
            $recorded = [DateTimeOffset]::MinValue
            if (-not [DateTimeOffset]::TryParse($recordedUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$recorded)) {
                [void]$Failures.Add("derived artifact failure '$failureId' has an invalid recordedUtc")
            }
            if (-not [string]::IsNullOrWhiteSpace($failureId) -and -not $manifestFailureById.ContainsKey($failureId)) {
                $manifestFailureById[$failureId] = $failure
            }
        }

        $reviewPacketPath = [string](Get-QualificationProperty $manifest 'reviewPacketPath')
        $reviewPacketHash = [string](Get-QualificationProperty $manifest 'reviewPacketSha256')
        $scenarioPacketPath = [string](Get-QualificationProperty $ScenarioEntry 'visualReviewPacketArtifact')
        $scenarioPacketHash = [string](Get-QualificationProperty $ScenarioEntry 'visualReviewPacketSha256')
        $scenarioInstructionsPath = [string](Get-QualificationProperty $ScenarioEntry 'visualReviewInstructionsArtifact')
        $scenarioReviewResultPath = [string](Get-QualificationProperty $ScenarioEntry 'visualReviewResultArtifact')
        if (-not [string]::IsNullOrWhiteSpace($reviewPacketPath) -or -not [string]::IsNullOrWhiteSpace($reviewPacketHash)) {
            if ([string]::IsNullOrWhiteSpace($reviewPacketPath) -or -not (Test-QualificationVisualSha256 $reviewPacketHash)) {
                [void]$Failures.Add("visual manifest '$visualPath' review-packet link is incomplete")
            }
            else {
                if (-not [string]::IsNullOrWhiteSpace($scenarioPacketPath) -and `
                    -not [string]::Equals($scenarioPacketPath, $reviewPacketPath, [StringComparison]::Ordinal)) {
                    [void]$Failures.Add("visual review packet path disagrees between scenario and visual manifest")
                }
                if (-not [string]::IsNullOrWhiteSpace($scenarioPacketHash) -and `
                    -not [string]::Equals($scenarioPacketHash, $reviewPacketHash, [StringComparison]::OrdinalIgnoreCase)) {
                    [void]$Failures.Add("visual review packet hash disagrees between scenario and visual manifest")
                }
                if ([string]::IsNullOrWhiteSpace($scenarioInstructionsPath)) {
                    [void]$Failures.Add("visual review packet '$reviewPacketPath' has no instructions artifact link")
                }
                else {
                    try {
                        $instructionsResolved = Resolve-QualificationRelativePath -Root $manifestRoot -RelativePath $scenarioInstructionsPath
                        $instructionsBundleRelative = (Resolve-QualificationRelativePath -Root $BundleRoot -RelativePath ([IO.Path]::GetRelativePath($BundleRoot, $instructionsResolved.FullPath))).RelativePath
                        if (-not $ArtifactMap.ContainsKey($instructionsBundleRelative)) {
                            [void]$Failures.Add("visual review instructions '$scenarioInstructionsPath' is absent from bundle artifactIndex")
                        }
                        if (-not (Test-Path -LiteralPath $instructionsResolved.FullPath -PathType Leaf)) {
                            [void]$Failures.Add("visual review instructions '$scenarioInstructionsPath' is missing")
                        }
                    }
                    catch {
                        [void]$Failures.Add("visual review instructions path is invalid: $($_.Exception.Message)")
                    }
                }
                try {
                    $packetResolved = Resolve-QualificationRelativePath -Root $manifestRoot -RelativePath $reviewPacketPath
                    $packetBundleRelative = (Resolve-QualificationRelativePath -Root $BundleRoot -RelativePath ([IO.Path]::GetRelativePath($BundleRoot, $packetResolved.FullPath))).RelativePath
                    if (-not $ArtifactMap.ContainsKey($packetBundleRelative)) {
                        [void]$Failures.Add("visual review packet '$reviewPacketPath' is absent from bundle artifactIndex")
                    }
                    elseif (-not [string]::Equals($ArtifactMap[$packetBundleRelative].Hash, $reviewPacketHash, [StringComparison]::OrdinalIgnoreCase)) {
                        [void]$Failures.Add("visual review packet hash disagrees with visual manifest")
                    }
                    if (-not (Test-Path -LiteralPath $packetResolved.FullPath -PathType Leaf)) {
                        [void]$Failures.Add("visual review packet '$reviewPacketPath' is missing")
                    }
                    else {
                        $actualPacketHash = Get-QualificationFileSha256 $packetResolved.FullPath
                        if (-not [string]::Equals($actualPacketHash, $reviewPacketHash, [StringComparison]::OrdinalIgnoreCase)) {
                            [void]$Failures.Add("visual review packet bytes do not match the visual manifest SHA-256")
                        }
                        $packetJson = Read-QualificationJsonFile $packetResolved.FullPath
                        foreach ($failure in @($packetJson.DuplicateFailures)) { [void]$Failures.Add("visual review packet '$reviewPacketPath': $failure") }
                        $packet = $packetJson.Value
                        if ([string](Get-QualificationProperty $packet 'schema') -ne 'tabdock-visual-review-packet-v1' `
                            -or [int](Get-QualificationProperty $packet 'schemaVersion') -ne 1) {
                            [void]$Failures.Add("visual review packet '$reviewPacketPath' schema is unsupported")
                        }
                        if (-not [string]::Equals([string](Get-QualificationProperty $packet 'candidateSha'), $expectedCandidate, [StringComparison]::Ordinal) `
                            -or -not [string]::Equals([string](Get-QualificationProperty $packet 'runId'), $expectedRunId, [StringComparison]::Ordinal) `
                            -or -not [string]::Equals([string](Get-QualificationProperty $packet 'scenario'), $expectedScenario, [StringComparison]::Ordinal) `
                            -or [int](Get-QualificationProperty $packet 'attempt') -ne $expectedAttempt) {
                            [void]$Failures.Add("visual review packet '$reviewPacketPath' identity disagrees")
                        }
                        $expectedPacketPath = "visual/$expectedScenario/attempt-$("{0:D3}" -f $expectedAttempt)/review/visual-review-manifest.json"
                        if (-not [string]::Equals([string](Get-QualificationProperty $packet 'visualManifestPath'), $visualPath, [StringComparison]::Ordinal)) {
                            [void]$Failures.Add("visual review packet '$reviewPacketPath' visual-manifest path disagrees")
                        }
                        $packetCheckpoints = @(Get-QualificationRequiredVisualArray `
                                -Object $packet `
                                -Name 'checkpoints' `
                                -Path "visual review packet '$reviewPacketPath'.checkpoints" `
                                -Failures $Failures)
                        $packetImages = @(Get-QualificationRequiredVisualArray `
                                -Object $packet `
                                -Name 'images' `
                                -Path "visual review packet '$reviewPacketPath'.images" `
                                -Failures $Failures)
                        $packetEnvironmentNotes = @(Get-QualificationRequiredVisualArray `
                                -Object $packet `
                                -Name 'environmentNotes' `
                                -Path "visual review packet '$reviewPacketPath'.environmentNotes" `
                                -Failures $Failures)
                        $packetProhibited = @(Get-QualificationRequiredVisualArray `
                                -Object $packet `
                                -Name 'prohibitedInferenceReminders' `
                                -Path "visual review packet '$reviewPacketPath'.prohibitedInferenceReminders" `
                                -Failures $Failures)
                        $packetFailures = @(Get-QualificationRequiredVisualArray `
                                -Object $packet `
                                -Name 'derivedArtifactFailures' `
                                -Path "visual review packet '$reviewPacketPath'.derivedArtifactFailures" `
                                -Failures $Failures)
                        $packetImageById = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
                        foreach ($image in $packetImages) {
                            $imageId = [string](Get-QualificationProperty $image 'artifactId')
                            $imageCheckpoint = [string](Get-QualificationProperty $image 'checkpointId')
                            $imagePath = [string](Get-QualificationProperty $image 'relativePath')
                            $imageHash = [string](Get-QualificationProperty $image 'sha256')
                            if ([string]::IsNullOrWhiteSpace($imageId) -or $packetImageById.ContainsKey($imageId)) {
                                [void]$Failures.Add("visual review packet '$reviewPacketPath' contains a duplicate or empty image artifact ID")
                                continue
                            }
                            $packetImageById[$imageId] = $image
                            if (-not $artifactById.ContainsKey($imageId)) {
                                [void]$Failures.Add("visual review image '$imageId' is absent from the visual manifest")
                                continue
                            }
                            $manifestImage = $artifactById[$imageId]
                            if ([bool](Get-QualificationProperty $manifestImage 'derived') `
                                -or -not [string]::Equals([string](Get-QualificationProperty $manifestImage 'checkpointId'), $imageCheckpoint, [StringComparison]::Ordinal) `
                                -or -not [string]::Equals([string](Get-QualificationProperty $manifestImage 'relativePath'), $imagePath, [StringComparison]::Ordinal) `
                                -or -not [string]::Equals([string](Get-QualificationProperty $manifestImage 'sha256'), $imageHash, [StringComparison]::OrdinalIgnoreCase)) {
                                [void]$Failures.Add("visual review image '$imageId' binding disagrees with the visual manifest")
                            }
                        }
                        $packetCheckpointById = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
                        foreach ($checkpoint in $packetCheckpoints) {
                            $checkpointId = [string](Get-QualificationProperty $checkpoint 'checkpointId')
                            $checkpointRequiredness = [string](Get-QualificationProperty $checkpoint 'requiredness')
                            if ([string]::IsNullOrWhiteSpace($checkpointId) -or $packetCheckpointById.ContainsKey($checkpointId)) {
                                [void]$Failures.Add("visual review packet '$reviewPacketPath' contains a duplicate or empty checkpoint ID")
                                continue
                            }
                            $packetCheckpointById[$checkpointId] = $checkpoint
                            if ($checkpointRequiredness -notin @('REQUIRED', 'BEST_EFFORT')) {
                                [void]$Failures.Add("visual review checkpoint '$checkpointId' has unsupported requiredness '$checkpointRequiredness'")
                            }
                            $checkpointArtifacts = @(Get-QualificationRequiredVisualArray `
                                    -Object $checkpoint `
                                    -Name 'artifactIds' `
                                    -Path "visual review checkpoint '$checkpointId'.artifactIds" `
                                    -Failures $Failures)
                            if ($checkpointArtifacts.Count -eq 0) {
                                [void]$Failures.Add("visual review checkpoint '$checkpointId' has no image artifacts")
                            }
                            foreach ($checkpointArtifactId in $checkpointArtifacts) {
                                $checkpointArtifactText = [string]$checkpointArtifactId
                                if (-not $packetImageById.ContainsKey($checkpointArtifactText)) {
                                    [void]$Failures.Add("visual review checkpoint '$checkpointId' references unknown image '$checkpointArtifactText'")
                                }
                                elseif (-not [string]::Equals([string](Get-QualificationProperty $packetImageById[$checkpointArtifactText] 'checkpointId'), $checkpointId, [StringComparison]::Ordinal)) {
                                    [void]$Failures.Add("visual review checkpoint '$checkpointId' image binding disagrees")
                                }
                            }
                        }
                        $packetFailureById = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
                        foreach ($failure in $packetFailures) {
                            $failureId = [string](Get-QualificationProperty $failure 'failureId')
                            if ([string]::IsNullOrWhiteSpace($failureId) -or $packetFailureById.ContainsKey($failureId)) {
                                [void]$Failures.Add("visual review packet '$reviewPacketPath' contains a duplicate or empty derived-failure ID")
                            }
                            elseif ($manifestFailureById.ContainsKey($failureId)) {
                                $packetFailureById[$failureId] = $failure
                            }
                            else {
                                [void]$Failures.Add("visual review packet '$reviewPacketPath' contains unknown derived-failure '$failureId'")
                            }
                        }
                        if ($manifestFailureById.Count -ne $packetFailureById.Count) {
                            [void]$Failures.Add("derived artifact failure count disagrees between visual manifest and review packet '$reviewPacketPath'")
                        }
                        foreach ($failureId in $manifestFailureById.Keys) {
                            if (-not $packetFailureById.ContainsKey($failureId)) {
                                [void]$Failures.Add("visual review packet '$reviewPacketPath' is missing derived-failure '$failureId'")
                                continue
                            }
                            $left = $manifestFailureById[$failureId]
                            $right = $packetFailureById[$failureId]
                            foreach ($field in @('failureId', 'artifactKind', 'artifactId', 'checkpointId', 'scenario', 'reason', 'requiredness', 'recordedUtc')) {
                                if (-not [string]::Equals([string](Get-QualificationProperty $left $field), [string](Get-QualificationProperty $right $field), [StringComparison]::Ordinal)) {
                                    [void]$Failures.Add("derived artifact failure binding disagrees for '$failureId'")
                                    break
                                }
                            }
                            if ([int](Get-QualificationProperty $left 'attempt') -ne [int](Get-QualificationProperty $right 'attempt') `
                                -or [bool](Get-QualificationProperty $left 'rawArtifactsPreserved') -ne [bool](Get-QualificationProperty $right 'rawArtifactsPreserved')) {
                                [void]$Failures.Add("derived artifact failure binding disagrees for '$failureId'")
                            }
                            $leftSources = @(Get-QualificationProperty $left 'sourceArtifactIds')
                            $rightSources = @(Get-QualificationProperty $right 'sourceArtifactIds')
                            if ($leftSources.Count -ne $rightSources.Count) {
                                [void]$Failures.Add("derived artifact failure source binding disagrees for '$failureId'")
                            }
                            else {
                                for ($sourceIndex = 0; $sourceIndex -lt $leftSources.Count; $sourceIndex++) {
                                    if (-not [string]::Equals([string]$leftSources[$sourceIndex], [string]$rightSources[$sourceIndex], [StringComparison]::Ordinal)) {
                                        [void]$Failures.Add("derived artifact failure source binding disagrees for '$failureId'")
                                        break
                                    }
                                }
                            }
                        }

                        $requiredResultPath = [string](Get-QualificationProperty $packet 'requiredResultPath')
                        $requiredResultSchema = [string](Get-QualificationProperty $packet 'requiredResultSchema')
                        if ([string]::IsNullOrWhiteSpace($requiredResultPath) -or $requiredResultSchema -ne 'tabdock-visual-review-result-v1') {
                            [void]$Failures.Add("visual review packet '$reviewPacketPath' has an incomplete required-result contract")
                        }
                        else {
                            if ([string]::IsNullOrWhiteSpace($scenarioReviewResultPath) `
                                -or -not [string]::Equals($scenarioReviewResultPath, $requiredResultPath, [StringComparison]::Ordinal)) {
                                [void]$Failures.Add("visual review result path disagrees between scenario and review packet '$reviewPacketPath'")
                            }
                            try {
                                $resultResolved = Resolve-QualificationRelativePath -Root $manifestRoot -RelativePath $requiredResultPath
                                $resultBundleRelative = (Resolve-QualificationRelativePath -Root $BundleRoot -RelativePath ([IO.Path]::GetRelativePath($BundleRoot, $resultResolved.FullPath))).RelativePath
                                if (-not $ArtifactMap.ContainsKey($resultBundleRelative)) {
                                    [void]$Failures.Add("visual review result '$requiredResultPath' is absent from bundle artifactIndex")
                                }
                                if (-not (Test-Path -LiteralPath $resultResolved.FullPath -PathType Leaf)) {
                                    [void]$Failures.Add("required visual review result is missing: '$requiredResultPath'")
                                }
                                else {
                                    $actualResultHash = Get-QualificationFileSha256 $resultResolved.FullPath
                                    if ($ArtifactMap.ContainsKey($resultBundleRelative) `
                                        -and -not [string]::Equals($ArtifactMap[$resultBundleRelative].Hash, $actualResultHash, [StringComparison]::OrdinalIgnoreCase)) {
                                        [void]$Failures.Add("visual review result hash disagrees with bundle artifactIndex: '$requiredResultPath'")
                                    }
                                    $resultJson = Read-QualificationJsonFile $resultResolved.FullPath
                                    foreach ($failure in @($resultJson.DuplicateFailures)) { [void]$Failures.Add("visual review result '$requiredResultPath': $failure") }
                                    $result = $resultJson.Value
                                    if ([string](Get-QualificationProperty $result 'schema') -ne 'tabdock-visual-review-result-v1' `
                                        -or [int](Get-QualificationProperty $result 'schemaVersion') -ne 1) {
                                        [void]$Failures.Add("visual review result '$requiredResultPath' schema is unsupported")
                                    }
                                    $resultPacketHash = [string](Get-QualificationProperty $result 'packetSha256')
                                    if (-not (Test-QualificationVisualSha256 $resultPacketHash) `
                                        -or -not [string]::Equals($resultPacketHash, $actualPacketHash, [StringComparison]::OrdinalIgnoreCase)) {
                                        [void]$Failures.Add("visual review result '$requiredResultPath' packet hash disagrees with exact packet bytes")
                                    }
                                    if (-not [string]::Equals([string](Get-QualificationProperty $result 'candidateSha'), $expectedCandidate, [StringComparison]::Ordinal) `
                                        -or -not [string]::Equals([string](Get-QualificationProperty $result 'runId'), $expectedRunId, [StringComparison]::Ordinal) `
                                        -or -not [string]::Equals([string](Get-QualificationProperty $result 'scenario'), $expectedScenario, [StringComparison]::Ordinal) `
                                        -or [int](Get-QualificationProperty $result 'attempt') -ne $expectedAttempt) {
                                        [void]$Failures.Add("visual review result '$requiredResultPath' identity disagrees")
                                    }
                                    $reviewedImages = @(Get-QualificationRequiredVisualArray `
                                            -Object $result `
                                            -Name 'reviewedImages' `
                                            -Path "visual review result '$requiredResultPath'.reviewedImages" `
                                            -Failures $Failures)
                                    $resultFindings = @(Get-QualificationRequiredVisualArray `
                                            -Object $result `
                                            -Name 'findings' `
                                            -Path "visual review result '$requiredResultPath'.findings" `
                                            -Failures $Failures)
                                    $acknowledged = @(Get-QualificationRequiredVisualArray `
                                            -Object $result `
                                            -Name 'acknowledgedDerivedFailureIds' `
                                            -Path "visual review result '$requiredResultPath'.acknowledgedDerivedFailureIds" `
                                            -Failures $Failures)
                                    $reviewedById = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
                                    $reviewedCheckpoints = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                                    foreach ($reviewed in $reviewedImages) {
                                        $reviewedId = [string](Get-QualificationProperty $reviewed 'artifactId')
                                        if ([string]::IsNullOrWhiteSpace($reviewedId) -or $reviewedById.ContainsKey($reviewedId)) {
                                            [void]$Failures.Add("visual review result '$requiredResultPath' contains a duplicate or empty reviewed artifact ID")
                                            continue
                                        }
                                        $reviewedById[$reviewedId] = $reviewed
                                        $reviewedCheckpoints.Add([string](Get-QualificationProperty $reviewed 'checkpointId')) | Out-Null
                                        if (-not $packetImageById.ContainsKey($reviewedId)) {
                                            [void]$Failures.Add("visual review result '$requiredResultPath' references unknown image '$reviewedId'")
                                            continue
                                        }
                                        $packetImage = $packetImageById[$reviewedId]
                                        if (-not [string]::Equals([string](Get-QualificationProperty $reviewed 'checkpointId'), [string](Get-QualificationProperty $packetImage 'checkpointId'), [StringComparison]::Ordinal) `
                                            -or -not [string]::Equals([string](Get-QualificationProperty $reviewed 'sha256'), [string](Get-QualificationProperty $packetImage 'sha256'), [StringComparison]::OrdinalIgnoreCase)) {
                                            [void]$Failures.Add("visual review result '$requiredResultPath' image binding disagrees for '$reviewedId'")
                                        }
                                    }
                                    $findingIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                                    foreach ($finding in $resultFindings) {
                                        $findingId = [string](Get-QualificationProperty $finding 'findingId')
                                        $findingArtifactId = [string](Get-QualificationProperty $finding 'artifactId')
                                        if ([string]::IsNullOrWhiteSpace($findingId) -or -not $findingIds.Add($findingId)) {
                                            [void]$Failures.Add("visual review result '$requiredResultPath' contains a duplicate or empty finding ID")
                                        }
                                        if (-not $packetImageById.ContainsKey($findingArtifactId)) {
                                            [void]$Failures.Add("visual review finding '$findingId' references unknown image '$findingArtifactId'")
                                        }
                                        elseif (-not $reviewedById.ContainsKey($findingArtifactId)) {
                                            [void]$Failures.Add("visual review finding '$findingId' references an image that was not reviewed")
                                        }
                                    }
                                    $acknowledgedIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                                    foreach ($acknowledgedId in $acknowledged) {
                                        $acknowledgedText = [string]$acknowledgedId
                                        if ([string]::IsNullOrWhiteSpace($acknowledgedText) -or -not $acknowledgedIds.Add($acknowledgedText)) {
                                            [void]$Failures.Add("visual review result '$requiredResultPath' contains duplicate or empty derived-failure acknowledgements")
                                        }
                                        elseif (-not $manifestFailureById.ContainsKey($acknowledgedText)) {
                                            [void]$Failures.Add("visual review result '$requiredResultPath' acknowledges unknown derived-failure '$acknowledgedText'")
                                        }
                                    }
                                    $verdict = [string](Get-QualificationProperty $result 'verdict')
                                    if ($verdict -notin @('VISUAL_OK', 'VISUAL_SUSPECT', 'VISUAL_DEFECT', 'REVIEW_UNAVAILABLE')) {
                                        [void]$Failures.Add("visual review result '$requiredResultPath' has unsupported verdict '$verdict'")
                                    }
                                    elseif ($verdict -eq 'REVIEW_UNAVAILABLE') {
                                        [void]$Failures.Add("required visual review is unavailable: '$requiredResultPath'")
                                    }
                                    elseif ($verdict -ne 'VISUAL_OK') {
                                        [void]$Failures.Add("required visual review is non-pass: '$verdict'")
                                    }
                                    if ($verdict -eq 'VISUAL_OK') {
                                        if ($resultFindings.Count -gt 0) {
                                            [void]$Failures.Add("VISUAL_OK visual review result '$requiredResultPath' contains findings")
                                        }
                                        foreach ($checkpointId in $packetCheckpointById.Keys) {
                                            if (-not $reviewedCheckpoints.Contains($checkpointId)) {
                                                [void]$Failures.Add("VISUAL_OK visual review omits checkpoint '$checkpointId'")
                                            }
                                        }
                                        foreach ($failureId in $manifestFailureById.Keys) {
                                            if (-not $acknowledgedIds.Contains($failureId)) {
                                                [void]$Failures.Add("VISUAL_OK visual review does not acknowledge derived-failure '$failureId'")
                                            }
                                            else {
                                                $failure = $manifestFailureById[$failureId]
                                                if ([string](Get-QualificationProperty $failure 'requiredness') -eq 'REQUIRED' `
                                                    -or -not [bool](Get-QualificationProperty $failure 'rawArtifactsPreserved')) {
                                                    [void]$Failures.Add("VISUAL_OK cannot accept derived-failure '$failureId'")
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch {
                                [void]$Failures.Add("visual review result '$requiredResultPath' could not be verified: $($_.Exception.Message)")
                            }
                        }
                    }
                }
                catch {
                    [void]$Failures.Add("visual review packet '$reviewPacketPath' could not be verified: $($_.Exception.Message)")
                }
            }
        }
    }
    catch {
        [void]$Failures.Add("visual manifest '$visualPath' could not be verified: $($_.Exception.Message)")
    }
}
function Test-QualificationVisualPerformance {
    <# Verifies synthetic/physical visual measurement reports offline. #>
    param(
        [Parameter(Mandatory = $true)][object]$RunManifest,
        [Parameter(Mandatory = $true)][string]$RunManifestPath,
        [Parameter(Mandatory = $true)][string]$BundleRoot,
        [Parameter(Mandatory = $true)][hashtable]$ArtifactMap,
        [Parameter(Mandatory = $true)][object]$ScenarioEntry,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Failures,
        [switch]$RequirePhysicalTopology
    )
    $performancePath = [string](Get-QualificationProperty $ScenarioEntry 'visualPerformanceArtifact')
    $junitPath = [string](Get-QualificationProperty $ScenarioEntry 'visualPerformanceJUnitArtifact')
    if ([string]::IsNullOrWhiteSpace($performancePath) -and [string]::IsNullOrWhiteSpace($junitPath)) {
        return
    }
    if ([string]::IsNullOrWhiteSpace($performancePath)) {
        [void]$Failures.Add("visual performance report is missing for scenario '$([string](Get-QualificationProperty $ScenarioEntry 'scenario'))'")
        return
    }
    if ([string]::IsNullOrWhiteSpace($junitPath)) {
        [void]$Failures.Add("visual performance JUnit is missing for scenario '$([string](Get-QualificationProperty $ScenarioEntry 'scenario'))'")
    }
    $manifestRoot = [IO.Path]::GetDirectoryName($RunManifestPath)
    $resolved = $null
    $bundleRelative = $null
    try {
        $resolved = Resolve-QualificationRelativePath -Root $manifestRoot -RelativePath $performancePath
        $bundleRelative = (Resolve-QualificationRelativePath -Root $BundleRoot -RelativePath ([IO.Path]::GetRelativePath($BundleRoot, $resolved.FullPath))).RelativePath
    }
    catch {
        [void]$Failures.Add("visual performance report path is invalid: $($_.Exception.Message)")
        return
    }
    if (-not $ArtifactMap.ContainsKey($bundleRelative)) {
        [void]$Failures.Add("visual performance report '$performancePath' is absent from bundle artifactIndex")
    }
    elseif (-not (Test-Path -LiteralPath $resolved.FullPath -PathType Leaf)) {
        [void]$Failures.Add("visual performance report '$performancePath' is missing")
        return
    }
    else {
        $actualHash = Get-QualificationFileSha256 $resolved.FullPath
        if (-not [string]::Equals($actualHash, $ArtifactMap[$bundleRelative].Hash, [StringComparison]::OrdinalIgnoreCase)) {
            [void]$Failures.Add("visual performance report '$performancePath' hash disagrees with bundle artifactIndex")
        }
    }
    if (-not (Test-Path -LiteralPath $resolved.FullPath -PathType Leaf)) {
        return
    }

    try {
        $json = Read-QualificationJsonFile $resolved.FullPath
        foreach ($failure in @($json.DuplicateFailures)) { [void]$Failures.Add("visual performance report '$performancePath': $failure") }
        $report = $json.Value
        if ([int](Get-QualificationProperty $report 'schemaVersion') -ne 1 `
            -or [string](Get-QualificationProperty $report 'artifactKind') -ne 'visual-performance-measurements') {
            [void]$Failures.Add("visual performance report '$performancePath' has an unsupported schema")
        }
        $expectedCandidate = [string](Get-QualificationProperty $RunManifest 'candidateSha')
        $expectedRunId = [string](Get-QualificationProperty $RunManifest 'runId')
        $driverIdentity = Get-QualificationProperty $RunManifest 'driverIdentity'
        $expectedDriver = [string](Get-QualificationProperty $driverIdentity 'sha256')
        if (-not [string]::Equals([string](Get-QualificationProperty $report 'candidateSha'), $expectedCandidate, [StringComparison]::Ordinal)) {
            [void]$Failures.Add("visual performance report '$performancePath' candidate binding disagrees")
        }
        if (-not [string]::Equals([string](Get-QualificationProperty $report 'runId'), $expectedRunId, [StringComparison]::Ordinal) `
            -or -not [string]::Equals([string](Get-QualificationProperty $report 'driverSha'), $expectedDriver, [StringComparison]::OrdinalIgnoreCase)) {
            [void]$Failures.Add("visual performance report '$performancePath' run/driver binding disagrees")
        }
        $classification = [string](Get-QualificationProperty $report 'classification')
        $capabilities = Get-QualificationProperty $ScenarioEntry 'capabilities'
        $syntheticProperty = $null
        if ($null -ne $capabilities) { $syntheticProperty = $capabilities.PSObject.Properties['syntheticMeasurements'] }
        if ($null -ne $syntheticProperty -and [bool]$syntheticProperty.Value -and $classification -ne 'SYNTHETIC') {
            [void]$Failures.Add("visual performance report '$performancePath' marks a synthetic run as non-synthetic")
        }
        if ($classification -notin @('SYNTHETIC', 'PHYSICAL')) {
            [void]$Failures.Add("visual performance report '$performancePath' has an unsupported classification '$classification'")
        }
        elseif ($RequirePhysicalTopology -and $classification -ne 'PHYSICAL') {
            [void]$Failures.Add("synthetic visual performance report '$performancePath' cannot satisfy a physical gate")
        }

        $samples = @(Get-QualificationRequiredVisualArray `
                -Object $report `
                -Name 'samples' `
                -Path "visual performance report '$performancePath'.samples" `
                -Failures $Failures)
        $cells = @(Get-QualificationRequiredVisualArray `
                -Object $report `
                -Name 'cells' `
                -Path "visual performance report '$performancePath'.cells" `
                -Failures $Failures)
        $budgets = @(Get-QualificationRequiredVisualArray `
                -Object $report `
                -Name 'budgets' `
                -Path "visual performance report '$performancePath'.budgets" `
                -Failures $Failures)
        $pairs = @(Get-QualificationRequiredVisualArray `
                -Object $report `
                -Name 'pairComparisons' `
                -Path "visual performance report '$performancePath'.pairComparisons" `
                -Failures $Failures)
        if ($samples.Count -eq 0 -or $cells.Count -eq 0) {
            [void]$Failures.Add("visual performance report '$performancePath' has no raw samples or aggregate cells")
        }
        $sampleIdentity = @{}
        $sampleCellCounts = @{}
        $enabledSampleKeys = [System.Collections.Generic.List[string]]::new()
        $allowedModes = @('DISABLED', 'CHECKPOINTS', 'CHECKPOINTS_PLUS_PACKET', 'FLIGHT_HEALTHY_DISCARD', 'FLIGHT_FAILURE_FLUSH')
        foreach ($sample in $samples) {
            $sampleCandidate = [string](Get-QualificationProperty $sample 'candidateSha')
            $sampleRun = [string](Get-QualificationProperty $sample 'runId')
            $sampleClass = [string](Get-QualificationProperty $sample 'classification')
            $sampleNumber = [int](Get-QualificationProperty $sample 'sampleNumber')
            $sampleAttempt = [int](Get-QualificationProperty $sample 'attempt')
            $scenario = [string](Get-QualificationProperty $sample 'scenario')
            $sampleConfig = [string](Get-QualificationProperty $sample 'configuration')
            $mode = [string](Get-QualificationProperty $sample 'mode')
            $topology = Get-QualificationProperty $sample 'machineTopology'
            $machineClass = [string](Get-QualificationProperty $topology 'machineClass')
            $topologyClass = [string](Get-QualificationProperty $topology 'topologyClass')
            $displayClass = [string](Get-QualificationProperty $topology 'displayClass')
            $dpi = [int](Get-QualificationProperty $topology 'dpi')
            if (-not [string]::Equals($sampleCandidate, $expectedCandidate, [StringComparison]::Ordinal) `
                -or -not [string]::Equals($sampleRun, $expectedRunId, [StringComparison]::Ordinal) `
                -or $sampleClass -ne $classification `
                -or $sampleAttempt -lt 1 `
                -or $sampleNumber -lt 1 `
                -or $mode -notin $allowedModes `
                -or [string]::IsNullOrWhiteSpace($scenario) `
                -or [string]::IsNullOrWhiteSpace($sampleConfig) `
                -or [string]::IsNullOrWhiteSpace($machineClass) `
                -or [string]::IsNullOrWhiteSpace($topologyClass) `
                -or [string]::IsNullOrWhiteSpace($displayClass) `
                -or $dpi -lt 1) {
                [void]$Failures.Add("visual performance report '$performancePath' contains an invalid sample identity")
                continue
            }
            $sampleKey = "$scenario|$mode|$sampleNumber"
            if ($sampleIdentity.ContainsKey($sampleKey)) {
                [void]$Failures.Add("visual performance report '$performancePath' contains duplicate sample '$sampleKey'")
            }
            else {
                $sampleIdentity[$sampleKey] = $sample
                $cellKey = "$scenario|$mode|$sampleClass|$sampleConfig|$machineClass|$topologyClass|$displayClass|$dpi"
                if (-not $sampleCellCounts.ContainsKey($cellKey)) { $sampleCellCounts[$cellKey] = 0 }
                $sampleCellCounts[$cellKey]++
                if ($mode -ne 'DISABLED') { [void]$enabledSampleKeys.Add($sampleKey) }
            }
        }
        $cellIdentity = @{}
        $underSampled = $false
        foreach ($cell in $cells) {
            $cellKey = [string](Get-QualificationProperty $cell 'cellKey')
            $cellScenario = [string](Get-QualificationProperty $cell 'scenario')
            $cellMode = [string](Get-QualificationProperty $cell 'mode')
            $cellClass = [string](Get-QualificationProperty $cell 'classification')
            $cellCandidate = [string](Get-QualificationProperty $cell 'candidateSha')
            $cellConfig = [string](Get-QualificationProperty $cell 'configuration')
            $cellTopology = Get-QualificationProperty $cell 'machineTopology'
            $cellMachine = [string](Get-QualificationProperty $cellTopology 'machineClass')
            $cellTopologyClass = [string](Get-QualificationProperty $cellTopology 'topologyClass')
            $cellDisplay = [string](Get-QualificationProperty $cellTopology 'displayClass')
            $cellDpi = [int](Get-QualificationProperty $cellTopology 'dpi')
            $cellCount = [int](Get-QualificationProperty $cell 'sampleCount')
            $computedCellKey = "$cellScenario|$cellMode|$cellClass|$cellConfig|$cellMachine|$cellTopologyClass|$cellDisplay|$cellDpi"
            if ([string]::IsNullOrWhiteSpace($cellKey) `
                -or $cellKey -ne $computedCellKey `
                -or [string]::IsNullOrWhiteSpace($cellScenario) `
                -or $cellMode -notin $allowedModes `
                -or $cellClass -ne $classification `
                -or -not [string]::Equals($cellCandidate, $expectedCandidate, [StringComparison]::Ordinal) `
                -or [string]::IsNullOrWhiteSpace($cellConfig) `
                -or [string]::IsNullOrWhiteSpace($cellMachine) `
                -or [string]::IsNullOrWhiteSpace($cellTopologyClass) `
                -or [string]::IsNullOrWhiteSpace($cellDisplay) `
                -or $cellDpi -lt 1 `
                -or $cellCount -lt 1 `
                -or $cellIdentity.ContainsKey($cellKey)) {
                [void]$Failures.Add("visual performance report '$performancePath' contains an invalid or duplicate aggregate cell")
                continue
            }
            $cellIdentity[$cellKey] = $cell
            if (-not $sampleCellCounts.ContainsKey($cellKey) -or $sampleCellCounts[$cellKey] -ne $cellCount) {
                [void]$Failures.Add("visual performance report '$performancePath' cell '$cellKey' sample count disagrees with raw samples")
            }
            $underSampled = $underSampled -or $cellCount -lt 20
            $statistics = @(Get-QualificationRequiredVisualArray `
                    -Object $cell `
                    -Name 'statistics' `
                    -Path "visual performance report '$performancePath'.cell.statistics" `
                    -Failures $Failures)
            foreach ($statistic in $statistics) {
                $statisticMetric = [string](Get-QualificationProperty $statistic 'metric')
                $statisticUnits = [string](Get-QualificationProperty $statistic 'units')
                $available = [bool](Get-QualificationProperty $statistic 'available')
                $statisticCount = [int](Get-QualificationProperty $statistic 'sampleCount')
                $p95Property = $statistic.PSObject.Properties['p95']
                if ([string]::IsNullOrWhiteSpace($statisticMetric) `
                    -or [string]::IsNullOrWhiteSpace($statisticUnits) `
                    -or $statisticCount -ne $cellCount) {
                    [void]$Failures.Add("visual performance report '$performancePath' contains an invalid statistic")
                }
                if ($available -and $statisticCount -ge 20 -and ($null -eq $p95Property -or $null -eq $p95Property.Value)) {
                    [void]$Failures.Add("visual performance report '$performancePath' omits supported p95")
                }
                if ($null -ne $p95Property -and $null -ne $p95Property.Value -and $statisticCount -lt 20) {
                    [void]$Failures.Add("visual performance report '$performancePath' presents p95 for an under-sampled statistic")
                }
            }
        }
        foreach ($cellKey in @($sampleCellCounts.Keys)) {
            if (-not $cellIdentity.ContainsKey($cellKey)) {
                [void]$Failures.Add("visual performance report '$performancePath' is missing aggregate cell '$cellKey'")
            }
        }
        $reportOutcome = [string](Get-QualificationProperty $report 'outcome')
        if ($reportOutcome -notin @('PASS', 'PROVISIONAL', 'FAIL', 'BLOCKED')) {
            [void]$Failures.Add("visual performance report '$performancePath' has an unsupported outcome '$reportOutcome'")
        }
        elseif (($reportOutcome -eq 'PASS' -and $underSampled) `
            -or ($reportOutcome -eq 'PROVISIONAL' -and -not $underSampled)) {
            [void]$Failures.Add("visual performance report '$performancePath' outcome is inconsistent with cell sample counts")
        }
        $pairIdentity = @{}
        $pairOutcomes = [System.Collections.Generic.List[string]]::new()
        if ($pairs.Count -ne $enabledSampleKeys.Count) {
            [void]$Failures.Add("visual performance report '$performancePath' must pair every enabled sample with one disabled baseline")
        }
        foreach ($budget in $budgets) {
            $budgetCell = [string](Get-QualificationProperty $budget 'cellKey')
            $budgetMetric = [string](Get-QualificationProperty $budget 'metric')
            $budgetStatistic = [string](Get-QualificationProperty $budget 'statistic')
            $budgetCandidate = [string](Get-QualificationProperty $budget 'sourceCandidateSha')
            $sourceCount = [int](Get-QualificationProperty $budget 'sourceSampleCount')
            $limitProperty = $budget.PSObject.Properties['limit']
            $diagnosticProperty = $budget.PSObject.Properties['diagnosticOnly']
            $hardProperty = $budget.PSObject.Properties['hardCeiling']
            $rationale = [string](Get-QualificationProperty $budget 'rationale')
            if (-not $cellIdentity.ContainsKey($budgetCell) `
                -or [string]::IsNullOrWhiteSpace($budgetMetric) `
                -or $budgetStatistic -notin @('p95', 'maximum') `
                -or -not [string]::Equals($budgetCandidate, [string](Get-QualificationProperty $report 'candidateSha'), [StringComparison]::Ordinal) `
                -or $sourceCount -lt 1 `
                -or $null -eq $limitProperty `
                -or [double]::IsNaN([double]$limitProperty.Value) `
                -or [double]::IsInfinity([double]$limitProperty.Value) `
                -or [double]$limitProperty.Value -lt 0 `
                -or $null -eq $diagnosticProperty `
                -or $null -eq $hardProperty `
                -or ([bool]$diagnosticProperty.Value -eq [bool]$hardProperty.Value) `
                -or [string]::IsNullOrWhiteSpace($rationale)) {
                [void]$Failures.Add("visual performance report '$performancePath' contains incomplete or contradictory budget provenance")
            }
        }
        foreach ($pair in $pairs) {
            $pairCandidate = [string](Get-QualificationProperty $pair 'candidateSha')
            $pairRun = [string](Get-QualificationProperty $pair 'runId')
            $pairScenario = [string](Get-QualificationProperty $pair 'scenario')
            $pairMode = [string](Get-QualificationProperty $pair 'enabledMode')
            $pairClass = [string](Get-QualificationProperty $pair 'classification')
            $pairConfig = [string](Get-QualificationProperty $pair 'configuration')
            $pairSampleNumber = [int](Get-QualificationProperty $pair 'sampleNumber')
            $pairOutcome = [string](Get-QualificationProperty $pair 'outcome')
            $pairReason = [string](Get-QualificationProperty $pair 'reason')
            $pairTopology = Get-QualificationProperty $pair 'machineTopology'
            $pairMachine = [string](Get-QualificationProperty $pairTopology 'machineClass')
            $pairTopologyClass = [string](Get-QualificationProperty $pairTopology 'topologyClass')
            $pairDisplay = [string](Get-QualificationProperty $pairTopology 'displayClass')
            $pairDpi = [int](Get-QualificationProperty $pairTopology 'dpi')
            $pairCellKey = "$pairScenario|$pairMode|$pairClass|$pairConfig|$pairMachine|$pairTopologyClass|$pairDisplay|$pairDpi"
            $pairSampleKey = "$pairScenario|$pairMode|$pairSampleNumber"
            if (-not [string]::Equals($pairCandidate, $expectedCandidate, [StringComparison]::Ordinal) `
                -or -not [string]::Equals($pairRun, $expectedRunId, [StringComparison]::Ordinal) `
                -or $pairMode -notin $allowedModes `
                -or $pairMode -eq 'DISABLED' `
                -or $pairClass -ne $classification `
                -or [string]::IsNullOrWhiteSpace($pairScenario) `
                -or [string]::IsNullOrWhiteSpace($pairConfig) `
                -or $pairSampleNumber -lt 1 `
                -or $pairOutcome -notin @('PASS', 'FAIL', 'BLOCKED') `
                -or -not $cellIdentity.ContainsKey($pairCellKey) `
                -or -not $sampleIdentity.ContainsKey($pairSampleKey) `
                -or $pairIdentity.ContainsKey($pairSampleKey) `
                -or ($pairOutcome -eq 'PASS' -and -not [string]::IsNullOrWhiteSpace($pairReason)) `
                -or ($pairOutcome -ne 'PASS' -and [string]::IsNullOrWhiteSpace($pairReason))) {
                [void]$Failures.Add("visual performance report '$performancePath' contains an invalid pair comparison")
            }
            else {
                $pairIdentity[$pairSampleKey] = $pair
            }
            $pairOutcomes.Add($pairOutcome)
            $resourceDeltas = Get-QualificationProperty $pair 'resourceDeltas'
            $allowedDeltas = Get-QualificationProperty $pair 'allowedPositiveDeltas'
            foreach ($name in @('processHandleCount','userObjectCount','gdiObjectCount','hBitmapCount','hdcCount','fileHandleCount','privateBytes','workingSet','threadCount','workerThreadCount','timerCount','tabDockOwnedWindowCount','ringBytes','artifactCount','artifactBytes')) {
                if ($null -eq $resourceDeltas -or $null -eq $resourceDeltas.PSObject.Properties[$name] `
                    -or $null -eq $allowedDeltas -or $null -eq $allowedDeltas.PSObject.Properties[$name]) {
                    [void]$Failures.Add("visual performance report '$performancePath' pair comparison omits resource metric '$name'")
                }
            }
        }
        foreach ($enabledSampleKey in $enabledSampleKeys) {
            if (-not $pairIdentity.ContainsKey($enabledSampleKey)) {
                [void]$Failures.Add("visual performance report '$performancePath' enabled sample '$enabledSampleKey' has no pair comparison")
            }
        }
        if (($reportOutcome -in @('PASS', 'PROVISIONAL')) `
            -and @($pairOutcomes | Where-Object { $_ -ne 'PASS' }).Count -gt 0) {
            [void]$Failures.Add("visual performance report '$performancePath' has a non-pass pair in a non-failing report")
        }
    }
    catch {
        [void]$Failures.Add("visual performance report '$performancePath' could not be verified: $($_.Exception.Message)")
    }

    if (-not [string]::IsNullOrWhiteSpace($junitPath)) {
        try {
            $junitResolved = Resolve-QualificationRelativePath -Root $manifestRoot -RelativePath $junitPath
            $junitBundleRelative = (Resolve-QualificationRelativePath -Root $BundleRoot -RelativePath ([IO.Path]::GetRelativePath($BundleRoot, $junitResolved.FullPath))).RelativePath
            if (-not $ArtifactMap.ContainsKey($junitBundleRelative)) {
                [void]$Failures.Add("visual performance JUnit '$junitPath' is absent from bundle artifactIndex")
            }
            elseif (Test-Path -LiteralPath $junitResolved.FullPath -PathType Leaf) {
                $junitHash = Get-QualificationFileSha256 $junitResolved.FullPath
                if (-not [string]::Equals($junitHash, $ArtifactMap[$junitBundleRelative].Hash, [StringComparison]::OrdinalIgnoreCase)) {
                    [void]$Failures.Add("visual performance JUnit '$junitPath' hash disagrees with bundle artifactIndex")
                }
            }
            if (-not (Test-Path -LiteralPath $junitResolved.FullPath -PathType Leaf)) {
                [void]$Failures.Add("visual performance JUnit '$junitPath' is missing")
            }
            else {
                [xml]$junit = Get-Content -Raw -LiteralPath $junitResolved.FullPath
                if ($junit.testsuite -eq $null) {
                    [void]$Failures.Add("visual performance JUnit '$junitPath' has no testsuite root")
                }
            }
        }
        catch {
            [void]$Failures.Add("visual performance JUnit '$junitPath' could not be verified: $($_.Exception.Message)")
        }
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
        foreach ($scenarioEntry in @(Get-QualificationProperty $summary.Manifest 'scenarios')) {
            Test-QualificationVisualManifest `
                -RunManifest $summary.Manifest `
                -RunManifestPath $artifactMap[$relative].FullPath `
                -BundleRoot $root `
                -ArtifactMap $artifactMap `
                -ScenarioEntry $scenarioEntry `
                -Failures $failures
            Test-QualificationVisualPerformance `
                -RunManifest $summary.Manifest `
                -RunManifestPath $artifactMap[$relative].FullPath `
                -BundleRoot $root `
                -ArtifactMap $artifactMap `
                -ScenarioEntry $scenarioEntry `
                -Failures $failures `
                -RequirePhysicalTopology:$RequirePhysicalTopology
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
        if ($RequirePhysicalTopology) {
            $bundleOutcome = Get-QualificationProperty $bundle 'outcome'
            if ([string](Get-QualificationProperty $bundleOutcome 'overall') -ne 'PASS') {
                [void]$failures.Add('physical qualification evidence cannot bind a non-PASS automated outcome')
            }
            $bundleCounts = Get-QualificationProperty $bundleOutcome 'scenarioCounts'
            foreach ($code in @('FAIL_PRODUCT', 'FAIL_HARNESS', 'BLOCKED_ENVIRONMENT', 'BLOCKED_SUPERVISED', 'BLOCKED_CAPABILITY', 'SKIP_CAPABILITY', 'FLAKE_UNCLASSIFIED')) {
                $count = Get-QualificationProperty $bundleCounts $code
                if ($null -ne $count -and [int]$count -gt 0) {
                    [void]$failures.Add("physical qualification evidence contains non-PASS outcome '$code'")
                }
            }
        }
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
