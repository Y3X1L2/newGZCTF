using System.Diagnostics;
using System.Text.RegularExpressions;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services;

public class KvmService
{
    private readonly KvmConfig _config;
    private readonly ILogger<KvmService> _logger;

    private static readonly Regex SafeNamePattern = new(@"^[a-zA-Z0-9_\-]+$", RegexOptions.Compiled);

    public KvmService(IOptions<KvmConfig> config, ILogger<KvmService> logger)
    { _config = config.Value; _logger = logger; }

    public async Task<CreateVmResponse?> CreateVmAsync(CreateVmRequest request, CancellationToken token)
    {
        if (!SafeNamePattern.IsMatch(request.VmName))
            throw new ArgumentException("Invalid VM name", nameof(request.VmName));

        var templatePath = request.TemplateId.HasValue
            ? Path.Combine(_config.ImageStoragePath, $"{request.TemplateId}.qcow2")
            : "";
        var vmPath = Path.Combine(_config.ImageStoragePath, $"{request.VmName}.qcow2");

        if (!string.IsNullOrEmpty(templatePath) && File.Exists(templatePath))
            await RunCommandAsync($"qemu-img create -f qcow2 -b {ShellEscape(templatePath)} -F qcow2 {ShellEscape(vmPath)}", token);
        else
            await RunCommandAsync($"qemu-img create -f qcow2 {ShellEscape(vmPath)} 20G", token);

        await RunCommandAsync(
            $"virt-install --name {ShellEscape(request.VmName)} --memory {request.Memory} --vcpus {request.Cpu} " +
            $"--disk path={ShellEscape(vmPath)} --os-variant detect=on --import --noautoconsole --network default", token);

        return new CreateVmResponse
        {
            VmName = request.VmName,
            Status = "Running",
            VncAddress = await GetVncAddressAsync(request.VmName, token)
        };
    }

    public async Task DestroyVmAsync(string vmName, CancellationToken token)
    {
        if (!SafeNamePattern.IsMatch(vmName)) return;
        await RunCommandAsync($"virsh destroy {ShellEscape(vmName)} 2>/dev/null || true", token);
        await RunCommandAsync($"virsh undefine {ShellEscape(vmName)} --remove-all-storage 2>/dev/null || true", token);
    }

    public async Task<int> GetVmCountAsync(CancellationToken token)
    {
        var result = await RunCommandAsync("virsh list --name 2>/dev/null", token);
        return result.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private async Task<string> GetVncAddressAsync(string vmName, CancellationToken token)
    {
        var result = await RunCommandAsync($"virsh vncdisplay {ShellEscape(vmName)} 2>/dev/null", token);
        return result.Trim();
    }

    private static string ShellEscape(string arg) => $"'{arg.Replace("'", "'\\''")}'";

    private async Task<string> RunCommandAsync(string cmd, CancellationToken token)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{cmd}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process is null) return "";
        await process.WaitForExitAsync(token);
        return await process.StandardOutput.ReadToEndAsync(token);
    }
}
