param(
    [string]$ApiBase = "http://localhost:5247",
    [string]$AdminEmail = "admin@nca.gov",
    [string]$AdminPassword = "ChangeThisPassword!",
    [string]$TestPassword = "RoleTest2026!"
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
    if ($null -ne $Session) { $params.WebSession = $Session }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20) }

    try {
        Invoke-RestMethod @params
    } catch {
        $status = if ($_.Exception.Response -and $_.Exception.Response.StatusCode) { [int]$_.Exception.Response.StatusCode } else { "no-status" }
        throw "$Method $Uri failed with status $status. $($_.Exception.Message)"
    }
}

function Invoke-AppStatus {
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
    if ($null -ne $Session) { $params.WebSession = $Session }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20) }

    try {
        $response = Invoke-WebRequest @params
        return [int]$response.StatusCode
    } catch {
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            return [int]$_.Exception.Response.StatusCode
        }
        throw
    }
}

function Assert-Equal {
    param([object]$Actual, [object]$Expected, [string]$Message)
    if ($Actual -ne $Expected) {
        throw "$Message Expected '$Expected' but got '$Actual'."
    }
}

function Assert-UserVisible {
    param([array]$Users, [string]$Email, [string]$Context)
    if (-not ($Users | Where-Object { $_.email -eq $Email })) {
        throw "$Context should include $Email."
    }
}

function Assert-UserHidden {
    param([array]$Users, [string]$Email, [string]$Context)
    if ($Users | Where-Object { $_.email -eq $Email }) {
        throw "$Context should not include $Email."
    }
}

function As-FlatArray {
    param([object]$Value)
    $items = @()
    foreach ($item in @($Value)) {
        if ($item -is [System.Array]) {
            $items += As-FlatArray $item
        } else {
            $items += $item
        }
    }
    return $items
}

function Login-App {
    param([string]$Email, [string]$Password)
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $user = Invoke-AppJson -Method Post -Uri "$base/api/auth/login" -Session $session -Body @{
        email = $Email
        password = $Password
    }
    return @{ Session = $session; User = $user }
}

$base = $ApiBase.TrimEnd("/")

Write-Host "Checking API health at $base..."
$health = Invoke-AppJson -Method Get -Uri "$base/api/health"
Assert-Equal $health.status "ok" "Health check failed."

Write-Host "Logging in as administrator..."
$adminLogin = Login-App -Email $AdminEmail -Password $AdminPassword
$adminSession = $adminLogin.Session

$units = @(
    @{ division = "RIPS"; name = "Innovation" },
    @{ division = "RIPS"; name = "Strategy" },
    @{ division = "Legal"; name = "Legal Unit" }
)

foreach ($unit in $units) {
    Invoke-AppJson -Method Post -Uri "$base/api/units" -Session $adminSession -Body $unit | Out-Null
}

$testUsers = @(
    @{ key = "employee"; email = "role.employee@nca.local"; displayName = "Role Test Employee"; role = "employee"; division = "RIPS"; unit = "Innovation" },
    @{ key = "unitLead"; email = "role.unitlead@nca.local"; displayName = "Role Test Unit Lead"; role = "unitLead"; division = "RIPS"; unit = "Innovation" },
    @{ key = "otherUnit"; email = "role.otherunit@nca.local"; displayName = "Role Test Other Unit"; role = "employee"; division = "RIPS"; unit = "Strategy" },
    @{ key = "otherDivision"; email = "role.otherdivision@nca.local"; displayName = "Role Test Other Division"; role = "employee"; division = "Legal"; unit = "Legal Unit" },
    @{ key = "divisionalHead"; email = "role.divisionalhead@nca.local"; displayName = "Role Test Divisional Head"; role = "divisionalHead"; division = "RIPS"; unit = "Innovation" },
    @{ key = "director"; email = "role.director@nca.local"; displayName = "Role Test Director"; role = "director"; division = "RIPS"; unit = "Innovation" },
    @{ key = "secretariat"; email = "role.secretariat@nca.local"; displayName = "Role Test Secretariat"; role = "secretariat"; division = "RIPS"; unit = "Innovation" },
    @{ key = "deputy"; email = "role.deputy@nca.local"; displayName = "Role Test Deputy Director General"; role = "deputyDirectorGeneral"; division = "RIPS"; unit = "Innovation" }
)

Write-Host "Creating/updating deterministic role test users..."
$existingUsers = As-FlatArray (Invoke-AppJson -Method Get -Uri "$base/api/users" -Session $adminSession)
$usersByKey = @{}

foreach ($testUser in $testUsers) {
    $email = $testUser["email"]
    $existing = $null
    foreach ($candidate in $existingUsers) {
        if ($candidate.email -eq $email) {
            $existing = $candidate
            break
        }
    }
    if (-not $existing) {
        $created = Invoke-AppJson -Method Post -Uri "$base/api/users" -Session $adminSession -Body @{
            email = $testUser["email"]
            displayName = $testUser["displayName"]
            password = $TestPassword
            division = $testUser["division"]
            unit = $testUser["unit"]
            role = $testUser["role"]
        }
        $existing = $created
    } else {
        Invoke-AppJson -Method Patch -Uri "$base/api/users/$($existing.id)/assignment" -Session $adminSession -Body @{
            division = $testUser["division"]
            unit = $testUser["unit"]
            role = $testUser["role"]
        } | Out-Null
        Invoke-AppJson -Method Patch -Uri "$base/api/users/$($existing.id)/password" -Session $adminSession -Body @{
            newPassword = $TestPassword
        } | Out-Null
    }
}

$existingUsers = As-FlatArray (Invoke-AppJson -Method Get -Uri "$base/api/users" -Session $adminSession)
$userIdsByEmail = @{}
foreach ($candidate in $existingUsers) {
    $userIdsByEmail[[string]$candidate.email] = [string]$candidate.id
}
foreach ($testUser in $testUsers) {
    $email = $testUser["email"]
    $key = [string]$testUser["key"]
    if (-not $userIdsByEmail.ContainsKey($email)) {
        throw "Could not find prepared test user $email after setup."
    }
    $usersByKey[$key] = [string]$userIdsByEmail[$email]
}

Write-Host "Checking role visibility and dashboard boundaries..."

$employeeId = [string]$usersByKey["employee"]
$unitLeadId = [string]$usersByKey["unitLead"]
$otherUnitId = [string]$usersByKey["otherUnit"]
$otherDivisionId = [string]$usersByKey["otherDivision"]

$employee = Login-App -Email "role.employee@nca.local" -Password $TestPassword
$employeeUsers = As-FlatArray (Invoke-AppJson -Method Get -Uri "$base/api/users" -Session $employee.Session)
Assert-Equal $employee.User.role "employee" "Employee role mismatch."
Assert-UserVisible $employeeUsers "role.employee@nca.local" "Employee user list"
Assert-UserHidden $employeeUsers "role.unitlead@nca.local" "Employee user list"
Assert-Equal (Invoke-AppStatus -Method Get -Uri "$base/api/dashboard/$unitLeadId" -Session $employee.Session) 403 "Employee should not read unit lead dashboard."

$unitLead = Login-App -Email "role.unitlead@nca.local" -Password $TestPassword
$unitLeadUsers = As-FlatArray (Invoke-AppJson -Method Get -Uri "$base/api/users" -Session $unitLead.Session)
Assert-Equal $unitLead.User.role "unitLead" "Unit Lead role mismatch."
Assert-UserVisible $unitLeadUsers "role.employee@nca.local" "Unit Lead user list"
Assert-UserVisible $unitLeadUsers "role.unitlead@nca.local" "Unit Lead user list"
Assert-UserHidden $unitLeadUsers "role.otherunit@nca.local" "Unit Lead user list"
Assert-Equal (Invoke-AppStatus -Method Get -Uri "$base/api/dashboard/$employeeId" -Session $unitLead.Session) 200 "Unit Lead should read same-unit employee dashboard."
Assert-Equal (Invoke-AppStatus -Method Get -Uri "$base/api/dashboard/$otherUnitId" -Session $unitLead.Session) 403 "Unit Lead should not read another unit dashboard."
Assert-Equal (Invoke-AppStatus -Method Get -Uri "$base/api/dashboard/$otherDivisionId" -Session $unitLead.Session) 403 "Unit Lead should not read another division dashboard."

foreach ($roleKey in @("divisionalHead", "director", "secretariat", "deputy")) {
    $roleMatches = @($testUsers | Where-Object { $_["key"] -eq $roleKey })
    $roleUser = $roleMatches[0]
    $login = Login-App -Email $roleUser["email"] -Password $TestPassword
    $visibleUsers = As-FlatArray (Invoke-AppJson -Method Get -Uri "$base/api/users" -Session $login.Session)
    Assert-Equal $login.User.role $roleUser["role"] "$($roleUser["role"]) role mismatch."
    Assert-UserVisible $visibleUsers "role.employee@nca.local" "$($roleUser["role"]) user list"
    Assert-UserVisible $visibleUsers "role.otherunit@nca.local" "$($roleUser["role"]) user list"
    Assert-UserHidden $visibleUsers "role.otherdivision@nca.local" "$($roleUser["role"]) user list"
    Assert-Equal (Invoke-AppStatus -Method Get -Uri "$base/api/dashboard/$employeeId" -Session $login.Session) 200 "$($roleUser["role"]) should read same-division employee dashboard."
    Assert-Equal (Invoke-AppStatus -Method Get -Uri "$base/api/dashboard/$otherUnitId" -Session $login.Session) 200 "$($roleUser["role"]) should read same-division other-unit dashboard."
    Assert-Equal (Invoke-AppStatus -Method Get -Uri "$base/api/dashboard/$otherDivisionId" -Session $login.Session) 403 "$($roleUser["role"]) should not read another division dashboard."
}

Write-Host "PASS role access smoke test"
Write-Host "Verified: employee own-only list, unit lead unit scope, director-level division scope, and cross-division denial."
