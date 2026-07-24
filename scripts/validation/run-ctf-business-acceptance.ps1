param(
    [Parameter(Mandatory = $true)]
    [string]$AdminPassword,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https?://')]
    [string]$BaseUrl,
    [int]$SourceGameId = 23,
    [string]$Marker = 'VNEXT-CTF-ACCEPT-20260720'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

function New-ApiClient {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.CookieContainer = [System.Net.CookieContainer]::new()
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.BaseAddress = [uri]$BaseUrl
    $client.Timeout = [TimeSpan]::FromMinutes(12)
    [pscustomobject]@{ Client = $client; Handler = $handler }
}

function Invoke-Api {
    param(
        [System.Net.Http.HttpClient]$Client,
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
            $json = $Body | ConvertTo-Json -Depth 30 -Compress
            if (($Path -eq '/api/admin/users' -or $Path.EndsWith('/flags')) -and
                -not $json.TrimStart().StartsWith('[')) {
                $json = "[$json]"
            }
            $request.Content = [System.Net.Http.StringContent]::new(
                $json,
                [System.Text.Encoding]::UTF8,
                'application/json'
            )
        }
        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $data = $null
        if (-not [string]::IsNullOrWhiteSpace($raw)) {
            try { $data = $raw | ConvertFrom-Json } catch { $data = $raw }
        }
        if (-not $response.IsSuccessStatusCode) {
            throw "$Method $Path failed: status=$([int]$response.StatusCode) body=$raw"
        }
        [pscustomobject]@{ Status = [int]$response.StatusCode; Data = $data; Raw = $raw }
    }
    finally {
        $request.Dispose()
    }
}

function Invoke-FileUpload {
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$FilePath,
        [string]$FileName
    )

    $content = [System.Net.Http.MultipartFormDataContent]::new()
    $stream = [System.IO.File]::OpenRead($FilePath)
    $fileContent = [System.Net.Http.StreamContent]::new($stream)
    $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new('text/plain')
    $content.Add($fileContent, 'files', $FileName)
    try {
        $uploadTask = $Client.PostAsync(
            "/api/assets?filename=$([uri]::EscapeDataString($FileName))",
            $content
        )
        $response = $uploadTask.GetAwaiter().GetResult()
        $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            throw "attachment upload failed: status=$([int]$response.StatusCode) body=$raw"
        }
        return $raw | ConvertFrom-Json
    }
    finally {
        $content.Dispose()
        $fileContent.Dispose()
        $stream.Dispose()
    }
}

function Wait-Queue {
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$TicketId,
        [int]$TimeoutSeconds = 300
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $status = (Invoke-Api $Client GET "/api/v1/deployment-queue/$TicketId").Data
        $value = [string]$status.status
        if ($value -in @('3', 'Completed', 'Succeeded')) { return $status }
        if ($value -in @('4', '5', 'Failed', 'Cancelled')) {
            throw "queue $TicketId ended with status=$value error=$($status.errorMessage)"
        }
        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "queue $TicketId timed out"
}

function Wait-ChallengeEntry {
    param(
        [System.Net.Http.HttpClient]$Client,
        [int]$GameId,
        [int]$ChallengeId,
        [int]$TimeoutSeconds = 90
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $detail = (Invoke-Api $Client GET "/api/game/$GameId/challenges/$ChallengeId").Data
        if ($detail.context.instanceEntry) { return $detail }
        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "challenge $ChallengeId did not expose an entry"
}

function Wait-VmReady {
    param(
        [System.Net.Http.HttpClient]$Client,
        [int]$GameId,
        [int]$ChallengeId,
        [int]$TimeoutSeconds = 600
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $status = (Invoke-Api $Client GET "/api/game/$GameId/vm/$ChallengeId").Data
        if ([string]$status.status -in @('1', 'Running')) { return $status }
        if ([string]$status.status -in @('4', 'Error') -or [string]$status.stage -eq 'error') {
            throw "VM entered error state: $($status.stageMessage)"
        }
        Start-Sleep -Seconds 5
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "VM challenge $ChallengeId timed out"
}

function Resolve-EntryUri {
    param([string]$Entry)
    if ([string]::IsNullOrWhiteSpace($Entry)) { throw 'Instance entry is empty.' }
    $absolute = $null
    if ([uri]::TryCreate($Entry, [UriKind]::Absolute, [ref]$absolute) -and $absolute.Scheme) {
        return $absolute.AbsoluteUri
    }
    if ($Entry.StartsWith('/')) { return "$BaseUrl$Entry" }
    return "http://$Entry"
}

function Wait-HttpEndpoint {
    param(
        [string]$Uri,
        [int]$TimeoutSeconds = 30
    )

    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds(3)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastStatus = $null
    $lastError = $null
    try {
        do {
            try {
                $response = $client.GetAsync($Uri).GetAwaiter().GetResult()
                $lastStatus = [int]$response.StatusCode
                $response.Dispose()
                if ($lastStatus -notin @(502, 503, 504)) {
                    return [pscustomobject]@{ Reachable = $true; Status = $lastStatus; Error = $null }
                }
            }
            catch {
                $lastError = $_.Exception.Message
            }
            Start-Sleep -Seconds 1
        } while ([DateTimeOffset]::UtcNow -lt $deadline)

        return [pscustomobject]@{ Reachable = $false; Status = $lastStatus; Error = $lastError }
    }
    finally {
        $client.Dispose()
    }
}

function Add-Result {
    param([string]$Step, [bool]$Passed, [string]$Detail)
    $script:results.Add([pscustomobject]@{
        Step = $Step
        Passed = $Passed
        Detail = $Detail
        TimeUtc = [DateTimeOffset]::UtcNow.ToString('O')
    })
    Write-Host "[$(if ($Passed) { 'PASS' } else { 'FAIL' })] $Step - $Detail"
}

$adminRuntime = New-ApiClient
$userRuntime = New-ApiClient
$admin = $adminRuntime.Client
$user = $userRuntime.Client
$results = [System.Collections.Generic.List[object]]::new()
$gameId = $null
$userId = $null
$teamId = $null
$assetHash = $null
$dockerChallengeId = $null
$vmChallengeId = $null
$tempFile = Join-Path $env:TEMP "vnext-ctf-attachment-$([guid]::NewGuid().ToString('N')).txt"
$suffix = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString().Substring(5)
$testUserName = "ctf${suffix}"
$testTeamName = "CTF-ACT-$suffix"
$testEmail = "$testUserName@example.test"
$testPassword = "Vn!aA1$([guid]::NewGuid().ToString('N'))"
$staticFlag = "flag{VNEXT_STATIC_$suffix}"
$dynamicFlagTemplate = 'flag{vnext-acceptance-[TEAM_HASH]-20260720}'
$vmFlag = "flag{VNEXT_VM_$suffix}"

try {
    Invoke-Api $admin POST '/api/account/login' @{
        userName = 'admin'
        password = $AdminPassword
    } | Out-Null
    Add-Result 'admin-login' $true 'authenticated'

    $sourceChallenges = (Invoke-Api $admin GET "/api/edit/games/$SourceGameId/challenges").Data
    $dockerSource = @($sourceChallenges) | Where-Object {
        $_.isEnabled -and $_.type -eq 'DynamicContainer' -and $_.environment -eq 'Docker' -and $_.exposePort -eq 80
    } | Select-Object -First 1
    $vmSource = @($sourceChallenges) | Where-Object {
        $_.isEnabled -and $_.environment -eq 'WindowsVM' -and $_.imageTemplateId
    } | Select-Object -First 1
    if (-not $dockerSource -or -not $vmSource) { throw 'Source Docker or Windows VM challenge was not found.' }
    Add-Result 'source-runtime-selection' $true "docker=$($dockerSource.id) vm=$($vmSource.id)"

    $now = [DateTimeOffset]::UtcNow
    $game = (Invoke-Api $admin POST '/api/edit/games' @{
        title = "$Marker-$suffix"
        hidden = $true
        summary = 'Temporary vNext CTF acceptance game'
        content = 'Automated acceptance game. Safe to delete.'
        acceptWithoutReview = $true
        writeupRequired = $false
        teamMemberCountLimit = 3
        containerCountLimit = 2
        practiceMode = $false
        isTest = $true
        start = $now.AddMinutes(-2).ToUnixTimeMilliseconds()
        end = $now.AddHours(2).ToUnixTimeMilliseconds()
        writeupDeadline = $now.AddHours(3).ToUnixTimeMilliseconds()
        bloodBonus = 0
        gameType = 'Jeopardy'
    }).Data
    $gameId = $game.id
    Add-Result 'create-test-game' ([bool]$gameId) "gameId=$gameId"

    $staticChallenge = (Invoke-Api $admin POST "/api/edit/games/$gameId/challenges" @{
        title = 'vNext static attachment'
        category = 'Misc'
        type = 'StaticAttachment'
        environment = 'None'
        isEnabled = $false
        score = 100
        originalScore = 100
        minScore = 100
    }).Data
    Invoke-Api $admin POST "/api/edit/games/$gameId/challenges/$($staticChallenge.id)/flags" @{
        flag = $staticFlag
        orderIndex = 0
        answerType = 'Flag'
        scoreMode = 'InheritDecay'
    } | Out-Null

    [System.IO.File]::WriteAllText($tempFile, "$Marker attachment content", [System.Text.Encoding]::UTF8)
    $uploaded = @(Invoke-FileUpload $admin $tempFile 'vnext-acceptance.txt')[0]
    $assetHash = $uploaded.hash
    Invoke-Api $admin POST "/api/edit/games/$gameId/challenges/$($staticChallenge.id)/attachment" @{
        attachmentType = 'Local'
        fileHash = $assetHash
    } | Out-Null
    Invoke-Api $admin PUT "/api/edit/games/$gameId/challenges/$($staticChallenge.id)" @{
        title = 'vNext static attachment'
        content = 'Download the attachment and submit the static flag.'
        category = 'Misc'
        isEnabled = $true
        originalScore = 100
        minScoreRate = 1
        difficulty = 1
        submissionLimit = 0
        environment = 'None'
    } | Out-Null
    Add-Result 'configure-static-attachment' $true "challengeId=$($staticChallenge.id) asset=$assetHash"

    $dockerChallenge = (Invoke-Api $admin POST "/api/edit/games/$gameId/challenges" @{
        title = 'vNext dynamic Docker'
        category = 'Web'
        type = 'DynamicContainer'
        containerImage = $dockerSource.containerImage
        exposePort = $dockerSource.exposePort
        environment = 'Docker'
        isEnabled = $false
        score = 200
        originalScore = 200
        minScore = 200
    }).Data
    $dockerChallengeId = $dockerChallenge.id
    Invoke-Api $admin POST "/api/edit/games/$gameId/challenges/$dockerChallengeId/flags" @{
        flag = 'flag{dynamic-container-placeholder}'
        orderIndex = 0
        answerType = 'Flag'
        scoreMode = 'InheritDecay'
    } | Out-Null
    Invoke-Api $admin PUT "/api/edit/games/$gameId/challenges/$dockerChallengeId" @{
        title = 'vNext dynamic Docker'
        content = 'Start the Docker instance and submit its assigned flag.'
        flagTemplate = $dynamicFlagTemplate
        category = 'Web'
        isEnabled = $true
        containerImage = $dockerSource.containerImage
        exposePort = $dockerSource.exposePort
        memoryLimit = 256
        cpuCount = 2
        storageLimit = 256
        networkMode = 'Open'
        originalScore = 200
        minScoreRate = 1
        difficulty = 1
        submissionLimit = 0
        environment = 'Docker'
    } | Out-Null
    Add-Result 'configure-dynamic-docker' $true "challengeId=$dockerChallengeId image=$($dockerSource.containerImage)"

    $vmChallenge = (Invoke-Api $admin POST "/api/edit/games/$gameId/challenges" @{
        title = 'vNext Windows VM'
        category = 'Misc'
        type = 'StaticContainer'
        environment = 'WindowsVM'
        imageTemplateId = $vmSource.imageTemplateId
        isEnabled = $false
        score = 300
        originalScore = 300
        minScore = 300
    }).Data
    $vmChallengeId = $vmChallenge.id
    Invoke-Api $admin POST "/api/edit/games/$gameId/challenges/$vmChallengeId/flags" @{
        flag = $vmFlag
        orderIndex = 0
        answerType = 'Flag'
        scoreMode = 'InheritDecay'
    } | Out-Null
    Invoke-Api $admin PUT "/api/edit/games/$gameId/challenges/$vmChallengeId" @{
        title = 'vNext Windows VM'
        content = 'Start the Windows VM and verify the RDP entry.'
        category = 'Misc'
        isEnabled = $true
        imageTemplateId = $vmSource.imageTemplateId
        memoryLimit = 4096
        cpuCount = 2
        originalScore = 300
        minScoreRate = 1
        difficulty = 1
        submissionLimit = 0
        environment = 'WindowsVM'
    } | Out-Null
    Add-Result 'configure-windows-vm' $true "challengeId=$vmChallengeId imageTemplateId=$($vmSource.imageTemplateId)"

    $newUser = @{
        userName = $testUserName
        password = $testPassword
        email = $testEmail
        realName = 'vNext CTF acceptance user'
        stdNumber = "CTF-$suffix"
        teamName = $testTeamName
        assignedRole = 'Student'
        studentGroupIds = @()
    }
    Invoke-Api $admin POST '/api/admin/users' $newUser | Out-Null
    $users = (Invoke-Api $admin POST "/api/admin/users/search?hint=$([uri]::EscapeDataString($testUserName))").Data
    $createdUser = @($users.data) | Where-Object userName -eq $testUserName | Select-Object -First 1
    $userId = $createdUser.id
    $teams = (Invoke-Api $admin POST "/api/admin/teams/search?hint=$([uri]::EscapeDataString($testTeamName))").Data
    $createdTeam = @($teams.data) | Where-Object name -eq $testTeamName | Select-Object -First 1
    $teamId = $createdTeam.id
    Add-Result 'create-player-and-team' ([bool]$userId -and [bool]$teamId) "userId=$userId teamId=$teamId"

    Invoke-Api $user POST '/api/account/login' @{
        userName = $testUserName
        password = $testPassword
    } | Out-Null
    $check = (Invoke-Api $user GET "/api/game/$gameId/check").Data
    [Nullable[int]]$divisionId = $null
    $joinableDivisions = @($check.joinableDivisions)
    if ($joinableDivisions.Count -gt 0) {
        $divisionId = [int]$joinableDivisions[0]
    }
    Invoke-Api -Client $user -Method POST -Path "/api/game/$gameId" -Body @{
        teamId = $teamId
        divisionId = $divisionId
        inviteCode = $null
    } | Out-Null
    $detailsBefore = (Invoke-Api $user GET "/api/game/$gameId/details").Data
    Add-Result 'join-test-game' ([bool]$detailsBefore.teamToken) "challengeCount=$($detailsBefore.challengeCount)"

    $staticDetail = (Invoke-Api $user GET "/api/game/$gameId/challenges/$($staticChallenge.id)").Data
    $attachmentResponse = $user.GetAsync($staticDetail.context.url).GetAwaiter().GetResult()
    $attachmentBytes = $attachmentResponse.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    Add-Result 'download-attachment' ($attachmentResponse.IsSuccessStatusCode -and $attachmentBytes.Length -gt 0) "status=$([int]$attachmentResponse.StatusCode) bytes=$($attachmentBytes.Length)"

    $staticSubmit = (Invoke-Api $user POST "/api/game/$gameId/challenges/$($staticChallenge.id)" @{ flag = $staticFlag }).Data
    Add-Result 'submit-static-flag' ($staticSubmit.status -eq 'Accepted') "status=$($staticSubmit.status)"

    $dockerCreate = Invoke-Api $user POST "/api/game/$gameId/container/$dockerChallengeId"
    $dockerQueue = $dockerCreate.Data.queue
    if (-not $dockerQueue) { $dockerQueue = $dockerCreate.Data }
    if ($dockerQueue.ticketId) { Wait-Queue $user $dockerQueue.ticketId 300 | Out-Null }
    $dockerDetail = Wait-ChallengeEntry $user $gameId $dockerChallengeId 120
    $entryUri = Resolve-EntryUri ([string]$dockerDetail.context.instanceEntry)
    $entryCheck = Wait-HttpEndpoint $entryUri 30
    Add-Result 'start-docker-instance' $entryCheck.Reachable "entry=$entryUri status=$($entryCheck.Status) error=$($entryCheck.Error)"

    $instances = (Invoke-Api $admin GET '/api/admin/instances').Data
    $adminInstance = @($instances.data) | Where-Object { $_.challenge.id -eq $dockerChallengeId } | Select-Object -First 1
    if ($adminInstance.ip -and $adminInstance.port) {
        $nodeEntry = Resolve-EntryUri "$($adminInstance.ip):$($adminInstance.port)"
        $nodeCheck = Wait-HttpEndpoint $nodeEntry 15
        Add-Result 'docker-node-entry' $nodeCheck.Reachable "entry=$nodeEntry status=$($nodeCheck.Status) error=$($nodeCheck.Error)"
    } else {
        Add-Result 'docker-node-entry' $false 'admin instance did not expose ip/port'
    }

    Write-Host "[INPUT] dynamic-flag gameId=$gameId challengeId=$dockerChallengeId teamId=$teamId"
    $actualDynamicFlag = [Console]::ReadLine()
    if ([string]::IsNullOrWhiteSpace($actualDynamicFlag)) { throw 'Dynamic flag input was empty.' }
    $dynamicSubmit = (Invoke-Api $user POST "/api/game/$gameId/challenges/$dockerChallengeId" @{ flag = $actualDynamicFlag.Trim() }).Data
    Add-Result 'submit-dynamic-flag' ($dynamicSubmit.status -eq 'Accepted') "status=$($dynamicSubmit.status)"

    Start-Sleep -Seconds 11
    Invoke-Api $user DELETE "/api/game/$gameId/container/$dockerChallengeId" | Out-Null
    Add-Result 'destroy-docker-instance' $true "challengeId=$dockerChallengeId"

    $vmCreate = Invoke-Api $user POST "/api/game/$gameId/container/$vmChallengeId"
    $vmQueue = $vmCreate.Data.queue
    if (-not $vmQueue) { $vmQueue = $vmCreate.Data }
    if ($vmQueue.ticketId) { Wait-Queue $user $vmQueue.ticketId 600 | Out-Null }
    $vmStatus = Wait-VmReady $user $gameId $vmChallengeId 600
    Add-Result 'start-windows-vm' ([string]$vmStatus.status -in @('1', 'Running') -and [bool]$vmStatus.rdpUrl) "stage=$($vmStatus.stage) rdp=$($vmStatus.rdpUrl)"

    Invoke-Api $user DELETE "/api/game/$gameId/vm/$vmChallengeId" | Out-Null
    Add-Result 'destroy-windows-vm' $true "challengeId=$vmChallengeId"

    $detailsAfter = (Invoke-Api $user GET "/api/game/$gameId/details").Data
    Add-Result 'scoreboard-updated' ([int]$detailsAfter.rank.score -ge 300) "score=$($detailsAfter.rank.score)"
}
finally {
    if ($gameId) {
        try { Invoke-Api $admin DELETE "/api/edit/games/$gameId" | Out-Null; Add-Result 'delete-test-game' $true "gameId=$gameId" }
        catch { Add-Result 'delete-test-game' $false $_.Exception.Message }
    }
    if ($assetHash) {
        try { Invoke-Api $admin DELETE "/api/assets/$assetHash" | Out-Null; Add-Result 'delete-test-asset' $true "hash=$assetHash" }
        catch {
            if ($_.Exception.Message -like '*status=404*') {
                Add-Result 'delete-test-asset' $true 'removed with game cleanup'
            } else {
                Add-Result 'delete-test-asset' $false $_.Exception.Message
            }
        }
    }
    if ($teamId) {
        try { Invoke-Api $admin DELETE "/api/admin/teams/$teamId" | Out-Null; Add-Result 'delete-test-team' $true "teamId=$teamId" }
        catch { Add-Result 'delete-test-team' $false $_.Exception.Message }
    }
    if ($userId) {
        try { Invoke-Api $admin DELETE "/api/admin/users/$userId" | Out-Null; Add-Result 'delete-test-user' $true "userId=$userId" }
        catch { Add-Result 'delete-test-user' $false $_.Exception.Message }
    }
    Remove-Item -LiteralPath $tempFile -ErrorAction SilentlyContinue
    $admin.Dispose()
    $adminRuntime.Handler.Dispose()
    $user.Dispose()
    $userRuntime.Handler.Dispose()
}

$results | ConvertTo-Json -Depth 8
if ($results.Where({ -not $_.Passed }).Count -gt 0) { exit 1 }
