param(
    [Parameter(Mandatory = $true)]
    [string]$AdminPassword,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https?://')]
    [string]$BaseUrl,
    [int]$CourseId = 3,
    [int]$TheoryChapterId = 6,
    [int]$ChallengeId = 39,
    [string]$Marker = 'VNEXT-TRAINING-ACCEPT-20260720'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

function New-ApiClient {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.CookieContainer = [System.Net.CookieContainer]::new()
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.BaseAddress = [uri]$BaseUrl
    $client.Timeout = [TimeSpan]::FromMinutes(8)
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

function Wait-Queue {
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$TicketId,
        [int]$TimeoutSeconds = 300
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $queue = (Invoke-Api $Client GET "/api/v1/deployment-queue/$TicketId").Data
        $status = [string]$queue.status
        if ($status -in @('3', 'Completed', 'Succeeded')) { return $queue }
        if ($status -in @('4', '5', 'Failed', 'Cancelled')) {
            throw "queue $TicketId ended with status=$status error=$($queue.errorMessage)"
        }
        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "queue $TicketId timed out"
}

function Wait-CourseEntry {
    param(
        [System.Net.Http.HttpClient]$Client,
        [int]$TimeoutSeconds = 120
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $detail = (Invoke-Api $Client GET "/api/training/courses/$CourseId/challenges/$ChallengeId").Data
        if ($detail.context.instanceEntry) { return $detail }
        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "course challenge $ChallengeId did not expose an entry"
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
$studentRuntime = New-ApiClient
$admin = $adminRuntime.Client
$student = $studentRuntime.Client
$results = [System.Collections.Generic.List[object]]::new()
$studentId = $null
$teamId = $null
$containerCreated = $false
$suffix = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString().Substring(5)
$studentName = "trn${suffix}"
$teamName = "TRN-ACT-$suffix"
$studentPassword = "Vn!aA1$([guid]::NewGuid().ToString('N'))"

try {
    Invoke-Api $admin POST '/api/account/login' @{
        userName = 'admin'
        password = $AdminPassword
    } | Out-Null
    Add-Result 'admin-login' $true 'authenticated'

    $course = (Invoke-Api $admin GET "/api/training/courses/$CourseId").Data
    $chapter = @($course.chapters) | Where-Object id -eq $TheoryChapterId | Select-Object -First 1
    $challenge = @($course.challenges) | Where-Object exerciseChallengeId -eq $ChallengeId | Select-Object -First 1
    if (-not $chapter -or -not $chapter.theoryPaper -or -not $challenge) {
        throw 'Configured course, theory chapter, or course challenge was not found.'
    }
    Add-Result 'training-fixture-discovery' $true "course=$CourseId chapter=$TheoryChapterId challenge=$ChallengeId"

    Invoke-Api $admin POST '/api/admin/users' @{
        userName = $studentName
        password = $studentPassword
        email = "$studentName@example.test"
        realName = 'vNext training acceptance user'
        stdNumber = "TRN-$suffix"
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
    Add-Result 'create-training-student' ([bool]$studentId -and [bool]$teamId) "userId=$studentId teamId=$teamId"

    Invoke-Api $student POST '/api/account/login' @{
        userName = $studentName
        password = $studentPassword
    } | Out-Null
    Add-Result 'student-login' $true "user=$studentName"

    $enrollment = (Invoke-Api $student POST "/api/training/courses/$CourseId/enroll" @{
        applyReason = $Marker
    }).Data
    Add-Result 'apply-course-enrollment' ($enrollment.status -eq 'Pending') "status=$($enrollment.status)"

    Invoke-Api $admin PUT "/api/admin/training/courses/$CourseId/enrollments/$studentId" @{
        status = 'Approved'
        reviewComment = $Marker
    } | Out-Null
    $studentCourse = (Invoke-Api $student GET "/api/training/courses/$CourseId").Data
    Add-Result 'approve-course-enrollment' ($studentCourse.canLearn -and $studentCourse.enrollmentStatus -eq 'Approved') "status=$($studentCourse.enrollmentStatus) canLearn=$($studentCourse.canLearn)"

    $paper = (Invoke-Api $student GET "/api/training/courses/$CourseId/chapters/$TheoryChapterId/theory").Data
    $questions = @($paper.questions)
    if ($questions.Count -eq 0) { throw 'Published chapter theory paper has no questions.' }

    $draftAnswers = @(
        [pscustomobject]@{
            paperQuestionId = [int]$questions[0].id
            selectedIndexes = @(0)
        }
    )
    $draft = (Invoke-Api $student PUT "/api/training/courses/$CourseId/chapters/$TheoryChapterId/theory/draft" @{
        answers = $draftAnswers
    }).Data
    $savedDraft = @($draft.answers) | Where-Object paperQuestionId -eq $questions[0].id | Select-Object -First 1
    Add-Result 'save-training-theory-draft' ($draft.status -eq 'Draft' -and [bool]$savedDraft) "answers=$(@($draft.answers).Count)"

    $finalAnswers = @($questions | ForEach-Object {
        [pscustomobject]@{
            paperQuestionId = [int]$_.id
            selectedIndexes = @(0)
        }
    })
    $submitted = (Invoke-Api $student POST "/api/training/courses/$CourseId/chapters/$TheoryChapterId/theory/submit" @{
        answers = $finalAnswers
    }).Data
    Add-Result 'submit-training-theory' ($submitted.status -eq 'Submitted' -and $null -ne $submitted.score) "score=$($submitted.score)/$($submitted.totalScore) passed=$($submitted.passed)"

    $repeat = Invoke-Api $student POST "/api/training/courses/$CourseId/chapters/$TheoryChapterId/theory/submit" @{
        answers = $finalAnswers
    } -AllowError
    Add-Result 'reject-training-theory-resubmit' (-not $repeat.Success -and $repeat.Status -eq 400) "status=$($repeat.Status)"

    $create = Invoke-Api $student POST "/api/training/courses/$CourseId/challenges/$ChallengeId/container"
    $queue = $create.Data.queue
    if (-not $queue) { $queue = $create.Data }
    if ($queue.ticketId) { Wait-Queue $student $queue.ticketId 300 | Out-Null }
    $containerCreated = $true

    $detail = Wait-CourseEntry $student 150
    $entryUri = Resolve-EntryUri ([string]$detail.context.instanceEntry)
    $remaining = $detail.context.closeTime
    Add-Result 'create-training-container' ([bool]$entryUri -and $null -ne $remaining) "entry=$entryUri closeTime=$remaining"

    $entryCheck = Wait-HttpEndpoint $entryUri 30
    Add-Result 'training-container-entry' $entryCheck.Reachable "entry=$entryUri status=$($entryCheck.Status) error=$($entryCheck.Error)"

    Start-Sleep -Seconds 11
    $destroy = Invoke-Api $student DELETE "/api/training/courses/$CourseId/challenges/$ChallengeId/container"
    $destroyQueue = $destroy.Data
    if ($destroyQueue.ticketId) { Wait-Queue $student $destroyQueue.ticketId 300 | Out-Null }
    $containerCreated = $false
    Add-Result 'destroy-training-container' $true "challengeId=$ChallengeId"
}
finally {
    if ($containerCreated) {
        try {
            Start-Sleep -Seconds 11
            $destroy = Invoke-Api $student DELETE "/api/training/courses/$CourseId/challenges/$ChallengeId/container"
            if ($destroy.Data.ticketId) { Wait-Queue $student $destroy.Data.ticketId 300 | Out-Null }
            Add-Result 'cleanup-training-container' $true "challengeId=$ChallengeId"
        }
        catch { Add-Result 'cleanup-training-container' $false $_.Exception.Message }
    }
    if ($teamId) {
        try { Invoke-Api $admin DELETE "/api/admin/teams/$teamId" | Out-Null; Add-Result 'delete-training-team' $true "teamId=$teamId" }
        catch { Add-Result 'delete-training-team' $false $_.Exception.Message }
    }
    if ($studentId) {
        try { Invoke-Api $admin DELETE "/api/admin/users/$studentId" | Out-Null; Add-Result 'delete-training-student' $true "userId=$studentId" }
        catch { Add-Result 'delete-training-student' $false $_.Exception.Message }
    }

    $student.Dispose()
    $studentRuntime.Handler.Dispose()
    $admin.Dispose()
    $adminRuntime.Handler.Dispose()
}

$results | ConvertTo-Json -Depth 8
if ($results.Where({ -not $_.Passed }).Count -gt 0) { exit 1 }
