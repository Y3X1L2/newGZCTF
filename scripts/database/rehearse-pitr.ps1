param(
    [int]$SourcePort = 55439,
    [int]$RestorePort = 55440,
    [switch]$KeepResources
)

$ErrorActionPreference = 'Stop'
$phaseThreeMigration = '20260712054103_CompleteTeamLabRuntimeReliability'
$database = 'gzctf_phase4_pitr'
$password = 'postgres'
$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 10)
$sourceContainer = "gzctf-pitr-source-$suffix"
$restoreContainer = "gzctf-pitr-restore-$suffix"
$dataVolume = "gzctf-pitr-data-$suffix"
$archiveVolume = "gzctf-pitr-archive-$suffix"
$baseVolume = "gzctf-pitr-base-$suffix"
$volumes = @($dataVolume, $archiveVolume, $baseVolume)

function Invoke-Docker {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Wait-Postgres {
    param([string]$Container)

    for ($attempt = 1; $attempt -le 60; $attempt++) {
        $previousErrorPreference = $ErrorActionPreference
        $ErrorActionPreference = 'SilentlyContinue'
        & docker exec $Container pg_isready -U postgres -d $database *> $null
        $readyExitCode = $LASTEXITCODE
        $ErrorActionPreference = $previousErrorPreference
        if ($readyExitCode -eq 0) {
            return
        }
        $running = & docker inspect --format '{{.State.Running}}' $Container 2>$null
        if ($running -eq 'false') {
            $logs = & cmd /c "docker logs $Container 2>&1" | Out-String
            throw "PostgreSQL container $Container exited during startup:`n$logs"
        }
        Start-Sleep -Seconds 1
    }
    throw "PostgreSQL container $Container did not become ready."
}

function Invoke-Psql {
    param([string]$Container, [string]$Sql)

    $output = $Sql | & docker exec -i -e "PGPASSWORD=$password" $Container `
        psql -v ON_ERROR_STOP=1 -U postgres -d $database -At
    if ($LASTEXITCODE -ne 0) {
        throw "psql failed in $Container."
    }
    return ($output | Out-String).Trim()
}

function Wait-WalArchived {
    param([string]$Container, [string]$WalFile)

    for ($attempt = 1; $attempt -le 30; $attempt++) {
        $previousErrorPreference = $ErrorActionPreference
        $ErrorActionPreference = 'SilentlyContinue'
        & docker exec $Container test -f "/archive/$WalFile" *> $null
        $archived = $LASTEXITCODE -eq 0
        $ErrorActionPreference = $previousErrorPreference
        if ($archived) {
            return
        }
        Start-Sleep -Seconds 1
    }
    throw "WAL segment $WalFile was not archived by $Container."
}

try {
    foreach ($volume in $volumes) {
        Invoke-Docker volume create $volume | Out-Null
    }
    $prepareArchiveArgs = @(
        'run', '--rm', '--entrypoint', 'sh', '-v', "${archiveVolume}:/archive",
        'postgres:16-alpine', '-c', 'chown 70:70 /archive'
    )
    Invoke-Docker @prepareArchiveArgs | Out-Null

    $sourceArgs = @(
        'run', '-d', '--name', $sourceContainer,
        '-e', "POSTGRES_PASSWORD=$password",
        '-e', "POSTGRES_DB=$database",
        '-p', "${SourcePort}:5432",
        '-v', "${dataVolume}:/var/lib/postgresql/data",
        '-v', "${archiveVolume}:/archive",
        '-v', "${baseVolume}:/base",
        'postgres:16-alpine',
        'postgres',
        '-c', 'wal_level=replica',
        '-c', 'archive_mode=on',
        '-c', 'archive_timeout=1s',
        '-c', 'archive_command=test ! -f /archive/%f && cp %p /archive/%f'
    )
    Invoke-Docker @sourceArgs | Out-Null
    Wait-Postgres $sourceContainer

    $sourceConnection = "Host=127.0.0.1;Port=$SourcePort;Database=$database;Username=postgres;Password=$password"
    & dotnet ef database update $phaseThreeMigration --project src/GZCTF/GZCTF.csproj --configuration Release --no-build --connection $sourceConnection
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to migrate the PITR source database to the Phase 3 baseline.'
    }

    Invoke-Psql $sourceContainer @'
INSERT INTO "Logs" ("Id", "TimeUtc", "Level", "Logger", "Message")
VALUES (970001, CURRENT_TIMESTAMP, 'Information', 'Phase4.Pitr', 'before-contract');
CHECKPOINT;
'@ | Out-Null
    $baselineWal = Invoke-Psql $sourceContainer 'SELECT pg_walfile_name(pg_switch_wal())'
    Wait-WalArchived $sourceContainer $baselineWal

    $baseBackupArgs = @(
        'exec', '-e', "PGPASSWORD=$password", $sourceContainer,
        'pg_basebackup', '-h', '127.0.0.1', '-U', 'postgres', '-D', '/base', '-Fp', '-Xs', '-P'
    )
    Invoke-Docker @baseBackupArgs | Out-Null
    $recoveryTarget = Invoke-Psql $sourceContainer "SELECT to_char(clock_timestamp(), 'YYYY-MM-DD HH24:MI:SS.USOF')"

    & dotnet ef database update --project src/GZCTF/GZCTF.csproj --configuration Release --no-build --connection $sourceConnection
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to migrate the PITR source database to the Phase 4 contract.'
    }
    Invoke-Psql $sourceContainer @'
INSERT INTO "Logs" ("Id", "TimeUtc", "Level", "Logger", "Message")
VALUES (970002, CURRENT_TIMESTAMP, 'Information', 'Phase4.Pitr', 'after-contract');
'@ | Out-Null
    $contractWal = Invoke-Psql $sourceContainer 'SELECT pg_walfile_name(pg_switch_wal())'
    Wait-WalArchived $sourceContainer $contractWal
    Invoke-Docker stop $sourceContainer | Out-Null

    $configureRecoveryArgs = @(
        'run', '--rm', '--entrypoint', 'sh',
        '-v', "${baseVolume}:/base", '-v', "${archiveVolume}:/archive",
        'postgres:16-alpine', '-c', 'touch /base/recovery.signal'
    )
    Invoke-Docker @configureRecoveryArgs | Out-Null

    $restoreArgs = @(
        'run', '-d', '--name', $restoreContainer,
        '-p', "${RestorePort}:5432",
        '-v', "${baseVolume}:/var/lib/postgresql/data",
        '-v', "${archiveVolume}:/archive",
        'postgres:16-alpine',
        'postgres',
        '-c', 'restore_command=cp /archive/%f %p',
        '-c', "recovery_target_time=$recoveryTarget",
        '-c', 'recovery_target_action=promote'
    )
    Invoke-Docker @restoreArgs | Out-Null
    Wait-Postgres $restoreContainer

    $migrationHead = Invoke-Psql $restoreContainer 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1'
    $beforeCount = Invoke-Psql $restoreContainer 'SELECT count(*) FROM "Logs" WHERE "Id" = 970001'
    $afterCount = Invoke-Psql $restoreContainer 'SELECT count(*) FROM "Logs" WHERE "Id" = 970002'
    if ($migrationHead -ne $phaseThreeMigration -or $beforeCount -ne '1' -or $afterCount -ne '0') {
        throw "PITR verification failed: migration=$migrationHead, before=$beforeCount, after=$afterCount."
    }

    [PSCustomObject]@{
        Status = 'passed'
        RecoveryTargetUtc = $recoveryTarget
        MigrationHead = $migrationHead
        PreContractMarkerCount = [int]$beforeCount
        PostContractMarkerCount = [int]$afterCount
    } | ConvertTo-Json
}
finally {
    if (-not $KeepResources) {
        $previousErrorPreference = $ErrorActionPreference
        $ErrorActionPreference = 'SilentlyContinue'
        & docker rm -f $sourceContainer $restoreContainer *> $null
        foreach ($volume in $volumes) {
            & docker volume rm -f $volume *> $null
        }
        $ErrorActionPreference = $previousErrorPreference
    }
}
