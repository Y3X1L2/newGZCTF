$ErrorActionPreference = 'Stop'
$agent = (docker inspect gzctf-agent-local-sim | ConvertFrom-Json)[0]
if ($LASTEXITCODE -ne 0) { throw 'Local file QA Agent not found.' }
$auth = $agent.Config.Env | Where-Object { $_ -like 'Agent__AuthToken=*' } | Select-Object -First 1
if (-not $auth) { throw 'Agent authentication is not configured.' }
$headers = @{ Authorization = 'Bearer ' + $auth.Substring('Agent__AuthToken='.Length) }
$containerId = $null
try {
    $containerId = docker run -d --network none --label ManagedBy=GZCTF --label GZCTF.RuntimeId=987604 --label GZCTF.Generation=3 --entrypoint /bin/sh postgres:16-alpine -c 'mkdir /tmp/files; ln -s /etc/passwd /tmp/files/link; mkfifo /tmp/files/pipe; sleep 300'
    if ($LASTEXITCODE -ne 0) { throw 'Could not create file fixture.' }
    $containerId = $containerId.Trim()
    $url = 'http://127.0.0.1:5001/api/teamlab/diagnostics/container/files'
    $body = @{ runtimeId = 987604; generation = 3; containerId = $containerId; operation = 'list'; path = '/tmp/files' }
    function Request { Invoke-RestMethod $url -Method Post -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json -Compress) -TimeoutSec 45 }
    function Rejected {
        $response = Invoke-WebRequest $url -Method Post -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json -Compress) -SkipHttpErrorCheck -TimeoutSec 45
        if ($response.StatusCode -lt 400) { throw 'Unsafe file request was accepted.' }
    }
    $listing = Request
    if (@($listing.entries | Where-Object kind -eq 'restricted').Count -ne 2) { throw 'Links/special files must be restricted.' }
    $filename = [string]::Concat([char]0x4e2d, [char]0x6587, '.txt')
    $body.path = '/tmp/files/' + $filename
    $body.operation = 'upload'
    $body.content = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('UTF-8 file evidence'))
    $body.overwrite = $true
    $null = Request
    $body.overwrite = $false
    Rejected
    docker exec $containerId chmod 755 $body.path
    docker exec $containerId chown 999:999 $body.path
    $body.overwrite = $true
    $body.content = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('replaced'))
    $null = Request
    $permissions = docker exec $containerId stat -c '%a:%u:%g' $body.path
    if ($permissions.Trim() -ne '755:999:999') { throw 'Overwrite did not preserve executable permissions and ownership.' }
    $body.operation = 'download'
    $download = Request
    if ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($download.content)) -ne 'replaced') { throw 'File round trip failed.' }
    $body.path = '/tmp/files/link'
    Rejected
    $body.path = '/tmp/files/pipe'
    Rejected
    $body.path = '/etc/hosts'
    Rejected
    $body.path = '/tmp/../etc/passwd'
    Rejected
    $body.path = '/tmp/files/' + $filename
    $body.generation = 2
    Rejected
    $body.generation = 3
    $body.runtimeId = 987605
    Rejected
    $body.runtimeId = 987604
    $body.operation = 'delete'
    $null = Request
    $body.path = '/tmp/files'
    $body.operation = 'list'
    docker exec $containerId mkdir -p /tmp/files/.gzctf-upload-staging
    docker exec $containerId touch -t 202601010000 /tmp/files/.gzctf-upload-staging/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
    docker exec $containerId touch /tmp/files/.gzctf-upload-staging/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
    $listing = Request
    if (@($listing.entries).Count -ne 2) { throw 'Upload staging residue or undeleted file remains.' }
    docker exec $containerId test ! -e /tmp/files/.gzctf-upload-staging/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
    if ($LASTEXITCODE -ne 0) { throw 'Stale interrupted upload was not reclaimed.' }
    docker exec $containerId test -f /tmp/files/.gzctf-upload-staging/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
    if ($LASTEXITCODE -ne 0) { throw 'Recent upload staging was removed too early.' }
    $body.operation = 'download'
    $body.path = '/tmp/files/.gzctf-upload-staging/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
    Rejected
    Write-Output 'PASS: directory, Unicode filename, upload/download, no-overwrite, confirmed overwrite, delete, mount/link/special-file refusal, generation/ownership and staging cleanup.'
} finally {
    $headers.Clear()
    $auth = $null
    if ($containerId -and $containerId -match '^[a-f0-9]{64}$') { docker rm -fv $containerId | Out-Null }
}
