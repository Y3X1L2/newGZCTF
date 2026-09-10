$ErrorActionPreference = 'Stop'
$agent = (docker inspect gzctf-agent-local-sim | ConvertFrom-Json)[0]
$auth = $agent.Config.Env | Where-Object { $_ -like 'Agent__AuthToken=*' } | Select-Object -First 1
if (-not $auth) { throw 'Local Agent authentication is not configured.' }
$headers = @{ Authorization = 'Bearer ' + $auth.Substring('Agent__AuthToken='.Length) }
$imageName = docker inspect newgzctf-main-redis-1 --format '{{.Config.Image}}'
$reference = docker image inspect $imageName.Trim() --format '{{index .RepoDigests 0}}'
if ($LASTEXITCODE -ne 0 -or $reference -notmatch '@sha256:[a-f0-9]{64}$') { throw 'A local immutable Redis image is required.' }
$runtimeId = 987606
$publicId = [guid]::NewGuid().ToString('D')
$resources = [Collections.Generic.HashSet[string]]::new()
$plan = [ordered]@{
    RuntimeId = $runtimeId; RuntimePublicId = $publicId; Generation = 3; ShardKey = 'asset-control-qa'
    PlanDigest = ''; NetworkDigest = ('sha256:' + ('a' * 64)); NetworkOwner = $false; Networks = @()
    Assets = @('web', 'peer' | ForEach-Object { [ordered]@{
        AssetKey = $_; Kind = 'docker'; ResourceId = $_; ImageDigest = $reference.Split('@')[1]; DomainIdentity = $null
        TemplateId = 1; Cpu = 1; MemoryMiB = 128; NetworkAttachments = @(); HealthChecks = @(); ImageReference = $reference.Trim()
    } })
    ObservationPoints = @(); NetworkControl = $null
}
$bytes = [Text.Encoding]::UTF8.GetBytes(($plan | ConvertTo-Json -Depth 20 -Compress))
$plan.PlanDigest = 'sha256:' + [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($bytes))
$url = 'http://127.0.0.1:5001/api/teamlab/execution-plan/asset-control'
function Control([string]$key, [string]$action, [string]$resourceId) {
    $body = @{ plan = $plan; assetKey = $key; action = $action; expectedResourceId = $resourceId; expectedNativeIdentity = $null }
    $result = Invoke-RestMethod $url -Method Post -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 24 -Compress) -TimeoutSec 90
    if ($result.asset.resourceId) { $null = $resources.Add($result.asset.resourceId) }
    return $result
}
function MustSucceed($result, [string]$state) {
    if (-not $result.success -or $result.asset.state -ne $state) { throw "Expected $state, got $($result.errorCode)." }
}
try {
    $web = Control 'web' 'create' ''
    MustSucceed $web 'running'
    $webId = $web.asset.resourceId
    $peer = Control 'peer' 'create' ''
    MustSucceed $peer 'running'
    $peerId = $peer.asset.resourceId
    docker exec $webId touch /tmp/retained-before-rebuild
    $runningReplay = Control 'web' 'start' $webId
    MustSucceed $runningReplay 'running'
    if ($runningReplay.asset.resourceId -ne $webId) { throw 'Start replaced an existing running container.' }
    MustSucceed (Control 'web' 'pause' $webId) 'paused'
    MustSucceed (Control 'web' 'resume' $webId) 'running'
    MustSucceed (Control 'web' 'stop' $webId) 'exited'
    MustSucceed (Control 'web' 'start' $webId) 'running'
    docker exec $webId test -f /tmp/retained-before-rebuild
    if ($LASTEXITCODE -ne 0) { throw 'Stop/start destroyed writable data.' }
    $foreign = Control 'web' 'stop' $peerId
    if ($foreign.success -or $foreign.errorCode -ne 'asset_control.identity_conflict') { throw 'Foreign resource binding was accepted.' }
    $removed = Control 'web' 'remove' $webId
    if (-not $removed.success -or $null -ne $removed.asset) { throw 'Target cleanup did not finish.' }
    $rebuilt = Control 'web' 'create' ''
    MustSucceed $rebuilt 'running'
    if ($rebuilt.asset.resourceId -eq $webId) { throw 'Rebuild retained the old container.' }
    docker exec $rebuilt.asset.resourceId test ! -e /tmp/retained-before-rebuild
    if ($LASTEXITCODE -ne 0) { throw 'Rebuild retained the writable layer.' }
    $peerState = docker inspect $peerId --format '{{.State.Status}}'
    if ($peerState.Trim() -ne 'running') { throw 'Peer asset was affected.' }
    Write-Output 'PASS: real container create/start/stop/pause/resume/rebuild, writable data preservation/reset, peer isolation and binding rejection.'
} finally {
    $headers.Clear()
    $auth = $null
    foreach ($resourceId in $resources) {
        if ($resourceId -match '^[a-f0-9]{64}$') { docker rm -fv $resourceId 2>$null | Out-Null }
    }
}
