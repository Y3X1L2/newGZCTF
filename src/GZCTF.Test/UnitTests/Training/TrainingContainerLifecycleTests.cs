using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Container.Manager;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using TaskStatus = GZCTF.Utils.TaskStatus;

namespace GZCTF.Test.UnitTests.Training;

public sealed class TrainingContainerLifecycleTests
{
    [Fact]
    public async Task ThreeChapterLabs_CoexistWithoutEvictingEarlierInstances()
    {
        await using var fixture = new Fixture();
        var labs = await fixture.AddInstances(3, training: true);
        foreach (var lab in labs)
            Assert.Equal(TaskStatus.Success, (await fixture.Create(lab)).Status);

        Assert.Equal(3, labs.Select(lab => lab.ContainerId).Distinct().Count());
        Assert.All(labs, lab => Assert.NotNull(lab.Container));
        fixture.Containers.VerifyNoOtherCalls();
        Assert.Equal(labs[0].ContainerId, (await fixture.Create(labs[0])).Result?.Id);
        Assert.Equal(3, fixture.Created.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrainingLimit_DeniesNewLabWithoutEviction(bool autoDestroy)
    {
        await using var fixture = new Fixture(new ContainerPolicy { AutoDestroyOnLimitReached = autoDestroy }, trainingLimit: 1);
        var labs = await fixture.AddInstances(2, training: true);
        await fixture.Create(labs[0]);
        Assert.Equal(TaskStatus.Denied, (await fixture.Create(labs[1])).Status);
        Assert.NotNull(labs[0].Container);
        Assert.Null(labs[1].Container);
        fixture.Containers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublicAndTrainingLimits_AreIndependentAndPublicHonorsDisabledEviction()
    {
        await using var fixture = new Fixture();
        var publicLabs = await fixture.AddInstances(2, training: false);
        var courseLabs = await fixture.AddInstances(1, training: true);
        Assert.Equal(TaskStatus.Success, (await fixture.Create(courseLabs[0])).Status);
        Assert.Equal(TaskStatus.Success, (await fixture.Create(publicLabs[0])).Status);
        Assert.Equal(TaskStatus.Denied, (await fixture.Create(publicLabs[1])).Status);
        fixture.Containers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnabledPublicEviction_NeverTargetsTrainingContainer()
    {
        await using var fixture = new Fixture(new ContainerPolicy { AutoDestroyOnLimitReached = true });
        var courseLabs = await fixture.AddInstances(1, training: true);
        var publicLabs = await fixture.AddInstances(2, training: false);
        await fixture.Create(courseLabs[0]);
        await fixture.Create(publicLabs[0]);
        var oldPublic = publicLabs[0].Container!;
        fixture.Containers.Setup(repository => repository.DestroyContainer(oldPublic, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Assert.Equal(TaskStatus.Success, (await fixture.Create(publicLabs[1])).Status);
        Assert.NotNull(courseLabs[0].Container);
        fixture.Containers.Verify(repository => repository.DestroyContainer(oldPublic, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Containers.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(RuntimeOperationKind.Create, DeploymentQueueTicketStatus.Pending, false, TaskStatus.Denied)]
    [InlineData(RuntimeOperationKind.Create, DeploymentQueueTicketStatus.Running, true, TaskStatus.Success)]
    [InlineData(RuntimeOperationKind.Extend, DeploymentQueueTicketStatus.Pending, false, TaskStatus.Success)]
    [InlineData(RuntimeOperationKind.Create, DeploymentQueueTicketStatus.Succeeded, false, TaskStatus.Success)]
    public async Task TrainingQuota_CountsOnlyUnmaterializedActiveCreates(
        RuntimeOperationKind operation, DeploymentQueueTicketStatus status, bool materialized, TaskStatus expected)
    {
        await using var fixture = new Fixture(trainingLimit: materialized ? 2 : 1);
        var labs = await fixture.AddInstances(2, training: true);
        if (materialized) await fixture.Create(labs[0]);
        var ticket = DeploymentQueueTicket.Create(DeploymentQueueRequest.TrainingContainer(fixture.User.Id, labs[0].ExerciseId)
            with { Operation = operation });
        ticket.Status = status;
        fixture.Context.Add(ticket);
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(expected, (await fixture.Create(labs[1])).Status);
        fixture.Containers.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task LoadedDynamicInstance_RepairsMissingFlagBeforeInjection(string? oldFlag)
    {
        await using var fixture = new Fixture();
        var labs = await fixture.AddInstances(2, training: true);
        foreach (var lab in labs)
        {
            lab.IsLoaded = true;
            lab.Exercise.FlagTemplate = null;
            if (oldFlag is not null) lab.FlagContext = new FlagContext { Flag = oldFlag, IsOccupied = true };
        }
        await fixture.Context.SaveChangesAsync();
        foreach (var lab in labs) await fixture.Create(lab);
        Assert.All(fixture.Created, config => Assert.False(string.IsNullOrWhiteSpace(config.Flag)));
        Assert.True(fixture.Created[0].Flag != fixture.Created[1].Flag);
        foreach (var lab in labs)
        {
            var persisted = await fixture.Context.FlagContexts.AsNoTracking().SingleAsync(flag => flag.Id == lab.FlagId);
            Assert.True(persisted.Flag == fixture.Created.Single(config => config.ChallengeId == lab.ExerciseId).Flag);
            Assert.Null(persisted.ExerciseId);
        }
        var originalFlagId = labs[0].FlagId;
        await fixture.Create(labs[0]);
        Assert.Equal(originalFlagId, labs[0].FlagId);
        Assert.Equal(2, fixture.Created.Count);
    }

    [Fact]
    public async Task QueueAdmission_ReservesTrainingQuotaAndDeduplicatesRetries()
    {
        await using var fixture = new Fixture(trainingLimit: 2);
        var labs = await fixture.AddInstances(3, training: true);
        Assert.IsType<QueuedTaskResult<Container>>(await fixture.Enqueue(labs[0]));
        Assert.IsType<QueuedTaskResult<Container>>(await fixture.Enqueue(labs[0]));
        Assert.IsType<QueuedTaskResult<Container>>(await fixture.Enqueue(labs[1]));
        Assert.Equal(TaskStatus.Denied, (await fixture.Enqueue(labs[2])).Status);
        Assert.Equal(2, await fixture.Context.DeploymentQueueTickets.CountAsync());
        Assert.Empty(fixture.Created);
        fixture.Containers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExistingDynamicFlag_IsPreservedAndStaticFlagIsNotInjected()
    {
        await using var fixture = new Fixture();
        var labs = await fixture.AddInstances(2, training: true);
        var expected = Guid.NewGuid().ToString();
        labs[0].FlagContext = FlagContext.CreateInstanceFlag(expected);
        labs[1].Exercise.Type = ChallengeType.StaticContainer;
        await fixture.Context.SaveChangesAsync();
        await fixture.Create(labs[0]);
        await fixture.Create(labs[1]);
        Assert.True(fixture.Created[0].Flag == expected);
        Assert.Null(fixture.Created[1].Flag);
    }

    sealed class Fixture : IAsyncDisposable
    {
        public AppDbContext Context { get; } = new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        public UserInfo User { get; } = new() { Id = Guid.NewGuid(), UserName = "training-regression" };
        public Mock<IContainerRepository> Containers { get; } = new(MockBehavior.Strict);
        public List<ContainerConfig> Created { get; } = [];
        readonly ExerciseInstanceRepository _repository;
        readonly DeploymentExecutionContextAccessor _execution = new();

        public Fixture(ContainerPolicy? policy = null, int trainingLimit = 3)
        {
            var manager = new Mock<IContainerManager>();
            manager.Setup(service => service.CreateContainerAsync(It.IsAny<ContainerConfig>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ContainerConfig config, CancellationToken _) =>
                {
                    Created.Add(config);
                    var container = new Container { Id = Guid.NewGuid(), ContainerId = Guid.NewGuid().ToString(),
                        Image = config.Image, Status = ContainerStatus.Running };
                    Context.Containers.Add(container);
                    return container;
                });
            var options = new Mock<IOptionsSnapshot<ContainerPolicy>>();
            options.SetupGet(item => item.Value).Returns(policy ?? new ContainerPolicy());
            var trainingOptions = new Mock<IOptionsSnapshot<TrainingContainerPolicy>>();
            trainingOptions.SetupGet(item => item.Value)
                .Returns(new TrainingContainerPolicy { MaxContainerCountPerUser = trainingLimit });
            var registry = new Mock<DockerImageRegistryService>(Options.Create(new DockerRegistrySettings()),
                null!, null!, NullLogger<DockerImageRegistryService>.Instance, null!);
            registry.Setup(service => service.ResolveImageReferenceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string image, CancellationToken _) => image);
            _repository = new ExerciseInstanceRepository(Context, null!, manager.Object, Containers.Object,
                options.Object, trainingOptions.Object, registry.Object, Mock.Of<INginxProxySyncService>(),
                new DeploymentQueueService(Context, NullLogger<DeploymentQueueService>.Instance), _execution,
                new LocalDevelopmentLeaseProvider(), NullLogger<ExerciseInstanceRepository>.Instance,
                Mock.Of<IStringLocalizer<Program>>());
        }

        public async Task<ExerciseInstance[]> AddInstances(int count, bool training)
        {
            var labs = Enumerable.Range(0, count).Select(_ => new ExerciseInstance
            {
                User = User, IsLoaded = true,
                Exercise = new ExerciseChallenge { Title = "regression lab", Content = "", IsEnabled = true,
                    Type = ChallengeType.DynamicContainer, TrainingCourseId = training ? 32 : null,
                    ContainerImage = "example.test/lab:latest", ExposePort = 80 }
            }).ToArray();
            Context.AddRange(labs);
            await Context.SaveChangesAsync();
            return labs;
        }

        public async Task<TaskResult<Container>> Create(ExerciseInstance instance)
        {
            using var execution = _execution.Push(new DeploymentExecutionContext(Guid.NewGuid(), true, Guid.NewGuid()));
            return await _repository.CreateContainer(instance, User);
        }

        public Task<TaskResult<Container>> Enqueue(ExerciseInstance instance) =>
            _repository.CreateContainer(instance, User);

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
