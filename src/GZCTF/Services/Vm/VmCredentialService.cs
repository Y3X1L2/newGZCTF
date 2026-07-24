using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace GZCTF.Services.Vm;

public sealed class VmCredentialService(IDataProtectionProvider provider)
{
    private const string Purpose = "GZCTF.WindowsVm.RdpCredential.v1";
    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public void Initialize(VmInstance instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.RdpPasswordProtected))
            return;

        instance.RdpUsername = "player";
        instance.RdpPasswordProtected = _protector.Protect(GeneratePassword());
    }

    public string RevealPassword(VmInstance instance)
    {
        if (string.IsNullOrWhiteSpace(instance.RdpPasswordProtected))
            throw new InvalidOperationException($"VM {instance.Id} has no protected RDP credential.");

        return _protector.Unprotect(instance.RdpPasswordProtected);
    }

    internal static string BuildWindowsUserData(string username, string password)
    {
        var safeUsername = PowerShellSingleQuote(username);
        var safePassword = PowerShellSingleQuote(password);
        return $$"""
                 #ps1_sysnative
                 $ErrorActionPreference = 'Stop'
                 $Username = '{{safeUsername}}'
                 $Password = ConvertTo-SecureString '{{safePassword}}' -AsPlainText -Force
                 $ExistingUser = Get-LocalUser -Name $Username -ErrorAction SilentlyContinue
                 if ($null -eq $ExistingUser) {
                   New-LocalUser -Name $Username -Password $Password -PasswordNeverExpires -AccountNeverExpires | Out-Null
                 } else {
                   Set-LocalUser -Name $Username -Password $Password -PasswordNeverExpires $true
                 }
                 $RdpGroup = Get-LocalGroup -SID 'S-1-5-32-555'
                 if (-not (Get-LocalGroupMember -Group $RdpGroup -ErrorAction SilentlyContinue | Where-Object Name -Match "\\$Username$")) {
                   Add-LocalGroupMember -Group $RdpGroup -Member $Username
                 }
                 Set-ItemProperty -Path 'HKLM:\System\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections -Value 0
                 $RdpRules = Get-NetFirewallRule -Name 'RemoteDesktop-UserMode-In-*' -ErrorAction SilentlyContinue
                 if ($RdpRules) {
                   $RdpRules | Enable-NetFirewallRule
                 } elseif (-not (Get-NetFirewallRule -Name 'GZCTF-RDP-In-TCP' -ErrorAction SilentlyContinue)) {
                   New-NetFirewallRule -Name 'GZCTF-RDP-In-TCP' -DisplayName 'GZCTF RDP' -Direction Inbound -Protocol TCP -LocalPort 3389 -Action Allow | Out-Null
                 }
                 """;
    }

    private static string GeneratePassword()
    {
        Span<byte> random = stackalloc byte[24];
        RandomNumberGenerator.Fill(random);
        return $"Aa1!{Convert.ToHexStringLower(random)}";
    }

    private static string PowerShellSingleQuote(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
