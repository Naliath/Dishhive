[CmdletBinding()]
param([string]$ArtifactDirectory = 'artifacts/local-restore', [string]$Image = '')

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not [IO.Path]::IsPathRooted($ArtifactDirectory)) { $ArtifactDirectory = Join-Path $repoRoot $ArtifactDirectory }
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
$runId = [Guid]::NewGuid().ToString('N')
$restoreDatabase = "dishhive_restore_$runId"
$restoreContainer = "dishhive-restore-$runId"
$remoteDump = "/tmp/dishhive-restore-$runId.dump"
$dumpPath = Join-Path $ArtifactDirectory 'dishhive.dump'
$paused = $false
$created = $false
$launched = $false

function Invoke-Docker([string[]]$Arguments) {
    $output = & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker operation failed: $($Arguments[0])" }
    return $output
}

function Get-DatabaseManifest([string]$Database) {
    # Hash complete rows (including bytea images), in deterministic order, for EVERY
    # public table including migrations. No recipe-only export can satisfy this check.
    $sql = @'
CREATE TEMP TABLE restore_manifest (name text, rows bigint, digest text);
DO $$ DECLARE t record; BEGIN
  FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename LOOP
    EXECUTE format('INSERT INTO restore_manifest SELECT %L, count(*), md5(coalesce(string_agg(md5(row_to_json(r)::text), '''' ORDER BY row_to_json(r)::text), '''')) FROM public.%I r', t.tablename, t.tablename);
  END LOOP;
END $$;
SELECT coalesce(json_agg(restore_manifest ORDER BY name), '[]') FROM restore_manifest;
'@
    $result = $sql | & docker exec -i dishhive-db psql -X -qAt -v ON_ERROR_STOP=1 -U dishhive -d $Database
    if ($LASTEXITCODE -ne 0) { throw 'Database manifest failed' }
    return ($result -join "`n").Trim()
}

try {
    $databaseInfo = Invoke-Docker @('inspect', 'dishhive-db') | ConvertFrom-Json
    $appInfo = Invoke-Docker @('inspect', 'dishhive-app') | ConvertFrom-Json
    $restoreImage = if ($Image) { (Invoke-Docker @('image', 'inspect', '--format', '{{.Id}}', $Image)).Trim() } else { $appInfo[0].Image }
    if ($databaseInfo[0].Config.Labels.'com.docker.compose.service' -ne 'db' -or
        $appInfo[0].Config.Labels.'com.docker.compose.service' -ne 'app') { throw 'Expected local Dishhive Compose services' }
    $network = @($databaseInfo[0].NetworkSettings.Networks.PSObject.Properties.Name)[0]
    $connection = @($appInfo[0].Config.Env | Where-Object { $_.StartsWith('ConnectionStrings__DefaultConnection=') })[0]
    if (-not $connection -or $connection -notmatch 'Database=dishhive;') { throw 'Unexpected source database; refusing rehearsal' }
    $restoreConnection = $connection.Replace('Database=dishhive;', "Database=$restoreDatabase;")

    # Briefly freeze only Dishhive's app so the manifest and pg_dump share stable data.
    Invoke-Docker @('pause', 'dishhive-app') | Out-Null
    $paused = $true
    $sourceManifest = Get-DatabaseManifest 'dishhive'
    Invoke-Docker @('exec', 'dishhive-db', 'pg_dump', '-U', 'dishhive', '-d', 'dishhive', '-Fc', '--no-owner', '-f', $remoteDump) | Out-Null
    # Never pipe a binary pg_dump through PowerShell's text redirection.
    Invoke-Docker @('cp', "dishhive-db:$remoteDump", $dumpPath) | Out-Null
    Invoke-Docker @('exec', 'dishhive-db', 'createdb', '-U', 'dishhive', '-T', 'template0', $restoreDatabase) | Out-Null
    $created = $true
    Invoke-Docker @('exec', 'dishhive-db', 'pg_restore', '-U', 'dishhive', '-d', $restoreDatabase, '--no-owner', '--exit-on-error', $remoteDump) | Out-Null
    $restoredManifest = Get-DatabaseManifest $restoreDatabase
    if ($sourceManifest -cne $restoredManifest) { throw 'Restored table counts or row/image hashes differ' }
    $sourceManifest | Set-Content (Join-Path $ArtifactDirectory 'tables.json') -Encoding utf8
    Invoke-Docker @('unpause', 'dishhive-app') | Out-Null
    $paused = $false

    # Boot the exact deployed image on the clone, with all external/background work off.
    Invoke-Docker @('run', '-d', '--name', $restoreContainer, '--network', $network,
        '-p', '127.0.0.1::5100', '--env', $restoreConnection,
        '--env', 'Database__AutoMigrate=true', '--env', 'BackgroundJobs__Enabled=false',
        '--env', 'Demo__Enabled=false', '--env', 'Ai__Provider=', '--env', 'Freezy__BaseUrl=',
        '--env', 'WebSearch__Provider=', $restoreImage) | Out-Null
    $launched = $true
    $binding = Invoke-Docker @('port', $restoreContainer, '5100/tcp')
    $baseUrl = 'http://' + @($binding)[0].Trim()
    $healthy = $false
    for ($attempt = 0; $attempt -lt 45; $attempt++) {
        try { $healthy = (Invoke-WebRequest "$baseUrl/health" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { }
        if ($healthy) { break }
        Start-Sleep -Seconds 1
    }
    if (-not $healthy) { throw 'Restored application did not become healthy' }
    $recipes = Invoke-RestMethod "$baseUrl/api/recipes/page?page=1&pageSize=1"
    $members = Invoke-RestMethod "$baseUrl/api/familymembers"
    $meals = Invoke-RestMethod "$baseUrl/api/plannedmeals?from=0001-01-01&to=9999-12-31"
    $preferences = Invoke-RestMethod "$baseUrl/api/settings/preferences"
    $afterBootManifest = Get-DatabaseManifest $restoreDatabase
    if ($restoredManifest -cne $afterBootManifest) { throw 'Restored app startup unexpectedly changed persisted data' }
    $tables = $sourceManifest | ConvertFrom-Json
    if (@($members).Count -ne @($tables | Where-Object name -eq 'FamilyMembers')[0].rows -or
        @($meals).Count -ne @($tables | Where-Object name -eq 'PlannedMeals')[0].rows) {
        throw 'Restored API row counts differ from the database manifest'
    }
    [pscustomobject]@{ outcome='passed'; completedAt=(Get-Date).ToUniversalTime().ToString('o');
        image=$restoreImage; backupSha256=(Get-FileHash $dumpPath -Algorithm SHA256).Hash;
        tables=($sourceManifest | ConvertFrom-Json).Count; recipes=$recipes.totalCount;
        members=@($members).Count; meals=@($meals).Count; preferencesReadable=($null -ne $preferences);
        exactRowsAndImages=$true; startupMigrationsPassed=$true; sourceDatabase='dishhive' } |
        ConvertTo-Json | Set-Content (Join-Path $ArtifactDirectory 'summary.json') -Encoding utf8
    Write-Host "Full restore rehearsal passed. Evidence: $ArtifactDirectory"
} finally {
    if ($paused) { Invoke-Docker @('unpause', 'dishhive-app') | Out-Null }
    if ($launched) { Invoke-Docker @('rm', '-f', $restoreContainer) | Out-Null }
    # Destructive targets are generated here and may ONLY be disposable restore names.
    if ($created -and $restoreDatabase -match '^dishhive_restore_[a-f0-9]{32}$') {
        Invoke-Docker @('exec', 'dishhive-db', 'dropdb', '-U', 'dishhive', '--force', $restoreDatabase) | Out-Null
    }
    if ($remoteDump -match '^/tmp/dishhive-restore-[a-f0-9]{32}\.dump$') {
        Invoke-Docker @('exec', 'dishhive-db', 'rm', '-f', $remoteDump) | Out-Null
    }
}
