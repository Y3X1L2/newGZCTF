param(
    [string]$AdminUser = "Admin",
    [string]$ExistingReleaseId,
    [switch]$KeepEnvironment
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../../..")).Path
$composeFile = Join-Path $PSScriptRoot "docker-compose.p1.yml"
$projectName = "gzctf-teamlab-p1"
$releaseId = if ($ExistingReleaseId) { $ExistingReleaseId } else { "teamlab-p1-$(Get-Date -Format 'yyyyMMdd-HHmmss')" }
$outputRoot = "artifacts/teamlab-p1/releases"
$publishRelative = "$outputRoot/$releaseId/publish"
$evidenceRoot = Join-Path $repoRoot "artifacts/teamlab-p1/evidence/$releaseId"
$adminPassword = "P1!aA9$([Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)))"
$nodeId = [guid]::NewGuid()
$nodeToken = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$failed = $true

function Invoke-Compose {
    & docker compose -p $projectName -f $composeFile @args
    if ($LASTEXITCODE -ne 0) { throw "docker compose failed with exit code $LASTEXITCODE" }
}

function Invoke-Psql([string]$Sql) {
    $result = & docker compose -p $projectName -f $composeFile exec -T db `
        psql -v ON_ERROR_STOP=1 -U postgres -d gzctf_p1 -At -c $Sql
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL command failed with exit code $LASTEXITCODE" }
    return ($result -join "`n").Trim()
}

function Wait-Until([scriptblock]$Probe, [int]$TimeoutSeconds, [string]$Failure) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try { if (& $Probe) { return } } catch { }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw $Failure
}

try {
    & git -C $repoRoot merge-base --is-ancestor 61b951f HEAD
    if ($LASTEXITCODE -ne 0) { throw "HEAD does not contain fixed candidate 61b951f." }
    docker version --format '{{.Server.Version}}' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Docker Engine is unavailable." }

    New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
    if (-not $ExistingReleaseId) {
        & (Join-Path $repoRoot "scripts/deployment/build-gzctf-release.ps1") `
            -ReleaseId $releaseId -OutputRoot $outputRoot
        if ($LASTEXITCODE -ne 0) { throw "Formal release build failed." }
    } elseif (-not (Test-Path (Join-Path $repoRoot $publishRelative) -PathType Container)) {
        throw "Existing formal release was not found: $releaseId"
    }

    $env:TEAMLAB_P1_PUBLISH_PATH = $publishRelative
    $env:TEAMLAB_P1_ADMIN_PASSWORD = $adminPassword
    $env:TEAMLAB_P1_NODE_ID = $nodeId.ToString("D")
    $env:TEAMLAB_P1_NODE_TOKEN = $nodeToken

    if ($ExistingReleaseId) {
        Invoke-Compose up -d --no-build db redis registry guacd api
    } else {
        Invoke-Compose up -d --build db redis registry guacd api
    }
    Wait-Until {
        (Invoke-WebRequest "http://127.0.0.1:18080/api/Config" -TimeoutSec 3).StatusCode -eq 200
    } 180 "The published API did not become healthy."

    $sql = @"
INSERT INTO "WorkerNodes" (
  "Id", "Name", "HostAddress", "AuthToken", "Capabilities", "Status",
  "CpuLoad", "MemoryLoad", "CurrentContainers", "MaxContainers", "CurrentVms", "MaxVms",
  "UsedPorts", "TotalPorts", "LiveMetricSequence", "RegisteredAt", "IsSchedulable", "IsLocal",
  "IsStorageNode", "AgentPort", "RegistryPort", "TeamLabNetworkEnabled", "TeamLabTunnelStatus",
  "TeamLabTunnelIp", "TeamLabTunnelLastHandshake", "TeamLabTunnelConfigVersion", "TeamLabFabricStatus",
  "CapabilityManifestSchemaVersion", "CapabilityManifestJson", "AgentUpdateState", "AgentUpdateWasSchedulable")
VALUES (
  '$($nodeId.ToString("D"))', 'P1 real worker', 'agent', '$nodeToken', 0, 0,
  0, 0, 0, 20, 0, 0, 0, 21, 0, NOW(), TRUE, FALSE, FALSE, 5001, 5000, TRUE, 3,
  '10.250.0.2', NOW(), 1, 0, 0, '{}', 0, FALSE);
"@
    Invoke-Psql $sql | Out-Null
    if ($ExistingReleaseId) {
        Invoke-Compose up -d --no-build agent
    } else {
        Invoke-Compose up -d --build agent
    }

    $agentHeaders = @{ Authorization = "Bearer $nodeToken" }
    Wait-Until {
        $status = Invoke-RestMethod "http://127.0.0.1:18501/api/teamlab/status" -Headers $agentHeaders -TimeoutSec 5
        $status.available -and $status.enable -and -not $status.dryRun -and $status.fabricReady
    } 120 "The real Agent execution plane did not become ready."
    Wait-Until {
        $heartbeatSql = 'SELECT COUNT(*) FROM "WorkerNodes" WHERE "Id"=''{0}'' AND "Status"=1 AND "LastHeartbeat" > NOW() - INTERVAL ''15 seconds'' AND "TeamLabFabricStatus"=3 AND "CapabilityManifestSchemaVersion">0;' -f $nodeId.ToString("D")
        (Invoke-Psql $heartbeatSql) -eq "1"
    } 60 "The real Agent heartbeat and capability manifest were not persisted."

    & (Join-Path $PSScriptRoot "run-teamlab-api-p1.ps1") `
        -BaseUrl "http://127.0.0.1:18080" `
        -AdminUser $AdminUser `
        -AdminPassword $adminPassword `
        -AgentBaseUrl "http://127.0.0.1:18501" `
        -AgentToken $nodeToken `
        -ComposeFile $composeFile `
        -ComposeProject $projectName `
        -EvidenceRoot $evidenceRoot
    if ($LASTEXITCODE -ne 0) { throw "TeamLab P1 API flow failed." }
    $failed = $false
}
finally {
    if ($failed) {
        try {
            Invoke-Compose ps | Out-File (Join-Path $evidenceRoot "compose-ps.txt") -Encoding utf8
            Invoke-Compose logs --no-color | Out-File (Join-Path $evidenceRoot "compose.log") -Encoding utf8
        } catch { }
    }
    if (-not $KeepEnvironment) {
        try { Invoke-Compose down --remove-orphans } catch { }
    }
    Remove-Item Env:TEAMLAB_P1_PUBLISH_PATH, Env:TEAMLAB_P1_ADMIN_PASSWORD, `
        Env:TEAMLAB_P1_NODE_ID, Env:TEAMLAB_P1_NODE_TOKEN -ErrorAction SilentlyContinue
    $adminPassword = $null
    $nodeToken = $null
}

[pscustomobject]@{
    Result = "passed"
    ReleaseId = $releaseId
    GitCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
    Evidence = $evidenceRoot
}
