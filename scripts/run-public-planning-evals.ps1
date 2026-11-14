[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Image,
    [ValidateRange(1,10)][int]$Iterations = 3,
    [string]$ScenarioId = '',
    [string]$ArtifactDirectory = 'artifacts/public-planning-evals'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not [IO.Path]::IsPathRooted($ArtifactDirectory)) { $ArtifactDirectory = Join-Path $repoRoot $ArtifactDirectory }
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
$contextPath = Join-Path $repoRoot 'src/Dishhive.Api.Tests/Fixtures/AiWeekPlanning/public-context.json'
$publicContext = Get-Content $contextPath -Raw -Encoding UTF8 | ConvertFrom-Json
$runId = [Guid]::NewGuid().ToString('N')
$database = "dishhive_public_eval_$runId"
$container = "dishhive-public-eval-$runId"
$remoteDump = "/tmp/dishhive-public-eval-$runId.dump"
$created = $false
$launched = $false
$evaluationExit = 2
function Invoke-Docker([string[]]$Arguments) {
    $output = & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker operation failed: $($Arguments[0])" }
    return $output
}
function Add-PublicFixture([string]$Path, $Body) {
    Invoke-RestMethod -Method Post -Uri "$baseUrl/api/$Path" -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 20 -Compress)))
}
try {
    # Read configuration only. No source DB queries, dumps or household/recipe API reads.
    $appInfo = Invoke-Docker @('inspect', 'dishhive-app') | ConvertFrom-Json
    $dbInfo = Invoke-Docker @('inspect', 'dishhive-db') | ConvertFrom-Json
    $resolvedImage = (Invoke-Docker @('image', 'inspect', '--format', '{{.Id}}', $Image)).Trim()
    $network = @($dbInfo[0].NetworkSettings.Networks.PSObject.Properties.Name)[0]
    $settings = $appInfo[0].Config.Env
    $connection = @($settings | Where-Object { $_.StartsWith('ConnectionStrings__DefaultConnection=') })[0]
    if (-not $connection -or $connection -notmatch 'Database=dishhive;') { throw 'Unexpected local database configuration' }
    $connection = $connection.Replace('Database=dishhive;', "Database=$database;")
    Invoke-Docker @('exec', 'dishhive-db', 'createdb', '-U', 'dishhive', '-T', 'template0', $database) | Out-Null
    $created = $true
    $arguments = @('run', '-d', '--name', $container, '--network', $network, '-p', '127.0.0.1::5100', '--env', $connection)
    foreach ($setting in $settings) {
        if ($setting -match '^(Ai__|WebSearch__|RecipeScrapers__)' -and $setting -notmatch '^Ai__RetainDiagnosticText=') {
            $arguments += @('--env', $setting)
        }
    }
    $arguments += @('--env', 'Database__AutoMigrate=true', '--env', 'BackgroundJobs__Enabled=false',
        '--env', 'Demo__Enabled=false', '--env', 'Freezy__BaseUrl=', '--env', 'Ai__RetainDiagnosticText=true', $resolvedImage)
    Invoke-Docker $arguments | Out-Null
    $launched = $true
    $binding = Invoke-Docker @('port', $container, '5100/tcp')
    $baseUrl = 'http://' + @($binding)[0].Trim()
    $healthy = $false
    for ($attempt=0; $attempt -lt 45; $attempt++) {
        try { $healthy = (Invoke-WebRequest "$baseUrl/health" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { }
        if ($healthy) { break }
        Start-Sleep -Seconds 1
    }
    if (-not $healthy) { throw 'Public evaluation app did not start' }
    foreach ($member in $publicContext.members) { Add-PublicFixture 'familymembers' $member | Out-Null }
    $recipeIds = @()
    foreach ($recipe in $publicContext.recipes) { $recipeIds += (Add-PublicFixture 'recipes' $recipe).id }
    $collection = Add-PublicFixture 'cookbooks' @{ name=$publicContext.collection.name }
    $membership = @($publicContext.collection.recipeIndexes | ForEach-Object { $recipeIds[$_] })
    Add-PublicFixture "cookbooks/$($collection.id)/recipes" @{ recipeIds=$membership } | Out-Null
    $storedRecipes = Invoke-RestMethod "$baseUrl/api/recipes/page?pageSize=1"
    $storedMembers = Invoke-RestMethod "$baseUrl/api/familymembers"
    $prompt = Invoke-RestMethod "$baseUrl/api/settings/ai-prompt"
    if ($storedRecipes.totalCount -ne $publicContext.recipes.Count -or @($storedMembers).Count -ne $publicContext.members.Count -or
        $prompt.isCustomized -or @($storedMembers | Where-Object { $_.name -notlike 'Public evaluation *' }).Count -gt 0) {
        throw 'Public context isolation check failed; refusing model evaluation'
    }
    [pscustomobject]@{ dataOrigin='checked-in synthetic public-context.json only; no development DB rows';
        contextSha256=(Get-FileHash $contextPath -Algorithm SHA256).Hash; image=$resolvedImage;
        members=$publicContext.members.Count; recipes=$recipeIds.Count; collectionMembers=$membership.Count;
        freezyEnabled=$false; iterations=$Iterations; scenarioId=$ScenarioId } |
        ConvertTo-Json -Depth 4 | Set-Content (Join-Path $ArtifactDirectory 'configuration.json') -Encoding utf8
    $evalArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'run-ai-week-planning-evals.ps1'),
        '-BaseUrl', $baseUrl, '-Iterations', $Iterations, '-IncludeKnownLimitations', '-OutputPath', (Join-Path $ArtifactDirectory 'results.json'))
    if ($ScenarioId) { $evalArguments += @('-ScenarioId', $ScenarioId) }
    & powershell @evalArguments
    $evaluationExit = $LASTEXITCODE
    Invoke-Docker @('logs', $container) | Set-Content (Join-Path $ArtifactDirectory 'app.log') -Encoding utf8
    Invoke-RestMethod "$baseUrl/api/integrations/ai/planning-runs?take=200" |
        ConvertTo-Json -Depth 40 | Set-Content (Join-Path $ArtifactDirectory 'metrics.json') -Encoding utf8
    # Preserve only this public fixture DB for diagnostics before deleting the disposable DB.
    Invoke-Docker @('exec', 'dishhive-db', 'pg_dump', '-U', 'dishhive', '-d', $database, '-Fc', '--no-owner', '-f', $remoteDump) | Out-Null
    Invoke-Docker @('cp', "dishhive-db:$remoteDump", (Join-Path $ArtifactDirectory 'public-context.dump')) | Out-Null
} finally {
    if ($launched) { Invoke-Docker @('rm', '-f', $container) | Out-Null }
    if ($created -and $database -match '^dishhive_public_eval_[a-f0-9]{32}$') {
        Invoke-Docker @('exec', 'dishhive-db', 'dropdb', '-U', 'dishhive', '--force', $database) | Out-Null
    }
    if ($remoteDump -match '^/tmp/dishhive-public-eval-[a-f0-9]{32}\.dump$') {
        Invoke-Docker @('exec', 'dishhive-db', 'rm', '-f', $remoteDump) | Out-Null
    }
}
exit $evaluationExit
