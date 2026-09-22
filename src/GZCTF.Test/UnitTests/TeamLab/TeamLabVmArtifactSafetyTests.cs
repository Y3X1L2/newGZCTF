using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.Vm;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabVmArtifactSafetyTests
{
    [Fact]
    public async Task VerifyBaseImage_RehashesContentEvenWhenLengthAndTimestampAreUnchanged()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "content-a");
            var timestamp = File.GetLastWriteTimeUtc(path);
            var expected = $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("content-a"))).ToLowerInvariant()}";

            Assert.True(await LibvirtTeamLabProvider.HasExpectedBaseImageAsync(
                path, expected, CancellationToken.None));

            await File.WriteAllTextAsync(path, "content-b");
            File.SetLastWriteTimeUtc(path, timestamp);

            Assert.False(await LibvirtTeamLabProvider.HasExpectedBaseImageAsync(
                path, expected, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VmImageDigestLock_SerializesVerificationAndReplacement()
    {
        var resourceLock = new AgentResourceLock();
        var imagePath = Path.Combine(Path.GetTempPath(), "teamlab-vm-artifact-lock.qcow2");
        await using var verification = await resourceLock.AcquireAsync(
            LibvirtTeamLabProvider.BaseImageLockKey(imagePath), CancellationToken.None);

        var replacement = resourceLock.AcquireAsync(
            LibvirtTeamLabProvider.BaseImageLockKey(imagePath), CancellationToken.None).AsTask();

        Assert.False(replacement.IsCompleted);
        await verification.DisposeAsync();
        await using var acquired = await replacement.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void NoCloudNetworkConfig_UsesPlanMacAddressAndStaticRoute()
    {
        var network = new TeamLabNetworkIntentV2(
            "field", "10.96.1.0/24", "10.96.1.1",
            [new("vm-port", "linux-vm", "02:42:29:19:d6:14", "10.96.1.20")],
            [], [], null, [], []);
        var asset = new TeamLabAssetExecutionSpecV2(
            "linux-vm", "vm", "domain", new string('a', 64), "domain", 79, 2, 2048,
            [new("field", "vm-port", "eth0", "10.96.1.20", "10.96.1.1", true)], []);
        var plan = new TeamLabExecutionPlanV2(
            1, Guid.NewGuid(), 1, "shard", $"sha256:{new string('b', 64)}", $"sha256:{new string('c', 64)}",
            true, [network], [asset], []);

        var config = LibvirtTeamLabProvider.BuildNoCloudNetworkConfig(plan, asset);

        Assert.NotNull(config);
        Assert.Contains("macaddress: \"02:42:29:19:d6:14\"", config);
        Assert.Contains("set-name: \"eth0\"", config);
        Assert.Contains("- 10.96.1.20/24", config);
        Assert.Contains("to: default", config);
        Assert.Contains("via: 10.96.1.1", config);
    }

    [Fact]
    public void NoCloudNetworkConfig_WithoutStaticAddress_IsNotCreated()
    {
        var network = new TeamLabNetworkIntentV2(
            "field", "10.96.1.0/24", "10.96.1.1",
            [new("vm-port", "linux-vm", "02:42:29:19:d6:14", null)],
            [], [], null, [], []);
        var asset = new TeamLabAssetExecutionSpecV2(
            "linux-vm", "vm", "domain", new string('a', 64), "domain", 79, 2, 2048,
            [new("field", "vm-port", "eth0", null, "10.96.1.1", true)], []);
        var plan = new TeamLabExecutionPlanV2(
            1, Guid.NewGuid(), 1, "shard", $"sha256:{new string('b', 64)}", $"sha256:{new string('c', 64)}",
            true, [network], [asset], []);

        Assert.Null(LibvirtTeamLabProvider.BuildNoCloudNetworkConfig(plan, asset));
    }

    [Fact]
    public void DomainXml_UsesWindowsCompatibleDiskAndNetworkDevices()
    {
        var network = new TeamLabNetworkIntentV2(
            "field", "10.96.1.0/24", "10.96.1.1",
            [new("vm-port", "windows-vm", "02:42:29:19:d6:14", "10.96.1.20")],
            [], [], null, [], []);
        var asset = new TeamLabAssetExecutionSpecV2(
            "windows-vm", "vm", "domain", new string('a', 64), "domain", 1, 2, 4096,
            [new("field", "vm-port", "eth0", "10.96.1.20", "10.96.1.1", true)], [],
            OperatingSystem: TeamLabGuestOperatingSystem.Windows);
        var plan = new TeamLabExecutionPlanV2(
            1, Guid.NewGuid(), 1, "shard", $"sha256:{new string('b', 64)}", $"sha256:{new string('c', 64)}",
            true, [network], [asset], []);
        var provider = new LibvirtTeamLabProvider(
            Options.Create(new KvmConfig()),
            Options.Create(new AgentTeamLabConfig()),
            new AgentResourceLock(),
            NullLogger<LibvirtTeamLabProvider>.Instance);

        var domain = XElement.Parse(provider.BuildDomainXml(
            plan, asset, "domain", "/tmp/windows.qcow2", "/tmp/seed.iso"));
        var devices = domain.Element("devices")!;
        var systemDisk = devices.Elements("disk").Single(item => (string?)item.Attribute("device") == "disk");
        var seedDisk = devices.Elements("disk").Single(item => (string?)item.Attribute("device") == "cdrom");

        Assert.NotNull(domain.Element("features")?.Element("acpi"));
        Assert.NotNull(domain.Element("features")?.Element("apic"));
        Assert.Equal("sata", (string?)systemDisk.Element("target")?.Attribute("bus"));
        Assert.Equal("sda", (string?)systemDisk.Element("target")?.Attribute("dev"));
        Assert.Equal("sdb", (string?)seedDisk.Element("target")?.Attribute("dev"));
        Assert.Equal("e1000e", (string?)devices.Element("interface")?.Element("model")?.Attribute("type"));
    }
}
