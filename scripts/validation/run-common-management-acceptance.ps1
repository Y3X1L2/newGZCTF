param(
    [Parameter(Mandatory = $true)]
    [string]$AdminPassword,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https?://')]
    [string]$BaseUrl,
    [string]$Marker = 'VNEXT-ACCEPT-20260720'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.CookieContainer = [System.Net.CookieContainer]::new()
$client = [System.Net.Http.HttpClient]::new($handler)
$client.BaseAddress = [uri]$BaseUrl
$client.Timeout = [TimeSpan]::FromSeconds(30)
$results = [System.Collections.Generic.List[object]]::new()
$testUserId = $null
$testTeamId = $null
$testGroupId = $null
$originalConfig = $null
$configChanged = $false

function Add-Result {
    param([string]$Step, [bool]$Passed, [string]$Detail)
    $results.Add([pscustomobject]@{
        Step = $Step
        Passed = $Passed
        Detail = $Detail
        TimeUtc = [DateTimeOffset]::UtcNow.ToString('O')
    })
}

function Invoke-Api {
    param(
        [ValidateSet('GET', 'POST', 'PUT', 'DELETE')]
        [string]$Method,
        [string]$Path,
        [object]$Body
    )

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::new($Method),
        $Path
    )
    try {
        if ($PSBoundParameters.ContainsKey('Body')) {
            $json = $Body | ConvertTo-Json -Depth 20 -Compress
            if ($Path -eq '/api/admin/users' -and -not $json.TrimStart().StartsWith('[')) {
                $json = "[$json]"
            }
            $request.Content = [System.Net.Http.StringContent]::new(
                $json,
                [System.Text.Encoding]::UTF8,
                'application/json'
            )
        }
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $responseBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            throw "$Method $Path failed: status=$([int]$response.StatusCode) body=$responseBody"
        }
        if ([string]::IsNullOrWhiteSpace($responseBody)) { return $null }
        try { return $responseBody | ConvertFrom-Json } catch { return $responseBody }
    }
    finally {
        $request.Dispose()
    }
}

$suffix = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString().Substring(5)
$testUserName = "vn${suffix}"
$testTeamName = "VN-ACT-$suffix"
$testGroupName = "$Marker-$suffix"
$testEmail = "$testUserName@example.test"
$testPassword = "Vn!aA1$([guid]::NewGuid().ToString('N'))"

try {
    Invoke-Api POST '/api/account/login' @{
        userName = 'admin'
        password = $AdminPassword
    } | Out-Null
    $profile = Invoke-Api GET '/api/account/profile'
    Add-Result 'local-admin-login' ($profile.userName -eq 'admin') "role=$($profile.role)"

    $newUser = @{
        userName = $testUserName
        password = $testPassword
        email = $testEmail
        realName = 'vNext acceptance user'
        stdNumber = "AC-$suffix"
        teamName = $testTeamName
        assignedRole = 'Student'
        studentGroupIds = @()
    }
    Invoke-Api -Method POST -Path '/api/admin/users' -Body (, $newUser) | Out-Null

    $users = Invoke-Api POST "/api/admin/users/search?hint=$([uri]::EscapeDataString($testUserName))"
    $createdUser = @($users.data) | Where-Object userName -eq $testUserName | Select-Object -First 1
    if (-not $createdUser.id) { throw 'Created test user was not returned by search.' }
    $testUserId = $createdUser.id
    Add-Result 'create-user' $true "user=$testUserName id=$testUserId"

    Invoke-Api PUT "/api/admin/users/$testUserId" @{
        userName = $testUserName
        email = $testEmail
        bio = $Marker
        realName = 'vNext acceptance user updated'
        stdNumber = "AC-$suffix"
        emailConfirmed = $true
        role = 'Student'
        studentGroupIds = @()
    } | Out-Null
    $updatedUser = Invoke-Api GET "/api/admin/users/$testUserId"
    Add-Result 'update-user' ($updatedUser.bio -eq $Marker) "bio=$($updatedUser.bio)"

    $teams = Invoke-Api POST "/api/admin/teams/search?hint=$([uri]::EscapeDataString($testTeamName))"
    $createdTeam = @($teams.data) | Where-Object name -eq $testTeamName | Select-Object -First 1
    if (-not $createdTeam.id) { throw 'Created test team was not returned by search.' }
    $testTeamId = $createdTeam.id
    Add-Result 'create-team' $true "team=$testTeamName id=$testTeamId"

    Invoke-Api PUT "/api/admin/teams/$testTeamId" @{
        name = $testTeamName
        bio = $Marker
        locked = $true
    } | Out-Null
    $teamsAfterUpdate = Invoke-Api POST "/api/admin/teams/search?hint=$([uri]::EscapeDataString($testTeamName))"
    $updatedTeam = @($teamsAfterUpdate.data) | Where-Object id -eq $testTeamId | Select-Object -First 1
    Add-Result 'update-team' ($updatedTeam.bio -eq $Marker -and $updatedTeam.locked) "locked=$($updatedTeam.locked)"

    $group = Invoke-Api POST '/api/admin/student-groups' @{
        name = $testGroupName
        description = 'vNext acceptance temporary group'
    }
    $testGroupId = $group.id
    Add-Result 'create-student-group' ([bool]$testGroupId) "group=$testGroupName id=$testGroupId"

    Invoke-Api POST "/api/admin/student-groups/$testGroupId/members" @{
        studentId = $testUserId
        note = $Marker
    } | Out-Null
    Invoke-Api PUT "/api/admin/student-groups/$testGroupId" @{
        name = $testGroupName
        description = 'vNext acceptance temporary group updated'
    } | Out-Null
    $groupAfterUpdate = Invoke-Api GET "/api/admin/student-groups/$testGroupId"
    $memberFound = @($groupAfterUpdate.members) | Where-Object studentId -eq $testUserId
    Add-Result 'update-student-group-and-bind-member' ([bool]$memberFound) "members=$(@($groupAfterUpdate.members).Count)"

    $originalConfig = Invoke-Api GET '/api/admin/config'
    $testConfig = $originalConfig | ConvertTo-Json -Depth 20 | ConvertFrom-Json
    $originalFooter = [string]$testConfig.globalConfig.footerInfo
    $testConfig.globalConfig.footerInfo = if ($originalFooter) {
        "$originalFooter`n$Marker"
    } else {
        $Marker
    }
    Invoke-Api PUT '/api/admin/config' $testConfig | Out-Null
    $configChanged = $true
    $configAfterUpdate = Invoke-Api GET '/api/admin/config'
    Add-Result 'update-system-config' ([string]$configAfterUpdate.globalConfig.footerInfo -like "*$Marker*") 'temporary footer update succeeded'

    Invoke-Api PUT '/api/admin/config' $originalConfig | Out-Null
    $configChanged = $false
    $configAfterRestore = Invoke-Api GET '/api/admin/config'
    Add-Result 'restore-system-config' ([string]$configAfterRestore.globalConfig.footerInfo -eq [string]$originalConfig.globalConfig.footerInfo) 'footer restored'
}
finally {
    if ($configChanged -and $originalConfig) {
        try { Invoke-Api PUT '/api/admin/config' $originalConfig | Out-Null } catch {}
    }
    if ($testGroupId) {
        try {
            Invoke-Api DELETE "/api/admin/student-groups/$testGroupId" | Out-Null
            Add-Result 'archive-test-student-group' $true "groupId=$testGroupId"
        } catch { Add-Result 'archive-test-student-group' $false $_.Exception.Message }
    }
    if ($testTeamId) {
        try {
            Invoke-Api DELETE "/api/admin/teams/$testTeamId" | Out-Null
            Add-Result 'delete-test-team' $true "teamId=$testTeamId"
        } catch { Add-Result 'delete-test-team' $false $_.Exception.Message }
    }
    if ($testUserId) {
        try {
            Invoke-Api DELETE "/api/admin/users/$testUserId" | Out-Null
            Add-Result 'delete-test-user' $true "userId=$testUserId"
        } catch { Add-Result 'delete-test-user' $false $_.Exception.Message }
    }
}

$client.Dispose()
$handler.Dispose()
$results | ConvertTo-Json -Depth 6
if ($results.Where({ -not $_.Passed }).Count -gt 0) { exit 1 }
