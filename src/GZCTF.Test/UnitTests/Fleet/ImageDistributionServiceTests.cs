using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public class ImageDistributionServiceTests
{
    [Fact]
    public async Task DistributeTemplateAsync_UsesOnlyCapabilityMatchingNodes()
    {
        await using var context = CreateContext();
        var dockerNode = SeedNode(context, "docker-node", NodeCapability.Docker);
        var kvmNode = SeedNode(context, "kvm-node", NodeCapability.Kvm);
        var hybridNode = SeedNode(context, "hybrid-node", NodeCapability.Docker | NodeCapability.Kvm);
        var dockerTemplate = SeedDockerTemplate(context);
        var vmTemplate = SeedVmTemplate(context);
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient();
        var service = CreateService(context, agent);

        await service.DistributeTemplateAsync(dockerTemplate.Id, ImageDistributionReferenceKey.Game(1), CancellationToken.None);
        await service.DistributeTemplateAsync(vmTemplate.Id, ImageDistributionReferenceKey.Game(1), CancellationToken.None);

        Assert.Equal(new[] { dockerNode.Id, hybridNode.Id }.OrderBy(x => x),
            agent.PulledDockerNodes.OrderBy(x => x));
        Assert.Equal(new[] { kvmNode.Id, hybridNode.Id }.OrderBy(x => x),
            agent.DownloadedVmNodes.OrderBy(x => x));

        var records = await context.ImageDistributionRecords.AsNoTracking().ToArrayAsync();
        Assert.All(records.Where(r => r.ImageTemplateId == dockerTemplate.Id),
            r => Assert.Equal(ImageDistributionStatus.Ready, r.Status));
        Assert.All(records.Where(r => r.ImageTemplateId == vmTemplate.Id),
            r => Assert.Equal(ImageDistributionStatus.Ready, r.Status));
    }

    [Fact]
    public async Task DistributeTemplateAsync_SkipsReadySameHashRecordAndIncrementsReference()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, "docker-node", NodeCapability.Docker);
        var template = SeedDockerTemplate(context);
        context.ImageDistributionRecords.Add(new ImageDistributionRecord
        {
            ImageTemplateId = template.Id,
            WorkerNodeId = node.Id,
            ImageHash = template.ImageHash!,
            ImageType = template.ImageType,
            Status = ImageDistributionStatus.Ready,
            References = [Reference(ImageDistributionReferenceKind.Game, 1)]
        });
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient();
        var service = CreateService(context, agent);

        await service.DistributeTemplateAsync(template.Id, ImageDistributionReferenceKey.Game(2), CancellationToken.None);

        Assert.Empty(agent.PulledDockerNodes);
        var record = await context.ImageDistributionRecords.SingleAsync();
        Assert.Equal(2, await context.ImageDistributionReferences.CountAsync());
        Assert.Equal(ImageDistributionStatus.Ready, record.Status);
    }

    [Fact]
    public async Task DistributeToCapableNodesAsync_MarksTemplateErrorAndThrowsWhenNodeFails()
    {
        await using var context = CreateContext();
        SeedNode(context, "docker-node", NodeCapability.Docker);
        var template = SeedDockerTemplate(context);
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient
        {
            DockerPullException = new HttpRequestException("registry unavailable")
        };
        var service = CreateService(context, agent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DistributeToCapableNodesAsync(template, CancellationToken.None));

        var record = await context.ImageDistributionRecords.SingleAsync();
        Assert.Equal(ImageDistributionStatus.Failed, record.Status);
        Assert.Equal(ImageStatus.Error, template.Status);
        Assert.Contains("docker-node", template.ErrorMessage);
        Assert.Contains("registry unavailable", exception.Message);
    }

    [Fact]
    public async Task DistributeToCapableNodesAsync_MarksTemplateErrorWhenNoCapableNodeExists()
    {
        await using var context = CreateContext();
        SeedNode(context, "kvm-only", NodeCapability.Kvm);
        var template = SeedDockerTemplate(context);
        template.Status = ImageStatus.Importing;
        await context.SaveChangesAsync();
        var service = CreateService(context, new RecordingAgentClient());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DistributeToCapableNodesAsync(template, CancellationToken.None));

        Assert.Equal(ImageStatus.Error, template.Status);
        Assert.Contains("no online schedulable Docker node", exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no online schedulable Docker node", template.ErrorMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.ImageDistributionRecords.ToArrayAsync());
    }

    [Fact]
    public async Task DistributeToCapableNodesAsync_RecordsTimeoutAsFailedDistribution()
    {
        await using var context = CreateContext();
        SeedNode(context, "docker-node", NodeCapability.Docker);
        var template = SeedDockerTemplate(context);
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient
        {
            DockerPullException = new TaskCanceledException("agent request timed out")
        };
        var service = CreateService(context, agent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DistributeToCapableNodesAsync(template, CancellationToken.None));

        var record = await context.ImageDistributionRecords.SingleAsync();
        Assert.Equal(ImageDistributionStatus.Failed, record.Status);
        Assert.Contains("timed out", record.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ImageStatus.Error, template.Status);
    }

    [Fact]
    public async Task DistributeToCapableNodesAsync_PropagatesCallerCancellationWithoutFailedRecord()
    {
        await using var context = CreateContext();
        SeedNode(context, "docker-node", NodeCapability.Docker);
        var template = SeedDockerTemplate(context);
        await context.SaveChangesAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var agent = new RecordingAgentClient
        {
            DockerPullException = new OperationCanceledException(cancellation.Token)
        };
        var service = CreateService(context, agent);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.DistributeToCapableNodesAsync(template, cancellation.Token));

        Assert.NotEqual(ImageStatus.Error, template.Status);
        Assert.DoesNotContain(await context.ImageDistributionRecords.ToArrayAsync(),
            record => record.Status == ImageDistributionStatus.Failed);
    }

    [Fact]
    public async Task DistributeToCapableNodesAsync_SuccessfulRetryRestoresTemplateReady()
    {
        await using var context = CreateContext();
        SeedNode(context, "docker-node", NodeCapability.Docker);
        var template = SeedDockerTemplate(context);
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient
        {
            DockerPullException = new HttpRequestException("temporary registry failure")
        };
        var service = CreateService(context, agent);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DistributeToCapableNodesAsync(template, CancellationToken.None));
        Assert.Equal(ImageStatus.Error, template.Status);

        agent.DockerPullException = null;
        var records = await service.DistributeToCapableNodesAsync(template, CancellationToken.None);

        Assert.Equal(ImageStatus.Ready, template.Status);
        Assert.Null(template.ErrorMessage);
        var record = Assert.Single(records);
        Assert.Equal(ImageDistributionStatus.Ready, record.Status);
    }

    [Fact]
    public async Task ReleaseGameReferencesAsync_DoesNotDeleteCacheStillReferencedByAnotherGame()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, "kvm-node", NodeCapability.Kvm);
        var template = SeedVmTemplate(context);
        context.ImageDistributionRecords.Add(new ImageDistributionRecord
        {
            ImageTemplateId = template.Id,
            WorkerNodeId = node.Id,
            ImageHash = template.ImageHash!,
            ImageType = template.ImageType,
            Status = ImageDistributionStatus.Ready,
            References =
            [
                Reference(ImageDistributionReferenceKind.Game, 1),
                Reference(ImageDistributionReferenceKind.Game, 2)
            ]
        });
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient();
        var service = CreateService(context, agent);

        await service.ReleaseGameReferencesAsync(1, CancellationToken.None);

        var record = await context.ImageDistributionRecords.SingleAsync();
        Assert.Equal(ImageDistributionStatus.Ready, record.Status);
        Assert.Single(await context.ImageDistributionReferences.ToArrayAsync());
        Assert.Empty(agent.DeletedVmNodes);
    }

    [Fact]
    public async Task ReleaseGameReferencesAsync_DoesNotDeleteActiveVmTemplateCache()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, "kvm-node", NodeCapability.Kvm);
        var template = SeedVmTemplate(context);
        var challenge = new GameChallenge
        {
            Id = 42,
            GameId = 1,
            Title = "vm",
            Content = "vm",
            Type = ChallengeType.DynamicContainer,
            Environment = EnvironmentType.WindowsVM,
            ImageTemplateId = template.Id
        };
        context.GameChallenges.Add(challenge);
        context.VmInstances.Add(new VmInstance
        {
            Id = Guid.NewGuid(),
            ChallengeId = challenge.Id,
            NodeId = node.Id,
            UserId = Guid.NewGuid(),
            VmName = "test-vm",
            ProviderName = "fleet",
            Status = VmInstanceStatus.Running
        });
        context.ImageDistributionRecords.Add(new ImageDistributionRecord
        {
            ImageTemplateId = template.Id,
            WorkerNodeId = node.Id,
            ImageHash = template.ImageHash!,
            ImageType = template.ImageType,
            Status = ImageDistributionStatus.Ready,
            References = [Reference(ImageDistributionReferenceKind.Game, 1)]
        });
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient();
        var service = CreateService(context, agent);

        await service.ReleaseGameReferencesAsync(1, CancellationToken.None);

        var record = await context.ImageDistributionRecords.SingleAsync();
        Assert.Equal(ImageDistributionStatus.CleanupPending, record.Status);
        Assert.Empty(await context.ImageDistributionReferences.ToArrayAsync());
        Assert.Empty(agent.DeletedVmNodes);
    }

    [Fact]
    public async Task ReconcileReferencesAsync_RemovesReferencesToDeletedResources()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, "kvm-node", NodeCapability.Kvm);
        var template = SeedVmTemplate(context);
        context.ImageDistributionRecords.Add(new ImageDistributionRecord
        {
            ImageTemplateId = template.Id,
            WorkerNodeId = node.Id,
            ImageHash = template.ImageHash!,
            ImageType = template.ImageType,
            Status = ImageDistributionStatus.Ready,
            References = [Reference(ImageDistributionReferenceKind.Game, 999)]
        });
        await context.SaveChangesAsync();
        var service = CreateService(context, new RecordingAgentClient());

        await service.ReconcileReferencesAsync(CancellationToken.None);

        var record = await context.ImageDistributionRecords.SingleAsync();
        Assert.Empty(record.References);
        Assert.Empty(await context.ImageDistributionReferences.ToArrayAsync());
        Assert.Equal(ImageDistributionStatus.CleanupPending, record.Status);
    }

    static ImageDistributionService CreateService(AppDbContext context, RecordingAgentClient agent)
    {
        var registry = new DockerImageRegistryService(
            Options.Create(new DockerRegistrySettings { Address = "10.24.0.28:5000", Namespace = "ctf" }),
            BuildScopeFactory(context),
            agent,
            NullLogger<DockerImageRegistryService>.Instance);

        var vmRegistry = new Mock<VmImageRegistryService>(
            Options.Create(new DockerRegistrySettings { Address = "10.24.0.28:5000", Namespace = "ctf" }),
            new ServiceCollection().AddHttpClient().BuildServiceProvider().GetRequiredService<IHttpClientFactory>(),
            NullLogger<VmImageRegistryService>.Instance);
        vmRegistry
            .Setup(r => r.EnsureArtifactAsync(It.IsAny<ImageTemplate>(), It.IsAny<CancellationToken>()))
            .Returns<ImageTemplate, CancellationToken>((template, _) => Task.FromResult(
                new VmImageArtifactReference(
                    "10.24.0.28:5000",
                    $"ctf/gzctf/vm-template/{template.Id}",
                    template.ImageHash!,
                    $"sha256:{template.ImageHash}")));

        var artifacts = new VmArtifactStore(
            Options.Create(new DockerRegistrySettings { Address = "10.24.0.28:5000", Namespace = "ctf" }),
            vmRegistry.Object,
            NullLogger<VmArtifactStore>.Instance);

        return new ImageDistributionService(
            context,
            agent,
            registry,
            artifacts,
            NullLogger<ImageDistributionService>.Instance);
    }

    static ImageDistributionReference Reference(ImageDistributionReferenceKind kind, int resourceId) => new()
    {
        Kind = kind,
        ResourceId = resourceId
    };

    static IServiceScopeFactory BuildScopeFactory(AppDbContext context)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton<INodeRepository>(new Mock<INodeRepository>().Object);
        services.AddLogging();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    static WorkerNode SeedNode(AppDbContext context, string name, NodeCapability capability)
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Name = name,
            HostAddress = "10.24.0.30",
            AuthToken = Guid.NewGuid().ToString("N"),
            Capabilities = capability,
            Status = NodeStatus.Online,
            IsSchedulable = true,
            MaxContainers = 10,
            MaxVms = 10,
            LastHeartbeat = DateTimeOffset.UtcNow
        };
        context.WorkerNodes.Add(node);
        return node;
    }

    static ImageTemplate SeedDockerTemplate(AppDbContext context)
    {
        var template = new ImageTemplate
        {
            Id = 11,
            Name = "web",
            ImageType = ImageType.Docker,
            OSType = OSType.Linux,
            RegistryUrl = "gzctf-internal://training/web:latest",
            ImageHash = "training-web-latest",
            FileSize = 1,
            Status = ImageStatus.Ready
        };
        context.ImageTemplates.Add(template);
        return template;
    }

    static ImageTemplate SeedVmTemplate(AppDbContext context)
    {
        var template = new ImageTemplate
        {
            Id = 12,
            Name = "win",
            ImageType = ImageType.Qcow2,
            OSType = OSType.Windows,
            ImageHash = new string('a', 64),
            FileSize = 1024,
            Status = ImageStatus.Ready
        };
        context.ImageTemplates.Add(template);
        return template;
    }

    static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    sealed class RecordingAgentClient : AgentClient
    {
        public List<Guid> PulledDockerNodes { get; } = [];
        public List<Guid> DownloadedVmNodes { get; } = [];
        public List<Guid> DeletedVmNodes { get; } = [];
        public Exception? DockerPullException { get; set; }

        public RecordingAgentClient() : base(
            new Mock<IHttpClientFactory>().Object,
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<AgentClient>.Instance)
        {
        }

        public override Task PullDockerImageAsync(Guid nodeId, string image, string? registryAuth, CancellationToken token)
        {
            if (DockerPullException is not null)
                throw DockerPullException;
            PulledDockerNodes.Add(nodeId);
            return Task.CompletedTask;
        }

        public override Task<AgentVmImageDownloadResult> DownloadVmImageAsync(Guid nodeId, int templateId, string hash,
            string? downloadUrl = null, long? expectedSize = null, CancellationToken token = default)
        {
            DownloadedVmNodes.Add(nodeId);
            return Task.FromResult(AgentVmImageDownloadResult.Ok(false, true, expectedSize, $"sha256:{hash}"));
        }

        public override Task DeleteVmImageAsync(Guid nodeId, int templateId, string hash, CancellationToken token)
        {
            DeletedVmNodes.Add(nodeId);
            return Task.CompletedTask;
        }
    }
}
