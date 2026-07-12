param(
    [Parameter(Mandatory = $true)][string]$ConnectionString,
    [string]$Prefix = "gzctf",
    [int]$MaximumKeys = 200000
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command redis-cli -ErrorAction SilentlyContinue)) {
    throw "redis-cli is required"
}

$parts = $ConnectionString.Split(',', [System.StringSplitOptions]::RemoveEmptyEntries)
$endpoint = $parts[0].Split(':')
$hostName = $endpoint[0]
$port = if ($endpoint.Length -gt 1) { [int]$endpoint[1] } else { 6379 }
$passwordPart = $parts | Where-Object { $_ -like 'password=*' } | Select-Object -First 1
$password = if ($passwordPart) { $passwordPart.Substring('password='.Length) } else { $null }
$baseArgs = @('-h', $hostName, '-p', $port, '--raw')
if ($password) { $baseArgs += @('-a', $password) }

function Get-RedisKeys([string]$Pattern) {
    $cursor = '0'
    do {
        $response = @(& redis-cli @baseArgs SCAN $cursor MATCH $Pattern COUNT 1000)
        if ($LASTEXITCODE -ne 0 -or $response.Count -lt 1) {
            throw "Redis SCAN failed"
        }
        $cursor = [string]$response[0]
        if ($response.Count -gt 1) { $response[1..($response.Count - 1)] }
    } while ($cursor -ne '0')
}

$counts = @{}
$ttlMissing = 0
$memoryBytes = 0L
$seen = 0
$keys = @(Get-RedisKeys "$Prefix`:v1:*")
foreach ($key in $keys) {
    if (++$seen -gt $MaximumKeys) { throw "key scan exceeded MaximumKeys=$MaximumKeys" }
    $segments = $key.Split(':')
    $purpose = if ($segments.Length -ge 3) { $segments[2] } else { 'invalid' }
    $currentCount = if ($counts.ContainsKey($purpose)) { [int]$counts[$purpose] } else { 0 }
    $counts[$purpose] = 1 + $currentCount
    $ttl = [long](& redis-cli @baseArgs PTTL $key)
    if ($purpose -in @('cache', 'lease', 'lock') -and $ttl -lt 0) { $ttlMissing++ }
    $usage = & redis-cli @baseArgs MEMORY USAGE $key
    if ($usage -match '^\d+$') { $memoryBytes += [long]$usage }
}

[pscustomobject]@{
    Prefix = $Prefix
    KeyCount = $seen
    PurposeCounts = $counts
    MemoryBytes = $memoryBytes
    MissingRequiredTtl = $ttlMissing
} | ConvertTo-Json -Depth 5

if ($ttlMissing -gt 0) { throw "$ttlMissing cache/lease/lock keys have no TTL" }
