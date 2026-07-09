using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Concurrency;
using GZCTF.Services.Fleet;
using GZCTF.Services.Vm;
using GZCTF.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public class FleetVmServiceTests
{
    [Fact]
    public async Task CreateVmAsync_QueuesWhenNoKvmCapacityExists()
    {
        await using var context = CreateContext();
        var node = SeedKvmNode(context, maxVms: 1, currentVms: 1);
        context.Games.Add(new Game { Id = 2, Title = "vm-game" });
        context.GameChallenges.Add(new GameChallenge
        {
            Id = 9,
            GameId = 2,
            Title = "windows",
            Category = ChallengeCategory.Misc
        });
        await context.SaveChangesAsync();
        var queueState = new DeploymentQueueStateAccessor();
        var agent = new RecordingAgentClient();
        var service = CreateService(context, node, queueState, agent: agent);
        var vm = new VmInstance
        {
            Id = Guid.Parse("4b61fba5-f6a6-43cf-b3f4-4873c3d2d105"),
            ChallengeId = 9,
            UserId = Guid.Parse("09d1cd51-f835-47d8-8e34-4ca6d84c94f5"),
            VmName = "vm-c9-u1",
            Status = VmInstanceStatus.Creating
        };

        var result = await service.CreateVmAsync(vm, templateId: 3, templatePath: "/images/windows.qcow2",
            memory: 4096, cpu: 2, flag: "flag{vm-secret}", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(VmInstanceStatus.Creating, vm.Status);
        var queued = queueState.ConsumeQueued();
        Assert.NotNull(queued);
        Assert.Equal(DeploymentQueueKind.Vm, queued!.Kind);
        Assert.DoesNotContain("flag{vm-secret}", queued.ToString(), StringComparison.OrdinalIgnoreCase);

        var ticket = Assert.Single(context.DeploymentQueueTickets);
        Assert.Equal(DeploymentQueueKind.Vm, ticket.Kind);
        Assert.Equal(DeploymentQueueTicketStatus.Pending, ticket.Status);
        Assert.Equal(vm.Id, ticket.VmInstanceId);
        Assert.Equal("vm:2:09d1cd51-f835-47d8-8e34-4ca6d84c94f5:9:4b61fba5-f6a6-43cf-b3f4-4873c3d2d105",
            ticket.ActiveIdentity);
        Assert.Equal(1, ticket.VmSlots);
        Assert.Equal(1, context.WorkerNodes.Single(n => n.Id == node.Id).CurrentVms);
    }

    [Fact]
    public async Task ProcessPendingAsync_ExecutesVmTicket_WhenKvmCapacityBecomesAvailable()
    {
        await using var context = CreateContext();
        var node = SeedKvmNode(context, maxVms: 1, currentVms: 0, isLocal: false);
        var templatePath = CreateTempTemplateFile("windows-template");
        var templateHash = await ComputeSha256Async(templatePath);
        context.ImageTemplates.Add(new ImageTemplate
        {
            Id = 3,
            Name = "windows",
            OSType = OSType.Windows,
            ImageType = ImageType.Qcow2,
            LocalFilePath = templatePath,
            ImageHash = templateHash,
            FileSize = new FileInfo(templatePath).Length
        });
        context.Games.Add(new Game { Id = 2, Title = "vm-game" });
        context.GameChallenges.Add(new GameChallenge
        {
            Id = 9,
            GameId = 2,
            Title = "windows",
            Category = ChallengeCategory.Misc,
            Environment = EnvironmentType.WindowsVM,
            ImageTemplateId = 3,
            MemoryLimit = 4096,
            CPUCount = 2
        });
        var vm = new VmInstance
        {
            Id = Guid.Parse("4b61fba5-f6a6-43cf-b3f4-4873c3d2d105"),
            ChallengeId = 9,
            UserId = Guid.Parse("09d1cd51-f835-47d8-8e34-4ca6d84c94f5"),
            VmName = "vm-c9-u1",
            ProviderName = "KVM",
            OSType = OSType.Windows,
            Status = VmInstanceStatus.Creating
        };
        context.VmInstances.Add(vm);
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.Vm(
            gameId: 2,
            userId: vm.UserId,
            challengeId: 9,
            vmInstanceId: vm.Id));
        ticket.DeploymentTarget = new DeploymentTarget
        {
            Type = TargetType.Vm,
            Action = TargetAction.Create,
            Status = TargetStatus.Pending,
            Payload = "{\"Flag\":\"flag{must-not-be-used}\"}"
        };
        context.DeploymentQueueTickets.Add(ticket);
        await context.SaveChangesAsync();
        var queueState = new DeploymentQueueStateAccessor();
        var executionContext = new DeploymentExecutionContextAccessor();
        var agent = new RecordingAgentClient();
        var service = CreateService(context, node, queueState, executionContext, agent: agent);
        var queue = CreateQueueManager(context, new DeploymentExecutionService(
            context,
            service,
            executionContext,
            NullLogger<DeploymentExecutionService>.Instance));

        try
        {
            var processed = await queue.ProcessPendingAsync(CancellationToken.None);

            Assert.Equal(1, processed);
            Assert.Equal(DeploymentQueueTicketStatus.Completed, ticket.Status);
            Assert.Equal(TargetStatus.Completed, ticket.DeploymentTarget.Status);
            Assert.Equal(VmInstanceStatus.Running, vm.Status);
            Assert.Equal(node.Id, vm.NodeId);
            Assert.Equal(1, context.WorkerNodes.Single(n => n.Id == node.Id).CurrentVms);
            Assert.Equal(node.Id, agent.DownloadVmImageNodeId);
            Assert.Equal(3, agent.DownloadVmImageTemplateId);
            Assert.Equal(templateHash, agent.DownloadVmImageHash);
            Assert.DoesNotContain("flag{must-not-be-used}", ticket.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(templatePath);
        }
    }

    [Fact]
    public async Task CreateVmAsync_DownloadsRemoteTemplateBeforeCreatingVm()
    {
        await using var context = CreateContext();
        var node = SeedKvmNode(context, maxVms: 1, currentVms: 0, isLocal: false);
        var templatePath = CreateTempTemplateFile("windows-template");
        var templateHash = await ComputeSha256Async(templatePath);
        context.ImageTemplates.Add(new ImageTemplate
        {
            Id = 3,
            Name = "windows",
            OSType = OSType.Windows,
            ImageType = ImageType.Qcow2,
            LocalFilePath = templatePath,
            ImageHash = templateHash,
            FileSize = new FileInfo(templatePath).Length
        });
        context.Games.Add(new Game { Id = 2, Title = "vm-game" });
        context.GameChallenges.Add(new GameChallenge
        {
            Id = 9,
            GameId = 2,
            Title = "windows",
            Category = ChallengeCategory.Misc,
            Environment = EnvironmentType.WindowsVM,
            ImageTemplateId = 3
        });
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient();
        var service = CreateService(context, node, new DeploymentQueueStateAccessor(), agent: agent);
        var vm = new VmInstance
        {
            Id = Guid.Parse("4b61fba5-f6a6-43cf-b3f4-4873c3d2d105"),
            ChallengeId = 9,
            UserId = Guid.Parse("09d1cd51-f835-47d8-8e34-4ca6d84c94f5"),
            VmName = "vm-c9-u1",
            ProviderName = "KVM",
            OSType = OSType.Windows,
            Status = VmInstanceStatus.Creating
        };

        try
        {
            var result = await service.CreateVmAsync(vm, templateId: 3, templatePath: null,
                memory: 4096, cpu: 2, flag: "flag{vm-secret}", CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(node.Id, agent.DownloadVmImageNodeId);
            Assert.Equal(3, agent.DownloadVmImageTemplateId);
            Assert.Equal(templateHash, agent.DownloadVmImageHash);
            Assert.Equal(
                $"http://10.24.0.28:5000/v2/ctf/gzctf/vm-template/3/blobs/sha256:{templateHash}",
                agent.DownloadVmImageUrl);
            Assert.True(agent.CreateVmCalledAfterDownload);
        }
        finally
        {
            File.Delete(templatePath);
        }
    }

    [Fact]
    public async Task CreateVmAsync_UsesRegistryMetadataWithoutReadingLocalTemplateOnStartup()
    {
        await using var context = CreateContext();
        var node = SeedKvmNode(context, maxVms: 1, currentVms: 0, isLocal: false);
        var templateHash = new string('a', 64);
        context.ImageTemplates.Add(new ImageTemplate
        {
            Id = 3,
            Name = "windows",
            OSType = OSType.Windows,
            ImageType = ImageType.Qcow2,
            LocalFilePath = "D:/missing/windows-template.qcow2",
            ImageHash = templateHash,
            FileSize = 6_040_518_656
        });
        context.Games.Add(new Game { Id = 2, Title = "vm-game" });
        context.GameChallenges.Add(new GameChallenge
        {
            Id = 9,
            GameId = 2,
            Title = "windows",
            Category = ChallengeCategory.Misc,
            Environment = EnvironmentType.WindowsVM,
            ImageTemplateId = 3
        });
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient();
        var service = CreateService(context, node, new DeploymentQueueStateAccessor(), agent: agent);
        var vm = new VmInstance
        {
            Id = Guid.Parse("4b61fba5-f6a6-43cf-b3f4-4873c3d2d105"),
            ChallengeId = 9,
            UserId = Guid.Parse("09d1cd51-f835-47d8-8e34-4ca6d84c94f5"),
            VmName = "vm-c9-u1",
            ProviderName = "KVM",
            OSType = OSType.Windows,
            Status = VmInstanceStatus.Creating
        };

        var result = await service.CreateVmAsync(vm, templateId: 3, templatePath: null,
            memory: 4096, cpu: 2, flag: "flag{vm-secret}", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(node.Id, agent.DownloadVmImageNodeId);
        Assert.Equal(3, agent.DownloadVmImageTemplateId);
        Assert.Equal(templateHash, agent.DownloadVmImageHash);
        Assert.Equal(
            $"http://10.24.0.28:5000/v2/ctf/gzctf/vm-template/3/blobs/sha256:{templateHash}",
            agent.DownloadVmImageUrl);
        Assert.True(agent.CreateVmCalledAfterDownload);
    }

    [Fact]
    public async Task CreateVmAsync_DoesNotCreateRemoteVm_WhenTemplateDownloadFails()
    {
        await using var context = CreateContext();
        var node = SeedKvmNode(context, maxVms: 1, currentVms: 0, isLocal: false);
        var templatePath = CreateTempTemplateFile("windows-template");
        var templateHash = await ComputeSha256Async(templatePath);
        context.ImageTemplates.Add(new ImageTemplate
        {
            Id = 3,
            Name = "windows",
            OSType = OSType.Windows,
            ImageType = ImageType.Qcow2,
            LocalFilePath = templatePath,
            ImageHash = templateHash,
            FileSize = new FileInfo(templatePath).Length
        });
        context.Games.Add(new Game { Id = 2, Title = "vm-game" });
        context.GameChallenges.Add(new GameChallenge
        {
            Id = 9,
            GameId = 2,
            Title = "windows",
            Category = ChallengeCategory.Misc,
            Environment = EnvironmentType.WindowsVM,
            ImageTemplateId = 3
        });
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient { DownloadSucceeds = false };
        var service = CreateService(context, node, new DeploymentQueueStateAccessor(), agent: agent);
        var vm = new VmInstance
        {
            Id = Guid.Parse("4b61fba5-f6a6-43cf-b3f4-4873c3d2d105"),
            ChallengeId = 9,
            UserId = Guid.Parse("09d1cd51-f835-47d8-8e34-4ca6d84c94f5"),
            VmName = "vm-c9-u1",
            ProviderName = "KVM",
            OSType = OSType.Windows,
            Status = VmInstanceStatus.Creating
        };

        try
        {
            var result = await service.CreateVmAsync(vm, templateId: 3, templatePath: null,
                memory: 4096, cpu: 2, flag: "flag{vm-secret}", CancellationToken.None);

            Assert.Null(result);
            Assert.Equal(VmInstanceStatus.Error, vm.Status);
            Assert.Equal(node.Id, agent.DownloadVmImageNodeId);
            Assert.False(agent.CreateVmCalledAfterDownload);
            Assert.Equal(0, context.WorkerNodes.Single(n => n.Id == node.Id).ReservedVms);
            Assert.Equal(0, context.WorkerNodes.Single(n => n.Id == node.Id).CurrentVms);
        }
        finally
        {
            File.Delete(templatePath);
        }
    }

    [Fact]
    public async Task CreateVmAsync_DoesNotRehashLocalTemplate_WhenTemplateHashDiffersFromLocalFile()
    {
        await using var context = CreateContext();
        var node = SeedKvmNode(context, maxVms: 1, currentVms: 0, isLocal: false);
        var templatePath = CreateTempTemplateFile("real-template-content");
        context.ImageTemplates.Add(new ImageTemplate
        {
            Id = 3,
            Name = "windows",
            OSType = OSType.Windows,
            ImageType = ImageType.Qcow2,
            LocalFilePath = templatePath,
            ImageHash = new string('0', 64),
            FileSize = new FileInfo(templatePath).Length
        });
        context.Games.Add(new Game { Id = 2, Title = "vm-game" });
        context.GameChallenges.Add(new GameChallenge
        {
            Id = 9,
            GameId = 2,
            Title = "windows",
            Category = ChallengeCategory.Misc,
            Environment = EnvironmentType.WindowsVM,
            ImageTemplateId = 3
        });
        await context.SaveChangesAsync();
        var agent = new RecordingAgentClient();
        var service = CreateService(context, node, new DeploymentQueueStateAccessor(), agent: agent);
        var vm = new VmInstance
        {
            Id = Guid.Parse("4b61fba5-f6a6-43cf-b3f4-4873c3d2d105"),
            ChallengeId = 9,
            UserId = Guid.Parse("09d1cd51-f835-47d8-8e34-4ca6d84c94f5"),
            VmName = "vm-c9-u1",
            ProviderName = "KVM",
            OSType = OSType.Windows,
            Status = VmInstanceStatus.Creating
        };

        try
        {
            var result = await service.CreateVmAsync(vm, templateId: 3, templatePath: null,
                memory: 4096, cpu: 2, flag: "flag{vm-secret}", CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(node.Id, agent.DownloadVmImageNodeId);
            Assert.Equal(new string('0', 64), agent.DownloadVmImageHash);
            Assert.True(agent.CreateVmCalledAfterDownload);
        }
        finally
        {
            File.Delete(templatePath);
        }
    }

    [Fact]
    public async Task DestroyVmAsync_RemovesGuacamoleConnectionAndClearsAccessFields()
    {
        await using var context = CreateContext();
        var node = SeedKvmNode(context, maxVms: 2, currentVms: 1);
        var vm = new VmInstance
        {
            Id = Guid.Parse("4b61fba5-f6a6-43cf-b3f4-4873c3d2d105"),
            ChallengeId = 9,
            UserId = Guid.Parse("09d1cd51-f835-47d8-8e34-4ca6d84c94f5"),
            VmName = "vm-c9-u1",
            NodeId = node.Id,
            Status = VmInstanceStatus.Running,
            GuacamoleConnectionId = "conn-1",
            RdpUrl = "http://guac/#/client/conn-1",
            IpAddress = "10.24.0.30"
        };
        var guacamole = new RecordingGuacamoleService();
        var service = CreateService(context, node, new DeploymentQueueStateAccessor(), guacamole: guacamole);

        await service.DestroyVmAsync(vm, CancellationToken.None);

        Assert.Equal(VmInstanceStatus.Destroyed, vm.Status);
        Assert.Contains("conn-1", guacamole.DeletedConnections);
        Assert.Null(vm.GuacamoleConnectionId);
        Assert.Null(vm.RdpUrl);
    }

    static FleetVmService CreateService(AppDbContext context, WorkerNode node,
        DeploymentQueueStateAccessor queueState,
        DeploymentExecutionContextAccessor? executionContext = null,
        GuacamoleService? guacamole = null,
        AgentClient? agent = null)
    {
        var nodeRepo = new Mock<INodeRepository>();
        nodeRepo.Setup(r => r.GetNodeByIdAsync(node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => context.WorkerNodes.First(n => n.Id == node.Id));
        nodeRepo.Setup(r => r.GetOnlineNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => context.WorkerNodes.ToList());

        var lockService = new LocalSemaphoreLock(NullLogger<LocalSemaphoreLock>.Instance);
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton<IDistributedLockService>(lockService);
        services.AddScoped(_ => new FleetCapacityReservationService(
            context,
            lockService,
            NullLogger<FleetCapacityReservationService>.Instance));
        services.AddScoped(_ => new DeploymentQueueService(
            context,
            NullLogger<DeploymentQueueService>.Instance));
        services.AddSingleton<DeploymentExecutionService>(
            new DeploymentExecutionService(context, NullLogger<DeploymentExecutionService>.Instance));
        services.AddSingleton(new NodeExecutionGate(
            new NodeExecutionGateOptions(),
            NullLogger<NodeExecutionGate>.Instance));
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var queue = new QueueManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            lockService,
            provider.GetRequiredService<NodeExecutionGate>(),
            NullLogger<QueueManager>.Instance);
        var capacity = new FleetCapacityReservationService(context, lockService,
            NullLogger<FleetCapacityReservationService>.Instance);
        var queueService = new DeploymentQueueService(context, NullLogger<DeploymentQueueService>.Instance);
        var fleet = new FleetManager(
            queue,
            nodeRepo.Object,
            context,
            capacity,
            queueService,
            NullLogger<FleetManager>.Instance);

        return new FleetVmService(
            fleet,
            agent ?? CreateAgentClientMock(),
            nodeRepo.Object,
            new RecordingVmProvider(),
            guacamole ?? new RecordingGuacamoleService(),
            CreateImageDistributionService(context, agent as RecordingAgentClient),
            Options.Create(new KvmSettings()),
            context,
            queueState,
            executionContext ?? new DeploymentExecutionContextAccessor(),
            NullLogger<FleetVmService>.Instance);
    }

    static AgentClient CreateAgentClientMock()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INodeRepository>(new Mock<INodeRepository>().Object);
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        return new Mock<AgentClient>(
            factory.Object,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<AgentClient>.Instance)
        { CallBase = true }.Object;
    }

    static ImageDistributionService CreateImageDistributionService(AppDbContext context, RecordingAgentClient? agent)
    {
        var agentClient = agent ?? new RecordingAgentClient();
        var registryService = new DockerImageRegistryService(
            Options.Create(new DockerRegistrySettings
            {
                Address = "10.24.0.28:5000",
                Namespace = "ctf"
            }),
            new ServiceCollection().AddSingleton(context).AddLogging().BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            agentClient,
            NullLogger<DockerImageRegistryService>.Instance);

        var registry = new Mock<VmImageRegistryService>(
            Options.Create(new DockerRegistrySettings
            {
                Address = "10.24.0.28:5000",
                Namespace = "ctf"
            }),
            new ServiceCollection().AddHttpClient().BuildServiceProvider().GetRequiredService<IHttpClientFactory>(),
            NullLogger<VmImageRegistryService>.Instance);
        registry
            .Setup(r => r.EnsureArtifactAsync(It.IsAny<ImageTemplate>(), It.IsAny<CancellationToken>()))
            .Returns<ImageTemplate, CancellationToken>((template, _) => Task.FromResult(
                new VmImageArtifactReference(
                    "10.24.0.28:5000",
                    $"ctf/gzctf/vm-template/{template.Id}",
                    template.ImageHash!,
                    $"sha256:{template.ImageHash}")));

        var artifacts = new VmArtifactStore(
            Options.Create(new DockerRegistrySettings
            {
                Address = "10.24.0.28:5000",
                Namespace = "ctf"
            }),
            registry.Object,
            NullLogger<VmArtifactStore>.Instance);

        return new ImageDistributionService(
            context,
            agentClient,
            registryService,
            artifacts,
            NullLogger<ImageDistributionService>.Instance);
    }

    static QueueManager CreateQueueManager(AppDbContext context, DeploymentExecutionService executor)
    {
        var lockService = new LocalSemaphoreLock(NullLogger<LocalSemaphoreLock>.Instance);
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton<IDistributedLockService>(lockService);
        services.AddScoped(_ => new FleetCapacityReservationService(
            context,
            lockService,
            NullLogger<FleetCapacityReservationService>.Instance));
        services.AddSingleton(executor);
        services.AddSingleton(new NodeExecutionGate(
            new NodeExecutionGateOptions(),
            NullLogger<NodeExecutionGate>.Instance));
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        return new QueueManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            lockService,
            provider.GetRequiredService<NodeExecutionGate>(),
            NullLogger<QueueManager>.Instance);
    }

    static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    static string CreateTempTemplateFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.qcow2");
        File.WriteAllText(path, content);
        return path;
    }

    static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    static WorkerNode SeedKvmNode(AppDbContext context, int maxVms, int currentVms, bool isLocal = true)
    {
        var node = new WorkerNode
        {
            Id = Guid.NewGuid(),
            Name = "kvm-node",
            HostAddress = "10.24.0.30",
            Status = NodeStatus.Online,
            IsSchedulable = true,
            IsLocal = isLocal,
            LastHeartbeat = DateTimeOffset.UtcNow,
            Capabilities = NodeCapability.Kvm,
            MaxContainers = 0,
            MaxVms = maxVms,
            CurrentVms = currentVms
        };

        context.WorkerNodes.Add(node);
        context.SaveChanges();
        return node;
    }

    sealed class RecordingAgentClient : AgentClient
    {
        public RecordingAgentClient() : base(
            new ServiceCollection().AddHttpClient().BuildServiceProvider().GetRequiredService<IHttpClientFactory>(),
            new ServiceCollection().AddSingleton<INodeRepository>(new Mock<INodeRepository>().Object)
                .BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<AgentClient>.Instance)
        {
        }

        public Guid? DownloadVmImageNodeId { get; private set; }
        public int? DownloadVmImageTemplateId { get; private set; }
        public string? DownloadVmImageHash { get; private set; }
        public string? DownloadVmImageUrl { get; private set; }
        public bool CreateVmCalledAfterDownload { get; private set; }
        public bool DownloadSucceeds { get; set; } = true;
        bool _downloaded;

        public override Task<AgentVmImageDownloadResult> DownloadVmImageAsync(Guid nodeId, int templateId, string hash,
            string? downloadUrl = null, long? expectedSize = null, CancellationToken token = default)
        {
            DownloadVmImageNodeId = nodeId;
            DownloadVmImageTemplateId = templateId;
            DownloadVmImageHash = hash;
            DownloadVmImageUrl = downloadUrl;
            if (!DownloadSucceeds)
                return Task.FromResult(AgentVmImageDownloadResult.Failed("registry unavailable"));
            _downloaded = true;
            return Task.FromResult(new AgentVmImageDownloadResult(true, "downloaded", false, true, 1024, hash));
        }

        public override Task<AgentCreateVmResponse?> CreateVmAsync(Guid nodeId, AgentCreateVmRequest request,
            CancellationToken token)
        {
            CreateVmCalledAfterDownload = _downloaded;
            return Task.FromResult<AgentCreateVmResponse?>(new AgentCreateVmResponse
            {
                VmName = request.VmName,
                Status = "Running"
            });
        }
    }

    sealed class RecordingVmProvider : IVirtualMachineProvider
    {
        public string ProviderName => "test";
        public OSType SupportedOSType => OSType.Windows;

        public Task<VmOperationResult> CreateFromTemplateAsync(string templatePath, string vmName,
            int? memoryMb = null, int? cpuCount = null, CancellationToken token = default) =>
            Task.FromResult(VmOperationResult.Ok(vmName));

        public Task<VmOperationResult> StartAsync(string vmName, CancellationToken token) =>
            Task.FromResult(VmOperationResult.Ok(vmName));

        public Task<VmOperationResult> ShutdownAsync(string vmName, CancellationToken token) =>
            Task.FromResult(VmOperationResult.Ok(vmName));

        public Task<VmOperationResult> DestroyAsync(string vmName, CancellationToken token) =>
            Task.FromResult(VmOperationResult.Ok(vmName));

        public Task<VmOperationResult> CreateSnapshotAsync(string vmName, string snapshotName,
            CancellationToken token) =>
            Task.FromResult(VmOperationResult.Ok(vmName));

        public Task<VmOperationResult> SnapshotRevertAsync(string vmName, CancellationToken token) =>
            Task.FromResult(VmOperationResult.Ok(vmName));

        public Task<VmConnectionInfo?> GetConnectionInfoAsync(string vmName, CancellationToken token) =>
            Task.FromResult<VmConnectionInfo?>(null);

        public Task<string?> GetIpAddressAsync(string vmName, CancellationToken token) =>
            Task.FromResult<string?>(null);

        public Task<bool> IsRunningAsync(string vmName, CancellationToken token) =>
            Task.FromResult(true);
    }

    sealed class RecordingGuacamoleService : GuacamoleService
    {
        public RecordingGuacamoleService()
            : base(new TestHttpClientFactory(),
                Options.Create(new GuacamoleSettings { GuacamoleAuthToken = "token" }),
                NullLogger<GuacamoleService>.Instance)
        {
        }

        public List<string> DeletedConnections { get; } = [];

        public override Task<bool> DeleteConnectionAsync(string connectionId, CancellationToken token = default)
        {
            DeletedConnections.Add(connectionId);
            return Task.FromResult(true);
        }
    }

    sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
