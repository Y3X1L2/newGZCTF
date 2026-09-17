using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabRuntimeUpdateDispatchTests
{
    [Fact]
    public async Task DeploymentExecution_ForwardsUpdateTicketToTeamLabRuntime()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var runtimes = new Mock<ITeamLabRuntimeApplicationService>(MockBehavior.Strict);
        var ticket = new DeploymentQueueTicket
        {
            Id = Guid.NewGuid(),
            Kind = DeploymentQueueKind.TeamLabRuntime,
            Operation = RuntimeOperationKind.Update,
            TeamLabRuntimeId = 42,
            Generation = 3,
            ProtectedPayload = "protected-update"
        };
        runtimes.Setup(item => item.ExecuteQueuedUpdateAsync(
                42, ticket.Id, ticket.ProtectedPayload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TeamLabNodeResult.Ok("updated"));
        var execution = new DeploymentExecutionService(
            context,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            new DeploymentExecutionContextAccessor(),
            null!,
            runtimes.Object,
            null!,
            NullLogger<DeploymentExecutionService>.Instance);

        var result = await execution.ExecuteAsync(ticket, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        runtimes.VerifyAll();
    }
}
