<#
.SYNOPSIS
    Builds and pushes the Dishhive container images to Docker Hub.

.DESCRIPTION
    Builds the two custom images from this repo (app + scraper) and pushes
    them to Docker Hub as <Username>/dishhive-app and <Username>/dishhive-scraper.
    The db (postgres) and searxng services use stock upstream images and are
    not pushed.

    Requires Docker Desktop to be running and an authenticated session
    (run `docker login` once beforehand; the script checks and prompts if needed).

.PARAMETER Username
    Your Docker Hub username (the namespace to push under).

.PARAMETER Tag
    Version tag for the images (e.g. 1.2.0). Defaults to the short git commit
    hash. The images are always additionally tagged and pushed as :latest.

.PARAMETER SkipBuild
    Skip the build and only tag/push existing local images
    (dishhive-app:latest / dishhive-scraper:latest from `docker compose build`).

.EXAMPLE
    .\scripts\push-to-dockerhub.ps1 -Username naliath
    .\scripts\push-to-dockerhub.ps1 -Username naliath -Tag 1.2.0
    .\scripts\push-to-dockerhub.ps1 -Username naliath -SkipBuild
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Username,

    [string]$Tag,

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

# Docker Hub namespaces are lowercase-only; an uppercase letter makes Docker
# parse the name as a registry hostname (e.g. "Naliath:443") instead.
$Username = $Username.ToLowerInvariant()

# Repo root = parent of this script's folder, so the script works from anywhere
$repoRoot = Split-Path -Parent $PSScriptRoot

# The two custom-built services: local compose image name -> Dockerfile
$images = @(
    @{ Local = 'dishhive-app';     Dockerfile = 'docker/app.Dockerfile' },
    @{ Local = 'dishhive-scraper'; Dockerfile = 'docker/scraper.Dockerfile' }
)

function Assert-LastExit([string]$What) {
    if ($LASTEXITCODE -ne 0) {
        Write-Error "$What failed (exit code $LASTEXITCODE)."
    }
}

# --- Preflight: daemon running? ---------------------------------------------
docker version --format '{{.Server.Version}}' | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error 'Docker daemon is not reachable. Start Docker Desktop and try again.'
}

# --- Preflight: logged in? ---------------------------------------------------
# `docker login` with no args re-uses stored credentials (fast no-op) and
# prompts interactively only when none are stored.
docker login
Assert-LastExit 'docker login'

# --- Default tag: short git commit hash --------------------------------------
if (-not $Tag) {
    $Tag = (git -C $repoRoot rev-parse --short HEAD).Trim()
    Assert-LastExit 'git rev-parse'
    Write-Host "No -Tag given, using git commit: $Tag"
}

# --- Build --------------------------------------------------------------------
if (-not $SkipBuild) {
    foreach ($img in $images) {
        Write-Host "`nBuilding $($img.Local) from $($img.Dockerfile)..." -ForegroundColor Cyan
        docker build -f (Join-Path $repoRoot $img.Dockerfile) -t "$($img.Local):latest" $repoRoot
        Assert-LastExit "docker build $($img.Local)"
    }
}

# --- Tag + push ---------------------------------------------------------------
$pushed = @()
foreach ($img in $images) {
    $remote = "$Username/$($img.Local)"

    foreach ($t in @($Tag, 'latest')) {
        docker tag "$($img.Local):latest" "${remote}:$t"
        Assert-LastExit "docker tag ${remote}:$t"

        Write-Host "`nPushing ${remote}:$t..." -ForegroundColor Cyan
        docker push "${remote}:$t"
        Assert-LastExit "docker push ${remote}:$t"

        $pushed += "${remote}:$t"
    }
}

Write-Host "`nDone. Pushed:" -ForegroundColor Green
$pushed | ForEach-Object { Write-Host "  $_" }
