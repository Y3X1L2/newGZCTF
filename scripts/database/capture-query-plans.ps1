[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ConnectionString,
    [Parameter(Mandatory)] [string] $OutputPath,
    [ValidateSet('ci', 'commercial')] [string] $Profile = 'ci',
    [string] $PsqlDockerContainer
)

$ErrorActionPreference = 'Stop'
$seed = Join-Path $PSScriptRoot 'sql/seed-commercial-baseline.sql'
$contracts = Join-Path $PSScriptRoot 'sql/query-plan-contracts.sql'
$psql = if ($PsqlDockerContainer) { $null } else { Get-Command psql -ErrorAction Stop }

function ConvertTo-PsqlArguments([string] $value) {
    if ($value -match '^postgres(ql)?://') {
        return @{
            Arguments = @($value)
            Database = ([uri]$value).AbsolutePath.TrimStart('/')
            UserName = ([uri]$value).UserInfo.Split(':')[0]
            Password = $null
        }
    }

    $settings = @{}
    foreach ($part in $value -split ';') {
        if ([string]::IsNullOrWhiteSpace($part)) { continue }
        $pair = $part -split '=', 2
        if ($pair.Count -eq 2) { $settings[$pair[0].Trim().ToLowerInvariant()] = $pair[1].Trim() }
    }
    $database = $settings['database']
    if ([string]::IsNullOrWhiteSpace($database)) { throw 'Connection string must include Database.' }
    $hostName = if ($settings['host']) { $settings['host'] } else { 'localhost' }
    $port = if ($settings['port']) { $settings['port'] } else { '5432' }
    $userName = if ($settings['username']) {
        $settings['username']
    } elseif ($settings['user id']) {
        $settings['user id']
    } else {
        'postgres'
    }
    $arguments = @(
        '-h', $hostName,
        '-p', $port,
        '-U', $userName,
        '-d', $database
    )
    return @{
        Arguments = $arguments
        Database = $database
        UserName = $userName
        Password = $settings['password']
    }
}

$connection = ConvertTo-PsqlArguments $ConnectionString
if ($connection.Database -notmatch '(?i)(benchmark|phase.?4|test|ci)') {
    throw "Refusing to seed database '$($connection.Database)'. Use a dedicated benchmark/test database."
}

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
$previousPassword = $env:PGPASSWORD
try {
    if ($connection.Password) { $env:PGPASSWORD = $connection.Password }
    if ($PsqlDockerContainer) {
        $containerPassword = "PGPASSWORD=$($connection.Password)"
        Get-Content $seed -Raw | & docker exec -i -e $containerPassword $PsqlDockerContainer `
            psql -U $connection.UserName -d $connection.Database -X -q -v ON_ERROR_STOP=1 -v "profile=$Profile"
    } else {
        & $psql.Source @($connection.Arguments) -X -q -v ON_ERROR_STOP=1 -v "profile=$Profile" -f $seed
    }
    if ($LASTEXITCODE -ne 0) { throw "Benchmark seed failed with exit code $LASTEXITCODE." }

    if ($PsqlDockerContainer) {
        $raw = (Get-Content $contracts -Raw | & docker exec -i -e $containerPassword $PsqlDockerContainer `
            psql -U $connection.UserName -d $connection.Database -X -A -t -q -v ON_ERROR_STOP=1) -join "`n"
    } else {
        $raw = (& $psql.Source @($connection.Arguments) -X -A -t -q -v ON_ERROR_STOP=1 -f $contracts) -join "`n"
    }
    if ($LASTEXITCODE -ne 0) { throw "Query-plan capture failed with exit code $LASTEXITCODE." }

    $sections = @([regex]::Split($raw, '(?m)^__PLAN__:') |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($sections.Count -ne 7) {
        throw "Expected 7 query plans, captured $($sections.Count)."
    }
    foreach ($section in $sections) {
        $lineBreak = $section.IndexOf("`n")
        if ($lineBreak -lt 1) { throw 'Captured query-plan section has no JSON body.' }
        $name = $section.Substring(0, $lineBreak).Trim()
        $json = $section.Substring($lineBreak + 1).Trim()
        $null = $json | ConvertFrom-Json
        [IO.File]::WriteAllText(
            (Join-Path $OutputPath "$name.json"),
            $json,
            [Text.UTF8Encoding]::new($false))
    }
}
finally {
    $env:PGPASSWORD = $previousPassword
}

Write-Host "Captured $($sections.Count) Phase 4 query plans in $OutputPath."
