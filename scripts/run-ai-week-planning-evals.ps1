[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5100",
    [string[]]$ScenarioId = @(),
    [int]$Iterations = 0,
    [string]$OutputPath = "",
    [switch]$IncludeKnownLimitations,
    [switch]$FailOnPerformance
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$scenarioPath = Join-Path $repoRoot "src\Dishhive.Api.Tests\Fixtures\AiWeekPlanning\scenarios.json"
$scenarios = Get-Content -Raw -LiteralPath $scenarioPath | ConvertFrom-Json
$scenarios = @($scenarios)
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
    $warnings = [System.Collections.Generic.List[string]]::new()
    $suggestions = @($response.suggestions)

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

    foreach ($source in @($scenario.expectations.sources)) {
        $course = Get-CourseNumber $source.course
        $matches = @($suggestions | Where-Object {
            $_.sourceUrl -and
            (Get-NormalizedHost $_.sourceUrl) -eq $source.host.ToLowerInvariant() -and
            [int]$_.course -eq $course
        })
        $distinctUrls = @($matches.sourceUrl | Sort-Object -Unique)
        if ($distinctUrls.Count -lt [int]$source.minDistinctRecipes) {
            $failures.Add("$($source.host) $($source.course): expected at least $($source.minDistinctRecipes) distinct recipes, got $($distinctUrls.Count)")
        }
        if ($distinctUrls.Count -gt [int]$source.maxDistinctRecipes) {
            $failures.Add("$($source.host) $($source.course): expected at most $($source.maxDistinctRecipes) distinct recipes, got $($distinctUrls.Count)")
        }
        foreach ($match in $matches) {
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
                $failures.Add("$($source.host) $($source.course): missing required date $requiredDate")
            }
        }
    }

    foreach ($slot in @($scenario.expectations.slots)) {
        $date = $weekStart.AddDays([int]$slot.dayOffset).ToString("yyyy-MM-dd")
        $course = Get-CourseNumber $slot.course
        $matches = @($suggestions | Where-Object { $_.date -eq $date -and [int]$_.course -eq $course })
        if ($matches.Count -lt [int]$slot.minCount -or $matches.Count -gt [int]$slot.maxCount) {
            $failures.Add("Slot $date/$($slot.course): expected $($slot.minCount)-$($slot.maxCount) suggestion(s), got $($matches.Count)")
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
        durationMs = $durationMs
        failures = @($failures)
        warnings = @($warnings)
        suggestions = $suggestions
    }
}

$allResults = [System.Collections.Generic.List[object]]::new()
$nextMonday = Get-NextMonday (Get-Date)
foreach ($scenario in $scenarios) {
    $weekStart = $nextMonday.AddDays(7 * [int]$scenario.leadWeeks)
    $runCount = if ($Iterations -gt 0) { $Iterations } else { [int]$scenario.iterations }
    for ($iteration = 1; $iteration -le $runCount; $iteration++) {
        Write-Host "[$($scenario.id)] iteration $iteration/$runCount..." -ForegroundColor Cyan
        $body = @{
            weekStart = $weekStart.ToString("yyyy-MM-dd")
            attendeeIds = @()
            instructions = $scenario.prompt
        } | ConvertTo-Json
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $response = Invoke-RestMethod -Method Post -Uri "$($BaseUrl.TrimEnd('/'))/api/plannedmeals/suggestions" `
            -ContentType "application/json" -Body $body -TimeoutSec 300
        $watch.Stop()
        $evaluation = Test-Iteration $scenario $weekStart $response $watch.ElapsedMilliseconds
        $allResults.Add([pscustomobject]@{
            scenarioId = $scenario.id
            description = $scenario.description
            prompt = $scenario.prompt
            weekStart = $weekStart.ToString("yyyy-MM-dd")
            iteration = $iteration
            passed = $evaluation.passed
            durationMs = $evaluation.durationMs
            failures = $evaluation.failures
            warnings = $evaluation.warnings
            suggestions = $evaluation.suggestions
        })
        $color = if ($evaluation.passed) { "Green" } else { "Red" }
        Write-Host "  $($(if ($evaluation.passed) { 'PASS' } else { 'FAIL' })) in $($watch.ElapsedMilliseconds)ms" -ForegroundColor $color
        foreach ($failure in $evaluation.failures) { Write-Host "    $failure" -ForegroundColor Red }
        foreach ($warning in $evaluation.warnings) { Write-Host "    $warning" -ForegroundColor Yellow }
    }
}

$report = [pscustomobject]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
    baseUrl = $BaseUrl
    skippedKnownLimitations = @($skippedKnownLimitations.id)
    passed = @($allResults | Where-Object passed).Count
    failed = @($allResults | Where-Object { -not $_.passed }).Count
    results = @($allResults)
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $directory = Join-Path $repoRoot "artifacts\ai-evals"
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $OutputPath = Join-Path $directory ("ai-week-planning-{0}.json" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
}
$report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "Report: $OutputPath"
Write-Host "Passed: $($report.passed); failed: $($report.failed)"
if ($report.failed -gt 0) { exit 1 }
