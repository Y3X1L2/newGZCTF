[CmdletBinding()]
param([Parameter(Mandatory)] [string] $InputPath)

$ErrorActionPreference = 'Stop'
$contracts = @{
    'submissions' = @('^IX_Submissions_Game_Time_Id$')
    'participations' = @('^IX_Participations_Game_Status_Division_Team$')
    'training-progress' = @('^IX_TrainingCourseProgress_Course_Status_Updated_User$')
    'theory-tags' = @('^UX_TheoryQuestionTags_NormalizedName$', '^IX_TheoryQuestionTagBindings_Tag_Question$')
    'deployment-queue' = @('^IX_DeploymentQueueTickets_Status_NotBefore_Created_Id$')
    'teamlab-flow' = @('^(IX_TeamLabFlows_Runtime_Generation_Time_Id|TeamLabTrafficFlows_p\d+_RuntimeId_Generation_Captured_idx)$')
    'logs' = @('^(IX_Logs_Level_Time_Id|Logs_p\d+_Level_TimeUtc_Id_idx)$')
}

function Get-PlanNodes($node) {
    if ($null -eq $node) { return }
    if ($node.PSObject.Properties.Name -contains 'Node Type') { Write-Output $node }
    foreach ($child in @($node.Plans)) { Get-PlanNodes $child }
}

$failures = [Collections.Generic.List[string]]::new()
foreach ($entry in $contracts.GetEnumerator()) {
    $path = Join-Path $InputPath "$($entry.Key).json"
    if (-not (Test-Path $path)) {
        $failures.Add("$($entry.Key): plan file is missing")
        continue
    }
    $raw = Get-Content $path -Raw
    if ($raw -match '(?i)(password|privatekey|wireguard|\bflag\b|\banswer\b|token)') {
        $failures.Add("$($entry.Key): plan artifact contains a forbidden sensitive-field marker")
    }
    $document = $raw | ConvertFrom-Json
    $nodes = @(Get-PlanNodes $document[0].Plan)
    $indexes = @($nodes | ForEach-Object { $_.'Index Name' } | Where-Object { $_ })
    foreach ($expectedPattern in $entry.Value) {
        if (-not ($indexes | Where-Object { $_ -match $expectedPattern })) {
            $failures.Add("$($entry.Key): expected index pattern '$expectedPattern' was not selected; selected: $($indexes -join ', ')")
        }
    }
    foreach ($node in $nodes) {
        if ($node.'Node Type' -eq 'Seq Scan' -and [long]$node.'Plan Rows' -ge 10000) {
            $failures.Add("$($entry.Key): unbounded large Seq Scan on $($node.'Relation Name')")
        }
    }
    if ($entry.Key -in @('teamlab-flow', 'logs')) {
        $relations = @($nodes | ForEach-Object { $_.'Relation Name' } |
            Where-Object { $_ -match '^(Logs_p|TeamLabTrafficFlows_p)\d' } | Sort-Object -Unique)
        if ($relations.Count -ne 1) {
            $failures.Add("$($entry.Key): expected one pruned child partition, found $($relations.Count): $($relations -join ', ')")
        }
    }
}

if ($failures.Count -gt 0) {
    throw "Phase 4 query plan contracts failed:`n - $($failures -join "`n - ")"
}

Write-Host "All $($contracts.Count) Phase 4 query plan contracts passed."
