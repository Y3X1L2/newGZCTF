param(
    [Parameter(Mandatory = $true)][string]$ConnectionString,
    [string]$Prefix = "gzctf",
    [int]$MaximumPending = 5000,
    [int]$MaximumLength = 250000
)

$ErrorActionPreference = "Stop"
if (-not (Get-Command redis-cli -ErrorAction SilentlyContinue)) { throw "redis-cli is required" }
$parts = $ConnectionString.Split(',', [System.StringSplitOptions]::RemoveEmptyEntries)
$endpoint = $parts[0].Split(':')
$port = if ($endpoint.Length -gt 1) { [int]$endpoint[1] } else { 6379 }
$redisArgs = @('-h', $endpoint[0], '-p', $port, '--raw')
$passwordPart = $parts | Where-Object { $_ -like 'password=*' } | Select-Object -First 1
if ($passwordPart) { $redisArgs += @('-a', $passwordPart.Substring('password='.Length)) }

function Get-RedisKeys([string]$Pattern) {
    $cursor = '0'
    do {
        $response = @(& redis-cli @redisArgs SCAN $cursor MATCH $Pattern COUNT 1000)
        if ($LASTEXITCODE -ne 0 -or $response.Count -lt 1) {
            throw "Redis SCAN failed"
        }
        $cursor = [string]$response[0]
        if ($response.Count -gt 1) { $response[1..($response.Count - 1)] }
    } while ($cursor -ne '0')
}

$failed = @()
$summaries = @()
$streams = @(Get-RedisKeys "$Prefix`:v1:stream:*")
foreach ($stream in $streams) {
    $length = [long](& redis-cli @redisArgs XLEN $stream)
    $groupInfo = @(& redis-cli @redisArgs XINFO GROUPS $stream)
    $pending = 0L
    for ($index = 0; $index -lt $groupInfo.Count - 1; $index += 2) {
        if ($groupInfo[$index] -eq 'pending' -and $groupInfo[$index + 1] -match '^\d+$') {
            $pending += [long]$groupInfo[$index + 1]
        }
    }
    $streamLimit = if ($stream -like '*:node-metrics') { 100000 } else { $MaximumLength }
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($stream))
    }
    finally {
        $sha256.Dispose()
    }
    $streamHash = ([BitConverter]::ToString($hashBytes).Replace('-', '')).Substring(0, 16)
    $summaries += [pscustomobject]@{
        StreamHash = $streamHash
        Length = $length
        LengthLimit = $streamLimit
        Pending = $pending
    }
    if ($length -gt $streamLimit -or $pending -gt $MaximumPending) { $failed += $streamHash }
}

$summaries | ConvertTo-Json -Depth 3
if ($failed.Count -gt 0) { throw "stream health thresholds failed for $($failed.Count) stream(s)" }
