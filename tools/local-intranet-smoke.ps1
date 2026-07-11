param(
    [string]$ApiBase = "http://localhost:5247",
    [string]$Email = "admin@nca.gov",
    [string]$Password = "ChangeThisPassword!"
)

$ErrorActionPreference = "Stop"

function Invoke-AppJson {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [object]$Body = $null,
        [Microsoft.PowerShell.Commands.WebRequestSession]$Session = $null
    )

    $params = @{
        Method = $Method
        Uri = $Uri
        ContentType = "application/json"
        UseBasicParsing = $true
    }

    if ($null -ne $Session) {
        $params.WebSession = $Session
    }

    if ($null -ne $Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20)
    }

    Invoke-RestMethod @params
}

$base = $ApiBase.TrimEnd("/")
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$stamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$reviewDisplay = "Smoke Test $stamp"

Write-Host "Checking API health at $base..."
$health = Invoke-AppJson -Method Get -Uri "$base/api/health"
if ($health.status -ne "ok") {
    throw "Unexpected health status: $($health.status)"
}

Write-Host "Logging in as $Email..."
$user = Invoke-AppJson -Method Post -Uri "$base/api/auth/login" -Session $session -Body @{
    email = $Email
    password = $Password
}

if (-not $user.id) {
    throw "Login did not return a user id."
}

$me = Invoke-AppJson -Method Get -Uri "$base/api/auth/me" -Session $session
if ($me.email -ne $Email) {
    throw "Authenticated user mismatch. Expected $Email but got $($me.email)."
}

$payload = @{
    reviewPeriod = @{
        display = $reviewDisplay
        savedBy = "local-intranet-smoke"
    }
    objectivesData = @()
    smokeTest = @{
        status = "Verified"
        updatedAt = $stamp
    }
}

Write-Host "Saving dashboard test payload..."
Invoke-AppJson -Method Put -Uri "$base/api/dashboard/me" -Session $session -Body $payload | Out-Null

Write-Host "Reading dashboard test payload..."
$dashboard = Invoke-AppJson -Method Get -Uri "$base/api/dashboard/me" -Session $session
if ($dashboard.reviewPeriod.display -ne $reviewDisplay) {
    throw "Dashboard round-trip failed. Expected '$reviewDisplay' but got '$($dashboard.reviewPeriod.display)'."
}

Write-Host "PASS local intranet smoke test"
Write-Host "User: $($me.email) [$($me.role)]"
Write-Host "Dashboard marker: $reviewDisplay"
