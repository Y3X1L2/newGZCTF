"""Start yinyu-ctf-platform on server — fire and forget version."""
import os, paramiko, json, time, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
HOST = os.environ.get('YINYU_DEPLOY_HOST')
USER = os.environ.get('YINYU_DEPLOY_USER', 'ubuntu')
PASS = os.environ.get('YINYU_DEPLOY_PASS')
REMOTE_ROOT = os.environ.get('YINYU_REMOTE_ROOT', f'/home/{USER}/yinyu-ctf-platform')

if not HOST or not PASS:
    print('Set YINYU_DEPLOY_HOST and YINYU_DEPLOY_PASS before running this script.', file=sys.stderr)
    sys.exit(2)

ssh.connect(HOST, username=USER, password=PASS, timeout=10)

# Step 1: Kill existing processes
ssh.exec_command('sudo pkill -9 dotnet 2>/dev/null; sudo fuser -k 3000/tcp 2>/dev/null; true')
time.sleep(3)

# Step 2: Write config
config = json.dumps({
    'XorKey': os.environ.get('YINYU_TEST_XOR_KEY', 'replace-this-test-xor-key'),
    'ConnectionStrings': {
        'Database': os.environ.get(
            'YINYU_TEST_DATABASE',
            'Host=localhost;Port=5432;Database=yinyu_ctf;Username=postgres;Password=change-me',
        ),
        'RedisCache': os.environ.get('YINYU_TEST_REDIS', 'localhost:6379')
    }
}, indent=2)
sftp = ssh.open_sftp()
with sftp.file(f'{REMOTE_ROOT}/src/GZCTF/appsettings.json', 'w') as f:
    f.write(config)
sftp.close()
print("Config OK")

# Step 3: Start (don't wait for PID output)
channel = ssh.get_transport().open_session()
channel.exec_command(
    'export PATH=/usr/local/share/dotnet:$PATH; '
    f'cd {REMOTE_ROOT}; '
    'ASPNETCORE_URLS=http://0.0.0.0:8080 '
    'nohup dotnet run --project src/GZCTF/GZCTF.csproj -c Release --no-build '
    '> /tmp/gzctf.log 2>&1 &'
)
print("Started, waiting 35s...")
time.sleep(35)

# Step 4: Check
_, out, _ = ssh.exec_command('curl -s http://localhost:8080/api/info', timeout=10)
resp = out.read().decode('utf-8', errors='replace')
if resp and len(resp) > 10 and resp[0] in '{[':
    print('SUCCESS!')
    print(resp[:400])
else:
    _, out, _ = ssh.exec_command('grep "Now listening\|Hosting failed\|Application started\|ftl" /tmp/gzctf.log | tail -3', timeout=5)
    print('Status:', out.read().decode('utf-8', errors='replace')[:500])

ssh.close()
