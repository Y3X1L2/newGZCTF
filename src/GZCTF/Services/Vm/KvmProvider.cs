using System.Diagnostics;
using System.Text;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using Microsoft.Extensions.Options;

namespace GZCTF.Services.Vm;

/// <summary>
/// KVM/libvirt implementation of IVirtualMachineProvider.
/// Refactored from VmManager.cs — same logic, interface-backed.
/// </summary>
public class KvmProvider : IVirtualMachineProvider
{
    private readonly ILogger<KvmProvider> _logger;
    private readonly string _libvirtUri;
    private readonly string _imageStoragePath;
    private readonly int _defaultMemoryMb;
    private readonly int _defaultCpu;
    private readonly int _timeoutSeconds;

    public string ProviderName => "KVM";
    public OSType SupportedOSType => OSType.Windows; // KVM on Linux can run Windows guests

    public KvmProvider(IOptions<KvmSettings> settings, ILogger<KvmProvider> logger)
    {
        _logger = logger;
        var cfg = settings.Value;
        _libvirtUri = string.IsNullOrWhiteSpace(cfg.LibvirtUri) ? "qemu:///system" : cfg.LibvirtUri;
        _imageStoragePath = string.IsNullOrWhiteSpace(cfg.ImageStoragePath) ? "/var/lib/gzctf/images" : cfg.ImageStoragePath;
        _defaultMemoryMb = cfg.DefaultVmMemoryMb > 0 ? cfg.DefaultVmMemoryMb : 2048;
        _defaultCpu = cfg.DefaultVmCpu > 0 ? cfg.DefaultVmCpu : 2;
        _timeoutSeconds = cfg.OperationTimeoutSeconds > 0 ? cfg.OperationTimeoutSeconds : 120;
    }

    /// <summary>
    /// Validates a VM name against allowed characters: [a-zA-Z0-9_-], max 64 chars.
    /// </summary>
    public static string SanitizeVmName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new VmOperationException("VM name cannot be empty");
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_\-]{1,64}$"))
            throw new VmOperationException($"Invalid VM name: {name}");
        return name;
    }

    public async Task<VmOperationResult> CreateFromTemplateAsync(string templatePath, string vmName, CancellationToken token)
    {
        _logger.LogInformation("Creating VM '{VmName}' from template '{Template}'", vmName, templatePath);

        if (!Directory.Exists(_imageStoragePath))
            Directory.CreateDirectory(_imageStoragePath);

        var newImagePath = Path.Combine(_imageStoragePath, $"{vmName}.qcow2");

        // Step 1: Clone qcow2 image with backing file
        var cloneResult = await RunCommandAsync("qemu-img",
            $"create -f qcow2 -b \"{templatePath}\" \"{newImagePath}\"");
        if (cloneResult.ExitCode != 0)
        {
            _logger.LogError("qemu-img create failed for '{VmName}': {Error}", vmName, cloneResult.StandardError);
            return VmOperationResult.Fail(vmName, $"Failed to clone VM image: {cloneResult.StandardError}");
        }

        // Step 2: Generate domain XML and define
        var xml = GenerateDomainXml(vmName, newImagePath);
        var xmlPath = Path.Combine(_imageStoragePath, $"{vmName}.xml");
        await File.WriteAllTextAsync(xmlPath, xml, token);
        var defineResult = await RunCommandAsync("virsh",
            $"-c {_libvirtUri} define \"{xmlPath}\"");
        if (defineResult.ExitCode != 0)
        {
            SafeDeleteFile(newImagePath);
            SafeDeleteFile(xmlPath);
            return VmOperationResult.Fail(vmName, $"Failed to define VM: {defineResult.StandardError}");
        }

        return VmOperationResult.Ok(vmName);
    }

    public async Task<VmOperationResult> StartAsync(string vmName, CancellationToken token)
    {
        _logger.LogInformation("Starting VM '{VmName}'", vmName);
        var result = await RunCommandAsync("virsh", $"-c {_libvirtUri} start \"{vmName}\"");
        if (result.ExitCode != 0)
            return VmOperationResult.Fail(vmName, result.StandardError);
        return VmOperationResult.Ok(vmName);
    }

    public async Task<VmOperationResult> ShutdownAsync(string vmName, CancellationToken token)
    {
        _logger.LogInformation("Shutting down VM '{VmName}'", vmName);
        var result = await RunCommandAsync("virsh", $"-c {_libvirtUri} shutdown \"{vmName}\"");
        if (result.ExitCode != 0)
            return VmOperationResult.Fail(vmName, result.StandardError);
        return VmOperationResult.Ok(vmName);
    }

    public async Task<VmOperationResult> DestroyAsync(string vmName, CancellationToken token)
    {
        _logger.LogWarning("Force destroying VM '{VmName}'", vmName);
        var result = await RunCommandAsync("virsh", $"-c {_libvirtUri} destroy \"{vmName}\"");
        if (result.ExitCode != 0)
            return VmOperationResult.Fail(vmName, result.StandardError);
        // Undefine to clean up
        try { await RunCommandAsync("virsh", $"-c {_libvirtUri} undefine \"{vmName}\""); }
        catch { /* best-effort */ }
        return VmOperationResult.Ok(vmName);
    }

    public async Task<VmOperationResult> CreateSnapshotAsync(string vmName, string snapshotName, CancellationToken token)
    {
        _logger.LogInformation("Creating snapshot '{Snapshot}' for VM '{VmName}'", snapshotName, vmName);
        var result = await RunCommandAsync("virsh",
            $"-c {_libvirtUri} snapshot-create-as \"{vmName}\" --name \"{snapshotName}\"");
        if (result.ExitCode != 0)
            return VmOperationResult.Fail(vmName, result.StandardError);
        return VmOperationResult.Ok(vmName);
    }

    public async Task<VmOperationResult> SnapshotRevertAsync(string vmName, CancellationToken token)
    {
        _logger.LogInformation("Reverting snapshot for VM '{VmName}'", vmName);
        var result = await RunCommandAsync("virsh", $"-c {_libvirtUri} snapshot-revert \"{vmName}\" --current");
        if (result.ExitCode != 0)
            return VmOperationResult.Fail(vmName, result.StandardError);
        return VmOperationResult.Ok(vmName);
    }

    public async Task<VmConnectionInfo?> GetConnectionInfoAsync(string vmName, CancellationToken token)
    {
        var ip = await GetIpAddressAsync(vmName, token);
        var vncPort = await GetVncPortAsync(vmName);

        return new VmConnectionInfo
        {
            IP = ip,
            VncPort = vncPort,
            Protocol = "vnc"
        };
    }

    public async Task<string?> GetIpAddressAsync(string vmName, CancellationToken token)
    {
        // Try guest agent first
        var result = await RunCommandAsync("virsh",
            $"-c {_libvirtUri} domifaddr \"{vmName}\" --source agent");
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            result = await RunCommandAsync("virsh",
                $"-c {_libvirtUri} domifaddr \"{vmName}\"");
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            return null;
        return ParseFirstNonLoopbackIp(result.StandardOutput);
    }

    public async Task<bool> IsRunningAsync(string vmName, CancellationToken token)
    {
        var result = await RunCommandAsync("virsh", $"-c {_libvirtUri} domstate \"{vmName}\"");
        return result.ExitCode == 0 && result.StandardOutput?.Trim() == "running";
    }

    private async Task<int?> GetVncPortAsync(string vmName)
    {
        var result = await RunCommandAsync("virsh", $"-c {_libvirtUri} vncdisplay \"{vmName}\"");
        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            var display = result.StandardOutput.Trim();
            var colonIdx = display.LastIndexOf(':');
            if (colonIdx >= 0 && int.TryParse(display[(colonIdx + 1)..], out var displayNum))
                return 5900 + displayNum;
        }
        return null;
    }

    private string GenerateDomainXml(string vmName, string diskImagePath)
    {
        var memoryKib = _defaultMemoryMb * 1024;
        var escapedName = System.Security.SecurityElement.Escape(vmName);
        var escapedImage = System.Security.SecurityElement.Escape(diskImagePath);
        return $"""
                <domain type='kvm'>
                  <name>{escapedName}</name>
                  <memory unit='KiB'>{memoryKib}</memory>
                  <currentMemory unit='KiB'>{memoryKib}</currentMemory>
                  <vcpu placement='static'>{_defaultCpu}</vcpu>
                  <os>
                    <type arch='x86_64' machine='pc-q35-7.2'>hvm</type>
                    <boot dev='hd'/>
                  </os>
                  <features>
                    <acpi/><apic/>
                    <hyperv mode='custom'>
                      <relaxed state='on'/><vapic state='on'/>
                      <spinlocks state='on' retries='8191'/>
                    </hyperv>
                  </features>
                  <cpu mode='host-passthrough' check='none'/>
                  <clock offset='localtime'/>
                  <on_poweroff>destroy</on_poweroff>
                  <on_reboot>restart</on_reboot>
                  <on_crash>destroy</on_crash>
                  <devices>
                    <disk type='file' device='disk'>
                      <driver name='qemu' type='qcow2'/>
                      <source file='{escapedImage}'/>
                      <target dev='vda' bus='virtio'/>
                    </disk>
                    <graphics type='vnc' port='-1' autoport='yes' listen='0.0.0.0'>
                      <listen type='address' address='0.0.0.0'/>
                    </graphics>
                    <video>
                      <model type='qxl' ram='65536' vram='65536' vgamem='16384' heads='1'/>
                    </video>
                    <interface type='network'>
                      <source network='default'/>
                      <model type='virtio'/>
                    </interface>
                    <channel type='unix'>
                      <target type='virtio' name='org.qemu.guest_agent.0'/>
                    </channel>
                  </devices>
                </domain>
                """;
    }

    private async Task<CommandResult> RunCommandAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName, Arguments = arguments,
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) error.AppendLine(e.Data); };
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
            await process.WaitForExitAsync(cts.Token);
            return new CommandResult(process.ExitCode, output.ToString().Trim(), error.ToString().Trim());
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            return new CommandResult(-1, "", $"Timeout after {_timeoutSeconds}s");
        }
    }

    private static string? ParseFirstNonLoopbackIp(string output)
    {
        foreach (var line in output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part.Contains('/') && !part.StartsWith("127.") && !part.StartsWith("::1") &&
                    !part.StartsWith("fe80:"))
                {
                    var ip = part.Split('/')[0];
                    if (System.Net.IPAddress.TryParse(ip, out var addr) &&
                        addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip;
                }
            }
        }
        return null;
    }

    private void SafeDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up '{Path}'", path); }
    }
}

internal sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
