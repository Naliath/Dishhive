param(
  [string]$BaseUrl = 'http://127.0.0.1:4300',
  [string]$OutputDir = (Join-Path $env:TEMP 'dishhive-mobile-audit'),
  [int]$Width = 360,
  [int]$Height = 800,
  [string]$AdditionalRoutes = ''
)

$ErrorActionPreference = 'Stop'

$auditScript = Join-Path $PSScriptRoot 'audit-mobile-ui.cjs'
$cacheRoot = Join-Path $env:TEMP 'dishhive-mobile-audit-playwright'
$playwrightPath = Join-Path $cacheRoot 'node_modules\playwright-core'

if (-not (Test-Path $playwrightPath)) {
  New-Item -ItemType Directory -Force $cacheRoot | Out-Null
  npm install --prefix $cacheRoot playwright-core@1.55.0 --no-save --no-package-lock | Out-Null
}

New-Item -ItemType Directory -Force $OutputDir | Out-Null
$env:PLAYWRIGHT_CORE_PATH = $playwrightPath

$auditArguments = @(
  $auditScript,
  '--base-url', $BaseUrl,
  '--output', $OutputDir,
  '--width', $Width,
  '--height', $Height
)

if ($AdditionalRoutes) {
  $auditArguments += @('--additional-routes', $AdditionalRoutes)
}

node @auditArguments

if ($LASTEXITCODE -ne 0) {
  throw "Mobile audit failed with exit code $LASTEXITCODE"
}

Write-Host "Audit artifacts: $OutputDir"
