$ErrorActionPreference = 'Stop'

if ((Get-CimInstance Win32_ComputerSystem).PartOfDomain) {
    exit 0
}

$runtime = Get-Content -LiteralPath 'C:\ProgramData\GZCTF\Runtime\runtime.json' -Raw | ConvertFrom-Json
$domainFqdn = [string]$runtime.parameters.domain_fqdn
$netbiosName = [string]$runtime.parameters.netbios_name
$passwordPath = 'C:\ProgramData\GZCTF\Runtime\secrets\safe_mode_password'
if ([string]::IsNullOrWhiteSpace($domainFqdn) -or [string]::IsNullOrWhiteSpace($netbiosName) -or -not (Test-Path -LiteralPath $passwordPath)) {
    throw 'Domain parameters or the safe-mode password are missing.'
}

Install-WindowsFeature AD-Domain-Services -IncludeManagementTools | Out-Null
Import-Module ADDSDeployment
$password = ConvertTo-SecureString (Get-Content -LiteralPath $passwordPath -Raw) -AsPlainText -Force
Install-ADDSForest -DomainName $domainFqdn -DomainNetbiosName $netbiosName -SafeModeAdministratorPassword $password -InstallDns -NoRebootOnCompletion -Force
exit 3010
