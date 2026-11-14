[CmdletBinding()]
param(
    [ValidateRange(1, 10)][int]$Iterations = 3,
    [string]$OutputPath = 'artifacts/facts-evals/latest.json',
    [string[]]$CaseId = @(),
    [switch]$EnableFactsReasoning,
    [switch]$DisableFactsReasoning,
    [switch]$NoBuild,
    [switch]$DishMatching,
    [switch]$CupTranslation,
    [ValidateRange(0, 12000)][int]$MaxOutputTokens = 0,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
if ($EnableFactsReasoning -and $DisableFactsReasoning) { throw 'Choose either enabled or disabled facts reasoning' }
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src/Dishhive.Evaluations/Dishhive.Evaluations.csproj'
$saved = @{}
Push-Location $repoRoot
try {
    # Build before loading credentials. No model call is made by validation or build.
    if (-not $NoBuild) {
        & dotnet build $project --disable-build-servers -m:1
        if ($LASTEXITCODE -ne 0) { throw 'Could not build the facts evaluator' }
    }
    $arguments = @('--iterations', "$Iterations", '--output', $OutputPath)
    if ($DishMatching) { $arguments += '--dish-matching' }
    if ($CupTranslation) { $arguments += '--cup-translation' }
    if ($CaseId.Count -gt 0) { $arguments += @('--case', ($CaseId -join ',')) }
    if ($ValidateOnly) { $arguments += '--validate-only' }
    else {
        # Reuse the local app's exact provider/task configuration. Secrets travel only
        # through child-process environment variables and are never written to reports.
        $settings = & docker inspect --format '{{json .Config.Env}}' dishhive-app | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0) { throw 'Cannot read the local application configuration' }
        foreach ($setting in $settings) {
            if ($setting -match '^(Ai__[^=]+)=(.*)$') {
                $name = $Matches[1]
                $saved[$name] = [Environment]::GetEnvironmentVariable($name)
                [Environment]::SetEnvironmentVariable($name, $Matches[2])
            }
        }
    }
    if ($EnableFactsReasoning) {
        if (-not $saved.ContainsKey('Ai__FactsEnableReasoning')) { $saved['Ai__FactsEnableReasoning'] = $env:Ai__FactsEnableReasoning }
        $env:Ai__FactsEnableReasoning = 'true'
    }
    if ($DisableFactsReasoning) {
        if (-not $saved.ContainsKey('Ai__FactsEnableReasoning')) { $saved['Ai__FactsEnableReasoning'] = $env:Ai__FactsEnableReasoning }
        $env:Ai__FactsEnableReasoning = 'false'
    }
    if ($MaxOutputTokens -gt 0) {
        if (-not $saved.ContainsKey('Ai__FactsMaxOutputTokens')) { $saved['Ai__FactsMaxOutputTokens'] = $env:Ai__FactsMaxOutputTokens }
        $env:Ai__FactsMaxOutputTokens = "$MaxOutputTokens"
    }
    & dotnet run --no-build --no-restore --project $project -- @arguments
    $result = $LASTEXITCODE
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Pop-Location
}
exit $result
