using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.Observation;

namespace GZCTF.Agent.Services.Vm;

public static partial class VmDomainBuilder
{
    public static string BuildVirtInstallArguments(
        CreateVmRequest request,
        string overlayPath,
        string mediaArguments)
    {
        if (!SafeName().IsMatch(request.VmName))
            throw new ArgumentException("Invalid VM name.", nameof(request));
        if (request.Memory < 256 || request.Cpu < 1)
            throw new ArgumentException("VM resources are invalid.", nameof(request));

        var generation = Math.Max(1, request.Generation);
        var domainId = BuildStableDomainId(request.VmName, generation);
        var channels = BuildChannelArguments(request);
        var media = string.IsNullOrWhiteSpace(mediaArguments)
            ? string.Empty
            : mediaArguments.Trim() + " ";

        return $"--name {ShellEscape(request.VmName)} --uuid {domainId:D} " +
               $"--memory {request.Memory} --vcpus {request.Cpu} " +
               "--cpu host-passthrough --rng /dev/urandom " +
               $"--metadata description={ShellEscape($"gzctf-generation={generation}")} " +
               $"--disk path={ShellEscape(overlayPath)} --osinfo detect=on,require=off --import --noautoconsole " +
               $"{media}{KvmService.BuildVirtInstallNetworkArguments(request)} {channels} " +
               "--graphics vnc,listen=0.0.0.0";
    }

    internal static string BuildChannelArguments(CreateVmRequest request)
    {
        var config = request.GuestControl;
        if (!config.Enabled)
            return string.Empty;

        var arguments = new List<string>
        {
            "--channel unix,target.type=virtio,target.name=org.qemu.guest_agent.0"
        };
        if (config.EndpointSensorChannel)
            arguments.Add(
                $"--channel unix,path={ShellEscape(EndpointSensorChannelService.VmSocketPath(request.VmName, request.Generation))},mode=bind,target.type=virtio,target.name=org.gzctf.sensor.0");
        return string.Join(' ', arguments);
    }

    internal static Guid BuildStableDomainId(string vmName, int generation)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"gzctf-vm:{vmName}:{Math.Max(1, generation)}"));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    internal static string ShellEscape(string value) => $"'{value.Replace("'", "'\\''")}'";

    [GeneratedRegex("^[a-zA-Z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();
}
