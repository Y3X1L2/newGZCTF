using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.Vm;
using GZCTF.Agent.Services.GuestControl;
using Xunit;

namespace GZCTF.Test.UnitTests.Vm;

public sealed class VmGuestControlTests
{
    [Theory]
    [InlineData(5, 6, true)]
    [InlineData(6, 6, false)]
    [InlineData(7, 6, false)]
    [InlineData(null, 6, false)]
    public void VmGenerationReplacement_OnlyAcceptsKnownOlderGeneration(
        int? existingGeneration,
        int requestedGeneration,
        bool expected)
    {
        Assert.Equal(expected, KvmService.IsStaleGeneration(existingGeneration, requestedGeneration));
    }

    [Theory]
    [InlineData(5, 5, 5, true, (int)KvmService.VmCreateDisposition.Reuse)]
    [InlineData(5, 4, 5, true, (int)KvmService.VmCreateDisposition.Conflict)]
    [InlineData(null, 4, 5, true, (int)KvmService.VmCreateDisposition.Replace)]
    [InlineData(null, 5, 5, true, (int)KvmService.VmCreateDisposition.Replace)]
    [InlineData(null, 6, 5, true, (int)KvmService.VmCreateDisposition.Conflict)]
    [InlineData(null, null, 5, true, (int)KvmService.VmCreateDisposition.Conflict)]
    public void VmCreateIdentity_CrossValidatesDomainSidecarAndOrphanOverlay(
        int? domainGeneration,
        int? sidecarGeneration,
        int requestedGeneration,
        bool overlayExists,
        int expected)
    {
        var domainExists = domainGeneration.HasValue;

        Assert.Equal((KvmService.VmCreateDisposition)expected, KvmService.EvaluateCreateDisposition(
            domainExists, domainGeneration, sidecarGeneration, overlayExists, requestedGeneration));
    }

    [Fact]
    public void VmIdentityValidation_RequiresMatchingGenerationSidecarAndStableNativeId()
    {
        const string vmName = "tl42-windows";
        var nativeId = VmDomainBuilder.BuildStableDomainId(vmName, 3).ToString("D");

        Assert.Null(KvmService.GetIdentityConflict(
            vmName, nativeId, 3, 3, 3, nativeId));
        Assert.NotNull(KvmService.GetIdentityConflict(
            vmName, nativeId, 3, 2, 3, nativeId));
        Assert.NotNull(KvmService.GetIdentityConflict(
            vmName, nativeId, 3, 3, 2, nativeId));
        Assert.NotNull(KvmService.GetIdentityConflict(
            vmName, Guid.NewGuid().ToString("D"), 3, 3, 3, nativeId));
        var foreignNativeId = Guid.NewGuid().ToString("D");
        Assert.NotNull(KvmService.GetIdentityConflict(
            vmName, foreignNativeId, 3, 3, 3, foreignNativeId));
    }

    [Fact]
    public void DomainBuilder_UsesStableIdentityAndTypedChannels()
    {
        var request = new CreateVmRequest
        {
            VmName = "tl42-windows",
            Generation = 3,
            Memory = 4096,
            Cpu = 4,
            GuestControl = new VmGuestControlConfig
            {
                Enabled = true,
                Required = true,
                EndpointSensorChannel = true
            }
        };

        var first = VmDomainBuilder.BuildVirtInstallArguments(request, "/var/lib/gzctf/images/tl42.qcow2", "");
        var second = VmDomainBuilder.BuildVirtInstallArguments(request, "/var/lib/gzctf/images/tl42.qcow2", "");

        Assert.Equal(first, second);
        Assert.Contains($"--uuid {VmDomainBuilder.BuildStableDomainId(request.VmName, 3):D}", first);
        Assert.Contains("--cpu host-passthrough", first);
        Assert.Contains("--rng /dev/urandom", first);
        Assert.Contains("org.qemu.guest_agent.0", first);
        Assert.Contains("org.gzctf.sensor.0", first);
    }

    [Fact]
    public void GuestAgentPayload_SerializesArgumentsWithoutShellJsonConstruction()
    {
        var payload = VmGuestAgentService.BuildCommandPayload("guest-exec", new Dictionary<string, object?>
        {
            ["path"] = "powershell.exe",
            ["arg"] = new[] { "-File", "C:\\ProgramData\\GZCTF\\a'b.ps1" }
        });
        using var json = JsonDocument.Parse(payload);

        Assert.Equal("guest-exec", json.RootElement.GetProperty("execute").GetString());
        Assert.Equal("C:\\ProgramData\\GZCTF\\a'b.ps1",
            json.RootElement.GetProperty("arguments").GetProperty("arg")[1].GetString());
    }

    [Fact]
    public void BootstrapArtifactPaths_RejectTraversalAndTemplatesFailClosed()
    {
        Assert.Equal("bin/install.sh", VmBootstrapService.NormalizeArtifactPath("./bin/install.sh"));
        Assert.Throws<InvalidOperationException>(() => VmBootstrapService.NormalizeArtifactPath("../escape.sh"));
        Assert.Throws<InvalidOperationException>(() => VmBootstrapService.RenderTemplate(
            Encoding.UTF8.GetBytes("port=${service_port}"), new Dictionary<string, string>()));

        var rendered = VmBootstrapService.RenderTemplate(
            "port=${service_port}", new Dictionary<string, string> { ["service_port"] = "8080" });
        Assert.Equal("port=8080", rendered);
    }

    [Fact]
    public void BootstrapStepCheckpoints_RequireStableIdsAndRecognizeMissingGuestFiles()
    {
        Assert.Equal("install-service", VmBootstrapService.NormalizeStepId("install-service"));
        Assert.Throws<InvalidOperationException>(() => VmBootstrapService.NormalizeStepId("../install"));
        Assert.True(VmBootstrapService.IsMissingGuestFileError(
            "QGA command guest-file-open failed: No such file or directory"));
        Assert.False(VmBootstrapService.IsMissingGuestFileError(
            "QGA command guest-file-open failed: permission denied"));
    }

    [Fact]
    public void WindowsCapabilityProbe_OrdersGuestPrerequisitesBeforeFirstBoot()
    {
        var ordered = new[]
        {
            "bootstrap.firstboot.v1",
            "network.e1000e.v1",
            "windows.powershell.v1",
            "guest.qga.v1"
        }.OrderBy(VmBootstrapService.ProbeOrder).ToArray();

        Assert.Equal(
            ["guest.qga.v1", "windows.powershell.v1", "network.e1000e.v1", "bootstrap.firstboot.v1"],
            ordered);
    }

    [Fact]
    public void BootstrapHealthChecks_RunInsideGuestAndSystemdEnvironmentHasNoExportPrefix()
    {
        var tcp = VmBootstrapService.BuildNetworkHealthCommand(
            VmInitOsType.Linux, "service-port", "Tcp", "8081", "192.168.0.20", 5);
        Assert.Equal("/bin/bash", tcp.Path);
        Assert.Equal("192.168.0.20", tcp.Environment!["GZCTF_HEALTH_HOST"]);
        Assert.Equal("8081", tcp.Environment["GZCTF_HEALTH_PORT"]);

        var http = VmBootstrapService.BuildNetworkHealthCommand(
            VmInitOsType.Windows, "service-http", "Http", "http://${PRIMARY_IP}:8080/health", "10.0.0.2", 10);
        Assert.Equal(VmBootstrapService.WindowsPowerShellPath, http.Path);
        Assert.Equal("http://10.0.0.2:8080/health", http.Environment!["GZCTF_HEALTH_URI"]);

        var environment = Encoding.UTF8.GetString(VmBootstrapService.BuildLinuxEnvironmentFile(
            new Dictionary<string, string> { ["service_port"] = "8081" }));
        Assert.Equal("service_port='8081'\n", environment);
        Assert.DoesNotContain("export ", environment, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiNicIpSelection_UsesPrimaryInterfaceMac()
    {
        const string output = """
                               Name       MAC address          Protocol     Address
                               -------------------------------------------------------------------------------
                               vnet0      52:54:00:00:00:01    ipv4         10.20.0.10/24
                               vnet1      52:54:00:00:00:02    ipv4         172.16.0.20/24
                               """;
        var interfaces = new List<VmNetworkInterfaceRequest>
        {
            new() { MacAddress = "52:54:00:00:00:01", IpAddress = "10.20.0.10" },
            new() { MacAddress = "52:54:00:00:00:02", IpAddress = "172.16.0.20", IsPrimary = true }
        };

        Assert.Equal("172.16.0.20", KvmService.ParsePreferredInterfaceIp(output, interfaces));
        Assert.Null(KvmService.ParsePreferredInterfaceIp(
            "vnet0 52:54:00:00:00:01 ipv4 10.20.0.10/24", interfaces));
    }

    [Fact]
    public void WindowsNetworkRoutes_ArePersistentAndRouteErrorsAreNotSuppressed()
    {
        var script = VmBootstrapService.BuildWindowsNetworkScript([
            new VmNetworkInterfaceRequest
            {
                MacAddress = "52:54:00:00:00:02",
                IpAddress = "172.16.0.20",
                PrefixLength = 24,
                IsPrimary = true,
                Gateway = "172.16.0.1",
                Routes = ["10.20.0.0/24 via 172.16.0.1"]
            }
        ]);

        Assert.Contains("New-NetRoute", script, StringComparison.Ordinal);
        Assert.Contains("-PolicyStore PersistentStore", script, StringComparison.Ordinal);
        Assert.Contains("New-NetRoute @routeArgs -ErrorAction Stop", script, StringComparison.Ordinal);
        Assert.DoesNotContain("New-NetRoute @routeArgs -ErrorAction SilentlyContinue", script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigDriveV2_EmitsStructuredRoutesAndNoManagementDefaultRoute()
    {
        var request = new CreateVmRequest
        {
            Interfaces =
            [
                new VmNetworkInterfaceRequest
                {
                    MacAddress = "52:54:00:00:00:10",
                    IpAddress = "10.10.0.20",
                    PrefixLength = 24,
                    Gateway = "10.10.0.1",
                    DnsServers = ["10.10.0.2"],
                    Routes = ["192.168.20.0/24 via 10.10.0.1"],
                    IsPrimary = true
                },
                new VmNetworkInterfaceRequest
                {
                    MacAddress = "52:54:00:00:00:11",
                    IpAddress = "192.168.20.20",
                    PrefixLength = 24,
                    IsPrimary = false
                }
            ],
            ManagementInterface = new VmManagementInterfaceConfig
            {
                MacAddress = "02:7f:00:00:00:10",
                IpAddress = "100.127.0.20",
                PrefixLength = 16
            }
        };

        using var document = JsonDocument.Parse(GuestConfigDriveBuilder.BuildOpenStackNetworkData(request));
        var networks = document.RootElement.GetProperty("networks");
        var topologyRoutes = networks[0].GetProperty("routes");
        Assert.Equal("0.0.0.0", topologyRoutes[0].GetProperty("network").GetString());
        Assert.Equal("192.168.20.0", topologyRoutes[1].GetProperty("network").GetString());
        Assert.Equal("255.255.255.0", topologyRoutes[1].GetProperty("netmask").GetString());
        Assert.Equal("10.10.0.1", topologyRoutes[1].GetProperty("gateway").GetString());
        Assert.Empty(networks[1].GetProperty("routes").EnumerateArray());
        Assert.Empty(networks[2].GetProperty("routes").EnumerateArray());

        request.Interfaces[1].Gateway = "192.168.20.1";
        Assert.Throws<InvalidOperationException>(() =>
            GuestConfigDriveBuilder.BuildOpenStackNetworkData(request));
        request.Interfaces[1].Gateway = null;

        request.Interfaces[0].Routes = ["not-a-route"];
        Assert.Throws<InvalidOperationException>(() =>
            GuestConfigDriveBuilder.BuildOpenStackNetworkData(request));
    }
}
