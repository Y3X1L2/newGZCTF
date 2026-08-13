using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Controllers;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Content.Application;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Infrastructure.Cache;
using GZCTF.Services.Container.Manager;
using GZCTF.Services.Fleet;
using GZCTF.Services.Transfer;
using GZCTF.Utils;
using GZCTF.Modules.Exercise.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
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
    public async Task CreateTestContainer_QueuesDockerRuntimeOperation()
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
        var (controller, context) = CreateController(challengeRepository.Object);

        var result = await controller.CreateTestContainer(3, 11, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var status = Assert.IsType<DeploymentQueueStatusModel>(accepted.Value);
        Assert.Equal(DeploymentQueueKind.ChallengeTestContainer, status.Kind);
        var ticket = await context.DeploymentQueueTickets.SingleAsync();
        Assert.Equal(RuntimeOperationKind.Create, ticket.Operation);
        Assert.Equal("challenge-test-container:3:11", ticket.SubjectConcurrencyKey);
        Assert.Equal(1, ticket.DockerSlots);
    }

    private static (EditController Controller, AppDbContext Context) CreateController(
        IGameChallengeRepository challengeRepository)
    {
        var context = CreateContext();
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var queue = new DeploymentQueueService(context, NullLogger<DeploymentQueueService>.Instance);
        var userManager = CreateUserManager();
        var actorId = Guid.CreateVersion7();
        userManager
            .Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new UserInfo { Id = actorId, UserName = "teacher" });
        var gameRepository = new Mock<IGameRepository>();
        gameRepository
            .Setup(repository => repository.GetGameById(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Game { Id = 3, OwnerId = actorId });

        var controller = new EditController(
            null!,
            userManager.Object,
            NullLogger<EditController>.Instance,
            Mock.Of<IPostRepository>(),
            challengeRepository,
            Mock.Of<IGameInstanceRepository>(),
            Mock.Of<IGameNoticeRepository>(),
            gameRepository.Object,
            context,
            Mock.Of<IBlobRepository>(),
            null!,
            null!,
            Mock.Of<IDivisionRepository>(),
            Mock.Of<IStringLocalizer<Program>>(),
            queue,
            new ImageRemoteAccessService(context, new EphemeralDataProtectionProvider()),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IExerciseManagementService>())
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

        return (controller, context);
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

}
