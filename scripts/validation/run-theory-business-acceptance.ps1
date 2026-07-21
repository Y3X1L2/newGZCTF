param(
    [Parameter(Mandatory = $true)]
    [string]$AdminPassword,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https?://')]
    [string]$BaseUrl,
    [string]$Marker = 'VNEXT-THEORY-ACCEPT-20260720'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

function New-ApiClient {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.CookieContainer = [System.Net.CookieContainer]::new()
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.BaseAddress = [uri]$BaseUrl
    $client.Timeout = [TimeSpan]::FromMinutes(3)
    [pscustomobject]@{ Client = $client; Handler = $handler }
}

function Invoke-Api {
    param(
        [System.Net.Http.HttpClient]$Client,
        [ValidateSet('GET', 'POST', 'PUT', 'DELETE')]
        [string]$Method,
        [string]$Path,
        [object]$Body,
        [switch]$AllowError
    )

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::new($Method),
        $Path
    )
    try {
        if ($PSBoundParameters.ContainsKey('Body')) {
            $json = $Body | ConvertTo-Json -Depth 30 -Compress
            if ($Path -eq '/api/admin/users' -and -not $json.TrimStart().StartsWith('[')) {
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
        $result = [pscustomobject]@{
            Status = [int]$response.StatusCode
            Success = $response.IsSuccessStatusCode
            Data = $data
            Raw = $raw
        }
        if (-not $response.IsSuccessStatusCode -and -not $AllowError) {
            throw "$Method $Path failed: status=$([int]$response.StatusCode) body=$raw"
        }
        return $result
    }
    finally {
        $request.Dispose()
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
$studentRuntime = New-ApiClient
$admin = $adminRuntime.Client
$student = $studentRuntime.Client
$results = [System.Collections.Generic.List[object]]::new()
$gameId = $null
$studentId = $null
$teamId = $null
$suffix = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString().Substring(5)
$studentName = "thy${suffix}"
$teamName = "THY-ACT-$suffix"
$studentPassword = "Vn!aA1$([guid]::NewGuid().ToString('N'))"

try {
    Invoke-Api $admin POST '/api/account/login' @{
        userName = 'admin'
        password = $AdminPassword
    } | Out-Null
    Add-Result 'admin-login' $true 'authenticated'

    $now = [DateTimeOffset]::UtcNow
    $game = (Invoke-Api $admin POST '/api/edit/games' @{
        title = "$Marker-$suffix"
        hidden = $true
        summary = 'Temporary vNext theory acceptance game'
        content = 'Automated acceptance game. Safe to delete.'
        acceptWithoutReview = $true
        writeupRequired = $false
        teamMemberCountLimit = 1
        containerCountLimit = 0
        practiceMode = $false
        isTest = $true
        start = $now.AddMinutes(-2).ToUnixTimeMilliseconds()
        end = $now.AddHours(1).ToUnixTimeMilliseconds()
        writeupDeadline = $now.AddHours(2).ToUnixTimeMilliseconds()
        bloodBonus = 0
        gameType = 'Theory'
    }).Data
    $gameId = $game.id
    Add-Result 'create-theory-game' ([bool]$gameId) "gameId=$gameId"

    $paper = (Invoke-Api $admin PUT "/api/admin/theory/games/$gameId/paper" @{
        title = 'vNext acceptance paper'
        description = $Marker
        questions = @(
            @{
                id = 0
                sourceQuestionId = $null
                type = 'SingleChoice'
                bankName = 'Acceptance'
                title = 'Which value equals two plus two?'
                content = 'Select the correct answer.'
                options = @('3', '4', '5', '6')
                answerIndexes = @(1)
                tags = @('acceptance')
                score = 10
                order = 1
            },
            @{
                id = 0
                sourceQuestionId = $null
                type = 'MultipleChoice'
                bankName = 'Acceptance'
                title = 'Select the even numbers.'
                content = 'Two answers are correct.'
                options = @('1', '2', '3', '4')
                answerIndexes = @(1, 3)
                tags = @('acceptance')
                score = 10
                order = 2
            }
        )
    }).Data
    Add-Result 'save-theory-paper' (@($paper.questions).Count -eq 2 -and -not $paper.isPublished) "paperId=$($paper.id)"

    $published = (Invoke-Api $admin POST "/api/admin/theory/games/$gameId/paper/publish").Data
    Add-Result 'publish-theory-paper' $published.isPublished "questionCount=$(@($published.questions).Count)"

    Invoke-Api $admin POST '/api/admin/users' @{
        userName = $studentName
        password = $studentPassword
        email = "$studentName@example.test"
        realName = 'vNext theory acceptance user'
        stdNumber = "THY-$suffix"
        teamName = $teamName
        assignedRole = 'Student'
        studentGroupIds = @()
    } | Out-Null
    $users = (Invoke-Api $admin POST "/api/admin/users/search?hint=$([uri]::EscapeDataString($studentName))").Data
    $createdUser = @($users.data) | Where-Object userName -eq $studentName | Select-Object -First 1
    $studentId = $createdUser.id
    $teams = (Invoke-Api $admin POST "/api/admin/teams/search?hint=$([uri]::EscapeDataString($teamName))").Data
    $createdTeam = @($teams.data) | Where-Object name -eq $teamName | Select-Object -First 1
    $teamId = $createdTeam.id
    Add-Result 'create-theory-player' ([bool]$studentId -and [bool]$teamId) "userId=$studentId teamId=$teamId"

    Invoke-Api $student POST '/api/account/login' @{
        userName = $studentName
        password = $studentPassword
    } | Out-Null
    $check = (Invoke-Api $student GET "/api/game/$gameId/check").Data
    [Nullable[int]]$divisionId = $null
    if (@($check.joinableDivisions).Count -gt 0) {
        $divisionId = [int]@($check.joinableDivisions)[0]
    }
    Invoke-Api $student POST "/api/game/$gameId" @{
        teamId = $teamId
        divisionId = $divisionId
        inviteCode = $null
    } | Out-Null
    Add-Result 'join-theory-game' $true "gameId=$gameId"

    $playerPaper = (Invoke-Api $student GET "/api/theory/games/$gameId/paper").Data
    $questions = @($playerPaper.questions)
    $answers = @(
        @{
            paperQuestionId = [int]$questions[0].id
            selectedIndexes = @(1)
        },
        @{
            paperQuestionId = [int]$questions[1].id
            selectedIndexes = @(1, 3)
        }
    )
    $draft = (Invoke-Api $student PUT "/api/theory/games/$gameId/draft" @{ answers = @($answers[0]) }).Data
    $savedDraftAnswer = @($draft.answers) | Where-Object paperQuestionId -eq $questions[0].id | Select-Object -First 1
    Add-Result 'save-theory-draft' ($draft.status -eq 'Draft' -and [bool]$savedDraftAnswer -and @($savedDraftAnswer.selectedIndexes)[0] -eq 1) "answers=$(@($draft.answers).Count)"

    $submitted = (Invoke-Api $student POST "/api/theory/games/$gameId/submit" @{ answers = $answers }).Data
    Add-Result 'submit-theory-paper' ($submitted.status -eq 'Submitted' -and [int]$submitted.score -eq 20) "score=$($submitted.score)/$($submitted.totalScore)"

    $repeat = Invoke-Api $student POST "/api/theory/games/$gameId/submit" @{ answers = $answers } -AllowError
    Add-Result 'reject-theory-resubmit' (-not $repeat.Success -and $repeat.Status -eq 409) "status=$($repeat.Status)"

    $scoreboard = (Invoke-Api $student GET "/api/theory/games/$gameId/scoreboard").Data
    $scoreRow = @($scoreboard) | Where-Object teamId -eq $teamId | Select-Object -First 1
    Add-Result 'theory-scoreboard' ([bool]$scoreRow -and [int]$scoreRow.score -eq 20) "score=$($scoreRow.score) rank=$($scoreRow.rank)"

    $resultsView = (Invoke-Api $admin GET "/api/admin/theory/games/$gameId/results").Data
    $submission = @($resultsView.submissions) | Where-Object userName -eq $studentName | Select-Object -First 1
    Add-Result 'theory-admin-results' ([bool]$submission -and [int]$submission.score -eq 20) "submissionId=$($submission.id)"
}
finally {
    if ($gameId) {
        try { Invoke-Api $admin DELETE "/api/edit/games/$gameId" | Out-Null; Add-Result 'delete-theory-game' $true "gameId=$gameId" }
        catch { Add-Result 'delete-theory-game' $false $_.Exception.Message }
    }
    if ($teamId) {
        try { Invoke-Api $admin DELETE "/api/admin/teams/$teamId" | Out-Null; Add-Result 'delete-theory-team' $true "teamId=$teamId" }
        catch { Add-Result 'delete-theory-team' $false $_.Exception.Message }
    }
    if ($studentId) {
        try { Invoke-Api $admin DELETE "/api/admin/users/$studentId" | Out-Null; Add-Result 'delete-theory-student' $true "userId=$studentId" }
        catch { Add-Result 'delete-theory-student' $false $_.Exception.Message }
    }

    $student.Dispose()
    $studentRuntime.Handler.Dispose()
    $admin.Dispose()
    $adminRuntime.Handler.Dispose()
}

$results | ConvertTo-Json -Depth 8
if ($results.Where({ -not $_.Passed }).Count -gt 0) { exit 1 }
