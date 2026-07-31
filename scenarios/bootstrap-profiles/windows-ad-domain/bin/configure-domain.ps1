$ErrorActionPreference = 'Stop'

$runtime = Get-Content -LiteralPath 'C:\ProgramData\GZCTF\Runtime\runtime.json' -Raw | ConvertFrom-Json
$expectedDomain = [string]$runtime.parameters.domain_fqdn
$domain = Get-ADDomain
if (-not [string]::Equals($domain.DNSRoot, $expectedDomain, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The active domain '$($domain.DNSRoot)' does not match '$expectedDomain'."
}

Get-Service NTDS, DNS | Where-Object Status -ne 'Running' | Start-Service
