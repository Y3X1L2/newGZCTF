#!/usr/bin/env python3
import paramiko, os, sys, time

HOST = '203.195.157.191'
USER = 'ubuntu'
PASS = 'Fisher(1^'
BASE = '/home/ubuntu/newGZCTF/src/GZCTF'
LOCAL_DLL = r'D:\newGZ\newGZCTF\src\GZCTF\bin\Release\net10.0\GZCTF.dll'
LOCAL_BUILD = r'D:\newGZ\newGZCTF\src\GZCTF\ClientApp\build'

try:
    print('1/5 Connecting...', flush=True)
    ssh = paramiko.SSHClient()
    ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    ssh.connect(HOST, username=USER, password=PASS, timeout=30,
                look_for_keys=False, allow_agent=False, banner_timeout=30)
    print('Connected.', flush=True)

    print('2/5 Stopping server...', flush=True)
    ssh.exec_command('sudo pkill -9 -f GZCTF 2>/dev/null')
    time.sleep(2)
    print('Stopped.', flush=True)

    print('3/5 Uploading backend...', flush=True)
    sftp = ssh.open_sftp()
    sftp.put(LOCAL_DLL, f'{BASE}/bin/Release/net10.0/GZCTF.dll')
    sftp.close()
    print('Backend uploaded.', flush=True)

    print('4/5 Uploading frontend...', flush=True)
    sftp = ssh.open_sftp()
    sftp.put(f'{LOCAL_BUILD}/index.html', f'{BASE}/wwwroot/index.html')
    ssh.exec_command(f'mkdir -p {BASE}/wwwroot/static')
    count = 0
    for f in os.listdir(f'{LOCAL_BUILD}/static'):
        src = os.path.join(LOCAL_BUILD, 'static', f)
        sftp.put(src, f'{BASE}/wwwroot/static/{f}')
        count += 1
    sftp.close()
    print(f'Frontend uploaded ({count} files).', flush=True)

    print('5/5 Restarting server...', flush=True)
    cmd = (f'cd {BASE} && ASPNETCORE_URLS=http://0.0.0.0:8080 '
           f'ASPNETCORE_ENVIRONMENT=Production '
           f'YES_I_KNOW_FILES_ARE_NOT_PERSISTED_GO_AHEAD_PLEASE=1 '
           f'nohup /usr/local/share/dotnet/dotnet {BASE}/bin/Release/net10.0/GZCTF.dll '
           f'> /tmp/gzctf.log 2>&1 &')
    ssh.exec_command(cmd)
    time.sleep(4)

    _, stdout, _ = ssh.exec_command('ps aux | grep GZCTF | grep -v grep')
    proc = stdout.read().decode().strip()
    if proc:
        print(f'OK - Process running: {proc[:100]}', flush=True)
    else:
        print('WARN - Process may need time to start', flush=True)
        _, out, _ = ssh.exec_command('tail -5 /tmp/gzctf.log')
        print(f'Log: {out.read().decode()[:300]}', flush=True)

    ssh.close()
    print('DEPLOY SUCCESS', flush=True)
    sys.exit(0)

except Exception as e:
    print(f'FAILED: {e}', file=sys.stderr, flush=True)
    sys.exit(1)
