param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts\publish",
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "backend\Appraisal.Api\Appraisal.Api.csproj"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$publishDir = Join-Path $repoRoot (Join-Path $OutputRoot "nca-appraisal-api-$timestamp")
$zipPath = "$publishDir.zip"
$buildOutputRoot = Join-Path $repoRoot (Join-Path "artifacts\build" $timestamp)
$buildOutputProperty = ($buildOutputRoot -replace "\\", "/") + "/"

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host "Publishing NCA Appraisal API..."
$publishArgs = @(
    "publish",
    $projectPath,
    "--configuration",
    $Configuration,
    "--output",
    $publishDir,
    "--no-self-contained",
    "/p:UseAppHost=false",
    "/p:BaseOutputPath=$buildOutputProperty"
)

& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path (Join-Path $publishDir "wwwroot\index.html"))) {
    throw "Publish completed, but wwwroot\index.html was not found in the output."
}

if (-not $NoZip) {
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
}

Write-Host ""
Write-Host "Publish folder: $publishDir"
if (-not $NoZip) {
    Write-Host "Deployment zip: $zipPath"
}
Write-Host ""
Write-Host "Next: copy the zip/folder to the NCA server and apply appsettings.Production.json or environment variables."
