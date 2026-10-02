param(
 [Parameter(Mandatory=$true)][string]$ApplyScript,
 [Parameter(Mandatory=$true)][string]$ReadScript,
 [Parameter(Mandatory=$true)][string]$StateRoot,
 [ValidateSet(0,1)][int]$WmiSuccessCode=1
)
$ErrorActionPreference='Stop'
# Generated fixture: MAC ...d6:14, field0, 10.96.1.20/24, no DNS/gateway,
# route 172.16.0.0/16 via 10.96.1.1 metric 25. WMI/netsh are always simulated.
$script:NetshCalls = New-Object 'System.Collections.Generic.List[string]'
$script:Cfg = New-Object PSObject -Property @{MACAddress='02:42:29:19:d6:14';Index=7;InterfaceIndex=19;IPConnectionMetric=5;DHCPEnabled=$true;IPAddress=@('10.96.1.99');IPSubnet=@('255.255.255.0');DefaultIPGateway=@('10.96.1.1');DNSServerSearchOrder=@('10.96.1.53')}
$script:Adapter = New-Object PSObject -Property @{Index=7;NetConnectionID='ens3'}
$script:Adapter | Add-Member ScriptMethod Put { return $null }
$script:WmiSuccessCode=$WmiSuccessCode
$script:Cfg | Add-Member ScriptMethod SetDNSServerSearchOrder { param($dns) $script:Cfg.DNSServerSearchOrder=$dns; return (New-Object PSObject -Property @{ReturnValue=$script:WmiSuccessCode}) }
$script:Routes = @()
function Get-WmiObject {
 param($Class,$Filter)
 switch ($Class) {
  'Win32_NetworkAdapterConfiguration' { return $script:Cfg }
  'Win32_NetworkAdapter' { return $script:Adapter }
  'Win32_IP4RouteTable' { return $script:Routes }
  default { throw ('Unexpected WMI class '+$Class) }
 }
}
function Invoke-Netsh-Test {
 $parts = @($args)
 [void]$script:NetshCalls.Add(($parts -join ' '))
 if ($parts[2] -eq 'set' -and $parts[3] -eq 'address') {
  if ($parts -notcontains 'name=19' -or $parts -notcontains 'gateway=none') { throw 'Wrong target NIC or empty gateway handling' }
  $script:Cfg.DHCPEnabled=$false; $script:Cfg.IPAddress=@('10.96.1.20'); $script:Cfg.IPSubnet=@('255.255.255.0'); $script:Cfg.DefaultIPGateway=@()
 } elseif ($parts[2] -eq 'add' -and $parts[3] -eq 'route') {
  if ($parts -notcontains 'interface=19' -or $parts -notcontains 'metric=25') { throw 'Wrong route target or metric' }
  $script:Routes=@(New-Object PSObject -Property @{InterfaceIndex=19;Destination='172.16.0.0';Mask='255.255.0.0';NextHop='10.96.1.1';Metric1=30})
 } elseif ($parts[2] -eq 'show' -and $parts[3] -eq 'route') {
  foreach ($route in $script:Routes) { 'No Manual 25 172.16.0.0/16 19 10.96.1.1' }
 } else { throw ('Unexpected netsh command: '+($parts -join ' ')) }
 $global:LASTEXITCODE=0
}
$quotedRoot = "'" + $StateRoot.Replace("'", "''") + "'"
function Read-IsolatedSource($path) {
 $source = [IO.File]::ReadAllText($path)
 $source = $source.Replace("`$stateRoot = Join-Path `$env:ProgramData 'GZCTF\TeamLab'", '$stateRoot = ' + $quotedRoot)
 $source = $source.Replace('& "$env:SystemRoot\System32\netsh.exe"', 'Invoke-Netsh-Test')
 if ($source.Contains('netsh.exe') -or $source.Contains('ProgramData') -or !$source.Contains('$stateRoot = ' + $quotedRoot)) {
  throw 'Mock substitutions did not match the generated script; refusing to run any real guest operations'
 }
 return $source.Replace('[Console]::Out.Write($doc.OuterXml)','Write-Output $doc.OuterXml')
}
$source = Read-IsolatedSource $ApplyScript
$readSource = Read-IsolatedSource $ReadScript
& ([scriptblock]::Create($source))
if ($script:Cfg.DNSServerSearchOrder -and @($script:Cfg.DNSServerSearchOrder).Count -gt 0) { throw 'Explicit empty DNS was not cleared' }
if ($script:Adapter.NetConnectionID -ne 'field0') { throw 'Target name was not applied' }
[xml]$snapshot = & ([scriptblock]::Create($readSource))
if ($snapshot.network.interface.address.ip -ne '10.96.1.20' -or $snapshot.network.interface.address.mask -ne '255.255.255.0') { throw 'Actual readback incorrect' }
if ($snapshot.network.interface.gateway -or $snapshot.network.interface.dns) { throw 'Unexpected gateway/DNS in actual readback' }
if ($snapshot.network.interface.route.destination -ne '172.16.0.0/16') { throw 'Route live readback incorrect' }
if ($snapshot.network.interface.route.metric -ne '25') { throw 'WMI combined metric was not normalized to route metric' }
$firstCalls=$script:NetshCalls.Count
& ([scriptblock]::Create($source))
if ($script:NetshCalls.Count -ne $firstCalls) { throw 'Correct interfaces/routes were configured again' }
$script:Cfg.DNSServerSearchOrder=@('fec0:0:0:ffff::1','2001:db8::53')
[xml]$ipv6Only = & ([scriptblock]::Create($readSource))
if ($ipv6Only.network.interface.SelectNodes('dns').Count -ne 0) { throw 'IPv6-only DNS must match an empty IPv4 DNS requirement' }
$script:Cfg.DNSServerSearchOrder=@('fec0:0:0:ffff::1','10.96.1.53','2001:db8::53')
[xml]$mixedDns = & ([scriptblock]::Create($readSource))
if ($mixedDns.network.interface.SelectNodes('dns').Count -ne 1 -or $mixedDns.network.interface.dns.ip -ne '10.96.1.53') { throw 'Mixed DNS readback must contain only its IPv4 resolver' }
Write-Output ('Isolated WMI/netsh mock: MAC selection, static IPv4, no gateway, empty/mixed IPv4 DNS with IPv6 present, rename, route, live XML readback, idempotence, WMI success code ' + $WmiSuccessCode + ' passed.')
