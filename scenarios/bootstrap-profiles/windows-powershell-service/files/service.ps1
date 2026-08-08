$ErrorActionPreference = 'Stop'
$runtimeRoot = 'C:\ProgramData\GZCTF\Runtime'
$config = Get-Content -Raw -LiteralPath (Join-Path $runtimeRoot 'runtime.json') | ConvertFrom-Json
$secrets = Get-Content -Raw -LiteralPath (Join-Path $runtimeRoot 'secrets.json') | ConvertFrom-Json
$port = [int]$config.Parameters.service_port
$listener = [Net.HttpListener]::new()
$listener.Prefixes.Add("http://+:$port/")
$listener.Start()
while ($listener.IsListening) {
    $context = $listener.GetContext()
    $payload = @{
        service = [string]$config.Parameters.service_name
        flag = [string]$secrets.flag
        status = 'ready'
    } | ConvertTo-Json -Compress
    $buffer = [Text.Encoding]::UTF8.GetBytes($payload)
    $context.Response.StatusCode = 200
    $context.Response.ContentType = 'application/json'
    $context.Response.ContentLength64 = $buffer.Length
    $context.Response.OutputStream.Write($buffer, 0, $buffer.Length)
    $context.Response.Close()
}
