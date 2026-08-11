[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [uri]$AgentUri,
    [Parameter(Mandatory)]
    [string]$PlanPath,
    [Parameter(Mandatory)]
    [string]$AgentToken
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PlanPath -PathType Leaf)) {
    throw "Plan file was not found: $PlanPath"
}

$plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
foreach ($required in 'runtimeId', 'runtimePublicId', 'generation', 'shardKey', 'planDigest') {
    if ($null -eq $plan.$required -or [string]::IsNullOrWhiteSpace([string]$plan.$required)) {
        throw "Execution plan is missing $required."
    }
}

$headers = @{ Authorization = "Bearer $AgentToken" }
$body = @{ plan = $plan } | ConvertTo-Json -Depth 32
$applyUri = [uri]::new($AgentUri, '/api/teamlab/execution-plan/apply')
$cleanupUri = [uri]::new($AgentUri, '/api/teamlab/execution-plan/cleanup')
$inventoryUri = [uri]::new($AgentUri, '/api/runtime/inventory')

$first = Invoke-RestMethod -Method Post -Uri $applyUri -Headers $headers -ContentType 'application/json' -Body $body
if (-not $first.success) { throw "Initial apply failed: $($first.message)" }
if ($first.inventory.Count -ne $plan.assets.Count) {
    throw "Initial apply inventory mismatch: expected $($plan.assets.Count), got $($first.inventory.Count)."
}

$repeat = Invoke-RestMethod -Method Post -Uri $applyUri -Headers $headers -ContentType 'application/json' -Body $body
if (-not $repeat.success -or -not $repeat.alreadyApplied) {
    throw 'Repeated apply did not converge to the already-applied result.'
}

$cleanup = Invoke-RestMethod -Method Post -Uri $cleanupUri -Headers $headers -ContentType 'application/json' -Body $body
if (-not $cleanup.success -or $cleanup.inventory.Count -ne 0) {
    throw "Cleanup did not converge: $($cleanup.message)"
}

$inventory = Invoke-RestMethod -Method Get -Uri $inventoryUri -Headers $headers
$remaining = @($inventory.containers + $inventory.vms | Where-Object {
    $_.runtimeId -eq $plan.runtimeId -and $_.generation -eq $plan.generation
})
if ($remaining.Count -ne 0) {
    throw "Agent inventory still reports $($remaining.Count) resource(s) for the cleaned generation."
}

[pscustomobject]@{
    RuntimeId = $plan.runtimeId
    Generation = $plan.generation
    PlanDigest = $plan.planDigest
    AppliedAssets = $first.inventory.Count
    CleanupInventory = $cleanup.inventory.Count
    Result = 'passed'
} | ConvertTo-Json
