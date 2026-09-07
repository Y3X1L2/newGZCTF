param(
    [string]$Configuration = "Release",
    [string]$ReleaseId = "phase9-$(Get-Date -Format 'yyyyMMdd-HHmmss')",
    [string]$OutputRoot = "artifacts/releases",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$releaseRoot = Join-Path $repoRoot (Join-Path $OutputRoot $ReleaseId)
$publishRoot = Join-Path $releaseRoot "publish"
$archivePath = Join-Path $releaseRoot "$ReleaseId.tar.gz"
$manifestPath = Join-Path $releaseRoot "release-manifest.json"

if (Test-Path $releaseRoot) {
    throw "Release directory already exists: $releaseRoot"
}
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

if (-not $SkipPublish) {
    & dotnet restore (Join-Path $repoRoot "src/GZCTF/GZCTF.csproj") -r linux-x64
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }
    & dotnet publish (Join-Path $repoRoot "src/GZCTF/GZCTF.csproj") `
        -c $Configuration --no-restore -r linux-x64 --self-contained false `
        -p:DebugType=None -p:DebugSymbols=false `
        -o $publishRoot
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    # The release must carry its own migration runner. Production hosts only receive
    # published artifacts, so applying migrations cannot depend on an installed SDK.
    & dotnet build (Join-Path $repoRoot "src/GZCTF/GZCTF.csproj") `
        -c Migration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "migration build failed with exit code $LASTEXITCODE" }
    $migrationBundle = Join-Path $publishRoot "efbundle"
    & dotnet ef migrations bundle `
        --project (Join-Path $repoRoot "src/GZCTF/GZCTF.csproj") `
        --startup-project (Join-Path $repoRoot "src/GZCTF/GZCTF.csproj") `
        --configuration Migration --no-build --target-runtime linux-x64 --self-contained `
        --output $migrationBundle --force
    if ($LASTEXITCODE -ne 0) { throw "EF migration bundle failed with exit code $LASTEXITCODE" }
}

$required = @(
    "GZCTF",
    "GZCTF.dll",
    "efbundle",
    "agent/gzctf-agent",
    "agent/endpoint-sensor/linux-x64/gzctf-endpoint-sensor",
    "agent/endpoint-sensor/win-x64/gzctf-endpoint-sensor.exe",
    "agent/guest-supervisor/linux-x64/gzctf-guest-supervisor",
    "agent/guest-supervisor/win-x64/gzctf-guest-supervisor.exe"
)
foreach ($relative in $required) {
    if (-not (Test-Path (Join-Path $publishRoot $relative) -PathType Leaf)) {
        throw "Required release artifact is missing: $relative"
    }
}

$files = Get-ChildItem $publishRoot -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($publishRoot.Length).TrimStart('\', '/').Replace('\', '/')
    [ordered]@{
        path = $relative
        size = $_.Length
        sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$manifest = [ordered]@{
    schemaVersion = 1
    releaseId = $ReleaseId
    gitCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
    createdAt = [DateTimeOffset]::UtcNow.ToString("O")
    files = @($files)
}
$manifestJson = $manifest | ConvertTo-Json -Depth 6
[IO.File]::WriteAllText($manifestPath, $manifestJson, [Text.UTF8Encoding]::new($false))
Copy-Item $manifestPath (Join-Path $publishRoot "release-manifest.json")

& tar -C $publishRoot -czf $archivePath .
if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE" }
$archiveSha = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()

[ordered]@{
    releaseId = $ReleaseId
    archive = $archivePath
    archiveSha256 = $archiveSha
    manifest = $manifestPath
    fileCount = $files.Count
} | ConvertTo-Json -Depth 4
