using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.TeamLab.Application;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabResourcePoolTests
{
    private static AppDbContext CreateContext(Action<AppDbContext>? seed = null)
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"resource-pools-{Guid.NewGuid():N}")
            .Options);
        seed?.Invoke(context);
        context.SaveChanges();
        return context;
    }

    [Fact]
    public async Task Snapshot_ProjectsNodesAndTemplates_WithoutExecutionPlaneAddresses()
    {
        using var context = CreateContext(seed =>
        {
            seed.WorkerNodes.Add(new WorkerNode
            {
                Id = Guid.NewGuid(),
                Name = "agent-01",
                HostAddress = "10.0.7.125",
                AuthToken = "secret",
                Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
                Status = NodeStatus.Online,
                MaxContainers = 20,
                MaxVms = 5,
                TeamLabNetworkEnabled = true,
                TeamLabFabricStatus = TeamLabFabricStatus.Healthy
            });
            seed.ImageTemplates.Add(new ImageTemplate
            {
                Id = 7,
                Name = "windows-2022",
                OSType = OSType.Windows,
                ImageType = ImageType.Qcow2,
                FileSize = 8_589_934_592,
                ImageHash = new string('b', 64),
                RegistryUrl = "https://registry.internal",
                RegistryAuth = "credential"
            });
        });
        var service = new TeamLabResourcePoolService(context);

        var snapshot = await service.GetSnapshotAsync(CancellationToken.None);

        var node = Assert.Single(snapshot.ComputeNodes);
        Assert.Equal("agent-01", node.Name);
        Assert.True(node.DockerCapable);
        Assert.True(node.KvmCapable);
        Assert.Equal("healthy", node.FabricStatus);
        var template = Assert.Single(snapshot.Templates);
        Assert.Equal("windows", template.OsType);
        Assert.Equal("qcow2", template.ImageType);
        var serialized = System.Text.Json.JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("10.0.7.125", serialized);
        Assert.DoesNotContain("registry.internal", serialized);
        Assert.DoesNotContain("credential", serialized);
    }

    [Fact]
    public async Task NodeCache_ReportsDistributionStateAndReferenceCount()
    {
        using var context = CreateContext(seed =>
        {
            var record = new ImageDistributionRecord
            {
                Id = Guid.NewGuid(),
                ImageTemplateId = 7,
                WorkerNodeId = Guid.NewGuid(),
                ImageHash = new string('c', 64),
                Status = ImageDistributionStatus.Ready,
                Operation = ImageDistributionOperation.Distribute,
                Stage = ImageDistributionStage.Verifying
            };
            record.References.Add(new ImageDistributionReference
            {
                Kind = ImageDistributionReferenceKind.TeamLabRuntime,
                ResourceId = 42
            });
            seed.ImageDistributionRecords.Add(record);
        });
        var service = new TeamLabResourcePoolService(context);

        var page = await service.ListNodeCacheAsync(null, 50, CancellationToken.None);

        var entry = Assert.Single(page.Items);
        Assert.Equal(7, entry.TemplateId);
        Assert.Equal("ready", entry.Status);
        Assert.Equal("verifying", entry.Stage);
        Assert.Equal(1, entry.ActiveReferenceCount);
        Assert.Null(page.Next);
    }
}
