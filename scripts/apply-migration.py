import paramiko, time

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('203.195.157.191', username='ubuntu', password='Fisher(1^', timeout=10, look_for_keys=False, allow_agent=False)
DOTNET = '/usr/local/share/dotnet/dotnet'
PROJ = '/home/ubuntu/newGZCTF/src/GZCTF'

# Write SQL to temp file on server (avoids quoting issues)
sql = r"""
ALTER TABLE "GameChallenges" ADD COLUMN IF NOT EXISTS "Environment" smallint NOT NULL DEFAULT 0;
ALTER TABLE "GameChallenges" ADD COLUMN IF NOT EXISTS "ImageTemplateId" integer;
ALTER TABLE "ExerciseChallenges" ADD COLUMN IF NOT EXISTS "Environment" smallint NOT NULL DEFAULT 0;
ALTER TABLE "ExerciseChallenges" ADD COLUMN IF NOT EXISTS "ImageTemplateId" integer;
ALTER TABLE "Containers" DROP COLUMN IF EXISTS "ExerciseInstanceId";
-- Remove the incorrect migration record
DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260521163925_SyncChallengeModel';
-- Also remove any earlier partial ones
DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '%SyncChallengeModel%';
"""

_, stdout, stderr = ssh.exec_command(f'cat > /tmp/migration.sql << \'SQLEOF\'\n{sql}\nSQLEOF')
ssh.exec_command('docker cp /tmp/migration.sql newgzctf-db-1:/tmp/migration.sql')
_, stdout, stderr = ssh.exec_command(
    'docker exec newgzctf-db-1 psql -U postgres -d gzctf -f /tmp/migration.sql 2>&1'
)
print('SQL result:', stdout.read().decode() + stderr.read().decode())

# Verify columns
_, stdout, _ = ssh.exec_command(
    "docker exec newgzctf-db-1 psql -U postgres -d gzctf -t -c "
    "\"SELECT COUNT(*) FROM information_schema.columns WHERE table_name='GameChallenges' AND column_name IN ('Environment','ImageTemplateId')\" 2>&1"
)
print(f'GameChallenges cols: {stdout.read().decode().strip()} (expect 2)')

_, stdout, _ = ssh.exec_command(
    "docker exec newgzctf-db-1 psql -U postgres -d gzctf -t -c "
    "\"SELECT COUNT(*) FROM information_schema.columns WHERE table_name='ExerciseChallenges' AND column_name IN ('Environment','ImageTemplateId')\" 2>&1"
)
print(f'ExerciseChallenges cols: {stdout.read().decode().strip()} (expect 2)')

# Check migration history
_, stdout, _ = ssh.exec_command(
    'docker exec newgzctf-db-1 psql -U postgres -d gzctf -c '
    '"SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 3" 2>&1'
)
print('Last migrations:', stdout.read().decode()[:200])

# Clean up ConfigureWarnings hack — not needed if model matches DB
# Restart
ssh.exec_command('sudo pkill -9 -f GZCTF 2>/dev/null')
time.sleep(2)
ssh.exec_command(
    f'cd {PROJ} && ASPNETCORE_URLS=http://0.0.0.0:8080 '
    'ASPNETCORE_ENVIRONMENT=Production YES_I_KNOW_FILES_ARE_NOT_PERSISTED_GO_AHEAD_PLEASE=1 '
    f'nohup {DOTNET} "{PROJ}/bin/Release/net10.0/GZCTF.dll" > /tmp/gzctf.log 2>&1 &'
)
time.sleep(10)

_, stdout, _ = ssh.exec_command('curl -so /dev/null -w "%{http_code}" http://localhost:8080/')
http = stdout.read().decode()
_, stdout, _ = ssh.exec_command('tail -5 /tmp/gzctf.log')
log = stdout.read().decode('utf-8', errors='replace')
has_err = 'ERR' in log or 'FTL' in log
print(f'HTTP: {http} | {"ERRORS" if has_err else "Healthy"}')
if has_err:
    for line in log.split('\n'):
        if 'ERR' in line or 'FTL' in line: print(' ', line[:250])

ssh.close()
print('DONE')
