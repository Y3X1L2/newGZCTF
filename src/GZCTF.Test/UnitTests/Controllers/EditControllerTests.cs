using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Controllers;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Cache;
using GZCTF.Services.Container.Manager;
using GZCTF.Services.Fleet;
using GZCTF.Services.Transfer;
using GZCTF.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Controllers;

public class EditControllerTests
{
    [Fact]
    public async Task CreateTestContainer_ResolvesManagedDockerImageBeforeCreatingContainer()
    {
        var challenge = new GameChallenge
        {
            Id = 11,
            GameId = 3,
            Title = "web",
            Type = ChallengeType.StaticContainer,
            Environment = EnvironmentType.Docker,
            ContainerImage = "10.24.0.28:5000/ctf/web/demo:latest",
            ExposePort = 80
        };
        var challengeRepository = new Mock<IGameChallengeRepository>();
        challengeRepository
            .Setup(r => r.GetChallenge(3, 11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);
        var containerManager = new RecordingContainerManager();
        var controller = CreateController(challengeRepository.Object, containerManager,
            registryAddress: "10.24.0.99:5000");

        var result = await controller.CreateTestContainer(3, 11, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(containerManager.LastConfig);
        Assert.Equal("10.24.0.99:5000/ctf/web/demo:latest", containerManager.LastConfig!.Image);
    }

    private static EditController CreateController(IGameChallengeRepository challengeRepository,
        RecordingContainerManager containerManager, string registryAddress)
    {
        var context = CreateContext();
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var dockerRegistry = new DockerImageRegistryService(
            Options.Create(new DockerRegistrySettings { Address = registryAddress }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            new AgentClient(new StaticHttpClientFactory(), provider.GetRequiredService<IServiceScopeFactory>(),
                new ConfigurationBuilder().Build(), NullLogger<AgentClient>.Instance),
            NullLogger<DockerImageRegistryService>.Instance);
        var userManager = CreateUserManager();
        userManager
            .Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new UserInfo { Id = Guid.CreateVersion7(), UserName = "teacher" });

        var controller = new EditController(
            null!,
            userManager.Object,
            NullLogger<EditController>.Instance,
            Mock.Of<IPostRepository>(),
            Mock.Of<IContainerRepository>(),
            challengeRepository,
            Mock.Of<IGameInstanceRepository>(),
            Mock.Of<IGameNoticeRepository>(),
            Mock.Of<IGameRepository>(),
            context,
            containerManager,
            Mock.Of<INginxProxySyncService>(s =>
                s.TrySyncNowAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.CompletedTask),
            Mock.Of<IBlobRepository>(),
            null!,
            null!,
            Mock.Of<IDivisionRepository>(),
            Mock.Of<IStringLocalizer<Program>>(),
            dockerRegistry,
            provider.GetRequiredService<IServiceScopeFactory>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())
                    ], "test"))
                }
            }
        };

        return controller;
    }

    private static Mock<UserManager<UserInfo>> CreateUserManager()
    {
        var store = new Mock<IUserStore<UserInfo>>();
        return new Mock<UserManager<UserInfo>>(
            store.Object,
            null!,
            null!,
            Array.Empty<IUserValidator<UserInfo>>(),
            Array.Empty<IPasswordValidator<UserInfo>>(),
            null!,
            null!,
            null!,
            null!);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private sealed class RecordingContainerManager : IContainerManager
    {
        public ContainerConfig? LastConfig { get; private set; }

        public Task<Container?> CreateContainerAsync(ContainerConfig config, CancellationToken token = default)
        {
            LastConfig = config;
            return Task.FromResult<Container?>(new Container
            {
                Id = Guid.CreateVersion7(),
                Image = config.Image,
                ContainerId = "test-container",
                Status = ContainerStatus.Running,
                IP = "127.0.0.1",
                Port = config.ExposedPort,
                StartedAt = DateTimeOffset.UtcNow
            });
        }

        public Task DestroyContainerAsync(Container container, CancellationToken token = default) =>
            Task.CompletedTask;
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
