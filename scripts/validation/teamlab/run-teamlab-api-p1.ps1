param(
    [string]$BaseUrl = "http://127.0.0.1:18080",
    [Parameter(Mandatory = $true)][string]$AdminUser,
    [Parameter(Mandatory = $true)][string]$AdminPassword,
    [Parameter(Mandatory = $true)][string]$AgentBaseUrl,
    [Parameter(Mandatory = $true)][string]$AgentToken,
    [Parameter(Mandatory = $true)][string]$ComposeFile,
    [Parameter(Mandatory = $true)][string]$ComposeProject,
    [Parameter(Mandatory = $true)][string]$EvidenceRoot
)

$ErrorActionPreference = "Stop"
$base = $BaseUrl.TrimEnd('/')
$agentBase = $AgentBaseUrl.TrimEnd('/')
$adminSession = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
$issuedTokenIds = [Collections.Generic.List[string]]::new()
$stamp = Get-Date -Format "yyyyMMddHHmmss"
$runtimeId = [guid]::Empty
$runtimeDestroyed = $false

function Invoke-Json {
    param([string]$Method, [string]$Path, [string]$Token, [object]$Body = $null, [hashtable]$ExtraHeaders = @{})
    $headers = @{}
    foreach ($item in $ExtraHeaders.GetEnumerator()) { $headers[$item.Key] = $item.Value }
    if ($Token) { $headers.Authorization = "Bearer $Token" }
    $request = @{ Uri = "$base$Path"; Method = $Method; Headers = $headers; TimeoutSec = 60 }
    if ($null -ne $Body) {
        $request.ContentType = "application/json"
        $request.Body = $Body | ConvertTo-Json -Depth 30 -Compress
    }
    try { return Invoke-RestMethod @request }
    catch { throw "$Method $Path failed: $($_.Exception.Message) $($_.ErrorDetails.Message)" }
}

function Assert-HttpStatus {
    param([string]$Method, [string]$Path, [string]$Token, [int]$Expected)
    $response = Invoke-WebRequest -Uri "$base$Path" -Method $Method `
        -Headers @{ Authorization = "Bearer $Token" } -SkipHttpErrorCheck -TimeoutSec 30
    if ([int]$response.StatusCode -ne $Expected) {
        throw "$Method $Path returned $([int]$response.StatusCode), expected $Expected. $($response.Content)"
    }
}

function New-ApiToken {
    param([string]$Name, [string[]]$Scopes, [object[]]$Resources)
    $issued = Invoke-RestMethod -Uri "$base/api/tokens" -Method Post -WebSession $adminSession `
        -ContentType "application/json" -Body (@{
            name = $Name; scopes = $Scopes; resources = $Resources; requestsPerMinute = 5000
        } | ConvertTo-Json -Depth 8 -Compress)
    $issuedTokenIds.Add([string]$issued.info.id)
    return [string]$issued.plainTextToken
}

function Wait-Operation([guid]$Id, [string]$Token, [int]$TimeoutSeconds = 300) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $operation = Invoke-Json Get "/api/open/v1/operations/$($Id.ToString('D'))" $Token
        $status = ([string]$operation.status).ToLowerInvariant()
        if ($status -in @("2", "succeeded")) { return $operation }
        if ($status -in @("3", "failed")) {
            throw "Operation $Id failed at $($operation.stage): $($operation.errorCode) $($operation.errorDetail)"
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Operation $Id did not complete within $TimeoutSeconds seconds."
}

function Wait-Runtime([guid]$Id, [string]$Token, [string[]]$States, [int]$TimeoutSeconds = 300) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $targets = $States | ForEach-Object { $_.ToLowerInvariant() }
    do {
        $runtime = Invoke-Json Get "/api/open/v1/teamlab/runtimes/$($Id.ToString('D'))" $Token
        $status = ([string]$runtime.status).ToLowerInvariant()
        if ($status -in $targets) { return $runtime }
        if ($status -in @("6", "failed")) {
            throw "Runtime $Id failed at $($runtime.stage): $($runtime.failure.code) $($runtime.failure.detail)"
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Runtime $Id did not reach $($States -join '/') within $TimeoutSeconds seconds."
}

function Wait-ControlTask([guid]$RuntimeId, [int]$AssetId, [guid]$TicketId, [string]$Token) {
    $path = "/api/open/v1/teamlab/runtimes/$($RuntimeId.ToString('D'))/assets/$AssetId/control/$($TicketId.ToString('D'))"
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(3)
    do {
        $task = Invoke-Json Get $path $Token
        $status = ([string]$task.status).ToLowerInvariant()
        if ($status -in @("2", "succeeded", "completed")) { return $task }
        if ($status -in @("3", "failed", "cancelled", "canceled")) {
            throw "Asset control $TicketId failed at $($task.stage): $($task.errorCode)"
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Asset control $TicketId timed out."
}

function Invoke-Psql([string]$Sql) {
    $result = & docker compose -p $ComposeProject -f $ComposeFile exec -T db `
        psql -v ON_ERROR_STOP=1 -U postgres -d gzctf_p1 -At -c $Sql
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL evidence query failed." }
    return ($result -join "`n").Trim()
}

function Invoke-AgentShell([string]$Command) {
    $result = & docker compose -p $ComposeProject -f $ComposeFile exec -T agent sh -lc $Command
    if ($LASTEXITCODE -ne 0) { throw "Agent evidence command failed: $Command" }
    return ($result -join "`n").Trim()
}

function Invoke-TerminalProbe([guid]$SessionId, [string]$Token) {
    $socket = [Net.WebSockets.ClientWebSocket]::new()
    $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(45))
    $received = [Text.StringBuilder]::new()
    try {
        $socket.Options.SetRequestHeader("Authorization", "Bearer $Token")
        $uri = [uri]("$base/api/open/v1/teamlab/remote-sessions/$($SessionId.ToString('D'))/terminal" -replace '^http', 'ws')
        $socket.ConnectAsync($uri, $deadline.Token).GetAwaiter().GetResult()
        $payload = [Text.Encoding]::UTF8.GetBytes("printf 'P1_TERMINAL_OK\n'`r")
        $socket.SendAsync([ArraySegment[byte]]::new($payload), [Net.WebSockets.WebSocketMessageType]::Binary,
            $true, $deadline.Token).GetAwaiter().GetResult()
        $buffer = [byte[]]::new(8192)
        while ($received.ToString() -notmatch 'P1_TERMINAL_OK\r?\n') {
            $message = $socket.ReceiveAsync([ArraySegment[byte]]::new($buffer), $deadline.Token).GetAwaiter().GetResult()
            if ($message.MessageType -eq [Net.WebSockets.WebSocketMessageType]::Close) {
                throw "Terminal closed before returning the probe output."
            }
            [void]$received.Append([Text.Encoding]::UTF8.GetString($buffer, 0, $message.Count))
            if ($received.Length -gt 65536) { throw "Terminal probe output exceeded 64 KiB." }
        }
    }
    finally {
        $socket.Abort(); $socket.Dispose(); $deadline.Dispose()
    }
}

function Find-ResourceId($Operation, [string]$ResourceType) {
    if ($Operation.resourceId) { return [string]$Operation.resourceId }
    if ($Operation.result -and $Operation.result.resourceId) { return [string]$Operation.result.resourceId }
    throw "$ResourceType operation did not return a resource id."
}

$allScopes = @(
    "operations:read", "images:read", "images:write", "images:delete",
    "teamlab.topologies:read", "teamlab.topologies:write",
    "teamlab.runtimes:read", "teamlab.runtimes:write",
    "teamlab.traffic:read", "teamlab.capture:read", "teamlab.capture:write",
    "teamlab.resource-pools:read", "teamlab.device-packages:read", "teamlab.device-packages:write",
    "teamlab.connectors:read", "teamlab.connectors:write",
    "teamlab.link-policies:read", "teamlab.link-policies:write",
    "teamlab.remote-sessions:read", "teamlab.remote-sessions:write"
)

try {
    Invoke-RestMethod -Uri "$base/api/account/login" -Method Post -WebSession $adminSession `
        -ContentType "application/json" -Body (@{ userName = $AdminUser; password = $AdminPassword } |
            ConvertTo-Json -Compress) | Out-Null

    $bootstrapToken = New-ApiToken "P1 bootstrap $stamp" $allScopes @(
        @{ resourceType = "teamlab-scope"; resourceId = "*" },
        @{ resourceType = "image"; resourceId = "*" }
    )
    $scopeA = Invoke-Json Post "/api/open/v1/teamlab/scopes" $bootstrapToken @{
        key = "p1-a-$stamp"; displayName = "P1 scope A $stamp"
    }
    $scopeB = Invoke-Json Post "/api/open/v1/teamlab/scopes" $bootstrapToken @{
        key = "p1-b-$stamp"; displayName = "P1 scope B $stamp"
    }
    $scopeTokenScopes = $allScopes | Where-Object { $_ -notlike "images:*" -and $_ -notlike "teamlab.device-packages:*" }
    $tokenA = New-ApiToken "P1 scope A $stamp" $scopeTokenScopes @(
        @{ resourceType = "teamlab-scope"; resourceId = [string]$scopeA.id }
    )
    $tokenB = New-ApiToken "P1 scope B $stamp" $scopeTokenScopes @(
        @{ resourceType = "teamlab-scope"; resourceId = [string]$scopeB.id }
    )

    $fixtureImage = "127.0.0.1:15000/p1/web:$stamp"
    & docker build --pull=false -t $fixtureImage `
        -f (Join-Path $PSScriptRoot "docker/web-fixture.Dockerfile") (Join-Path $PSScriptRoot "../../..")
    if ($LASTEXITCODE -ne 0) { throw "P1 web fixture image build failed." }
    & docker push $fixtureImage
    if ($LASTEXITCODE -ne 0) { throw "P1 web fixture image push failed." }

    $imageSubmit = Invoke-Json Post "/api/open/v1/images/docker-references" $bootstrapToken @{
        name = "p1-web-$stamp"; registryUrl = $fixtureImage; osType = 0
    } @{ "Idempotency-Key" = "p1-image-$stamp" }
    $imageOperation = Wait-Operation ([guid]$imageSubmit.id) $bootstrapToken 600
    $imageId = [int](Find-ResourceId $imageOperation "image")
    $remote = Invoke-Json Patch "/api/open/v1/images/$imageId/remote-access" $bootstrapToken @{
        enabled = $true; protocol = "containerTerminal"; port = 1; username = $null
        credential = $null; clearCredential = $false
    }
    if (-not $remote.enabled -or $remote.protocol -ne "containerTerminal") {
        throw "Container terminal configuration was not persisted."
    }
    $image = Invoke-Json Get "/api/open/v1/images/$imageId" $bootstrapToken
    if (-not $image.registryUrl -or -not $image.imageHash) { throw "Imported image is missing registry identity." }
    $imageDigest = if ($image.imageHash.StartsWith("sha256:")) { $image.imageHash } else { "sha256:$($image.imageHash)" }

    $device = Invoke-Json Post "/api/open/v1/teamlab/device-packages" $bootstrapToken @{
        name = "p1-web-$stamp"; displayName = "P1 Web device"; version = "1.0.0"
        artifactKind = "oci-image"; artifactReference = $image.registryUrl; digest = $imageDigest
        description = "P1 real Docker device"; supportedAssetKinds = @("docker")
        cpuMillis = 100; memoryMib = 64; storageGib = 1
        ports = @(@{ name = "http"; port = 80; protocol = "tcp" })
        parameterSchema = @{ type = "object"; additionalProperties = $false }
        healthDeclaration = @{ kind = "tcp"; port = 80 }; protocolEventTypes = @()
    }

    $topologySubmit = Invoke-Json Post "/api/open/v1/teamlab/topologies" $tokenA @{
        name = "P1 real topology $stamp"; controlScopeId = $scopeA.id; schemaVersion = 2
        networks = @(@{
            key = "lab"; name = "Lab network"; isEntry = $true; orderIndex = 0
            addressPool = @{ poolCidr = "10.77.0.0/16"; runtimePrefixLength = 24 }
        })
        assets = @(
            @{ key = "web-a"; name = "Web A"; kind = 0; imageTemplateId = $imageId; devicePackageId = $device.bindingId
               resources = @{ cpuUnits = 1; memoryMiB = 64; storageMiB = 1024 }
               interfaces = @(@{ key = "eth0"; networkKey = "lab"; hostOffset = 10; primary = $true; orderIndex = 0 })
               healthCheck = @{ kind = 0; port = 80 }; orderIndex = 0 },
            @{ key = "web-b"; name = "Web B"; kind = 0; imageTemplateId = $imageId; devicePackageId = $device.bindingId
               resources = @{ cpuUnits = 1; memoryMiB = 64; storageMiB = 1024 }
               interfaces = @(@{ key = "eth0"; networkKey = "lab"; hostOffset = 11; primary = $true; orderIndex = 0 })
               healthCheck = @{ kind = 0; port = 80 }; orderIndex = 1 }
        )
        connections = @()
    } @{ "Idempotency-Key" = "p1-topology-$stamp" }
    $topologyOperation = Wait-Operation ([guid]$topologySubmit.id) $tokenA
    $topologyId = [guid](Find-ResourceId $topologyOperation "topology")
    Assert-HttpStatus Get "/api/open/v1/teamlab/topologies/$($topologyId.ToString('D'))" $tokenB 404
    Assert-HttpStatus Get "/api/open/v1/operations/$($topologySubmit.id)" $tokenB 404
    $validation = Invoke-Json Post "/api/open/v1/teamlab/topologies/$($topologyId.ToString('D'))/validate" $tokenA
    if (-not $validation.valid) { throw "Topology validation failed: $($validation.issues | ConvertTo-Json -Compress)" }
    $topology = Invoke-Json Get "/api/open/v1/teamlab/topologies/$($topologyId.ToString('D'))" $tokenA
    $publishSubmit = Invoke-Json Post "/api/open/v1/teamlab/topologies/$($topologyId.ToString('D'))/releases" $tokenA `
        @{ revision = $topology.revision } @{ "Idempotency-Key" = "p1-release-$stamp" }
    Wait-Operation ([guid]$publishSubmit.id) $tokenA | Out-Null
    $releases = Invoke-Json Get "/api/open/v1/teamlab/topologies/$($topologyId.ToString('D'))/releases?limit=20" $tokenA
    $releaseId = [guid]$releases.items[0].id
    Invoke-Json Post "/api/open/v1/teamlab/topologies/$($topologyId.ToString('D'))/releases/$($releaseId.ToString('D'))/plan" $tokenA | Out-Null

    $prepareSubmit = Invoke-Json Post "/api/open/v1/teamlab/preparations/releases/$($releaseId.ToString('D'))" $tokenA $null `
        @{ "Idempotency-Key" = "p1-prepare-$stamp" }
    Wait-Operation ([guid]$prepareSubmit.id) $tokenA 600 | Out-Null
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(5)
    do {
        $preparation = Invoke-Json Get "/api/open/v1/teamlab/preparations/releases/$($releaseId.ToString('D'))" $tokenA
        if ($preparation.readyToStart) { break }
        if ($preparation.state -eq "blocked") { throw "Image preparation blocked: $($preparation.blockers -join ', ')" }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    if (-not $preparation.readyToStart) { throw "Image preparation did not become ready." }

    $runtimeSubmit = Invoke-Json Post "/api/open/v1/teamlab/runtimes" $tokenA @{
        releaseId = $releaseId; externalReference = "p1-$stamp"; constraints = $null; overlays = @()
    } @{ "Idempotency-Key" = "p1-runtime-$stamp" }
    $runtimeOperation = Wait-Operation ([guid]$runtimeSubmit.id) $tokenA 600
    $runtimeId = [guid](Find-ResourceId $runtimeOperation "runtime")
    $runtime = Wait-Runtime $runtimeId $tokenA @("5", "running") 300
    if (@($runtime.assets).Count -ne 2 -or @($runtime.assets | Where-Object { [int]$_.id -gt 0 }).Count -ne 2) {
        throw "Runtime did not expose two usable asset ids."
    }
    Assert-HttpStatus Get "/api/open/v1/teamlab/runtimes/$($runtimeId.ToString('D'))" $tokenB 404
    Assert-HttpStatus Get "/api/open/v1/operations/$($runtimeSubmit.id)" $tokenB 404

    $runtimeSql = 'SELECT r."Id" || ''|'' || COUNT(a."Id") || ''|'' || COUNT(*) FILTER (WHERE a."Status"=5) FROM "TeamLabRuntimes" r JOIN "TeamLabRuntimeAssets" a ON a."RuntimeId"=r."Id" AND a."Generation"=r."Generation" WHERE r."PublicId"=''{0}'' GROUP BY r."Id";' -f $runtimeId.ToString('D')
    $runtimeRow = Invoke-Psql $runtimeSql
    $parts = $runtimeRow.Split('|')
    if ($parts.Count -ne 3 -or $parts[1] -ne "2" -or $parts[2] -ne "2") { throw "PostgreSQL runtime facts do not match the API." }
    $internalRuntimeId = [int]$parts[0]
    $agentInventory = Invoke-RestMethod "$agentBase/api/runtime/inventory" -Headers @{ Authorization = "Bearer $AgentToken" }
    $inventoryAssets = @($agentInventory.containers | Where-Object { $_.runtimeId -eq $internalRuntimeId -and $_.generation -eq $runtime.generation })
    if ($inventoryAssets.Count -ne 2) { throw "Agent inventory does not contain both runtime containers." }
    $dockerAssets = @((Invoke-AgentShell "docker ps --filter 'label=GZCTF.RuntimeId=$internalRuntimeId' --filter 'label=GZCTF.Generation=$($runtime.generation)' --format '{{.ID}}'") -split "`n" | Where-Object { $_ })
    if ($dockerAssets.Count -ne 2) { throw "Docker does not contain both runtime containers." }
    $ovnSwitches = Invoke-AgentShell "ovn-nbctl --data=bare --no-heading --columns=name find Logical_Switch external_ids:gzctf-runtime=$($runtimeId.ToString('D')) external_ids:gzctf-generation=$($runtime.generation)"
    if (@($ovnSwitches -split "`n" | Where-Object { $_ }).Count -lt 1) { throw "OVN has no logical switch for the runtime." }
    $ovsPorts = Invoke-AgentShell "ovs-vsctl --data=bare --no-heading --columns=name find Interface external_ids:gzctf-runtime=$($runtimeId.ToString('D')) external_ids:gzctf-generation=$($runtime.generation)"
    if (@($ovsPorts -split "`n" | Where-Object { $_ }).Count -lt 2) { throw "OVS does not contain both workload attachments." }

    $asset = $runtime.assets | Where-Object key -eq "web-a" | Select-Object -First 1
    $assetId = [int]$asset.id
    $fileBase = "/api/open/v1/teamlab/runtimes/$($runtimeId.ToString('D'))/assets/$assetId/files"
    Assert-HttpStatus Get "${fileBase}?generation=$($runtime.generation)&path=%2Ftmp" $tokenB 404
    Invoke-Json Post "$fileBase/directories" $tokenA @{ generation = $runtime.generation; path = "/tmp/p1" } | Out-Null
    $uploadPath = Join-Path $EvidenceRoot "p1-upload.txt"
    [IO.File]::WriteAllText($uploadPath, "P1_FILE_OK`n", [Text.UTF8Encoding]::new($false))
    Invoke-RestMethod -Uri "$base$fileBase/upload" -Method Post -Headers @{ Authorization = "Bearer $tokenA" } `
        -Form @{ generation = [string]$runtime.generation; path = "/tmp/p1/upload.txt"; file = Get-Item $uploadPath } | Out-Null
    $downloadPath = Join-Path $EvidenceRoot "p1-download.txt"
    Invoke-WebRequest -Uri "$base$fileBase/download?generation=$($runtime.generation)&path=%2Ftmp%2Fp1%2Fupload.txt" `
        -Headers @{ Authorization = "Bearer $tokenA" } -OutFile $downloadPath
    if ((Get-FileHash $uploadPath -Algorithm SHA256).Hash -ne (Get-FileHash $downloadPath -Algorithm SHA256).Hash) {
        throw "File upload/download digest mismatch."
    }
    Invoke-Json Post "$fileBase/move" $tokenA @{
        generation = $runtime.generation; sourcePath = "/tmp/p1/upload.txt"; destinationPath = "/tmp/p1/moved.txt"
    } | Out-Null
    Invoke-Json Delete "${fileBase}?generation=$($runtime.generation)&path=%2Ftmp%2Fp1&recursive=true&confirmed=true" $tokenA | Out-Null

    $controlPath = "/api/open/v1/teamlab/runtimes/$($runtimeId.ToString('D'))/assets/$assetId/control"
    $capability = Invoke-Json Get $controlPath $tokenA
    if (-not $capability.allowed) { throw "Asset control unavailable: $($capability.reason)" }
    $stop = Invoke-Json Post $controlPath $tokenA @{
        generation = $runtime.generation; action = "stop"; reason = "P1 lifecycle verification"; confirmed = $true
    } @{ "Idempotency-Key" = "p1-stop-$stamp" }
    Wait-ControlTask $runtimeId $assetId ([guid]$stop.ticketId) $tokenA | Out-Null
    $start = Invoke-Json Post $controlPath $tokenA @{
        generation = $runtime.generation; action = "start"; reason = "P1 lifecycle verification"; confirmed = $true
    } @{ "Idempotency-Key" = "p1-start-$stamp" }
    Wait-ControlTask $runtimeId $assetId ([guid]$start.ticketId) $tokenA | Out-Null

    $policy = Invoke-Json Post "/api/open/v1/teamlab/link-policies" $tokenA @{
        runtimeId = $runtimeId; networkKey = "lab"; assetKey = $null; kind = "latency"
        parameters = @{ delayMillis = 25 }; recoverAt = $null
    }
    if ($policy.status -ne "active") { throw "Whole-network latency policy did not become active." }
    $netem = Invoke-AgentShell "tc qdisc show | grep 'netem.*delay 25' || true"
    if (@($netem -split "`n" | Where-Object { $_ }).Count -lt 2) { throw "Latency was not applied to both asset interfaces." }
    $recovered = Invoke-Json Post "/api/open/v1/teamlab/link-policies/$($policy.id)/recover" $tokenA
    if ($recovered.status -ne "recovered") { throw "Latency policy recovery did not complete." }
    if (Invoke-AgentShell "tc qdisc show | grep 'netem.*delay 25' || true") { throw "Recovered latency qdisc remains on the Agent." }

    $access = Invoke-Json Post "/api/open/v1/teamlab/runtimes/$($runtimeId.ToString('D'))/assets/$assetId/service-access" $tokenA @{
        protocol = "tcp"; internalPort = 80; networkKey = "lab"
    }
    $serviceUri = "http://$($access.endpoint)"
    $serviceDeadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    $service = $null
    do {
        try { $service = Invoke-WebRequest $serviceUri -TimeoutSec 3; if ($service.StatusCode -eq 200) { break } } catch { }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $serviceDeadline)
    if (-not $service -or $service.StatusCode -ne 200) { throw "Published service $serviceUri is not reachable." }
    $removedAccess = Invoke-Json Delete "/api/open/v1/teamlab/runtimes/$($runtimeId.ToString('D'))/service-access/$($access.id)" $tokenA
    if ($removedAccess.status -ne "revoked") { throw "Service access was not revoked." }

    $availability = Invoke-Json Get "/api/open/v1/teamlab/runtimes/$($runtimeId.ToString('D'))/remote-access" $tokenA
    $terminalAsset = $availability | Where-Object { $_.assetId -eq $assetId -and $_.available } | Select-Object -First 1
    if (-not $terminalAsset -or $terminalAsset.protocol -ne "containerTerminal") { throw "Container terminal is not available." }
    $sessionSubmit = Invoke-Json Post "/api/open/v1/teamlab/runtimes/$($runtimeId.ToString('D'))/assets/$assetId/remote-sessions" $tokenA `
        @{ reason = "P1 terminal and audit verification"; vncConsole = $false } @{ "Idempotency-Key" = "p1-session-$stamp" }
    $sessionOperation = Wait-Operation ([guid]$sessionSubmit.id) $tokenA
    $sessionId = if ($sessionOperation.result.sessionId) { [guid]$sessionOperation.result.sessionId } else { [guid]$sessionOperation.id }
    $sessionPath = "/api/open/v1/teamlab/remote-sessions/$($sessionId.ToString('D'))"
    $session = Invoke-Json Get $sessionPath $tokenA
    if (([string]$session.status).ToLowerInvariant() -notin @("2", "ready")) { throw "Remote session is not ready." }
    Assert-HttpStatus Get $sessionPath $tokenB 404
    Invoke-TerminalProbe $sessionId $tokenA
    $sessions = Invoke-Json Get "/api/open/v1/teamlab/remote-sessions?runtimeId=$($runtimeId.ToString('D'))&query=Web%20A" $tokenA
    if (@($sessions.items | Where-Object { $_.id -eq $sessionId }).Count -ne 1) { throw "Session discovery by asset name failed." }
    $end = Invoke-Json Delete $sessionPath $tokenA $null @{ "Idempotency-Key" = "p1-session-end-$stamp" }
    Wait-Operation ([guid]$end.id) $tokenA | Out-Null
    Invoke-Json Post "$sessionPath/audit" $tokenA | Out-Null
    $audit = Invoke-Json Get "$sessionPath/audit" $tokenA
    if ($audit.state -ne "ready" -or @($audit.evidence).Count -ne 1) { throw "Remote session audit evidence is not ready." }
    $evidence = $audit.evidence[0]
    $auditPath = Join-Path $EvidenceRoot "remote-session-audit.json"
    Invoke-WebRequest -Uri "$base$sessionPath/audit/evidence/$($evidence.id)/download" `
        -Headers @{ Authorization = "Bearer $tokenA" } -OutFile $auditPath
    if ((Get-FileHash $auditPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne ([string]$evidence.sha256).ToLowerInvariant()) {
        throw "Remote session audit evidence digest mismatch."
    }

    $statusCheck = Invoke-Json Get "/api/open/v1/teamlab/runtimes/$($runtimeId.ToString('D'))/status-check" $tokenA
    if (@($statusCheck.items | Where-Object { $_.difference -ne "matched" }).Count -ne 0) {
        throw "Runtime status check found drift: $($statusCheck.items | ConvertTo-Json -Compress)"
    }
    $destroySubmit = Invoke-Json Delete "/api/open/v1/teamlab/runtimes/$($runtimeId.ToString('D'))" $tokenA $null `
        @{ "Idempotency-Key" = "p1-destroy-$stamp" }
    Wait-Operation ([guid]$destroySubmit.id) $tokenA 600 | Out-Null
    Wait-Runtime $runtimeId $tokenA @("10", "destroyed") 300 | Out-Null
    $runtimeDestroyed = $true
    $destroyedAssetsSql = 'SELECT COUNT(*) FROM "TeamLabRuntimeAssets" a JOIN "TeamLabRuntimes" r ON r."Id"=a."RuntimeId" WHERE r."PublicId"=''{0}'' AND a."Status"<>10;' -f $runtimeId.ToString('D')
    if ((Invoke-Psql $destroyedAssetsSql) -ne "0") {
        throw "PostgreSQL contains non-destroyed runtime assets after cleanup."
    }
    $finalInventory = Invoke-RestMethod "$agentBase/api/runtime/inventory" -Headers @{ Authorization = "Bearer $AgentToken" }
    if (@($finalInventory.containers | Where-Object runtimeId -eq $internalRuntimeId).Count -ne 0 -or
        @($finalInventory.teamLabResources | Where-Object runtimeId -eq $internalRuntimeId).Count -ne 0) {
        throw "Agent inventory retains runtime resources after destroy."
    }
    if (@((Invoke-AgentShell "docker ps -a --filter 'label=GZCTF.RuntimeId=$internalRuntimeId' --format '{{.ID}}'") -split "`n" | Where-Object { $_ }).Count -ne 0) {
        throw "Docker retains runtime containers after destroy."
    }
    if (Invoke-AgentShell "ovn-nbctl --data=bare --no-heading --columns=name find Logical_Switch external_ids:gzctf-runtime=$($runtimeId.ToString('D'))") {
        throw "OVN retains runtime logical switches after destroy."
    }
    if (Invoke-AgentShell "ovs-vsctl --data=bare --no-heading --columns=name find Interface external_ids:gzctf-runtime=$($runtimeId.ToString('D'))") {
        throw "OVS retains runtime interfaces after destroy."
    }

    $unprepare = Invoke-Json Delete "/api/open/v1/teamlab/preparations/releases/$($releaseId.ToString('D'))" $tokenA $null `
        @{ "Idempotency-Key" = "p1-unprepare-$stamp" }
    Wait-Operation ([guid]$unprepare.id) $tokenA | Out-Null
    Invoke-Json Post "/api/open/v1/teamlab/topologies/$($topologyId.ToString('D'))/releases/$($releaseId.ToString('D'))/archive" $tokenA | Out-Null
    Invoke-Json Post "/api/open/v1/teamlab/device-packages/$($device.id)/archive" $bootstrapToken | Out-Null
    Invoke-Json Post "/api/open/v1/teamlab/scopes/$($scopeA.id)/archive" $bootstrapToken | Out-Null
    Invoke-Json Post "/api/open/v1/teamlab/scopes/$($scopeB.id)/archive" $bootstrapToken | Out-Null

    $result = [ordered]@{
        result = "passed"; topologyId = $topologyId; releaseId = $releaseId; runtimeId = $runtimeId
        assetCount = 2; apiDatabaseAgentDockerOvnOvsConsistent = $true
        fileOperations = @("mkdir", "upload", "download", "move", "delete")
        assetControl = @("stop", "start"); wholeNetworkPolicyTargets = 2
        serviceAccess = "created_reached_revoked"; terminal = "real_websocket_pty"
        auditEvidenceSha256 = ([string]$evidence.sha256).ToLowerInvariant(); scopeIsolation = "passed"
        cleanup = "passed"
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $EvidenceRoot "result.json") -Encoding utf8
    [pscustomobject]$result
}
finally {
    if ($runtimeId -ne [guid]::Empty -and -not $runtimeDestroyed -and $tokenA) {
        try {
            $cleanup = Invoke-Json Delete "/api/open/v1/teamlab/runtimes/$($runtimeId.ToString('D'))" $tokenA $null `
                @{ "Idempotency-Key" = "p1-failure-cleanup-$stamp" }
            Wait-Operation ([guid]$cleanup.id) $tokenA 600 | Out-Null
        } catch {
            Write-Warning "P1 runtime cleanup failed: $($_.Exception.Message)"
        }
    }
    foreach ($tokenId in $issuedTokenIds) {
        try { Invoke-RestMethod -Uri "$base/api/tokens/$tokenId" -Method Delete -WebSession $adminSession | Out-Null } catch { }
    }
    $bootstrapToken = $null; $tokenA = $null; $tokenB = $null; $AgentToken = $null
}
