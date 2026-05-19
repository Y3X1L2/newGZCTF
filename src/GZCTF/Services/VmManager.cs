using System.Diagnostics;
using System.Text;
using GZCTF.Models.Internal;
using Microsoft.Extensions.Options;

namespace GZCTF.Services;

/// <summary>
/// [DEPRECATED] Use KvmProvider (IVirtualMachineProvider) instead.
/// This class is retained for backward compatibility only.
/// All new code should inject IVirtualMachineProvider.
/// </summary>
[Obsolete("Use KvmProvider via IVirtualMachineProvider")]
public class VmManager
{
    private readonly ILogger<VmManager> _logger;
    private readonly string _libvirtUri;
    private readonly string _imageStoragePath;
    private readonly int _defaultMemoryMb;
    private readonly int _defaultCpu;
    private readonly int _timeoutSeconds;

    /// <summary>
    /// Initializes a new instance of <see cref="VmManager"/> with KVM configuration and structured logging.
    /// </summary>
    /// <param name="settings">KVM configuration options from the "KvmSettings" configuration section.</param>
    /// <param name="logger">Structured logger for operation auditing.</param>
    public VmManager(IOptions<KvmSettings> settings, ILogger<VmManager> logger)
    {
        _logger = logger;
        var cfg = settings.Value;
        _libvirtUri = string.IsNullOrWhiteSpace(cfg.LibvirtUri) ? "qemu:///system" : cfg.LibvirtUri;
        _imageStoragePath = string.IsNullOrWhiteSpace(cfg.ImageStoragePath)
            ? "/var/lib/gzctf/images"
            : cfg.ImageStoragePath;
        _defaultMemoryMb = cfg.DefaultVmMemoryMb > 0 ? cfg.DefaultVmMemoryMb : 2048;
        _defaultCpu = cfg.DefaultVmCpu > 0 ? cfg.DefaultVmCpu : 2;
        _timeoutSeconds = cfg.OperationTimeoutSeconds > 0 ? cfg.OperationTimeoutSeconds : 120;
    }

    /// <summary>
    /// Creates a new VM by cloning a template qcow2 image (copy-on-write)
    /// and defining the resulting domain in libvirt.
    /// </summary>
    /// <param name="templateImagePath">Full path to the backing qcow2 template image.</param>
    /// <param name="newVmName">Name for the new VM, used as both the disk image filename and libvirt domain name.</param>
    /// <returns>The new VM name on success.</returns>
    /// <exception cref="VmOperationException">Thrown when qemu-img cloning or virsh define fails.</exception>
    public async Task<string> CreateFromTemplate(string templateImagePath, string newVmName)
    {
        _logger.LogInformation("Creating VM '{VmName}' from template '{Template}'", newVmName, templateImagePath);

        if (!Directory.Exists(_imageStoragePath))
            Directory.CreateDirectory(_imageStoragePath);

        var newImagePath = Path.Combine(_imageStoragePath, $"{newVmName}.qcow2");

        // Step 1: Clone qcow2 image with backing file (copy-on-write)
        var cloneArgs = $"create -f qcow2 -b \"{templateImagePath}\" \"{newImagePath}\"";
        var cloneResult = await RunCommandAsync("qemu-img", cloneArgs);

        if (cloneResult.ExitCode != 0)
        {
            _logger.LogError("qemu-img create failed for '{VmName}': {Error}", newVmName, cloneResult.StandardError);
            throw new VmOperationException($"Failed to clone VM image: {cloneResult.StandardError}");
        }

        _logger.LogDebug("Cloned disk image for '{VmName}' at '{ImagePath}'", newVmName, newImagePath);

        // Step 2: Generate domain XML and define in libvirt
        var xml = GenerateDomainXml(newVmName, newImagePath);
        var xmlPath = Path.Combine(_imageStoragePath, $"{newVmName}.xml");
        await File.WriteAllTextAsync(xmlPath, xml);

        var defineArgs = $"-c {_libvirtUri} define \"{xmlPath}\"";
        var defineResult = await RunCommandAsync("virsh", defineArgs);

        if (defineResult.ExitCode != 0)
        {
            _logger.LogError("virsh define failed for '{VmName}': {Error}", newVmName, defineResult.StandardError);
            // Clean up the disk image on failure
            SafeDeleteFile(newImagePath);
            SafeDeleteFile(xmlPath);
            throw new VmOperationException($"Failed to define VM: {defineResult.StandardError}");
        }

        _logger.LogInformation("VM '{VmName}' created successfully from template", newVmName);
        return newVmName;
    }

    /// <summary>
    /// Starts a previously defined VM.
    /// </summary>
    /// <param name="vmName">Name of the VM (libvirt domain name).</param>
    /// <exception cref="VmOperationException">Thrown when virsh start fails.</exception>
    public async Task Start(string vmName)
    {
        _logger.LogInformation("Starting VM '{VmName}'", vmName);
        var result = await RunCommandAsync("virsh", $"-c {_libvirtUri} start \"{vmName}\"");

        if (result.ExitCode != 0)
        {
            _logger.LogError("virsh start failed for '{VmName}': {Error}", vmName, result.StandardError);
            throw new VmOperationException($"Failed to start VM '{vmName}': {result.StandardError}");
        }

        _logger.LogInformation("VM '{VmName}' started successfully", vmName);
    }

    /// <summary>
    /// Gracefully shuts down a running VM via ACPI signal.
    /// </summary>
    /// <param name="vmName">Name of the VM (libvirt domain name).</param>
    /// <exception cref="VmOperationException">Thrown when virsh shutdown fails.</exception>
    public async Task Shutdown(string vmName)
    {
        _logger.LogInformation("Shutting down VM '{VmName}'", vmName);
        var result = await RunCommandAsync("virsh", $"-c {_libvirtUri} shutdown \"{vmName}\"");

        if (result.ExitCode != 0)
        {
            _logger.LogError("virsh shutdown failed for '{VmName}': {Error}", vmName, result.StandardError);
            throw new VmOperationException($"Failed to shutdown VM '{vmName}': {result.StandardError}");
        }

        _logger.LogInformation("VM '{VmName}' shutdown signal sent", vmName);
    }

    /// <summary>
    /// Force-stops a running VM immediately (equivalent to pulling the power cord).
    /// </summary>
    /// <param name="vmName">Name of the VM (libvirt domain name).</param>
    /// <exception cref="VmOperationException">Thrown when virsh destroy fails.</exception>
    public async Task Destroy(string vmName)
    {
        _logger.LogWarning("Force destroying VM '{VmName}'", vmName);
        var result = await RunCommandAsync("virsh", $"-c {_libvirtUri} destroy \"{vmName}\"");

        if (result.ExitCode != 0)
        {
            _logger.LogError("virsh destroy failed for '{VmName}': {Error}", vmName, result.StandardError);
            throw new VmOperationException($"Failed to destroy VM '{vmName}': {result.StandardError}");
        }

        _logger.LogInformation("VM '{VmName}' destroyed", vmName);
    }

    /// <summary>
    /// Reverts a VM to its current (latest) snapshot, restoring disk and VM state.
    /// </summary>
    /// <param name="vmName">Name of the VM (libvirt domain name).</param>
    /// <exception cref="VmOperationException">Thrown when virsh snapshot-revert fails.</exception>
    public async Task SnapshotRevert(string vmName)
    {
        _logger.LogInformation("Reverting snapshot for VM '{VmName}'", vmName);
        var result = await RunCommandAsync("virsh",
            $"-c {_libvirtUri} snapshot-revert \"{vmName}\" --current");

        if (result.ExitCode != 0)
        {
            _logger.LogError("virsh snapshot-revert failed for '{VmName}': {Error}", vmName, result.StandardError);
            throw new VmOperationException($"Failed to revert snapshot for VM '{vmName}': {result.StandardError}");
        }

        _logger.LogInformation("VM '{VmName}' snapshot reverted successfully", vmName);
    }

    /// <summary>
    /// Gets the VNC display port for a VM by parsing its domain XML.
    /// </summary>
    /// <param name="vmName">Name of the VM (libvirt domain name).</param>
    /// <returns>The VNC port number, or <c>null</c> if not available.</returns>
    public async Task<int?> GetVncPort(string vmName)
    {
        // Attempt virsh vncdisplay first (fast path)
        var vncResult = await RunCommandAsync("virsh", $"-c {_libvirtUri} vncdisplay \"{vmName}\"");

        if (vncResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(vncResult.StandardOutput))
        {
            var vncDisplay = vncResult.StandardOutput.Trim();
            // vncdisplay returns something like "127.0.0.1:0" or ":0"
            var colonIdx = vncDisplay.LastIndexOf(':');
            if (colonIdx >= 0 && int.TryParse(vncDisplay[(colonIdx + 1)..], out var displayNum))
                return 5900 + displayNum;

            if (int.TryParse(vncDisplay, out var directPort))
                return directPort;
        }

        // Fallback: parse domain XML for graphics element
        var xmlResult = await RunCommandAsync("virsh", $"-c {_libvirtUri} dumpxml \"{vmName}\"");

        if (xmlResult.ExitCode != 0 || string.IsNullOrWhiteSpace(xmlResult.StandardOutput))
        {
            _logger.LogWarning("Could not determine VNC port for VM '{VmName}'", vmName);
            return null;
        }

        return ParseVncPortFromXml(xmlResult.StandardOutput);
    }

    /// <summary>
    /// Gets the IP address of a running VM by querying the guest agent or ARP table.
    /// </summary>
    /// <param name="vmName">Name of the VM (libvirt domain name).</param>
    /// <returns>The first non-loopback IPv4 address, or <c>null</c> if not available.</returns>
    public async Task<string?> GetIpAddress(string vmName)
    {
        // Try guest agent first (more reliable)
        var result = await RunCommandAsync("virsh",
            $"-c {_libvirtUri} domifaddr \"{vmName}\" --source agent");

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            // Fallback to ARP table if guest agent is unavailable
            result = await RunCommandAsync("virsh",
                $"-c {_libvirtUri} domifaddr \"{vmName}\"");
        }

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            _logger.LogWarning("Could not determine IP address for VM '{VmName}'", vmName);
            return null;
        }

        return ParseFirstNonLoopbackIp(result.StandardOutput);
    }

    /// <summary>
    /// Runs a command-line executable asynchronously with the configured timeout.
    /// Captures both standard output and standard error via async stream reading.
    /// </summary>
    /// <param name="fileName">The executable to run (e.g., "virsh", "qemu-img").</param>
    /// <param name="arguments">Arguments to pass to the executable.</param>
    /// <returns>A <see cref="CommandResult"/> containing the exit code and output streams.</returns>
    /// <exception cref="VmOperationException">Thrown when the command times out.</exception>
    private async Task<CommandResult> RunCommandAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) error.AppendLine(e.Data);
        };

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
            _logger.LogError("Command '{Command} {Args}' timed out after {Timeout}s",
                fileName, arguments, _timeoutSeconds);

            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            throw new VmOperationException(
                $"Command '{fileName} {arguments}' timed out after {_timeoutSeconds}s");
        }
    }

    /// <summary>
    /// Generates a libvirt domain XML configuration for a Windows VM with KVM acceleration and Hyper-V enlightenments.
    /// </summary>
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
                    <acpi/>
                    <apic/>
                    <hyperv mode='custom'>
                      <relaxed state='on'/>
                      <vapic state='on'/>
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

    /// <summary>
    /// Parses the first non-loopback IPv4 address from virsh domifaddr output.
    /// </summary>
    private static string? ParseFirstNonLoopbackIp(string output)
    {
        foreach (var line in output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part.Contains('/') &&
                    !part.StartsWith("127.") &&
                    !part.StartsWith("::1") &&
                    !part.StartsWith("fe80:"))
                {
                    var ip = part.Split('/')[0];
                    if (System.Net.IPAddress.TryParse(ip, out var addr) &&
                        addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return ip;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Parses the VNC port from a libvirt domain XML string.
    /// </summary>
    private static int? ParseVncPortFromXml(string xml)
    {
        var portStart = xml.IndexOf("type='vnc'", StringComparison.Ordinal);
        if (portStart < 0)
            return null;

        var portAttrStart = xml.IndexOf("port='", portStart, StringComparison.Ordinal);
        if (portAttrStart < 0)
            return null;

        portAttrStart += 6;
        var portAttrEnd = xml.IndexOf('\'', portAttrStart);
        if (portAttrEnd < 0)
            return null;

        var portStr = xml[portAttrStart..portAttrEnd];
        if (int.TryParse(portStr, out var port))
            return port;

        return null;
    }

    /// <summary>
    /// Attempts to delete a file without throwing on failure (best-effort cleanup).
    /// </summary>
    private void SafeDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up file '{Path}'", path);
        }
    }
}

/// <summary>
/// Represents the result of executing a command-line process.
/// </summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">Captured standard output.</param>
/// <param name="StandardError">Captured standard error.</param>
internal sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Exception thrown when a KVM/libvirt VM operation fails.
/// Wraps underlying virsh/qemu-img errors for consistent error handling upstream.
/// </summary>
public class VmOperationException : Exception
{
    /// <summary>
    /// Creates a new <see cref="VmOperationException"/> with the specified error message.
    /// </summary>
    public VmOperationException(string message) : base(message) { }

    /// <summary>
    /// Creates a new <see cref="VmOperationException"/> with the specified error message and inner exception.
    /// </summary>
    public VmOperationException(string message, Exception innerException) : base(message, innerException) { }
}
