using System.Diagnostics;

namespace GZCTF.GuestSupervisor.Lifecycle;

public sealed class GuestRemoteAccessProvisioner
{
    public async Task ApplyAsync(IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken)
    {
        if (!secrets.TryGetValue("GZCTF_REMOTE_ACCESS_PROTOCOL", out var protocol) ||
            !secrets.TryGetValue("GZCTF_REMOTE_ACCESS_USERNAME", out var username) ||
            !secrets.TryGetValue("GZCTF_REMOTE_ACCESS_PASSWORD", out var password))
            return;
        if (username.Length is < 1 or > 64 || password.Length is < 12 or > 256)
            throw new InvalidDataException("guest_remote_access_credential_invalid");
        if (OperatingSystem.IsWindows())
        {
            if (!string.Equals(protocol, "Rdp", StringComparison.Ordinal))
                throw new InvalidDataException("guest_remote_access_protocol_invalid");
            await RunWindowsAsync(username, password, cancellationToken);
            return;
        }
        if (!string.Equals(protocol, "Ssh", StringComparison.Ordinal))
            throw new InvalidDataException("guest_remote_access_protocol_invalid");
        await RunLinuxAsync(username, password, cancellationToken);
    }

    private static async Task RunLinuxAsync(string username, string password, CancellationToken token)
    {
        await RunAsync("/usr/sbin/useradd", ["--create-home", "--shell", "/bin/bash", username], token,
            allowAlreadyExists: true);
        await RunAsync("/usr/sbin/chpasswd", [], token, input: username + ":" + password + "\n");
    }

    private static Task RunWindowsAsync(string username, string password, CancellationToken token) =>
        RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command",
            "$u=$env:GZCTF_REMOTE_USER;$p=ConvertTo-SecureString $env:GZCTF_REMOTE_PASSWORD -AsPlainText -Force;" +
            "if(Get-LocalUser -Name $u -ErrorAction SilentlyContinue){Set-LocalUser -Name $u -Password $p}else{New-LocalUser -Name $u -Password $p -PasswordNeverExpires};" +
            "$rdpGroup=Get-LocalGroup -SID 'S-1-5-32-555';Add-LocalGroupMember -SID $rdpGroup.SID -Member $u -ErrorAction Stop;" +
            "Set-ItemProperty 'HKLM:\\System\\CurrentControlSet\\Control\\Terminal Server' fDenyTSConnections 0;" +
            "Get-NetFirewallRule -Service TermService -ErrorAction SilentlyContinue|Enable-NetFirewallRule"], token,
            environment: new Dictionary<string, string> { ["GZCTF_REMOTE_USER"] = username, ["GZCTF_REMOTE_PASSWORD"] = password });

    private static async Task RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken token,
        string? input = null, bool allowAlreadyExists = false, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (input is not null) start.RedirectStandardInput = true;
        if (environment is not null) foreach (var item in environment) start.Environment[item.Key] = item.Value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("guest_remote_access_process_start_failed");
        if (input is not null) { await process.StandardInput.WriteAsync(input.AsMemory(), token); process.StandardInput.Close(); }
        await process.WaitForExitAsync(token);
        var error = await process.StandardError.ReadToEndAsync(token);
        if (process.ExitCode != 0 && !(allowAlreadyExists && error.Contains("already exists", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("guest_remote_access_provision_failed");
    }
}
