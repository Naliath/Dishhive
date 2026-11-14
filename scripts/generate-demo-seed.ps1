<#
.SYNOPSIS
    Regenerates the offline demo ZIP through the application's import and AI enrichment services.
.DESCRIPTION
    Reuses the running dishhive-app container's AI configuration without logging credentials.
    Uses its own PostgreSQL container/database. Failed runs retain that database for resume;
    successful runs remove the disposable container. Existing household data is never used.
#>
[CmdletBinding()]
param(
    [string]$ContainerName = 'dishhive-demo-generation-db',
    [string]$ManifestPath = 'src/Dishhive.Api/Services/Demo/demo-recipes.json',
    [string]$OutputPath = 'src/Dishhive.Api/Services/Demo/demo-seed.zip',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src/Dishhive.Api/Dishhive.Api.csproj'
$saved = @{}
function Set-GenerationEnvironment([string]$Name, [string]$Value) {
    if (-not $saved.ContainsKey($Name)) { $saved[$Name] = [Environment]::GetEnvironmentVariable($Name) }
    [Environment]::SetEnvironmentVariable($Name, $Value)
}
function Assert-NativeExit([string]$Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed (exit $LASTEXITCODE)" }
}
Push-Location $repoRoot
try {
    if (-not $NoBuild) {
        & dotnet build $project -c DemoGeneration --no-restore --disable-build-servers -m:1 -p:OpenApiGenerateDocumentsOnBuild=false -p:GenerateDemoSeed=true
        Assert-NativeExit 'Build demo generator'
    }
    $settings = & docker inspect --format '{{json .Config.Env}}' dishhive-app | ConvertFrom-Json
    Assert-NativeExit 'Read application AI configuration'
    foreach ($setting in $settings) {
        if ($setting -match '^(Ai__[^=]+)=(.*)$') {
            $name = $Matches[1]
            $value = $Matches[2]
            if ($name -eq 'Ai__BaseUrl') { $value = $value.Replace('host.docker.internal', '127.0.0.1') }
            Set-GenerationEnvironment $name $value
        }
    }
    $existing = @(& docker ps -a --filter "name=^/${ContainerName}$" --format '{{.Names}}')
    Assert-NativeExit 'Inspect generation database'
    if ($existing -notcontains $ContainerName) {
        $password = [Guid]::NewGuid().ToString('N')
        & docker run -d --name $ContainerName --label dishhive.purpose=demo-generation `
            --publish '127.0.0.1::5432' --tmpfs /var/lib/postgresql/data:rw `
            --env POSTGRES_DB=dishhive_demo_seed_build --env POSTGRES_USER=dishhive `
            --env "POSTGRES_PASSWORD=$password" postgres:16-alpine | Out-Null
        Assert-NativeExit 'Create disposable generation database'
    }
    $purpose = & docker inspect --format '{{json .Config.Labels}}' $ContainerName | ConvertFrom-Json
    Assert-NativeExit 'Check generation container identity'
    if ($purpose.'dishhive.purpose' -ne 'demo-generation') { throw 'The container is not a Dishhive demo generation database' }
    $databaseSettings = & docker inspect --format '{{json .Config.Env}}' $ContainerName | ConvertFrom-Json
    Assert-NativeExit 'Read generation database configuration'
    $password = ($databaseSettings | Where-Object { $_ -like 'POSTGRES_PASSWORD=*' }).Substring('POSTGRES_PASSWORD='.Length)
    $binding = & docker port $ContainerName 5432/tcp
    Assert-NativeExit 'Resolve generation database port'
    if ($binding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Unexpected generation database port binding' }
    $port = $Matches[1]
    Set-GenerationEnvironment 'ConnectionStrings__DefaultConnection' "Host=127.0.0.1;Port=$port;Database=dishhive_demo_seed_build;Username=dishhive;Password=$password"
    Set-GenerationEnvironment 'RecipeScrapers__BaseUrl' 'http://127.0.0.1:5101'
    Set-GenerationEnvironment 'Database__AutoMigrate' 'true'
    Set-GenerationEnvironment 'Logging__LogLevel__Default' 'Warning'
    Set-GenerationEnvironment 'ASPNETCORE_ENVIRONMENT' 'Production'
    $manifest = (Resolve-Path -LiteralPath $ManifestPath).Path
    $output = if ([IO.Path]::IsPathRooted($OutputPath)) { [IO.Path]::GetFullPath($OutputPath) }
        else { [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputPath)) }
    $logDirectory = Join-Path $repoRoot 'artifacts/demo-collection'
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    # Native progress/errors must reach the log; the exit code decides success.
    $ErrorActionPreference = 'Continue'
    & dotnet run -c DemoGeneration --no-build --no-restore --no-launch-profile --project $project -- `
        --generate-demo-seed --demo-manifest $manifest --demo-output $output 2>&1 |
        Tee-Object -FilePath (Join-Path $logDirectory 'generation.log')
    $generationExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($generationExit -ne 0) { throw "Demo generation failed. Its disposable database is retained in $ContainerName for resume." }
    & docker rm -f $ContainerName | Out-Null
    Assert-NativeExit 'Remove disposable generation database'
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Pop-Location
}
