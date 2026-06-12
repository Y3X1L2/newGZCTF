import os, sys
import paramiko, time

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
HOST = os.environ.get('YINYU_DEPLOY_HOST')
USER = os.environ.get('YINYU_DEPLOY_USER', 'ubuntu')
PASS = os.environ.get('YINYU_DEPLOY_PASS')
REMOTE_ROOT = os.environ.get('YINYU_REMOTE_ROOT', f'/home/{USER}/yinyu-ctf-platform')
DB_CONTAINER = os.environ.get('YINYU_DB_CONTAINER', 'yinyu-ctf-db-1')
DB_NAME = os.environ.get('YINYU_DB_NAME', 'gzctf')

if not HOST or not PASS:
    print('Set YINYU_DEPLOY_HOST and YINYU_DEPLOY_PASS before running this script.', file=sys.stderr)
    sys.exit(2)

ssh.connect(HOST, username=USER, password=PASS, timeout=10, look_for_keys=False, allow_agent=False)
DOTNET = '/usr/local/share/dotnet/dotnet'
PROJ = f'{REMOTE_ROOT}/src/GZCTF'

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
ssh.exec_command(f'docker cp /tmp/migration.sql {DB_CONTAINER}:/tmp/migration.sql')
_, stdout, stderr = ssh.exec_command(
    f'docker exec {DB_CONTAINER} psql -U postgres -d {DB_NAME} -f /tmp/migration.sql 2>&1'
)
print('SQL result:', stdout.read().decode() + stderr.read().decode())

# Verify columns
_, stdout, _ = ssh.exec_command(
    f"docker exec {DB_CONTAINER} psql -U postgres -d {DB_NAME} -t -c "
    "\"SELECT COUNT(*) FROM information_schema.columns WHERE table_name='GameChallenges' AND column_name IN ('Environment','ImageTemplateId')\" 2>&1"
)
print(f'GameChallenges cols: {stdout.read().decode().strip()} (expect 2)')

_, stdout, _ = ssh.exec_command(
    f"docker exec {DB_CONTAINER} psql -U postgres -d {DB_NAME} -t -c "
    "\"SELECT COUNT(*) FROM information_schema.columns WHERE table_name='ExerciseChallenges' AND column_name IN ('Environment','ImageTemplateId')\" 2>&1"
)
print(f'ExerciseChallenges cols: {stdout.read().decode().strip()} (expect 2)')

# Check migration history
_, stdout, _ = ssh.exec_command(
    f'docker exec {DB_CONTAINER} psql -U postgres -d {DB_NAME} -c '
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
