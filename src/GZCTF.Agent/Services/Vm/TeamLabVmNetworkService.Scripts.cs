using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace GZCTF.Agent.Services.Vm;

public sealed partial class TeamLabVmNetworkService
{
    internal static string BuildWindowsReadScript(IReadOnlyList<GuestInterfaceRequirement> desired)
    {
        var targets = string.Join(',', desired.Select(item => PsQuote(item.MacAddress)));
        return $$"""
            $ErrorActionPreference = 'Stop'
            [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
            $targets = @({{targets}})
            $doc = New-Object System.Xml.XmlDocument
            $root = $doc.CreateElement('network'); [void]$doc.AppendChild($root)
            function Add-Value($parent, $kind, $value) {
              $node = $doc.CreateElement($kind); $node.SetAttribute('ip', [string]$value); [void]$parent.AppendChild($node)
            }
            $configs = @(Get-WmiObject -Class Win32_NetworkAdapterConfiguration | Where-Object {
              $_.MACAddress -and $targets -contains $_.MACAddress.Replace('-',':').ToLowerInvariant()
            })
            $routes = @(Get-WmiObject -Class Win32_IP4RouteTable)
            foreach ($cfg in $configs) {
              $adapter = Get-WmiObject -Class Win32_NetworkAdapter -Filter ('Index=' + $cfg.Index)
              $nic = $doc.CreateElement('interface')
              $nic.SetAttribute('mac', $cfg.MACAddress.Replace('-',':').ToLowerInvariant())
              $nic.SetAttribute('name', [string]$adapter.NetConnectionID)
              $nic.SetAttribute('dhcp', ([bool]$cfg.DHCPEnabled).ToString().ToLowerInvariant())
              [void]$root.AppendChild($nic)
              for ($i=0; $i -lt @($cfg.IPAddress).Count; $i++) {
                if ($cfg.IPAddress[$i] -match '^\d+\.\d+\.\d+\.\d+$') {
                  $node = $doc.CreateElement('address'); $node.SetAttribute('ip',[string]$cfg.IPAddress[$i])
                  $node.SetAttribute('mask',[string]$cfg.IPSubnet[$i]); [void]$nic.AppendChild($node)
                }
              }
              foreach ($dns in @($cfg.DNSServerSearchOrder)) { if ($dns) { Add-Value $nic 'dns' $dns } }
              foreach ($route in @($routes | Where-Object { $_.InterfaceIndex -eq $cfg.InterfaceIndex })) {
                if ($route.Destination -eq '0.0.0.0' -and $route.Mask -eq '0.0.0.0') {
                  Add-Value $nic 'gateway' $route.NextHop
                } else {
                  $prefix = 0
                  foreach ($part in $route.Mask.Split('.')) {
                    $bits = [Convert]::ToString([int]$part,2).PadLeft(8,'0')
                    foreach ($bit in $bits.ToCharArray()) { if ($bit -eq '1') { $prefix++ } }
                  }
                  $node = $doc.CreateElement('route'); $node.SetAttribute('destination',($route.Destination + '/' + $prefix))
                  $node.SetAttribute('nextHop',[string]$route.NextHop); $node.SetAttribute('metric',[string]$route.Metric1)
                  [void]$nic.AppendChild($node)
                }
              }
            }
            [Console]::Out.Write($doc.OuterXml)
            """;
    }

    internal static string BuildWindowsApplyScript(IReadOnlyList<GuestInterfaceRequirement> desired)
    {
        var root = new XElement("network", desired.Select(item => new XElement("interface",
            new XAttribute("mac", item.MacAddress), new XAttribute("name", item.Name ?? ""),
            new XAttribute("ip", item.IpAddress), new XAttribute("mask", MaskFromPrefix(item.PrefixLength)),
            new XAttribute("gateway", item.Gateway ?? ""),
            item.DnsServers.Select(dns => new XElement("dns", new XAttribute("ip", dns))),
            item.Routes.Select(route => new XElement("route", new XAttribute("destination", route.DestinationCidr),
                new XAttribute("nextHop", route.NextHop), new XAttribute("metric", route.Metric ?? 1))))));
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting)));
        return $$"""
            $ErrorActionPreference = 'Stop'
            [xml]$desired = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{payload}}'))
            $stateRoot = Join-Path $env:ProgramData 'GZCTF\TeamLab'
            $statePath = Join-Path $stateRoot 'network.xml'
            $lockPath = Join-Path $stateRoot 'network.lock'
            [void][IO.Directory]::CreateDirectory($stateRoot)
            $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            try {
              $previous = $null
              if ([IO.File]::Exists($statePath)) { [xml]$previous = [IO.File]::ReadAllText($statePath) }
              function Same-Values($left, $right) { return (@($left) -join '|') -eq (@($right) -join '|') }
              function Check-Wmi($result) {
                if ($result.ReturnValue -ne 0) { throw ('Network WMI operation failed, return=' + $result.ReturnValue) }
              }
              function Run-Netsh($operation, $route, $index, $store) {
                $args = @('interface','ipv4',$operation,'route',('prefix=' + $route.destination),('interface=' + $index),('nexthop=' + $route.nextHop),('store=' + $store))
                if ($operation -eq 'add') { $args += ('metric=' + $route.metric) }
                & "$env:SystemRoot\System32\netsh.exe" @args | Out-Null
                if ($LASTEXITCODE -ne 0 -and $operation -eq 'add') { throw 'Static route configuration failed' }
              }
              # Reject any missing/ambiguous MAC or rename collision before changing a device.
              $allConfigs = @(Get-WmiObject -Class Win32_NetworkAdapterConfiguration)
              $allAdapters = @(Get-WmiObject -Class Win32_NetworkAdapter)
              foreach ($item in @($desired.network.interface)) {
                $matches = @($allConfigs | Where-Object { $_.MACAddress -and $_.MACAddress.Replace('-',':').ToLowerInvariant() -eq $item.mac })
                if ($matches.Count -ne 1) { throw 'Declared MAC is absent or ambiguous; check the network driver' }
                if ($item.name -and @($allAdapters | Where-Object { $_.NetConnectionID -eq $item.name -and $_.Index -ne $matches[0].Index }).Count -ne 0) { throw 'Guest interface rename conflicts with another device' }
              }
              foreach ($item in @($desired.network.interface)) {
                $cfg = @($allConfigs | Where-Object { $_.MACAddress -and $_.MACAddress.Replace('-',':').ToLowerInvariant() -eq $item.mac })[0]
                $adapter = @($allAdapters | Where-Object { $_.Index -eq $cfg.Index })[0]
                $ips = @($cfg.IPAddress | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' })
                $masks = @($cfg.IPSubnet | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' })
                $gateways = @(); if ($item.gateway) { $gateways = @([string]$item.gateway) }
                if ($cfg.DHCPEnabled -or !(Same-Values $ips @([string]$item.ip)) -or !(Same-Values $masks @([string]$item.mask)) -or !(Same-Values @($cfg.DefaultIPGateway) $gateways)) {
                  $gateway = 'none'; if ($item.gateway) { $gateway = [string]$item.gateway }
                  # gateway=none also clears a template's stale default route on this MAC.
                  & "$env:SystemRoot\System32\netsh.exe" interface ipv4 set address ('name=' + $cfg.InterfaceIndex) source=static ('address=' + $item.ip) ('mask=' + $item.mask) ('gateway=' + $gateway) gwmetric=1 store=persistent | Out-Null
                  if ($LASTEXITCODE -ne 0) { throw 'Static address/default gateway configuration failed' }
                  $cfg = Get-WmiObject -Class Win32_NetworkAdapterConfiguration -Filter ('Index=' + $cfg.Index)
                }
                $dns = @($item.SelectNodes('dns') | ForEach-Object { [string]$_.ip })
                if (!(Same-Values @($cfg.DNSServerSearchOrder) $dns)) {
                  if ($dns.Count -eq 0) { Check-Wmi ($cfg.SetDNSServerSearchOrder($null)) }
                  else { Check-Wmi ($cfg.SetDNSServerSearchOrder([string[]]$dns)) }
                }
                if ($item.name -and $adapter.NetConnectionID -ne $item.name) {
                  $adapter.NetConnectionID = [string]$item.name; [void]$adapter.Put()
                }
                $old = @(); if ($previous) { $old = @($previous.network.interface | Where-Object { $_.mac -eq $item.mac }) }
                $oldRoutes = @($old | ForEach-Object { $_.SelectNodes('route') })
                foreach ($oldRoute in $oldRoutes) {
                  if (!$oldRoute) { continue }
                  if (@($item.route | Where-Object { $_.destination -eq $oldRoute.destination -and $_.nextHop -eq $oldRoute.nextHop -and $_.metric -eq $oldRoute.metric }).Count -eq 0) {
                    Run-Netsh 'delete' $oldRoute $cfg.InterfaceIndex 'persistent'
                    Run-Netsh 'delete' $oldRoute $cfg.InterfaceIndex 'active'
                  }
                }
                foreach ($route in @($item.route)) {
                  if (!$route) { continue }
                  $parts = $route.destination.Split('/'); $prefix = [int]$parts[1]
                  $mask = @(); for ($n=0; $n -lt 4; $n++) { $count = [Math]::Max(0,[Math]::Min(8,$prefix-$n*8)); $mask += (256-[Math]::Pow(2,8-$count)) }
                  $routeMask = $mask -join '.'
                  $existing = @(Get-WmiObject -Class Win32_IP4RouteTable | Where-Object { $_.InterfaceIndex -eq $cfg.InterfaceIndex -and $_.Destination -eq $parts[0] -and $_.Mask -eq $routeMask -and $_.NextHop -eq $route.nextHop })
                  if ($existing.Count -eq 0) { Run-Netsh 'add' $route $cfg.InterfaceIndex 'persistent' }
                  elseif (@($existing | Where-Object { $_.Metric1 -eq [int]$route.metric }).Count -eq 0) {
                    Run-Netsh 'delete' $route $cfg.InterfaceIndex 'persistent'; Run-Netsh 'delete' $route $cfg.InterfaceIndex 'active'
                    Run-Netsh 'add' $route $cfg.InterfaceIndex 'persistent'
                  }
                }
              }
              # Keep ownership records for undeclared devices; do not modify their routes.
              if ($previous) {
                foreach ($oldItem in @($previous.network.interface)) {
                  if (@($desired.network.interface | Where-Object { $_.mac -eq $oldItem.mac }).Count -eq 0) {
                    [void]$desired.network.AppendChild($desired.ImportNode($oldItem,$true))
                  }
                }
              }
              $temp = $statePath + '.tmp'; $desired.Save($temp)
              Move-Item -LiteralPath $temp -Destination $statePath -Force
            } finally { $lock.Dispose() }
            """;
    }

    static string LinuxPayload(IReadOnlyList<GuestInterfaceRequirement> desired) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(desired, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    internal static string BuildLinuxReadScript(IReadOnlyList<GuestInterfaceRequirement> desired) => $$$"""
        import base64, json, subprocess, ipaddress, shutil, sys, xml.etree.ElementTree as ET
        if any(shutil.which(name) is None for name in ['ip','resolvectl']):
            sys.stderr.write('GZCTF_NETWORK_TOOLS_MISSING'); sys.exit(78)
        targets = json.loads(base64.b64decode('{{{LinuxPayload(desired)}}}'))
        def run(args):
            return subprocess.check_output(args, stderr=subprocess.DEVNULL, timeout=15, text=True).strip()
        addresses = json.loads(run(['ip','-j','-4','address','show']))
        routes = json.loads(run(['ip','-j','-4','route','show','table','main']))
        links = json.loads(run(['ip','-j','link','show']))
        root = ET.Element('network')
        for target in targets:
            for link in links:
                if link.get('address','').lower() != target['macAddress']: continue
                name = link['ifname']
                address = next((a for a in addresses if a['ifname'] == name), {})
                info = [a for a in address.get('addr_info',[]) if a.get('family') == 'inet']
                nic = ET.SubElement(root,'interface', mac=target['macAddress'], name=name,
                    dhcp='true' if any(a.get('dynamic',False) for a in info) else 'false')
                for a in info: ET.SubElement(nic,'address',ip=a['local'],prefix=str(a['prefixlen']))
                dns = run(['resolvectl','dns',name]).partition(':')[2].split()
                for ip in dns:
                    try:
                        if ipaddress.ip_address(ip).version == 4: ET.SubElement(nic,'dns',ip=ip)
                    except ValueError: pass
                for r in routes:
                    if r.get('dev') != name: continue
                    if r.get('dst') == 'default': ET.SubElement(nic,'gateway',ip=r.get('gateway',''))
                    elif r.get('gateway'):
                        ET.SubElement(nic,'route',destination=r['dst'],nextHop=r['gateway'],metric=str(r.get('metric',0)))
        print(ET.tostring(root,encoding='unicode'))
        """;

    internal static string BuildLinuxApplyScript(IReadOnlyList<GuestInterfaceRequirement> desired) => $$$$"""
        import base64, json, os, subprocess, tempfile, fcntl, glob, shutil, sys, hashlib, signal
        try: import yaml
        except ImportError:
            sys.stderr.write('GZCTF_NETWORK_TOOLS_MISSING'); sys.exit(78)
        if any(shutil.which(name) is None for name in ['cloud-init','netplan','ip','resolvectl']):
            sys.stderr.write('GZCTF_NETWORK_TOOLS_MISSING'); sys.exit(78)
        targets = json.loads(base64.b64decode('{{{{LinuxPayload(desired)}}}}'))
        def run(args, limit=25):
            return subprocess.check_output(args,stderr=subprocess.DEVNULL,timeout=limit,text=True)
        def cancelled(signum, frame): raise TimeoutError('Guest network apply cancelled')
        signal.signal(signal.SIGTERM,cancelled)
        with open('/run/lock/gzctf-teamlab-network.lock','w') as lock:
            fcntl.flock(lock,fcntl.LOCK_EX | fcntl.LOCK_NB)
            # First boot must finish its seed processing before a live repair can update netplan.
            run(['cloud-init','status','--wait'],80)
            links = json.loads(run(['ip','-j','link','show']))
            names = set()
            macs = {t['macAddress'] for t in targets}
            for target in targets:
                matches = [l for l in links if l.get('address','').lower() == target['macAddress']]
                if len(matches) != 1: raise RuntimeError('Declared MAC absent or ambiguous; check guest network driver')
                names.add(matches[0]['ifname'])
                if target['name'] and any(l['ifname'] == target['name'] and l.get('address','').lower() != target['macAddress'] for l in links):
                    raise RuntimeError('Guest interface rename conflicts with another device')
                if target['name']: names.add(target['name'])
            def atomic(path, data):
                fd, temp = tempfile.mkstemp(dir=os.path.dirname(path),prefix='.gzctf-network-')
                try:
                    with os.fdopen(fd,'wb') as file:
                        file.write(data); file.flush(); os.fsync(file.fileno())
                    os.chmod(temp,0o600); os.replace(temp,path)
                finally:
                    if os.path.exists(temp): os.unlink(temp)
            changes = {}
            originals = {}
            managed = '/etc/netplan/90-gzctf-teamlab.yaml'
            # Remove conflicting definitions only for the declared current MACs/interfaces.
            # Other NICs, bridges, bonds, users and application configuration are preserved.
            for path in sorted(glob.glob('/etc/netplan/*.yaml') + glob.glob('/etc/netplan/*.yml')):
                with open(path,'rb') as file: original = file.read()
                config = yaml.safe_load(original) or {}
                ethernets = config.get('network',{}).get('ethernets',{})
                remove = [key for key,value in ethernets.items() if
                    str(value.get('match',{}).get('macaddress','')).lower() in macs or
                    key in names and not value.get('match',{}).get('macaddress')]
                if remove:
                    for key in remove: del ethernets[key]
                if remove or path == managed:
                    originals[path] = original
                    changes[path] = config
            config = changes.get(managed,{'network':{'version':2,'ethernets':{}}})
            if managed not in originals: originals[managed] = None
            ethernets = config['network'].setdefault('ethernets',{})
            for target in targets:
                nic = {'match':{'macaddress':target['macAddress']},'dhcp4':False,'dhcp6':False,
                    'addresses':[target['ipAddress']+'/'+str(target['prefixLength'])],
                    'nameservers':{'addresses':target['dnsServers']}}
                if target['name']: nic['set-name'] = target['name']
                routes = []
                if target['gateway']: routes.append({'to':'default','via':target['gateway']})
                for route in target['routes']:
                    entry = {'to':route['destinationCidr'],'via':route['nextHop']}
                    if route['metric'] is not None: entry['metric'] = route['metric']
                    routes.append(entry)
                nic['routes'] = routes
                ethernets['gzctf-'+target['macAddress'].replace(':','')] = nic
            changes[managed] = config
            backup = '/var/lib/gzctf/teamlab/network-backups/' + hashlib.sha256('|'.join(sorted(macs)).encode()).hexdigest()[:20]
            os.makedirs(backup,mode=0o700,exist_ok=True)
            # Preserve the first version for operator rollback, even after retries/plan updates.
            for path, original in originals.items():
                initial = os.path.join(backup,os.path.basename(path)+'.initial')
                absent = os.path.join(backup,os.path.basename(path)+'.absent')
                if not os.path.exists(initial) and not os.path.exists(absent):
                    with open(initial if original is not None else absent,'xb') as file:
                        if original is not None: file.write(original)
                    os.chmod(initial if original is not None else absent,0o600)
            written = []
            try:
                for path, data in changes.items():
                    written.append(path); atomic(path,yaml.safe_dump(data,sort_keys=False).encode())
                run(['netplan','generate'])
                run(['netplan','apply'],30)
            except BaseException:
                # Restore this invocation's starting files, retaining any earlier successful plan.
                try:
                    for path in reversed(written):
                        original = originals[path]
                        if original is None:
                            if os.path.exists(path): os.unlink(path)
                        else: atomic(path,original)
                    run(['netplan','generate']); run(['netplan','apply'],30)
                except Exception: sys.stderr.write('GZCTF_NETWORK_ROLLBACK_FAILED')
                raise
        """;
}
