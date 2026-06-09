from pathlib import Path
import paramiko, os, glob, sys

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
HOST = os.environ.get('YINYU_DEPLOY_HOST')
USER = os.environ.get('YINYU_DEPLOY_USER', 'ubuntu')
PASS = os.environ.get('YINYU_DEPLOY_PASS')
PROJECT_ROOT = Path(os.environ.get('YINYU_PROJECT_ROOT', Path(__file__).resolve().parents[1]))
REMOTE_ROOT = os.environ.get('YINYU_REMOTE_ROOT', f'/home/{USER}/yinyu-ctf-platform')

if not HOST or not PASS:
    print('Set YINYU_DEPLOY_HOST and YINYU_DEPLOY_PASS before running this script.', file=sys.stderr)
    sys.exit(2)

ssh.connect(HOST, username=USER, password=PASS, timeout=30)
sftp = ssh.open_sftp()

# Stop server
ssh.exec_command('sudo pkill -9 -f GZCTF 2>/dev/null')
print('Server stopped')

# Upload backend DLL
backend_target = f'{REMOTE_ROOT}/src/GZCTF/bin/Release/net10.0'
local_dll = os.environ.get(
    'YINYU_LOCAL_DLL',
    str(PROJECT_ROOT / 'src' / 'GZCTF' / 'bin' / 'Release' / 'net10.0' / 'GZCTF.dll'),
)
sftp.put(local_dll, f'{backend_target}/GZCTF.dll')
print('Backend DLL uploaded')

# Upload frontend
frontend = os.environ.get(
    'YINYU_LOCAL_BUILD',
    str(PROJECT_ROOT / 'src' / 'GZCTF' / 'ClientApp' / 'build'),
)
www = f'{REMOTE_ROOT}/src/GZCTF/wwwroot'

ssh.exec_command(f'mkdir -p {www}/static')
sftp.put(f'{frontend}/index.html', f'{www}/index.html')
print('index.html uploaded')

# Upload static files
static_src = os.path.join(frontend, 'static')
for f in os.listdir(static_src):
    src = os.path.join(static_src, f)
    dst = f'{www}/static/{f}'
    sftp.put(src, dst)

print(f'Frontend uploaded ({len(os.listdir(static_src))} static files)')
sftp.close()

# Start server
stdin, stdout, stderr = ssh.exec_command(
    f'cd {REMOTE_ROOT}/src/GZCTF && '
    'ASPNETCORE_URLS=http://0.0.0.0:8080 '
    'ASPNETCORE_ENVIRONMENT=Production '
    'YES_I_KNOW_FILES_ARE_NOT_PERSISTED_GO_AHEAD_PLEASE=1 '
    'nohup /usr/local/share/dotnet/dotnet '
    f'{REMOTE_ROOT}/src/GZCTF/bin/Release/net10.0/GZCTF.dll '
    '> /tmp/gzctf.log 2>&1 &'
)
print('Server start command sent')

# Verify
import time
time.sleep(5)
stdin2, stdout2, stderr2 = ssh.exec_command('ps aux | grep GZCTF | grep -v grep')
print(f'Process: {stdout2.read().decode()}')

stdin3, stdout3, stderr3 = ssh.exec_command('tail -5 /tmp/gzctf.log')
print(f'Log tail: {stdout3.read().decode()}')
ssh.close()
print('Deploy complete')
