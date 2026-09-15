param(
    [string]$BaseUrl = "http://127.0.0.1:18080",
    [string]$AdminUser = "Admin",
    [Parameter(Mandatory = $true)][string]$AdminPassword,
    [string]$AgentBaseUrl = "http://127.0.0.1:18501",
    [int[]]$SingleAssetCounts = @(80, 100),
    [int]$ConcurrentRuntimeCount = 5,
    [int]$ConcurrentAssetCount = 20,
    [string]$OutputPath = "artifacts/teamlab-p3/full-chain-latest.json"
)

$ErrorActionPreference = "Stop"
$base = $BaseUrl.TrimEnd('/')
$stamp = Get-Date -Format "yyyyMMddHHmmss"
$session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
$runtimeIds = [Collections.Generic.List[guid]]::new()
$destroyedRuntimeIds = [Collections.Generic.HashSet[guid]]::new()
$measurements = [Collections.Generic.List[object]]::new()

function Invoke-Json {
    param([string]$Method, [string]$Path, [string]$Token, [object]$Body = $null, [hashtable]$Headers = @{})
    $requestHeaders = @{}
    foreach ($entry in $Headers.GetEnumerator()) { $requestHeaders[$entry.Key] = $entry.Value }
    if ($Token) { $requestHeaders.Authorization = "Bearer $Token" }
    $request = @{ Uri = "$base$Path"; Method = $Method; Headers = $requestHeaders; TimeoutSec = 120 }
    if ($null -ne $Body) {
        $request.ContentType = "application/json"
        $request.Body = $Body | ConvertTo-Json -Depth 30 -Compress
    }
    try { Invoke-RestMethod @request }
    catch { throw "$Method $Path failed: $($_.Exception.Message) $($_.ErrorDetails.Message)" }
}

function Wait-Operation {
    param([guid]$Id, [string]$Token, [int]$TimeoutSeconds = 900)
    $started = [Diagnostics.Stopwatch]::StartNew()
    do {
        $operation = Invoke-Json Get "/api/open/v1/operations/$($Id.ToString('D'))" $Token
        $status = ([string]$operation.status).ToLowerInvariant()
        if ($status -in @("2", "succeeded")) {
            return [pscustomobject]@{ Operation = $operation; ElapsedMs = $started.Elapsed.TotalMilliseconds }
        }
        if ($status -in @("3", "4", "failed", "cancelled", "canceled")) {
            throw "Operation $Id failed at $($operation.stage): $($operation.errorCode) $($operation.errorDetail)"
        }
        Start-Sleep -Milliseconds 200
    } while ($started.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    throw "Operation $Id timed out after $TimeoutSeconds seconds."
}

function Wait-Runtime {
    param([guid]$Id, [string[]]$States, [string]$Token, [int]$TimeoutSeconds = 900)
    $targets = $States | ForEach-Object { $_.ToLowerInvariant() }
    $started = [Diagnostics.Stopwatch]::StartNew()
    do {
        $runtime = Invoke-Json Get "/api/open/v1/teamlab/runtimes/$($Id.ToString('D'))/status" $Token
        $status = ([string]$runtime.status).ToLowerInvariant()
        if ($status -in $targets) { return $runtime }
        if ($status -in @("6", "failed")) {
            throw "Runtime $Id failed at $($runtime.stage): $($runtime.failure.code) $($runtime.failure.detail)"
        }
        Start-Sleep -Milliseconds 200
    } while ($started.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    throw "Runtime $Id did not reach $($States -join '/') within $TimeoutSeconds seconds."
}

function Submit-Operation {
    param([string]$Method, [string]$Path, [string]$Token, [string]$Key, [object]$Body = $null)
    $started = [Diagnostics.Stopwatch]::StartNew()
    $accepted = Invoke-Json $Method $Path $Token $Body @{ "Idempotency-Key" = $Key }
    [pscustomobject]@{ Id = [guid]$accepted.id; AcceptedMs = $started.Elapsed.TotalMilliseconds }
}

function Resolve-ResourceId($Operation) {
    if ($Operation.resourceId) { return [string]$Operation.resourceId }
    if ($Operation.result.resourceId) { return [string]$Operation.result.resourceId }
    throw "Operation did not return a resource id."
}

function Add-Measurement {
    param([string]$Phase, [int]$Assets, [int]$Runtimes, [double]$ElapsedMs, [object]$Extra = $null)
    $row = [ordered]@{ phase = $Phase; assets = $Assets; runtimes = $Runtimes; elapsedMs = [math]::Round($ElapsedMs, 1) }
    if ($null -ne $Extra) {
        foreach ($property in $Extra.PSObject.Properties) { $row[$property.Name] = $property.Value }
    }
    $measurements.Add([pscustomobject]$row)
    Write-Host ("{0,-28} {1,10:N1} ms" -f $Phase, $ElapsedMs)
}

function New-TopologyRelease {
    param([int]$AssetCount, [guid]$ScopeId, [int]$ImageId, [int]$DeviceBindingId, [string]$Token)
    $assets = for ($index = 0; $index -lt $AssetCount; $index++) {
        @{
            key = "node-$index"; name = "Node $index"; kind = 0; imageTemplateId = $ImageId
            devicePackageId = $DeviceBindingId
            resources = @{ cpuUnits = 1; memoryMiB = 64; storageMiB = 1024 }
            interfaces = @(@{ key = "eth0"; networkKey = "lab"; hostOffset = 10 + $index; primary = $true; orderIndex = 0 })
            healthCheck = @{ kind = 0; port = 70 }; orderIndex = $index
        }
    }
    $submit = Submit-Operation Post "/api/open/v1/teamlab/topologies" $Token "p3-topology-$stamp-$AssetCount" @{
        name = "P3 $AssetCount assets $stamp"; controlScopeId = $ScopeId; schemaVersion = 2
        networks = @(@{ key = "lab"; name = "Lab network"; isEntry = $true; orderIndex = 0
            addressPool = @{ poolCidr = "10.88.0.0/16"; runtimePrefixLength = 24 } })
        assets = $assets; connections = @()
    }
    $topologyResult = Wait-Operation $submit.Id $Token
    $topologyId = [guid](Resolve-ResourceId $topologyResult.Operation)
    $validation = Invoke-Json Post "/api/open/v1/teamlab/topologies/$($topologyId.ToString('D'))/validate" $Token
    if (-not $validation.valid) { throw "Topology $AssetCount validation failed: $($validation.issues | ConvertTo-Json -Compress)" }
    $topology = Invoke-Json Get "/api/open/v1/teamlab/topologies/$($topologyId.ToString('D'))" $Token
    $publish = Submit-Operation Post "/api/open/v1/teamlab/topologies/$($topologyId.ToString('D'))/releases" $Token `
        "p3-release-$stamp-$AssetCount" @{ revision = $topology.revision }
    Wait-Operation $publish.Id $Token | Out-Null
    $releases = Invoke-Json Get "/api/open/v1/teamlab/topologies/$($topologyId.ToString('D'))/releases?limit=1" $Token
    $releaseId = [guid]$releases.items[0].id
    $prepare = Submit-Operation Post "/api/open/v1/teamlab/preparations/releases/$($releaseId.ToString('D'))" $Token `
        "p3-prepare-$stamp-$AssetCount"
    $prepared = Wait-Operation $prepare.Id $Token
    Add-Measurement "prepare-$AssetCount" $AssetCount 0 $prepared.ElapsedMs
    [pscustomobject]@{ TopologyId = $topologyId; ReleaseId = $releaseId; AssetCount = $AssetCount }
}

function Start-Runtime {
    param($Release, [string]$Token, [string]$Label)
    $started = [Diagnostics.Stopwatch]::StartNew()
    $submit = Submit-Operation Post "/api/open/v1/teamlab/runtimes" $Token "p3-create-$stamp-$Label" @{
        releaseId = $Release.ReleaseId; externalReference = "p3-$stamp-$Label"; overlays = @()
    }
    $completed = Wait-Operation $submit.Id $Token
    $runtimeId = [guid](Resolve-ResourceId $completed.Operation)
    $runtimeIds.Add($runtimeId)
    Wait-Runtime $runtimeId @("5", "running") $Token | Out-Null
    Add-Measurement "create-$Label" $Release.AssetCount 1 $started.Elapsed.TotalMilliseconds `
        ([pscustomobject]@{ acceptedMs = [math]::Round($submit.AcceptedMs, 1); operationMs = [math]::Round($completed.ElapsedMs, 1) })
    $runtimeId
}

function Invoke-Lifecycle {
    param([guid[]]$Ids, [string]$Action, [string]$Token, [int]$Assets)
    $started = [Diagnostics.Stopwatch]::StartNew()
    $submitted = foreach ($id in $Ids) {
        Submit-Operation Post "/api/open/v1/teamlab/runtimes/$($id.ToString('D'))/$Action" $Token `
            "p3-$Action-$stamp-$($id.ToString('N'))"
    }
    foreach ($item in $submitted) { Wait-Operation $item.Id $Token | Out-Null }
    Add-Measurement $Action $Assets $Ids.Count $started.Elapsed.TotalMilliseconds
}

function Invoke-NetworkPolicy {
    param([guid]$RuntimeId, [int]$AssetCount, [string]$Token)
    $started = [Diagnostics.Stopwatch]::StartNew()
    $policy = Invoke-Json Post "/api/open/v1/teamlab/link-policies" $Token @{
        runtimeId = $RuntimeId; networkKey = "lab"; assetKey = $null; kind = "latency"
        parameters = @{ delayMillis = 10 }; recoverAt = $null
    }
    Add-Measurement "link-policy-apply" $AssetCount 1 $started.Elapsed.TotalMilliseconds
    $started.Restart()
    $recovered = Invoke-Json Post "/api/open/v1/teamlab/link-policies/$($policy.id)/recover" $Token
    if (([string]$recovered.status).ToLowerInvariant() -notin @("2", "recovered")) { throw "Link policy recovery failed." }
    Add-Measurement "link-policy-recover" $AssetCount 1 $started.Elapsed.TotalMilliseconds
}

function Invoke-Capture {
    param([guid[]]$Ids, [string]$Token, [int]$Assets)
    $started = [Diagnostics.Stopwatch]::StartNew()
    $submitted = foreach ($id in $Ids) {
        $operation = Submit-Operation Post "/api/open/v1/teamlab/runtimes/$($id.ToString('D'))/captures" $Token `
            "p3-capture-$stamp-$($id.ToString('N'))" @{
                scope = "runtime"; networkKey = $null; maxSeconds = 30; maxBytes = 1048576; expiresInSeconds = 3600
            }
        [pscustomobject]@{ RuntimeId = $id; Operation = $operation }
    }
    $captures = foreach ($item in $submitted) {
        $completed = Wait-Operation $item.Operation.Id $Token
        [pscustomobject]@{ RuntimeId = $item.RuntimeId; CaptureId = [guid](Resolve-ResourceId $completed.Operation) }
    }
    Add-Measurement "capture-start" $Assets $Ids.Count $started.Elapsed.TotalMilliseconds
    $started.Restart()
    foreach ($capture in $captures) {
        $stop = Submit-Operation Post "/api/open/v1/teamlab/runtimes/$($capture.RuntimeId.ToString('D'))/captures/$($capture.CaptureId.ToString('D'))/stop" $Token `
            "p3-capture-stop-$stamp-$($capture.CaptureId.ToString('N'))"
        Wait-Operation $stop.Id $Token | Out-Null
    }
    Add-Measurement "capture-stop" $Assets $Ids.Count $started.Elapsed.TotalMilliseconds
}

function Stop-Runtimes {
    param([guid[]]$Ids, [string]$Token, [int]$Assets)
    $started = [Diagnostics.Stopwatch]::StartNew()
    $submitted = foreach ($id in $Ids) {
        [pscustomobject]@{ RuntimeId = $id; Operation = Submit-Operation Delete `
            "/api/open/v1/teamlab/runtimes/$($id.ToString('D'))" $Token "p3-destroy-$stamp-$($id.ToString('N'))" }
    }
    foreach ($item in $submitted) {
        Wait-Operation $item.Operation.Id $Token | Out-Null
        Wait-Runtime $item.RuntimeId @("10", "destroyed") $Token | Out-Null
        [void]$destroyedRuntimeIds.Add($item.RuntimeId)
    }
    Add-Measurement "destroy" $Assets $Ids.Count $started.Elapsed.TotalMilliseconds
}

function Assert-AssetPage {
    param([guid]$RuntimeId, [int]$Expected, [string]$Token)
    $page = Invoke-Json Get "/api/open/v1/teamlab/runtimes/$($RuntimeId.ToString('D'))/assets?limit=100" $Token
    if (@($page.items).Count -ne $Expected) { throw "Runtime $RuntimeId returned $(@($page.items).Count) assets, expected $Expected." }
}

try {
    Invoke-RestMethod -Uri "$base/api/account/login" -Method Post -WebSession $session -ContentType "application/json" `
        -Body (@{ userName = $AdminUser; password = $AdminPassword } | ConvertTo-Json -Compress) | Out-Null
    $scopes = @(
        "operations:read", "images:read", "images:write", "teamlab.topologies:read", "teamlab.topologies:write",
        "teamlab.runtimes:read", "teamlab.runtimes:write", "teamlab.traffic:read", "teamlab.capture:read",
        "teamlab.capture:write", "teamlab.device-packages:read", "teamlab.device-packages:write",
        "teamlab.link-policies:read", "teamlab.link-policies:write"
    )
    $issued = Invoke-RestMethod -Uri "$base/api/tokens" -Method Post -WebSession $session -ContentType "application/json" `
        -Body (@{ name = "P3 full chain $stamp"; scopes = $scopes; requestsPerMinute = 10000
            resources = @(@{ resourceType = "teamlab-scope"; resourceId = "*" }, @{ resourceType = "image"; resourceId = "*" }) } |
            ConvertTo-Json -Depth 8 -Compress)
    $token = [string]$issued.plainTextToken

    $scope = Invoke-Json Post "/api/open/v1/teamlab/scopes" $token @{ key = "p3-$stamp"; displayName = "P3 $stamp" }
    $fixtureImage = "127.0.0.1:15000/p3/echo:$stamp"
    docker tag ghcr.io/gzctf/challenge-base/echo:latest $fixtureImage
    docker push $fixtureImage | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Fixture image push failed." }
    $imageSubmit = Submit-Operation Post "/api/open/v1/images/docker-references" $token "p3-image-$stamp" @{
        name = "p3-echo-$stamp"; registryUrl = $fixtureImage; osType = 0
    }
    $imageResult = Wait-Operation $imageSubmit.Id $token
    $imageId = [int](Resolve-ResourceId $imageResult.Operation)
    $image = Invoke-Json Get "/api/open/v1/images/$imageId" $token
    $digest = if ($image.imageHash.StartsWith("sha256:")) { $image.imageHash } else { "sha256:$($image.imageHash)" }
    $device = Invoke-Json Post "/api/open/v1/teamlab/device-packages" $token @{
        name = "p3-echo-$stamp"; displayName = "P3 echo"; version = "1.0.0"; artifactKind = "oci-image"
        artifactReference = $image.registryUrl; digest = $digest; supportedAssetKinds = @("docker")
        cpuMillis = 10; memoryMib = 64; storageGib = 1
        ports = @(@{ name = "tcp"; port = 70; protocol = "tcp" })
        parameterSchema = @{ type = "object"; additionalProperties = $false }
        healthDeclaration = @{ kind = "tcp"; port = 70 }; protocolEventTypes = @()
    }

    $counts = @($SingleAssetCounts + $ConcurrentAssetCount | Select-Object -Unique)
    $releases = @{}
    foreach ($count in $counts) {
        $releases[$count] = New-TopologyRelease $count ([guid]$scope.id) $imageId ([int]$device.bindingId) $token
    }

    foreach ($count in $SingleAssetCounts) {
        $runtimeId = Start-Runtime $releases[$count] $token "$count-single"
        Assert-AssetPage $runtimeId $count $token
        Invoke-NetworkPolicy $runtimeId $count $token
        Invoke-Lifecycle @($runtimeId) "pause" $token $count
        Invoke-Lifecycle @($runtimeId) "resume" $token $count
        Invoke-Capture @($runtimeId) $token $count
        Stop-Runtimes @($runtimeId) $token $count
    }

    $batchStarted = [Diagnostics.Stopwatch]::StartNew()
    $submissions = for ($index = 0; $index -lt $ConcurrentRuntimeCount; $index++) {
        $release = $releases[$ConcurrentAssetCount]
        $submit = Submit-Operation Post "/api/open/v1/teamlab/runtimes" $token "p3-batch-create-$stamp-$index" @{
            releaseId = $release.ReleaseId; externalReference = "p3-$stamp-batch-$index"; overlays = @()
        }
        [pscustomobject]@{ Submit = $submit; Index = $index }
    }
    $batchIds = foreach ($entry in $submissions) {
        $completed = Wait-Operation $entry.Submit.Id $token
        $id = [guid](Resolve-ResourceId $completed.Operation)
        $runtimeIds.Add($id)
        Wait-Runtime $id @("5", "running") $token | Out-Null
        Assert-AssetPage $id $ConcurrentAssetCount $token
        $id
    }
    Add-Measurement "create-concurrent" ($ConcurrentRuntimeCount * $ConcurrentAssetCount) $ConcurrentRuntimeCount $batchStarted.Elapsed.TotalMilliseconds
    Invoke-Lifecycle $batchIds "pause" $token ($ConcurrentRuntimeCount * $ConcurrentAssetCount)
    Invoke-Lifecycle $batchIds "resume" $token ($ConcurrentRuntimeCount * $ConcurrentAssetCount)
    Invoke-Capture $batchIds $token ($ConcurrentRuntimeCount * $ConcurrentAssetCount)
    Stop-Runtimes $batchIds $token ($ConcurrentRuntimeCount * $ConcurrentAssetCount)

    $agentToken = (docker exec gzctf-teamlab-p3-db-1 psql -U postgres -d gzctf_p1 -At -c `
        'SELECT "AuthToken" FROM "WorkerNodes" WHERE "Name"=''P3 real worker'' LIMIT 1;').Trim()
    $inventory = Invoke-RestMethod "$AgentBaseUrl/api/runtime/inventory" -Headers @{ Authorization = "Bearer $agentToken" }
    $residualContainers = @($inventory.containers | Where-Object { $_.runtimeId })
    $residualResources = @($inventory.teamLabResources | Where-Object { $_.runtimeId })
    if ($residualContainers.Count -or $residualResources.Count) {
        throw "Agent inventory retains $($residualContainers.Count) containers and $($residualResources.Count) TeamLab resources."
    }

    $report = [ordered]@{
        result = "passed"; startedAt = $stamp; completedAt = [DateTimeOffset]::Now
        singleAssetCounts = $SingleAssetCounts
        concurrent = @{ runtimes = $ConcurrentRuntimeCount; assetsPerRuntime = $ConcurrentAssetCount }
        measurements = $measurements
        residuals = @{ containers = 0; teamLabResources = 0 }
    }
    $directory = Split-Path -Parent $OutputPath
    if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
    $report | ConvertTo-Json -Depth 8 | Set-Content $OutputPath -Encoding utf8
    $report | ConvertTo-Json -Depth 8
}
finally {
    foreach ($id in $runtimeIds) {
        if ($destroyedRuntimeIds.Contains($id) -or -not $token) { continue }
        try {
            $cleanup = Submit-Operation Delete "/api/open/v1/teamlab/runtimes/$($id.ToString('D'))" $token `
                "p3-final-cleanup-$stamp-$($id.ToString('N'))"
            Wait-Operation $cleanup.Id $token | Out-Null
        } catch { Write-Warning "Cleanup failed for runtime ${id}: $($_.Exception.Message)" }
    }
}
