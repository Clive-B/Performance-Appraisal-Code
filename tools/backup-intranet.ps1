param(
    [string]$DatabaseHost = "localhost",
    [int]$DatabasePort = 5432,
    [string]$DatabaseName = "nca_appraisal",
    [string]$DatabaseUser = "nca_appraisal_app",
    [string]$PostgresBin = "C:\Program Files\PostgreSQL\16\bin",
    [string]$BackupRoot = "backups",
    [string]$StorageRoot = "backend\Appraisal.Api\App_Data\attachments",
    [switch]$SkipDatabaseDump
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupDir = Join-Path $repoRoot (Join-Path $BackupRoot "nca-appraisal-$timestamp")
$databaseDump = Join-Path $backupDir "database.dump"
$attachmentBackup = Join-Path $backupDir "attachments"
$manifestPath = Join-Path $backupDir "backup-manifest.json"
$pgDump = Join-Path $PostgresBin "pg_dump.exe"
$storagePath = Join-Path $repoRoot $StorageRoot

New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

if (-not $SkipDatabaseDump) {
    if (-not (Test-Path $pgDump)) {
        throw "pg_dump.exe was not found at $pgDump. Pass -PostgresBin with the installed PostgreSQL bin path."
    }

    Write-Host "Backing up PostgreSQL database $DatabaseName..."
    & $pgDump `
        --host $DatabaseHost `
        --port $DatabasePort `
        --username $DatabaseUser `
        --format custom `
        --file $databaseDump `
        $DatabaseName
}

if (Test-Path $storagePath) {
    Write-Host "Copying attachment storage..."
    Copy-Item -Path $storagePath -Destination $attachmentBackup -Recurse -Force
}

$manifest = [ordered]@{
    createdAt = (Get-Date).ToString("o")
    databaseHost = $DatabaseHost
    databasePort = $DatabasePort
    databaseName = $DatabaseName
    databaseUser = $DatabaseUser
    databaseDump = if (Test-Path $databaseDump) { "database.dump" } else { $null }
    attachments = if (Test-Path $attachmentBackup) { "attachments" } else { $null }
}

$manifest | ConvertTo-Json -Depth 4 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host ""
Write-Host "Backup folder: $backupDir"
Write-Host "Manifest: $manifestPath"
