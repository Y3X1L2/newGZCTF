$ErrorActionPreference = 'Stop'

$runtimePath = 'C:\ProgramData\GZCTF\Runtime\runtime.json'
$flagPath = 'C:\ProgramData\GZCTF\Runtime\secrets\flag'
if (-not (Test-Path -LiteralPath $runtimePath) -or -not (Test-Path -LiteralPath $flagPath)) {
    throw 'GZCTF runtime parameters or flag secret are missing.'
}

$parameters = (Get-Content -LiteralPath $runtimePath -Raw | ConvertFrom-Json).parameters
$serviceName = [string]$parameters.service_name
$servicePort = [int]$parameters.service_port
if ([string]::IsNullOrWhiteSpace($serviceName) -or $servicePort -lt 1 -or $servicePort -gt 65535) {
    throw 'The service_name or service_port parameter is invalid.'
}

$root = 'C:\ProgramData\GZCTF\Runtime\service'
$scriptPath = Join-Path $root 'host.ps1'
New-Item -ItemType Directory -Path $root -Force | Out-Null
@'
$ErrorActionPreference = 'Stop'
$runtime = Get-Content -LiteralPath 'C:\ProgramData\GZCTF\Runtime\runtime.json' -Raw | ConvertFrom-Json
$name = [string]$runtime.parameters.service_name
$port = [int]$runtime.parameters.service_port
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://+:$port/")
$listener.Start()
try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        $body = [Text.Encoding]::UTF8.GetBytes((@{ service = $name; status = 'ready' } | ConvertTo-Json -Compress))
        $context.Response.ContentType = 'application/json'
        $context.Response.ContentLength64 = $body.Length
        $context.Response.OutputStream.Write($body, 0, $body.Length)
        $context.Response.Close()
    }
}
finally {
    $listener.Close()
}
'@ | Set-Content -LiteralPath $scriptPath -Encoding UTF8

$taskName = "GZCTF-Runtime-$($serviceName -replace '[^A-Za-z0-9_.-]', '-')"
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$scriptPath`""
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
