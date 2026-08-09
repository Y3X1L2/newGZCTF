$ErrorActionPreference = 'Stop'

Import-Module ActiveDirectory
$null = Get-ADDomain
$services = Get-Service NTDS, DNS
if ($services.Where({ $_.Status -ne 'Running' }).Count -gt 0) {
    exit 1
}
exit 0
