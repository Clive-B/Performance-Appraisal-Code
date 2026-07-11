param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,
    [string]$DatabaseHost = "localhost",
    [int]$DatabasePort = 5432,
    [string]$DatabaseName = "nca_appraisal",
    [string]$DatabaseUser = "nca_appraisal_app",
    [string]$PostgresBin = "C:\Program Files\PostgreSQL\16\bin",
    [string]$StorageRoot = "backend\Appraisal.Api\App_Data\attachments",
    [switch]$SkipDatabaseRestore,
    [switch]$RestoreAttachments
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$resolvedBackup = Resolve-Path $BackupPath
$databaseDump = Join-Path $resolvedBackup "database.dump"
$attachmentBackup = Join-Path $resolvedBackup "attachments"
$pgRestore = Join-Path $PostgresBin "pg_restore.exe"
$storagePath = Join-Path $repoRoot $StorageRoot

if (-not $SkipDatabaseRestore) {
    if (-not (Test-Path $pgRestore)) {
        throw "pg_restore.exe was not found at $pgRestore. Pass -PostgresBin with the installed PostgreSQL bin path."
    }
    if (-not (Test-Path $databaseDump)) {
        throw "Database dump was not found at $databaseDump."
    }

    Write-Host "Restoring PostgreSQL database $DatabaseName..."
    & $pgRestore `
        --host $DatabaseHost `
        --port $DatabasePort `
        --username $DatabaseUser `
        --dbname $DatabaseName `
        --clean `
        --if-exists `
        $databaseDump
}

if ($RestoreAttachments) {
    if (-not (Test-Path $attachmentBackup)) {
        throw "Attachment backup was not found at $attachmentBackup."
    }

    New-Item -ItemType Directory -Force -Path $storagePath | Out-Null
    Write-Host "Restoring attachments into $storagePath..."
    Copy-Item -Path (Join-Path $attachmentBackup "*") -Destination $storagePath -Recurse -Force
}

Write-Host ""
Write-Host "Restore completed."
