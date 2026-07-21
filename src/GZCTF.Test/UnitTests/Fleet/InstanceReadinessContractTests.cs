using System;
using System.Text.Json;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Fleet;
using GZCTF.Services.Vm;
using GZCTF.Utils;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public sealed class InstanceReadinessContractTests
{
    [Fact]
    public void PortMapRevision_IsStableAcrossEnumerationOrder()
    {
        var first = new PortMappingEntry(30001, "10.24.0.30", 32768,
            Guid.Parse("11111111-1111-4111-8111-111111111111"));
        var second = new PortMappingEntry(30002, "10.24.0.31", 32769,
            Guid.Parse("22222222-2222-4222-8222-222222222222"));

        Assert.Equal(
            PortMappingRevision.Compute([first, second]),
            PortMappingRevision.Compute([second, first]));
    }

    [Fact]
    public void PortMapRevision_RejectsStaleRevisionOrLeaseSet()
    {
        var lease = Guid.Parse("11111111-1111-4111-8111-111111111111");
        PortMappingEntry[] current = [new(30001, "10.24.0.30", 32768, lease)];
        var revision = PortMappingRevision.Compute(current);

        Assert.True(PortMappingRevision.Matches(revision, [lease], current));
        Assert.False(PortMappingRevision.Matches("stale", [lease], current));
        Assert.False(PortMappingRevision.Matches(revision, [Guid.NewGuid()], current));
    }

    [Fact]
    public void PublicationError_IsTrimmedAndBounded()
    {
        Assert.Equal(
            "Public gateway failed to publish the instance route.",
            PortMappingRevision.NormalizeError("  "));

        var normalized = PortMappingRevision.NormalizeError($"  {new string('x', 700)}  ");
        Assert.Equal(512, normalized.Length);
        Assert.DoesNotContain(' ', normalized);
    }

    [Fact]
    public void DirectContainerEntry_IsReadyByDefault()
    {
        var container = new Container
        {
            IP = "10.24.0.30",
            Port = 32768,
            PublicIP = "10.24.0.30",
            PublicPort = 32768
        };

        Assert.Equal(ContainerEntryStatus.Ready, container.EntryStatus);
        Assert.Equal("10.24.0.30:32768", container.ReadyEntry);
    }

    [Fact]
    public void VmCredentials_AreUniqueProtectedAndCloudbaseConsumable()
    {
        var service = new VmCredentialService(new EphemeralDataProtectionProvider());
        var first = new VmInstance { VmName = "vm-first" };
        var second = new VmInstance { VmName = "vm-second" };

        service.Initialize(first);
        service.Initialize(second);
        var firstPassword = service.RevealPassword(first);
        var secondPassword = service.RevealPassword(second);

        Assert.NotEqual(firstPassword, secondPassword);
        Assert.NotEqual(firstPassword, first.RdpPasswordProtected);
        Assert.DoesNotContain("qwer1234!", firstPassword, StringComparison.Ordinal);

        var cloudInit = FleetVmService.BuildWindowsCloudInit(first, firstPassword);
        Assert.True(cloudInit.Enabled);
        Assert.Contains(firstPassword, cloudInit.UserData, StringComparison.Ordinal);
        Assert.Contains("RemoteDesktop-UserMode-In-*", cloudInit.UserData, StringComparison.Ordinal);
        Assert.Contains("GZCTF-RDP-In-TCP", cloudInit.UserData, StringComparison.Ordinal);
        Assert.DoesNotContain("qwer1234!", cloudInit.UserData, StringComparison.Ordinal);
    }

    [Fact]
    public void VmCredentialContract_RejectsUnsupportedNodeAndImage()
    {
        var unsupported = CreateNode([AgentFeatureIds.Kvm]);
        var supported = CreateNode([AgentFeatureIds.Kvm, AgentFeatureIds.CloudInit]);

        Assert.NotNull(FleetVmService.ValidateCredentialNode(unsupported));
        Assert.Null(FleetVmService.ValidateCredentialNode(supported));
        supported.IsLocal = true;
        Assert.NotNull(FleetVmService.ValidateCredentialNode(supported));
        Assert.NotNull(FleetVmService.ValidateCredentialImage(null));
        Assert.NotNull(FleetVmService.ValidateCredentialImage(new ImageTemplate()));
        Assert.NotNull(FleetVmService.ValidateCredentialImage(new ImageTemplate
        {
            OSType = OSType.Windows,
            ImageType = ImageType.Qcow2,
            Status = ImageStatus.Error,
            SupportsInstanceCredentials = true
        }));
        Assert.Null(FleetVmService.ValidateCredentialImage(new ImageTemplate
        {
            OSType = OSType.Windows,
            ImageType = ImageType.Qcow2,
            Status = ImageStatus.Ready,
            SupportsInstanceCredentials = true
        }));
    }

    private static WorkerNode CreateNode(string[] features)
    {
        var manifest = new AgentCapabilityManifest(
            "test",
            null,
            AgentCapabilityEvaluator.SupportedManifestSchema,
            features,
            new AgentExecutionLimits(1, 1, 1, 1, 1, 1),
            new AgentHostFacts(4, 8L * 1024 * 1024 * 1024, true, true),
            DateTimeOffset.UtcNow);

        return new WorkerNode
        {
            CapabilityManifestJson = JsonSerializer.Serialize(
                manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
    }
}
