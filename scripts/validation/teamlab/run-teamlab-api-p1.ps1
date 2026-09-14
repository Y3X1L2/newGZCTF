param(
    [string]$BaseUrl = "http://127.0.0.1:8080",
    [Parameter(Mandatory = $true)][string]$AdminUser,
    [Parameter(Mandatory = $true)][string]$AdminPassword
)

$ErrorActionPreference = "Stop"
$base = $BaseUrl.TrimEnd('/')
$session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
$tokenId = $null
$token = $null

function Invoke-Json {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [hashtable]$Headers = @{}
    )

    $request = @{
        Uri = "$base$Path"
        Method = $Method
        Headers = $Headers
    }
    if ($null -ne $Body) {
        $request.ContentType = "application/json"
        $request.Body = $Body | ConvertTo-Json -Depth 20 -Compress
    }
    Invoke-RestMethod @request
}

try {
    Invoke-RestMethod -Uri "$base/api/account/login" -Method Post -WebSession $session `
        -ContentType "application/json" -Body (@{
            userName = $AdminUser
            password = $AdminPassword
        } | ConvertTo-Json -Compress) | Out-Null

    $scopes = @(
        "operations:read", "images:read", "assets:read",
        "teamlab.topologies:read", "teamlab.topologies:write",
        "teamlab.runtimes:read", "teamlab.runtimes:write",
        "teamlab.traffic:read", "teamlab.capture:read", "teamlab.capture:write",
        "teamlab.resource-pools:read",
        "teamlab.device-packages:read", "teamlab.device-packages:write",
        "teamlab.connectors:read", "teamlab.connectors:write",
        "teamlab.link-policies:read", "teamlab.link-policies:write",
        "teamlab.remote-sessions:read", "teamlab.remote-sessions:write"
    )
    $issued = Invoke-RestMethod -Uri "$base/api/tokens" -Method Post -WebSession $session `
        -ContentType "application/json" -Body (@{
            name = "TeamLab P1 local validation"
            scopes = $scopes
            resources = @(@{ resourceType = "teamlab-scope"; resourceId = "*" })
            requestsPerMinute = 1000
        } | ConvertTo-Json -Depth 5 -Compress)
    $token = $issued.plainTextToken
    $tokenId = $issued.info.id
    $auth = @{ Authorization = "Bearer $token" }

    $stamp = Get-Date -Format "yyyyMMddHHmmss"
    $createdScope = Invoke-Json Post "/api/open/v1/teamlab/scopes" @{
        key = "p1-$stamp"
        displayName = "P1 本地验证 $stamp"
    } $auth

    $capabilities = Invoke-Json Get "/api/open/v1/teamlab/capabilities" $null $auth
    $controlScopes = Invoke-Json Get "/api/open/v1/teamlab/scopes" $null $auth
    $topologies = Invoke-Json Get "/api/open/v1/teamlab/topologies?limit=20" $null $auth
    $runtimes = Invoke-Json Get "/api/open/v1/teamlab/runtimes?limit=20" $null $auth
    $resourcePools = Invoke-Json Get "/api/open/v1/teamlab/resource-pools" $null $auth
    $devicePackages = Invoke-Json Get "/api/open/v1/teamlab/device-packages?limit=20" $null $auth
    $connectors = Invoke-Json Get "/api/open/v1/teamlab/connectors?limit=20" $null $auth
    $sessions = Invoke-Json Get "/api/open/v1/teamlab/remote-sessions?limit=20" $null $auth
    $operations = Invoke-Json Get "/api/open/v1/operations?limit=20" $null $auth

    $runtimeChecks = 0
    $runtimeId = $runtimes.items | Select-Object -First 1 -ExpandProperty id
    if ($runtimeId) {
        $runtimePaths = @(
            "/api/open/v1/teamlab/runtimes/$runtimeId",
            "/api/open/v1/teamlab/runtimes/$runtimeId/events?limit=20",
            "/api/open/v1/teamlab/runtimes/$runtimeId/access-grants?limit=20",
            "/api/open/v1/teamlab/runtimes/$runtimeId/service-access",
            "/api/open/v1/teamlab/runtimes/$runtimeId/remote-access",
            "/api/open/v1/teamlab/runtimes/$runtimeId/device-health",
            "/api/open/v1/teamlab/runtimes/$runtimeId/status-check",
            "/api/open/v1/teamlab/runtimes/$runtimeId/traffic/flows?limit=20",
            "/api/open/v1/teamlab/runtimes/$runtimeId/traffic/paths?limit=20",
            "/api/open/v1/teamlab/runtimes/$runtimeId/captures?limit=20"
        )
        foreach ($path in $runtimePaths) {
            Invoke-Json Get $path $null $auth | Out-Null
            $runtimeChecks++
        }
    }

    Invoke-Json Post "/api/open/v1/teamlab/scopes/$($createdScope.id)/archive" $null $auth | Out-Null

    [pscustomobject]@{
        MainApi = "ok"
        ScopeLifecycle = "created_and_archived"
        CapabilityCount = @($capabilities).Count
        ScopeCount = @($controlScopes).Count
        TopologyCount = @($topologies.items).Count
        RuntimeCount = @($runtimes.items).Count
        ResourcePoolCount = @($resourcePools).Count
        DevicePackageCount = @($devicePackages.items).Count
        ConnectorCount = @($connectors.items).Count
        RemoteSessionCount = @($sessions.items).Count
        OperationCount = @($operations.items).Count
        RuntimeDetailChecks = $runtimeChecks
    }
}
finally {
    if ($tokenId) {
        Invoke-RestMethod -Uri "$base/api/tokens/$tokenId" -Method Delete -WebSession $session | Out-Null
    }
}
