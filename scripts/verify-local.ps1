[CmdletBinding()]
param(
    [string]$PostgresConnection = $env:DISHHIVE_TEST_POSTGRES,
    [string]$ArtifactDirectory = "artifacts/local-verification"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not [IO.Path]::IsPathRooted($ArtifactDirectory)) { $ArtifactDirectory = Join-Path $repoRoot $ArtifactDirectory }
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
$webRoot = Join-Path $repoRoot "src/dishhive-web"
$npm = if ([Environment]::OSVersion.Platform -eq 'Win32NT') { 'npm.cmd' } else { 'npm' }

function Invoke-LocalCheck([string]$Name, [string]$Directory, [string]$Command, [string[]]$Arguments) {
    Write-Host "Running $Name..."
    Push-Location $Directory
    $savedErrorPreference = $ErrorActionPreference
    try {
        # Windows PowerShell represents ordinary native stderr progress as ErrorRecords.
        $ErrorActionPreference = 'Continue'
        & $Command @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $ArtifactDirectory "$Name.log")
        $checkExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $savedErrorPreference
        Pop-Location
    }
    if ($checkExitCode -ne 0) { throw "$Name failed (exit $checkExitCode). See $ArtifactDirectory/$Name.log" }
}

# Only the local database service is started. Every relational test creates and drops
# its own randomly named database; the development database and Freezy data are retained.
Invoke-LocalCheck 'database' $repoRoot 'docker' @('compose', 'up', '-d', 'db')
if ([string]::IsNullOrWhiteSpace($PostgresConnection)) {
    $values = @{}
    $envFile = Join-Path $repoRoot '.env'
    if (Test-Path -LiteralPath $envFile) {
        foreach ($line in Get-Content -LiteralPath $envFile) {
            if ($line -match '^(POSTGRES_USER|POSTGRES_PASSWORD)=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim('"', "'") }
        }
    }
    $databaseUser = if ($values.ContainsKey('POSTGRES_USER')) { $values['POSTGRES_USER'] } else { 'dishhive' }
    $databasePassword = if ($values.ContainsKey('POSTGRES_PASSWORD')) { $values['POSTGRES_PASSWORD'] } else { 'dishhive_dev_password' }
    $binding = & docker compose -f (Join-Path $repoRoot 'docker-compose.yml') port db 5432
    if ($LASTEXITCODE -ne 0 -or $binding -notmatch ':(\d+)\s*$') { throw 'Cannot resolve the local PostgreSQL port' }
    $PostgresConnection = 'Host=127.0.0.1;Port=' + $Matches[1] + ';Database=postgres;Username="' +
        $databaseUser.Replace('"', '""') + '";Password="' + $databasePassword.Replace('"', '""') + '"'
}

$savedPostgres = $env:DISHHIVE_TEST_POSTGRES
$savedBrowser = $env:DISHHIVE_TEST_BROWSER
$savedArtifacts = $env:DISHHIVE_LOCAL_ARTIFACTS
try {
    $env:DISHHIVE_TEST_POSTGRES = $PostgresConnection
    $env:DISHHIVE_TEST_BROWSER = '1'
    $env:DISHHIVE_LOCAL_ARTIFACTS = $ArtifactDirectory

    $contracts = @(
        (Join-Path $webRoot 'src/app/api/generated/dishhive-api.client.ts'),
        (Join-Path $repoRoot 'src/Dishhive.Api/openapi/Dishhive.Api.json')
    )
    $before = @($contracts | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
    Invoke-LocalCheck 'api-generation' $webRoot $npm @('run', 'generate:api-client')
    $after = @($contracts | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash })
    if (@(Compare-Object $before $after).Count -gt 0) { throw 'Generated API contracts changed. Review the generated files and repeat verification.' }

    Invoke-LocalCheck 'frontend-tests' $webRoot $npm @('test', '--', '--watch=false')
    Invoke-LocalCheck 'localization' $webRoot 'node' @('scripts/check-localization.mjs')
    Invoke-LocalCheck 'production-build' $webRoot $npm @('run', 'build')
    Invoke-LocalCheck 'browser-contracts' $webRoot $npm @('run', 'check:browser-fixtures')
    Invoke-LocalCheck 'backend' $repoRoot 'dotnet' @('test', 'src/Dishhive.Api.Tests/Dishhive.Api.Tests.csproj',
        '--no-restore', '--disable-build-servers', '-m:1', '--logger', 'trx;LogFileName=backend.trx', '--results-directory', $ArtifactDirectory)
    Invoke-LocalCheck 'facts-corpus' $repoRoot 'dotnet' @('run', '--no-build', '--no-restore',
        '--project', 'src/Dishhive.Evaluations', '--', '--validate-only')
    Invoke-LocalCheck 'planning-oracle' $repoRoot (Get-Process -Id $PID).Path @('-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', (Join-Path $repoRoot 'scripts/run-ai-week-planning-evals.ps1'), '-ValidateOracle')
    Invoke-LocalCheck 'browser-fixtures' $webRoot $npm @('run', 'test:browser')
    Invoke-LocalCheck 'production-pwa' $webRoot $npm @('exec', '--', 'playwright', 'test', '--config=playwright.pwa.config.ts')
    $pwaEvidence = Join-Path $ArtifactDirectory 'pwa'
    New-Item -ItemType Directory -Path $pwaEvidence -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $webRoot 'test-results') -Recurse -Filter 'api-outage*.png' |
        Copy-Item -Destination $pwaEvidence
    Invoke-LocalCheck 'scraper-build' $repoRoot 'docker' @('compose', 'build', 'scraper')
    Push-Location $repoRoot
    $savedErrorPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $buildDefinition = & docker compose build --print scraper 2> (Join-Path $ArtifactDirectory 'scraper-metadata.log') | ConvertFrom-Json
        $metadataExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $savedErrorPreference
        Pop-Location
    }
    # `compose images` describes existing containers, which can still refer to a
    # replaced/pruned image. Build metadata identifies the tag just built above.
    $scraperImage = @($buildDefinition.target.scraper.tags)[0]
    if ($metadataExitCode -ne 0 -or -not $scraperImage) { throw 'Cannot resolve the built scraper image' }
    $scraperSource = Join-Path $repoRoot 'src/dishhive-scraper'
    Invoke-LocalCheck 'linux-scraper' $repoRoot 'docker' @('run', '--rm', '--network', 'none',
        '--mount', "type=bind,source=$scraperSource,target=/workspace,readonly", '--workdir', '/workspace',
        '--env', 'PYTHONDONTWRITEBYTECODE=1', '--entrypoint', 'python', $scraperImage.Trim(), '-m', 'unittest', '-v')
    [pscustomobject]@{ completedAt=(Get-Date).ToUniversalTime().ToString('o'); outcome='passed';
        checks=@('api-generation', 'frontend-tests', 'localization', 'production-build', 'browser-contracts',
            'backend-with-postgresql-and-real-api-browser', 'facts-corpus', 'planning-oracle', 'browser-fixtures', 'production-pwa', 'linux-scraper') } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'summary.json') -Encoding utf8
    Write-Host "All local checks passed. Evidence: $ArtifactDirectory"
} finally {
    $env:DISHHIVE_TEST_POSTGRES = $savedPostgres
    $env:DISHHIVE_TEST_BROWSER = $savedBrowser
    $env:DISHHIVE_LOCAL_ARTIFACTS = $savedArtifacts
}
