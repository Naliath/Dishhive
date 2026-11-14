[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5100",
    [string[]]$ScenarioId = @(),
    [int]$Iterations = 0,
    [string]$OutputPath = "",
    [string]$ApiKey = "",
    [string]$ReplayReport = "",
    [switch]$ValidateOracle,
    [ValidateRange(1, 3600)]
    [int]$RequestTimeoutSeconds = 360,
    [ValidateRange(0, 60)]
    [int]$MinimumRequestIntervalSeconds = 7,
    [switch]$IncludeKnownLimitations,
    [switch]$FailOnPerformance
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$scenarioPath = Join-Path $repoRoot "src\Dishhive.Api.Tests\Fixtures\AiWeekPlanning\scenarios.json"
$scenarios = Get-Content -Raw -LiteralPath $scenarioPath | ConvertFrom-Json
$scenarios = @($scenarios)
if ($ValidateOracle) { $ScenarioId = @('dutch-friday-pancakes-from-source') }
$foodCorpusPath = Join-Path $repoRoot 'src/Dishhive.Api.Tests/Fixtures/RecipeDishMatching/scenarios.json'
$foodCorpus = Get-Content -Raw -LiteralPath $foodCorpusPath | ConvertFrom-Json
$skippedKnownLimitations = @()
if ($ScenarioId.Count -gt 0) {
    $scenarios = @($scenarios | Where-Object { $ScenarioId -contains $_.id })
    if ($scenarios.Count -ne $ScenarioId.Count) {
        $found = @($scenarios.id)
        $missing = @($ScenarioId | Where-Object { $_ -notin $found })
        throw "Unknown scenario id(s): $($missing -join ', ')"
    }
} elseif (-not $IncludeKnownLimitations) {
    $skippedKnownLimitations = @($scenarios | Where-Object { $_.status -eq "known-limitation" })
    $scenarios = @($scenarios | Where-Object { $_.status -ne "known-limitation" })
    if ($skippedKnownLimitations.Count -gt 0) {
        Write-Host "Skipping $($skippedKnownLimitations.Count) known-limitation scenario(s): $($skippedKnownLimitations.id -join ', ')" -ForegroundColor Yellow
    }
}

function Get-NextMonday([datetime]$today) {
    $days = (([int][DayOfWeek]::Monday - [int]$today.DayOfWeek + 7) % 7)
    if ($days -eq 0) { $days = 7 }
    return $today.Date.AddDays($days)
}

$collectionMemberships = @{}
$requiredCollections = @($scenarios | ForEach-Object { $_.expectations.requiredCollections } | Where-Object { $_ } | Sort-Object -Unique)
if ($requiredCollections.Count -gt 0) {
    if ($ReplayReport) { throw 'Offline replay requires selecting scenarios without collection lookups' }
    $lookupHeaders = @{}
    if (-not [string]::IsNullOrWhiteSpace($ApiKey)) { $lookupHeaders['X-Dishhive-Api-Key'] = $ApiKey }
    $availableCollections = Invoke-RestMethod -Uri "$($BaseUrl.TrimEnd('/'))/api/cookbooks" -Headers $lookupHeaders
    foreach ($name in $requiredCollections) {
        $collection = $availableCollections | Where-Object { $_.name -eq $name } | Select-Object -First 1
        if ($null -ne $collection) {
            $members = Invoke-RestMethod -Uri "$($BaseUrl.TrimEnd('/'))/api/cookbooks/$($collection.id)/recipes" -Headers $lookupHeaders
            $collectionMemberships[$name] = @($members | ForEach-Object id)
        }
    }
}

function Get-CourseNumber([string]$course) {
    switch ($course.ToLowerInvariant()) {
        "main" { return 0 }
        "appetizer" { return 1 }
        "side" { return 2 }
        "dessert" { return 3 }
        default { throw "Unknown course '$course' in evaluation scenario" }
    }
}

function Get-NormalizedHost([string]$url) {
    try {
        $normalizedHost = ([uri]$url).Host.ToLowerInvariant()
        if ($normalizedHost.StartsWith("www.")) { return $normalizedHost.Substring(4) }
        return $normalizedHost
    } catch { return "" }
}

function Test-Iteration($scenario, [datetime]$weekStart, $response, [long]$durationMs) {
    $failures = [System.Collections.Generic.List[string]]::new()
    $acceptedGapFailures = [System.Collections.Generic.List[string]]::new()
    $warnings = [System.Collections.Generic.List[string]]::new()
    $suggestions = @($response.suggestions)
    function Test-ExplainedEmptyDate([string]$date, [int]$course) {
        return $response.status -eq 'partial' -and
            @($suggestions | Where-Object { $_.date -eq $date -and [int]$_.course -eq $course }).Count -eq 0 -and
            @($response.unmetRequirements | Where-Object { $_.id -eq "day-$date" -and -not [string]::IsNullOrWhiteSpace($_.message) }).Count -gt 0
    }

    foreach ($name in @($scenario.expectations.requiredCollections | Where-Object { $_ })) {
        if (-not $collectionMemberships.ContainsKey($name)) {
            $failures.Add("Required evaluation collection '$name' is unavailable")
            continue
        }
        foreach ($suggestion in $suggestions) {
            if (-not $suggestion.recipeId -or $suggestion.recipeId -notin $collectionMemberships[$name]) {
                $failures.Add("Suggestion '$($suggestion.dishName)' is not an id-linked member of required collection '$name'")
            }
        }
    }

    if ($scenario.expectations.PSObject.Properties.Name -contains "allowedStatuses") {
        if ($response.status -notin @($scenario.expectations.allowedStatuses)) {
            $failures.Add("Plan status '$($response.status)' was not allowed; expected $(@($scenario.expectations.allowedStatuses) -join ', ')")
        }
    }
    if ($scenario.expectations.PSObject.Properties.Name -contains "minimumUnmetRequirements") {
        $unmetCount = @($response.unmetRequirements).Count
        if ($unmetCount -lt [int]$scenario.expectations.minimumUnmetRequirements) {
            $failures.Add("Expected at least $($scenario.expectations.minimumUnmetRequirements) unmet requirement(s), got $unmetCount")
        }
    }
    if ($scenario.expectations.rejectAllergyWarnings) {
        foreach ($suggestion in @($suggestions | Where-Object allergyWarning)) {
            $failures.Add("Suggestion '$($suggestion.dishName)' raised an allergy review warning: $($suggestion.allergyWarning)")
        }
    }
    if ($scenario.expectations.PSObject.Properties.Name -contains "maxTotalTimeMinutes") {
        foreach ($suggestion in $suggestions) {
            if ($null -eq $suggestion.totalTimeMinutes -or $suggestion.totalTimeMinutes -gt $scenario.expectations.maxTotalTimeMinutes) {
                $failures.Add("Suggestion '$($suggestion.dishName)' is not verified within $($scenario.expectations.maxTotalTimeMinutes) minutes")
            }
        }
    }
    if ($scenario.expectations.PSObject.Properties.Name -contains "allowedOrigins") {
        foreach ($suggestion in $suggestions) {
            $origin = if ($suggestion.freezyItemRef) { "freezer" }
                elseif ($suggestion.recipeId) { "local" }
                elseif ($suggestion.sourceUrl) { "external" }
                else { "plain" }
            if ($origin -notin @($scenario.expectations.allowedOrigins)) {
                $failures.Add("Suggestion '$($suggestion.dishName)' used disallowed origin '$origin'")
            }
        }
    }

    if ($scenario.PSObject.Properties.Name -contains "selectedDayOffsets") {
        $allowedDates = @($scenario.selectedDayOffsets | ForEach-Object {
            $weekStart.AddDays([int]$_).ToString("yyyy-MM-dd")
        })
        foreach ($suggestion in $suggestions) {
            if ($suggestion.date -notin $allowedDates) {
                $failures.Add("Suggestion $($suggestion.dishName) used unselected date $($suggestion.date)")
            }
        }
    }

    if ($scenario.expectations.uniqueDishes) {
        $duplicates = @($suggestions |
            Where-Object { $_.dishName } |
            Group-Object { if ($_.sourceUrl) { "url:$($_.sourceUrl.ToLowerInvariant())" } else { "dish:$($_.dishName.Trim().ToLowerInvariant())" } } |
            Where-Object { @($_.Group.date | Sort-Object -Unique).Count -gt 1 })
        foreach ($duplicate in $duplicates) {
            $failures.Add("Repeated dish across dates: $($duplicate.Group[0].dishName)")
        }
    }

    if ($scenario.expectations.rejectCollectionPages) {
        foreach ($suggestion in @($suggestions | Where-Object sourceUrl)) {
            $path = ([uri]$suggestion.sourceUrl).AbsolutePath.ToLowerInvariant()
            $badPath = @("/category/", "/tag/", "/author/", "/search/", "/feed/") |
                Where-Object { $path.Contains($_) }
            if (@($badPath).Count -gt 0) {
                $failures.Add("External result is a collection/article page, not a recipe: $($suggestion.sourceUrl)")
            }
        }
    }

    if ($scenario.expectations.PSObject.Properties.Name -contains "minimumLocalRecipeLinks") {
        $linked = @($suggestions | Where-Object { $_.recipeId })
        if ($linked.Count -lt [int]$scenario.expectations.minimumLocalRecipeLinks) {
            $failures.Add("Expected at least $($scenario.expectations.minimumLocalRecipeLinks) local recipe link(s), got $($linked.Count)")
        }
    }

    foreach ($source in @($scenario.expectations.sources)) {
        $course = Get-CourseNumber $source.course
        $matches = @($suggestions | Where-Object {
            $_.sourceUrl -and
            (Get-NormalizedHost $_.sourceUrl) -eq $source.host.ToLowerInvariant() -and
            [int]$_.course -eq $course
        })
        $distinctUrls = @($matches.sourceUrl | Sort-Object -Unique)
        $acceptEmptySource = $source.allowUnfilledWithExplanation -and $distinctUrls.Count -eq 0 -and @($source.requiredDayOffsets).Count -gt 0
        foreach ($offset in @($source.requiredDayOffsets)) {
            if (-not (Test-ExplainedEmptyDate ($weekStart.AddDays([int]$offset).ToString('yyyy-MM-dd')) $course)) { $acceptEmptySource = $false }
        }
        if ($distinctUrls.Count -lt [int]$source.minDistinctRecipes) {
            $message = "$($source.host) $($source.course): expected at least $($source.minDistinctRecipes) distinct recipes, got $($distinctUrls.Count)"
            $failures.Add($message)
            if ($acceptEmptySource) { $acceptedGapFailures.Add($message) }
        }
        if ($distinctUrls.Count -gt [int]$source.maxDistinctRecipes) {
            $failures.Add("$($source.host) $($source.course): expected at most $($source.maxDistinctRecipes) distinct recipes, got $($distinctUrls.Count)")
        }
        foreach ($match in $matches) {
            if ($source.PSObject.Properties.Name -contains 'foodIdentity') {
                $family = $foodCorpus.recipeFamilies.PSObject.Properties[$source.foodIdentity]
                if ($null -eq $family) { throw "Unknown independent food identity '$($source.foodIdentity)'" }
                $allowedUrls = @($family.Value | ForEach-Object { ([uri]$_).GetLeftPart([UriPartial]::Path).TrimEnd('/').ToLowerInvariant() })
                $actualUrl = ([uri]$match.sourceUrl).GetLeftPart([UriPartial]::Path).TrimEnd('/').ToLowerInvariant()
                if ($actualUrl -notin $allowedUrls) {
                    $failures.Add("$($source.host) $($source.course): '$($match.dishName)' is not independently labelled as $($source.foodIdentity)")
                }
                if (-not $match.factsAssessed) { $failures.Add("Food-identity candidate '$($match.dishName)' has no assessed ingredient facts") }
            }
            if (@($source.requiredClasses).Count -gt 0 -or @($source.excludedClasses).Count -gt 0) {
                if (-not $match.factsAssessed) {
                    $failures.Add("$($source.host) $($source.course): '$($match.dishName)' has no assessed canonical facts")
                    continue
                }
                foreach ($required in @($source.requiredClasses)) {
                    if ($required -notin @($match.containsClasses)) {
                        $failures.Add("$($source.host) $($source.course): '$($match.dishName)' misses required class $required")
                    }
                }
                foreach ($excluded in @($source.excludedClasses)) {
                    if ($excluded -in @($match.containsClasses)) {
                        $failures.Add("$($source.host) $($source.course): '$($match.dishName)' contains excluded class $excluded")
                    }
                }
            }
        }
        foreach ($offset in @($source.requiredDayOffsets)) {
            $requiredDate = $weekStart.AddDays([int]$offset).ToString("yyyy-MM-dd")
            if (-not ($matches | Where-Object { $_.date -eq $requiredDate })) {
                $message = "$($source.host) $($source.course): missing required date $requiredDate"
                $failures.Add($message)
                if ($acceptEmptySource) { $acceptedGapFailures.Add($message) }
            }
        }
    }

    foreach ($slot in @($scenario.expectations.slots)) {
        $date = $weekStart.AddDays([int]$slot.dayOffset).ToString("yyyy-MM-dd")
        $course = Get-CourseNumber $slot.course
        $matches = @($suggestions | Where-Object { $_.date -eq $date -and [int]$_.course -eq $course })
        if ($matches.Count -lt [int]$slot.minCount -or $matches.Count -gt [int]$slot.maxCount) {
            $message = "Slot $date/$($slot.course): expected $($slot.minCount)-$($slot.maxCount) suggestion(s), got $($matches.Count)"
            $failures.Add($message)
            if ($slot.allowUnfilledWithExplanation -and $matches.Count -eq 0 -and (Test-ExplainedEmptyDate $date $course)) {
                $acceptedGapFailures.Add($message)
            }
        }
        foreach ($required in @($slot.requiredClasses)) {
            if (-not ($matches | Where-Object { $_.factsAssessed -and $required -in @($_.containsClasses) })) {
                $failures.Add("Slot $date/$($slot.course): no verified dish contains $required")
            }
        }
        if ($slot.requiresConstraintClaim -and -not ($matches | Where-Object { @($_.constraintIds).Count -gt 0 })) {
            $failures.Add("Slot $date/$($slot.course): explicit requirement was not linked to a normalized constraint")
        }
        if ($slot.requiresDietWarning -and -not ($matches | Where-Object { $_.dietWarning })) {
            $failures.Add("Slot $date/$($slot.course): explicit dietary exception has no review warning")
        }
        if ($slot.rejectAllergyWarnings -and ($matches | Where-Object allergyWarning)) {
            $failures.Add("Slot $date/$($slot.course): a dish raised an allergy review warning")
        }
        if ($slot.allAttendees -and ($matches | Where-Object { @($_.attendeeNames).Count -gt 0 })) {
            $failures.Add("Slot $date/$($slot.course): explicitly requested dish was split across attendees")
        }
    }

    if ($durationMs -gt [long]$scenario.performanceBudgetMs) {
        $message = "Duration ${durationMs}ms exceeded advisory budget $($scenario.performanceBudgetMs)ms"
        if ($FailOnPerformance) { $failures.Add($message) } else { $warnings.Add($message) }
    }

    return [pscustomobject]@{
        passed = $failures.Count -eq 0
        policyPassed = @($failures | Where-Object { $_ -notin $acceptedGapFailures }).Count -eq 0
        acceptedPartial = $failures.Count -gt 0 -and @($failures | Where-Object { $_ -notin $acceptedGapFailures }).Count -eq 0
        acceptedGaps = @($acceptedGapFailures)
        durationMs = $durationMs
        failures = @($failures)
        warnings = @($warnings)
        suggestions = $suggestions
    }
}

if ($ValidateOracle) {
    $scenario = $scenarios[0]
    $week = [datetime]'2027-02-22'
    $locals = @(0..3 | ForEach-Object { [pscustomobject]@{
        date=$week.AddDays($_).ToString('yyyy-MM-dd'); course=0; dishName="Saved meal $_";
        recipeId=[guid]::NewGuid().ToString(); factsAssessed=$true; containsClasses=@(); attendeeNames=@()
    } })
    $empty = [pscustomobject]@{status='partial'; suggestions=$locals; unmetRequirements=@(
        [pscustomobject]@{id='day-2027-02-26';message='No assessed dinner main is available for Friday 26 February 2027.'})}
    $valid = Test-Iteration $scenario $week $empty 100
    if ($valid.passed -or -not $valid.policyPassed -or -not $valid.acceptedPartial) { throw 'Explained empty Friday must be accepted without claiming meal completion' }
    $unexplained = Test-Iteration $scenario $week ([pscustomobject]@{status='partial';suggestions=$locals;unmetRequirements=@()}) 100
    if ($unexplained.policyPassed) { throw 'An unexplained gap must fail' }
    foreach ($url in @('https://dagelijksekost.vrt.be/gerechten/veggie-okonomiyaki', 'https://dagelijksekost.vrt.be/gerechten/pannenkoeken-op-zn-amerikaans-met-bosbessen-en-esdoornsiroop')) {
        $foreign = [pscustomobject]@{date='2027-02-26';course=0;dishName='Other pancake style';sourceUrl=$url;factsAssessed=$true;containsClasses=@();attendeeNames=@()}
        $bad = Test-Iteration $scenario $week ([pscustomobject]@{status='partial';suggestions=@($locals)+@($foreign);unmetRequirements=@()}) 100
        if ($bad.policyPassed) { throw 'A different pancake style must not satisfy the scoped request' }
    }
    $locals[0] | Add-Member allergyWarning 'Conflicts with an allergy'
    $allergy = Test-Iteration $scenario $week $empty 100
    if ($allergy.policyPassed) { throw 'Partial policy must never forgive an allergy violation' }
    Write-Output 'Oracle checks passed: explained gap, unexplained gap, two wrong styles and allergy rejection; strict completion remains separate.'
    exit 0
}

$allResults = [System.Collections.Generic.List[object]]::new()
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $directory = Join-Path $repoRoot "artifacts\ai-evals"
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $OutputPath = Join-Path $directory ("ai-week-planning-{0}.json" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
}
function Save-EvaluationReport {
    $report = [pscustomobject]@{
        generatedAt = (Get-Date).ToUniversalTime().ToString("o")
        baseUrl = $BaseUrl
        skippedKnownLimitations = @($skippedKnownLimitations.id)
        minimumRequestIntervalSeconds = $MinimumRequestIntervalSeconds
        replayedFrom = if ($ReplayReport) { $ReplayReport } else { $null }
        passed = @($allResults | Where-Object passed).Count
        failed = @($allResults | Where-Object { -not $_.passed }).Count
        policyPassed = @($allResults | Where-Object policyPassed).Count
        policyFailed = @($allResults | Where-Object { -not $_.policyPassed }).Count
        results = @($allResults)
    }
    $report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    return $report
}
if ($ReplayReport) {
    $previous = Get-Content -LiteralPath $ReplayReport -Raw | ConvertFrom-Json
    foreach ($record in @($previous.results | Where-Object { $_.scenarioId -in @($scenarios.id) })) {
        $scenario = $scenarios | Where-Object id -eq $record.scenarioId | Select-Object -First 1
        $response = [pscustomobject]@{ status=$record.applicationStatus; suggestions=@($record.suggestions); unmetRequirements=@($record.unmetRequirements) }
        $evaluation = Test-Iteration $scenario ([datetime]$record.weekStart) $response $record.durationMs
        $allResults.Add([pscustomobject]@{ scenarioId=$record.scenarioId; iteration=$record.iteration;
            weekStart=$record.weekStart; prompt=$record.prompt; instructions=$record.instructions; passed=$evaluation.passed;
            policyPassed=$evaluation.policyPassed; acceptedPartial=$evaluation.acceptedPartial; acceptedGaps=$evaluation.acceptedGaps;
            durationMs=$record.durationMs; failures=$evaluation.failures; warnings=$evaluation.warnings;
            applicationStatus=$response.status; unmetRequirements=$response.unmetRequirements; suggestions=$evaluation.suggestions })
    }
    if ($allResults.Count -eq 0) { throw 'No selected scenarios in the replay report' }
    $report = Save-EvaluationReport
    Write-Host "Offline oracle replay: $($report.passed) passed; $($report.failed) failed. Report: $OutputPath"
    if ($report.failed -gt 0) { exit 1 }; exit 0
}
$nextMonday = Get-NextMonday (Get-Date)
$lastRequestStartedAt = (Get-Date).AddSeconds(-$MinimumRequestIntervalSeconds)
foreach ($scenario in $scenarios) {
    $weekStart = $nextMonday.AddDays(7 * [int]$scenario.leadWeeks)
    $runCount = if ($Iterations -gt 0) { $Iterations } else { [int]$scenario.iterations }
    for ($iteration = 1; $iteration -le $runCount; $iteration++) {
        $spacingMs = [int]($MinimumRequestIntervalSeconds * 1000 - ((Get-Date) - $lastRequestStartedAt).TotalMilliseconds)
        if ($spacingMs -gt 0) { Start-Sleep -Milliseconds $spacingMs }
        $lastRequestStartedAt = Get-Date
        Write-Host "[$($scenario.id)] iteration $iteration/$runCount..." -ForegroundColor Cyan
        $instructions = $scenario.prompt
        if (-not [string]::IsNullOrWhiteSpace($scenario.instructionClarification)) { $instructions += "`n`n" + $scenario.instructionClarification }
        $body = @{
            weekStart = $weekStart.ToString("yyyy-MM-dd")
            attendeeIds = @()
            instructions = $instructions
        }
        if ($scenario.PSObject.Properties.Name -contains "selectedDayOffsets") {
            $body["selectedDates"] = @($scenario.selectedDayOffsets | ForEach-Object {
                $weekStart.AddDays([int]$_).ToString("yyyy-MM-dd")
            })
        }
        $body = $body | ConvertTo-Json
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $headers = @{}
        if (-not [string]::IsNullOrWhiteSpace($ApiKey)) { $headers["X-Dishhive-Api-Key"] = $ApiKey }
        try {
            $response = Invoke-RestMethod -Method Post -Uri "$($BaseUrl.TrimEnd('/'))/api/plannedmeals/suggestions" `
                -ContentType "application/json" -Headers $headers -Body $body -TimeoutSec $RequestTimeoutSeconds
            $watch.Stop()
            $evaluation = Test-Iteration $scenario $weekStart $response $watch.ElapsedMilliseconds
        } catch {
            $watch.Stop()
            $response = [pscustomobject]@{ status = 'transportFailure'; unmetRequirements = @() }
            $evaluation = [pscustomobject]@{
                passed = $false; policyPassed=$false; acceptedPartial=$false; acceptedGaps=@(); durationMs = $watch.ElapsedMilliseconds
                failures = @("Request failed: $($_.Exception.Message)"); warnings = @(); suggestions = @()
            }
        }
        $allResults.Add([pscustomobject]@{
            scenarioId = $scenario.id
            description = $scenario.description
            prompt = $scenario.prompt
            instructions = $instructions
            weekStart = $weekStart.ToString("yyyy-MM-dd")
            iteration = $iteration
            passed = $evaluation.passed
            policyPassed = $evaluation.policyPassed
            acceptedPartial = $evaluation.acceptedPartial
            acceptedGaps = $evaluation.acceptedGaps
            durationMs = $evaluation.durationMs
            failures = $evaluation.failures
            warnings = $evaluation.warnings
            applicationStatus = $response.status
            unmetRequirements = @($response.unmetRequirements)
            suggestions = $evaluation.suggestions
        })
        $report = Save-EvaluationReport
        $color = if ($evaluation.passed) { "Green" } else { "Red" }
        Write-Host "  $($(if ($evaluation.passed) { 'PASS' } else { 'FAIL' })) in $($watch.ElapsedMilliseconds)ms" -ForegroundColor $color
        foreach ($failure in $evaluation.failures) { Write-Host "    $failure" -ForegroundColor Red }
        foreach ($warning in $evaluation.warnings) { Write-Host "    $warning" -ForegroundColor Yellow }
        if ($evaluation.acceptedPartial) { Write-Host '    POLICY PASS: explicitly permitted, explained empty slot; strict meal completion remains unfilled.' -ForegroundColor Green }
    }
}

$report = Save-EvaluationReport
Write-Host "Report: $OutputPath"
Write-Host "Passed: $($report.passed); failed: $($report.failed)"
Write-Host "Policy passed: $($report.policyPassed); policy failed: $($report.policyFailed). Strict completion and exit status remain unchanged."
if ($report.failed -gt 0) { exit 1 }
