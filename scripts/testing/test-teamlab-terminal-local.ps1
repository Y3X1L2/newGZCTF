param([string]$AgentContainer = 'gzctf-agent-local-sim')
$ErrorActionPreference = 'Stop'

$agent = docker inspect $AgentContainer | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Local simulation Agent not found.' }
$authEntry = $agent[0].Config.Env | Where-Object { $_ -like 'Agent__AuthToken=*' } | Select-Object -First 1
if (-not $authEntry) { throw 'Local simulation Agent authentication is not configured in its environment.' }
$authValue = $authEntry.Substring('Agent__AuthToken='.Length)
$headers = @{ Authorization = 'Bearer ' + $authValue }
$containerName = 'teamlab-terminal-check-' + [guid]::NewGuid().ToString('N')
$containerId = $null
$socket = [System.Net.WebSockets.ClientWebSocket]::new()
$deadline = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(45))
$decoder = [System.Text.UTF8Encoding]::new($false, $true).GetDecoder()
$received = [System.Text.StringBuilder]::new()

function Send-TerminalBytes([byte[]]$Bytes, [System.Net.WebSockets.WebSocketMessageType]$Type = 'Binary') {
    Write-Verbose "Sending $($Bytes.Length) bytes as $Type."
    [void]$socket.SendAsync([ArraySegment[byte]]::new($Bytes), $Type, $true, $deadline.Token).GetAwaiter().GetResult()
}

function Send-TerminalText([string]$Text) {
    Send-TerminalBytes ([System.Text.Encoding]::UTF8.GetBytes($Text))
}

function Receive-Until([string]$Pattern) {
    $buffer = [byte[]]::new(8192)
    $chars = [char[]]::new(8192)
    while ($received.ToString() -notmatch $Pattern) {
        $message = $socket.ReceiveAsync([ArraySegment[byte]]::new($buffer), $deadline.Token).GetAwaiter().GetResult()
        if ($message.MessageType -ne 'Binary') { throw 'Terminal output was not binary.' }
        $count = $decoder.GetChars($buffer, 0, $message.Count, $chars, 0, $false)
        [void]$received.Append($chars, 0, $count)
        Write-Verbose ([string]::new($chars, 0, $count).Replace([string][char]27, '<ESC>'))
        if ([string]::new($chars, 0, $count).Contains("$([char]27)[6n")) {
            Send-TerminalText "$([char]27)[1;1R"
        }
        if ($received.Length -gt 65536) { throw 'Terminal output exceeded the test limit.' }
    }
}

try {
    $containerId = docker run -d --name $containerName --network none --env SHELL=/bin/bash --env 'PS1=TLREADY>' --label ManagedBy=GZCTF --label GZCTF.RuntimeId=987601 --label GZCTF.Generation=1 --entrypoint /bin/sleep postgres:16-alpine 300
    if ($LASTEXITCODE -ne 0) { throw 'Could not start isolated terminal fixture.' }
    $containerId = $containerId.Trim()
    $sessionId = [guid]::NewGuid().ToString()
    $expiry = [uri]::EscapeDataString([DateTimeOffset]::UtcNow.AddMinutes(2).ToString('O'))
    $url = "ws://127.0.0.1:5001/api/remote-access/terminals/${sessionId}?runtimeId=987601&generation=1&containerId=${containerId}&expiresAt=${expiry}"
    $socket.Options.SetRequestHeader('Authorization', 'Bearer ' + $authValue)
    [void]$socket.ConnectAsync([uri]$url, $deadline.Token).GetAwaiter().GetResult()
    Receive-Until 'TLREADY>'
    [void]$received.Clear()
    Send-TerminalBytes ([System.Text.Encoding]::UTF8.GetBytes('{"type":"resize","cols":103,"rows":37}')) 'Text'
    Receive-Until 'TLREADY>'
    [void]$received.Clear()
    Send-TerminalText "stty size`r"
    Receive-Until '37 103\r?\n'
    Receive-Until 'TLREADY>'
    Write-Output 'PASS: PTY resize.'
    [void]$received.Clear()
    # Octal output avoids mistaking the PTY's command echo for successful UTF-8 output.
    Send-TerminalText "printf '\344\270\255\346\226\207\347\273\210\347\253\257\n'`r"
    Receive-Until ([string]::Concat([char]0x4e2d, [char]0x6587, [char]0x7ec8, [char]0x7aef))
    Receive-Until 'TLREADY>'
    Write-Output 'PASS: UTF-8 output.'
    [void]$received.Clear()
    Send-TerminalText "sleep 30`r"
    $sleepDeadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    do {
        $processes = docker top $containerId -eo pid,args
        if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the foreground process.' }
        $sleepStarted = @($processes | Where-Object { $_ -match '^\s*\d+\s+sleep 30\s*$' }).Count -gt 0
        if ($sleepStarted) { break }
        [System.Threading.Tasks.Task]::Delay(50).GetAwaiter().GetResult() | Out-Null
    } while ([DateTimeOffset]::UtcNow -lt $sleepDeadline)
    if (-not $sleepStarted) { throw 'Foreground sleep did not start.' }
    Send-TerminalBytes ([byte[]]@(3))
    Receive-Until 'TLREADY>'
    [void]$received.Clear()
    Send-TerminalText "printf '\111\116\124\105\122\122\125\120\124\105\104\n'`r"
    Receive-Until 'INTERRUPTED\r?\n'
    $inventory = Invoke-RestMethod 'http://127.0.0.1:5001/api/remote-access/inventory' -Headers $headers -TimeoutSec 5
    if ($sessionId -notin $inventory) { throw 'Connected session missing from Agent inventory.' }
    Invoke-RestMethod "http://127.0.0.1:5001/api/remote-access/terminals/${sessionId}" -Method Delete -Headers $headers -TimeoutSec 5 | Out-Null
    $inventory = Invoke-RestMethod 'http://127.0.0.1:5001/api/remote-access/inventory' -Headers $headers -TimeoutSec 5
    if ($sessionId -in $inventory) { throw 'Cancelled session remains active in Agent inventory.' }
    $processDeadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    do {
        $processes = docker top $containerId -eo pid,args
        if ($LASTEXITCODE -ne 0) { throw 'Could not verify terminal process cleanup.' }
        $shellAlive = @($processes | Where-Object { $_ -match '/bin/bash -i' }).Count -gt 0
        if (-not $shellAlive) { break }
        [System.Threading.Tasks.Task]::Delay(100).GetAwaiter().GetResult() | Out-Null
    } while ([DateTimeOffset]::UtcNow -lt $processDeadline)
    if ($shellAlive) { throw 'Cancelled session left its shell process alive.' }
    Write-Output 'PASS: real Docker PTY, binary UTF-8, resize, Ctrl-C, inventory and cancellation.'
} catch {
    Write-Output ('Terminal fixture output: ' + $received.ToString().Replace([string][char]27, '<ESC>'))
    throw
} finally {
    $socket.Abort()
    $socket.Dispose()
    $deadline.Dispose()
    $authValue = $null
    $headers.Clear()
    if ($containerId -and $containerId -match '^[a-f0-9]{64}$') {
        docker rm -fv $containerId | Out-Null
    }
}
