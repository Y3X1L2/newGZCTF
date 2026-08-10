using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using GZCTF.Modules.Content.Infrastructure;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.TeamLab.Domain;
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
    public async Task DistributeTemplateAsync_QueuesOnlyCapabilityMatchingNodes()
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

        await service.DistributeTemplateAsync(
            dockerTemplate.Id, ImageDistributionReferenceKey.Game(1), CancellationToken.None);
        await service.DistributeTemplateAsync(
            vmTemplate.Id, ImageDistributionReferenceKey.Game(1), CancellationToken.None);

        Assert.Empty(agent.PulledDockerNodes);
        Assert.Empty(agent.DownloadedVmNodes);
        var records = await context.ImageDistributionRecords.AsNoTracking().ToArrayAsync();
        Assert.Equal(new[] { dockerNode.Id, hybridNode.Id }.Order().ToArray(), records
            .Where(record => record.ImageTemplateId == dockerTemplate.Id)
            .Select(record => record.WorkerNodeId).Order().ToArray());
        Assert.Equal(new[] { hybridNode.Id, kvmNode.Id }.Order().ToArray(), records
            .Where(record => record.ImageTemplateId == vmTemplate.Id)
            .Select(record => record.WorkerNodeId).Order().ToArray());
        Assert.All(records, record => Assert.Equal(ImageDistributionStatus.Pending, record.Status));
        Assert.Equal(4, await context.ImageDistributionReferences.CountAsync());
        Assert.Equal(4, await context.OperationalEvents.CountAsync(item =>
            item.EventCode == OperationalEventCodes.Image.DistributionQueued));
        Assert.Equal(4, await context.OperationalEvents.CountAsync(item =>
            item.EventCode == OperationalEventCodes.Image.ReferenceAttached));
    }

    [Fact]
    public async Task ProcessClaimedAsync_TransfersAndVerifiesVmArtifact()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, "kvm-node", NodeCapability.Kvm);
        var template = SeedVmTemplate(context);
        var record = new ImageDistributionRecord
        {
            ImageTemplateId = template.Id,
            WorkerNodeId = node.Id,
            ImageHash = template.ImageHash!,
            ImageType = template.ImageType,
            Status = ImageDistributionStatus.Pulling,
            ClaimOwner = "worker-1",
            ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        context.ImageDistributionRecords.Add(record);
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient();

        await CreateService(context, agent)
            .ProcessClaimedAsync(record.Id, "worker-1", CancellationToken.None);

        Assert.Equal([node.Id], agent.DownloadedVmNodes);
        await context.Entry(record).ReloadAsync();
        Assert.Equal(ImageDistributionStatus.Ready, record.Status);
        Assert.Equal(ImageDistributionStage.None, record.Stage);
        Assert.Null(record.ClaimOwner);
        Assert.Null(record.ErrorMessage);
        Assert.Equal(ImageStatus.Ready, template.Status);
        var eventCodes = await context.OperationalEvents
            .Where(item => item.CorrelationId == record.Id)
            .Select(item => item.EventCode)
            .ToArrayAsync();
        Assert.Contains(OperationalEventCodes.Image.TransferStarted, eventCodes);
        Assert.Contains(OperationalEventCodes.Image.VerifyStarted, eventCodes);
        Assert.Contains(OperationalEventCodes.Image.DistributionReady, eventCodes);
    }

    [Fact]
    public async Task ProcessClaimedAsync_PreparedVmUsesImmutableRegistryProvenance()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, "kvm-node", NodeCapability.Kvm);
        var source = SeedVmTemplate(context);
        var artifact = new VmPreparedArtifact
        {
            Id = 7,
            OSType = OSType.Windows,
            Status = VmPreparedArtifactStatus.Ready,
            ArtifactDigest = new string('d', 64),
            ArtifactSize = 2048,
            RegistryAddress = "10.24.0.28:5000",
            RegistryRepository = "gzctf/vm-prepared/12",
            RegistryTag = "f1-p1-dddd"
        };
        var prepared = new ImageTemplate
        {
            Id = 13,
            Name = "prepared-win",
            ImageType = ImageType.Qcow2,
            OSType = OSType.Windows,
            ImageHash = artifact.ArtifactDigest,
            FileSize = artifact.ArtifactSize,
            Status = ImageStatus.Ready,
            VmArtifactStatus = VmArtifactStatus.Ready,
            VmRuntimeMode = VmRuntimeMode.Managed,
            PreparedArtifact = artifact
        };
        artifact.DerivedImageTemplate = prepared;
        context.AddRange(artifact, prepared);
        var record = new ImageDistributionRecord
        {
            ImageTemplateId = prepared.Id,
            WorkerNodeId = node.Id,
            ImageHash = prepared.ImageHash,
            ImageType = prepared.ImageType,
            Status = ImageDistributionStatus.Pulling,
            ClaimOwner = "worker-1",
            ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        context.ImageDistributionRecords.Add(record);
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient();

        await CreateService(context, agent)
            .ProcessClaimedAsync(record.Id, "worker-1", CancellationToken.None);

        Assert.Equal([node.Id], agent.DownloadedPreparedVmNodes);
        Assert.Empty(agent.DownloadedVmNodes);
        Assert.Equal(ImageDistributionStatus.Ready,
            (await context.ImageDistributionRecords.SingleAsync()).Status);
    }

    [Fact]
    public async Task ProcessClaimedAsync_FailureDoesNotPoisonStorageTemplate()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, "docker-node", NodeCapability.Docker);
        var template = SeedDockerTemplate(context);
        var record = new ImageDistributionRecord
        {
            ImageTemplateId = template.Id,
            WorkerNodeId = node.Id,
            ImageHash = template.ImageHash!,
            ImageType = template.ImageType,
            Status = ImageDistributionStatus.Pulling,
            ClaimOwner = "worker-1",
            ClaimExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        context.ImageDistributionRecords.Add(record);
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient
        {
            DockerPullException = new HttpRequestException("registry unavailable")
        };

        await CreateService(context, agent)
            .ProcessClaimedAsync(record.Id, "worker-1", CancellationToken.None);

        await context.Entry(record).ReloadAsync();
        Assert.Equal(ImageDistributionStatus.Failed, record.Status);
        Assert.Equal(OperationalErrorCodes.ImageTransferFailed, record.LastErrorCode);
        Assert.Contains("registry unavailable", record.ErrorMessage);
        Assert.Equal(ImageStatus.Ready, template.Status);
        Assert.Contains(OperationalEventCodes.Image.DistributionFailed,
            await context.OperationalEvents.Select(item => item.EventCode).ToArrayAsync());
    }

    [Fact]
    public async Task DistributeTemplateAsync_ReadySameHashOnlyAddsReference()
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

        await CreateService(context, agent).DistributeTemplateAsync(
            template.Id, ImageDistributionReferenceKey.Game(2), CancellationToken.None);

        Assert.Empty(agent.PulledDockerNodes);
        Assert.Equal(2, await context.ImageDistributionReferences.CountAsync());
        Assert.Equal(ImageDistributionStatus.Ready,
            (await context.ImageDistributionRecords.SingleAsync()).Status);
    }

    [Fact]
    public async Task DistributeTemplateAsync_TeamLabReleasesKeepIndependentPublicReferences()
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
            Status = ImageDistributionStatus.Ready
        });
        await context.SaveChangesAsync();
        var firstRelease = Guid.NewGuid();
        var secondRelease = Guid.NewGuid();
        var service = CreateService(context, new RecordingAgentClient());

        await service.DistributeTemplateAsync(template.Id, ImageDistributionReferenceKey.TeamLabRelease(firstRelease), CancellationToken.None);
        await service.DistributeTemplateAsync(template.Id, ImageDistributionReferenceKey.TeamLabRelease(secondRelease), CancellationToken.None);
        await service.DistributeTemplateAsync(template.Id, ImageDistributionReferenceKey.TeamLabRelease(firstRelease), CancellationToken.None);

        var references = await context.ImageDistributionReferences
            .Where(item => item.Kind == ImageDistributionReferenceKind.TeamLabRelease)
            .ToArrayAsync();
        Assert.Equal(2, references.Length);
        Assert.Equal(new[] { firstRelease, secondRelease }.Order(), references.Select(item => item.ResourcePublicId!.Value).Order());
    }

    [Fact]
    public async Task EnsureDockerImageOnNodeAsync_LegacyManagedAddressMatchesInternalTemplate()
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
            Status = ImageDistributionStatus.Ready
        });
        await context.SaveChangesAsync();

        await CreateService(context, new RecordingAgentClient()).EnsureDockerImageOnNodeAsync(
            "10.24.0.28:5000/training/web:latest", node.Id, CancellationToken.None);

        Assert.Equal(ImageDistributionStatus.Ready,
            (await context.ImageDistributionRecords.SingleAsync()).Status);
    }

    [Fact]
    public async Task DistributeGameAsync_LegacyManagedAddressQueuesInternalTemplate()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, "docker-node", NodeCapability.Docker);
        var template = SeedDockerTemplate(context);
        context.GameChallenges.Add(new GameChallenge
        {
            Id = 21,
            GameId = 7,
            Title = "legacy-image",
            Content = "test",
            Type = ChallengeType.StaticContainer,
            Environment = EnvironmentType.Docker,
            ContainerImage = "10.24.0.28:5000/training/web:latest",
            ExposePort = 80,
            IsEnabled = true
        });
        await context.SaveChangesAsync();

        await CreateService(context, new RecordingAgentClient())
            .DistributeGameAsync(7, CancellationToken.None);

        var record = await context.ImageDistributionRecords.SingleAsync();
        Assert.Equal(template.Id, record.ImageTemplateId);
        Assert.Equal(node.Id, record.WorkerNodeId);
        Assert.Equal(ImageDistributionStatus.Pending, record.Status);
        Assert.Contains(record.References, reference =>
            reference.Kind == ImageDistributionReferenceKind.Game && reference.ResourceId == 7);
    }

    [Fact]
    public async Task ReleaseReference_QueuesCleanupWithoutDeletingSharedCacheInline()
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
        Assert.Equal(ImageDistributionStatus.Ready,
            (await context.ImageDistributionRecords.SingleAsync()).Status);

        await service.ReleaseGameReferencesAsync(2, CancellationToken.None);
        Assert.Equal(ImageDistributionStatus.CleanupPending,
            (await context.ImageDistributionRecords.SingleAsync()).Status);
        Assert.Empty(agent.DeletedVmNodes);
    }

    [Fact]
    public async Task ReconcileReferencesAsync_RemovesDeletedResourceReference()
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

        await CreateService(context, new RecordingAgentClient())
            .ReconcileReferencesAsync(CancellationToken.None);

        var record = await context.ImageDistributionRecords.SingleAsync();
        Assert.Empty(await context.ImageDistributionReferences.ToArrayAsync());
        Assert.Equal(ImageDistributionStatus.CleanupPending, record.Status);
        Assert.Equal(ImageDistributionOperation.Cleanup, record.Operation);
    }

    [Fact]
    public async Task ReconcileReferencesAsync_KeepsCacheForRunningImageCertification()
    {
        await using var context = CreateContext();
        var node = SeedNode(context, "kvm-node", NodeCapability.Kvm);
        var template = SeedVmTemplate(context);
        var operation = new ApiOperation
        {
            Kind = "image-template.certify",
            Status = ApiOperationStatus.Running,
            ApiTokenId = Guid.NewGuid(),
            RouteKey = "certify",
            IdempotencyKey = "phase9-certification",
            RequestHash = new string('b', 64)
        };
        context.ImageTemplateCertificationJobs.Add(new ImageTemplateCertificationJob
        {
            Operation = operation,
            ImageTemplateId = template.Id,
            ActorUserId = Guid.NewGuid()
        });
        context.ImageDistributionRecords.Add(new ImageDistributionRecord
        {
            ImageTemplateId = template.Id,
            WorkerNodeId = node.Id,
            ImageHash = template.ImageHash!,
            ImageType = template.ImageType,
            Status = ImageDistributionStatus.Ready,
            References = [Reference(ImageDistributionReferenceKind.ImageCertification, template.Id)]
        });
        await context.SaveChangesAsync();

        await CreateService(context, new RecordingAgentClient())
            .ReconcileReferencesAsync(CancellationToken.None);

        Assert.Single(await context.ImageDistributionReferences.ToArrayAsync());
        Assert.Equal(ImageDistributionStatus.Ready,
            (await context.ImageDistributionRecords.SingleAsync()).Status);
    }

    static ImageDistributionService CreateService(AppDbContext context, RecordingAgentClient agent)
    {
        var registry = new DockerImageRegistryService(
            Options.Create(new DockerRegistrySettings { Address = "10.24.0.28:5000", Namespace = "ctf" }),
            BuildScopeFactory(context), agent, NullLogger<DockerImageRegistryService>.Instance);
        var httpFactory = new ServiceCollection().AddHttpClient().BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>();
        var vmRegistry = new Mock<VmImageRegistryService>(
            Options.Create(new DockerRegistrySettings { Address = "10.24.0.28:5000", Namespace = "ctf" }),
            new OciArtifactRegistryClient(httpFactory, NullLogger<OciArtifactRegistryClient>.Instance));
        vmRegistry.Setup(service => service.EnsureArtifactAsync(
                It.IsAny<ImageTemplate>(), It.IsAny<CancellationToken>()))
            .Returns<ImageTemplate, CancellationToken>((template, _) => Task.FromResult(
                new VmImageArtifactReference("10.24.0.28:5000",
                    $"ctf/gzctf/vm-template/{template.Id}", template.ImageHash!,
                    $"sha256:{template.ImageHash}")));
        vmRegistry.Setup(service => service.ArtifactExistsAsync(
                It.IsAny<ImageTemplate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var artifacts = new VmArtifactStore(
            Options.Create(new DockerRegistrySettings { Address = "10.24.0.28:5000", Namespace = "ctf" }),
            vmRegistry.Object, NullLogger<VmArtifactStore>.Instance);
        var writer = new EfOperationalEventWriter(context, NullLogger<EfOperationalEventWriter>.Instance);
        return new ImageDistributionService(context, agent, registry, artifacts, vmRegistry.Object,
            new ImageDistributionCoordinator(), new DeploymentExecutionContextAccessor(),
            writer, NullLogger<ImageDistributionService>.Instance);
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

    static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    sealed class RecordingAgentClient : AgentClient
    {
        public List<Guid> PulledDockerNodes { get; } = [];
        public List<Guid> DownloadedVmNodes { get; } = [];
        public List<Guid> DownloadedPreparedVmNodes { get; } = [];
        public List<Guid> DeletedVmNodes { get; } = [];
        public Exception? DockerPullException { get; set; }

        public RecordingAgentClient() : base(new Mock<IHttpClientFactory>().Object,
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(), NullLogger<AgentClient>.Instance)
        {
        }

        public override Task PullDockerImageAsync(Guid nodeId, string image, string? registryAuth,
            CancellationToken token)
        {
            if (DockerPullException is not null)
                throw DockerPullException;
            PulledDockerNodes.Add(nodeId);
            return Task.CompletedTask;
        }

        public override Task<AgentVmImageDownloadResult> DownloadVmImageAsync(Guid nodeId, int templateId,
            string hash, string? downloadUrl = null, long? expectedSize = null,
            CancellationToken token = default)
        {
            DownloadedVmNodes.Add(nodeId);
            return Task.FromResult(AgentVmImageDownloadResult.Ok(false, true, expectedSize, $"sha256:{hash}"));
        }

        public override Task<AgentVmImageDownloadResult> DownloadPreparedVmImageAsync(
            Guid nodeId,
            int templateId,
            string hash,
            long expectedSize,
            string registryAddress,
            string repository,
            string tag,
            CancellationToken token = default)
        {
            DownloadedPreparedVmNodes.Add(nodeId);
            return Task.FromResult(AgentVmImageDownloadResult.Ok(false, true, expectedSize, $"sha256:{hash}"));
        }

        public override Task DeleteVmImageAsync(Guid nodeId, int templateId, string hash, CancellationToken token)
        {
            DeletedVmNodes.Add(nodeId);
            return Task.CompletedTask;
        }
    }
}
