$ErrorActionPreference = 'Stop'
$agent = docker inspect gzctf-agent-local-sim | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Local Agent not found.' }
$entry = $agent[0].Config.Env | Where-Object { $_ -like 'Agent__AuthToken=*' } | Select-Object -First 1
if (-not $entry) { throw 'Agent authentication is not configured.' }
$headers = @{ Authorization = 'Bearer ' + $entry.Substring('Agent__AuthToken='.Length) }
$containerId = $null
$noLogsContainerId = $null
try {
    $name = 'teamlab-diagnostics-check-' + [guid]::NewGuid().ToString('N')
    $containerId = docker run -d --name $name --network none --label ManagedBy=GZCTF --label GZCTF.RuntimeId=987602 --label GZCTF.Generation=3 --entrypoint /bin/sh postgres:16-alpine -c "printf '\344\270\255\346\226\207\n'; head -c 100000 /dev/zero | tr '\000' x; printf '\nEND\n'; sleep 300"
    if ($LASTEXITCODE -ne 0) { throw 'Could not create diagnostic fixture.' }
    $containerId = $containerId.Trim()
    $body = @{ runtimeId = 987602; generation = 3; containerId = $containerId; tail = 200 }
    $url = 'http://127.0.0.1:5001/api/teamlab/diagnostics/container'
    $result = Invoke-RestMethod $url -Method Post -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json) -TimeoutSec 15
    if ($result.state -ne 'running' -or -not $result.truncated) { throw 'Expected running fixture with truncated logs.' }
    if ([Text.Encoding]::UTF8.GetByteCount($result.logs) -gt 65536) { throw 'Log bound exceeded.' }
    if (-not $result.logs.Contains([string]::Concat([char]0x4e2d, [char]0x6587))) { throw 'UTF-8 output was lost.' }
    $body.generation = 2
    $wrong = Invoke-WebRequest $url -Method Post -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json) -SkipHttpErrorCheck -TimeoutSec 15
    if ($wrong.StatusCode -lt 400 -or $wrong.Content -notmatch 'diagnostics.identity_mismatch') { throw 'Stale generation was not rejected.' }
    $body.generation = 3
    $body.runtimeId = 987603
    $wrong = Invoke-WebRequest $url -Method Post -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json) -SkipHttpErrorCheck -TimeoutSec 15
    if ($wrong.StatusCode -lt 400 -or $wrong.Content -notmatch 'diagnostics.identity_mismatch') { throw 'Foreign runtime was not rejected.' }
    $noLogsContainerId = docker run -d --network none --log-driver none --label ManagedBy=GZCTF --label GZCTF.RuntimeId=987602 --label GZCTF.Generation=3 --entrypoint /bin/sh postgres:16-alpine -c 'sleep 300'
    if ($LASTEXITCODE -ne 0) { throw 'Could not create no-log fixture.' }
    $noLogsContainerId = $noLogsContainerId.Trim()
    $body.runtimeId = 987602
    $body.containerId = $noLogsContainerId
    $result = Invoke-RestMethod $url -Method Post -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json) -TimeoutSec 15
    if ($result.state -ne 'running' -or $result.logsError -ne 'diagnostics.logs_unavailable') { throw 'Unavailable logs must not hide actual container state.' }
    Write-Output 'PASS: real container state, UTF-8, 64 KiB bound, stale generation, runtime ownership and unavailable log driver.'
} finally {
    $headers.Clear()
    $entry = $null
    if ($containerId -and $containerId -match '^[a-f0-9]{64}$') { docker rm -fv $containerId | Out-Null }
    if ($noLogsContainerId -and $noLogsContainerId -match '^[a-f0-9]{64}$') { docker rm -fv $noLogsContainerId | Out-Null }
}
